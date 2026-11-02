using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Coquena.Backends;
using Coquena.Configuration;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class BackendUpgradeTests
{
    private sealed class Transport(byte[] bytes, Action? before = null) : IDownloadTransport
    {
        public int Calls;
        public long LastOffset;
        public Task<HttpResponseMessage> OpenAsync(OperationDocument operation, int file, long offset, CancellationToken cancellation)
        {
            Calls++; LastOffset = offset; before?.Invoke(); cancellation.ThrowIfCancellationRequested();
            var response = new HttpResponseMessage(offset == 0 ? HttpStatusCode.OK : HttpStatusCode.PartialContent)
                { Content = new ByteArrayContent(bytes[(int)offset..]) };
            response.Headers.ETag = new EntityTagHeaderValue("\"fixture\"");
            if (offset > 0) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, bytes.Length - 1, bytes.Length);
            return Task.FromResult(response);
        }
    }
    private static byte[] Zip(string build)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            using var file = zip.CreateEntry("llama-server.exe").Open();
            file.Write(Encoding.UTF8.GetBytes($"version: {build[1..]} (abcdef)"));
        }
        return buffer.ToArray();
    }
    public static async Task Run(string? executable)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-upgrade-" + Guid.NewGuid());
        var home = new Home(root); var store = new LocalStore(home); var json = ContractJsonContext.Default;
        var manager = new ConfigurationManager(home); var checks = 0;
        void Check(bool value, string reason) { checks++; if (!value) throw new Exception("Upgrade: " + reason); }
        BackendRecord Record(string build, string variant = "cpu", bool latest = false, bool pin = false, string? location = null)
        {
            var id = Guid.NewGuid(); var op = Guid.NewGuid();
            var record = new BackendRecord(id, build, $"version: {build[1..]} (abcdef)", "ggml", "ggml-org/llama.cpp", variant,
                variant.Split('@')[0], "win-x64", (location is null ? "backends/" : "") + $"{id:D}/{op:D}/llama-server.exe", latest, pin, location);
            var slot = new BackendPaths(home).Slot(record); Directory.CreateDirectory(slot);
            File.WriteAllText(Path.Combine(slot, ".coquena-owner"), op.ToString("D"));
            File.WriteAllText(new BackendPaths(home).Server(record), record.Version);
            return record;
        }
        void Save(params BackendRecord[] records) => store.Update(DocumentKind.Backends, json.BackendsDocument, ContractValidation.Backends, _ => new(1, records));
        BackendRecord[] Read() => store.Read(DocumentKind.Backends, json.BackendsDocument, ContractValidation.Backends)!.Backends;
        void Update(BackendRecord baseline, string candidate, byte[]? bytes = null, string state = "available") =>
            store.Update(DocumentKind.BackendUpdates, json.BackendUpdatesDocument, ContractValidation.BackendUpdates,
                old => new(1, (old?.Updates ?? []).Where(o => o.Variant != baseline.Variant).Append(new("ggml", baseline.Variant, baseline.Platform,
                    baseline.Id, baseline.Build, DateTimeOffset.UtcNow, state, "fixture", state == "available" ? new(candidate,
                        [new($"llama-{candidate}-bin-win-{baseline.Variant}-x64.zip", (bytes ?? Zip(candidate)).Length)], "unknown", "fixture") : null)).ToArray()));
        Task<string> Probe(string path, CancellationToken token) { token.ThrowIfCancellationRequested(); return File.ReadAllTextAsync(path, token); }
        var installer = new BackendInstaller(home, store, Probe);
        Task<BackendUpgradeResult[]> Upgrade(Func<OperationDocument, CancellationToken, Task<bool>>? run = null, string? selector = "cpu:latest", Action<string>? report = null) =>
            new BackendUpgrader(home, store, installer).UpgradeAsync(selector, run ?? ((_, _) => throw new Exception("Unexpected download")), report);
        async Task<bool> Install(OperationDocument op, CancellationToken token)
        {
            var result = await installer.Downloads.RunAsync(op.Id, new Transport(Zip(op.BackendSelection!.Release)), cancellation: token,
                finalize: installer.FinalizeAsync, beforeCancel: installer.CancelAsync);
            return result is null && installer.Published(op);
        }
        try
        {
            home.Ensure();
            using var output = new StringWriter(); using var error = new StringWriter();
            Check(ConfigurationCli.Run("config", ["backend-retention", "show"], output, error, manager) == 2, "automatic retention policy retired");
            manager.SetBackendRetention(null);
            var baseline = Record("b1", latest: true, pin: true); var target = Record("b2", pin: true);
            Save(baseline, target); Update(baseline, "b2");
            store.Update(DocumentKind.Config, json.ConfigDocument, ContractValidation.Config,
                old => old! with { DefaultBackend = new("fixed", baseline.Id) });
            var targetBytes = File.ReadAllBytes(new BackendPaths(home).Server(target));
            var result = await Upgrade();
            Check(result.Single().State == "reused" && Read().Single(b => b.Id == target.Id).TracksLatest &&
                Read().Single(b => b.Id == target.Id).Pinned && Read().Single(b => b.Id == baseline.Id).Pinned, "reuse preserves both pins and IDs");
            Check(File.ReadAllBytes(new BackendPaths(home).Server(target)).SequenceEqual(targetBytes) && !new DownloadManager(home).List().Any(), "reuse no transfer or file replacement");
            Check(installer.Use(null)?.BackendId == baseline.Id, "fixed default unchanged");
            // Validation failure never falls back to a destructive reinstall.
            var badBaseline = Record("b7", latest: true); var badTarget = Record("b8", pin: true);
            Save(badBaseline, badTarget); Update(badBaseline, "b8");
            File.WriteAllText(new BackendPaths(home).Server(badTarget), "version: 99 (abcdef)");
            Check((await Upgrade()).Single().State == "failed" && Read().Single(b => b.TracksLatest).Id == badBaseline.Id &&
                !new DownloadManager(home).List().Any(), "invalid installed target does not advance or reinstall");
            Save(baseline with { TracksLatest = false }, target with { TracksLatest = true });
            Check((await Upgrade()).Single().Diagnostic == "update-required", "stale observation rejected");
            Update(Read().Single(b => b.Id == target.Id), "b2", state: "unchanged");
            Check((await Upgrade()).Single().State == "unchanged", "no change has no transfer or cleanup");
            Update(Read().Single(b => b.Id == target.Id), "b3");
            result = await Upgrade(Install);
            Check(result.Single().State == "installed" && Read().Single(b => b.TracksLatest).Build == "b3", "new build installed with fixed plan");
            Check(Read().Any(b => b.Id == target.Id) && Read().Any(b => b.Id == baseline.Id), "pins retained after install");
            var latest = Read().Single(b => b.TracksLatest);
            Update(latest, "b4");
            OperationDocument? paused = null;
            result = await Upgrade(async (op, _) =>
            {
                using var stop = new CancellationTokenSource(); stop.Cancel();
                paused = await installer.Downloads.RunAsync(op.Id, new Transport(Zip("b4")), cancellation: stop.Token, finalize: installer.FinalizeAsync);
                return false;
            });
            Check(result.Single().State == "pending" && paused?.State == "paused" && Read().Single(b => b.TracksLatest) == latest, "paused upgrade preserves current");
            store.Update(DocumentKind.Operation, json.OperationDocument, ContractValidation.Operation,
                old => old! with { State = "downloading" }, operation: paused!.Id);
            Check((await Upgrade()).Single().Diagnostic == "upgrade-pending", "duplicate pending upgrade rejected");
            Check(installer.Downloads.Status(paused.Id).State == "paused", "interrupted executor remains recoverable after snapshot inspection");
            var partialBytes = Zip("b4"); var partialOffset = partialBytes.Length / 2;
            File.WriteAllBytes(home.PathFor(paused.Files[0].PartialPath), partialBytes[..partialOffset]);
            store.Update(DocumentKind.Operation, json.OperationDocument, ContractValidation.Operation,
                old => old! with { Files = old.Files.Select(f => f with { ConfirmedBytes = partialOffset, ETag = "\"fixture\"", State = "paused" }).ToArray() }, operation: paused.Id);
            var partialTransport = new Transport(partialBytes);
            Check(await installer.Downloads.RunAsync(paused.Id, partialTransport, finalize: installer.FinalizeAsync) is null &&
                partialTransport.LastOffset == partialOffset && Read().Single(b => b.TracksLatest).Build == "b4", "resume pinned candidate by byte range");
            latest = Read().Single(b => b.TracksLatest); Update(latest, "b5");
            OperationDocument? failed = null;
            await Upgrade(async (op, token) =>
            {
                failed = await installer.Downloads.RunAsync(op.Id, new Transport("bad zip"u8.ToArray()), cancellation: token, finalize: installer.FinalizeAsync);
                return false;
            });
            Check(failed?.State == "failed" && Read().Single(b => b.TracksLatest) == latest && File.Exists(new BackendPaths(home).Server(latest)), "failed transfer/validation preserves installation");
            await installer.Downloads.ControlAsync(failed!.Id, true, beforeCancel: installer.CancelAsync);
            Check(!Directory.Exists(home.PathFor($"downloads/{failed.Id:D}")) && Read().Single(b => b.TracksLatest) == latest, "cancel removes only new operation");
            // Concurrent update during download invalidates publication without changing current.
            Update(latest, "b6"); OperationDocument? stale = null;
            await Upgrade(async (op, token) =>
            {
                stale = await installer.Downloads.RunAsync(op.Id, new Transport(Zip("b6"), () => Update(latest, "b7")), cancellation: token, finalize: installer.FinalizeAsync);
                return false;
            });
            Check(stale?.Diagnostic == "stale-upgrade-plan" && Read().Single(b => b.TracksLatest) == latest, "changed observation prevents obsolete publication");
            await installer.Downloads.ControlAsync(stale!.Id, true, beforeCancel: installer.CancelAsync);
            // Interrupted publication resumes without transfer and preserves previous builds.
            foreach (var stage in new[] { InstallStage.AfterMove, InstallStage.AfterIndex })
            {
                latest = Read().Single(b => b.TracksLatest);
                var nextBuild = stage == InstallStage.AfterMove ? "b8" : "b9"; Update(latest, nextBuild);
                var interruptedConsumer = new BackendInstaller(home, store, Probe,
                    current => { if (current == stage) throw new IOException("fixture interruption"); });
                OperationDocument? interrupted = null;
                await new BackendUpgrader(home, store, interruptedConsumer).UpgradeAsync("cpu:latest", async (op, token) =>
                {
                    interrupted = await interruptedConsumer.Downloads.RunAsync(op.Id, new Transport(Zip(nextBuild)), cancellation: token, finalize: interruptedConsumer.FinalizeAsync);
                    return false;
                });
                Check(interrupted?.State == "failed" && interrupted.Phase == "publishing", "publication interrupted: " + stage);
                var resumeTransport = new Transport(Zip(nextBuild));
                Check(await installer.Downloads.RunAsync(interrupted!.Id, resumeTransport, finalize: installer.FinalizeAsync) is null &&
                    resumeTransport.Calls == 0 && Read().Single(b => b.TracksLatest).Build == nextBuild, "publication resume without network: " + stage);
            }
            // Old persisted retention preferences do not authorize deletion.
            var history = Enumerable.Range(11, 7).Select(n => Record("b" + n, latest: n == 17)).ToArray();
            var retainedTarget = Record("b18"); Save([.. history, retainedTarget]);
            manager.SetBackendRetention(1); Update(history[^1], "b18");
            Check((await Upgrade()).Single().State == "reused" && Read().Length == 8 &&
                history.All(b => File.Exists(new BackendPaths(home).Server(b))), "upgrade preserves unprotected history beyond old retention preference");
            // Multiple units continue independently and only selected latest filters are eligible.
            baseline = Record("b9", latest: true); target = Record("b10", pin: true); var v = Record("b9", "vulkan", latest: true);
            Save(baseline, target, v); Update(baseline, "b10"); Update(v, "b10", state: "failed"); manager.SetBackendRetention(5);
            result = await Upgrade(selector: null);
            Check(result.Length == 2 && result[0].State == "reused" && result[1].State == "failed", "global units independent");
            foreach (var selector in new[] { "cpu:b10", "external", "lab/cpu:latest", "cpu:latest:extra" })
                try { await Upgrade(selector: selector); throw new Exception("Expected selector rejection"); }
                catch (BackendException e) { Check(e.Code == "invalid-latest-selector", "invalid upgrade selector: " + selector); }
            // Native CLI: real --version probe of the fixture executable, reuse and config persistence.
            if (executable is not null)
            {
                var nativeRoot = Path.Combine(root, "native"); var nativeHome = new Home(nativeRoot); nativeHome.Ensure();
                var nativeStore = new LocalStore(nativeHome); var id = Guid.NewGuid(); var slot = Guid.NewGuid();
                var nativeTarget = target with { Id = id, Build = "b2", Version = "version: 2 (abcdef)", ServerPath = $"backends/{id:D}/{slot:D}/llama-server.exe" };
                var server = nativeHome.PathFor(nativeTarget.ServerPath); Directory.CreateDirectory(Path.GetDirectoryName(server)!);
                File.Copy(Environment.ProcessPath!, server); File.WriteAllText(Path.Combine(Path.GetDirectoryName(server)!, ".coquena-owner"), slot.ToString("D"));
                var nativeBaseline = baseline with { Build = "b1", TracksLatest = true };
                nativeStore.Update(DocumentKind.Backends, json.BackendsDocument, ContractValidation.Backends, _ => new(1, [nativeBaseline, nativeTarget]));
                nativeStore.Update(DocumentKind.BackendUpdates, json.BackendUpdatesDocument, ContractValidation.BackendUpdates, _ => new(1,
                    [new("ggml", "cpu", "win-x64", nativeBaseline.Id, "b1", DateTimeOffset.UtcNow, "available", "fixture",
                        new("b2", [new("llama-b2-bin-win-cpu-x64.zip", 123)], "unknown", "fixture"))]));
                async Task<(int Code, string Text)> Native(params string[] args)
                {
                    var info = new ProcessStartInfo(args[0] == "backend" ? Environment.ProcessPath! : executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                    if (args[0] == "backend") info.ArgumentList.Add("--m2-backend-api");
                    info.Environment.Clear(); foreach (System.Collections.DictionaryEntry pair in Environment.GetEnvironmentVariables()) info.Environment[(string)pair.Key] = (string)pair.Value!;
                    info.Environment["COQUENA_HOME"] = nativeRoot; info.Environment["COQUENA_FIXTURE_BUILD"] = "2";
                    foreach (var arg in args) info.ArgumentList.Add(arg);
                    using var process = Process.Start(info)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
                    return (process.ExitCode, await stdout + await stderr);
                }
                var nativeResult = await Native("backend", "upgrade", "cpu:latest");
                Check(nativeResult.Code == 0 && nativeResult.Text.Contains("reused"), "native CLI reuses real validated executable: " + nativeResult.Text);
                nativeResult = await Native("config", "backend-retention", "set", "5"); Check(nativeResult.Code == 2, "native retired retention set rejected");
                nativeResult = await Native("config", "backend-retention", "show"); Check(nativeResult.Code == 2, "native retired retention show rejected");
                Check((await Native("config", "backend-retention", "set", "0")).Code == 2, "native invalid policy");
                Check((await Native("backend", "upgrade", "cpu:b2")).Code == 2, "native fixed selector rejected");
                Check((await Native("--help")).Text.Contains("backend download"), "native M3 help");
            }
            Console.WriteLine($"OK: {checks} comprobaciones de upgrade/retención" + (executable is null ? "." : " y CLI Native AOT."));
        }
        finally { Directory.Delete(root, true); }
    }
}
