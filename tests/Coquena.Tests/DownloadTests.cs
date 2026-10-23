using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class DownloadTests
{
    private static int checks;
    private static void Check(bool condition) { checks++; if (!condition) throw new Exception($"Download assertion {checks} failed."); }
    private static OperationDocument Selection(Guid id, int files = 1, long? size = 6, string kind = "model")
    {
        var remote = Enumerable.Range(0, files).Select(i => new RemoteFile($"file{i}.gguf", size)).ToArray();
        return new(1, id, kind, "fixture", kind == "model" ? new("owner/repo", "main", "fixed-commit", remote) : null,
            kind == "backend" ? new("ggml", "b1", "cpu", "cpu", "win-x64", remote) : null,
            remote.Select(f => new TransferRecord(f.Path, $"downloads/{id:D}/files/{f.Path}.part",
                $"{(kind == "backend" ? "backends" : "models")}/fixture/{f.Path}", 0, size, null, null, null, "pending")).ToArray(),
            "pending", "transfer", DateTimeOffset.UtcNow, null);
    }
    private static HttpResponseMessage Response(byte[] bytes, long offset = 0, bool ignore = false, string tag = "\"v1\"", long total = 6)
    {
        var response = new HttpResponseMessage(offset > 0 && !ignore ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
        { Content = new ByteArrayContent(bytes) };
        response.Headers.ETag = new EntityTagHeaderValue(tag);
        if (offset > 0 && !ignore) response.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, offset + bytes.Length - 1, total);
        return response;
    }
    private sealed class Fixture(Func<OperationDocument, int, long, CancellationToken, Task<HttpResponseMessage>> run) : IDownloadTransport
    {
        public int Calls;
        public Task<HttpResponseMessage> OpenAsync(OperationDocument o, int i, long offset, CancellationToken ct)
        { Calls++; return run(o, i, offset, ct); }
    }
    private static Fixture Bytes() => new((_, _, offset, _) => Task.FromResult(Response("abcdef"u8.ToArray()[(int)offset..], offset)));
    private sealed class SlowStream : Stream
    {
        private bool first = true;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (first) { first = false; "abc"u8.CopyTo(buffer.Span); return 3; }
            await Task.Delay(Timeout.Infinite, cancellationToken); return 0;
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
    private static Fixture Slow() => new((_, _, _, _) =>
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new SlowStream()) };
        response.Headers.ETag = new EntityTagHeaderValue("\"v1\"");
        return Task.FromResult(response);
    });
    private sealed class CutStream : Stream
    {
        private bool first = true;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!first) throw new IOException("fixture disconnect");
            first = false; "abc"u8.CopyTo(buffer.Span); return ValueTask.FromResult(3);
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
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(response(request));
    }

    public static void Worker(string root, Guid id)
    {
        var manager = new DownloadManager(new Home(root));
        var result = DownloadCli.Run(["resume", id.ToString()], Console.Out, Console.Error, manager, Slow());
        if (result != 0) throw new Exception("worker failed");
    }

    public static async Task Run(string? executable)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-download-" + Guid.NewGuid().ToString("N"));
        var home = new Home(root); var manager = new DownloadManager(home); var store = new LocalStore(home);
        OperationDocument New(int count = 1, long? size = 6, string kind = "model") => manager.Create(Selection(Guid.NewGuid(), count, size, kind));
        void Save(OperationDocument o) => store.Update(DocumentKind.Operation, ContractJsonContext.Default.OperationDocument,
            ContractValidation.Operation, _ => o, operation: o.Id);
        try
        {
            Check(manager.List().Length == 0 && !Directory.Exists(root));
            var bad = Selection(Guid.NewGuid());
            bad.Files[0] = bad.Files[0] with { PartialPath = "models/escape.part" };
            try { manager.Create(bad); throw new Exception("unsafe accepted"); } catch (DownloadException e) { Check(e.Code == "unsafe-path"); }
            Check(!Directory.Exists(root));
            var complete = New(2); var bytes = Bytes();
            var done = await manager.RunAsync(complete.Id, bytes);
            Check(done!.State == "verifying" && done.Phase == "resource-validation" && bytes.Calls == 2);
            Check(done.Files.All(f => f.ConfirmedBytes == 6 && f.State == "transferred"));
            Check(manager.Status(done.Id).State == "verifying");
            await manager.RunAsync(done.Id, bytes); Check(bytes.Calls == 2); // no repeat
            Check(!File.Exists(home.PathFor(done.Files[0].DestinationPath)));
            var unknown = New(size: null, kind: "backend");
            var unknownDone = await manager.RunAsync(unknown.Id, Bytes());
            Check(unknownDone!.Files[0].ConfirmedBytes == 6 && unknownDone.Files[0].ExpectedSize is null);
            using var output = new StringWriter(); using var error = new StringWriter();
            Check(DownloadCli.Run(["status", unknown.Id.ToString()], output, error, manager) == 0);
            Check(output.ToString().Contains("desconocido") && !output.ToString().Contains('%'));
            Check(manager.List("model").Length == 1 && manager.List("backend").Length == 1 && manager.List("companion").Length == 0);
            Check(DownloadCli.Run(["list", "--model", "--backend"], output, error, manager) == 2);
            Check(DownloadCli.Run(["resume", unknown.Id.ToString()], output, error, manager) == 1);
            Check(error.ToString().Contains("provider-not-integrated"));

            var interrupted = New(); using (var stop = new CancellationTokenSource(180))
            {
                var paused = await manager.RunAsync(interrupted.Id, Slow(), cancellation: stop.Token);
                Check(paused!.State == "paused" && paused.Files[0].ConfirmedBytes == 3);
                Check(File.ReadAllText(home.PathFor(paused.Files[0].PartialPath)) == "abc");
            }
            // A new manager uses the same GUID, validators and prefix.
            var resumed = await new DownloadManager(home).RunAsync(interrupted.Id, Bytes());
            Check(resumed!.Files[0].ConfirmedBytes == 6 && File.ReadAllText(home.PathFor(resumed.Files[0].PartialPath)) == "abcdef");

            OperationDocument Partial()
            {
                var o = New();
                File.WriteAllText(home.PathFor(o.Files[0].PartialPath), "abc");
                o.Files[0] = o.Files[0] with { ConfirmedBytes = 3, ETag = "\"v1\"", State = "paused" };
                o = o with { State = "paused" }; Save(o); return o;
            }
            var ignored = Partial(); var noRange = new Fixture((_, _, _, _) => Task.FromResult(Response("abcdef"u8.ToArray(), ignore: true)));
            var rejected = await manager.RunAsync(ignored.Id, noRange);
            Check(rejected!.Diagnostic == "restart-required" && File.ReadAllText(home.PathFor(ignored.Files[0].PartialPath)) == "abc");
            var restarted = await manager.RunAsync(ignored.Id, noRange, (_, _, _) => Task.FromResult(true));
            Check(restarted!.Id == ignored.Id && File.ReadAllText(home.PathFor(ignored.Files[0].PartialPath)) == "abcdef");
            var changed = Partial();
            var different = new Fixture((_, _, offset, _) => Task.FromResult(Response("def"u8.ToArray(), offset, tag: "\"v2\"")));
            var changedResult = await manager.RunAsync(changed.Id, different, (_, _, _) => throw new Exception("must not ask restart"));
            Check(changedResult!.Diagnostic == "content-changed" && changedResult.Files[0].ConfirmedBytes == 3);
            var wrong = Partial();
            var invalidRange = new Fixture((_, _, _, _) => Task.FromResult(Response("def"u8.ToArray(), 2)));
            Check((await manager.RunAsync(wrong.Id, invalidRange))!.Diagnostic == "invalid-range");
            Check(new FileInfo(home.PathFor(wrong.Files[0].PartialPath)).Length == 3);
            var smaller = Partial(); File.WriteAllText(home.PathFor(smaller.Files[0].PartialPath), "ab");
            var unused = Bytes(); Check((await manager.RunAsync(smaller.Id, unused))!.Diagnostic == "checkpoint-inconsistent" && unused.Calls == 0);
            var ahead = Partial(); File.WriteAllText(home.PathFor(ahead.Files[0].PartialPath), "abcd");
            Check((await manager.RunAsync(ahead.Id, unused))!.Diagnostic == "checkpoint-inconsistent" && unused.Calls == 0);
            var transient = New(2); var unavailable = new Fixture((_, _, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
            Check((await manager.RunAsync(transient.Id, unavailable))!.Diagnostic == "retries-exhausted" && unavailable.Calls == 3);
            Check(manager.Status(transient.Id).Files[1].ConfirmedBytes == 0);
            var cut = New(); var calls = 0;
            var disconnected = new Fixture((_, _, offset, _) =>
            {
                if (++calls > 1) { Check(offset == 3); return Task.FromResult(Response("def"u8.ToArray(), offset)); }
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new CutStream()) };
                response.Headers.ETag = new EntityTagHeaderValue("\"v1\""); response.Content.Headers.ContentLength = 6;
                return Task.FromResult(response);
            });
            Check((await manager.RunAsync(cut.Id, disconnected))!.Files[0].State == "transferred" && calls == 2);
            Check(File.ReadAllText(home.PathFor(cut.Files[0].PartialPath)) == "abcdef");
            var retryLong = New(); var longWait = new Fixture((_, _, _, _) =>
            {
                var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
                response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(60)); return Task.FromResult(response);
            });
            Check((await manager.RunAsync(retryLong.Id, longWait))!.Diagnostic == "retry-after-exceeds-limit" && longWait.Calls == 1);
            var wrongSize = New(); var over = new Fixture((_, _, _, _) => Task.FromResult(Response("abcdefg"u8.ToArray())));
            Check((await manager.RunAsync(wrongSize.Id, over))!.Diagnostic == "size-mismatch" && !File.Exists(home.PathFor(wrongSize.Files[0].PartialPath)));
            var headers = Partial();
            using (var http = new HttpDownloadTransport(new Handler(request =>
            {
                Check(request.Headers.Range!.Ranges.Single().From == 3 && request.Headers.IfRange!.EntityTag!.Tag == "\"v1\"");
                Check(request.Headers.AcceptEncoding.Single().Value == "identity" && request.RequestUri!.AbsolutePath.Contains("fixed-commit"));
                return Response("def"u8.ToArray(), 3);
            }), (o, _, _) => Task.FromResult(new Uri($"https://fixture.invalid/{o.ModelSelection!.Commit}/file0.gguf"))))
                Check((await manager.RunAsync(headers.Id, http))!.Files[0].ConfirmedBytes == 6);
            var redirected = New(); var redirect = new Fixture((_, _, _, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Redirect)));
            Check((await manager.RunAsync(redirected.Id, redirect))!.Diagnostic == "redirect-requires-provider");
            var missingValidator = Partial();
            var absent = new Fixture((_, _, offset, _) => { var r = Response("def"u8.ToArray(), offset); r.Headers.ETag = null; return Task.FromResult(r); });
            Check((await manager.RunAsync(missingValidator.Id, absent))!.Diagnostic == "content-changed");
            var stale = Partial() with { State = "downloading" }; Save(stale);
            Check(manager.Status(stale.Id).State == "paused");

            // Persistence failure is NOT translated into a successful saved pause.
            var persistence = New();
            var faulty = new DownloadManager(home, new LocalStore(home, _ => throw new IOException("fixture")));
            try { await faulty.RunAsync(persistence.Id, Bytes()); throw new Exception("failure ignored"); }
            catch (IOException) { Check(manager.Status(persistence.Id).State == "pending"); }

            // Live independent process: foreground ownership, list doesn't pause it, cross-process controls wait.
            var active = New();
            using (var process = StartWorker(root, active.Id))
            {
                await WaitWriting(active);
                Check(!process.HasExited && manager.Status(active.Id).State == "downloading");
                try { await manager.RunAsync(active.Id, Bytes()); throw new Exception("duplicate executor"); }
                catch (DownloadException e) { Check(e.Code == "executor-busy"); }
                await manager.ControlAsync(active.Id, false);
                Check(process.WaitForExit(10000) && process.ExitCode == 0);
                Check(manager.Status(active.Id).State == "paused" && manager.Status(active.Id).Files[0].ConfirmedBytes == 3);
            }
            var cancelled = New();
            Directory.CreateDirectory(Path.GetDirectoryName(home.PathFor(cancelled.Files[0].DestinationPath))!);
            File.WriteAllText(home.PathFor(cancelled.Files[0].DestinationPath), "installed-preserved");
            using (var process = StartWorker(root, cancelled.Id))
            {
                await WaitWriting(cancelled);
                await manager.ControlAsync(cancelled.Id, true);
                Check(process.WaitForExit(10000) && process.ExitCode == 0);
            }
            Check(!Directory.Exists(home.PathFor($"downloads/{cancelled.Id:D}")));
            Check(File.ReadAllText(home.PathFor(cancelled.Files[0].DestinationPath)) == "installed-preserved");
            await manager.ControlAsync(active.Id, true);
            Check(!manager.List().Any(o => o.Id == active.Id));
            Check(File.ReadAllText(home.PathFor("logs/transactions.jsonl")).Contains(cancelled.Id.ToString()));
            Check(File.ReadAllText(home.PathFor("logs/transactions.jsonl")).Contains("\"result\":0"));
            var cleanupFailure = Partial();
            using (var occupied = new FileStream(home.PathFor(cleanupFailure.Files[0].PartialPath), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                try { await manager.ControlAsync(cleanupFailure.Id, true); throw new Exception("cleanup failure ignored"); }
                catch (DownloadException e) { Check(e.Code == "cleanup-pending"); }
                Check(manager.Status(cleanupFailure.Id).State == "cancelled" && manager.Status(cleanupFailure.Id).Phase == "cleanup");
            }
            await manager.ControlAsync(cleanupFailure.Id, true);
            Check(!Directory.Exists(home.PathFor($"downloads/{cleanupFailure.Id:D}")));

            if (executable is not null)
            {
                var checkpointPath = home.PathFor($"downloads/{unknown.Id:D}/operation.json");
                var originalCheckpoint = File.ReadAllBytes(checkpointPath);
                using var cli = new Process { StartInfo = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = root } };
                cli.StartInfo.ArgumentList.Add("download"); cli.StartInfo.ArgumentList.Add("status"); cli.StartInfo.ArgumentList.Add(unknown.Id.ToString());
                cli.StartInfo.Environment["COQUENA_HOME"] = root;
                cli.Start(); var text = cli.StandardOutput.ReadToEndAsync(); var err = cli.StandardError.ReadToEndAsync();
                // M3 resolves GUIDs in the selected project, never by falling back to an M2 home.
                Check(cli.WaitForExit(10000) && cli.ExitCode == 1 && (await text).Length == 0 && (await err).Contains("not-found"));
                Check(File.ReadAllBytes(checkpointPath).SequenceEqual(originalCheckpoint));
            }
            var corrupt = New(); File.WriteAllText(home.PathFor($"downloads/{corrupt.Id:D}/operation.json"), "{");
            try { manager.List(); throw new Exception("corruption ignored"); } catch (ContractException) { Check(true); }
            Console.WriteLine($"OK: download {checks} assertions; HTTP fixtures, foreground and cross-process controls.");

            async Task WaitWriting(OperationDocument o)
            {
                using var timeout = new CancellationTokenSource(10000);
                while (!File.Exists(home.PathFor(o.Files[0].PartialPath))) await Task.Delay(25, timeout.Token);
                // FileStream buffering may defer length until checkpoint; state is already downloading.
                await Task.Delay(150, timeout.Token);
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    private static Process StartWorker(string root, Guid id)
    {
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Coquena.Tests.dll"));
        start.ArgumentList.Add("--download-worker"); start.ArgumentList.Add(root); start.ArgumentList.Add(id.ToString());
        return Process.Start(start) ?? throw new Exception("worker start failed");
    }
}
