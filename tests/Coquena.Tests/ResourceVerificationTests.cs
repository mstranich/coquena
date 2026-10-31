using System.Text.Json.Nodes;
using System.Security.Cryptography;
using Coquena.Models;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class ResourceVerificationTests
{
    public static async Task Run(string? product = null)
    {
        var checks = 0;
        void Check(bool condition, string message) { if (!condition) throw new Exception(message); checks++; }
        var root = Path.Combine("D:/tmp", "coquena-verification-" + Guid.NewGuid()); Directory.CreateDirectory(root);
        var yaml = Path.Combine(root, "coquena.yaml");
        File.WriteAllText(yaml, "models:\n  local:\n    files: [weights.gguf]\npresets:\n  p:\n    args:\n      model: model://local\n");
        var file = Path.Combine(root, "weights.gguf");
        using (var writer = new BinaryWriter(File.Create(file))) { writer.Write("GGUF"u8); writer.Write(3u); writer.Write(0ul); writer.Write(0ul); }
        var bytes = File.ReadAllBytes(file);
        var context = ScopeContext.Project(yaml); var store = new ScopeStore(context); var models = new ScopedModels(context);
        var resource = models.AddExternal("weights.gguf", "local");
        var prepared = await new CombinationPreparation(context).PrepareAsync(new(root), new ExistingResource(resource));
        var resolutionPath = $"combinations/{prepared.Id}/resolved.json";
        var expectedResolution = store.Read().Documents[resolutionPath]["data"]!.ToJsonString();
        var verifier = new ResourceVerification(context);
        var selected = new VerificationSelection("local", "model", resource.ResourceId, resource.InstallationId);
        JsonNode Installation() => models.List().Single()!["installations"]![0]!;
        var original = File.ReadAllText(yaml);
        var report = await verifier.VerifyAsync([selected]);
        Check(!report.Verified && report.Resources[0]!["status"]!.GetValue<string>() == "no-reference", "external cannot be certified without baseline");
        Check(Installation()["files"]![0]!["integrity"] is null && File.ReadAllText(yaml) == original, "verify never creates baseline or edits intention");
        await verifier.SetBaselineAsync(resource.ResourceId, resource.InstallationId);
        var reference = Installation()["files"]![0]!["integrity"]!.ToJsonString();
        Check((await verifier.VerifyAsync([selected])).Verified, "explicit baseline enables a later successful comparison");
        File.AppendAllText(file, "corruption");
        Check(!(await verifier.VerifyAsync([selected])).Verified && Installation()["integrity-status"]!.GetValue<string>() == "invalid", "mismatch persists known failure");
        Check(Installation()["files"]![0]!["integrity"]!.ToJsonString() == reference, "mismatch preserves expected digest");
        Check(store.Read().Documents[resolutionPath]["data"]!.ToJsonString() == expectedResolution, "verification observations never rewrite combination resolutions");
        try { models.PrepareLocal(ModelIntent.FromRecipe(RecipeReader.Read(new(root))).Single()); throw new Exception("bad local accepted"); }
        catch (ModelSourceException e) { Check(e.Code == "model-integrity-invalid", "local preparation consults known failure"); }
        Check((await verifier.VerifyAsync([])).Verified && Installation()["integrity-status"]!.GetValue<string>() == "invalid", "excluding resource never repairs it");
        await verifier.SetBaselineAsync(resource.ResourceId, resource.InstallationId);
        Check(!(await verifier.VerifyAsync([selected])).Verified, "rebasing cannot erase original mismatch");
        File.WriteAllBytes(file, bytes);
        await verifier.SetBaselineAsync(resource.ResourceId, resource.InstallationId);
        Check((await verifier.VerifyAsync([selected])).Verified, "successful comparison against original bytes clears concrete failure");
        File.Delete(file);
        report = await verifier.VerifyAsync([selected]);
        Check(report.Resources[0]!["status"]!.GetValue<string>() == "missing-file", "missing is distinct from mismatch and no-reference");
        File.WriteAllBytes(file, bytes);
        Check((await verifier.VerifyAsync([selected])).Verified, "restored bytes require successful verification");
        using var output = new StringWriter(); using var error = new StringWriter();
        Check(Cli.Run(["verify", "--base", root, "--select", "model:unknown"], output, error) == 2, "unknown selector fails without auditing all resources");
        Check(Cli.Run(["verify", "--base", root, "--select", "model:local"], output, error) == 2, "even valid resource filters require global scope");
        Check(Cli.Run(["verify", "--base", root], output, error) == 0, "CLI audits selected local resource without preparing");
        var revisionBeforeInvalid = store.Read().Revision;
        File.WriteAllText(yaml, "presets: [");
        Check(Cli.Run(["verify", "--base", root], output, error) == 2 && store.Read().Revision == revisionBeforeInvalid, "invalid YAML is revalidated before observations and has no persistent resource effect");
        File.WriteAllText(yaml, original);
        File.WriteAllText(yaml, original.Replace("model://local", "{literal: hello}"));
        File.AppendAllText(file, "corruption");
        Check(Cli.Run(["verify", "--base", root], output, error) == 0, "unused inventory is excluded by default");
        Check(Cli.Run(["verify", "--base", root, "--include-unused"], output, error) == 2, "include-unused is exclusive to global verification");
        Check(!(await verifier.VerifyAsync([], includeUnused: true)).Verified, "inventory audit explicitly includes unused installation");
        File.WriteAllBytes(file, bytes); await verifier.VerifyAsync([selected]);
        await ManagedModels(root, Check);
        await CrossScope(context, selected, file, bytes, Check);
        var previousHome = Environment.GetEnvironmentVariable("COQUENA_HOME");
        var globalHome = new Home(Path.Combine(root, "global-catalog"));
        Environment.SetEnvironmentVariable("COQUENA_HOME", globalHome.Root);
        try
        {
            var globalContext = ScopeContext.Global(globalHome);
            var globalFile = Path.Combine(root, "global-weights.gguf"); File.Copy(file, globalFile);
            var globalModel = new ScopedModels(globalContext).AddExternal(globalFile, "global-model");
            var globalVerifier = new ResourceVerification(globalContext);
            await globalVerifier.SetBaselineAsync(globalModel.ResourceId, globalModel.InstallationId);
            output.GetStringBuilder().Clear();
            Check(Cli.Run(["verify", "--global"], output, error) == 0 && !output.ToString().Contains("global-model"), "global default excludes unreferenced catalog installation");
            output.GetStringBuilder().Clear();
            Check(Cli.Run(["verify", "--global", "--select", "model:global-model"], output, error) == 0 && output.ToString().Contains("verified"), "global selector can explicitly audit an unused resource");
            File.AppendAllText(globalFile, "global corruption");
            Check(Cli.Run(["verify", "--global", "--include-unused"], output, error) == 6, "global include-unused detects unreferenced corruption");
            File.WriteAllText(yaml, original);
            Check(Cli.Run(["verify", "--base", root], output, error) == 0, "project audit never includes corrupt global catalog resource");
            Check(Cli.Run(["verify", "--global", "--include-unused", "--select", "model:unknown"], output, error) == 2, "unknown global selector cannot broaden audit");
            File.WriteAllBytes(globalFile, bytes);
        }
        finally { Environment.SetEnvironmentVariable("COQUENA_HOME", previousHome); }
        var unprepared = Path.Combine(root, "unprepared"); Directory.CreateDirectory(unprepared);
        File.Copy(file, Path.Combine(unprepared, "weights.gguf")); File.WriteAllText(Path.Combine(unprepared, "coquena.yaml"), original);
        output.GetStringBuilder().Clear();
        Check(Cli.Run(["verify", "--base", unprepared], output, error) == 6 && output.ToString().Contains("no-reference"), "unregistered local bytes are read without implicit certification");
        Check(!new ScopedModels(ScopeContext.Project(Path.Combine(unprepared, "coquena.yaml"))).List().Any(), "audit does not register ownership or resources");
        Check(File.ReadAllText(Path.Combine(unprepared, ".coquena", ".gitignore")).Contains("/local/"), "unprepared recipe verification excludes local observations from Git");
        using (var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            report = await verifier.VerifyAsync([selected]);
            Check(report.Resources[0]!["status"]!.GetValue<string>() == "read-error", "read error is persisted independently of absence");
        }
        Check((await verifier.VerifyAsync([selected])).Verified, "successful check clears read failure");
        var beforeCancel = store.Read().Revision;
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try { await verifier.VerifyAsync([selected], cancellation: cancelled.Token); throw new Exception("cancel ignored"); }
            catch (OperationCanceledException) { Check(store.Read().Revision == beforeCancel, "cancel preserves preceding observations"); }
        }
        await new ResourceVerification(context, _ => File.AppendAllText(file, "change")).VerifyAsync([selected]);
        Check(Installation()["verification"]!["status"]!.GetValue<string>() == "changed-during-read", "whole unit rechecks earlier observations before publication");
        File.WriteAllBytes(file, bytes); await verifier.VerifyAsync([selected]);
        var managedId = Guid.NewGuid().ToString("D"); var managedInstall = Guid.NewGuid().ToString("D");
        var location = "backends/installations/" + managedInstall; Directory.CreateDirectory(context.PathFor(location));
        File.WriteAllText(context.PathFor(location + "/server.exe"), "fixture executable");
        File.WriteAllText(context.PathFor(location + "/dependency.dll"), "fixture dependency");
        var state = store.Read();
        var docs = state.Documents.ToDictionary(p => p.Key, p => ProjectDocuments.Create(p.Value["kind"]!.GetValue<string>(), p.Value["revision"]!.GetValue<long>() + 1, p.Value["data"]!.AsObject()));
        var managedFiles = new JsonArray(new[] { "server.exe", "dependency.dll" }.Select(p => (JsonNode?)new JsonObject
        { ["path"] = p, ["size"] = new FileInfo(context.PathFor(location + "/" + p)).Length, ["integrity"] = new JsonObject
            { ["algorithm"] = "sha256", ["domain"] = "file-bytes", ["value"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(context.PathFor(location + "/" + p)))) } }).ToArray());
        docs["local/inventory.json"]["data"]!["resources"]!.AsArray().Add((JsonNode)new JsonObject
        { ["resource-id"] = managedId, ["logical-key"] = "fixture-backend", ["kind"] = "backend", ["ownership"] = "managed",
            ["installations"] = new JsonArray(new JsonObject { ["installation-id"] = managedInstall, ["location"] = location, ["files"] = managedFiles }) });
        store.Commit(state.Revision, docs);
        var backend = new VerificationSelection("backend", "backend", managedId, managedInstall);
        Check((await verifier.VerifyAsync([backend])).Verified, "managed backend verifies executable and dependency against full digest");
        File.WriteAllText(context.PathFor(location + "/dependency.dll"), "damaged dependency");
        report = await verifier.VerifyAsync([backend]);
        Check(!report.Verified && report.Resources[0]!["files"]![1]!["status"]!.GetValue<string>() == "mismatch", "dependency corruption fails complete backend unit");
        // Keep the native scenario scoped to the selected healthy model.
        if (product is not null)
        {
            var start = new System.Diagnostics.ProcessStartInfo(product) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            File.WriteAllText(yaml, original);
            foreach (var arg in new[] { "verify", "--base", root }) start.ArgumentList.Add(arg);
            start.Environment["COQUENA_HOME"] = Path.Combine(root, "home");
            using var process = System.Diagnostics.Process.Start(start)!;
            var stdout = await process.StandardOutput.ReadToEndAsync(); var stderr = await process.StandardError.ReadToEndAsync(); await process.WaitForExitAsync();
            Check(process.ExitCode == 0 && stdout.Contains("verified") && stderr.Length == 0, "published Native AOT executes full verification");
            File.AppendAllText(file, "native corruption");
            using var corrupt = System.Diagnostics.Process.Start(start)!;
            var corruptOut = await corrupt.StandardOutput.ReadToEndAsync(); var corruptError = await corrupt.StandardError.ReadToEndAsync(); await corrupt.WaitForExitAsync();
            Check(corrupt.ExitCode == 6 && corruptOut.Contains("mismatch") && corruptError.Length == 0, "published Native AOT detects corruption without repair");
            File.WriteAllBytes(file, bytes);
            start.ArgumentList.Add("--include-unused");
            using var invalid = System.Diagnostics.Process.Start(start)!;
            _ = await invalid.StandardOutput.ReadToEndAsync(); var invalidError = await invalid.StandardError.ReadToEndAsync(); await invalid.WaitForExitAsync();
            Check(invalid.ExitCode == 2 && invalidError.Contains("invalid-selection"), "published Native AOT rejects global-only flags in project scope");
        }
        Console.WriteLine($"Resource verification: {checks} checks passed.");
    }

    private static async Task ManagedModels(string root, Action<bool, string> check)
    {
        var folder = Path.Combine(root, "managed"); Directory.CreateDirectory(folder);
        var context = ScopeContext.Project(Path.Combine(folder, "coquena.yaml"));
        var models = new ScopedModels(context); var verifier = new ResourceVerification(context);
        var bytes = ScopedModelTests.Gguf(); var transport = new ScopedModelTests.Transport(bytes);
        var selected = ScopedModelTests.Selection(bytes);
        var first = await models.AcquireAsync(selected, transport, explicitlyRequested: false);
        using var output = new StringWriter(); using var error = new StringWriter();
        check(Cli.Run(["model", "baseline", selected.Name, "--base", folder], output, error) == 2, "managed baseline replacement is rejected by CLI");
        File.WriteAllText(Path.Combine(folder, "coquena.yaml"), "models:\n  main: fixture/repository:Q4_0\npresets:\n  p:\n    args:\n      model: model://main\n");
        check(Cli.Run(["verify", "--base", folder], output, error) == 0, "recipe audits explicit downloaded selector without requiring preparation");
        var selection = new VerificationSelection("main", "model", first.ResourceId, first.InstallationId);
        check((await verifier.VerifyAsync([selection])).Verified, "download provides comparable reference without remote request at verify");
        var reads = 0;
        await new ResourceVerification(context, _ => reads++).VerifyAsync([selection, selection with { Key = "alias" }]);
        check(reads == 1, "aliases sharing an installation do not read weights twice");
        File.AppendAllText(first.EntryPath, "corruption");
        check(!(await verifier.VerifyAsync([selection])).Verified, "managed corruption detected against download digest");
        models.Protect(first.InstallationId, true);
        check(!(await verifier.VerifyAsync([selection])).Verified && transport.Calls == 1, "promoting explicit protection cannot repair corruption");
        try { await models.AcquireAsync(selected, transport, explicitlyRequested: false); throw new Exception("automatic repair"); }
        catch (ModelSourceException e) { check(e.Code == "model-integrity-invalid", "automatic preparation never repairs known corruption"); }
        var replacement = await models.AcquireAsync(selected, transport);
        check(replacement.InstallationId != first.InstallationId && transport.Calls == 2 &&
            (await verifier.VerifyAsync([new("main", "model", replacement.ResourceId, replacement.InstallationId)])).Verified,
            "explicit complete verified download publishes healthy revision without overwriting corrupt revision");
        check(File.ReadAllBytes(first.EntryPath).Length > bytes.Length, "redownload preserves prior physical revision");
        var parts = new[] { ExternalModelTests.Fixture(0, 2), ExternalModelTests.Fixture(1, 2) };
        var paths = new[] { "model-Q4_0-00001-of-00002.gguf", "model-Q4_0-00002-of-00002.gguf" };
        foreach (var pair in paths.Zip(parts)) File.WriteAllBytes(Path.Combine(folder, pair.First), pair.Second);
        var shard = models.AddExternal(paths[0], "shards");
        await verifier.SetBaselineAsync(shard.ResourceId, shard.InstallationId);
        var shardSelection = new VerificationSelection("shards", "model", shard.ResourceId, shard.InstallationId);
        check((await verifier.VerifyAsync([shardSelection])).Resources[0]!["files"]!.AsArray().Count == 2, "all shards audited as a complete unit");
        File.AppendAllText(Path.Combine(folder, paths[1]), "corruption");
        var report = await verifier.VerifyAsync([shardSelection]);
        check(!report.Verified && report.Resources[0]!["files"]![0]!["status"]!.GetValue<string>() == "verified" &&
            report.Resources[0]!["files"]![1]!["status"]!.GetValue<string>() == "mismatch", "per-shard mismatch fails whole unit without tainting healthy file");
        var companionFile = Path.Combine(folder, "vision.gguf"); File.WriteAllBytes(companionFile, bytes);
        var companion = models.AddExternal(companionFile, "vision", companion: true, type: "mmproj");
        await verifier.SetBaselineAsync(companion.ResourceId, companion.InstallationId);
        File.Delete(companionFile);
        check(!(await verifier.VerifyAsync([new("vision", "companion", companion.ResourceId, companion.InstallationId)])).Verified,
            "companions follow the same availability and integrity checks");
    }

    private static async Task CrossScope(ScopeContext owner, VerificationSelection model, string file, byte[] bytes, Action<bool, string> check)
    {
        var targetContext = ScopeContext.Global(new Home(Path.Combine(owner.Base, "shared-service")));
        var target = new ScopeStore(targetContext); var registry = new RecipeRegistry(target);
        var registered = registry.Register(["service.yaml"], "shared"); var service = registry.RegisterService(registered.RecipeId);
        var source = new ScopeStore(owner); var consumer = new RecipeRegistry(source).EnsureProjectConsumer([Path.Combine(owner.Base, "coquena.yaml")], "consumer");
        var publication = new Coquena.Servers.PresetPublication(source, target, service.ServiceId);
        var bindings = new JsonArray(new JsonObject { ["resource-id"] = model.ResourceId, ["installation-id"] = model.InstallationId });
        var preset = new Coquena.Servers.PublishedPreset("consumer/p", "[consumer/p]\nmodel=" + file + "\n", [], bindings, false);
        await publication.PublishAsync(consumer.RecipeId, [preset], "fixture", "consumer", true, true, _ => true, null);
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start(); var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
        var plan = new Coquena.Servers.ScopedStartPlan(Path.Combine(AppContext.BaseDirectory, "Coquena.Tests.exe"),
            ["--scoped-health-child", "--port", port.ToString()], Path.Combine(owner.Base, "health.log"), "fixture", new JsonObject(), [],
            new JsonObject { ["host"] = "127.0.0.1", ["port"] = port }, new Uri($"http://127.0.0.1:{port}/health"), TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100));
        var lifecycle = new Coquena.Servers.ScopedServiceProcess(target);
        using (var locked = new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            await lifecycle.StartAsync(service.ServiceId, plan);
        check(lifecycle.Reconcile(service.ServiceId), "startup checks known observations without reading locked model bytes");
        try
        {
            var active = target.Read().ActiveDocuments().Single().DeepClone();
            File.AppendAllText(file, "cross-scope corruption"); await new ResourceVerification(owner).VerifyAsync([model]);
            check(lifecycle.Reconcile(service.ServiceId), "verify leaves active process running");
            foreach (var detached in new[] { false, true })
            {
                var restores = 0;
                try
                {
                    await lifecycle.RunAsync(service.ServiceId, true, detached, _ => Task.FromResult(plan),
                        (_, _) => { restores++; return Task.FromResult(plan); }); throw new Exception("bad cross-scope accepted");
                }
                catch (StorageException e)
                { check(e.Code == "resource-integrity-invalid" && restores == 0 && lifecycle.Reconcile(service.ServiceId), "cross-scope known failure blocks both modes without fallback or stop"); }
            }
            check(JsonNode.DeepEquals(active, target.Read().ActiveDocuments().Single()), "failed restart preserves exact active snapshot");
        }
        finally
        {
            lifecycle.Stop(service.ServiceId); File.WriteAllBytes(file, bytes); await new ResourceVerification(owner).VerifyAsync([model]);
        }
    }

    private sealed class ExistingResource(PreparedModel resource) : ICombinationResources
    {
        public Task<PreparationResource> PrepareAsync(PreparationRequest request, JsonObject? exact, CancellationToken cancellation)
            => Task.FromResult(new PreparationResource(request.Key, request.Kind, resource.ResourceId, resource.InstallationId,
                resource.EntryPath, new JsonObject { ["origin"] = "external" }));
    }
}
