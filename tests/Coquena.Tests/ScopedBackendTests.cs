using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Text.Json.Nodes;
using Coquena.Backends;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Projects;
using Coquena.Recipes;

namespace Coquena.Tests;

internal static class ScopedBackendTests
{
    private static int checks;
    private static void Check(bool value, string message) { checks++; if (!value) throw new Exception(message); }
    private static HardwareContext Hardware(string cuda = "compatible", int driver = 12090) => new("win-x64", "fixture", null, [], driver,
        [new("fixture", 86)], new(cuda, "fixture CUDA"), new("compatible", "fixture Vulkan"));
    private static ScopeContext Project(string folder)
    { Directory.CreateDirectory(folder); return ScopeContext.Project(Path.Combine(folder, "coquena.yaml")); }
    private static ServiceIdentity Service(ScopeContext context, string name = "fixture")
    {
        var registry = new RecipeRegistry(new ScopeStore(context));
        return registry.RegisterService(registry.Register([Path.Combine(context.Base, "coquena.yaml")], name).RecipeId);
    }
    private static byte[] Archive(params (string Path, byte[] Bytes)[] files)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
            foreach (var file in files) { using var output = archive.CreateEntry(file.Path).Open(); output.Write(file.Bytes); }
        return memory.ToArray();
    }
    internal sealed class Fixture : IDownloadTransport
    {
        public int Calls;
        public int Resolutions;
        public string Build = "b1";
        public readonly Dictionary<string, byte[]> Artifacts = new();
        public void Add(string build, string variant, byte[]? archive = null)
        {
            var tag = variant.StartsWith("cuda@", StringComparison.Ordinal) ? "cuda-" + variant[5..] + ".0" : variant;
            Artifacts[$"llama-{build}-bin-win-{tag}-x64.zip"] = archive ?? Archive(("llama-server.exe", System.Text.Encoding.UTF8.GetBytes(build[1..])), ("neighbor.dll", [1, 2, 3]));
            if (tag.StartsWith("cuda", StringComparison.Ordinal)) Artifacts[$"cudart-llama-bin-win-{tag}-x64.zip"] = Archive(("cudart.dll", [4, 5]));
        }
        public Task<BackendResolution> Resolve(HardwareContext hardware, string variant, string version, CancellationToken cancellation)
        {
            Resolutions++;
            var tag = version == "latest" ? Build : version;
            var release = new OfficialRelease(tag, Artifacts.Where(a => a.Key.StartsWith("llama-" + tag + "-", StringComparison.Ordinal) || a.Key.StartsWith("cudart-", StringComparison.Ordinal))
                .Select(a => new ReleaseAsset(a.Key, a.Value.Length, "")).ToArray());
            return Task.FromResult(BackendCatalog.Resolve([release], hardware, variant, tag));
        }
        public Task<HttpResponseMessage> OpenAsync(OperationDocument operation, int file, long offset, CancellationToken cancellation)
        {
            Check(offset == 0, "fixture starts at zero"); Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Artifacts[operation.Files[file].SourcePath]) });
        }
    }
    private static Task<string> Probe(string path, CancellationToken cancellation) => Task.FromResult($"version: {File.ReadAllText(path)} (abcdef)");
    private static async Task Expect(string code, Func<Task> action)
    {
        try { await action(); throw new Exception("Expected " + code); }
        catch (BackendException e) { Check(e.Code == code, "Expected " + code + ", got " + e.Code); }
        catch (DownloadException e) { Check(e.Code == code, "Expected " + code + ", got " + e.Code); }
        catch (Coquena.Storage.StorageException e) { Check(e.Code == code, "Expected " + code + ", got " + e.Code); }
    }
    public static async Task Run(string? executable = null)
    {
        var root = Path.Combine(@"D:\tmp", "coquena-048-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var a = Project(Path.Combine(root, "project")); var own = Service(a);
        var fixture = new Fixture(); fixture.Add("b1", "cpu"); fixture.Add("b2", "cpu"); fixture.Add("b1", "cuda@13"); fixture.Add("b1", "vulkan");
        var manager = new ScopedBackends(a, fixture.Resolve, Probe);
        var hardware = Hardware();
        var intent = new BackendIntent("latest", "cpu");
        var first = await manager.PrepareAsync(own.ServiceId, intent, a.Base, hardware, fixture);
        var before = fixture.Resolutions; var calls = fixture.Calls; fixture.Build = "b2";
        var reused = await manager.PrepareAsync(own.ServiceId, intent, a.Base, hardware, fixture);
        Check(first == reused && before == fixture.Resolutions && calls == fixture.Calls, "unchanged latest reuses exact local revision without network");
        var second = await manager.PrepareAsync(own.ServiceId, new("b2", "cpu"), a.Base, hardware, fixture);
        Check(second.InstallationId != first.InstallationId && File.Exists(first.Executable), "new pin does not overwrite earlier candidate");
        Check(new RecipeRegistry(new ScopeStore(a)).Services().Length == 1, "preparation does not create services");
        var entry = new ScopeStore(a).Read().Documents["local/recipes.json"]["data"]!["recipes"]![0]!["entities"]![0]!;
        Check(entry["instance"] is null && entry["backend-intents"]!.AsArray().Count == 2 && entry["backend-intents"]![0]!["installation-id"]!.GetValue<string>() == first.InstallationId,
            "intent history retains exact references and no running instance");
        Check(manager.List().SelectMany(r => r!["installations"]!.AsArray()).Single(i => i!["installation-id"]!.GetValue<string>() == second.InstallationId)!["protected"]!.GetValue<bool>(),
            "fixed build receives protection independently of explicit download");
        await manager.DownloadAsync(new("b1", "cpu"), hardware, fixture);
        Check(fixture.Calls == calls + 1 && manager.List().SelectMany(r => r!["installations"]!.AsArray())
            .Single(i => i!["installation-id"]!.GetValue<string>() == first.InstallationId)!["protected"]!.GetValue<bool>(), "explicit request protects automatic revision without redownload");
        manager.Protect(second.InstallationId, false);
        Check(manager.List().SelectMany(r => r!["installations"]!.AsArray()).Single(i => i!["installation-id"]!.GetValue<string>() == second.InstallationId)!["protected"]!.GetValue<bool>() == false,
            "unprotect preserves files and intent references");

        var warnings = new List<string>(); calls = fixture.Calls;
        await Expect("backend-incompatible", () => manager.PrepareAsync(own.ServiceId, new("b1", "cuda@13"), a.Base, hardware, fixture, warning: warnings.Add));
        Check(fixture.Calls == calls && warnings.Any(w => w.Contains("incompatible")), "driver incompatibility stops before transfer");
        var forced = await manager.PrepareAsync(own.ServiceId, new("b1", "cuda@13"), a.Base, hardware, fixture, true, warnings.Add);
        Check(forced.Variant == "cuda@13" && warnings.Any(w => w.Contains("--force")) && File.Exists(first.Executable), "force retains requested family, warning and previous revision");
        await Expect("backend-incompatible", () => manager.PrepareAsync(own.ServiceId, new("b1", "cuda@13"), a.Base, hardware, fixture));
        await manager.PrepareAsync(own.ServiceId, new("b1", "vulkan"), a.Base, hardware, fixture);
        var unknownFixture = new Fixture(); unknownFixture.Add("b1", "cuda@12");
        var unknown = new ScopedBackends(a, unknownFixture.Resolve, Probe);
        warnings.Clear();
        await unknown.DownloadAsync(new("b1", "cuda@12"), Hardware("unknown"), unknownFixture, warning: warnings.Add);
        Check(warnings.Any(w => w.Contains("desconocida")), "unknown evidence warns and continues");
        var missing = manager.List().SelectMany(r => r!["installations"]!.AsArray()).Single(i => i!["installation-id"]!.GetValue<string>() == second.InstallationId)!;
        // Simulate a completely absent revision, keeping immutable inventory evidence.
        var missingDirectory = a.PathFor(missing["location"]!.GetValue<string>());
        var removed = missingDirectory + ".fixture-retained"; Directory.Move(missingDirectory, removed);
        var recovered = await manager.PrepareAsync(own.ServiceId, new("b2", "cpu"), a.Base, hardware, fixture);
        Check(recovered.Build == "b2" && recovered.InstallationId != second.InstallationId, "missing installation restores exact pin");
        var store = new ScopeStore(a); var snapshot = store.Read();
        var docs = snapshot.Documents.ToDictionary(p => p.Key, p => (JsonObject)p.Value.DeepClone());
        foreach (var doc in docs.Values) doc["revision"] = doc["revision"]!.GetValue<long>() + 1;
        docs["local/inventory.json"]["data"]!["resources"]!.AsArray().SelectMany(r => r!["installations"]!.AsArray())
            .Single(i => i!["installation-id"]!.GetValue<string>() == recovered.InstallationId)!["integrity-status"] = "invalid";
        store.Commit(snapshot.Revision, docs); calls = fixture.Calls;
        await Expect("backend-integrity-invalid", () => manager.PrepareAsync(own.ServiceId, new("b2", "cpu"), a.Base, hardware, fixture, true));
        Check(calls == fixture.Calls, "force never repairs known invalid integrity");

        var externalPath = Path.Combine(a.Base, "external.exe"); File.WriteAllText(externalPath, "fork without official version syntax");
        var external = await manager.PrepareAsync(own.ServiceId, new(Executable: "./external.exe"), a.Base, hardware, fixture);
        Check(external.Ownership == "external" && external.Executable == externalPath && external.Build is null, "external is used in situ without official probe");
        var b = Project(Path.Combine(root, "independent")); var other = Service(b);
        var isolated = await new ScopedBackends(b, fixture.Resolve, Probe).PrepareAsync(other.ServiceId, new("b1", "cpu"), b.Base, hardware, fixture);
        Check(isolated.InstallationId != first.InstallationId && isolated.Executable.StartsWith(b.Root, StringComparison.OrdinalIgnoreCase), "independent project ownership");
        var otherRegistry = new RecipeRegistry(new ScopeStore(b));
        otherRegistry.RegisterInstance(other.ServiceId, Guid.NewGuid().ToString("D"), new JsonObject { ["pid"] = 1, ["started-at"] = "fixture", ["executable"] = isolated.Executable });
        var instanceBefore = new ScopeStore(b).Read().Documents["local/recipes.json"]["data"]!["recipes"]![0]!["entities"]![0]!["instance"]!.ToJsonString();
        var global = ScopeContext.Global(new Coquena.Storage.Home(Path.Combine(root, "home"))); var globalService = Service(global, "imported");
        var imported = await new ScopedBackends(global, fixture.Resolve, Probe).PrepareAsync(globalService.ServiceId,
            new(Executable: "./external.exe"), a.Base, hardware, fixture);
        Check(imported.Executable == externalPath && imported.Ownership == "external", "global import resolves against original base");
        var managedGlobal = await new ScopedBackends(global, fixture.Resolve, Probe).PrepareAsync(globalService.ServiceId, new("b1", "cpu"), a.Base, hardware, fixture);
        Check(managedGlobal.Executable.StartsWith(global.Root, StringComparison.OrdinalIgnoreCase), "global managed installation belongs to global scope");
        File.Delete(externalPath);
        await Expect("backend-absent", () => new ScopedBackends(global).PrepareAsync(globalService.ServiceId,
            new(Executable: "./external.exe"), a.Base, hardware, fixture));
        File.WriteAllText(externalPath, "fork without official version syntax");
        var globalRegistry = new RecipeRegistry(new ScopeStore(global)); globalRegistry.SetDefaultService(globalService.ServiceId);
        var aliasBefore = new ScopeStore(global).Read().State!["data"]!["aliases"]!.ToJsonString();
        await new ScopedBackends(global).PrepareAsync(globalService.ServiceId, new(Executable: "./external.exe"), a.Base, hardware, fixture);
        Check(new ScopeStore(global).Read().State!["data"]!["aliases"]!.ToJsonString() == aliasBefore, "backend selection never changes default alias");

        File.WriteAllText(Path.Combine(a.Base, "coquena.yaml"), "name: fixture\nservice:\n  backend:\n    version: b1\n    variant: cpu\n");
        File.WriteAllText(Path.Combine(a.Base, "override.yaml"), "service:\n  backend:\n    executable: ./external.exe\n");
        var composed = RecipeReader.Read(new RecipeSelection(a.Base, ["coquena.yaml", "override.yaml"]));
        Check(BackendIntent.FromRecipe(composed) == new BackendIntent(Executable: "./external.exe"), "override replaces entire backend");
        using var output = new StringWriter(); using var error = new StringWriter();
        Check(ScopedBackendCli.Run(["download", "--version", "b1", "--variant", "cuda@13", "--base", b.Base], output, error,
            () => hardware, fixture.Resolve, fixture, Probe) == 5, "CLI returns hardware code");
        Check(ScopedBackendCli.Run(["download", "--version", "b1", "--variant", "cuda@13", "--base", b.Base, "--force"], output, error,
            () => hardware, fixture.Resolve, fixture, Probe) == 0, "CLI force is explicit");
        Check(new ScopeStore(b).Read().Documents["local/recipes.json"]["data"]!["recipes"]![0]!["entities"]![0]!["instance"]!.ToJsonString() == instanceBefore,
            "candidate preparation preserves registered active process snapshot");
        var resumeTransfers = new ScopeTransfers(b);
        var resumeCandidate = (await fixture.Resolve(hardware, "cuda@13", "b1", default)).Selected!;
        var resumeId = Guid.NewGuid();
        var resumeFiles = resumeCandidate.Artifacts.Select(f => new RemoteFile(f.Name, f.Size)).ToArray();
        var stopped = resumeTransfers.Create(new(1, resumeId, "backend", "ggml/cuda@13:b1", null,
            new("ggml", "b1", "cuda", "cuda@13", "win-x64", resumeFiles), resumeFiles.Select((f, i) =>
                new TransferRecord(f.Path, $"downloads/{resumeId:D}/files/{i}.part", "backends/unused", 0, f.Size, null, null, null, "pending")).ToArray(),
            "pending", "transfer", DateTimeOffset.UtcNow, null), requiredFiles: ["llama-server.exe"]);
        Check(ScopeDownloadCli.Run(["resume", resumeId.ToString("D"), "--base", b.Base], output, error, () => hardware) == 5 &&
            resumeTransfers.Downloads.Status(stopped.Id).State == "pending", "resume cannot bypass hardware gate or alter pending bytes");
        var bad = Project(Path.Combine(root, "bad-version")); var badFixture = new Fixture();
        badFixture.Add("b1", "cpu"); var invalidVersion = new ScopedBackends(bad, badFixture.Resolve, (_, _) => Task.FromResult("not an official version"));
        await Expect("invalid-server-version", () => invalidVersion.DownloadAsync(new("b1", "cpu"), hardware, badFixture, force: true));
        Check(invalidVersion.List().Count == 0 && new ScopeTransfers(bad).Downloads.List().Single().State == "failed", "invalid version never enters inventory even with force");
        foreach (var invalid in new string[][] { ["backend", "download"], ["runtime", "list", "--force"], ["backend", "list", "--global", "-f", "x"], ["backend", "use", "cpu:latest"] })
            Check(Cli.Run(invalid, output, error) == 2, "invalid syntax: " + string.Join(' ', invalid));
        output.GetStringBuilder().Clear();
        Check(Cli.Run(["runtime", "show", "--base", a.Base, "-f", "coquena.yaml", "-f", "override.yaml"], output, error) == 0 &&
            output.ToString().Contains("executable"), "alias show reads composed intent");
        Check(!File.Exists(a.PathFor("local/active.json")) && !Directory.Exists(a.PathFor("catalogs")), "no M2 migration or runtime promotion");

        // Native binary reads and mutates the same scoped state; no official network access.
        if (executable is not null)
        {
            Check(Invoke(executable, ["runtime", "show", "--base", a.Base]).Code == 0, "AOT reads YAML");
            var listed = Invoke(executable, ["backend", "list", "--base", a.Base, "--json"]);
            Check(listed.Code == 0 && JsonNode.Parse(listed.Output)!.AsArray().Count > 0, "AOT lists actual scoped inventory");
            Check(Invoke(executable, ["backend", "protect", first.InstallationId, "--base", a.Base]).Code == 0, "AOT protects physical revision");
            Check(Invoke(executable, ["runtime", "unprotect", first.InstallationId, "--base", a.Base]).Code == 0, "AOT unprotect alias");
            var native = Project(Path.Combine(root, "native-publication"));
            var nativeFixture = new Fixture();
            var folder = Path.GetDirectoryName(Environment.ProcessPath!)!;
            var fixtureFiles = Directory.GetFiles(folder).Where(p => Path.GetExtension(p) is ".dll" or ".json" or ".exe")
                .Select(p => (Path.GetFileName(p) == "Coquena.Tests.exe" ? "llama-server.exe" : Path.GetFileName(p), File.ReadAllBytes(p))).ToArray();
            nativeFixture.Add("b1", "cpu", Archive(fixtureFiles));
            var candidate = (await nativeFixture.Resolve(hardware, "cpu", "b1", default)).Selected!;
            var id = Guid.NewGuid(); var remote = candidate.Artifacts.Select(f => new RemoteFile(f.Name, f.Size)).ToArray();
            var transfers = new ScopeTransfers(native, stage => { if (stage == ScopeTransferStage.Prepared) throw new Injected(); });
            var pending = transfers.Create(new(1, id, "backend", "ggml/cpu:b1", null, new("ggml", "b1", "cpu", "cpu", "win-x64", remote),
                remote.Select((f, i) => new TransferRecord(f.Path, $"downloads/{id:D}/files/{i}.part", "backends/unused", 0, f.Size, null, null, null, "pending")).ToArray(),
                "pending", "transfer", DateTimeOffset.UtcNow, null), requiredFiles: ["llama-server.exe"]);
            try { await transfers.RunAsync(id, nativeFixture); } catch (Injected) { }
            var resumed = Invoke(executable, ["download", "resume", id.ToString("D"), "--base", native.Base, "--json"]);
            Check(resumed.Code == 0, "AOT validates fixture --version and publishes prepared backend: " + resumed.Error + resumed.Output);
            Check(new ScopedBackends(native).List().Count == 1, "AOT publishes inventory without network");
        }
        var movedBase = Path.Combine(root, "moved-project");
        Check(Path.GetFullPath(a.Base).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            Path.GetFullPath(movedBase).StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "fixture move remains within test root");
        Directory.Move(a.Base, movedBase);
        var moved = ScopeContext.Project(Path.Combine(movedBase, "coquena.yaml"));
        var movedExternal = await new ScopedBackends(moved).PrepareAsync(own.ServiceId, new(Executable: "./external.exe"), moved.Base, hardware, fixture);
        Check(movedExternal.Executable == Path.Combine(movedBase, "external.exe") && movedExternal.InstallationId == external.InstallationId,
            "project move keeps identity and resolves its relative external against new base");
        Console.WriteLine($"OK: {checks} comprobaciones de backends por ámbito, latest/pins, externos, incompatibilidad/force, integridad, aislamiento y CLI" + (executable is null ? "." : " Native AOT."));
    }
    private sealed class Injected : Exception;
    private static (int Code, string Output, string Error) Invoke(string executable, string[] args)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.Environment["COQUENA_FIXTURE_BUILD"] = "1";
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(30000)) { process.Kill(true); throw new Exception("AOT fixture timeout"); }
        return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }
}
