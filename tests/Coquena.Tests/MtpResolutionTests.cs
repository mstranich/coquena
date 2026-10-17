using System.Net;
using System.Diagnostics;
using System.Text;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Models;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class MtpResolutionTests
{
    private const string Commit = "2e9f6f487b82200f2a045e67d01a3f1e2982b00a";
    private const string Repository = "EryriLabs/Ornith-1.5-35B-A3B-BigBang-MTP-GGUF";
    private const string Main = "Ornith-1.5-35B-A3B-BigBang-MTP-Q8_0.gguf";
    private const string Draft = "mtpdraft-Q8_0.gguf";
    private static int checks;
    private static void Check(bool value, string reason)
    { checks++; if (!value) throw new Exception("037: " + reason); }
    private static RepositoryRevision Revision => new(Repository, "main", Commit);
    private static RepositoryFile File(string path, long size = 1000) => new(path, size, Commit, null, null);
    private static GgufHeader Header(uint? quant = 7, string architecture = "qwen35moe") =>
        new(3, 23, null, null, null, new Dictionary<string, string>
        { ["general.architecture"] = architecture, ["general.name"] = "Ornith Bigbang Mtpfix", ["general.type"] = "model" }, quant);
    private sealed class Inspector(Func<RepositoryFile, GgufHeader>? read = null) : IRemoteGgufInspector
    {
        public Task<GgufHeader> InspectAsync(RepositoryRevision revision, RepositoryFile file, CancellationToken cancellation)
        { Check(revision.Commit == Commit, "inspection pinned"); return Task.FromResult(read is null ? Header() with { TensorCount = file.Path == Main ? 733UL : 23UL } : read(file)); }
    }
    private sealed class Handler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            var path = request.RequestUri!.AbsolutePath;
            var json = path.EndsWith("/revision/main") ? $$"""{"id":"{{Repository}}","sha":"{{Commit}}"}""" :
                path.Contains("/tree/" + Commit) ? $$"""[{"type":"file","path":"{{Main}}","size":36903139136,"oid":"{{Commit}}"},{"type":"file","path":"{{Draft}}","size":{{payload.Length}},"oid":"{{Commit}}"}]""" :
                throw new Exception("Unexpected metadata request " + path);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        }
    }
    private sealed class Menu(int choice) : IModelSelectionInteraction
    {
        public bool IsInteractive => true;
        public string[] Options = [];
        public void Show(string text) { }
        public Task<int?> ChooseAsync(IReadOnlyList<string> options, CancellationToken cancellation)
        { Options = options.ToArray(); return Task.FromResult<int?>(choice); }
    }
    private sealed class Transport(byte[] payload) : IDownloadTransport
    {
        public int Calls;
        public Task<HttpResponseMessage> OpenAsync(OperationDocument operation, int file, long offset, CancellationToken cancellation)
        {
            Calls++;
            Check(operation.ModelSelection!.Commit == Commit && operation.ModelSelection.Files.Single().Path == Draft && offset == 0,
                "only pinned draft transferred");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) });
        }
    }
    private static byte[] Payload()
    {
        using var memory = new MemoryStream(); using var writer = new BinaryWriter(memory, Encoding.UTF8, true);
        void Text(string text) { var bytes = Encoding.UTF8.GetBytes(text); writer.Write((ulong)bytes.Length); writer.Write(bytes); }
        writer.Write("GGUF"u8); writer.Write(3u); writer.Write(23UL); writer.Write(3UL);
        Text("general.architecture"); writer.Write(8u); Text("qwen35moe");
        Text("general.type"); writer.Write(8u); Text("model");
        Text("general.file_type"); writer.Write(4u); writer.Write(7u);
        return memory.ToArray();
    }
    public static async Task Run(string? executable = null)
    {
        var inventory = new RepositoryInventory(Revision, [File(Main, 36903139136), File(Draft, 1990649440),
            File("Ornith-1.5-35B-A3B-BigBang-MTP-Q4_K_M-embedded.gguf", 21713463296)]);
        var variants = ModelSelection.Discover(inventory).Variants;
        Check(variants.Single(v => v.CompanionType == "mtp").Files.Single().Path == Draft, "fixed inventory offers draft");
        Check(variants.Count(v => v.CompanionType is null) == 2 && variants.Any(v => v.CompanionType is null && v.Files[0].Path == Main),
            "principal with MTP and embedded principal remain principals");
        foreach (var path in new[] { Draft, "mtp-draft-Q8_0.gguf", "mtp-modelo-Q8_0.gguf", "MTP/whatever.gguf", "nested/mtp/weights.gguf", "folder/model-mtpdraft-Q8_0.gguf" })
        foreach (var header in new GgufHeader?[] { null, Header(), Header(null), Header(uint.MaxValue) })
        {
            var evidence = ModelSelection.Classify(Revision, File(path), header);
            var expected = ModelSelection.NameQuantization(path) is null && (header?.FileTypeId is null or uint.MaxValue) ? EvidenceState.Inconclusive : EvidenceState.Candidate;
            Check(evidence.Role == ModelRole.Mtp && evidence.State == expected && evidence.Diagnostic!.Contains("no certifica"), "heuristic remains candidate " + path);
        }
        foreach (var path in new[] { Main, "model-MTP-Q8_0.gguf", "mtpdrafting-Q8_0.gguf", "notmtp/weights-Q8_0.gguf", "mtp-model/weights-Q8_0.gguf" })
            Check(ModelSelection.Classify(Revision, File(path), Header()).Role == ModelRole.Principal, "no internal token conversion " + path);
        var arbitrary = new RepositoryInventory(Revision, [File("mtp/whatever.gguf"), File("whatever.gguf")]);
        var discovered = await new ModelResolver(new Inspector()).DiscoverAsync(arbitrary, _ => { }, default);
        Check(discovered.Variants.Length == 2 && discovered.Variants.Count(v => v.CompanionType == "mtp") == 1, "arbitrary names quant from header, role from directory only");
        foreach (var quant in new[] { ("Q4_0", 2u), ("Q8_0", 7u), ("BF16", 32u) })
            Check(ModelSelection.Classify(Revision, File("mtpdraft-" + quant.Item1 + ".gguf"), Header(quant.Item2)).State == EvidenceState.Candidate, "quant does not verify role");
        foreach (var header in new[] { Header(2), Header(7, "clip") })
            Check(ModelSelection.Classify(Revision, File(Draft), header).State == EvidenceState.Conflict, "incompatible quant or architecture blocks");
        var conflict = ModelSelection.Classify(Revision, File("mtp-mmproj-Q8_0.gguf"), Header());
        Check(conflict.State == EvidenceState.Conflict, "incompatible name markers block");
        Check(ModelSelection.Classify(Revision, File("mtp-mmproj-weights.gguf"), Header(null)).State == EvidenceState.Conflict, "missing quant cannot erase role conflict");
        var futureConflict = ModelSelection.Classify(Revision, File("mtp-weights.gguf"), Header(uint.MaxValue, "clip"));
        Check(futureConflict.State == EvidenceState.Conflict && futureConflict.Diagnostic!.Contains("clip"), "unknown quant cannot hide incompatible architecture diagnostic");
        Check(ModelSelection.Classify(Revision, File("mmproj-Q8_0.gguf"), Header(7, "clip")).Role == ModelRole.Mmproj, "mmproj preserved");
        var noQuant = ModelSelection.Classify(Revision, File("mtp/weights.gguf"), Header(null));
        Check(noQuant.State == EvidenceState.Inconclusive, "unknown quant remains inconclusive");
        var shards = new RepositoryInventory(Revision, [File("mtpdraft-Q8_0-00001-of-00002.gguf"), File("mtpdraft-Q8_0-00002-of-00002.gguf")]);
        Check(ModelSelection.Discover(shards).Variants.Single().Files.Length == 2, "complete shards single auxiliary");
        Check(ModelSelection.Discover(shards with { Files = shards.Files[..1] }).Variants.Length == 0, "missing shard blocks");
        var shardInspector = new Inspector(f => Header() with { TensorCount = 1, SplitIndex = f.Path.Contains("00001-of") ? 0 : 1, SplitCount = 2, SplitTensorCount = 2 });
        var shardEvidence = await new ModelResolver(shardInspector).ValidateAsync(Revision, new(shards.Files), _ => { }, default);
        Check(ModelSelection.Variant(new(shards.Files), shardEvidence) is { CompanionType: "mtp", Files.Length: 2 }, "validated shards retain candidate role");
        try
        {
            await new ModelResolver(new Inspector(f => Header(f.Path.Contains("00001-of") ? 7u : 2u) with
                { TensorCount = 1, SplitIndex = f.Path.Contains("00001-of") ? 0 : 1, SplitCount = 2, SplitTensorCount = 2 }))
                .ValidateAsync(Revision, new(shards.Files), _ => { }, default);
            throw new Exception("Expected shard metadata conflict");
        }
        catch (ModelSourceException e) { Check(e.Code == "metadata-conflict", "incompatible shard metadata blocks"); }
        Check(ModelSelection.Classify(Revision with { Repository = "other/publisher" }, File(Draft), Header() with
            { Hints = new Dictionary<string, string>() }).State == EvidenceState.Candidate, "publisher and missing hints do not contradict candidate");
        var payload = Payload();
        using var client = new HttpClient(new Handler(payload));
        var source = new HuggingFaceStore(client);
        foreach (var selector in new[] { Repository + "#mtp", Repository + "#mtp:Q8_0", Repository + ":Q8_0" })
        {
            var menu = new Menu(0);
            var selected = await new ModelSelectionMenu(source, new Inspector()).SelectAsync(ModelRequest.Parse(selector), menu);
            Check(selected!.Selection.Commit == Commit && selected.Selection.Files.Single().Path == (selector.Contains('#') ? Draft : Main), "explicit menu selects correct role " + selector);
            Check(menu.Options.Length == 3, "one unit plus README/cancel");
            if (selector.Contains('#')) Check(menu.Options[0].Contains("rol MTP candidato"), "menu discloses heuristic");
        }
        var root = Path.Combine(Path.GetTempPath(), "coquena-mtp-" + Guid.NewGuid());
        try
        {
            var installer = new ModelInstaller(new Home(root)); var transport = new Transport(payload);
            using var output = new StringWriter(); using var error = new StringWriter();
            Check(ModelCli.Run(["companion", "download", Repository + "#mtp:Q8_0"], output, error, installer, source, transport,
                new Menu(2), inspector: new Inspector()) == 0 && !Directory.Exists(root) && transport.Calls == 0, "CLI cancel creates no home/GUID/transfer");
            Check(ModelCli.Run(["companion", "download", Repository + "#mtp:Q8_0"], output, error, installer, source, transport,
                new Menu(0), inspector: new Inspector()) == 0 && error.ToString().Length == 0, "CLI selects and installs fixture draft");
            var installed = installer.List().Single();
            Check(transport.Calls == 1 && installed.Record.CompanionType == "mtp" && installed.Record.Name == Repository + "#mtp:Q8_0" &&
                installed.Record.Selection!.Commit == Commit, "only draft catalogued with pinned selection");
            Check(!Directory.GetFiles(root, Main, SearchOption.AllDirectories).Any() && installer.Downloads.List().Length == 0, "no principal or remaining operation");
            if (executable is not null)
            {
                foreach (var companion in new[] { true, false })
                {
                    var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables()) environment[(string)entry.Key] = (string)entry.Value!;
                    info.Environment.Clear();
                    foreach (var entry in environment) info.Environment[entry.Key] = entry.Value;
                    info.Environment["COQUENA_HOME"] = root;
                    info.ArgumentList.Add("model"); if (companion) info.ArgumentList.Add("companion"); info.ArgumentList.Add("list");
                    using var process = Process.Start(info)!;
                    var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                    await process.WaitForExitAsync();
                    Check(process.ExitCode == 0 && (await stderr).Length == 0 && (await stdout).Contains(Repository + "#mtp:Q8_0") == companion,
                        "published CLI lists fixture only as companion");
                }
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        Console.WriteLine($"037 MTP resolution: {checks} checks passed.");
    }
}
