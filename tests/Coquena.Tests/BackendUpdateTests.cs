using System.Diagnostics;
using System.Net;
using System.Text;
using Coquena.Backends;
using Coquena.Contracts;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class BackendUpdateTests
{
    private sealed class Fixture(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            if (request.Method != HttpMethod.Get || request.RequestUri!.Host != "api.github.com" ||
                request.RequestUri.AbsolutePath != "/repos/ggml-org/llama.cpp/releases")
                throw new Exception("Update must only query official release metadata");
            Calls++;
            return Task.FromResult(response());
        }
    }
    private static HttpResponseMessage Page(string build, params string[] variants)
    {
        var names = variants.Select(v => $"llama-{build}-bin-win-{v}.zip").ToList();
        names.AddRange(variants.Where(v => v.StartsWith("cuda-", StringComparison.Ordinal)).Select(v => $"cudart-llama-bin-win-{v}.zip"));
        var assets = string.Join(",", names.Select(n => $$"""
            {"name":"{{n}}","size":123,"state":"uploaded","browser_download_url":"https://github.com/ggml-org/llama.cpp/releases/download/{{build}}/{{n}}"}
            """));
        return new(HttpStatusCode.OK) { Content = new StringContent($$"""
            [{"tag_name":"{{build}}","draft":false,"prerelease":true,"assets":[{{assets}}]}]
            """, Encoding.UTF8, "application/json") };
    }
    private static readonly HardwareContext Hardware = new("win-x64", "fixture", null, [], null, [],
        new("unknown", "fixture"), new("unknown", "fixture"));

    public static async Task Run(string? executable)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-update-" + Guid.NewGuid());
        var home = new Home(root); var store = new LocalStore(home); var json = ContractJsonContext.Default;
        var checks = 0;
        void Check(bool value, string reason) { checks++; if (!value) throw new Exception("Backend update: " + reason); }
        BackendRecord Record(string variant, string build = "b9", bool latest = true, bool pinned = false) =>
            new(Guid.NewGuid(), build, "version fixture", "ggml", "ggml-org/llama.cpp", variant,
                variant.Split('@')[0], "win-x64", $"backends/{Guid.NewGuid():D}/server.exe", latest, pinned);
        void Save(params BackendRecord[] records) => store.Update(DocumentKind.Backends, json.BackendsDocument,
            ContractValidation.Backends, _ => new(1, records));
        BackendUpdateObservation[] Read() => store.Read(DocumentKind.BackendUpdates, json.BackendUpdatesDocument,
            ContractValidation.BackendUpdates)!.Updates;
        async Task<BackendUpdateResult[]> Query(string? selector, Func<HttpResponseMessage> response, LocalStore? storage = null)
        {
            using var handler = new Fixture(response); using var client = new HttpClient(handler);
            var result = await new BackendUpdater(home, storage).UpdateAsync(selector, Hardware, new(client));
            Check(handler.Calls == result.Length, "one bounded metadata query per registered filter");
            return result;
        }
        async Task Reject(string selector, string code)
        {
            using var handler = new Fixture(() => throw new Exception("Rejected selector reached network"));
            using var client = new HttpClient(handler);
            try { await new BackendUpdater(home).UpdateAsync(selector, Hardware, new(client)); throw new Exception("Expected rejection"); }
            catch (BackendException e) { Check(e.Code == code && handler.Calls == 0, "reject selector before remote call: " + selector); }
        }
        try
        {
            var cpu = Record("cpu", pinned: true); var cuda = Record("cuda@12"); var vulkan = Record("vulkan", latest: false, pinned: true);
            var externalFile = Path.Combine(root, "external.exe"); home.Ensure(); File.WriteAllText(externalFile, "external fixture");
            var external = Record("cpu", latest: false) with { Name = "external", Ownership = "external", ServerPath = externalFile };
            Save(cpu, cuda, vulkan, external);
            store.Update(DocumentKind.Config, json.ConfigDocument, ContractValidation.Config, _ => new(1, null,
                new("fixed", cpu.Id), new(5)));
            Directory.CreateDirectory(Path.GetDirectoryName(home.PathFor(cpu.ServerPath))!);
            File.WriteAllText(home.PathFor(cpu.ServerPath), "installed fixture");
            var catalogBefore = File.ReadAllText(home.PathFor("catalogs/backends.json"));
            var configBefore = File.ReadAllText(home.PathFor("config.json"));
            var results = await Query(null, () => Page("b10", "cpu-x64", "cuda-12.4-x64", "vulkan-x64"));
            Check(results.Length == 2 && results.All(r => r.Persisted && r.Observation?.State == "available"), "latest queried despite shared build pin, pinned-only/external excluded");
            Check(File.ReadAllText(home.PathFor("catalogs/backends.json")) == catalogBefore &&
                File.ReadAllText(home.PathFor("config.json")) == configBefore, "installed records, flags and default unchanged");
            Check(File.ReadAllText(home.PathFor(cpu.ServerPath)) == "installed fixture" && File.ReadAllText(externalFile) == "external fixture",
                "managed and external bytes untouched");
            Check(!Directory.Exists(home.PathFor("downloads")) || !Directory.EnumerateFileSystemEntries(home.PathFor("downloads")).Any(), "no transfer operations");
            var observations = Read();
            var cudaUpdate = observations.Single(o => o.Variant == "cuda@12");
            Check(cudaUpdate.BackendId == cuda.Id && cudaUpdate.InstalledBuild == "b9" && cudaUpdate.Platform == "win-x64" &&
                cudaUpdate.Candidate?.Build == "b10" && cudaUpdate.Candidate.Artifacts.Length == 2 &&
                cudaUpdate.Candidate.Compatibility == "unknown", "fixed filter, baseline and complete CUDA bundle saved with evidence");
            Check(observations.All(o => o.CheckedAt.Offset == TimeSpan.Zero && o.CheckedAt != default), "UTC query dates");
            var bytes = File.ReadAllText(home.PathFor("catalogs/backend-updates.json"));
            Check(!bytes.Contains("https:") && !bytes.Contains("sha256"), "no URLs or backend digests persisted");
            Check(new BackendUpdater(home).Observation(cpu)?.Candidate?.Build == "b10", "restart reloads candidate");

            await Query("oficial/cpu:latest", () => Page("b9", "cpu-x64"));
            Check(Read().Single(o => o.Variant == "cpu").State == "unchanged" && Read().Single(o => o.Variant == "cpu").Candidate is null,
                "same build clears obsolete candidate");
            Check(Read().Single(o => o.Variant == "cuda@12").Candidate?.Build == "b10", "selected query preserves other filters");
            await Query("cpu:latest", () => Page("b8", "cpu-x64"));
            Check(Read().Single(o => o.Variant == "cpu").Candidate is null, "older remote release never proposes downgrade");
            await Query("ggml/cpu:latest", () => Page("b10", "cpu-x64"));
            await Query("cpu:latest", () => throw new HttpRequestException("fixture"));
            Check(Read().Single(o => o.Variant == "cpu") is { State: "failed", Candidate: null, Diagnostic: "remote-unavailable" }, "failed refresh clears applicable candidate and records failure");
            var call = 0;
            results = await Query(null, () => ++call == 1 ? new(HttpStatusCode.Forbidden) : Page("b11", "cuda-12.4-x64"));
            Check(results.Length == 2 && results[0].Observation?.State == "failed" && results[1].Observation?.Candidate?.Build == "b11",
                "one failed filter does not prevent another update");
            await Query("cpu:latest", () => new(HttpStatusCode.OK) { Content = new StringContent("invalid JSON") });
            Check(Read().Single(o => o.Variant == "cpu").Diagnostic == "invalid-metadata", "malformed metadata diagnosed");
            await Query("cpu:latest", () => Page("b1", "cpu-arm64"));
            Check(Read().Single(o => o.Variant == "cpu") is { State: "failed", Candidate: null, Diagnostic: "no-candidate" },
                "no artifact for registered platform is failure, not unchanged");
            await Reject("cpu:b9", "invalid-latest-selector"); await Reject("external", "invalid-latest-selector");
            await Reject("lab/cpu:latest", "invalid-latest-selector"); await Reject("cuda:latest", "invalid-latest-selector");
            await Reject("vulkan:latest", "latest-not-registered"); await Reject("rocm:latest", "invalid-latest-selector");

            var before = File.ReadAllText(home.PathFor("catalogs/backend-updates.json"));
            var fault = new LocalStore(home, stage => { if (stage == WriteStage.BeforeReplace) throw new IOException("fixture"); });
            results = await Query("cpu:latest", () => Page("b12", "cpu-x64"), fault);
            Check(results.Single() is { Persisted: false, Observation: null, Error: "update-not-saved" } &&
                File.ReadAllText(home.PathFor("catalogs/backend-updates.json")) == before, "write failure preserves previous observation without claiming saved candidate");

            results = await Query("cpu:latest", () => { Save(cpu with { Build = "b10" }, cuda, vulkan, external); return Page("b12", "cpu-x64"); });
            Check(results.Single().Error == "stale-update" && File.ReadAllText(home.PathFor("catalogs/backend-updates.json")) == before,
                "changed installed baseline rejects stale candidate");
            Check(new BackendUpdater(home).Observation(cpu with { Build = "b10" }) is null, "old observation not shown for new installed build");
            Save(cpu, cuda, vulkan, external);
            results = await Query("cpu:latest", () =>
            {
                store.Update(DocumentKind.BackendUpdates, json.BackendUpdatesDocument, ContractValidation.BackendUpdates,
                    old => old! with { Updates = old.Updates.Select(o => o.Variant == "cpu" ? o with { CheckedAt = o.CheckedAt.AddSeconds(1) } : o).ToArray() });
                return Page("b12", "cpu-x64");
            });
            Check(results.Single().Error == "stale-update", "concurrent observation cannot be overwritten by older query");
            results = await Query("cpu:latest", () => { Save(cpu with { TracksLatest = false }, cuda, vulkan, external); return Page("b12", "cpu-x64"); });
            Check(results.Single().Error == "stale-update", "removed tracking rejects stale observation");
            Save(cpu, cuda, vulkan, external);
            Save(cpu, Record("cpu"), cuda, vulkan, external);
            using (var handler = new Fixture(() => Page("b12", "cuda-12.4-x64")))
            using (var client = new HttpClient(handler))
            {
                results = await new BackendUpdater(home).UpdateAsync(null, Hardware, new(client));
                Check(results.Length == 2 && results[0].Error == "ambiguous-latest" && results[1].Observation?.Candidate?.Build == "b12" && handler.Calls == 1,
                    "ambiguous latest is isolated and other filter queried");
            }
            Save(cpu, cuda, vulkan, external);
            Save(cpu with { Repository = "https://github.com/ggml-org/llama.cpp" }, cuda, vulkan, external);
            results = await Query("cpu:latest", () => Page("b12", "cpu-x64"));
            Check(results.Single().Observation?.Candidate?.Build == "b12", "existing official repository URL recognized");
            Save(cpu, cuda, vulkan, external);

            using (var cancelled = new CancellationTokenSource())
            using (var handler = new Fixture(() => { cancelled.Cancel(); throw new OperationCanceledException(cancelled.Token); }))
            using (var client = new HttpClient(handler))
            {
                before = File.ReadAllText(home.PathFor("catalogs/backend-updates.json"));
                try { await new BackendUpdater(home).UpdateAsync("cpu:latest", Hardware, new(client), cancelled.Token); throw new Exception("Expected cancellation"); }
                catch (OperationCanceledException) { Check(File.ReadAllText(home.PathFor("catalogs/backend-updates.json")) == before, "cancellation preserves existing observation"); }
            }

            // Production CLI with injected official metadata exercises output and exit status, without real network.
            var previousHome = Environment.GetEnvironmentVariable("COQUENA_HOME");
            Environment.SetEnvironmentVariable("COQUENA_HOME", root);
            try
            {
                using var output = new StringWriter(); using var error = new StringWriter();
                using var handler = new Fixture(() => Page("b20", "cpu-x64", "cuda-12.4-x64")); using var client = new HttpClient(handler);
                Check(BackendCli.Run(["backend", "update"], output, error, () => Hardware, new(client)) == 0 &&
                    output.ToString().Contains("Candidato guardado: b20") && error.ToString().Length == 0, "CLI update all, durable candidate output and exit 0");
                output.GetStringBuilder().Clear(); error.GetStringBuilder().Clear();
                using var failed = new HttpClient(new Fixture(() => new(HttpStatusCode.ServiceUnavailable)));
                Check(BackendCli.Run(["backend", "update", "cpu:latest"], output, error, () => Hardware, new(failed)) == 1 &&
                    error.ToString().Contains("remote-status"), "CLI failed metadata query exit 1 and diagnostic");
                output.GetStringBuilder().Clear(); error.GetStringBuilder().Clear();
                Check(BackendCli.Run(["backend", "update", "cpu:latest", "extra"], output, error) == 2, "extra CLI arguments rejected");
                Check(BackendCli.Run(["backend", "update", "cpu:b9"], output, error, () => Hardware, new(client)) == 2, "CLI rejects pin selector");
                Check(BackendCli.Run(["backend", "list"], output, error, () => throw new Exception("Listing must not probe hardware")) == 0 &&
                    output.ToString().Contains("consulta=") && output.ToString().Contains("b20"), "listing reloads observations without network or hardware probe");
            }
            finally { Environment.SetEnvironmentVariable("COQUENA_HOME", previousHome); }

            // Independent native CLI sees persisted observations produced by the same AOT-compatible path.
            if (executable is not null)
            {
                (int Code, string Output) Native(params string[] args)
                {
                    var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                    var environment = start.Environment.ToArray(); start.Environment.Clear();
                    foreach (var group in environment.GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)) start.Environment[group.Key] = group.Last().Value;
                    start.Environment["COQUENA_HOME"] = home.Root;
                    foreach (var arg in args) start.ArgumentList.Add(arg);
                    using var process = Process.Start(start)!;
                    var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(15000)) { process.Kill(true); throw new Exception("Native update timeout"); }
                    return (process.ExitCode, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
                }
                var list = Native("backend", "list");
                Check(list.Code == 0 && list.Output.Contains("Candidato guardado: b20") && list.Output.Contains("remote-status"), "native CLI reads candidate and failure after restart");
                Check(Native("backend", "update", "cpu:b9").Code == 2 && Native("backend", "update", "external").Code == 2, "native pin and external update rejection");
                Check(Native("--help").Output.Contains("backend update [<seguimiento>]"), "native help includes update");
            }

            Save(vulkan, external);
            using (var handler = new Fixture(() => throw new Exception("Empty catalog queried remote")))
            using (var client = new HttpClient(handler))
                Check((await new BackendUpdater(home).UpdateAsync(null, Hardware, new(client))).Length == 0 && handler.Calls == 0,
                    "no tracking means no network, pins and externals excluded");
            // Invalid durable state is never translated to empty or repaired silently.
            File.WriteAllText(home.PathFor("catalogs/backend-updates.json"), "{}");
            using (var handler = new Fixture(() => throw new Exception("Corrupt catalog queried remote")))
            using (var client = new HttpClient(handler))
            {
                try { await new BackendUpdater(home).UpdateAsync(null, Hardware, new(client)); throw new Exception("Expected corruption rejection"); }
                catch (ContractException) { Check(handler.Calls == 0, "corrupt observations fail before remote query"); }
            }
            Console.WriteLine($"OK: {checks} comprobaciones de backend update.");
        }
        finally { Directory.Delete(root, true); }
    }
}
