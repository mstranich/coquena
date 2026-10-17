using System.Net;
using System.Diagnostics;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class ContentionTests
{
    public static async Task Run()
    {
        foreach (var (milliseconds, control) in new[] { (2000, "none"), (8500, "none"), (8500, "pause"), (8500, "cancel") })
        {
            var root = Path.Combine(Path.GetTempPath(), "coquena-contention-" + Guid.NewGuid());
            var home = new Home(root);
            try
            {
                var id = Guid.NewGuid();
                var manager = new DownloadManager(home);
                manager.Create(new(1, id, "backend", "fixture", null,
                    new("ggml", "b1", "cpu", "cpu", "win-x64", [new("test.zip", 131072)]),
                    [new("test.zip", $"downloads/{id:D}/files/0.part", "backends/fixture/test.zip", 0, 131072, null, null, null, "pending")],
                    "pending", "transfer", DateTimeOffset.UtcNow, null));
                using var source = new ContendedStream(home, id, milliseconds, control);
                OperationDocument? result;
                var waiting = false;
                try
                {
                    result = await manager.RunAsync(id, new Transport(source), progress: value =>
                    {
                        if (value.Diagnostic != "checkpoint-writer-busy") return;
                        waiting = true;
                        if (value.Files[0].ConfirmedBytes != 0) throw new Exception("Unpersisted bytes advertised as confirmed.");
                    });
                }
                finally { await source.Holder; }
                if (waiting != (milliseconds > 3000)) throw new Exception("Contention diagnostic missing or spurious.");
                var expected = control == "pause" ? 65536 : 131072;
                if (control == "cancel")
                {
                    if (result is not null || manager.List().Length != 0) throw new Exception("Cancel did not clean the operation.");
                }
                else if (result?.State != (control == "pause" ? "paused" : "verifying") || result.Files[0].ConfirmedBytes != expected ||
                    new FileInfo(home.PathFor(result.Files[0].PartialPath)).Length != expected || manager.Status(id).Diagnostic is not null)
                    throw new Exception("Contention lost a recoverable checkpoint: " + milliseconds);
                if (control == "pause")
                {
                    var resumed = await manager.RunAsync(id, new ResumeTransport());
                    if (resumed?.Files[0].ConfirmedBytes != 131072 || resumed.State != "verifying")
                        throw new Exception("Resume failed after checkpoint contention.");
                }
                Console.WriteLine($"OK: contention {milliseconds} ms; control {control}; checkpoint/cleanup coherent.");
            }
            finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        }
        await Crash();
    }

    public static async Task CrashWorker(string root, Guid id)
    {
        var home = new Home(root);
        using var source = new ContendedStream(home, id, 30000, "none");
        await new DownloadManager(home).RunAsync(id, new Transport(source), progress: value =>
        {
            if (value.Diagnostic == "checkpoint-writer-busy") File.WriteAllText(Path.Combine(root, "crash-ready"), "ready");
        });
    }

    private static async Task Crash()
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-checkpoint-crash-" + Guid.NewGuid());
        var home = new Home(root); var manager = new DownloadManager(home); var id = Guid.NewGuid();
        manager.Create(new(1, id, "backend", "fixture", null,
            new("ggml", "b1", "cpu", "cpu", "win-x64", [new("test.zip", 131072)]),
            [new("test.zip", $"downloads/{id:D}/files/0.part", "backends/fixture/test.zip", 0, 131072, null, null, null, "pending")],
            "pending", "transfer", DateTimeOffset.UtcNow, null));
        var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
        if (Path.GetFileNameWithoutExtension(Environment.ProcessPath!).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Coquena.Tests.dll"));
        start.ArgumentList.Add("--contention-crash-worker"); start.ArgumentList.Add(root); start.ArgumentList.Add(id.ToString());
        using var child = Process.Start(start) ?? throw new Exception("Crash worker failed to start.");
        try
        {
            using var timeout = new CancellationTokenSource(20000);
            while (!File.Exists(Path.Combine(root, "crash-ready"))) await Task.Delay(25, timeout.Token);
            child.Kill(true); await child.WaitForExitAsync(timeout.Token);
            var status = manager.Status(id);
            if (status.Diagnostic != "checkpoint-inconsistent" || status.Files[0].ConfirmedBytes != 0 ||
                new FileInfo(home.PathFor(status.Files[0].PartialPath)).Length != 65536)
                throw new Exception("Crash silently adopted unconfirmed bytes.");
            var resumed = await manager.RunAsync(id, new NoNetwork());
            if (resumed?.Diagnostic != "checkpoint-inconsistent") throw new Exception("Crash reused an unconfirmed prefix.");
            await manager.ControlAsync(id, true);
            Console.WriteLine("OK: forced crash during checkpoint wait; unconfirmed bytes rejected without network; explicit cancel cleans.");
        }
        finally
        {
            if (!child.HasExited) { child.Kill(true); await child.WaitForExitAsync(); }
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    private sealed class NoNetwork : IDownloadTransport
    {
        public Task<HttpResponseMessage> OpenAsync(OperationDocument operation, int file, long offset, CancellationToken cancellation)
            => throw new Exception("Unexpected network after inconsistent checkpoint.");
    }

    private sealed class ResumeTransport : IDownloadTransport
    {
        public Task<HttpResponseMessage> OpenAsync(OperationDocument operation, int file, long offset, CancellationToken cancellation)
        {
            if (offset != 65536) throw new Exception("Resume repeated a confirmed prefix.");
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
                { Content = new ByteArrayContent(Enumerable.Repeat((byte)65, 65536).ToArray()) };
            response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(offset, 131071, 131072);
            return Task.FromResult(response);
        }
    }

    private sealed class Transport(Stream source) : IDownloadTransport
    {
        public Task<HttpResponseMessage> OpenAsync(OperationDocument operation, int file, long offset, CancellationToken cancellation)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(source) };
            response.Content.Headers.ContentLength = 131072;
            return Task.FromResult(response);
        }
    }

    private sealed class ContendedStream(Home home, Guid id, int milliseconds, string control) : Stream
    {
        private int reads;
        public Task Holder { get; private set; } = Task.CompletedTask;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellation = default)
        {
            cancellation.ThrowIfCancellationRequested();
            if (reads++ == 2) return 0;
            if (reads == 1)
            {
                var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                Holder = Task.Run(async () =>
                {
                    using var held = new FileStream(home.PathFor(".writer.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                    acquired.SetResult();
                    await Task.Delay(1400);
                    if (control != "none") File.WriteAllText(home.PathFor($"downloads/{id:D}/{control}.request"), "");
                    await Task.Delay(milliseconds - 1400);
                });
                await acquired.Task.WaitAsync(TimeSpan.FromSeconds(10));
                // Force the periodic checkpoint while the writer is held, independent of disk speed.
                await Task.Delay(1100, cancellation);
            }
            buffer.Span[..65536].Fill(65);
            return 65536;
        }
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => 131072;
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
