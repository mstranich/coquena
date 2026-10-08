using System.IO.Compression;
using System.Net;
using Coquena;
using Coquena.Backends;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class InstallerTests
{
    private static int checks;
    private static void Check(bool condition, string description)
    { checks++; if (!condition) throw new Exception($"Installer {checks}: {description}"); }
    private static byte[] Zip(params (string Path, byte[] Bytes)[] entries)
    {
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
            foreach (var (path, bytes) in entries)
            { using var target = archive.CreateEntry(path).Open(); target.Write(bytes); }
        return stream.ToArray();
    }
    private sealed class Transport(byte[][] content) : IDownloadTransport
    {
        public int Calls;
        public Task<HttpResponseMessage> OpenAsync(OperationDocument operation, int file, long offset, CancellationToken cancellation)
        {
            Calls++; cancellation.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content[file]) });
        }
    }
    private static BackendCandidate Candidate(byte[][] bytes, string release = "b1", string variant = "cpu") => new(release,
        variant, "win-x64", "compatible", "fixture", bytes.Select((b,i) => new ReleaseAsset($"artifact{i}.zip", b.Length,
            $"https://github.com/ggml-org/llama.cpp/releases/download/{release}/artifact{i}.zip")).ToArray(), true);
    private static Task<string> Probe(string path, CancellationToken cancellation)
    { cancellation.ThrowIfCancellationRequested(); return Task.FromResult(File.ReadAllText(path)); }
    private static byte[] Server(string build = "1") => Zip(("llama-server.exe", System.Text.Encoding.UTF8.GetBytes($"version: {build} (abcdef)")), ("ggml.dll", "lib"u8.ToArray()));
    private static async Task<OperationDocument?> Run(BackendInstaller installer, OperationDocument operation, Transport transport)
        => await installer.Downloads.RunAsync(operation.Id, transport, finalize: installer.FinalizeAsync);

    public static async Task Run(string? executable)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var oldHome = Environment.GetEnvironmentVariable("COQUENA_HOME");
        try
        {
            var home = new Home(root); var store = new LocalStore(home);
            var installer = new BackendInstaller(home, probe: Probe);
            var transport = new Transport([Server()]);
            var operation = installer.Create(Candidate([Server()]), "latest");
            Check(operation.BackendSelection!.Release == "b1" && operation.BackendInstall!.RequestedRelease == "latest", "pinned release plus requested intent");
            Check(await Run(installer, operation, transport) is null && transport.Calls == 1, "installation completes");
            var original = installer.List().Single();
            Check(original.TracksLatest && !original.Pinned && File.Exists(home.PathFor(original.ServerPath)), "only validated catalog entry");
            Check(installer.Use(null)?.Kind == "latest", "first backend auto-default latest");
            Check(!Directory.Exists(home.PathFor($"downloads/{operation.Id:D}")), "successful operation cleaned");
            Check(!File.ReadAllText(home.PathFor("catalogs/backends.json")).Contains("sha256", StringComparison.OrdinalIgnoreCase), "no backend digests");
            Check(installer.Resolve("oficial/cpu:latest").Variant == "cpu", "official alias selection");
            installer.Use("cpu:b1");
            Check(installer.Use(null)?.BackendId == original.Id && installer.List().Single().Pinned, "fixed use pins record");
            operation = installer.Create(Candidate([Server()]), "latest"); transport = new([Server()]);
            Check(await Run(installer, operation, transport) is null && transport.Calls == 1, "forced reinstall redownloads");
            var reinstalled = installer.List().Single();
            Check(reinstalled.Id == original.Id && reinstalled.ServerPath != original.ServerPath && reinstalled.Pinned, "same build retains ID and pin");
            Check(!File.Exists(home.PathFor(original.ServerPath)) && installer.Use(null)?.Kind == "fixed", "old slot cleanup and default intent preserved");
            operation = installer.Create(Candidate([Server("2")], "b2"), "latest");
            await Run(installer, operation, new([Server("2")]));
            Check(installer.List().Count(b => b.TracksLatest) == 1 && installer.List().Single(b => b.TracksLatest).Build == "b2", "latest current advances");
            Check(installer.List().Single(b => b.Id == original.Id).Pinned, "previous pinned build retained");
            Check(installer.Use(null)?.BackendId == original.Id, "new latest never replaces fixed default");
            var pin = installer.Create(Candidate([Server("3")], "b3"), "b3");
            await Run(installer, pin, new([Server("3")]));
            Check(installer.List().Single(b => b.Build == "b3").Pinned && installer.List().Single(b => b.TracksLatest).Build == "b2", "new pin does not advance latest");

            // Validation failures never publish a partial backend or replace an installed one.
            foreach (var bytes in new[] {
                Zip(("../escaped.exe", "bad"u8.ToArray())), Zip(("C:/escaped.exe", "bad"u8.ToArray())),
                Zip(("llama-server.exe", "version: 9 (abcdef)"u8.ToArray())),
                Zip(("llama-server.exe", "not a version"u8.ToArray())),
                Zip(("foo.dll", "missing server"u8.ToArray())),
                Zip(("llama-server.exe", "version: 1 (abcdef)"u8.ToArray()), ("LLAMA-SERVER.EXE", "duplicate"u8.ToArray())),
                "incomplete zip"u8.ToArray() })
            {
                var prior = installer.List().Single(b => b.Build == "b1");
                operation = installer.Create(Candidate([bytes]), "latest");
                var result = await Run(installer, operation, new([bytes]));
                Check(result?.State == "failed" && File.Exists(home.PathFor(prior.ServerPath)) && installer.List().Single(b => b.Build == "b1") == prior, "invalid archive/version preserves old backend");
                await installer.Downloads.ControlAsync(operation.Id, true, beforeCancel: installer.CancelAsync);
                Check(!Directory.Exists(home.PathFor($"downloads/{operation.Id:D}")), "failed validation can cancel");
            }
            Check(!File.Exists(Path.Combine(root, "escaped.exe")), "no archive escapes");
            // Multi-artifact dependency collision and complete dependency extraction.
            byte[][] dependencies = [Server("4"), Zip(("cudart.dll", "cuda"u8.ToArray()))];
            operation = installer.Create(Candidate(dependencies, "b4", "cuda@12"), "b4");
            await Run(installer, operation, new(dependencies));
            Check(installer.List().Any(b => b.Variant == "cuda@12" && b.Build == "b4"), "multi-artifact backend published");

            // Crash after directory movement; resume uses fixed selection and no network.
            foreach (var stage in new[] { InstallStage.AfterMove, InstallStage.AfterIndex })
            {
                var crash = new BackendInstaller(home, probe: Probe, fault: current => { if (current == stage) throw new IOException("injected crash"); });
                var build = stage == InstallStage.AfterMove ? "5" : "6";
                operation = crash.Create(Candidate([Server(build)], "b" + build), "latest");
                var result = await Run(crash, operation, new([Server(build)]));
                Check(result?.Phase == "publishing" && result.State == "failed", "recoverable publication interruption");
                transport = new([Server(build)]);
                Check(await Run(installer, result!, transport) is null && transport.Calls == 0, "resume does not resolve or redownload release");
                Check(installer.List().Single(b => b.TracksLatest).Build == "b" + build, "recovered current latest coherent");
            }
            // Concurrent operations selected against the same catalog: stale plan cannot win.
            var first = installer.Create(Candidate([Server("7")], "b7"), "latest");
            var second = installer.Create(Candidate([Server("8")], "b8"), "latest");
            await Run(installer, first, new([Server("7")]));
            var stale = await Run(installer, second, new([Server("8")]));
            Check(stale?.Diagnostic == "stale-latest-plan" && installer.List().Single(b => b.TracksLatest).Build == "b7", "publication rechecks expected latest");
            await installer.Downloads.ControlAsync(second.Id, true, beforeCancel: installer.CancelAsync);
            // Destination collision must not overwrite a foreign slot.
            operation = installer.Create(Candidate([Server("9")], "b9"), "b9");
            var slot = home.PathFor(operation.BackendInstall!.Slot); Directory.CreateDirectory(slot);
            File.WriteAllText(Path.Combine(slot, "foreign.txt"), "foreign");
            var collision = await Run(installer, operation, new([Server("9")]));
            Check(collision?.Diagnostic == "destination-collision" && File.Exists(Path.Combine(slot, "foreign.txt")), "foreign destination preserved");
            // Avoid deleting the foreign slot when testing cancellation.
            Directory.Delete(slot, true);
            await installer.Downloads.ControlAsync(operation.Id, true, beforeCancel: installer.CancelAsync);

            // New official version format, limits and symlinks are resource-level checks.
            byte[] modern = Zip(("llama-server.exe", "version: 0.6.0-dev (build 10, commit abcdef)"u8.ToArray()));
            operation = installer.Create(Candidate([modern], "b10"), "b10");
            Check(await Run(installer, operation, new([modern])) is null, "current official version format");
            var many = Zip(Enumerable.Range(0, BackendInstaller.MaxEntries + 1).Select(i => ($"file{i}", Array.Empty<byte>())).ToArray());
            operation = installer.Create(Candidate([many], "b11"), "b11");
            Check((await Run(installer, operation, new([many])))?.Diagnostic == "extraction-limit", "entry limit enforced");
            await installer.Downloads.ControlAsync(operation.Id, true, beforeCancel: installer.CancelAsync);
            using (var buffer = new MemoryStream())
            {
                using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
                {
                    var link = zip.CreateEntry("llama-server.exe"); link.ExternalAttributes = unchecked((int)0xa1ff0000);
                    using var payload = link.Open(); payload.Write("target"u8);
                }
                byte[] symlink = buffer.ToArray();
                operation = installer.Create(Candidate([symlink], "b12"), "b12");
                Check((await Run(installer, operation, new([symlink])))?.Diagnostic == "unsafe-archive-entry", "symlink rejected");
                await installer.Downloads.ControlAsync(operation.Id, true, beforeCancel: installer.CancelAsync);
            }
            // Cancellation after movement rolls back only the operation-owned, unindexed slot.
            var interrupted = new BackendInstaller(home, probe: Probe, fault: _ => throw new IOException("crash"));
            operation = interrupted.Create(Candidate([Server("13")], "b13"), "b13");
            await Run(interrupted, operation, new([Server("13")]));
            await installer.Downloads.ControlAsync(operation.Id, true, beforeCancel: installer.CancelAsync);
            Check(!Directory.Exists(home.PathFor(operation.BackendInstall!.Slot)) && !installer.List().Any(b => b.Build == "b13"), "cancel unpublished moved slot");
            // Failed cleanup leaves an installed backend and a resumable completed/cleanup record.
            var existing = installer.List().Single(b => b.Build == "b1");
            operation = installer.Create(Candidate([Server()]), "b1");
            using (var locked = new FileStream(home.PathFor(Path.GetDirectoryName(existing.ServerPath)!.Replace('\\','/') + "/ggml.dll"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                try { await Run(installer, operation, new([Server()])); throw new Exception("expected cleanup failure"); }
                catch (DownloadException e) { Check(e.Code == "cleanup-pending", "installed cleanup failure reported separately"); }
                Check(installer.Downloads.Status(operation.Id).State == "completed" && installer.List().Single(b => b.Id == existing.Id).ServerPath != existing.ServerPath, "installed result survives cleanup failure");
            }
            transport = new([Server()]);
            Check(await Run(installer, operation, transport) is null && transport.Calls == 0, "cleanup resume no network");
            var active = installer.List().Single(b => b.Build == "b1");
            store.Update(DocumentKind.Instance, ContractJsonContext.Default.InstanceDocument, ContractValidation.Instance,
                _ => new(1, Guid.NewGuid(), Guid.NewGuid(), active.Id, "b1", 123, "detached", "127.0.0.1", 8080,
                    true, [Guid.NewGuid()], [Guid.NewGuid()], "run/test/x.ini", "logs/test/x.log"), server: "test");
            try { installer.Create(Candidate([Server()]), "b1"); throw new Exception("expected in-use guard"); }
            catch (DownloadException e) { Check(e.Code == "backend-in-use-or-uncertain", "uncertain recorded instance blocks reinstall"); }
            File.Delete(home.PathFor("run/test/instance.json"));

            using (var pause = new CancellationTokenSource())
            {
                var pausing = new BackendInstaller(home, probe: (_, token) =>
                {
                    pause.Cancel(); token.ThrowIfCancellationRequested(); return Task.FromResult("");
                });
                operation = pausing.Create(Candidate([Server("14")], "b14"), "b14");
                var paused = await pausing.Downloads.RunAsync(operation.Id, new Transport([Server("14")]),
                    cancellation: pause.Token, finalize: pausing.FinalizeAsync);
                Check(paused?.State == "paused" && !installer.List().Any(b => b.Build == "b14"), "consumer cancellation pauses without publishing");
                transport = new([Server("14")]);
                Check(await Run(installer, operation, transport) is null && transport.Calls == 0, "paused validation resumes fixed bytes");
            }

            // CLI local paths: no source or hardware probes, defaults resolve registered data.
            Environment.SetEnvironmentVariable("COQUENA_HOME", root);
            using var output = new StringWriter(); using var error = new StringWriter();
            Check(Cli.Run(["backend", "list"], output, error) == 0 && output.ToString().Contains("b7"), "CLI backend list local");
            Check(Cli.Run(["backend", "use", "cpu:latest"], output, error) == 0 && installer.Use(null)?.Kind == "latest", "CLI use persisted");
            Check(Cli.Run(["backend", "use", "cpu:b999"], output, error) == 1 && installer.Use(null)?.Kind == "latest", "absent use never changes default");
            if (executable is not null)
            {
                // Native runner is a tiny valid process fixture; checks actual bounded --version invocation.
                var version = await BackendInstaller.ProbeAsync(Environment.ProcessPath!, CancellationToken.None);
                Check(version.Contains("version: 1 (abcdef)"), "actual native process version probe");
                byte[] binary = File.ReadAllBytes(Environment.ProcessPath!);
                var nativeHome = new Home(Path.Combine(root, "native"));
                var native = new BackendInstaller(nativeHome);
                byte[] zip = Zip(("llama-server.exe", binary));
                operation = native.Create(Candidate([zip]), "latest");
                Check(await Run(native, operation, new([zip])) is null, "native extracted executable validated and published");
                operation = native.Create(Candidate([Server("2")], "b2"), "b2");
                Check((await Run(native, operation, new([Server("2")])))?.Diagnostic == "version-start-failed", "non-executable fails actual native probe");
                await native.Downloads.ControlAsync(operation.Id, true, beforeCancel: native.CancelAsync);
                var nonzero = Zip(("llama-server.exe", File.ReadAllBytes(executable)));
                operation = native.Create(Candidate([nonzero], "b3"), "b3");
                Check((await Run(native, operation, new([nonzero])))?.Diagnostic == "version-exit-failed", "nonzero exit fails actual native probe");
                await native.Downloads.ControlAsync(operation.Id, true, beforeCancel: native.CancelAsync);
            }
            Console.WriteLine($"OK: {checks} comprobaciones de instalación/reinstalación de backends.");
        }
        finally
        {
            Environment.SetEnvironmentVariable("COQUENA_HOME", oldHome);
            // Resolved literal temp root, generated above; never a catalog-controlled deletion.
            Directory.Delete(root, true);
        }
    }
}
