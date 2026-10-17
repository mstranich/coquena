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
                result = await Native(executable, root, "model", "companion", "download", "owner/repo");
                Check(result.Code == 1 && result.Output.Contains("invalid-selector"), "native companion requires explicit type before network");
            }
            using var output = new StringWriter(); using var error = new StringWriter();
            Check(ModelCli.Run(["list"], output, error, installer) == 0 && output.ToString().Contains(selected.Name) && error.ToString() == "", "model list adapter no provider");
            foreach (var args in new[] { Array.Empty<string>(), new[] { "verify" }, new[] { "download" }, new[] { "list", "x" }, new[] { "download", "x", "--json" } })
                Check(ModelCli.Run(args, output, error, installer) == 2, "invalid syntax no work");
            await MenuTests(home, installer, single);
            await PublicationContentionTests();
            await PublicationIdentityTests();
            await CompanionTests(executable);
            await TransitionTests(executable);
            await TransportTests(op);
            Console.WriteLine($"OK: {checks} comprobaciones de descarga/publicación de modelos; fixtures, recuperación y CLI{(executable is null ? "." : " Native AOT.")}");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
    }

    private static async Task PublicationContentionTests()
    {
        foreach (var companion in new[] { false, true })
        {
            var root = Path.Combine(Path.GetTempPath(), "coquena-publication-contention-" + Guid.NewGuid());
            var home = new Home(root);
            var installer = new ModelInstaller(home);
            try
            {
                byte[][] payload = [Gguf()];
                var selected = Selected(payload, "owner/publication");
                if (companion) selected = selected with { Name = "owner/publication#mmproj:Q4_K_M", CompanionType = "mmproj" };
                var operation = installer.Create(selected);
                var peer = installer.Create(Selected(payload, "owner/peer"));
                var interrupted = new ModelInstaller(home, fault: stage =>
                {
                    if (stage != ModelInstallStage.AfterPreparation) return;
                    // A second GUID completes its checkpoint and publication while the
                    // first owns a verified destination-volume preparation. This would
                    // fail writer-busy if preparation retained the home writer lease.
                    Check(Run(installer, peer, new Bytes(payload)).GetAwaiter().GetResult() is null,
                        "independent operation checkpoints and publishes during preparation");
                    throw new IOException("fixture interrupted before publication intent");
                });
                var failed = await Run(interrupted, operation, new Bytes(payload));
                Check(failed is { State: "failed", Phase: "resource-validation" }, "preparation interruption preserves transfer checkpoint");
                Check(installer.List().All(m => m.Record.Id != operation.ModelInstall!.ModelId), "prepared bytes never activate the record");
                // Simulate a process crash leaving an incomplete deterministic temporary.
                var target = home.PathFor(operation.Files[0].DestinationPath);
                var temporary = target + $".coquena-{operation.Id:D}.tmp";
                File.WriteAllText(temporary, "interrupted-copy");
                Check(await Run(installer, operation, new NoNetwork()) is null, "resume rebuilds orphan copy without redownloading");
                Check(!File.Exists(temporary) && installer.List().Count(m => m.Availability == "available") == 2,
                    "coherent publication and temporary cleanup for model/companion");
                var cancelSelection = Selected(payload, "owner/cancel-preparation");
                if (companion) cancelSelection = cancelSelection with { Name = "owner/cancel-preparation#mmproj:Q4_K_M", CompanionType = "mmproj" };
                var cancelled = installer.Create(cancelSelection);
                var cancelTarget = home.PathFor(cancelled.Files[0].DestinationPath);
                Directory.CreateDirectory(Path.GetDirectoryName(cancelTarget)!);
                var abandoned = cancelTarget + $".coquena-{cancelled.Id:D}.tmp";
                File.WriteAllText(abandoned, "crash before publication intent");
                await installer.Downloads.ControlAsync(cancelled.Id, true, beforeCancel: installer.CancelAsync);
                Check(!File.Exists(abandoned) && !File.Exists(cancelTarget), "cancel removes orphan preparation without activating resource");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }

    private static async Task PublicationIdentityTests()
    {
        foreach (var companion in new[] { false, true })
        foreach (var mode in new[] { "temporary", "same-bytes", "existing", "after-move", "renamed-path" })
        {
            var root = Path.Combine(Path.GetTempPath(), "coquena-publication-identity-" + Guid.NewGuid());
            var home = new Home(root); var installer = new ModelInstaller(home);
            try
            {
                byte[][] payload = [Gguf()];
                var selected = Selected(payload, "owner/identity");
                if (companion) selected = selected with { Name = "owner/identity#mmproj:Q4_K_M", CompanionType = "mmproj" };
                var operation = installer.Create(selected);
                var target = home.PathFor(operation.Files[0].DestinationPath);
                if (mode == "existing")
                {
                    var interrupted = new ModelInstaller(home, fault: stage =>
                    { if (stage == ModelInstallStage.AfterMove) throw new IOException("fixture interrupted after move"); });
                    Check((await Run(interrupted, operation, new Bytes(payload)))?.Phase == "publishing", "existing-file recovery fixture");
                }
                var attempted = false;
                var probing = new ModelInstaller(home, fault: stage =>
                {
                    var probeStage = mode == "after-move" ? ModelInstallStage.AfterMove :
                        mode == "renamed-path" ? ModelInstallStage.AfterPublicationRename : ModelInstallStage.AfterPreparation;
                    if (stage != probeStage) return;
                    attempted = true;
                    var path = mode is "existing" or "after-move" or "renamed-path" ? target : target + $".coquena-{operation.Id:D}.tmp";
                    var blocked = false;
                    try { File.Move(path, path + ".verified"); }
                    catch (IOException e) when ((e.HResult & 0xffff) is 32 or 33) { blocked = true; }
                    if (mode is "existing" or "after-move")
                    {
                        Check(blocked, "verified destination cannot be renamed before index publication");
                        return;
                    }
                    Check(!blocked, "fixture substitutes temporary while verified handle stays open");
                    var replacement = payload[0].ToArray();
                    if (mode != "same-bytes") replacement[^1] ^= 1;
                    File.WriteAllBytes(path, replacement);
                });
                var result = await Run(probing, operation, mode == "existing" ? new NoNetwork() : new Bytes(payload));
                Check(attempted, "publication substitution probe executed");
                if (mode is "temporary" or "same-bytes" or "renamed-path")
                {
                    Check(result is { State: "failed", Diagnostic: "publication-file-changed" }, "substituted temporary rejected by identity, even with identical bytes");
                    Check(installer.List().Length == 0, "substituted bytes never activate catalog");
                    if (mode != "renamed-path")
                    {
                        Check(!File.Exists(target), "substitution caught before moving replacement to destination");
                        Check(await Run(installer, operation, new NoNetwork()) is null, "rejected substitution resumes from verified partial without network");
                    }
                    else Check((await Run(installer, operation, new NoNetwork()))?.Diagnostic == "publication-integrity-failed",
                        "post-rename replacement stays unindexed and fails recovery integrity without network");
                }
                else
                {
                    Check(result is null && installer.List().Single().Availability == "available", "protected original publishes successfully");
                    Check(Digest(File.ReadAllBytes(target)) == Digest(payload[0]), "installed bytes match catalog integrity");
                }
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
    }

    private static async Task TransitionTests(string? executable)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-transition-" + Guid.NewGuid());
        var home = new Home(root); var installer = new ModelInstaller(home); var store = new LocalStore(home);
        var json = ContractJsonContext.Default;
        try
        {
            foreach (var type in new string?[] { null, "mmproj", "mtp" })
            {
                var destination = type == "mmproj" ? Path.Combine(root, "configured-models") : null;
                store.Update(DocumentKind.Config, json.ConfigDocument, ContractValidation.Config,
                    _ => new(1, null, null, new(5), ModelsDirectory: destination));
                byte[][] payload = type == "mtp" ? [Gguf(0), Gguf(1)] : [Gguf()];
                var selected = Selected(payload, "owner/transition-" + (type ?? "main"));
                if (type is not null) selected = selected with { Name = selected.Name.Replace(":", "#" + type + ":"), CompanionType = type };
                var externalPath = Path.Combine(root, "original-" + (type ?? "main"), Path.GetFileName(selected.Selection.Files[0].Path));
                Directory.CreateDirectory(Path.GetDirectoryName(externalPath)!);
                for (var i = 0; i < payload.Length; i++) File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(externalPath)!, Path.GetFileName(selected.Selection.Files[i].Path)), payload[i]);
                var previous = installer.AddExternal(externalPath, selected.Name, type is not null);
                previous = previous with { Parameters = new() { ["temperature"] = "0.4" } };
                store.Update(DocumentKind.Models, json.ModelsDocument, ContractValidation.Models,
                    d => d! with { Models = d.Models.Select(m => m.Id == previous.Id ? previous : m).ToArray() });
                var preset = new PresetRecord(Guid.NewGuid(), "reference-" + (type ?? "main"), previous.Id, []);
                store.Update(DocumentKind.Presets, json.PresetsDocument, ContractValidation.Presets,
                    d => new(1, (d?.Presets ?? []).Append(preset).ToArray()));
                Fails(() => installer.Create(selected), "model-collision");
                var instance = new InstanceDocument(1, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "fixture", Environment.ProcessId,
                    "detached", "127.0.0.1", 9931, false, [preset.Id], [previous.Id], "run/fixture/a.ini", "logs/fixture/a.log");
                store.Update(DocumentKind.Instance, json.InstanceDocument, ContractValidation.Instance, _ => instance, server: "fixture");
                Fails(() => installer.Create(selected, previous), "model-in-use");
                File.Delete(home.PathFor("run/fixture/instance.json"));
                var operation = installer.Create(selected, previous);
                Check(operation.ModelInstall!.ExternalPrevious == previous && operation.ModelInstall.ModelId == previous.Id, "transition captures stable ID and original snapshot");
                try { installer.Refresh(selected.Name, type is not null); throw new Exception("Expected pending transition"); }
                catch (ModelSourceException e) { Check(e.Code == "upgrade-pending", "metadata mutations blocked during reserved transition"); }
                Fails(() => installer.Create(selected, previous), "model-reserved");
                using var stop = new CancellationTokenSource();
                var paused = await Run(installer, operation, new Bytes(payload) { Pause = stop, PauseFile = payload.Length - 1 }, stop.Token);
                Check(paused is { State: "paused" } && installer.ExternalTransition(selected.Name)?.Id == previous.Id, "pause keeps external even with partially transferred shards");
                await installer.Downloads.ControlAsync(operation.Id, true, beforeCancel: installer.CancelAsync);
                Check(installer.ExternalTransition(selected.Name)?.Id == previous.Id && File.ReadAllBytes(externalPath).SequenceEqual(payload[0]), "cancel before publication preserves external bytes and catalog");

                operation = installer.Create(selected, previous);
                var broken = payload.Select(b => b.ToArray()).ToArray(); broken[^1][^1] ^= 1;
                var failed = await Run(installer, operation, new Bytes(broken));
                Check(failed is { State: "failed" } && installer.ExternalTransition(selected.Name)?.Id == previous.Id, "digest failure never converts external");
                await installer.Downloads.ControlAsync(operation.Id, true, beforeCancel: installer.CancelAsync);

                operation = installer.Create(selected, previous);
                var interrupted = new ModelInstaller(home, fault: s => { if (s == ModelInstallStage.AfterMove) throw new IOException("transition move interruption"); });
                failed = await Run(interrupted, operation, new Bytes(payload));
                Check(failed is { Phase: "publishing", State: "failed" } && installer.ExternalTransition(selected.Name)?.Id == previous.Id, "publication interruption keeps original record");
                await installer.Downloads.ControlAsync(operation.Id, true, beforeCancel: installer.CancelAsync);
                Check(operation.Files.All(f => !File.Exists(new ModelPaths(home).Destination(operation, f))) && File.Exists(externalPath), "cancel publication removes only prepared managed files");

                operation = installer.Create(selected, previous);
                failed = await Run(interrupted, operation, new Bytes(payload));
                if (executable is null) await Run(installer, failed!, new NoNetwork());
                else
                {
                    var result = await Native(executable, root, "download", "resume", operation.Id.ToString("D"));
                    Check(result.Code == 0, "native CLI resumes persisted transition without fetching weights: " + result.Output);
                }
                var managed = installer.List().Single(m => m.Record.Id == previous.Id).Record;
                Check(managed.Ownership == "managed" && managed.Selection?.Commit == Commit && managed.Parameters["temperature"] == "0.4" && managed.CompanionType == type, "complete transition preserves values and category");
                Check(managed.InstallationDirectory == destination && managed.Files.All(f => File.Exists(new ModelPaths(home).File(managed, f))), "transition honors pinned configured directory");
                Check(store.Read(DocumentKind.Presets, json.PresetsDocument, ContractValidation.Presets)!.Presets.Single(p => p.Id == preset.Id).ModelId == previous.Id, "preset references survive transition");
                for (var i = 0; i < payload.Length; i++) Check(File.ReadAllBytes(previous.Files[i].LocalPath).SequenceEqual(payload[i]), "every original external shard stays intact");
                Fails(() => installer.Create(selected, previous), "stale-transition-plan");
            }
            store.Update(DocumentKind.Config, json.ConfigDocument, ContractValidation.Config, _ => new(1, null, null, new(5)));

            byte[][] single = [Gguf()]; var selection = Selected(single, "owner/stale-transition");
            var missing = installer.AddExternal(Path.Combine(root, "missing.gguf"), selection.Name);
            var op = installer.Create(selection, missing);
            store.Update(DocumentKind.Models, json.ModelsDocument, ContractValidation.Models,
                d => d! with { Models = d.Models.Select(m => m.Id == missing.Id ? m with { Parameters = new() { ["temperature"] = "0.8" } } : m).ToArray() });
            var stale = await Run(installer, op, new Bytes(single));
            Check(stale?.Diagnostic == "stale-transition-plan" && installer.ExternalTransition(selection.Name)!.Parameters["temperature"] == "0.8" && !File.Exists(home.PathFor(op.Files[0].DestinationPath)), "stale snapshot rejected before publication, missing original need not be read");
            await installer.Downloads.ControlAsync(op.Id, true, beforeCancel: installer.CancelAsync);

            selection = Selected(single, "owner/overlap-transition");
            var samePath = home.PathFor("models/" + selection.Selection.Repository + "/" + selection.Selection.Files[0].Path);
            Directory.CreateDirectory(Path.GetDirectoryName(samePath)!); File.WriteAllBytes(samePath, single[0]);
            var overlap = installer.AddExternal(samePath, selection.Name);
            Fails(() => installer.Create(selection, overlap), "destination-collision");
            Check(File.ReadAllBytes(samePath).SequenceEqual(single[0]), "consent never adopts or overwrites an external path inside managed root");

            selection = Selected(single, "owner/menu-transition");
            var menuExternal = installer.AddExternal(Path.Combine(root, "menu-missing.gguf"), selection.Name);
            using var client = new HttpClient(new Handler(request => new(HttpStatusCode.OK) { Content = new StringContent(
                request.RequestUri!.AbsolutePath.EndsWith("/revision/main") ? $$"""{"id":"owner/menu-transition","sha":"{{Commit}}"}""" :
                $$$"""[{"type":"file","path":"sub/Model-Q4_K_M.gguf","size":{{{single[0].Length}}},"oid":"{{{Commit}}}","lfs":{"oid":"{{{Digest(single[0]).Value}}}","size":{{{single[0].Length}}}}}]""") }));
            using var output = new StringWriter(); using var error = new StringWriter();
            foreach (var rejected in new int?[] { 1, null })
            {
                var menu = new Menu(0, rejected);
                Check(ModelCli.Run(["download", selection.Name], output, error, installer, new(client), new NoNetwork(), menu,
                    inspector: new ResolutionTests.NamedInspector()) == 0 && installer.Downloads.List().Length == 0 &&
                    installer.ExternalTransition(selection.Name)?.Id == menuExternal.Id && menu.Options.Count == 2,
                    "CLI reject/EOF after selection keeps external and creates no GUID");
            }
            var accepted = new Menu(0, -1, 0);
            Check(ModelCli.Run(["download", selection.Name], output, error, installer, new(client), new Bytes(single), accepted,
                inspector: new ResolutionTests.NamedInspector()) == 0 && error.ToString().Length == 0 &&
                installer.List().Single(m => m.Record.Id == menuExternal.Id).Record.Ownership == "managed", "CLI repeats invalid choice and converts only after explicit acceptance");

            selection = Selected(single, "owner/index-transition");
            var original = installer.AddExternal(Path.Combine(root, "index-missing.gguf"), selection.Name);
            op = installer.Create(selection, original);
            var afterIndex = new ModelInstaller(home, fault: s => { if (s == ModelInstallStage.AfterIndex) throw new IOException("index interruption"); });
            var indexed = await Run(afterIndex, op, new Bytes(single));
            Check(indexed is { State: "failed", Phase: "publishing" } && installer.ExternalTransition(selection.Name) is null, "interrupted post-index transition is already managed");
            await installer.Downloads.ControlAsync(op.Id, true, beforeCancel: installer.CancelAsync);
            Check(installer.List().Single(m => m.Record.Id == original.Id).Record.Ownership == "managed" && installer.Downloads.List().Length == 0, "post-index cancel completes cleanup and preserves published transition");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
            => Task.FromResult(response(request));
    }
    private static async Task CompanionTests(string? executable)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-companions-" + Guid.NewGuid());
        var home = new Home(root);
        var destination = Path.Combine(root, "auxiliary");
        var storage = new LocalStore(home);
        storage.Update(DocumentKind.Config, ContractJsonContext.Default.ConfigDocument, ContractValidation.Config,
            _ => new ConfigDocument(1, null, null, new(5), ModelsDirectory: destination));
        var installer = new ModelInstaller(home);
        try
        {
            foreach (var type in new[] { "mmproj", "mtp" })
            {
                byte[][] payload = [Gguf(0), Gguf(1)];
                var selected = Selected(payload, quant: "BF16");
                selected = selected with { Name = "owner/repo#" + type + ":BF16", CompanionType = type,
                    Selection = selected.Selection with { Files = selected.Selection.Files.Select(f => f with { Path = type + "/" + f.Path }).ToArray() } };
                var operation = installer.Create(selected);
                Check(operation.ResourceKind == "companion" && operation.ModelInstall!.InstallationDirectory == destination, "companion plan pins configured directory");
                Check(installer.Downloads.List("companion").Length == 1 && installer.Downloads.List("model").Length == 0, "download filters distinguish auxiliary units");
                Fails(() => installer.Create(selected), "model-reserved");
                var principalCollision = selected with { Name = "owner/repo:BF16", CompanionType = null };
                Fails(() => installer.Create(principalCollision), "model-reserved");
                using var stopped = new CancellationTokenSource();
                var paused = await Run(installer, operation, new Bytes(payload) { Pause = stopped, PauseFile = 1 }, stopped.Token);
                Check(paused is { State: "paused" } && installer.List().All(m => m.Record.CompanionType != type), "paused auxiliary never publishes incomplete shards");
                var broken = payload.Select(b => b.ToArray()).ToArray(); broken[1][^1] ^= 1;
                var failed = await Run(installer, operation, new Bytes(broken));
                Check(failed is { State: "failed" } && installer.List().All(m => m.Record.CompanionType != type), "auxiliary hash mismatch preserves uninstalled state");
                await installer.Downloads.ControlAsync(operation.Id, true, beforeCancel: installer.CancelAsync);
                operation = installer.Create(selected);
                var interrupted = new ModelInstaller(home, fault: stage => { if (stage == ModelInstallStage.AfterMove) throw new IOException("fixture publication interruption"); });
                failed = await Run(interrupted, operation, new Bytes(payload));
                Check(failed is { State: "failed", Phase: "publishing" }, "auxiliary publication interruption remains recoverable");
                using var resumedOutput = new StringWriter(); using var resumedError = new StringWriter();
                Check(DownloadCli.Run(["resume", operation.Id.ToString("D")], resumedOutput, resumedError,
                    installer.Downloads, new NoNetwork()) == 0 && resumedError.ToString().Length == 0, "auxiliary resumes through generic download CLI without network");
                var installed = installer.List().Single(m => m.Record.CompanionType == type);
                Check(installed.Record is { Ownership: "managed" } && installed.Record.Name == selected.Name && installed.Availability == "available" &&
                    installed.Record.Files.All(f => File.Exists(Path.Combine(destination, f.LocalPath))) && installed.Record.Parameters.Count == 0, "typed managed auxiliary publishes all shards in configured directory");
                Fails(() => installer.Create(principalCollision), "model-collision");
                if (executable is not null)
                {
                    var result = await Native(executable, root, "model", "companion", "list");
                    Check(result.Code == 0 && result.Output.Contains(selected.Name), "native companion listing");
                    result = await Native(executable, root, "model", "list");
                    Check(result.Code == 0 && !result.Output.Contains(selected.Name), "native principal listing excludes auxiliary");
                }
            }
            Check(installer.List().Length == 2 && !File.Exists(home.PathFor("catalogs/presets.json")), "no principal or automatic presets");
            byte[][] single = [Gguf()];
            using var handler = new Handler(request =>
            {
                var path = request.RequestUri!.AbsolutePath;
                var json = path.EndsWith("/revision/main") ? $$"""{"id":"owner/menu-aux","sha":"{{Commit}}"}""" :
                    path.Contains("/tree/") ? $$$"""[{"type":"file","path":"mmproj-F16.gguf","size":{{{single[0].Length}}},"oid":"{{{Commit}}}","lfs":{"oid":"{{{Digest(single[0]).Value}}}","size":{{{single[0].Length}}}}},{"type":"file","path":"Model-Q4_K_M.gguf","size":9999,"oid":"{{{Commit}}}"}]""" :
                    throw new Exception("README must not be fetched automatically");
                return new(HttpStatusCode.OK) { Content = new StringContent(json) };
            });
            using var client = new HttpClient(handler);
            var menu = new Menu(0); var transfer = new Bytes(single);
            using var output = new StringWriter(); using var error = new StringWriter();
            Check(ModelCli.Run(["companion", "download", "owner/menu-aux#mmproj:F16"], output, error,
                installer, new HuggingFaceStore(client), transfer, menu, inspector: new ResolutionTests.NamedInspector()) == 0 &&
                error.ToString().Length == 0 && menu.Options.Count == 1 && transfer.Calls.Count == 1 &&
                installer.List().Any(m => m.Record.Name == "owner/menu-aux#mmproj:F16"), "companion menu starts only auxiliary download with no second confirmation");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
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
        Check(ModelCli.Run(["download", "owner/menu"], output, error, installer, source, bytes, menu,
            inspector: new ResolutionTests.NamedInspector()) == 0 &&
            bytes.Calls.Count == 1 && menu.Options.Count == 1 && installer.List().Any(m => m.Record.Name == "owner/menu:Q4_K_M"), "menu choice starts download without second confirmation");
        var count = metadata;
        Check(ModelCli.Run(["download", "owner/menu#mmproj:F16"], output, error, installer, source, bytes, menu) == 1 && metadata == count, "companion rejected before metadata");
        Check(error.ToString().Contains("invalid-selector"), "explicit selector diagnostic");
        menu = new Menu(1, 2);
        Check(ModelCli.Run(["companion", "download", "owner/menu#mmproj"], output, error, installer, source,
            new NoNetwork(), menu, inspector: new ResolutionTests.NamedInspector()) == 0 && installer.Downloads.List().Length == 0,
            "companion README then cancel creates no operation");
        Check(menu.Options.Count == 2 && menu.Options.All(o => o.Length == 3 && o[0].Contains("mmproj")), "companion menu excludes principal and repeats after README");
        menu = new Menu(0, 1);
        Check(ModelCli.Run(["companion", "download", "owner/menu#mtp"], output, error, installer, source,
            new NoNetwork(), menu, inspector: new ResolutionTests.NamedInspector()) == 0 &&
            menu.Options.All(o => o.SequenceEqual(new[] { "Mostrar README", "Cancelar" })) && installer.Downloads.List().Length == 0,
            "absent auxiliary type exposes only README and cancel at fixed commit");
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
