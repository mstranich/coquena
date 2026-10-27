using System.Text.Json.Nodes;
using System.Net;
using System.Security.Cryptography;
using Coquena;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Models;
using Coquena.Projects;
using Coquena.Recipes;

namespace Coquena.Tests;

internal static class ScopedLocalModelTests
{
    private static int checks;
    private static void Check(bool value, string message)
    { if (!value) throw new Exception(message); checks++; }
    private static void Bad(Action action, string code)
    { try { action(); throw new Exception("Expected " + code); } catch (ModelSourceException e) { Check(e.Code == code, code); } }
    public static async Task Run(string? product = null)
    {
        var root = Path.GetFullPath(Path.Combine("D:/tmp", "coquena-local-" + Guid.NewGuid()));
        Directory.CreateDirectory(root);
        var file = Path.GetFullPath(Path.Combine(root, "weights.gguf"));
        using (var stream = File.Create(file))
        using (var writer = new BinaryWriter(stream))
        { writer.Write("GGUF"u8); writer.Write(3u); writer.Write(0ul); writer.Write(0ul); }
        var yaml = Path.Combine(root, "coquena.yaml");
        File.WriteAllText(yaml, "models:\n  a: fixture/repository:Q4_0\npresets:\n  p:\n    args:\n      model: model://a\n      draft: fixture/repository:Q4_0\n      text: {literal: 'model://missing'}\n");
        var context = ScopeContext.Project(yaml);
        var models = new ScopedModels(context);
        var added = models.AddExternal("weights.gguf", "fixture/repository:Q4_0");
        Check(added.EntryPath == file && File.Exists(file), "external registered in place");
        Bad(() => models.AddExternal(file, "fixture/other:Q4_0"), "model-collision");
        var recipe = RecipeReader.Read(new(root));
        models.BindExternal("a", "model", added);
        Check(models.ResolveReference(new("model", "a")) == file, "typed alias resolves external entry");
        Check(models.ResolveReference(new("model", "fixture/repository:Q4_0", true)) == file, "direct selector shares identity");
        var block = ArgumentValidation.Validate(recipe).Single();
        var tokens = ArgumentEmission.Cli(recipe, block, resolve: models.ResolveReference);
        Check(tokens.Count(t => t == file) == 2 && tokens.Contains("model://missing"), "reference emission and literal");
        Check(models.Remove(added.ResourceId, false).FreedBytes == 0 && File.Exists(file), "remove external preserves bytes");
        Bad(() => models.ResolveReference(new("model", "a")), "unknown-resource");
        var again = models.AddExternal(file, "fixture/repository:Q4_0");
        Check(again.ResourceId != added.ResourceId, "new registration cannot reconnect broken alias");
        Bad(() => models.ResolveReference(new("model", "a")), "unknown-resource");
        var companion = models.AddExternal(file + ".missing.gguf", "fixture/repository#mmproj:BF16", true);
        Check(models.Query(true)[0]!["installations"]![0]!["observed-availability"]!.GetValue<string>() == "inaccessible", "missing external retained");
        Bad(() => models.ResolveReference(new("companion", "fixture/repository#mmproj:BF16", true)), "model-unavailable");
        await Transition(root, file);
        await InterruptedTransition(root, file);
        await Matching(root, file);
        ShardsAndMove(root);
        OriginsAndValidation(root, file);
        LocalNames(root, file);
        if (product is not null) await Native(product, root, file);
        Console.WriteLine($"Scoped local models: {checks} checks passed.");
    }

    private static async Task Transition(string root, string file)
    {
        var folder = Path.Combine(root, "transition"); Directory.CreateDirectory(folder);
        var context = ScopeContext.Project(Path.Combine(folder, "coquena.yaml"));
        var models = new ScopedModels(context);
        var added = models.AddExternal(file, "fixture/repository:Q4_0");
        models.BindExternal("a", "model", added);
        var bytes = File.ReadAllBytes(file);
        var selected = new SelectedModel("fixture/repository:Q4_0", null, new("fixture/repository", "main", new string('a', 40),
            [new("model.gguf", bytes.Length, new("sha256", "file-bytes", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()))]));
        try { await models.AcquireAsync(selected, new Transport(bytes)); throw new Exception("implicit adoption"); }
        catch (ModelSourceException e) { Check(e.Code == "resource-ownership-conflict", "implicit transition rejected"); }
        var bad = new Transport(new byte[bytes.Length]);
        try { await models.AcquireAsync(selected, bad, replaceExternal: true); throw new Exception("bad bytes accepted"); }
        catch (DownloadException) { Check(models.List()[0]!["ownership"]!.GetValue<string>() == "external" && File.ReadAllBytes(file).SequenceEqual(bytes), "failed verification preserves external"); }
        var pending = models.Transfers.Downloads.List().Single();
        Bad(() => models.ResolveReference(new("model", "a")), "resource-operation-pending");
        await models.Transfers.ControlAsync(pending.Id, true);
        Check(models.ResolveReference(new("model", "a")) == file, "cancel releases external reservation");
        var managed = await models.AcquireAsync(selected, new Transport(bytes), replaceExternal: true);
        Check(managed.ResourceId == added.ResourceId && managed.EntryPath != file && File.Exists(file), "explicit transition preserves ID and original");
        Check(models.ResolveReference(new("model", "a")) == managed.EntryPath, "alias follows verified managed revision");
        models.Remove(managed.ResourceId, false);
        Check(File.Exists(file) && !File.Exists(managed.EntryPath), "managed removal preserves original external bytes");

        added = models.AddExternal(file, selected.Name);
        var snapshot = new ScopeStore(context).Read(); var docs = snapshot.Documents.ToDictionary(p => p.Key, p => (JsonObject)p.Value.DeepClone());
        foreach (var doc in docs.Values) doc["revision"] = doc["revision"]!.GetValue<long>() + 1;
        docs["local/active.json"] = ProjectDocuments.Create("active", 1, new JsonObject { ["resource"] = added.ResourceId });
        new ScopeStore(context).Commit(snapshot.Revision, docs);
        try { await models.AcquireAsync(selected, new Transport(bytes), replaceExternal: true); throw new Exception("active transition"); }
        catch (ModelSourceException e) { Check(e.Code == "resource-in-use", "active external blocks transition"); }

        var staleFolder = Path.Combine(root, "changed-external"); Directory.CreateDirectory(staleFolder);
        var staleContext = ScopeContext.Project(Path.Combine(staleFolder, "coquena.yaml"));
        var stale = new ScopedModels(staleContext); stale.AddExternal(file, selected.Name);
        var modified = false;
        try
        {
            await stale.AcquireAsync(selected, new Transport(bytes), progress: _ =>
            {
                if (modified) return; modified = true;
                var store = new ScopeStore(staleContext); var state = store.Read();
                var changed = state.Documents.ToDictionary(p => p.Key, p => (JsonObject)p.Value.DeepClone());
                foreach (var doc in changed.Values) doc["revision"] = doc["revision"]!.GetValue<long>() + 1;
                changed["local/inventory.json"]["data"]!["resources"]![0]!["installations"]![0]!["protected"] = false;
                store.Commit(state.Revision, changed);
            }, replaceExternal: true);
            throw new Exception("changed external adopted");
        }
        catch (DownloadException)
        { Check(stale.List()[0]!["ownership"]!.GetValue<string>() == "external" && File.Exists(file), "changed original registration blocks publication"); }
        await stale.Transfers.ControlAsync(stale.Transfers.Downloads.List().Single().Id, true);
        Check(stale.List()[0]!["ownership"]!.GetValue<string>() == "external", "cancel after stale-publication failure keeps current external");
    }

    private static async Task Matching(string root, string file)
    {
        var folder = Path.Combine(root, "match"); Directory.CreateDirectory(folder);
        var models = new ScopedModels(ScopeContext.Project(Path.Combine(folder, "coquena.yaml")));
        using var handler = new Metadata(new FileInfo(file).Length); using var client = new HttpClient(handler);
        var source = new HuggingFaceStore(client);
        using var output = new StringWriter(); using var error = new StringWriter();
        int Add(string name, IModelSelectionInteraction interaction)
        {
            var copy = Path.Combine(folder, name + ".gguf"); File.Copy(file, copy);
            return ScopedModelCli.Run(["add", copy, "--name", "fixture/repository:" + name, "--base", folder, "--try-match"], output, error, source,
                interaction: interaction);
        }
        Check(Add("Q4_0", new Interaction(options =>
        {
            Check(models.List().Count == 1 && models.List()[0]!["informative-match"] is null, "registration durable before match menu");
            Check(options.Count == 2 && options[0].Contains("model-Q4_0.gguf"), "bounded matching choices");
            return 0;
        })) == 0, "CLI external add and match");
        Check(models.List()[0]!["informative-match"] is not null && models.List()[0]!["ownership"]!.GetValue<string>() == "external", "match informational only");
        var calls = handler.Calls;
        Check(Add("F16", new Interaction(_ => throw new Exception("no interaction"), false)) == 0 && handler.Calls == calls, "noninteractive skips network");
        handler.Fail = true;
        Check(Add("BF16", new Interaction(_ => 0)) == 0 && models.List().Count == 3, "network failure retains usable registration");
        handler.Fail = false;
        var cancel = models.AddExternal(file, "fixture/cancel:Q4_0");
        await models.TryMatchAsync(cancel, source, new Interaction(_ => null));
        Check(models.List().Single(r => r!["resource-id"]!.GetValue<string>() == cancel.ResourceId)!["informative-match"] is null, "cancel retains local registration");
    }

    private static async Task InterruptedTransition(string root, string file)
    {
        foreach (var stage in Enum.GetValues<ScopeTransferStage>())
        {
            var folder = Path.Combine(root, "interrupted-" + stage); Directory.CreateDirectory(folder);
            var context = ScopeContext.Project(Path.Combine(folder, "coquena.yaml")); var models = new ScopedModels(context);
            var external = models.AddExternal(file, "fixture/interrupted:Q4_0"); models.BindExternal("a", "model", external);
            var bytes = File.ReadAllBytes(file);
            var selection = new RemoteSelection("fixture/interrupted", "main", new string('a', 40),
                [new("model.gguf", bytes.Length, new("sha256", "file-bytes", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()))]);
            var engine = new ScopeTransfers(context, fault: current => { if (current == stage) throw new InvalidOperationException("fixture interruption"); });
            var id = Guid.NewGuid();
            var operation = engine.Create(new(1, id, "model", "fixture/interrupted:Q4_0", selection, null,
                [new("model.gguf", $"downloads/{id:D}/files/0.part", "unused", 0, bytes.Length, selection.Files[0].Identity, null, null, "pending")],
                "pending", "transfer", DateTimeOffset.UtcNow, null), externalPrevious: models.List()[0]!.AsObject());
            try { await engine.RunAsync(id, new Transport(bytes)); throw new Exception("missing interruption"); }
            catch (InvalidOperationException e) when (e.Message == "fixture interruption") { }
            Check(File.Exists(file), "interruption preserves external source " + stage);
            if (stage != ScopeTransferStage.InventoryPublished)
            {
                Check(models.List()[0]!["ownership"]!.GetValue<string>() == "external", "external stays current before inventory publication " + stage);
                Bad(() => models.Protect(external.InstallationId, false), "resource-operation-pending");
            }
            await new ScopeTransfers(context).RunAsync(operation.Id, new Transport(bytes));
            Check(models.List()[0]!["ownership"]!.GetValue<string>() == "managed" && models.List()[0]!["resource-id"]!.GetValue<string>() == external.ResourceId,
                "resume publishes same identity " + stage);
            Check(models.ResolveReference(new("model", "a")) != file && File.Exists(file), "resume updates alias without touching original " + stage);
        }
    }

    private static void ShardsAndMove(string root)
    {
        var folder = Path.Combine(root, "move"); Directory.CreateDirectory(folder);
        var first = Path.Combine(folder, "split-00001-of-00002.gguf");
        var second = Path.Combine(folder, "split-00002-of-00002.gguf");
        File.WriteAllBytes(first, ExternalModelTests.Fixture(0, 2));
        var models = new ScopedModels(ScopeContext.Project(Path.Combine(folder, "coquena.yaml")));
        var unit = models.AddExternal(Path.GetFileName(first), "fixture/shards:Q4_0");
        Check(models.Query()[0]!["installations"]![0]!["observed-availability"]!.GetValue<string>() == "incomplete", "incomplete shards registered as unit");
        Bad(() => models.ResolveReference(new("model", "fixture/shards:Q4_0", true)), "model-unavailable");
        File.WriteAllBytes(second, ExternalModelTests.Fixture(1, 2));
        Check(models.ResolveReference(new("model", "fixture/shards:Q4_0", true)) == first, "complete shards resolve to entry only");
        var destination = folder + "-moved";
        Directory.Move(folder, destination);
        models = new ScopedModels(ScopeContext.Project(Path.Combine(destination, "coquena.yaml")));
        Check(models.List()[0]!["resource-id"]!.GetValue<string>() == unit.ResourceId &&
            models.ResolveReference(new("model", "fixture/shards:Q4_0", true)) == Path.Combine(destination, Path.GetFileName(first)), "relative external survives project move");
        models.Remove(unit.ResourceId, false);
        Check(File.Exists(Path.Combine(destination, Path.GetFileName(second))), "shard removal preserves all external files");
    }

    private static async Task Native(string product, string root, string file)
    {
        var folder = Path.Combine(root, "native"); Directory.CreateDirectory(folder);
        async Task<JsonNode?> Run(params string[] args)
        {
            var start = new System.Diagnostics.ProcessStartInfo(product) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
            var environment = start.Environment.ToArray(); start.Environment.Clear();
            foreach (var group in environment.GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase)) start.Environment[group.Key] = group.Last().Value;
            start.Environment["COQUENA_HOME"] = Path.Combine(folder, "home");
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var process = System.Diagnostics.Process.Start(start)!; process.StandardInput.Close();
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await process.WaitForExitAsync(timeout.Token); }
            finally { if (!process.HasExited) process.Kill(true); }
            Check(process.ExitCode == 0, "native command: " + await stderr);
            return JsonNode.Parse(await stdout);
        }
        var added = await Run("model", "add", file, "--name", "fixture/native:Q4_0", "--base", folder, "--json", "--try-match");
        var list = await Run("model", "list", "--base", folder, "--json");
        Check(list![0]!["ownership"]!.GetValue<string>() == "external" && list[0]!["informative-match"] is null, "native add in noninteractive mode");
        await Run("model", "remove", added!["resource-id"]!.GetValue<string>(), "--base", folder);
        Check(File.Exists(file), "native removal retains external bytes");
        var named = await Run("model", "add", file, "--name", "my-model", "--base", folder, "--json", "--try-match");
        list = await Run("model", "list", "--base", folder, "--json");
        Check(list![0]!["logical-key"]!.GetValue<string>() == "my-model", "native local registered name");
        await Run("model", "remove", named!["resource-id"]!.GetValue<string>(), "--base", folder);
        var auxiliary = await Run("model", "companion", "add", file, "--name", "vision", "--type", "mmproj", "--base", folder, "--json");
        list = await Run("model", "companion", "list", "--base", folder, "--json");
        Check(list![0]!["companion-type"]!.GetValue<string>() == "mmproj", "native named companion keeps type");
        await Run("model", "companion", "remove", auxiliary!["resource-id"]!.GetValue<string>(), "--base", folder);
        Check(File.Exists(file), "native local names never adopt bytes");
    }

    private static void OriginsAndValidation(string root, string file)
    {
        var folder = Path.Combine(root, "origins"); Directory.CreateDirectory(folder);
        var models = new ScopedModels(ScopeContext.Global(new Coquena.Storage.Home(folder)));
        var registered = models.AddExternal(Path.GetFileName(file), "fixture/import:Q4_0", originBase: root);
        Check(registered.EntryPath == file && models.ResolveReference(new("model", "fixture/import:Q4_0", true)) == file,
            "global registration preserves original relative reference base");
        Check(Path.IsPathFullyQualified(models.List()[0]!["installations"]![0]!["files"]![0]!["path"]!.GetValue<string>()), "global import retains absolute external reference");
        File.WriteAllText(Path.Combine(folder, "coquena.yaml"), "service:\n  backend: {executable: ./server.exe}\n  server:\n    args:\n      x: fixture/import:Q4_0\npresets:\n  p:\n    args:\n      x: fixture/import:Q4_0\n      y: fixture/import\n");
        var recipe = RecipeReader.Read(new(folder));
        var blocks = ArgumentValidation.Validate(recipe);
        Check(blocks.Single(b => b.Scope == "server").Arguments["x"].Reference is null &&
            blocks.Single(b => b.Scope == "preset").Arguments["x"].Reference?.Canonical == true &&
            blocks.Single(b => b.Scope == "preset").Arguments["y"].Reference is null, "only complete preset canonical selectors are references");
        File.WriteAllText(Path.Combine(folder, "coquena.yaml"), "presets:\n  p:\n    args:\n      x: model://missing\n");
        try { ArgumentValidation.Validate(RecipeReader.Read(new(folder))); throw new Exception("missing reference accepted"); }
        catch (RecipeException e) { Check(e.Code == "unknown-resource-reference", "missing alias rejected in permissive mode"); }
        using var output = new StringWriter(); using var error = new StringWriter();
        foreach (var args in new[]
        {
            new[] { "add", file, "--base", folder },
            new[] { "add", file, "--name", "fixture/import#mmproj:Q4_0", "--base", folder },
            new[] { "list", "--try-match", "--base", folder },
            new[] { "add", file, "--name", "fixture/import:Q4_0", "--try-match", "yes", "--base", folder }
        }) Check(ScopedModelCli.Run(args, output, error) != 0, "invalid add options rejected");
        Check(!Directory.Exists(Path.Combine(folder, ".coquena")), "invalid CLI arguments have no state effects");
    }

    private static void LocalNames(string root, string source)
    {
        var folder = Path.Combine(root, "local-names"); Directory.CreateDirectory(folder);
        Directory.CreateDirectory(Path.Combine(folder, "weights"));
        var file = Path.Combine(folder, "weights/modelo.gguf"); File.Copy(source, file);
        File.WriteAllText(Path.Combine(folder, "coquena.yaml"), "models:\n  my-model:\n    files: [./weights/modelo.gguf]\npresets:\n  p:\n    args:\n      model: model://MY-MODEL\n");
        var recipe = RecipeReader.Read(new(folder));
        var intent = ModelIntent.FromRecipe(recipe).Single();
        var models = new ScopedModels(ScopeContext.Project(recipe.Selection.Files[0]));
        var first = models.PrepareLocal(intent);
        Check(models.List().Single()!["logical-key"]!.GetValue<string>() == "my-model", "YAML local key is the registered name");
        var second = models.PrepareLocal(intent);
        Check(first.ResourceId == second.ResourceId && first.InstallationId == second.InstallationId && models.List().Count == 1, "unchanged local recipe reuses registration");
        Check(models.ResolveReference(new("model", "my-model")) == Path.GetFullPath(file), "local registered name resolves entry");
        var emitted = ArgumentEmission.Cli(recipe, ArgumentValidation.Validate(recipe).Single(), resolve: models.ResolveReference);
        Check(emitted.Contains(Path.GetFullPath(file)), "YAML named local emits absolute path");
        Bad(() => models.PrepareLocal(intent with { Key = "another-name" }), "model-collision");
        var other = Path.Combine(folder, "other.gguf"); File.Copy(source, other);
        Bad(() => models.PrepareLocal(intent with { Files = ["other.gguf"] }), "model-collision");
        var companion = models.AddExternal(other, "vision", true, type: "mmproj");
        Check(models.ResolveReference(new("companion", "vision")) == other &&
            models.List(true).Single()!["companion-type"]!.GetValue<string>() == "mmproj", "named local companion preserves explicit type");
        Bad(() => models.AddExternal("missing.gguf", "ambiguous", true), "invalid-external-name");
        using var handler = new Metadata(24); using var client = new HttpClient(handler);
        models.TryMatchAsync(first, new HuggingFaceStore(client), new Interaction(_ => throw new Exception("no matching menu"))).GetAwaiter().GetResult();
        Check(handler.Calls == 0, "local name never invents matching repository");
        var viaReference = models.PrepareReferenceAsync(recipe, new("model", "MY-MODEL"), new HuggingFaceStore(client),
            new Transport([]), new Interaction(_ => throw new Exception("no remote menu"))).GetAwaiter().GetResult();
        Check(viaReference.ResourceId == first.ResourceId && handler.Calls == 0, "typed local preparation uses recipe base without network");
        var cliFolder = Path.Combine(root, "local-cli"); Directory.CreateDirectory(cliFolder);
        using var output = new StringWriter(); using var error = new StringWriter();
        Check(ScopedModelCli.Run(["add", file, "--name", "my-model", "--base", cliFolder], output, error) == 0, "CLI accepts the same local name");
        var cli = new ScopedModels(ScopeContext.Project(Path.Combine(cliFolder, "coquena.yaml")));
        Check(cli.ResolveReference(new("model", "my-model")) == Path.GetFullPath(file), "CLI local name works without extra alias");
        Check(ScopedModelCli.Run(["companion", "add", other, "--name", "vision", "--type", "mmproj", "--base", cliFolder], output, error) == 0,
            "CLI local companion type matches YAML vocabulary");
        var companionRecipe = Path.Combine(cliFolder, "companion.yaml");
        File.WriteAllText(companionRecipe, "companions:\n  vision:\n    type: mmproj\n    files: ['" + other.Replace('\\', '/') + "']\n");
        var localCompanion = ModelIntent.FromRecipe(RecipeReader.Read(new(cliFolder, [companionRecipe]))).Single();
        Check(cli.PrepareLocal(localCompanion).ResourceId == cli.List(true).Single()!["resource-id"]!.GetValue<string>(), "YAML reuses CLI companion by name and paths");
    }

    private sealed class Interaction(Func<IReadOnlyList<string>, int?> choose, bool interactive = true) : IModelSelectionInteraction
    {
        public bool IsInteractive => interactive;
        public void Show(string text) { }
        public Task<int?> ChooseAsync(IReadOnlyList<string> options, CancellationToken cancellation) => Task.FromResult(choose(options));
    }
    private sealed class Metadata(long size) : HttpMessageHandler
    {
        public int Calls; public bool Fail;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            Calls++; if (Fail) throw new HttpRequestException("fixture");
            if (!request.RequestUri!.AbsolutePath.StartsWith("/api/models/")) throw new Exception("weights request forbidden");
            var repository = request.RequestUri.AbsolutePath.Contains("/fixture/cancel/") ? "fixture/cancel" : "fixture/repository";
            var text = request.RequestUri.AbsolutePath.EndsWith("/revision/main") ? "{\"id\":\"" + repository + "\",\"sha\":\"" + new string('a', 40) + "\"}" :
                "[{\"type\":\"file\",\"path\":\"model-Q4_0.gguf\",\"size\":" + size + ",\"oid\":\"" + new string('b', 40) + "\"}]";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) });
        }
    }
    private sealed class Transport(byte[] bytes) : IDownloadTransport
    {
        public Task<HttpResponseMessage> OpenAsync(OperationDocument operation, int file, long offset, CancellationToken cancellation)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }
}
