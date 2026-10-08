using Coquena.Models;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Coquena.Storage;
using Coquena.Downloads;
using Coquena.Contracts;

namespace Coquena.Tests;

internal static class ResolutionTests
{
    private static int checks;
    private static void Check(bool value, string name) { checks++; if (!value) throw new Exception("015: " + name); }
    private const string Commit = "d1c082be9cf3c8a514acf63b8761f4b41935842e";
    private static RepositoryRevision Revision => new("owner/repo", "main", Commit);
    private static RepositoryFile File(string path, long size) => new(path, size, Commit, null, null);
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public int Calls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); Calls++; return Task.FromResult(respond(request)); }
    }
    private sealed class Headers(Func<RepositoryFile, GgufHeader> read) : IRemoteGgufInspector
    {
        public int Calls;
        public Task<GgufHeader> InspectAsync(RepositoryRevision revision, RepositoryFile file, CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); Calls++; return Task.FromResult(read(file)); }
    }
    private sealed class WaitingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellation)
        { await Task.Delay(Timeout.Infinite,cancellation); throw new Exception("Unreachable"); }
    }
    private sealed class ProbeStream : MemoryStream
    {
        public bool ReadCalled, Closed;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellation=default)
        { ReadCalled=true; throw new Exception("Ignored Range body must not be read"); }
        protected override void Dispose(bool disposing) { Closed=true; base.Dispose(disposing); }
    }
    private sealed class NoLengthStream(byte[] payload) : MemoryStream(payload)
    { public override bool CanSeek => false; }
    private static byte[] Header(uint? type = 2, int padding = 0, string architecture = "llama", bool badType = false)
    {
        using var memory = new MemoryStream(); using var w = new BinaryWriter(memory, Encoding.UTF8, true);
        void Text(string text) { var b = Encoding.UTF8.GetBytes(text); w.Write((ulong)b.Length); w.Write(b); }
        w.Write("GGUF"u8); w.Write(3u); w.Write(1UL); w.Write((ulong)(1 + (padding > 0 ? 1 : 0) + (type.HasValue ? 1 : 0)));
        Text("general.architecture"); w.Write(8u); Text(architecture);
        if (padding > 0) { Text("tokenizer.ggml.tokens"); w.Write(9u); w.Write(0u); w.Write((ulong)padding); w.Write(new byte[padding]); }
        if (type is { } id) { Text("general.file_type"); w.Write(badType ? 5u : 4u); w.Write(id); }
        return memory.ToArray();
    }
    private static HttpResponseMessage Range(HttpRequestMessage request, byte[] payload)
    {
        Check(request.RequestUri!.AbsolutePath.Contains("/resolve/" + Commit + "/"), "pinned range address");
        Check(request.Headers.AcceptEncoding.Single().Value == "identity" && !request.Headers.Contains("Authorization"), "safe headers");
        var r = request.Headers.Range!.Ranges.Single(); var from = r.From!.Value; var to = r.To!.Value;
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
        { Content = new ByteArrayContent(payload[(int)from..((int)to + 1)]) };
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, payload.Length);
        response.Headers.Add("X-Repo-Commit", Commit); return response;
    }
    private static async Task Fails(Func<Task> action, string code)
    {
        try { await action(); throw new Exception("Expected " + code); }
        catch (ModelSourceException e) { Check(e.Code == code, "diagnostic " + code + " received " + e.Code); }
    }
    private sealed class Interaction(params int?[] choices) : IModelSelectionInteraction
    {
        public bool IsInteractive => true;
        public List<string[]> Menus = [];
        public List<string> Messages = [];
        public void Show(string text) => Messages.Add(text);
        public Task<int?> ChooseAsync(IReadOnlyList<string> options, CancellationToken cancellation)
        { Menus.Add(options.ToArray()); return Task.FromResult(choices[Menus.Count - 1]); }
    }
    public static async Task Run()
    {
        Names();
        foreach (var name in new[] { "Model-Q4_0-it", "Model.Q4_0", "Model_q4_0", "Q4_0-instruct", "Model-Q4_0-chat" })
            Check(ModelSelection.NameQuantization(name + ".gguf") == "Q4_0", "token boundaries " + name);
        foreach (var name in new[] { "Model-Q4_01", "Model-Q4_0foo", "Model-IQ2_XXSfoo" })
            Check(ModelSelection.NameQuantization(name + ".gguf") is null, "no accidental token " + name);
        foreach (var name in new[] { "mmproj-model.gguf", "model-mmproj.gguf", "mmproj/model.gguf", "MTP/whatever.gguf" })
            Check(ModelSelection.NameRole(name) is ModelRole.Mmproj or ModelRole.Mtp, "auxiliary tokens " + name);
        Check(ModelSelection.NameRole("modelmmproj-Q4_0.gguf") == ModelRole.Principal, "no role substring");
        var h = GgufInspection.ReadHeader(new MemoryStream(Header()));
        Check(h.FileTypeId == 2, "typed UINT32 file_type");
        var f = File("whatever.gguf", 1000);
        Check(ModelSelection.Classify(Revision, f, h).PublisherLabel == "Q4_0", "arbitrary name metadata quant");
        Check(ModelSelection.Classify(Revision, File("model-IQ1_S-XL.gguf", 1000), h with { FileTypeId = 24 }).PublisherLabel == "IQ1_S-XL", "qualifier preserved");
        Check(ModelSelection.Classify(Revision, File("model-Q4_0.gguf", 1000), h with { FileTypeId = 1 }).State == EvidenceState.Conflict, "name/header conflict");
        Check(ModelSelection.Classify(Revision, f, h with { FileTypeId = uint.MaxValue }).PublisherLabel is null, "future unknown not invented");
        Check(ModelSelection.Classify(Revision, File("model-Q4_0.gguf", 1000), h with { FileTypeId = null }).PublisherLabel == "Q4_0", "optional file_type name-only");
        foreach (var label in new[] { "UD-IQ2_XXS", "UD-Q2_K_XL", "UD-Q3_K_XL", "UD-Q4_K_L", "UD-Q4_K_XL", "UD-Q5_K_XL", "UD-Q6_K_S", "UD-Q6_K_XL", "UD-Q8_K_XL" })
        {
            Check(ModelSelection.NameQuantization("Qwen3.5-35B-A3B-"+label+".gguf")==label,"publisher label preserved "+label);
            var recipe=ModelSelection.Classify(Revision,File("model-"+label+".gguf",1000),h with {FileTypeId=7});
            Check(recipe.PublisherLabel==label && recipe.BaseQuantization=="Q8_0" && recipe.State==EvidenceState.Candidate,"recipe not inferred from predominant ftype "+label);
        }
        var expected = new Dictionary<uint,string> { [0]="F32",[1]="F16",[2]="Q4_0",[3]="Q4_1",[7]="Q8_0",[8]="Q5_0",[9]="Q5_1",[10]="Q2_K",[11]="Q3_K_S",[12]="Q3_K_M",[13]="Q3_K_L",[14]="Q4_K_S",[15]="Q4_K_M",[16]="Q5_K_S",[17]="Q5_K_M",[18]="Q6_K",[19]="IQ2_XXS",[20]="IQ2_XS",[21]="Q2_K_S",[22]="IQ3_XS",[23]="IQ3_XXS",[24]="IQ1_S",[25]="IQ4_NL",[26]="IQ3_S",[27]="IQ3_M",[28]="IQ2_S",[29]="IQ2_M",[30]="IQ4_XS",[31]="IQ1_M",[32]="BF16",[36]="TQ1_0",[37]="TQ2_0",[38]="MXFP4_MOE",[39]="NVFP4",[40]="Q1_0",[41]="Q2_0" };
        foreach (var pair in expected)
        {
            var parsed = GgufInspection.ReadHeader(new MemoryStream(Header(pair.Key)));
            Check(parsed.FileTypeId == pair.Key && ModelSelection.FileType(pair.Key) == pair.Value, "canonical ftype " + pair.Key);
        }
        foreach (var id in new uint[] { 4,5,6,33,34,35,42,1024,uint.MaxValue }) Check(ModelSelection.FileType(id) is null, "unknown/removed ID " + id);
        try { GgufInspection.ReadHeader(new MemoryStream(Header(2, badType: true))); throw new Exception("Wrong type accepted"); }
        catch (GgufException e) { Check(e.Code == "invalid", "wrong metadata type"); }
        var payload = Header(2, 70000); // file_type after the first range and large array
        using var handler = new Handler(r => Range(r, payload));
        using var inspector = new RemoteGgufInspector(handler);
        var remote = await inspector.InspectAsync(Revision, File("arbitrary.gguf", payload.Length), default);
        Check(remote.FileTypeId == 2 && handler.Calls == 2 && inspector.Statistics.Bytes == payload.Length, "incremental header after array");
        await inspector.InspectAsync(Revision, File("arbitrary.gguf", payload.Length), default);
        Check(handler.Calls == 2, "memoized same commit/path");
        foreach (var mutation in new[] { "200", "416", "from", "total", "short", "excess", "encoding", "commit", "redirect", "cycle", "429long", "429reset" })
        {
            using var bad = new Handler(r =>
            {
                var response = Range(r, payload);
                switch (mutation)
                {
                    case "200": response.StatusCode = HttpStatusCode.OK; break;
                    case "416": response.StatusCode = HttpStatusCode.RequestedRangeNotSatisfiable; break;
                    case "from": response.Content.Headers.ContentRange = new ContentRangeHeaderValue(1, 65535, payload.Length); break;
                    case "total": response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0, 65535, payload.Length + 1); break;
                    case "short": case "excess":
                        response.Content = new StreamContent(new NoLengthStream(new byte[mutation == "short" ? 3 : 65537]));
                        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(0,65535,payload.Length);
                        response.Content.Headers.ContentLength = null; break;
                    case "encoding": response.Content.Headers.ContentEncoding.Add("gzip"); break;
                    case "commit": response.Headers.Remove("X-Repo-Commit"); response.Headers.Add("X-Repo-Commit",new string('a',40)); break;
                    case "redirect": case "cycle": response.StatusCode=HttpStatusCode.Redirect; response.Headers.Location = new Uri(mutation == "redirect" ? "https://evil.example/file" : r.RequestUri!.AbsoluteUri); break;
                    case "429long": response.StatusCode = HttpStatusCode.TooManyRequests; response.Headers.RetryAfter = new(TimeSpan.FromSeconds(30)); break;
                    case "429reset": response.StatusCode = HttpStatusCode.TooManyRequests; response.Headers.RetryAfter = new(TimeSpan.Zero); response.Headers.Add("RateLimit", "\"api\";r=0;t=120"); break;
                }
                return response;
            });
            using var reader = new RemoteGgufInspector(bad);
            var code = mutation switch { "200"=>"range-unsupported", "416"=>"inspection-http", "commit"=>"content-changed", "redirect"=>"unsafe-redirect", "cycle"=>"redirect-limit", "429long" or "429reset"=>"remote-rate-limit", _=>"invalid-range" };
            await Fails(() => reader.InspectAsync(Revision, File("arbitrary.gguf",payload.Length),default),code);
            Check(bad.Calls <= 6 && reader.Statistics.Bytes <= 65537, "bounded failed response " + mutation);
            if (mutation is "short" or "excess") Check(reader.Statistics.Bytes == (mutation=="short" ? 3 : 65537),"actual streamed body length checked "+mutation);
        }
        using var limitedHandler = new Handler(r => Range(r, payload));
        using var limited = new RemoteGgufInspector(limitedHandler, new(HeaderBytes: 65536));
        await Fails(() => limited.InspectAsync(Revision, File("arbitrary.gguf",payload.Length),default),"inspection-limit");
        Check(limitedHandler.Calls == 1, "no request beyond header budget");
        using var stopped = new CancellationTokenSource(); stopped.Cancel();
        try { await inspector.InspectAsync(Revision, File("other.gguf",payload.Length),stopped.Token); throw new Exception("Cancellation lost"); }
        catch (OperationCanceledException) { Check(true, "caller cancellation"); }
        var attempts = 0;
        using var retryHandler = new Handler(r => { var response = Range(r,payload); if (++attempts == 1) { response.StatusCode=HttpStatusCode.ServiceUnavailable; response.Headers.RetryAfter=new(TimeSpan.Zero); } return response; });
        using var retry = new RemoteGgufInspector(retryHandler);
        Check((await retry.InspectAsync(Revision,File("retry.gguf",payload.Length),default)).FileTypeId == 2 && attempts == 3, "bounded transient retry");
        await GoogleMenus();
        await UnitTests(h);
        await BudgetTests(payload);
        await Integration();
        Console.WriteLine($"OK: {checks} comprobaciones 015, metadata, resolución, HTTP acotado y selección Google.");
    }
    private static async Task UnitTests(GgufHeader header)
    {
        var files = new[] { File("a/model-Q4_0-00001-of-00002.gguf",1000), File("a/model-Q4_0-00002-of-00002.gguf",1000) };
        var inspector = new Headers(f => header with { SplitIndex = f.Path.Contains("00001-of") ? 0 : 1, SplitCount = 2, SplitTensorCount = 2 });
        var resolver = new ModelResolver(inspector);
        var evidence = await resolver.ValidateAsync(Revision,new(files),_=>{},default);
        Check(evidence.Length==2 && inspector.Calls==2 && ModelSelection.Variant(new(files),evidence) is not null,"all shards validated, one unit");
        foreach (var failure in new[] { "split", "quant", "architecture", "sum" })
        {
            var bad = new Headers(f => header with {
                SplitIndex = f.Path.Contains("00001-of") || failure=="split" ? 0 : 1, SplitCount=2,
                SplitTensorCount=failure=="sum" ? 3 : 2,
                FileTypeId=failure=="quant" && f.Path.Contains("00002-of") ? 15u : 2u,
                Hints=failure=="architecture" && f.Path.Contains("00002-of") ? new Dictionary<string,string>{{"general.architecture","other"}} : header.Hints });
            await Fails(()=>new ModelResolver(bad).ValidateAsync(Revision,new(files),_=>{},default),failure is "split" or "sum" ? "split-conflict" : "metadata-conflict");
        }
        var arbitrary = new RepositoryInventory(Revision,[File("whatever.gguf",1000),File("other.gguf",1000)]);
        var discovered = await new ModelResolver(new Headers(_=>header)).DiscoverAsync(arbitrary,_=>{},default);
        Check(discovered.Variants.Length==2 && discovered.Variants.All(v=>v.Quantization=="Q4_0"),"arbitrary names two units not first/hash dedup");
        var nameOnly = await new ModelResolver(new Headers(_=>header with {FileTypeId=null})).DiscoverAsync(arbitrary,_=>{},default);
        Check(nameOnly.Variants.Length==0 && nameOnly.Diagnostics.Length==2,"no invented quant with absent metadata");
        var roles = ModelSelection.Classify(Revision,File("mtp-x.gguf",1000),header with {Hints=new Dictionary<string,string>{{"general.architecture","clip"}}});
        Check(roles.State==EvidenceState.Conflict,"role disagreement not queued");
    }
    private static async Task BudgetTests(byte[] payload)
    {
        using var waiting = new RemoteGgufInspector(new WaitingHandler(),new(RequestSeconds:1));
        await Fails(()=>waiting.InspectAsync(Revision,File("x.gguf",payload.Length),default),"inspection-timeout");
        var probe=new ProbeStream();
        using var ignored=new RemoteGgufInspector(new Handler(_=>new(HttpStatusCode.OK){Content=new StreamContent(probe)}));
        await Fails(()=>ignored.InspectAsync(Revision,File("x.gguf",payload.Length),default),"range-unsupported");
        Check(!probe.ReadCalled && probe.Closed && ignored.Statistics.Bytes==0,"200 disposed without reading body");
        foreach (var limits in new[] { new InspectionLimits(SessionBytes:65537), new InspectionLimits(UnitBytes:65537),new InspectionLimits(Requests:1) })
        {
            using var handler = new Handler(r=>Range(r,payload));
            using var inspector = new RemoteGgufInspector(handler,limits);
            await Fails(()=>inspector.InspectAsync(Revision,File("x.gguf",payload.Length),default),"inspection-limit");
            Check(handler.Calls==1 && inspector.Statistics.Bytes==65536,"bounded session/unit/request limit");
        }
        using var etagHandler = new Handler(r=> { var response=Range(r,payload); response.Headers.ETag=new EntityTagHeaderValue(r.Headers.Range!.Ranges.Single().From==0 ? "\"a\"" : "\"b\""); return response; });
        using var etagReader = new RemoteGgufInspector(etagHandler);
        await Fails(()=>etagReader.InspectAsync(Revision,File("x.gguf",payload.Length),default),"content-changed");
        using var stop = new CancellationTokenSource(); var calls=0;
        using var cancelled = new Handler(r=> { var response=Range(r,payload); if (++calls==2) stop.Cancel(); return response; });
        using var reader = new RemoteGgufInspector(cancelled);
        try { await reader.InspectAsync(Revision,File("x.gguf",payload.Length),stop.Token); throw new Exception("Cancellation lost"); }
        catch (OperationCanceledException) { Check(reader.Statistics.Bytes==65536,"cancelled between blocks"); }
    }
    private sealed class PayloadTransport(byte[] payload, CancellationTokenSource? stop = null) : IDownloadTransport
    {
        public List<long> Offsets=[];
        public Task<HttpResponseMessage> OpenAsync(OperationDocument operation, int file, long offset, CancellationToken cancellation)
        {
            Check(operation.ModelSelection!.Commit==Commit && operation.ModelSelection.Files.Single().Path=="weights.gguf","integration exact pinned path, no mmproj");
            Offsets.Add(offset);
            var response=new HttpResponseMessage(offset==0 ? HttpStatusCode.OK : HttpStatusCode.PartialContent)
            { Content=stop is null ? new ByteArrayContent(payload[(int)offset..]) : new StreamContent(new Pausing(payload[(int)offset..],stop)) };
            response.Content.Headers.ContentLength=payload.Length-offset;
            response.Headers.ETag=new EntityTagHeaderValue("\"stable\"");
            if (offset>0) response.Content.Headers.ContentRange=new ContentRangeHeaderValue(offset,payload.Length-1,payload.Length);
            return Task.FromResult(response);
        }
    }
    private sealed class Pausing(byte[] payload,CancellationTokenSource stop) : MemoryStream(payload)
    {
        private int calls;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellation=default)
        {
            if (calls++==0) return base.ReadAsync(buffer[..Math.Min(buffer.Length,97)],cancellation);
            stop.Cancel(); return ValueTask.FromCanceled<int>(stop.Token);
        }
    }
    private static async Task Integration()
    {
        var payload=Header(2).Concat(new byte[1000]).ToArray();
        using var metadata = new HttpClient(new Handler(r=>new(HttpStatusCode.OK) {Content=new StringContent(
            r.RequestUri!.AbsolutePath.Contains("/revision/") ? $$"""{"id":"owner/repo","sha":"{{Commit}}"}""" :
            $$"""[{"type":"file","path":"weights.gguf","size":{{payload.Length}},"oid":"{{Commit}}"}]""")}));
        using var ranges = new Handler(r=>Range(r,payload)); using var inspector=new RemoteGgufInspector(ranges);
        var root=Path.Combine(Path.GetTempPath(),"coquena-resolution-"+Guid.NewGuid()); var home=new Home(root); var installer=new ModelInstaller(home);
        try
        {
            var cancelled=await new ModelSelectionMenu(new(metadata),inspector).SelectAsync(ModelRequest.Parse("owner/repo"),new Interaction(2));
            Check(cancelled is null && !Directory.Exists(root),"inspect/cancel does not create home or GUID");
            var selected=await new ModelSelectionMenu(new(metadata),inspector).SelectAsync(ModelRequest.Parse("owner/repo:Q4_0"),new Interaction(0));
            Check(!Directory.Exists(root),"selected header validation before state");
            var operation=installer.Create(selected!);
            using var stop=new CancellationTokenSource(); var first=new PayloadTransport(payload,stop);
            var paused=await installer.Downloads.RunAsync(operation.Id,first,cancellation:stop.Token,finalize:installer.FinalizeAsync,beforeCancel:installer.CancelAsync);
            Check(paused?.State=="paused" && paused.Files.Single().ConfirmedBytes==97 && installer.List().Length==0,"015 selected operation pauses without installation");
            var resume=new PayloadTransport(payload);
            await installer.Downloads.RunAsync(operation.Id,resume,finalize:installer.FinalizeAsync,beforeCancel:installer.CancelAsync);
            Check(resume.Offsets.SequenceEqual(new long[]{97}) && installer.Downloads.List().Length==0 && installer.List().Single().Record.Name=="owner/repo:Q4_0","existing 009 resume and publish");
            Check(System.IO.File.ReadAllBytes(home.PathFor("models/owner/repo/weights.gguf")).SequenceEqual(payload),"published exact bytes");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root,true); }
    }
    private static async Task GoogleMenus()
    {
        var primary = Header(2); var auxiliary = Header(32, architecture: "clip");
        using var metadata = new HttpClient(new Handler(r => new(HttpStatusCode.OK) { Content = new StringContent(
            r.RequestUri!.AbsolutePath.Contains("/revision/") ? $$"""{"id":"owner/repo","sha":"{{Commit}}"}""" :
            r.RequestUri.AbsolutePath.Contains("/tree/") ? $$"""[{"type":"file","path":"gemma-4-26B_q4_0-it.gguf","size":{{primary.Length}},"oid":"{{Commit}}"},{"type":"file","path":"gemma-4-26B-it-mmproj.gguf","size":{{auxiliary.Length}},"oid":"{{Commit}}"}]""" : "README") }));
        foreach (var selector in new[] { "owner/repo", "owner/repo:Q4_0" })
        {
            using var rangeHandler = new Handler(r => Range(r, r.RequestUri!.AbsolutePath.Contains("mmproj") ? auxiliary : primary));
            using var inspector = new RemoteGgufInspector(rangeHandler);
            var menu = new Interaction(1, 0);
            var selected = await new ModelSelectionMenu(new(metadata),inspector).SelectAsync(ModelRequest.Parse(selector),menu);
            Check(selected!.Selection.Commit == Commit && selected.Selection.Files.Length == 1 && selected.Selection.Files[0].Path == "gemma-4-26B_q4_0-it.gguf", "both Google selectors pinned principal only");
            Check(menu.Menus.Count == 2 && menu.Menus.All(m=>m.Length==3 && m[0].StartsWith("Descargar Q4_0")), "README same explicit menu");
            Check(rangeHandler.Calls == 2, "memoization after README");
        }
    }
    internal sealed class NamedInspector : IRemoteGgufInspector
    {
        public Task<GgufHeader> InspectAsync(RepositoryRevision revision, RepositoryFile file, CancellationToken cancellation)
        { cancellation.ThrowIfCancellationRequested(); return Task.FromResult(new GgufHeader(3, 1, null, null, null, new Dictionary<string, string>())); }
    }
    public static async Task Live(string selector, string? commit = null)
    {
        var request = ModelRequest.Parse(selector);
        using var client = HuggingFaceStore.CreateClient();
        var store = new HuggingFaceStore(client);
        var revision = await store.ResolveAsync(request.Repository, commit ?? "main");
        var inventory = await store.InventoryAsync(revision);
        using var inspector = new RemoteGgufInspector();
        Console.WriteLine($"{revision.Repository} @ {revision.Commit}");
        try
        {
        if (commit is not null)
            foreach (var file in inventory.Files.Where(f => f.Path.EndsWith(".gguf")))
            {
                var h = await inspector.InspectAsync(revision, file, default);
                Console.WriteLine($"{file.Path}: version={h.Version}; file_type={h.FileTypeId}; tensors={h.TensorCount}; architecture={h.Hints.GetValueOrDefault("general.architecture")}; split={h.SplitIndex}/{h.SplitCount}");
            }
        var resolver = new ModelResolver(inspector);
        var candidates = await resolver.DiscoverAsync(inventory, Console.WriteLine, default);
        foreach (var diagnostic in candidates.Diagnostics) Console.WriteLine(diagnostic);
        foreach (var v in candidates.Variants) Console.WriteLine(v.Label);
        var selected = candidates.Variants.First(v => v.CompanionType is null && (request.Quantization is null || v.Quantization.Equals(request.Quantization, StringComparison.OrdinalIgnoreCase)));
        var evidence = await resolver.ValidateAsync(revision, new(selected.Files), Console.WriteLine, default);
        if (ModelSelection.Variant(new(selected.Files), evidence) is null) throw new Exception("Live validation failed");
        Console.WriteLine($"SELECTED {selected.Name(revision.Repository)}: {string.Join(',', selected.Files.Select(f => f.Path))}");
        var menu = new Interaction(0);
        var result = await new ModelSelectionMenu(store, inspector).SelectAsync(request, menu, requestedRevision: revision.Commit);
        if (result is null || result.Selection.Commit != revision.Commit || result.Selection.Files.Length == 0)
            throw new Exception("Live pinned menu failed");
        Console.WriteLine("MENU " + string.Join(" | ", menu.Menus[0]));
        Console.WriteLine($"MENU SELECTED {result.Name} @ {result.Selection.Commit}: {string.Join(',', result.Selection.Files.Select(f=>f.Path))}");
        }
        finally { Console.WriteLine(inspector.Statistics); }
    }
    public static void Names()
    {
        var revision = new RepositoryRevision("google/gemma-4-26B-A4B-it-qat-q4_0-gguf", "main", "d1c082be9cf3c8a514acf63b8761f4b41935842e");
        var inventory = new RepositoryInventory(revision, [
            new("gemma-4-26B_q4_0-it.gguf", 14439363584, revision.Commit, "3eca3b8f6d7baf218a7dd6bba5fb59a56ee25fe2d567b6f5f589b4f697eca51d", null),
            new("gemma-4-26B-it-mmproj.gguf", 1194828160, revision.Commit, "a359953a076b877db30c31dbbb4c6d93b4a6e017ee5db5784247e4d4c0dd4f3b", null)]);
        var candidates = ModelSelection.Discover(inventory);
        if (candidates.Variants.Count(v => v.CompanionType is null && v.Quantization.Equals("Q4_0", StringComparison.OrdinalIgnoreCase)) != 1)
            throw new Exception("015: Google Q4_0 must survive discovery and explicit filtering, excluding suffix mmproj.");
    }
}
