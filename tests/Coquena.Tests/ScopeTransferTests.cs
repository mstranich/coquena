using System.IO.Compression;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Models;
using Coquena.Projects;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class ScopeTransferTests
{
    public static async Task CrashWorker(string folder, ScopeTransferStage stage)
    {
        var context = ScopeContext.Project(Path.Combine(folder, "coquena.yaml"));
        var transfers = new ScopeTransfers(context, reached => { if (reached == stage) Environment.Exit(73); });
        var operation = transfers.Downloads.List().Single();
        await transfers.RunAsync(operation.Id, new Fixture(Gguf()));
        throw new Exception("Crash stage not reached");
    }
    private static int checks;
    private static void Check(bool value, string reason)
    { checks++; if (!value) throw new Exception("Scoped transfer: " + reason); }
    private sealed class Fixture(byte[] bytes, bool changedTag = false, bool ignoreRange = false) : IDownloadTransport
    {
        public long Offset = -1;
        public Task<HttpResponseMessage> OpenAsync(OperationDocument operation, int file, long offset, CancellationToken cancellation)
        {
            Offset = offset;
            var response = new HttpResponseMessage(offset > 0 && !ignoreRange ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
            { Content = new ByteArrayContent(bytes[(ignoreRange ? 0 : (int)offset)..]) };
            response.Headers.ETag = new EntityTagHeaderValue(changedTag ? "\"changed\"" : "\"fixture\"");
            if (offset > 0 && !ignoreRange) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, bytes.Length - 1, bytes.Length);
            return Task.FromResult(response);
        }
    }
    private sealed class Blocking(TaskCompletionSource started) : IDownloadTransport
    {
        public async Task<HttpResponseMessage> OpenAsync(OperationDocument operation, int file, long offset, CancellationToken cancellation)
        { started.TrySetResult(); await Task.Delay(Timeout.Infinite, cancellation); throw new Exception("unexpected"); }
    }
    private sealed class PrefixTransport(byte[] bytes, TaskCompletionSource started) : IDownloadTransport
    {
        public Task<HttpResponseMessage> OpenAsync(OperationDocument operation, int file, long offset, CancellationToken cancellation)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new PrefixStream(bytes, started)) };
            response.Headers.ETag = new EntityTagHeaderValue("\"fixture\"");
            return Task.FromResult(response);
        }
    }
    private sealed class PrefixStream(byte[] bytes, TaskCompletionSource started) : Stream
    {
        private bool first = true;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (first) { first = false; bytes.AsSpan(0, 8).CopyTo(buffer.Span); return 8; }
            started.TrySetResult(); await Task.Delay(Timeout.Infinite, cancellationToken); return 0;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
    private static OperationDocument Selection(byte[] bytes, Guid? id = null, string name = "fixture", bool backend = false, string path = "weights.gguf")
    {
        var guid = id ?? Guid.NewGuid();
        var hash = new ContentIdentity("sha256", "file-bytes", Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)));
        var remote = new RemoteFile(path, bytes.Length, hash);
        return new(1, guid, backend ? "backend" : "model", name,
            backend ? null : new("fixture/repository", "main", new string('a', 40), [remote]),
            backend ? new("ggml", "b1", "cpu", "cpu", "win-x64", [remote]) : null,
            [new(path, $"downloads/{guid:D}/files/0.part", (backend ? "backends/" : "models/") + path, 0, bytes.Length, hash, null, null, "pending")],
            "pending", "transfer", DateTimeOffset.UtcNow, null);
    }
    private static byte[] Gguf()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write("GGUF"u8); writer.Write(3u); writer.Write(0ul); writer.Write(0ul);
        return stream.ToArray();
    }
    private static byte[] Archive(params (string Name, byte[] Bytes)[] files)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var file in files) { var entry = archive.CreateEntry(file.Name); using var output = entry.Open(); output.Write(file.Bytes); }
        return stream.ToArray();
    }
    private static ScopeContext Context(string root, string name)
    { var folder = Path.Combine(root, name); Directory.CreateDirectory(folder); return ScopeContext.Project(Path.Combine(folder, "coquena.yaml")); }
    private static JsonArray Resources(ScopeContext context) => new ScopeStore(context).Read().Documents["local/inventory.json"]["data"]!["resources"]!.AsArray();
    private static string Destination(ScopeContext context, OperationDocument operation) => context.PathFor(
        $"{(operation.ResourceKind == "backend" ? "backends" : "models")}/installations/{operation.ScopePublication!.InstallationId:D}");
    private static async Task Expect(string code, Func<Task> action)
    {
        try { await action(); throw new Exception("Expected " + code); }
        catch (DownloadException e) { Check(e.Code == code, code + ": " + e.Code); }
        catch (StorageException e) { Check(e.Code == code, code + ": " + e.Code); }
    }
    private static void Checkpoint(ScopeContext context, OperationDocument operation, byte[] bytes, long length)
    {
        var path = context.PathFor($"downloads/{operation.Id:D}/operation.json");
        var document = JsonNode.Parse(File.ReadAllBytes(path))!.AsObject();
        var prefixHash = new ContentIdentity("sha256", "file-bytes", Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes[..(int)length])));
        var value = operation with { Files = [operation.Files[0] with { ConfirmedBytes = length, ETag = "\"fixture\"", State = "paused", CheckpointIntegrity = prefixHash }], State = "paused" };
        document["data"]!["transfer"] = JsonNode.Parse(ContractJson.Write(value, ContractJsonContext.Default.OperationDocument));
        File.WriteAllText(path, document.ToJsonString());
        File.WriteAllBytes(context.PathFor(operation.Files[0].PartialPath), bytes[..(int)length]);
    }

    public static async Task Run(string? executable = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-scope-transfer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var bytes = Gguf(); var a = Context(root, "a"); var b = Context(root, "b");
            var global = ScopeContext.Global(new Home(Path.Combine(root, "home")));
            var first = new ScopeTransfers(a); var second = new ScopeTransfers(b); var imported = new ScopeTransfers(global);
            var id = Guid.NewGuid();
            var operation = first.Create(Selection(bytes, id)); var other = second.Create(Selection(bytes, id));
            imported.Create(Selection(bytes, id));
            Check(first.Downloads.List().Length == 1 && second.Downloads.List().Length == 1 && imported.Downloads.List().Length == 1, "GUID isolated in three scopes");
            Check(JsonNode.Parse(File.ReadAllBytes(a.PathFor($"downloads/{id:D}/operation.json")))!["kind"]!.GetValue<string>() == "operation", "project envelope");
            await first.ControlAsync(id, true);
            Check(second.Downloads.Status(id).State == "pending" && imported.Downloads.Status(id).State == "pending", "cancel scoped");
            await second.RunAsync(id, new Fixture(bytes));
            Check(second.Downloads.List().Length == 0 && File.Exists(Path.Combine(Destination(b, other), "weights.gguf")), "physical publish and cleanup");
            Check(Resources(b).Count == 1 && Resources(a).Count == 0 && Resources(global).Count == 0, "inventory scoped");
            Check(!Directory.Exists(b.PathFor("catalogs")), "no M2 catalogs");
            var previousPath = Path.Combine(Destination(b, other), "weights.gguf");
            var next = second.Create(Selection(bytes), "automatic");
            await second.RunAsync(next.Id, new Fixture(bytes));
            Check(Resources(b)[0]!["installations"]!.AsArray().Count == 2 && File.ReadAllBytes(previousPath).SequenceEqual(bytes), "new immutable revision retains previous");
            Check(!new ScopeStore(b).Read().Documents.Keys.Any(p => p.Contains("combinations", StringComparison.Ordinal)), "no combination locks");

            var resume = first.Create(Selection(bytes)); Checkpoint(a, resume, bytes, 8);
            var fixture = new Fixture(bytes); await first.RunAsync(resume.Id, fixture);
            Check(fixture.Offset == 8 && Resources(a).Count == 1, "range resume full hash");
            var mismatch = first.Create(Selection(bytes, name: "extra")); Checkpoint(a, mismatch, bytes, 8);
            using (var append = new FileStream(a.PathFor(mismatch.Files[0].PartialPath), FileMode.Append)) append.WriteByte(9);
            var untouched = new Fixture(bytes); var failed = await first.RunAsync(mismatch.Id, untouched);
            Check(failed?.Diagnostic == "checkpoint-inconsistent" && untouched.Offset == -1, "extra bytes never adopted");
            await first.ControlAsync(mismatch.Id, true);

            var prefix = first.Create(Selection(bytes, name: "prefix")); Checkpoint(a, prefix, bytes, 8);
            File.WriteAllBytes(a.PathFor(prefix.Files[0].PartialPath), "changed!"u8.ToArray());
            failed = await first.RunAsync(prefix.Id, new Fixture(bytes));
            Check(failed?.Diagnostic == "checkpoint-integrity-failed" && new FileInfo(a.PathFor(prefix.Files[0].PartialPath)).Length == 8,
                "same-length substituted prefix is not adopted");
            await first.ControlAsync(prefix.Id, true);

            foreach (var changed in new[] { false, true })
            {
                var broken = first.Create(Selection(bytes, name: "range")); Checkpoint(a, broken, bytes, 8);
                failed = await first.RunAsync(broken.Id, new Fixture(bytes, changedTag: changed, ignoreRange: !changed));
                Check(failed?.State == "failed" && new FileInfo(a.PathFor(broken.Files[0].PartialPath)).Length == 8, "200 or changed identity preserves partial");
                await first.ControlAsync(broken.Id, true);
            }
            var badHash = first.Create(Selection(bytes, name: "hash")); var tampered = (byte[])bytes.Clone(); tampered[^1] = 1;
            failed = await first.RunAsync(badHash.Id, new Fixture(tampered));
            Check(failed?.Diagnostic == "remote-hash-mismatch" && !Directory.Exists(Destination(a, badHash)), "remote hash mismatch");
            await first.ControlAsync(badHash.Id, true);

            var busy = first.Create(Selection(bytes, name: "busy")); var entered = new TaskCompletionSource();
            using (var stopped = new CancellationTokenSource())
            {
                var task = first.RunAsync(busy.Id, new Blocking(entered), cancellation: stopped.Token);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
                new RecipeRegistry(new ScopeStore(a)).RegisterResource("neighbor", "model", "external");
                Check(Resources(a).Any(r => r!["logical-key"]!.GetValue<string>() == "neighbor"), "network does not retain inventory writer");
                await Expect("executor-busy", () => new ScopeTransfers(a).RunAsync(busy.Id, new Fixture(bytes)));
                stopped.Cancel(); Check((await task)?.State == "paused", "interruption keeps checkpoint");
            }
            await first.ControlAsync(busy.Id, true);
            await Expect("resource-ownership-conflict", () => Task.FromResult(first.Create(Selection(bytes, name: "neighbor"))));

            var contended = first.Create(Selection(bytes, name: "contended"));
            var reading = new TaskCompletionSource(); var retrying = new TaskCompletionSource();
            using (var stopped = new CancellationTokenSource())
            {
                var task = first.RunAsync(contended.Id, new PrefixTransport(bytes, reading), operation =>
                { if (operation.Diagnostic == "checkpoint-writer-busy") retrying.TrySetResult(); }, stopped.Token);
                await reading.Task.WaitAsync(TimeSpan.FromSeconds(5));
                using (var held = new FileStream(a.PathFor("local/writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
                {
                    stopped.Cancel(); await retrying.Task.WaitAsync(TimeSpan.FromSeconds(10));
                    Check(!task.IsCompleted, "scoped checkpoint waits through inventory contention");
                }
                var paused = await task;
                Check(paused?.State == "paused" && paused.Files[0].ConfirmedBytes == 8 && paused.Files[0].CheckpointIntegrity is not null,
                    "flush and prefix identity retained during contended cancellation");
            }
            await first.RunAsync(contended.Id, new Fixture(bytes));
            Check(first.Published(contended), "contended checkpoint resumes without lost bytes");

            foreach (var stage in Enum.GetValues<ScopeTransferStage>())
            {
                var context = Context(root, "crash-" + stage);
                var crashing = new ScopeTransfers(context, reached => { if (reached == stage) throw new IOException("fixture interruption"); });
                var op = crashing.Create(Selection(bytes));
                failed = await crashing.RunAsync(op.Id, new Fixture(bytes));
                Check(failed?.State == "failed", "fault recorded: " + stage);
                var recovering = new ScopeTransfers(context);
                await recovering.RunAsync(op.Id, new Fixture(bytes));
                Check(Resources(context).Count == 1 && recovering.Downloads.List().Length == 0, "idempotent recovery: " + stage);
            }
            foreach (var stage in Enum.GetValues<ScopeTransferStage>())
            {
                var context = Context(root, "process-crash-" + stage); var transfers = new ScopeTransfers(context);
                var op = transfers.Create(Selection(bytes));
                var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
                foreach (var arg in new[] { "--scope-transfer-crash-worker", context.Base, stage.ToString() }) start.ArgumentList.Add(arg);
                using var child = Process.Start(start)!;
                await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(20));
                Check(child.ExitCode == 73, "actual process interruption: " + stage);
                await new ScopeTransfers(context).RunAsync(op.Id, new Fixture(bytes));
                Check(Resources(context).Count == 1 && transfers.Downloads.List().Length == 0, "actual process recovery: " + stage);
            }

            var cancelContext = Context(root, "cancel-physical");
            var cancel = new ScopeTransfers(cancelContext, stage => { if (stage == ScopeTransferStage.PhysicalPublished) throw new IOException(); });
            var cancelOp = cancel.Create(Selection(bytes)); await cancel.RunAsync(cancelOp.Id, new Fixture(bytes));
            await new ScopeTransfers(cancelContext).ControlAsync(cancelOp.Id, true);
            Check(!Directory.Exists(Destination(cancelContext, cancelOp)) && Resources(cancelContext).Count == 0, "cancel uncommitted own slot");

            var cleanupContext = Context(root, "cleanup"); FileStream? locked = null; OperationDocument? cleanupOp = null;
            var cleanup = new ScopeTransfers(cleanupContext, stage =>
            {
                if (stage == ScopeTransferStage.InventoryPublished)
                    locked = new FileStream(cleanupContext.PathFor(cleanupOp!.Files[0].PartialPath), FileMode.Open, FileAccess.Read, FileShare.Read);
            });
            cleanupOp = cleanup.Create(Selection(bytes));
            try { await Expect("cleanup-pending", () => cleanup.RunAsync(cleanupOp.Id, new Fixture(bytes))); }
            finally { locked?.Dispose(); }
            Check(Resources(cleanupContext).Count == 1 && cleanup.Downloads.Status(cleanupOp.Id).Phase == "cleanup", "cleanup failure retains committed inventory");
            await new ScopeTransfers(cleanupContext).RunAsync(cleanupOp.Id, new Fixture(bytes));
            Check(new ScopeTransfers(cleanupContext).Downloads.List().Length == 0 && File.Exists(Path.Combine(Destination(cleanupContext, cleanupOp), "weights.gguf")), "cleanup retry preserves published slot");

            var substitutionContext = Context(root, "substitution");
            var substitution = new ScopeTransfers(substitutionContext, stage => { if (stage == ScopeTransferStage.Prepared) throw new IOException(); });
            var substitutionOp = substitution.Create(Selection(bytes)); await substitution.RunAsync(substitutionOp.Id, new Fixture(bytes));
            var destination = Destination(substitutionContext, substitutionOp); Directory.CreateDirectory(destination);
            File.WriteAllBytes(Path.Combine(destination, "weights.gguf"), bytes);
            failed = await new ScopeTransfers(substitutionContext).RunAsync(substitutionOp.Id, new Fixture(bytes));
            Check(failed?.Diagnostic == "publication-directory-conflict" && Resources(substitutionContext).Count == 0, "same bytes do not prove destination ownership");
            await Expect("publication-manifest-conflict", () => new ScopeTransfers(substitutionContext).ControlAsync(substitutionOp.Id, true));
            Check(File.Exists(Path.Combine(destination, "weights.gguf")), "cancel preserves substituted destination");

            var backendContext = Context(root, "backend"); var backend = new ScopeTransfers(backendContext);
            var zip = Archive(("llama-server.exe", "fixture"u8.ToArray()), ("dependency.dll", "library"u8.ToArray()));
            var backendOp = backend.Create(Selection(zip, backend: true, path: "backend.zip"), requiredFiles: ["llama-server.exe", "dependency.dll"]);
            await backend.RunAsync(backendOp.Id, new Fixture(zip));
            Check(File.Exists(Path.Combine(Destination(backendContext, backendOp), "dependency.dll")) && Resources(backendContext).Count == 1, "backend complete artifacts");
            foreach (var invalidZip in new[] { Archive(("../escape", bytes)), Archive(("llama-server.exe", bytes)) })
            {
                var op = backend.Create(Selection(invalidZip, name: Guid.NewGuid().ToString("N"), backend: true, path: "backend.zip"), requiredFiles: ["llama-server.exe", "dependency.dll"]);
                failed = await backend.RunAsync(op.Id, new Fixture(invalidZip));
                Check(failed?.State == "failed" && failed.Diagnostic is "unsafe-path" or "backend-required-file-missing", "extraction failure persisted with diagnosis");
                Check(!Directory.Exists(Destination(backendContext, op)) && Resources(backendContext).Count == 1, "failed extraction preserves installed backend");
                await backend.ControlAsync(op.Id, true);
            }
            var invalidGguf = first.Create(Selection("invalid GGUF"u8.ToArray(), name: "invalid"));
            failed = await first.RunAsync(invalidGguf.Id, new Fixture("invalid GGUF"u8.ToArray()));
            Check(failed?.Diagnostic == "invalid-gguf-unit", "unit validation before publish"); await first.ControlAsync(invalidGguf.Id, true);
            var shards = first.Create(Selection(bytes, name: "shards", path: "weights-00001-of-00002.gguf"));
            failed = await first.RunAsync(shards.Id, new Fixture(bytes));
            Check(failed?.Diagnostic == "invalid-gguf-unit", "incomplete shards never published"); await first.ControlAsync(shards.Id, true);

            var copied = first.Create(Selection(bytes, name: "copied-checkpoint"));
            var foreign = imported.Create(Selection(bytes, copied.Id, name: "copied-checkpoint"));
            File.Copy(a.PathFor($"downloads/{copied.Id:D}/operation.json"), global.PathFor($"downloads/{foreign.Id:D}/operation.json"), true);
            await Expect("transfer-scope-conflict", () => imported.RunAsync(copied.Id, new Fixture(bytes)));
            Check(File.Exists(global.PathFor($"downloads/{foreign.Id:D}/operation.json")), "foreign checkpoint retained for diagnosis");
            // Restore only this test's known metadata to permit independent scoped CLI checks.
            File.Delete(global.PathFor($"downloads/{foreign.Id:D}/operation.json"));
            Directory.Delete(global.PathFor($"downloads/{foreign.Id:D}/files"));
            Directory.Delete(global.PathFor($"downloads/{foreign.Id:D}"));
            await first.ControlAsync(copied.Id, true);

            // Query uses only its selected scope and preserves exact byte counts in JSON.
            var query = imported.Downloads.Status(id); var output = new StringWriter(); var error = new StringWriter();
            var previousHome = Environment.GetEnvironmentVariable("COQUENA_HOME");
            Environment.SetEnvironmentVariable("COQUENA_HOME", global.Base);
            try
            {
                Check(Cli.Run(["download", "status", id.ToString("D"), "--global", "--json"], output, error) == 0 &&
                    JsonNode.Parse(output.ToString())!["files"]![0]!["confirmedBytes"]!.GetValue<long>() == 0, "CLI global exact status");
                output.GetStringBuilder().Clear();
                Check(Cli.Run(["download", "status", id.ToString("D"), "-g", "--json"], output, error) == 0 &&
                    JsonNode.Parse(output.ToString())!["id"]!.GetValue<string>() == id.ToString("D"), "short global alias selects same scope");
                var aliasOp = second.Create(Selection(bytes, name: "file-alias"));
                foreach (var option in new[] { "--file", "-f" })
                {
                    output.GetStringBuilder().Clear();
                    Check(Cli.Run(["download", "status", aliasOp.Id.ToString("D"), option, Path.Combine(b.Base, "coquena.yaml"), "--json"], output, error) == 0 &&
                        JsonNode.Parse(output.ToString())!["id"]!.GetValue<string>() == aliasOp.Id.ToString("D"), "file aliases select same scope: " + option);
                    Check(Cli.Run(["download", "list", "-g", option, Path.Combine(b.Base, "coquena.yaml")], output, error) == 2,
                        "global alias rejects project selection: " + option);
                }
                if (executable is not null)
                {
                    foreach (var option in new[] { "--global", "-g", "--file", "-f" })
                    {
                        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
                            RedirectStandardOutput = true, RedirectStandardError = true };
                        var selectedId = option is "--global" or "-g" ? id : aliasOp.Id;
                        foreach (var arg in new[] { "download", "status", selectedId.ToString("D"), option }) start.ArgumentList.Add(arg);
                        if (option is "--file" or "-f") start.ArgumentList.Add(Path.Combine(b.Base, "coquena.yaml"));
                        start.ArgumentList.Add("--json");
                        using var process = Process.Start(start)!;
                        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
                        Check(process.ExitCode == 0 && (await stderr).Length == 0 &&
                            JsonNode.Parse(await stdout)!["id"]!.GetValue<string>() == selectedId.ToString("D"), "published product selector: " + option);
                    }
                }
                await second.ControlAsync(aliasOp.Id, true);
                output.GetStringBuilder().Clear();
                Check(Cli.Run(["download", "status", id.ToString("D"), "--base", a.Base], output, error) == 1, "CLI never searches other scopes");
                var empty = Context(root, "empty");
                Check(Cli.Run(["download", "list", "--base", empty.Base], output, error) == 0 && !Directory.Exists(empty.Root), "read does not initialize empty scope");
            }
            finally { Environment.SetEnvironmentVariable("COQUENA_HOME", previousHome); }
            Console.WriteLine($"Scoped transfers: {checks} checks passed.");
        }
        finally { Directory.Delete(root, true); }
    }
}
