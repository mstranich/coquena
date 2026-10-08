using System.Net;
using System.Text;
using Coquena.Models;

namespace Coquena.Tests;

internal static class ModelSourceTests
{
    private const string Commit = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string Sha = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private static int checks;
    private static void Check(bool value, string name)
    { checks++; if (!value) throw new Exception($"HF: {name} ({checks})"); }
    private static string FileJson(string path) => $$"""
        {"type":"file","path":"{{path}}","size":123,"oid":"{{Commit}}","lfs":{"oid":"{{Sha}}","size":123,"pointerSize":134},"xetHash":"{{Sha}}"}
        """;
    private static HttpResponseMessage Json(string text) => new(HttpStatusCode.OK)
    { Content = new StringContent(text, Encoding.UTF8, "application/json") };
    private static RepositoryFile File(string path) => new(path, 123, Commit, Sha, Sha);
    private static RepositoryRevision Revision => new("owner/repo", "main", Commit);
    private sealed class Fixture(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<string> Calls { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            cancellation.ThrowIfCancellationRequested();
            Check(request.Method == HttpMethod.Get && request.RequestUri!.Host == "huggingface.co", "public metadata GET");
            Check(!request.Headers.Contains("Authorization"), "no credentials");
            Calls.Add(request.RequestUri!.PathAndQuery);
            return Task.FromResult(respond(request));
        }
    }
    private sealed class Interaction(params int?[] choices) : IModelSelectionInteraction
    {
        public bool IsInteractive { get; set; } = true;
        public List<string[]> Menus { get; } = [];
        public List<string> Messages { get; } = [];
        public void Show(string text) => Messages.Add(text);
        public Task<int?> ChooseAsync(IReadOnlyList<string> options, CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); Menus.Add(options.ToArray()); return Task.FromResult(choices[Menus.Count - 1]); }
    }
    private static async Task Fails(Func<Task> action, string code)
    {
        try { await action(); throw new Exception("Expected " + code); }
        catch (ModelSourceException e) { Check(e.Code == code, $"diagnostic {code}, received {e.Code}"); }
    }
    private static HttpResponseMessage Standard(HttpRequestMessage request)
    {
        var path = request.RequestUri!.AbsolutePath;
        if (path.EndsWith("/revision/main")) return Json($$"""{"id":"owner/repo","sha":"{{Commit}}"}""");
        if (path.Contains("/tree/")) return Json("[" + string.Join(',', new[] { FileJson("Model-Q4_K_M.gguf"), FileJson("mmproj-F16.gguf") }) + "]");
        if (path.EndsWith("/README.md")) return Json("README del commit");
        throw new Exception("Unexpected request " + path);
    }

    public static async Task Run()
    {
        using var fixture = new Fixture(Standard);
        using var client = new HttpClient(fixture);
        var store = new HuggingFaceStore(client);
        var resolved = await store.ResolveAsync("owner/repo");
        Check(resolved == Revision, "resolve branch to commit");
        var inventory = await store.InventoryAsync(resolved);
        Check(inventory.Files.Length == 2 && inventory.Files[0].LfsSha256 == Sha && inventory.Files[0].XetHash == Sha,
            "identity domains preserved");
        Check(store.File(inventory, "Model-Q4_K_M.gguf").ToRemoteFile().Identity!.Domain == "file-bytes", "LFS byte SHA-256");
        Check((File("one.gguf") with { LfsSha256 = null, XetHash = null }).ToRemoteFile().Identity!.Domain == "git-blob", "Git OID not weights SHA");
        Check((File("one.gguf") with { LfsSha256 = null }).ToRemoteFile().Identity!.Domain == "xet-file", "Xet fallback never interpreted as SHA-256");
        Check(store.FileAddress(resolved, "sub/Model Q4.gguf").AbsoluteUri.EndsWith("/sub/Model%20Q4.gguf"), "stable escaped address");
        await Fails(() => Task.FromResult(store.File(inventory, "missing.gguf")), "file-not-found");
        var candidates = ModelSelection.Discover(inventory);
        Check(candidates.Variants.Length == 2 && candidates.Variants.Count(v => v.CompanionType is null) == 1, "companions distinct");
        var shards = new RepositoryInventory(Revision,
            [File("IQ1_S-XL/model-IQ1_S-XL-00002-of-00002.gguf"), File("IQ1_S-XL/model-IQ1_S-XL-00001-of-00002.gguf")]);
        var divided = ModelSelection.Discover(shards);
        Check(divided.Variants.Length == 1 && divided.Variants[0].Quantization == "IQ1_S-XL" &&
            divided.Variants[0].Files[0].Path.Contains("00001"), "full label and ordered complete shards");
        Check(ModelSelection.Discover(shards with { Files = [shards.Files[0]] }).Variants.Length == 0, "missing shard excluded");
        Check(ModelSelection.Discover(new(Revision, [File("model-Q4-00001-of-00002.gguf"), File("model-Q4-00002-of-00003.gguf")])).Variants.Length == 0,
            "inconsistent shard totals excluded");
        var ambiguous = ModelSelection.Discover(new(Revision, [File("a/model-Q4_K_M.gguf"), File("b/model-Q4_K_M.gguf")]));
        Check(ambiguous.Variants.Length == 2 && ambiguous.Variants[0].Label != ambiguous.Variants[1].Label, "ambiguity preserved by path");
        Check(ModelSelection.Discover(new(Revision, [File("model.gguf")])).Diagnostics.Length == 1, "unconventional name diagnosed not invalid GGUF");
        Check(ModelSelection.Discover(new(Revision, [File("model-Q4-1-of-2.gguf")])).Variants.Length == 0, "nonstandard shards not treated as single file");
        Check(ModelSelection.Discover(new(Revision, [File("model-MXFP4_MOE.gguf"), File("model-NVFP4.gguf")])).Variants.Length == 2,
            "current microscaling labels retained");
        Check(ModelRequest.Parse("owner/repo#mmproj:BF16") == new ModelRequest("owner/repo", "BF16", "mmproj"), "companion selector");
        foreach (var selector in new[] { "repo", "owner/repo:", "owner/repo#", "owner/repo#bad:Q4", "owner/repo:Q4:Q5" })
            await Fails(() => Task.FromResult(ModelRequest.Parse(selector)), selector == "repo" ? "invalid-repository" : "invalid-selector");
        foreach (var path in new[] { "../one.gguf", "/one.gguf", "a\\one.gguf", "a/NUL.gguf", "a/a:one.gguf", "a/one.gguf." })
            await Fails(() => Task.FromResult(store.FileAddress(resolved, path)), "unsafe-path");

        var menu = new ModelSelectionMenu(store);
        var choose = new Interaction(0);
        var selection = await menu.SelectAsync(ModelRequest.Parse("owner/repo"), choose);
        Check(selection!.Name == "owner/repo:Q4_K_M" && selection.Selection.Commit == Commit && selection.Selection.Files.Length == 1,
            "main selection pinned and companion not enqueued");
        Check(choose.Menus.Count == 1 && choose.Menus[0][0].StartsWith("Descargar "), "choose starts with no second confirmation");
        Check(!fixture.Calls.Any(c => c.Contains("/raw/")), "README only on demand");
        var readme = new Interaction(1, 0);
        await menu.SelectAsync(ModelRequest.Parse("owner/repo:Q4_K_M"), readme);
        Check(readme.Menus.Count == 2 && readme.Menus[0].SequenceEqual(readme.Menus[1]) && readme.Messages.Contains("README del commit"), "README returns same menu");
        Check(fixture.Calls.Any(c => c == $"/owner/repo/raw/{Commit}/README.md"), "README pinned commit");
        var cancel = new Interaction(2);
        Check(await menu.SelectAsync(ModelRequest.Parse("owner/repo"), cancel) is null, "cancel returns no selection");
        Check(await menu.SelectAsync(ModelRequest.Parse("owner/repo"), new Interaction((int?)null)) is null, "EOF cancels");
        var invalidChoice = new Interaction(-1, 0);
        Check(await menu.SelectAsync(ModelRequest.Parse("owner/repo"), invalidChoice) is not null && invalidChoice.Menus.Count == 2,
            "invalid menu choice never implicitly starts");
        var companion = new Interaction(0);
        var aux = await menu.SelectAsync(ModelRequest.Parse("owner/repo#mmproj"), companion);
        Check(aux!.Name == "owner/repo#mmproj:F16" && aux.Selection.Files[0].Path == "mmproj-F16.gguf", "independent companion selection");
        var absent = new Interaction(1);
        Check(await menu.SelectAsync(ModelRequest.Parse("owner/repo:Q8_0"), absent) is null && absent.Menus[0].Length == 2 &&
            absent.Messages.Any(m => m.Contains("inexistente")), "missing quant not substituted");
        await Fails(() => menu.SelectAsync(ModelRequest.Parse("owner/repo"), new Interaction { IsInteractive = false }), "interaction-required");
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        try { await store.InventoryAsync(Revision, cancelled.Token); throw new Exception("Expected cancellation"); }
        catch (OperationCanceledException) { Check(true, "caller cancellation preserved"); }

        var pageCount = 0;
        using var paged = new Fixture(_ =>
        {
            var response = Json("[" + FileJson(++pageCount == 1 ? "a-Q4.gguf" : "b-Q8.gguf") + "]");
            if (pageCount == 1) response.Headers.Add("Link", $"<https://huggingface.co/api/models/owner/repo/tree/{Commit}?recursive=true&expand=false&cursor=next>; rel=\"next\"");
            return response;
        });
        using var pc = new HttpClient(paged);
        Check((await new HuggingFaceStore(pc).InventoryAsync(Revision)).Files.Length == 2 && pageCount == 2, "complete pagination");

        foreach (var link in new[] {
            $"<https://evil.example/api/models/owner/repo/tree/{Commit}?recursive=true&expand=false>; rel=\"next\"",
            "<https://huggingface.co/api/models/owner/repo/tree/main?recursive=true&expand=false>; rel=\"next\"",
            $"<https://huggingface.co/api/models/owner/repo/tree/{Commit}?recursive=false&expand=false>; rel=\"next\"",
            $"<https://huggingface.co/api/models/owner/repo/tree/{Commit}?recursive=true&recursive=false&expand=false>; rel=\"next\"",
            "broken" })
        {
            using var bad = new Fixture(_ => { var r = Json("[]"); r.Headers.Add("Link", link); return r; });
            using var bc = new HttpClient(bad);
            await Fails(() => new HuggingFaceStore(bc).InventoryAsync(Revision), "invalid-pagination");
            Check(bad.Calls.Count == 1, "unsafe next not requested");
        }
        using var cycle = new Fixture(_ => { var r = Json("[]"); r.Headers.Add("Link", $"<https://huggingface.co/api/models/owner/repo/tree/{Commit}?recursive=true&expand=false&limit=1000>; rel=\"next\""); return r; });
        using var cc = new HttpClient(cycle);
        await Fails(() => new HuggingFaceStore(cc).InventoryAsync(Revision), "inventory-incomplete");

        foreach (var invalid in new[] { "{}", "[" + FileJson("a-Q4.gguf") + "," + FileJson("a-Q4.gguf") + "]",
            "[" + FileJson("a-Q4.gguf").Replace("\"size\":123", "\"size\":-1") + "]", "[{}]", "not-json" })
        {
            using var bad = new Fixture(_ => Json(invalid)); using var bc = new HttpClient(bad);
            await Fails(() => new HuggingFaceStore(bc).InventoryAsync(Revision), "invalid-metadata");
        }
        using var missing = new Fixture(_ => { var r = new HttpResponseMessage(HttpStatusCode.NotFound); r.Headers.Add("X-Error-Code", "RevisionNotFound"); return r; });
        using var mc = new HttpClient(missing);
        await Fails(() => new HuggingFaceStore(mc).ResolveAsync("owner/repo", "missing"), "revision-not-found");
        var attempts = 0;
        using var rate = new Fixture(_ => { attempts++; var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests); r.Headers.RetryAfter = new(TimeSpan.Zero); return r; });
        using var rc = new HttpClient(rate);
        await Fails(() => new HuggingFaceStore(rc).ResolveAsync("owner/repo"), "remote-rate-limit");
        Check(attempts == 3, "bounded rate retries");
        using var longWait = new Fixture(_ => { var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests); r.Headers.RetryAfter = new(TimeSpan.FromSeconds(30)); return r; });
        using var lc = new HttpClient(longWait);
        await Fails(() => new HuggingFaceStore(lc).ResolveAsync("owner/repo"), "remote-rate-limit");
        Check(longWait.Calls.Count == 1, "long wait never shortened");
        using var hfLimit = new Fixture(_ => { var r = new HttpResponseMessage(HttpStatusCode.TooManyRequests); r.Headers.Add("RateLimit", "\"api\";r=0;t=120"); r.Headers.RetryAfter = new(TimeSpan.Zero); return r; });
        using var hflc = new HttpClient(hfLimit);
        await Fails(() => new HuggingFaceStore(hflc).ResolveAsync("owner/repo"), "remote-rate-limit");
        Check(hfLimit.Calls.Count == 1, "HF reset takes precedence over shorter retry-after");
        var retries = 0;
        using var transient = new Fixture(r => { if (++retries > 1) return Standard(r); var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable); response.Headers.RetryAfter = new(TimeSpan.Zero); return response; });
        using var tc = new HttpClient(transient);
        Check(await new HuggingFaceStore(tc).ResolveAsync("owner/repo") == Revision && retries == 2, "transient retry succeeds");
        using var huge = new Fixture(_ => Json(new string('x', HuggingFaceStore.MaxMetadataBytes + 1)));
        using var hc = new HttpClient(huge);
        await Fails(() => new HuggingFaceStore(hc).InventoryAsync(Revision), "metadata-limit");
        using var access = new Fixture(_ => new(HttpStatusCode.Forbidden)); using var ac = new HttpClient(access);
        await Fails(() => new HuggingFaceStore(ac).ResolveAsync("owner/repo"), "remote-access");
        using var ambiguousSource = new Fixture(r => r.RequestUri!.AbsolutePath.Contains("/tree/") ?
            Json("[" + FileJson("a/model-Q4_K_M.gguf") + "," + FileJson("b/model-Q4_K_M.gguf") + "]") : Standard(r));
        using var asc = new HttpClient(ambiguousSource);
        var secondVariant = new Interaction(1);
        var explicitVariant = await new ModelSelectionMenu(new(asc)).SelectAsync(ModelRequest.Parse("owner/repo:Q4_K_M"), secondVariant);
        Check(secondVariant.Menus[0].Length == 4 && explicitVariant!.Selection.Files[0].Path.StartsWith("b/"), "ambiguous quant explicit paths");
        using var missingReadme = new Fixture(r => r.RequestUri!.AbsolutePath.EndsWith("README.md") ? new(HttpStatusCode.NotFound) : Standard(r));
        using var mrc = new HttpClient(missingReadme);
        var unavailableReadme = new Interaction(1, 0);
        Check(await new ModelSelectionMenu(new(mrc)).SelectAsync(ModelRequest.Parse("owner/repo"), unavailableReadme) is not null &&
            unavailableReadme.Menus.Count == 2 && unavailableReadme.Messages.Any(m => m.StartsWith("README:")), "README failure nonfatal");
        using var empty = new Fixture(r => r.RequestUri!.AbsolutePath.Contains("/tree/") ? Json("[]") : Standard(r));
        using var ec = new HttpClient(empty);
        var noGguf = new Interaction(1);
        Check(await new ModelSelectionMenu(new(ec)).SelectAsync(ModelRequest.Parse("owner/repo"), noGguf) is null &&
            noGguf.Menus[0].SequenceEqual(new[] { "Mostrar README", "Cancelar" }), "no GGUF only readme/cancel");
        Check(!fixture.Calls.Any(c => c.Contains("/resolve/")), "no weight GET or transfer calls");
        Console.WriteLine($"OK: {checks} comprobaciones HF/selección; sin pesos, GUID ni persistencia.");
    }

    // Opt-in metadata-only real provider exercise, including in the published native runner.
    public static async Task Live(string repository)
    {
        using var client = HuggingFaceStore.CreateClient();
        var store = new HuggingFaceStore(client);
        var revision = await store.ResolveAsync(repository);
        var inventory = await store.InventoryAsync(revision);
        var candidates = ModelSelection.Discover(inventory);
        Console.WriteLine($"{revision.Repository} @ {revision.Commit}: {inventory.Files.Length} archivos");
        foreach (var candidate in candidates.Variants)
            Console.WriteLine($"{candidate.Name(revision.Repository)}: {candidate.Files.Length} parte(s)");
        foreach (var diagnostic in candidates.Diagnostics) Console.WriteLine(diagnostic);
        Console.WriteLine($"README del commit: {(await store.ReadmeAsync(revision)).Length} caracteres");
        var menu = new Interaction(0);
        var selected = await new ModelSelectionMenu(store).SelectAsync(ModelRequest.Parse(repository), menu);
        if (selected is null || selected.Selection.Files.Length == 0) throw new Exception("Live selection missing");
        Console.WriteLine($"Selección sin segunda confirmación: {selected.Name} @ {selected.Selection.Commit}; cero descarga de pesos.");
    }
}
