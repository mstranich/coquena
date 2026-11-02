using System.Diagnostics;
using System.Text.Json.Nodes;
using Coquena.Projects;
using Coquena.Storage;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Coquena.Servers;
using Coquena.Contracts;
using Coquena.Downloads;

namespace Coquena.Tests;

internal static class CleanupTests
{
    public static void Run(string root, string? executable)
    {
        if (Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).Any()) throw new ArgumentException("La raíz de pruebas debe estar vacía.");
        Directory.CreateDirectory(root);
        var home = new Home(Path.Combine(root, "home"));
        var project = Path.Combine(root, "project"); Directory.CreateDirectory(project);
        var yaml = Path.Combine(project, "coquena.yaml"); File.WriteAllText(yaml, "models: {}\n");
        var context = ScopeContext.Project(yaml); var store = new ScopeStore(context);
        var checks = 0;
        void Check(bool ok, string message) { if (!ok) throw new Exception(message); checks++; }
        string Add(bool protect = false, string origin = "automatic")
        {
            var state = store.Read(); var docs = Next(state);
            if (!docs.ContainsKey("local/inventory.json")) docs["local/inventory.json"] = ProjectDocuments.Create("inventory", 1, new() { ["resources"] = new JsonArray() });
            var id = Guid.NewGuid().ToString("D"); var location = "models/installations/" + id;
            Directory.CreateDirectory(context.PathFor(location));
            var file = context.PathFor(location + "/weights.gguf"); File.WriteAllBytes(file, new byte[1024]);
            using var identityHandle = File.OpenHandle(file);
            if (!Information(identityHandle, 18, out var identity, 24)) throw new Exception("fixture identity unavailable");
            docs["local/inventory.json"]["data"]!["resources"]!.AsArray().Add((JsonNode)new JsonObject
            {
                ["resource-id"] = Guid.NewGuid().ToString("D"), ["logical-key"] = id, ["kind"] = "model", ["ownership"] = "managed",
                ["installations"] = new JsonArray((JsonNode)new JsonObject { ["installation-id"] = id, ["location"] = location,
                    ["origin"] = origin, ["protected"] = protect, ["files"] = new JsonArray((JsonNode)new JsonObject {
                        ["path"] = "weights.gguf", ["size"] = 1024, ["volume"] = identity.Volume.ToString("x16"), ["file-id"] = identity.Low.ToString("x16") + identity.High.ToString("x16") }) })
            });
            store.Commit(state.Revision, docs); return id;
        }
        string PathOf(string id) => context.PathFor("models/installations/" + id + "/weights.gguf");
        void Change(Action<Dictionary<string, JsonObject>> action)
        { var state = store.Read(); var docs = Next(state); action(docs); store.Commit(state.Revision, docs); }
        var cleanup = new ResourceCleanup(context, home);
        var first = Add(); var protectedId = Add(true); var explicitId = Add(true, "explicit");
        var neighbor = context.PathFor("models/installations/" + first + "/neighbor.txt"); File.WriteAllText(neighbor, "user");
        var plan = cleanup.Preview();
        Check(plan.Items.Count == 1 && plan.Bytes == 1024, "preview respects explicit protection");
        Check(File.Exists(PathOf(first)), "preview never deletes");
        var result = cleanup.Apply(plan);
        Check(result.FreedBytes == 1024 && !File.Exists(PathOf(first)) && File.Exists(neighbor), "delete owns files only and reports actual bytes");
        Check(File.Exists(PathOf(protectedId)) && File.Exists(PathOf(explicitId)), "protected bytes preserved");
        var changed = Add(); plan = cleanup.Preview();
        new Coquena.Models.ScopedModels(context).Protect(changed, true);
        try { cleanup.Apply(plan); throw new Exception("expected stale plan"); }
        catch (StorageException e) { Check(e.Code == "scope-conflict", "stale confirmation rejected"); }
        Check(File.Exists(PathOf(changed)), "stale plan preserves bytes");
        new Coquena.Models.ScopedModels(context).Protect(changed, false);
        var blocked = Add();
        using (var handle = new FileStream(PathOf(blocked), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            result = cleanup.Apply(cleanup.Preview());
            Check(result.FreedBytes == 1024 && result.Diagnostics.Count == 1 && File.Exists(PathOf(blocked)), "partial deletion persists retry diagnostics");
            Check(store.Read().Documents["local/inventory.json"].ToJsonString().Contains("cleanup-diagnostics"), "partial diagnostic persisted");
        }
        result = cleanup.Apply(cleanup.Preview());
        Check(result.FreedBytes == 1024 && result.Diagnostics.Count == 0, "retry reports only newly deleted bytes");
        var interior = Add(); var externalPath = PathOf(interior);
        Change(docs => docs["local/inventory.json"]["data"]!["resources"]!.AsArray().Add((JsonNode)new JsonObject {
            ["resource-id"] = Guid.NewGuid().ToString("D"), ["logical-key"] = "external", ["kind"] = "model", ["ownership"] = "external",
            ["installations"] = new JsonArray((JsonNode)new JsonObject { ["installation-id"] = Guid.NewGuid().ToString("D"), ["external"] = true,
                ["files"] = new JsonArray((JsonNode)new JsonObject { ["path"] = externalPath }) }) }));
        Check(!cleanup.Preview().Items.Any(i => i.InstallationId == interior) && File.Exists(externalPath), "external inside managed directory protects bytes");
        var replaced = Add(); File.Delete(PathOf(replaced)); File.WriteAllText(PathOf(replaced), "replacement");
        Check(!cleanup.Preview().Items.Any(i => i.InstallationId == replaced), "replaced identity never adopted");
        var pending = Add(); var operationId = Guid.NewGuid();
        var contentIdentity = new ContentIdentity("sha256", "file-bytes", new string('a', 64));
        var transfer = new ScopeTransfers(context);
        transfer.Create(new OperationDocument(1, operationId, "model", pending,
            new("fixture/repository", "main", new string('a', 40), [new("weights.gguf", 1024, contentIdentity)]), null,
            [new("weights.gguf", $"downloads/{operationId:D}/files/0.part", "models/weights.gguf", 0, 1024, contentIdentity, null, null, "pending")],
            "pending", "transfer", DateTimeOffset.UtcNow, null), "automatic");
        Check(!cleanup.Preview().Items.Any(i => i.InstallationId == pending), "pending operation protects prior installation of its resource");
        transfer.ControlAsync(operationId, true).GetAwaiter().GetResult();
        Check(cleanup.Preview().Items.Any(i => i.InstallationId == pending), "cancelled operation releases prior installation");
        var combination = new string('a', 64); var referenced = Add();
        Change(docs => {
            docs["local/combinations.json"] = ProjectDocuments.Create("combination-index", 1, new() { ["combinations"] = new JsonObject {
                [combination] = new JsonObject { ["files"] = new JsonArray(JsonValue.Create("coquena.yaml")), ["env-files"] = new JsonArray(),
                    ["bindings"] = new JsonArray((JsonNode)new JsonObject { ["installation-id"] = referenced }) } } });
            docs[$"combinations/{combination}/resolved.json"] = ProjectDocuments.Create("resolution", 1, new() {
                ["combination-id"] = combination, ["selections"] = new JsonObject(), ["resources"] = new JsonArray() });
            docs["local/last-good.json"] = ProjectDocuments.Create("last-good", 1, new() { ["combination"] = combination,
                ["resources"] = new JsonArray((JsonNode)new JsonObject { ["installation-id"] = referenced }) });
            docs["local/active.json"] = ProjectDocuments.Create("active", 1, new() { ["combination"] = combination,
                ["resources"] = new JsonArray((JsonNode)new JsonObject { ["installation-id"] = referenced }) });
        });
        Check(!cleanup.Preview().Items.Any(i => i.InstallationId == referenced), "combination active and last good protect");
        new CombinationCleanup(context).Forget(combination);
        Check(new CombinationPreparation(context).List().Count == 0 && File.Exists(yaml) && File.Exists(PathOf(referenced)), "forget preserves YAML and weights");
        Check(!store.Read().Documents.ContainsKey("local/last-good.json") && !File.Exists(context.PathFor("local/last-good.json")), "forget removes last good and its projection");
        Check(!cleanup.Preview().Items.Any(i => i.InstallationId == referenced), "active protection survives forget");
        Change(docs => docs["local/active.json"]["data"]!["terminated"] = true);
        Check(cleanup.Preview().Items.Any(i => i.InstallationId == referenced), "terminated active releases its reference");
        new Coquena.Models.ScopedModels(context).Protect(explicitId, false);
        Check(cleanup.Preview().Items.Any(i => i.InstallationId == explicitId) && File.Exists(PathOf(explicitId)), "unprotect permits later cleanup without deleting explicit bytes");
        new Coquena.Models.ScopedModels(context).Protect(explicitId, true);
        var older = Add(); var newer = Add(); var exactCombination = new string('c', 64);
        Change(docs => {
            var resources = docs["local/inventory.json"]["data"]!["resources"]!.AsArray();
            var previousResource = resources.Single(r => r!["installations"]!.AsArray().Any(i => i!["installation-id"]!.GetValue<string>() == older))!;
            var nextResource = resources.Single(r => r!["installations"]!.AsArray().Any(i => i!["installation-id"]!.GetValue<string>() == newer))!;
            var installation = nextResource["installations"]![0]!.DeepClone(); nextResource["installations"]!.AsArray().Clear();
            previousResource["installations"]!.AsArray().Add(installation);
            docs["local/combinations.json"]["data"]!["combinations"]![exactCombination] = new JsonObject {
                ["files"] = new JsonArray(JsonValue.Create("coquena.yaml")), ["env-files"] = new JsonArray(),
                ["bindings"] = new JsonArray((JsonNode)new JsonObject { ["resource-id"] = previousResource["resource-id"]!.DeepClone(), ["installation-id"] = newer }) };
        });
        var exactPlan = cleanup.Preview();
        Check(exactPlan.Items.Any(i => i.InstallationId == older) && !exactPlan.Items.Any(i => i.InstallationId == newer), "binding protects exact revision without retaining every revision of same resource");
        new CombinationCleanup(context).Forget(exactCombination);
        new ProjectIndex(home).Refresh(store);
        var other = Path.Combine(root, "other"); Directory.CreateDirectory(other);
        var otherContext = ScopeContext.Project(Path.Combine(other, "coquena.yaml")); var otherStore = new ScopeStore(otherContext);
        otherStore.Commit(0, new Dictionary<string, JsonObject> { ["local/inventory.json"] = ProjectDocuments.Create("inventory", 1, new() { ["resources"] = new JsonArray() }) });
        new ProjectIndex(home).Refresh(otherStore);
        Directory.Move(other, other + "-moved");
        try { cleanup.Preview(); throw new Exception("expected inaccessible root"); }
        catch (StorageException) { checks++; }
        Directory.Move(other + "-moved", other);
        var target = new ScopeStore(ScopeContext.Global(home)); var targetRegistry = new RecipeRegistry(target);
        var targetRecipe = targetRegistry.Register(["service.yaml"], "shared");
        var service = targetRegistry.RegisterService(targetRecipe.RecipeId);
        var ownRecipe = new RecipeRegistry(store).EnsureProjectConsumer([yaml], "one");
        var otherRecipe = new RecipeRegistry(otherStore).EnsureProjectConsumer([Path.Combine(other, "coquena.yaml")], "two");
        var published = Add();
        var publishedResource = store.Read().Documents["local/inventory.json"]["data"]!["resources"]!.AsArray()
            .Single(r => r!["installations"]!.AsArray().Any(i => i!["installation-id"]!.GetValue<string>() == published))!;
        var refs = new JsonArray((JsonNode)new JsonObject { ["resource-id"] = publishedResource["resource-id"]!.DeepClone(), ["installation-id"] = published });
        var publication = new PresetPublication(store, target, service.ServiceId);
        publication.PublishAsync(ownRecipe.RecipeId, [new("one/chat", "[one/chat]\n", [], refs, false)], combination, "one", true, true, _ => true, null).GetAwaiter().GetResult();
        new PresetPublication(otherStore, target, service.ServiceId).PublishAsync(otherRecipe.RecipeId,
            [new("two/chat", "[two/chat]\n", [], new JsonArray(), false)], combination, "two", true, true, _ => true, null).GetAwaiter().GetResult();
        Check(!cleanup.Preview().Items.Any(i => i.InstallationId == published), "cross-scope publication protects exact installation");
        publication.PublishAsync(ownRecipe.RecipeId, [], combination, "one", true, true, _ => true, null).GetAwaiter().GetResult();
        Check(cleanup.Preview().Items.Any(i => i.InstallationId == published), "confirmed withdrawal releases historical operation reference");
        cleanup.Apply(cleanup.Preview());
        Check(!File.Exists(PathOf(published)) && PresetPublication.CurrentIni(target, service.ServiceId).Contains("two/chat"), "cleanup preserves sibling contribution");
        if (executable is not null)
        {
            (int Code, string Text) Invoke(params string[] arguments)
            {
                var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true };
                start.Environment["COQUENA_HOME"] = home.Root;
                foreach (var argument in arguments) start.ArgumentList.Add(argument);
                using var process = Process.Start(start)!; process.StandardInput.Close();
                var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(30000)) { process.Kill(true); throw new Exception("timeout"); }
                return (process.ExitCode, output.GetAwaiter().GetResult() + error.GetAwaiter().GetResult());
            }
            var native = Add();
            Check(Invoke("clean", "--project-directory", project, "--dry-run").Code == 0 && File.Exists(PathOf(native)), "AOT dry run preserves bytes");
            Check(Invoke("clean", "--project-directory", project).Code == 2 && File.Exists(PathOf(native)), "AOT redirected input requires confirmation");
            var response = Invoke("clean", "--project-directory", project, "--yes");
            Check(response.Code == 0 && response.Text.Contains("Liberado:") && !File.Exists(PathOf(native)), "AOT confirmed cleanup");
            Check(Invoke("clean", "--project-directory", project, "--force").Code == 2, "AOT rejects force");
            Check(Invoke("combination", "list", "--project-directory", project).Code == 0, "AOT lists combinations");
            var nativeCombination = new string('b', 64);
            Change(docs => docs["local/combinations.json"]["data"]!["combinations"]![nativeCombination] = new JsonObject {
                ["files"] = new JsonArray(JsonValue.Create("coquena.yaml")), ["env-files"] = new JsonArray() });
            Check(Invoke("combination", "forget", nativeCombination, "--project-directory", project).Code == 0 &&
                !new CombinationPreparation(context).List().ContainsKey(nativeCombination) && File.Exists(yaml), "AOT forget preserves YAML");
        }
        Console.WriteLine($"OK: {checks} comprobaciones de limpieza" + (executable is null ? "." : " y CLI Native AOT."));
    }
    private static Dictionary<string, JsonObject> Next(ScopeSnapshot state) => state.Documents.ToDictionary(p => p.Key,
        p => ProjectDocuments.Create(p.Value["kind"]!.GetValue<string>(), p.Value["revision"]!.GetValue<long>() + 1, p.Value["data"]!.AsObject()));
    [StructLayout(LayoutKind.Sequential)] private struct Identity { public ulong Volume, Low, High; }
    [DllImport("kernel32.dll", EntryPoint = "GetFileInformationByHandleEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Information(SafeFileHandle handle, int kind, out Identity identity, uint size);
}
