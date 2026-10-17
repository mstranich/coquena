using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using Coquena.Backends;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class BackendPublicationTests
{
    private static int checks;
    private static void Check(bool value, string message)
    { checks++; if (!value) throw new Exception("Backend publication: " + message); }
    private static byte[] Archive()
    {
        using var bytes = new MemoryStream();
        using (var zip = new ZipArchive(bytes, ZipArchiveMode.Create, true))
        {
            using (var writer = new StreamWriter(zip.CreateEntry("llama-server.exe").Open())) writer.Write("version: 1 (abcdef)");
            using (var writer = new StreamWriter(zip.CreateEntry("ggml.dll").Open())) writer.Write("fixture");
        }
        return bytes.ToArray();
    }
    private sealed class Transport(byte[] bytes) : IDownloadTransport
    {
        public int Calls;
        public Task<HttpResponseMessage> OpenAsync(OperationDocument operation, int file, long offset, CancellationToken cancellation)
        {
            Calls++;
            Check(offset == 0, "no unexpected range request");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }
    private static Task<string> Probe(string path, CancellationToken cancellation)
    { cancellation.ThrowIfCancellationRequested(); return Task.FromResult(File.ReadAllText(path)); }

    public static async Task Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-publication-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            foreach (var mode in new[] { "transient", "persistent", "cancel", "stale", "version", "owner", "collision", "unrelated-io" })
                await Scenario(mode);
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine($"OK: {checks} comprobaciones de publicación de backend con lectores Windows.");

        async Task Scenario(string mode)
        {
            var home = new Home(Path.Combine(root, mode)); var store = new LocalStore(home);
            var bytes = Archive();
            var candidate = new BackendCandidate("b1", "cpu", "win-x64", "compatible", "fixture",
                [new("fixture.zip", bytes.Length, "https://github.com/ggml-org/llama.cpp/releases/download/b1/fixture.zip")], true);
            var normal = new BackendInstaller(home, probe: Probe);
            if (mode == "stale")
            {
                var initial = normal.Create(candidate, "latest");
                Check(await normal.Downloads.RunAsync(initial.Id, new Transport(bytes), finalize: normal.FinalizeAsync) is null, "stale fixture installed");
            }
            FileStream? reader = null; var attempts = 0;
            var blocked = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var cancellation = new CancellationTokenSource();
            OperationDocument operation = null!;
            var installer = new BackendInstaller(home, probe: Probe, fault: stage =>
            {
                if (stage != InstallStage.BeforeMove || ++attempts != 1) return;
                if (mode == "unrelated-io") throw new IOException("secret-token=https://example.invalid/?token=private");
                if (mode == "collision")
                {
                    Directory.CreateDirectory(home.PathFor(operation.BackendInstall!.Slot));
                    File.WriteAllText(home.PathFor(operation.BackendInstall.Slot + "/foreign.txt"), "preserve");
                    return;
                }
                reader = new FileStream(home.PathFor($"downloads/{operation.Id:D}/extracted/ggml.dll"), FileMode.Open, FileAccess.Read, FileShare.Read);
                blocked.TrySetResult();
            });
            operation = installer.Create(candidate, "latest"); var transport = new Transport(bytes);
            Task? concurrent = null;
            if (mode is "transient" or "stale" or "version" or "owner" or "cancel")
                concurrent = Task.Run(async () =>
                {
                    await blocked.Task.WaitAsync(TimeSpan.FromSeconds(5));
                    if (mode == "cancel") { await Task.Delay(50); cancellation.Cancel(); return; }
                    // This writer must finish before the reader is released. A retry
                    // holding the global writer lease would deadlock until exhaustion.
                    store.Update(DocumentKind.Config, ContractJsonContext.Default.ConfigDocument, ContractValidation.Config,
                        current => current ?? new(1, null, null, new(5)));
                    if (mode == "stale") normal.Use("cpu:b1");
                    if (mode == "version") File.WriteAllText(home.PathFor($"downloads/{operation.Id:D}/extracted/llama-server.exe"), "version: 2 (abcdef)");
                    if (mode == "owner") File.WriteAllText(home.PathFor($"downloads/{operation.Id:D}/extracted/.coquena-owner"), Guid.NewGuid().ToString("D"));
                    reader!.Dispose();
                });
            try
            {
                var watch = Stopwatch.StartNew();
                var result = await installer.Downloads.RunAsync(operation.Id, transport, cancellation: cancellation.Token, finalize: installer.FinalizeAsync);
                if (concurrent is not null) await concurrent;
                Check(transport.Calls == 1, mode + ": original download only");
                if (mode == "transient")
                {
                    Check(result is null && attempts >= 2 && installer.List().Length == 1, "transient reader recovered without blocking concurrent writer");
                    Check(installer.Downloads.List().Length == 0, "successful retry cleaned operation");
                    return;
                }
                Check(result is not null && Directory.Exists(home.PathFor($"downloads/{operation.Id:D}/extracted")), mode + ": preparation retained");
                Check(installer.List().Length == (mode == "stale" ? 1 : 0), mode + ": no new publication");
                switch (mode)
                {
                    case "persistent":
                        Check(result!.State == "failed" && result.Phase == "publishing" && result.Diagnostic == "backend-publication-move-0x80070005" &&
                            attempts >= 2 && watch.ElapsedMilliseconds < 5000, "persistent rename denial bounded and safely diagnosed");
                        reader!.Dispose();
                        var resumedTransport = new Transport(bytes);
                        Check(await normal.Downloads.RunAsync(operation.Id, resumedTransport, finalize: normal.FinalizeAsync) is null && resumedTransport.Calls == 0,
                            "persistent conflict resumes same fixed bytes after release");
                        break;
                    case "cancel":
                        Check(result!.State == "paused" && result.Phase == "publishing" && watch.ElapsedMilliseconds < 3000,
                            "consumer cancellation interrupts publication backoff");
                        reader!.Dispose();
                        await normal.Downloads.ControlAsync(operation.Id, true, beforeCancel: normal.CancelAsync);
                        Check(!Directory.Exists(home.PathFor($"downloads/{operation.Id:D}")) && normal.List().Length == 0,
                            "cancel paused publication cleans only its preparation");
                        break;
                    case "stale": Check(result!.Diagnostic == "stale-install-plan" && attempts == 1 && installer.List().Single().Pinned,
                            "retry rechecks changed catalog rather than publishing stale plan"); break;
                    case "version": Check(result!.Diagnostic == "invalid-server-version" && attempts == 1, "retry rechecks executable version"); break;
                    case "owner": Check(result!.Diagnostic == "destination-collision" && attempts == 1, "retry rechecks preparation owner"); break;
                    case "collision": Check(result!.State == "failed" && result.Diagnostic!.StartsWith("backend-publication-move-0x") && attempts == 1 &&
                            File.ReadAllText(home.PathFor(operation.BackendInstall!.Slot + "/foreign.txt")) == "preserve", "foreign destination never overwritten or retried"); break;
                    case "unrelated-io": Check(result!.Diagnostic == "transfer-io-failed" && attempts == 1 && !result.Diagnostic.Contains("private"),
                            "unrelated IO is neither retried nor exposed"); break;
                }
            }
            finally { reader?.Dispose(); }
        }
    }
}
