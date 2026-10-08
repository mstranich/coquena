using System.Diagnostics;
using System.Net;
using System.Text;
using Coquena.Backends;

namespace Coquena.Tests;

internal static class BackendTests
{
    private static int checks;
    private static void Check(bool value, string name)
    { checks++; if (!value) throw new Exception($"Backend: {name} ({checks})"); }
    private static HardwareContext Nvidia(int driver = 13040, int sm = 86) =>
        new("win-x64", "Fixture CPU", 16UL * 1024 * 1024 * 1024, ["Fixture NVIDIA", "Fixture Intel"], driver,
            [new("Fixture NVIDIA", sm)], new("compatible", "Driver y GPU detectados"), new("compatible", "GPU Vulkan detectada"));
    private static HardwareContext Unknown() => Nvidia() with
    { CudaDriverVersion = null, CudaDevices = [], Cuda = new("unknown", "No se pudo consultar driver"), Vulkan = new("unknown", "Loader ausente") };
    private static ReleaseAsset Asset(string name, string tag) => new(name, 123,
        $"https://github.com/ggml-org/llama.cpp/releases/download/{tag}/{name}");
    private static OfficialRelease Release(string tag, params string[] flavors)
    {
        List<ReleaseAsset> assets = [];
        foreach (var flavor in flavors)
        {
            assets.Add(Asset($"llama-{tag}-bin-win-{flavor}.zip", tag));
            if (flavor.StartsWith("cuda-", StringComparison.Ordinal)) assets.Add(Asset($"cudart-llama-bin-win-{flavor}.zip", tag));
        }
        return new(tag, assets.ToArray());
    }
    private static void Fails(Action action, string code)
    {
        try { action(); throw new Exception($"Expected {code}"); }
        catch (BackendException e) { Check(e.Code == code, $"diagnostic {code}, received {e.Code}"); }
    }
    private static string Json(OfficialRelease release) => $$"""
        {"tag_name":"{{release.Tag}}","draft":false,"prerelease":false,"assets":[{{string.Join(",", release.Assets.Select(a => $$"""
        {"name":"{{a.Name}}","size":{{a.Size}},"state":"uploaded","browser_download_url":"{{a.Url}}","digest":"sha256:ignored-for-backends"}
        """))}}]}
        """;

    private sealed class Fixture(Func<string, HttpResponseMessage> response) : HttpMessageHandler
    {
        public List<string> Calls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            Check(request.Method == HttpMethod.Get && request.RequestUri!.Host == "api.github.com", "metadata GET only");
            Check(request.Headers.UserAgent.Count > 0, "GitHub user agent");
            var path = request.RequestUri!.PathAndQuery;
            Calls.Add(path);
            return Task.FromResult(response(path));
        }
    }
    private static HttpResponseMessage Ok(OfficialRelease release) => new(HttpStatusCode.OK)
    { Content = new StringContent(Json(release), Encoding.UTF8, "application/json") };
    private static HttpResponseMessage Page(params OfficialRelease[] releases) => new(HttpStatusCode.OK)
    {
        Content = new StringContent("[{\"tag_name\":\"v0.6.0\",\"draft\":false,\"assets\":[]}," +
            string.Join(",", releases.Select(r => Json(r).Replace("\"prerelease\":false", "\"prerelease\":true"))) + "]",
            Encoding.UTF8, "application/json")
    };

    public static async Task Run(string? executable)
    {
        var hardware = Nvidia();
        var newerCpu = Release("b10010", "cpu-x64", "cpu-arm64", "rocm-10.0-x64");
        var older = Release("b10009", "cpu-x64", "cuda-12.4-x64", "cuda-13.4-x64", "vulkan-x64");
        var resolution = BackendCatalog.Resolve([older, newerCpu], hardware);
        Check(resolution.Selected?.Release == "b10010" && resolution.Selected.Variant == "cpu", "build priority before hardware, numeric not lexical");
        Check(resolution.Alternatives.All(c => !c.Variant.StartsWith("rocm")), "exclude unsupported families");
        Check(BackendCatalog.Resolve([newerCpu, older], hardware, "cuda@12").Selected?.Release == "b10009", "latest constrained by CUDA filter");
        Check(BackendCatalog.Resolve([older], hardware).Selected?.Variant == "cuda@13", "CUDA highest compatible major");
        Check(BackendCatalog.Resolve([older], Nvidia(12040)).Selected?.Variant == "cuda@12", "exclude incompatible newer CUDA");
        Check(BackendCatalog.Resolve([older], Unknown()).Selected?.Variant == "cpu", "unknown acceleration CPU fallback");
        var incompatible = hardware with { Cuda = new("incompatible", "No CUDA"), Vulkan = new("incompatible", "No Vulkan") };
        Check(BackendCatalog.Resolve([older], incompatible).Selected?.Variant == "cpu", "incompatible acceleration CPU fallback");
        Check(BackendCatalog.Resolve([older], Unknown(), "cuda@13").Selected?.Compatibility == "unknown", "explicit unknown preserved");
        Check(BackendCatalog.Resolve([older], incompatible, "vulkan").Selected?.Compatibility == "incompatible", "explicit incompatible preserved");
        Check(BackendCatalog.Resolve([older], hardware, "cuda@12", "b10010").Selected is null, "explicit build no substitution");
        Check(BackendCatalog.Resolve([older], hardware, "cuda@12", "b10009").Selected?.Variant == "cuda@12", "CUDA major not llama build");
        Check(BackendCatalog.Resolve([Release("b10011", "cpu-arm64", "cuda-13.4-arm64")], hardware, "cpu").Selected is null, "explicit wrong platform rejected");
        Check(BackendCatalog.Resolve([Release("b10011", "cpu-arm64")], hardware with { Platform = "linux-x64" }, "cpu").Selected is null, "different OS rejected");
        Check(BackendCatalog.Resolve([older], Nvidia(13040, 61)).Selected?.Variant == "cuda@12", "CUDA13 unsupported SM excluded");
        Check(BackendCatalog.Resolve([older], Nvidia(13020), "cuda@13").Selected?.Compatibility == "unknown", "minor compatibility not presumed");
        Check(BackendCatalog.Resolve([older], Nvidia(13040, 120), "cuda@12").Selected?.Compatibility == "unknown", "Blackwell older toolkit PTX coverage not presumed");
        Check(BackendCatalog.Resolve([Release("b1", "cuda-14.0-x64")], hardware, "cuda@14").Selected?.Compatibility == "unknown", "future CUDA diagnostic");
        Check(BackendCatalog.Resolve([older], Nvidia(13040, 130), "cuda@13").Selected?.Compatibility == "unknown", "future GPU unknown");
        var noRuntime = older with { Assets = older.Assets.Where(a => !a.Name.StartsWith("cudart")).ToArray() };
        Check(BackendCatalog.Resolve([noRuntime], hardware).Selected?.Variant == "vulkan", "incomplete CUDA bundle excluded");
        Check(BackendCatalog.Resolve([noRuntime], hardware, "cuda@12").Selected is null, "explicit incomplete bundle not selected");
        var tied = Release("b1", "cuda-12.4-x64", "cuda-12.9-x64");
        Check(BackendCatalog.Resolve([tied], hardware, "cuda@12").Selected?.ToolkitVersion == 12090, "same-major highest minor deterministic");
        var pinnedRuntime = older with { Assets = older.Assets.Select(a => a.Name.StartsWith("cudart") ? Asset(a.Name.Replace("cudart-llama-", "cudart-llama-b10009-"), "b10009") : a).ToArray() };
        Check(BackendCatalog.Resolve([pinnedRuntime], hardware, "cuda@12").Selected?.Artifacts.Length == 2, "versioned cudart name supported");
        Check(BackendCatalog.Resolve([older], hardware, "cuda@12").Selected?.Artifacts.All(a => a.Url.Contains("/b10009/")) == true, "dependency pinned same release");
        foreach (var invalid in new[] { "rocm", "cuda", "cuda@012", "cuda@-1", "cpu:b10", "lab/cpu", "ggml/cpu", "cuda@0" })
            Fails(() => BackendCatalog.Resolve([older], hardware, invalid), "invalid-family");
        foreach (var invalid in new[] { "12", "b0", "b01", "b-1", "b999999999999999999999", "../latest" })
            Fails(() => BackendCatalog.BuildNumber(invalid), "invalid-release");
        Fails(() => BackendCatalog.Resolve([older with { Assets = [older.Assets[0], older.Assets[0]] }], hardware), "invalid-metadata");
        Fails(() => BackendCatalog.Resolve([Release("b1", "cuda-999999999999999999.4-x64")], hardware), "invalid-metadata");

        var json = Json(older);
        Check(OfficialBackendSource.Parse(Encoding.UTF8.GetBytes(json)).Assets.Length == older.Assets.Length, "metadata parsing fixture");
        foreach (var malformed in new[] { "{}", "null", "{", json.Replace("\"size\":123", "\"size\":-1"),
            json.Replace("\"draft\":false", "\"draft\":true"), json.Replace("\"draft\":false", "\"draft\":false,\"draft\":false"),
            json.Replace("https://github.com/ggml-org/llama.cpp", "https://evil.example/ggml-org/llama.cpp"), json.Replace("\"size\":123", "\"size\":\"123\"") })
            Fails(() => OfficialBackendSource.Parse(Encoding.UTF8.GetBytes(malformed)), "invalid-metadata");

        using (var fixture = new Fixture(path => path.Contains("?per_page") ? Page(newerCpu) : path.EndsWith("/tags/b10009") ? Ok(older) : new(HttpStatusCode.NotFound)))
        using (var client = new HttpClient(fixture))
        {
            var source = new OfficialBackendSource(client);
            var result = await source.ResolveAsync(hardware, "cuda@12", "latest");
            Check(result.Selected?.Release == "b10009" && fixture.Calls.Count == 2, "latest scans descending builds within filter");
            fixture.Calls.Clear();
            result = await source.ResolveAsync(hardware, null, "latest");
            Check(result.Selected?.Variant == "cpu" && fixture.Calls.Count == 1, "latest CPU wins no unnecessary older calls");
            fixture.Calls.Clear();
            result = await source.ResolveAsync(hardware, "cuda@13", "b10009");
            Check(result.Selected?.Variant == "cuda@13" && fixture.Calls.Single().EndsWith("/tags/b10009"), "explicit release endpoint only");
            using var output = new StringWriter(); using var error = new StringWriter();
            var code = BackendCli.Run(["backend", "remote", "cuda@13", "--release", "b10009"], output, error, Unknown, source);
            Check(code == 0 && error.ToString().Length == 0 && output.ToString().Contains("Selección explícita") && output.ToString().Contains("[unknown]"), "CLI explicit unknown diagnostics");
            Check(output.ToString().Contains("no descarga, instala") && !output.ToString().Contains("digest"), "consultation only no backend hash");
        }
        using (var fixture = new Fixture(path => path.Contains("?per_page") ? Page(Release("b5", "cpu-arm64")) : path.EndsWith("/tags/b3") ? Ok(older with { Tag = "b3", Assets = Release("b3", "cpu-x64").Assets }) : new(HttpStatusCode.NotFound)))
        using (var client = new HttpClient(fixture))
        {
            Check((await new OfficialBackendSource(client).ResolveAsync(hardware, "cpu", "latest")).Selected?.Release == "b3", "404 gaps skipped conservatively");
        }
        using (var fixture = new Fixture(path => path.Contains("?per_page") ? Page(older, newerCpu) : throw new Exception("/latest must not be queried")))
        using (var client = new HttpClient(fixture))
        {
            Check((await new OfficialBackendSource(client).ResolveAsync(hardware, null, "latest")).Selected?.Release == "b10010", "ignore stable marker, include bNNNN prereleases, compare numeric builds");
        }
        using (var fixture = new Fixture(path => path.EndsWith("page=1")
            ? new(HttpStatusCode.OK) { Content = new StringContent("[{\"tag_name\":\"v0.6.0\",\"draft\":false,\"assets\":[]}]") }
            : Page(newerCpu)))
        using (var client = new HttpClient(fixture))
        {
            Check((await new OfficialBackendSource(client).ResolveAsync(hardware, null, "latest")).Selected?.Release == "b10010" && fixture.Calls.Count == 2, "listing pagination past non-build marker");
        }
        using (var fixture = new Fixture(_ => Page(newerCpu, newerCpu)))
        using (var client = new HttpClient(fixture))
            Fails(() => new OfficialBackendSource(client).ResolveAsync(hardware, null, "latest").GetAwaiter().GetResult(), "invalid-metadata");
        using (var fixture = new Fixture(_ => throw new HttpRequestException("fixture offline")))
        using (var client = new HttpClient(fixture))
            Fails(() => new OfficialBackendSource(client).ResolveAsync(hardware, null, "latest").GetAwaiter().GetResult(), "remote-unavailable");
        foreach (var (status, code) in new[] { (HttpStatusCode.NotFound, "release-not-found"), (HttpStatusCode.Forbidden, "remote-rate-limit"), (HttpStatusCode.TooManyRequests, "remote-rate-limit"), (HttpStatusCode.InternalServerError, "remote-status") })
        {
            using var fixture = new Fixture(_ => new(status)); using var client = new HttpClient(fixture);
            Fails(() => new OfficialBackendSource(client).ResolveAsync(hardware, null, "b1").GetAwaiter().GetResult(), code);
        }
        using (var fixture = new Fixture(path => path.Contains("?per_page") ? Page(Release("b100", "cpu-arm64")) : new(HttpStatusCode.NotFound)))
        using (var client = new HttpClient(fixture))
        {
            Fails(() => new OfficialBackendSource(client).ResolveAsync(hardware, "cuda@12", "latest").GetAwaiter().GetResult(), "discovery-limit");
            Check(fixture.Calls.Count == OfficialBackendSource.MaxBuildProbes, "discovery bounded not false no-candidate");
        }
        using (var fixture = new Fixture(_ => Ok(older)))
        using (var client = new HttpClient(fixture))
        {
            Fails(() => new OfficialBackendSource(client).ResolveAsync(hardware, null, "b1").GetAwaiter().GetResult(), "invalid-metadata");
            var oversized = new string(' ', OfficialBackendSource.MaxMetadataBytes + 1);
            using var bigFixture = new Fixture(_ => new(HttpStatusCode.OK) { Content = new StringContent(oversized) });
            using var bigClient = new HttpClient(bigFixture);
            Fails(() => new OfficialBackendSource(bigClient).ResolveAsync(hardware, null, "latest").GetAwaiter().GetResult(), "metadata-limit");
            using var cancel = new CancellationTokenSource(); cancel.Cancel();
            using var output = new StringWriter(); using var error = new StringWriter();
            Check(BackendCli.Run(["backend", "remote"], output, error, () => hardware, new(client), cancel.Token) == 1 && error.ToString().Contains("query-cancelled"), "cancellation typed");
        }
        foreach (var arguments in new string[][] { ["system", "extra"], ["backend"], ["backend", "install"], ["backend", "remote", "--json"],
            ["backend", "remote", "--release"], ["backend", "remote", "cpu", "vulkan"], ["backend", "remote", "--release", "12"],
            ["backend", "remote", "rocm"], ["backend", "remote", "--release", "b1", "--release", "b2"] })
        {
            using var output = new StringWriter(); using var error = new StringWriter();
            Check(BackendCli.Run(arguments, output, error, () => throw new Exception("Invalid arguments must not probe hardware")) == 2 && output.ToString().Length == 0, "CLI invalid arguments before effects");
        }
        using (var output = new StringWriter())
        using (var error = new StringWriter())
        {
            Check(BackendCli.Run(["system"], output, error, Unknown) == 0 && output.ToString().Contains("unknown") && output.ToString().Contains("no valida un binario"), "system missing evidence honest");
        }
        using (var fixture = new Fixture(_ => throw new Exception("Unsupported platform must not query provider")))
        using (var client = new HttpClient(fixture))
        using (var output = new StringWriter())
        using (var error = new StringWriter())
            Check(BackendCli.Run(["backend", "remote"], output, error, () => hardware with { Platform = "win-arm64" }, new(client)) == 1 &&
                fixture.Calls.Count == 0 && error.ToString().Contains("unsupported-platform"), "M1 native platform boundary before network");
        if (executable is not null)
        {
            var start = new ProcessStartInfo(executable) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            start.ArgumentList.Add("system");
            using var process = Process.Start(start) ?? throw new Exception("Native system did not start");
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(30_000)) { process.Kill(true); throw new Exception("Native system timeout"); }
            Check(process.ExitCode == 0 && (await stdout).Contains("Plataforma nativa: win-x64") && (await stderr).Length == 0, "published system process real probes");
        }
        Console.WriteLine($"OK: {checks} comprobaciones de backends (fixtures sin binarios/pesos, metadatos simulados)." );
    }
}
