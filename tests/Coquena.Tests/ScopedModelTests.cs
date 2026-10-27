using System.Net;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Models;
using Coquena.Projects;
using Coquena.Recipes;

namespace Coquena.Tests;

internal static class ScopedModelTests
{
    private static int checks;
    private static void Check(bool value, string text)
    { if (!value) throw new Exception(text); checks++; }
    public static async Task Run(string? product = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-scoped-model-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var context = ScopeContext.Project(Path.Combine(root, "coquena.yaml"));
        var bytes = Gguf(); var transport = new Transport(bytes);
        var models = new ScopedModels(context);
        Check(models.List().Count == 0 && !Directory.Exists(context.Root), "query does not initialize scope");
        var selected = Selection(bytes);
        var first = await models.AcquireAsync(selected, transport, explicitlyRequested: false);
        Check(transport.Calls == 1 && models.List().Count == 1, "acquire automatic unit");
        var second = await models.AcquireAsync(selected, transport);
        Check(first.ResourceId == second.ResourceId && first.InstallationId == second.InstallationId && transport.Calls == 1, "explicit reuse without transfer");
        Check(models.List()[0]!["installations"]![0]!["protected"]!.GetValue<bool>(), "explicit request protects automatic revision");
        models.Protect(first.InstallationId, false);
        Check(!models.List()[0]!["installations"]![0]!["protected"]!.GetValue<bool>(), "unprotect retains installation");
        var yaml = Path.Combine(root, "coquena.yaml");
        File.WriteAllText(yaml, "name: test\nmodels:\n  principal: fixture/repository:Q4_0\n  alternativo: fixture/repository:Q4_0\n");
        var recipe = RecipeReader.Read(new RecipeSelection(root, [yaml]));
        var aliases = ModelIntent.FromRecipe(recipe);
        foreach (var intent in aliases) models.Bind(intent.Key, intent.Kind, selected, first);
        Check(models.ResolveAlias("principal", false) == models.ResolveAlias("alternativo", false), "aliases share resource identity");
        Check(models.List().Count == 1, "aliases do not duplicate inventory");
        var observation = models.Observe(models.List()[0]!.AsObject());
        Check(observation["installations"]![0]!["observed-availability"]!.GetValue<string>() == "available", "cheap GGUF availability");
        var path = first.EntryPath;
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-2));
        Check(models.Observe(models.List()[0]!.AsObject())["installations"]![0]!["changed"]!.GetValue<bool>(), "changed timestamp warns");
        var otherRoot = Path.Combine(root, "other"); Directory.CreateDirectory(otherRoot);
        var other = new ScopedModels(ScopeContext.Project(Path.Combine(otherRoot, "coquena.yaml")));
        Check(other.List().Count == 0, "scope isolation");
        var neighbor = Path.Combine(Path.GetDirectoryName(path)!, "user.txt"); File.WriteAllText(neighbor, "keep");
        using (var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var partial = models.Remove(first.ResourceId, false);
            Check(!partial.Complete && models.List().Count == 1, "locked file keeps pending withdrawal");
            try { await models.AcquireAsync(selected, transport); throw new Exception("expected pending rejection"); }
            catch (ModelSourceException e) { Check(e.Code == "removal-pending", "pending blocks reuse"); }
        }
        var removed = models.Remove(first.ResourceId, false);
        Check(removed.Complete && removed.FreedBytes == bytes.Length && !File.Exists(path), "retry removes only remaining bytes");
        Check(File.Exists(neighbor), "foreign neighbor retained");
        var fresh = await models.AcquireAsync(selected, transport);
        Check(fresh.ResourceId != first.ResourceId, "removed canonical name gets new identity");
        Check(models.ResolveAlias("principal", false) == first.ResourceId, "old aliases do not reconnect");
        try { models.Remove("principal", false); throw new Exception("expected broken alias"); }
        catch (ModelSourceException e) { Check(e.Code == "unknown-resource" && File.Exists(fresh.EntryPath), "broken alias cannot remove a reused canonical name"); }
        var activeStore = new ScopeStore(context); var activeSnapshot = activeStore.Read();
        var activeDocs = activeSnapshot.Documents.ToDictionary(p => p.Key, p => (JsonObject)p.Value.DeepClone());
        foreach (var doc in activeDocs.Values) doc["revision"] = doc["revision"]!.GetValue<long>() + 1;
        activeDocs["local/active.json"] = ProjectDocuments.Create("active", 1, new JsonObject { ["resources"] = new JsonArray(JsonValue.Create(fresh.InstallationId)) });
        activeStore.Commit(activeSnapshot.Revision, activeDocs);
        try { models.Remove(fresh.ResourceId, false); throw new Exception("expected active rejection"); }
        catch (ModelSourceException e) { Check(e.Code == "resource-in-use", "active physical revision blocks withdrawal"); }
        activeSnapshot = activeStore.Read(); activeDocs = activeSnapshot.Documents.ToDictionary(p => p.Key, p => (JsonObject)p.Value.DeepClone());
        foreach (var doc in activeDocs.Values) doc["revision"] = doc["revision"]!.GetValue<long>() + 1;
        activeDocs["local/active.json"]["data"] = new JsonObject(); activeStore.Commit(activeSnapshot.Revision, activeDocs);
        var newer = selected with { Selection = selected.Selection with { Commit = new string('b', 40) } };
        var revision2 = await models.AcquireAsync(newer, transport, false);
        Check(revision2.ResourceId == fresh.ResourceId && revision2.InstallationId != fresh.InstallationId && File.Exists(fresh.EntryPath), "new revision preserves previous physical files");
        var replaced = Path.Combine(root, "replacement.gguf"); File.WriteAllBytes(replaced, bytes);
        File.Move(replaced, revision2.EntryPath, true);
        Check(!models.Remove(fresh.ResourceId, false).Complete && File.Exists(revision2.EntryPath), "same-byte foreign replacement is not adopted");
        File.Delete(fresh.EntryPath);
        Check(models.Observe(models.List()[0]!.AsObject())["installations"]![0]!["observed-availability"]!.GetValue<string>() == "removal-pending", "pending revision is not servable");
        await RemoteTests(root, bytes);
        await UnitTests(root, bytes);
        if (product is not null)
        {
            async Task<string> Native(params string[] args)
            {
                var start = new System.Diagnostics.ProcessStartInfo(product) { RedirectStandardOutput = true, RedirectStandardError = true };
                var environment = start.Environment.ToArray(); start.Environment.Clear();
                foreach (var group in environment.GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase)) start.Environment[group.Key] = group.Last().Value;
                start.Environment["COQUENA_HOME"] = Path.Combine(root, "home");
                foreach (var arg in args) start.ArgumentList.Add(arg);
                using var process = System.Diagnostics.Process.Start(start)!;
                var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                try { await process.WaitForExitAsync(timeout.Token); }
                finally { if (!process.HasExited) process.Kill(true); }
                var text = await stdout;
                Check(process.ExitCode == 0, "native command " + string.Join(' ', args) + ": " + await stderr);
                return text;
            }
            Check(JsonNode.Parse(await Native("model", "list", "--base", root, "--json")) is JsonArray { Count: 1 }, "native scoped list");
            var effective = JsonNode.Parse(await Native("model", "show", "principal", "--base", root, "--json"))!;
            Check(effective["repository"]!.GetValue<string>() == selected.Selection.Repository && effective["quantization"]!.GetValue<string>() == "Q4_0", "native effective alias intent");
            await Native("model", "protect", fresh.InstallationId, "--base", root);
            await Native("model", "unprotect", fresh.InstallationId, "--base", root);
            Check(JsonNode.Parse(await Native("model", "list", "-g", "--json")) is JsonArray { Count: 1 }, "native global inventory");
            var remoteBase = Path.Combine(root, "remote");
            Check(JsonNode.Parse(await Native("model", "remove", selected.Name, "--base", remoteBase))!["complete"]!.GetValue<bool>(), "native removal");
            Check(JsonNode.Parse(await Native("model", "companion", "list", "--base", remoteBase, "--json")) is JsonArray { Count: 1 }, "native removal preserves companion");
        }
        Console.WriteLine($"Scoped models: {checks} checks passed.");
    }
    internal static byte[] Gguf()
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(0x46554747u); writer.Write(3u); writer.Write(0ul); writer.Write(0ul);
        return stream.ToArray();
    }
    internal static SelectedModel Selection(byte[] bytes) => new("fixture/repository:Q4_0", null,
        new("fixture/repository", "main", new string('a', 40), [new("model-Q4_0.gguf", bytes.Length,
            new("sha256", "file-bytes", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()))]));
    internal sealed class Transport(byte[] bytes) : IDownloadTransport
    {
        public int Calls;
        public Task<HttpResponseMessage> OpenAsync(OperationDocument operation, int file, long offset, CancellationToken cancellation)
        { Calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }); }
    }
    private static async Task RemoteTests(string root, byte[] bytes)
    {
        var directory = Path.Combine(root, "remote"); Directory.CreateDirectory(directory);
        var context = ScopeContext.Project(Path.Combine(directory, "coquena.yaml"));
        var transport = new Transport(bytes); var menu = new Menu();
        using var client = new HttpClient(new Metadata(bytes)); var source = new HuggingFaceStore(client);
        var output = new StringWriter(); var error = new StringWriter();
        var code = ScopedModelCli.Run(["download", "fixture/repository:Q4_0", "--base", directory, "--json"], output, error,
            source, transport, menu, new ResolutionTests.NamedInspector());
        Check(code == 0 && transport.Calls == 1 && menu.Choices == 0, "scoped unique quantization no menu: " + error);
        var models = new ScopedModels(context);
        Check(models.List().Count == 1 && models.List(true).Count == 0 && !File.Exists(Path.Combine(directory, "coquena.yaml")), "download does not create recipe or implicit companions");
        var intent = new ModelIntent("aux", "companion", ModelRequest.Parse("fixture/repository#mmproj:F16"), "main", ["mmproj-F16.gguf"]);
        var auxiliary = await models.PrepareAsync(intent, source, transport, menu, new ResolutionTests.NamedInspector());
        Check(models.List(true).Count == 1 && models.ResolveAlias("aux", true) == auxiliary.ResourceId, "explicit remote files companion selection and binding");
        Check(models.List(true)[0]!["installations"]![0]!["origin"]!.GetValue<string>() == "automatic", "declared dependency has automatic origin");
        var main = new ModelIntent("principal", "model", ModelRequest.Parse("fixture/repository:Q4_0"), "main", []);
        var a = await models.PrepareAsync(main, source, transport, menu, new ResolutionTests.NamedInspector());
        var b = await models.PrepareAsync(main with { Key = "alternativo" }, source, transport, menu, new ResolutionTests.NamedInspector());
        Check(a.InstallationId == b.InstallationId && transport.Calls == 2, "equivalent recipe aliases share one physical installation");
        File.WriteAllText(Path.Combine(directory, "coquena.yaml"), "presets:\n  p:\n    args:\n      model: fixture/repository:Q4_0\n");
        var direct = await models.PrepareReferenceAsync(RecipeReader.Read(new(directory)), new("model", "fixture/repository:Q4_0", true),
            source, transport, menu, new ResolutionTests.NamedInspector());
        Check(direct.ResourceId == a.ResourceId && direct.InstallationId == a.InstallationId && transport.Calls == 2,
            "direct canonical reference shares prepared alias installation without transfer");
        var directFolder = Path.Combine(directory, "direct-only"); Directory.CreateDirectory(directFolder);
        File.WriteAllText(Path.Combine(directFolder, "coquena.yaml"), "presets:\n  p:\n    args:\n      model: fixture/repository:Q4_0\n");
        var directModels = new ScopedModels(ScopeContext.Project(Path.Combine(directFolder, "coquena.yaml")));
        var directTransport = new Transport(bytes);
        var directRecipe = RecipeReader.Read(new(directFolder));
        var initial = await directModels.PrepareReferenceAsync(directRecipe, new("model", "fixture/repository:Q4_0", true), source,
            directTransport, menu, new ResolutionTests.NamedInspector());
        Check(directTransport.Calls == 1 && directModels.ResolveReference(new("model", "fixture/repository:Q4_0", true)) == initial.EntryPath,
            "direct canonical reference prepares an undeclared selection");
        var laterAlias = await directModels.PrepareAsync(main, source, directTransport, menu, new ResolutionTests.NamedInspector());
        Check(laterAlias.ResourceId == initial.ResourceId && laterAlias.InstallationId == initial.InstallationId && directTransport.Calls == 1,
            "subsequent alias shares the initially direct selection");
        var before = new ScopeStore(context).Read().Revision;
        output.GetStringBuilder().Clear(); error.GetStringBuilder().Clear();
        Check(ScopedModelCli.Run(["list", "--base", directory, "--json"], output, error) == 0 &&
            JsonNode.Parse(output.ToString()) is JsonArray { Count: 1 } && new ScopeStore(context).Read().Revision == before, "list reads availability without inventory mutation");
        output.GetStringBuilder().Clear();
        Check(ScopedModelCli.Run(["download", "fixture/repository#mmproj:F16", "--base", directory], output, error, source, transport, menu) == 2 && transport.Calls == 2,
            "role mismatch rejects before transfer");
        var changed = Selection(bytes) with { Selection = Selection(bytes).Selection with { Files = [new("different-Q4_0.gguf", bytes.Length)] } };
        try { await models.AcquireAsync(changed, transport); throw new Exception("expected canonical collision"); }
        catch (ModelSourceException e) { Check(e.Code == "canonical-selection-collision", "same canonical name cannot adopt different variant"); }
    }
    private sealed class Menu : IModelSelectionInteraction
    {
        public int Choices;
        public bool IsInteractive => false;
        public void Show(string text) { }
        public Task<int?> ChooseAsync(IReadOnlyList<string> options, CancellationToken cancellation)
        { Choices++; throw new Exception("Unexpected menu"); }
    }
    private sealed class Metadata(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            var commit = new string('a', 40);
            var content = request.RequestUri!.AbsolutePath.Contains("/revision/") ?
                $$"""{"id":"fixture/repository","sha":"{{commit}}"}""" :
                $$$"""[{"type":"file","path":"model-Q4_0.gguf","size":{{{bytes.Length}}},"oid":"{{{commit}}}","lfs":{"oid":"{{{hash}}}","size":{{{bytes.Length}}}}},{"type":"file","path":"mmproj-F16.gguf","size":{{{bytes.Length}}},"oid":"{{{commit}}}","lfs":{"oid":"{{{hash}}}","size":{{{bytes.Length}}}}}]""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(content) });
        }
    }
    private static async Task UnitTests(string root, byte[] bytes)
    {
        var globalContext = ScopeContext.Global(new Coquena.Storage.Home(Path.Combine(root, "home")));
        var global = new ScopedModels(globalContext);
        var installed = await global.AcquireAsync(Selection(bytes), new Transport(bytes));
        Check(global.List().Count == 1 && installed.EntryPath.StartsWith(globalContext.Root), "global inventory has its own physical slot");
        var snapshot = new ScopeStore(globalContext).Read();
        var query = global.Query();
        Check(query.Count == 1 && new ScopeStore(globalContext).Read().Revision == snapshot.Revision, "global query does not mutate inventory");
        var olderDocs = snapshot.Documents.ToDictionary(p => p.Key, p => (JsonObject)p.Value.DeepClone());
        foreach (var doc in olderDocs.Values) doc["revision"] = doc["revision"]!.GetValue<long>() + 1;
        var olderFile = olderDocs["local/inventory.json"]["data"]!["resources"]![0]!["installations"]![0]!["files"]![0]!.AsObject();
        olderFile.Remove("volume"); olderFile.Remove("file-id");
        new ScopeStore(globalContext).Commit(snapshot.Revision, olderDocs);
        var uncertain = global.Remove(installed.ResourceId, false);
        Check(!uncertain.Complete && uncertain.Pending.Any(p => p.Contains("file-ownership-uncertain")) && File.Exists(installed.EntryPath), "older installation without file identity remains pending");
        var extRoot = Path.Combine(root, "externals"); Directory.CreateDirectory(extRoot);
        var extContext = ScopeContext.Project(Path.Combine(extRoot, "coquena.yaml"));
        var externalFile = Path.Combine(extRoot, "foreign.gguf"); File.WriteAllBytes(externalFile, bytes);
        var identity = new RecipeRegistry(new ScopeStore(extContext)).RegisterResource(Selection(bytes).Name, "model", "external");
        var external = new ScopedModels(extContext);
        try { await external.AcquireAsync(Selection(bytes), new Transport(bytes)); throw new Exception("expected ownership conflict"); }
        catch (ModelSourceException e) { Check(e.Code == "resource-ownership-conflict", "explicit download cannot adopt an external"); }
        Check(external.Remove(identity.ResourceId, false).Complete && File.Exists(externalFile), "external withdrawal never deletes bytes");
        var shardsRoot = Path.Combine(root, "shards"); Directory.CreateDirectory(shardsRoot);
        var shardsContext = ScopeContext.Project(Path.Combine(shardsRoot, "coquena.yaml"));
        var shards = new ScopedModels(shardsContext);
        var parts = new[] { ExternalModelTests.Fixture(0, 2), ExternalModelTests.Fixture(1, 2) };
        var files = parts.Select((part, i) => new RemoteFile($"model-Q4_0-{i + 1:00000}-of-00002.gguf", part.Length,
            new("sha256", "file-bytes", Convert.ToHexString(SHA256.HashData(part)).ToLowerInvariant()))).ToArray();
        var selected = Selection(bytes) with { Selection = Selection(bytes).Selection with { Files = files } };
        var transport = new Parts(parts);
        var prepared = await shards.AcquireAsync(selected, transport);
        Check(shards.List().Count == 1 && transport.Calls == 2 && shards.List()[0]!["installations"]![0]!["files"]!.AsArray().Count == 2, "shards publish as one resource");
        var path2 = Path.Combine(Path.GetDirectoryName(prepared.EntryPath)!, files[1].Path);
        File.Delete(path2);
        Check(shards.Observe(shards.List()[0]!.AsObject())["installations"]![0]!["observed-availability"]!.GetValue<string>() == "incomplete", "missing shard availability");
        var removed = shards.Remove(prepared.ResourceId, false);
        Check(removed.Complete && removed.FreedBytes == parts[0].Length, "missing shard is not counted as released bytes");
        var id = Guid.NewGuid(); var remote = Selection(bytes).Selection.Files[0];
        var pending = shards.Transfers.Create(new(1, id, "model", "fixture/repository:Q4_0", Selection(bytes).Selection, null,
            [new(remote.Path, $"downloads/{id:D}/files/0.part", "models/" + remote.Path, 0, remote.Size, remote.Identity, null, null, "pending")],
            "pending", "transfer", DateTimeOffset.UtcNow, null));
        var calls = transport.Calls;
        Check(shards.Query()[0]!["operations"]![0]!["id"]!.GetValue<string>() == pending.Id.ToString("D") && transport.Calls == calls,
            "query exposes known pending progress without transfer");
    }
    private sealed class Parts(byte[][] parts) : IDownloadTransport
    {
        public int Calls;
        public Task<HttpResponseMessage> OpenAsync(OperationDocument operation, int file, long offset, CancellationToken cancellation)
        { Calls++; return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(parts[file]) }); }
    }
}
