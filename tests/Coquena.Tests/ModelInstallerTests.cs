using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Models;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class ModelInstallerTests
{
    private const string Commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private static int checks;
    private static void Check(bool value, string why)
    { Interlocked.Increment(ref checks); if (!value) throw new Exception($"Model install: {why} ({checks})"); }
    private static ContentIdentity Digest(byte[] bytes) => new("sha256", "file-bytes", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    private static byte[] Gguf(int? index = null, int total = 2)
    {
        using var memory = new MemoryStream(); using var writer = new BinaryWriter(memory, Encoding.UTF8, true);
        writer.Write("GGUF"u8); writer.Write(3u); writer.Write(0ul); writer.Write(index is null ? 0ul : 3ul);
        if (index is not null)
        {
            Text("split.no"); writer.Write(2u); writer.Write((ushort)index.Value);
            Text("split.count"); writer.Write(2u); writer.Write((ushort)total);
            Text("split.tensors.count"); writer.Write(5u); writer.Write(0);
        }
        writer.Write(new byte[1000]); return memory.ToArray();
        void Text(string text) { var bytes = Encoding.UTF8.GetBytes(text); writer.Write((ulong)bytes.Length); writer.Write(bytes); }
    }
    private static SelectedModel Selected(byte[][] bytes, string repo = "owner/repo", string quant = "Q4_K_M") =>
        new($"{repo}:{quant}", null, new(repo, "main", Commit, bytes.Select((b, i) => new RemoteFile(
            bytes.Length == 1 ? $"sub/Model-{quant}.gguf" : $"sub/Model-{quant}-{i + 1:00000}-of-{bytes.Length:00000}.gguf", b.Length, Digest(b))).ToArray()));
    private sealed class Bytes(byte[][] content) : IDownloadTransport
    {
        public List<(int File, long Offset, string Commit)> Calls { get; } = [];
        public CancellationTokenSource? Pause { get; set; }
        public int PauseFile { get; set; } = -1;
        public Task<HttpResponseMessage> OpenAsync(OperationDocument operation, int file, long offset, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            Calls.Add((file, offset, operation.ModelSelection!.Commit));
            Check(operation.ModelSelection.Commit == Commit, "transport never changes commit");
            var payload = content[file][(int)offset..];
            HttpContent body = Pause is not null && file == PauseFile ? new StreamContent(new Pausing(payload, Pause)) : new ByteArrayContent(payload);
            var response = new HttpResponseMessage(offset == 0 ? HttpStatusCode.OK : HttpStatusCode.PartialContent) { Content = body };
            response.Content.Headers.ContentLength = payload.Length;
            response.Headers.ETag = new EntityTagHeaderValue("\"stable\"");
            if (offset > 0) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, content[file].Length - 1, content[file].Length);
            return Task.FromResult(response);
        }
    }
    private sealed class Pausing(byte[] bytes, CancellationTokenSource stop) : MemoryStream(bytes)
    {
        private int calls;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellation = default)
        {
            if (calls++ == 0) return base.ReadAsync(buffer[..Math.Min(97, buffer.Length)], cancellation);
            stop.Cancel(); return ValueTask.FromCanceled<int>(stop.Token);
        }
    }
    private sealed class NoNetwork : IDownloadTransport
    {
        public Task<HttpResponseMessage> OpenAsync(OperationDocument o, int i, long offset, CancellationToken token)
            => throw new Exception("Unexpected network during recovered publication");
    }
    private static Task<OperationDocument?> Run(ModelInstaller installer, OperationDocument operation, IDownloadTransport transport,
        CancellationToken cancellation = default) => installer.Downloads.RunAsync(operation.Id, transport, cancellation: cancellation,
            finalize: installer.FinalizeAsync, beforeCancel: installer.CancelAsync);
    private static void Fails(Action action, string code)
    {
        try { action(); throw new Exception("Expected " + code); }
        catch (DownloadException e) { Check(e.Code == code, $"{code}, got {e.Code}"); }
        catch (StorageException e) { Check(e.Code == code, $"{code}, got {e.Code}"); }
        catch (ModelSourceException e) { Check(e.Code == code, $"{code}, got {e.Code}"); }
        catch (ContractException e) { Check(e.Code == code, $"{code}, got {e.Code}"); }
    }

    public static async Task Run(string? executable)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-models-" + Guid.NewGuid());
        var home = new Home(root); var installer = new ModelInstaller(home);
        try
        {
            byte[][] shards = [Gguf(0), Gguf(1)];
            var selected = Selected(shards); var op = installer.Create(selected);
            Check(op.ModelInstall!.ModelId != Guid.Empty && op.Id != op.ModelInstall.ModelId, "separate operation/model GUIDs");
            Check(installer.List().Length == 0 && op.ModelSelection == selected.Selection, "pinned operation is not installation");
            Check(op.Files[0].DestinationPath == "models/owner/repo/sub/Model-Q4_K_M-00001-of-00002.gguf", "no quantization folder");
            var bytes = new Bytes(shards);
            Check(await Run(installer, op, bytes) is null, "complete sharded install");
            var model = installer.List().Single();
            Check(model.Availability == "available" && !model.Changed && model.Record.Files.Length == 2, "local availability/date");
            Check(model.Record.Name == selected.Name && model.Record.Id == op.ModelInstall.ModelId && model.Record.Parameters.Count == 0, "canonical name/id/empty configuration");
            Check(model.Record.Files.Select((f, i) => f.Integrity == Digest(shards[i])).All(x => x), "full local SHA per shard");
            Check(!Directory.Exists(home.PathFor($"downloads/{op.Id:D}")) && installer.Downloads.List().Length == 0, "success cleans full operation");
            Check(File.ReadAllText(home.PathFor("logs/transactions.jsonl")).Contains("\"result\":1"), "installed transaction");
            Check(!Directory.GetFiles(home.PathFor("models"), "*", SearchOption.AllDirectories).Any(p => Path.GetFileName(p).StartsWith("mmproj")), "no implicit companion");
            var before = File.ReadAllBytes(home.PathFor("catalogs/models.json"));
            Fails(() => installer.Create(selected), "model-collision");
            Fails(() => installer.Create(selected with { Name = "OWNER/REPO:q4_k_m", Selection = selected.Selection with { Repository = "OWNER/REPO" } }), "model-collision");
            Check(before.SequenceEqual(File.ReadAllBytes(home.PathFor("catalogs/models.json"))), "collisions preserve catalog");
            var first = home.PathFor(model.Record.Files[0].LocalPath); var second = home.PathFor(model.Record.Files[1].LocalPath);
            File.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddHours(-1));
            Check(installer.List().Single() is { Availability: "available", Changed: true }, "changed date warns without blocking/rebasing");
            Check(before.SequenceEqual(File.ReadAllBytes(home.PathFor("catalogs/models.json"))), "listing does not mutate baseline");
            File.Move(second, second + ".held"); Check(installer.List().Single().Availability == "incomplete", "missing shard unavailable");
            File.Move(second + ".held", second);
            using (var locked = new FileStream(second, FileMode.Open, FileAccess.Read, FileShare.None))
                Check(installer.List().Single().Availability == "inaccessible", "locked shard not certified");

            // Distinct quantization shares the repository folder but never another unit's paths.
            byte[][] single = [Gguf()]; var other = installer.Create(Selected(single, quant: "Q8_0"));
            await Run(installer, other, new Bytes(single));
            Check(installer.List().Length == 2 && File.Exists(first), "neighbor unit preserved");
            var foreignSelection = Selected(single, "owner/foreign"); var foreign = home.PathFor("models/owner/foreign/sub/Model-Q4_K_M.gguf");
            Directory.CreateDirectory(Path.GetDirectoryName(foreign)!); File.WriteAllText(foreign, "foreign");
            Fails(() => installer.Create(foreignSelection), "destination-collision");
            Check(File.ReadAllText(foreign) == "foreign", "foreign file untouched");
            Fails(() => new ModelInstaller(home, availableBytes: () => 1).Create(Selected(single, "owner/no-space")), "insufficient-space");
            foreach (var path in new[] { "../escape.gguf", "/absolute.gguf", "sub/../../x.gguf", "sub/CON.gguf", "sub/a:ads.gguf", "sub/x\\y.gguf", "sub/trailing./x.gguf" })
            {
                var bad = Selected(single, "owner/unsafe");
                Fails(() => installer.Create(bad with { Selection = bad.Selection with { Files = [bad.Selection.Files[0] with { Path = path }] } }), "unsafe-path");
            }
            var duplicates = Selected(shards, "owner/duplicate");
            Fails(() => installer.Create(duplicates with { Selection = duplicates.Selection with { Files = [duplicates.Selection.Files[0], duplicates.Selection.Files[0]] } }), "invalid-document");
            Fails(() => installer.Create(Selected(single, "owner/companion") with { CompanionType = "mmproj" }), "invalid-selection");

            // Comparable mismatch does not install or change a baseline. Other domains aren't compared.
            var mismatch = Selected(single, "owner/mismatch");
            mismatch = mismatch with { Selection = mismatch.Selection with { Files = [mismatch.Selection.Files[0] with { Identity = new("sha256", "file-bytes", new string('b', 64)) }] } };
            op = installer.Create(mismatch); var failed = await Run(installer, op, new Bytes(single));
            Check(failed is { State: "failed", Diagnostic: "remote-hash-mismatch" } && installer.List().Length == 2, "remote mismatch rejects installation");
            Check(failed!.Files[0].LocalIntegrity is null && failed.Files[0].Identity == mismatch.Selection.Files[0].Identity, "never rebase after mismatch");
            await installer.Downloads.ControlAsync(op.Id, true, beforeCancel: installer.CancelAsync);
            foreach (var reference in new ContentIdentity?[] { null, new("xet", "xet-file", new string('b', 64)), new("sha1", "git-blob", Commit) })
            {
                var incomparable = Selected(single, "owner/incomparable" + checks);
                incomparable = incomparable with { Selection = incomparable.Selection with { Files = [incomparable.Selection.Files[0] with { Identity = reference }] } };
                op = installer.Create(incomparable); await Run(installer, op, new Bytes(single));
                Check(installer.List().Any(m => m.Record.Id == op.ModelInstall!.ModelId && m.Record.Files[0].Integrity == Digest(single[0])), "local SHA despite absent/incomparable remote");
            }
            var invalidBytes = new[] { "not GGUF"u8.ToArray() };
            op = installer.Create(Selected(invalidBytes, "owner/invalid"));
            failed = await Run(installer, op, new Bytes(invalidBytes));
            Check(failed is { State: "failed", Diagnostic: "gguf-invalid" }, "bad GGUF not installed");
            await installer.Downloads.ControlAsync(op.Id, true, beforeCancel: installer.CancelAsync);

            var wrongSize = Selected(single, "owner/wrong-size");
            wrongSize = wrongSize with { Selection = wrongSize.Selection with { Files = [wrongSize.Selection.Files[0] with { Size = single[0].Length + 1 }] } };
            op = installer.Create(wrongSize); failed = await Run(installer, op, new Bytes(single));
            Check(failed?.Diagnostic == "size-mismatch" && !File.Exists(home.PathFor(op.Files[0].DestinationPath)), "known size mismatch never publishes");
            await installer.Downloads.ControlAsync(op.Id, true, beforeCancel: installer.CancelAsync);

            op = installer.Create(Selected(single, "owner/late-collision"));
            var late = home.PathFor(op.Files[0].DestinationPath);
            Directory.CreateDirectory(Path.GetDirectoryName(late)!); File.WriteAllText(late, "arrived later");
            failed = await Run(installer, op, new Bytes(single));
            Check(failed?.State == "failed" && File.ReadAllText(late) == "arrived later" && !installer.List().Any(m => m.Record.Id == op.ModelInstall!.ModelId), "publication rechecks late collision");
            await installer.Downloads.ControlAsync(op.Id, true, beforeCancel: installer.CancelAsync);
            Check(File.ReadAllText(late) == "arrived later", "cancelling late collision preserves foreign file");
            byte[][] inconsistent = [Gguf(0), Gguf(0)]; op = installer.Create(Selected(inconsistent, "owner/bad-shards"));
            failed = await Run(installer, op, new Bytes(inconsistent)); Check(failed?.Diagnostic == "gguf-invalid", "contradictory shard indices rejected");
            await installer.Downloads.ControlAsync(op.Id, true, beforeCancel: installer.CancelAsync);

            // Ctrl+C-equivalent cancellation: keep completed first shard and hash the resumed prefix.
            using var stopped = new CancellationTokenSource();
            op = installer.Create(Selected(shards, "owner/resume")); bytes = new Bytes(shards) { Pause = stopped, PauseFile = 1 };
            var paused = await Run(installer, op, bytes, stopped.Token);
            Check(paused is { State: "paused" } && paused.Files[0].State == "transferred" && paused.Files[1].ConfirmedBytes == 97, "checkpoint and full first shard preserved");
            bytes = new Bytes(shards); await Run(installer, paused!, bytes);
            Check(bytes.Calls is [{ File: 1, Offset: 97, Commit: Commit }], "resume skips verified first shard, same commit and offset");
            Check(installer.List().Single(m => m.Record.Name == "owner/resume:Q4_K_M").Record.Files[1].Integrity == Digest(shards[1]), "resumed hash includes complete prefix");
            using var stopTamper = new CancellationTokenSource();
            op = installer.Create(Selected(shards, "owner/tamper"));
            paused = await Run(installer, op, new Bytes(shards) { Pause = stopTamper, PauseFile = 1 }, stopTamper.Token);
            var completePartial = home.PathFor(paused!.Files[0].PartialPath); var altered = File.ReadAllBytes(completePartial); altered[^1] = 9; File.WriteAllBytes(completePartial, altered);
            failed = await Run(installer, paused, new NoNetwork());
            Check(failed?.Diagnostic == "partial-integrity-failed", "completed partial checked before reuse, no traffic");
            await installer.Downloads.ControlAsync(op.Id, true, beforeCancel: installer.CancelAsync);

            // Reserved names/paths serialize acquisition; unrelated records merge, not lost.
            op = installer.Create(Selected(single, "owner/reserved"));
            Fails(() => installer.Create(Selected(single, "owner/reserved")), "model-reserved");
            await installer.Downloads.ControlAsync(op.Id, true, beforeCancel: installer.CancelAsync);
            Check(!Directory.Exists(home.PathFor($"downloads/{op.Id:D}")), "cancel removes own preparation only");

            var reservation = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() =>
            {
                try { return installer.Create(Selected(single, "owner/concurrent")); }
                catch (DownloadException e) when (e.Code == "model-reserved") { return null; }
            })));
            Check(reservation.Count(o => o is not null) == 1, "simultaneous same-name acquisition has one persisted reservation");
            await installer.Downloads.ControlAsync(reservation.Single(o => o is not null)!.Id, true, beforeCancel: installer.CancelAsync);
            var simultaneous = new[] { installer.Create(Selected(single, "owner/parallel-a")), installer.Create(Selected(single, "owner/parallel-b")) };
            await Task.WhenAll(simultaneous.Select(o => Run(installer, o, new Bytes(single))));
            Check(simultaneous.All(o => installer.List().Any(m => m.Record.Id == o.ModelInstall!.ModelId)), "unrelated concurrent publications retain both catalog records");

            foreach (var stage in new[] { ModelInstallStage.AfterMove, ModelInstallStage.AfterIndex })
            {
                var crashing = new ModelInstaller(home, fault: s => { if (s == stage) throw new IOException("simulated crash"); });
                op = crashing.Create(Selected(shards, "owner/crash" + stage));
                failed = await Run(crashing, op, new Bytes(shards));
                Check(failed is { State: "failed", Phase: "publishing" }, "recoverable " + stage);
                Check(installer.List().Any(m => m.Record.Id == op.ModelInstall!.ModelId) == (stage == ModelInstallStage.AfterIndex), "existence isn't index activation");
                if (stage == ModelInstallStage.AfterMove && executable is not null)
                {
                    var result = await Native(executable, root, "download", "resume", op.Id.ToString("D"));
                    Check(result.Code == 0 && result.Output.Contains("y limpieza completadas"), "published CLI recovers fixed model without network: " + result.Code + " " + result.Output);
                }
                else Check(await Run(installer, failed!, new NoNetwork()) is null, "recovered publication no HTTP");
                Check(installer.List().Single(m => m.Record.Id == op.ModelInstall!.ModelId).Availability == "available" && !Directory.Exists(home.PathFor($"downloads/{op.Id:D}")), "recovery all shards + catalog + cleanup");
            }

            var moveFailure = new ModelInstaller(home, fault: s => { if (s == ModelInstallStage.AfterMove) throw new IOException("move crash"); });
            op = moveFailure.Create(Selected(shards, "owner/cancel-move"));
            failed = await Run(moveFailure, op, new Bytes(shards));
            await installer.Downloads.ControlAsync(op.Id, true, beforeCancel: installer.CancelAsync);
            Check(!Directory.Exists(home.PathFor($"downloads/{op.Id:D}")) && op.Files.All(f => !File.Exists(home.PathFor(f.DestinationPath))), "cancel interrupted unindexed movement restores own files before cleanup");
            Check(!installer.List().Any(m => m.Record.Id == op.ModelInstall!.ModelId) && File.Exists(first), "cancel never activates incomplete unit nor affects neighbors");

            // Files are checked again on recovered publication, never activated by existence.
            op = moveFailure.Create(Selected(shards, "owner/corrupt-move"));
            failed = await Run(moveFailure, op, new Bytes(shards));
            var moved = home.PathFor(op.Files[0].DestinationPath); var originalBytes = File.ReadAllBytes(moved);
            var broken = (byte[])originalBytes.Clone(); broken[^1] = 7; File.WriteAllBytes(moved, broken);
            failed = await Run(installer, failed!, new NoNetwork());
            Check(failed?.Diagnostic == "publication-integrity-failed" && !installer.List().Any(m => m.Record.Id == op.ModelInstall!.ModelId), "moved corrupt shard cannot activate index");
            try { await installer.Downloads.ControlAsync(op.Id, true, beforeCancel: installer.CancelAsync); throw new Exception("Unsafe cancel"); }
            catch (DownloadException e) { Check(e.Code == "publication-integrity-failed" && File.Exists(moved), "cancel refuses to delete changed destination"); }
            File.WriteAllBytes(moved, originalBytes);
            await installer.Downloads.ControlAsync(op.Id, true, beforeCancel: installer.CancelAsync);
            Check(!File.Exists(moved), "repeat cancel reconciles retained rollback safely");

            var dirty = new ModelInstaller(home, fault: s => { if (s == ModelInstallStage.BeforeCleanup) throw new IOException("cleanup failure"); });
            op = dirty.Create(Selected(single, "owner/cleanup"));
            try { await Run(dirty, op, new Bytes(single)); throw new Exception("Expected cleanup failure"); }
            catch (DownloadException e) { Check(e.Code == "cleanup-pending", "cleanup failure separate from valid installation"); }
            Check(installer.Downloads.Status(op.Id) is { State: "completed", Phase: "cleanup" } && installer.List().Any(m => m.Record.Id == op.ModelInstall!.ModelId), "index retained after cleanup failure");
            await installer.Downloads.ControlAsync(op.Id, true, beforeCancel: installer.CancelAsync);
            Check(installer.List().Any(m => m.Record.Id == op.ModelInstall!.ModelId) && !Directory.Exists(home.PathFor($"downloads/{op.Id:D}")), "cancel post-index never reverts model");
            if (executable is not null)
            {
                var result = await Native(executable, root, "model", "list");
                Check(result.Code == 0 && result.Output.Contains(selected.Name) && result.Output.Contains("available"), "published CLI local list");
                result = await Native(executable, root, "model", "companion", "download", "owner/repo#mmproj:F16");
                Check(result.Code == 2, "unimplemented companion not advertised");
            }
            using var output = new StringWriter(); using var error = new StringWriter();
            Check(ModelCli.Run(["list"], output, error, installer) == 0 && output.ToString().Contains(selected.Name) && error.ToString() == "", "model list adapter no provider");
            foreach (var args in new[] { Array.Empty<string>(), new[] { "verify", "x" }, new[] { "download" }, new[] { "list", "x" }, new[] { "download", "x", "--json" } })
                Check(ModelCli.Run(args, output, error, installer) == 2, "invalid syntax no work");
            await MenuTests(home, installer, single);
            await TransportTests(op);
            Console.WriteLine($"OK: {checks} comprobaciones de descarga/publicación de modelos; fixtures, recuperación y CLI{(executable is null ? "." : " Native AOT.")}");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
            => Task.FromResult(response(request));
    }
    private sealed class Menu(params int?[] choices) : IModelSelectionInteraction
    {
        private int index;
        public bool IsInteractive => true;
        public List<string[]> Options { get; } = [];
        public void Show(string text) { }
        public Task<int?> ChooseAsync(IReadOnlyList<string> options, CancellationToken cancellation)
        { Options.Add(options.ToArray()); return Task.FromResult(choices[index++]); }
    }
    private static Task MenuTests(Home home, ModelInstaller installer, byte[][] payload)
    {
        int metadata = 0;
        using var client = new HttpClient(new Handler(request =>
        {
            metadata++;
            var path = request.RequestUri!.AbsolutePath;
            var text = path.EndsWith("/revision/main") ? $$"""{"id":"owner/menu","sha":"{{Commit}}"}""" :
                path.Contains("/tree/") ? $$$"""[{"type":"file","path":"sub/Model-Q4_K_M.gguf","size":{{{payload[0].Length}}},"oid":"{{{Commit}}}","lfs":{"oid":"{{{Digest(payload[0]).Value}}}","size":{{{payload[0].Length}}}}},{"type":"file","path":"mmproj-F16.gguf","size":11,"oid":"{{{Commit}}}"}]""" : "README fixture";
            return new(HttpStatusCode.OK) { Content = new StringContent(text) };
        }));
        var source = new HuggingFaceStore(client); var menu = new Menu(1, 2);
        using var output = new StringWriter(); using var error = new StringWriter();
        Check(ModelCli.Run(["download", "owner/menu:Q4_K_M"], output, error, installer, source,
            new NoNetwork(), menu) == 0 && installer.Downloads.List().Length == 0, "README then cancel creates no GUID or work");
        Check(menu.Options.Count == 2 && menu.Options.All(o => o.Length == 3 && o[0].StartsWith("Descargar")), "one repeated menu, no companion");
        var bytes = new Bytes(payload); menu = new Menu(0);
        Check(ModelCli.Run(["download", "owner/menu"], output, error, installer, source, bytes, menu) == 0 &&
            bytes.Calls.Count == 1 && menu.Options.Count == 1 && installer.List().Any(m => m.Record.Name == "owner/menu:Q4_K_M"), "menu choice starts download without second confirmation");
        var count = metadata;
        Check(ModelCli.Run(["download", "owner/menu#mmproj:F16"], output, error, installer, source, bytes, menu) == 1 && metadata == count, "companion rejected before metadata");
        Check(error.ToString().Contains("outside-m1"), "explicit M1 diagnostic");
        return Task.CompletedTask;
    }
    private static async Task TransportTests(OperationDocument operation)
    {
        int calls = 0;
        using var transport = new HuggingFaceTransport(new Handler(request =>
        {
            Check(request.Headers.Range?.Ranges.Single().From == 13 && request.Headers.IfRange?.EntityTag?.Tag == "\"stable\"", "Range/IfRange retained across CDN");
            Check(!request.Headers.Contains("Authorization") && !request.Headers.Contains("Cookie"), "no credential forwarding");
            if (calls++ == 0)
            {
                Check(request.RequestUri!.AbsolutePath.Contains("/resolve/" + Commit + "/"), "stable initial HF address");
                var response = new HttpResponseMessage(HttpStatusCode.Redirect);
                response.Headers.Location = new Uri("https://cas-bridge.xethub.hf.co/signed?token=not-persisted"); return response;
            }
            return new(HttpStatusCode.PartialContent) { Content = new ByteArrayContent([1]) };
        }));
        var pinned = operation with { Files = operation.Files.Select(f => f with { ETag = "\"stable\"" }).ToArray() };
        using var result = await transport.OpenAsync(pinned, 0, 13, default);
        Check(result.StatusCode == HttpStatusCode.PartialContent && calls == 2, "bounded explicit CDN redirect");
        foreach (var location in new[] { "http://hf.co/file", "https://evil.test/file", "https://hf.co.evil.test/file", "https://user:pass@hf.co/file", "https://hf.co:444/file" })
        {
            using var bad = new HuggingFaceTransport(new Handler(_ =>
            { var r = new HttpResponseMessage(HttpStatusCode.Redirect); r.Headers.Location = new(location); return r; }));
            try { using var r = await bad.OpenAsync(pinned, 0, 0, default); throw new Exception("unsafe redirect accepted"); }
            catch (DownloadException e) { Check(e.Code == "unsafe-redirect", "unsafe CDN redirect rejected"); }
        }
        using var loop = new HuggingFaceTransport(new Handler(_ =>
        { var r = new HttpResponseMessage(HttpStatusCode.Redirect); r.Headers.Location = new("https://hf.co/file"); return r; }));
        try { using var r = await loop.OpenAsync(pinned, 0, 0, default); throw new Exception("loop accepted"); }
        catch (DownloadException e) { Check(e.Code == "redirect-limit", "redirect budget"); }
        using var changed = new HuggingFaceTransport(new Handler(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) };
            r.Headers.TryAddWithoutValidation("X-Repo-Commit", new string('b', 40)); return r;
        }));
        try { using var r = await changed.OpenAsync(pinned, 0, 0, default); throw new Exception("changed commit accepted"); }
        catch (DownloadException e) { Check(e.Code == "content-changed", "response commit checked when present"); }
        using var limited = new HuggingFaceTransport(new Handler(_ =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            r.Headers.TryAddWithoutValidation("RateLimit", "\"resolvers\";r=0;t=60");
            r.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(1)); return r;
        }));
        using var rate = await limited.OpenAsync(pinned, 0, 0, default);
        Check(rate.Headers.RetryAfter?.Delta == TimeSpan.FromSeconds(60), "RateLimit reset cannot be shortened by Retry-After");
    }
    private static async Task<(int Code, string Output)> Native(string executable, string home, params string[] args)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["COQUENA_HOME"] = home;
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        finally { if (!process.HasExited) { process.Kill(true); await process.WaitForExitAsync(); } }
        return (process.ExitCode, await stdout + await stderr);
    }
}
