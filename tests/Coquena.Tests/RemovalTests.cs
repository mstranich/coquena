using Coquena.Backends;
using Coquena.Contracts;
using Coquena.Configuration;
using Coquena.Downloads;
using Coquena.Models;
using Coquena.Servers;
using Coquena.Storage;
using System.Diagnostics;

namespace Coquena.Tests;

internal static class RemovalTests
{
    public static void Run(string? executable = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-removal-" + Guid.NewGuid());
        var home = new Home(root); home.Ensure();
        var store = new LocalStore(home); var json = ContractJsonContext.Default;
        var removal = new ResourceRemoval(home); var checks = 0;
        void Check(bool condition, string reason) { checks++; if (!condition) throw new Exception("Removal: " + reason); }
        void Reject(Action action, string code)
        {
            try { action(); throw new Exception("Unexpected acceptance: " + code); }
            catch (StorageException e) { Check(e.Code == code, e.Message); }
        }
        void SaveModels(params ModelRecord[] models) => store.Update(DocumentKind.Models, json.ModelsDocument, ContractValidation.Models, _ => new(1, models));
        void SaveBackends(params BackendRecord[] backends) => store.Update(DocumentKind.Backends, json.BackendsDocument, ContractValidation.Backends, _ => new(1, backends));
        ModelRecord Managed(string name, string[] names, string? companion = null, string? location = null)
        {
            var paths = new ModelPaths(home);
            var files = names.Select((n, i) =>
            {
                var logical = "models/owner/repo/" + n; var target = paths.Resolve(location, logical);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!); File.WriteAllBytes(target, new byte[10 + i]);
                return new InstalledFile(n, location is null ? logical : logical[7..], 10 + i);
            }).ToArray();
            return new(Guid.NewGuid(), name, new("owner/repo", "main", new string('a', 40), files.Select(f => new RemoteFile(f.RemotePath, f.Size)).ToArray()), files, [], location, CompanionType: companion);
        }
        try
        {
            var primary = Managed("owner/repo:Q4", ["sub/one.gguf", "sub/two.gguf"]);
            var companion = Managed("owner/repo#mmproj:F16", ["sub/vision.gguf"], "mmproj");
            var paths = new ModelPaths(home);
            var neighbor = home.PathFor("models/owner/repo/sub/notes.txt"); File.WriteAllText(neighbor, "neighbor");
            SaveModels(primary, companion);
            var preset = new PresetRecord(Guid.NewGuid(), "p", primary.Id, [], new() { ["mmproj"] = companion.Id });
            var shared = preset with { Id = Guid.NewGuid(), Name = "shared" };
            store.Update(DocumentKind.Presets, json.PresetsDocument, ContractValidation.Presets, _ => new(1, [preset, shared]));
            Reject(() => removal.Model(companion.Name), "resource-not-found");
            using (var locked = new FileStream(paths.File(primary, primary.Files[1]), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var partial = removal.Model(primary.Name);
                Check(!partial.Complete && partial.FreedBytes == 10 && partial.Deleted.Length == 1 && partial.Pending.Length == 1, "partial bytes and diagnosis");
                Check(store.Read(DocumentKind.Models, json.ModelsDocument, ContractValidation.Models)!.Models.Single(m => m.Id == primary.Id).RemovalPending, "durable partial ownership");
                try { new LocalModelMaintenance(home).Refresh(primary.Name); throw new Exception("Unexpected refresh"); }
                catch (ConfigurationException) { Check(true, "partial mutation blocked"); }
                var repeated = removal.Model(primary.Name);
                Check(!repeated.Complete && repeated.FreedBytes == 0, "missing shards never counted twice");
            }
            var finished = new ResourceRemoval(home).Model(primary.Name);
            Check(finished.Complete && finished.FreedBytes == 11, "retry after reopening store");
            Check(File.Exists(neighbor) && File.Exists(paths.File(companion, companion.Files[0])), "neighbors and companion survive");
            Check(store.Read(DocumentKind.Presets, json.PresetsDocument, ContractValidation.Presets)!.Presets.All(p => p.ModelId == primary.Id), "presets unchanged");
            var reused = Managed(primary.Name, ["sub/new.gguf"]); SaveModels(reused, companion);
            Check(new ConfigurationManager(home).View("p").Diagnostics.Any(d => d.Contains("referencia rota")), "reused name does not reconnect ID");
            var retiredCompanion = removal.Model(companion.Name, true);
            Check(retiredCompanion.Complete && retiredCompanion.FreedBytes == 10 && File.Exists(neighbor), "shared companion removed independently");
            Check(new ConfigurationManager(home).View("shared").Diagnostics.Any(d => d.Contains("referencia rota")), "shared binding remains broken");

            var externalPath = Path.Combine(root, "external.gguf"); File.WriteAllBytes(externalPath, new byte[12]);
            var external = new ModelRecord(Guid.NewGuid(), "owner/external:Q4", null, [new("", externalPath, 12)], [], Ownership: "external");
            SaveModels(reused, external);
            var externalResult = removal.Model(external.Name);
            Check(externalResult.External && externalResult.FreedBytes == 0 && File.Exists(externalPath), "external files preserved");
            var alternate = Managed("owner/repo:Q8", ["alt/unit.gguf"], location: Path.Combine(root, "other-models")); SaveModels(reused, alternate);
            Check(removal.Model(alternate.Name).FreedBytes == 10 && !File.Exists(paths.File(alternate, alternate.Files[0])), "configured model location");

            var id = Guid.NewGuid(); var slot = $"backends/{id:D}/{Guid.NewGuid():D}";
            Directory.CreateDirectory(home.PathFor(slot));
            File.WriteAllBytes(home.PathFor(slot + "/llama-server.exe"), new byte[13]);
            File.WriteAllBytes(home.PathFor(slot + "/library.dll"), new byte[14]);
            File.WriteAllText(home.PathFor(slot + "/user.txt"), "keep");
            var backend = new BackendRecord(id, "b11482", "v", "ggml", "ggml-org/llama.cpp", "cpu", "cpu", "win-x64", slot + "/llama-server.exe", true, true, OwnedFiles: ["llama-server.exe", "library.dll"]);
            SaveBackends(backend);
            var config = new ConfigDocument(1, null, new("fixed", id), new(5));
            store.Update(DocumentKind.Config, json.ConfigDocument, ContractValidation.Config, _ => config);
            var server = new ServerRecord(Guid.NewGuid(), "s", new("fixed", id), [preset.Id], new() { ["port"] = System.Text.Json.JsonSerializer.SerializeToElement(9931, json.Options.GetTypeInfo(typeof(int))) });
            store.Update(DocumentKind.Servers, json.ServersDocument, ContractValidation.Servers, _ => new(1, [server]));
            // Suspended real child is still a live instance; no network/inference needed.
            var instanceId = Guid.NewGuid();
            var activeCompanion = Managed("owner/repo#mtp:Q4", ["active-companion.gguf"], "mtp");
            SaveModels(reused, activeCompanion);
            using (var child = WindowsServerProcess.Create(Path.Combine(AppContext.BaseDirectory, "Coquena.Tests.exe"), ["--runtime-child"], home.PathFor("logs/child.log"), instanceId))
            {
                var instance = new InstanceDocument(1, instanceId, server.Id, id, backend.Build, child.Pid, "detached", "127.0.0.1", 9931, false,
                    [preset.Id], [reused.Id, activeCompanion.Id], "run/s/test.ini", "logs/child.log", child.Created, child.Executable);
                store.Update(DocumentKind.Instance, json.InstanceDocument, ContractValidation.Instance, _ => instance, server: "s");
                Reject(() => removal.Backend(id), "resource-in-use");
                Reject(() => removal.Model(reused.Name), "resource-in-use");
                Reject(() => removal.Model(activeCompanion.Name, true), "resource-in-use");
                store.Update(DocumentKind.Instance, json.InstanceDocument, ContractValidation.Instance, _ => instance with { ExecutablePath = externalPath }, server: "s");
                Reject(() => removal.Backend(id), "resource-in-use");
                Check(File.Exists(home.PathFor(backend.ServerPath)), "uncertain identity preserves files");
            }
            using (var locked = new FileStream(home.PathFor(slot + "/library.dll"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var partial = removal.Backend(id);
                Check(!partial.Complete && partial.FreedBytes == 13, "partial backend inventory");
                Check(store.Read(DocumentKind.Backends, json.BackendsDocument, ContractValidation.Backends)!.Backends.Single().RemovalPending, "backend pending persisted");
            }
            Check(removal.Backend(id).FreedBytes == 14 && File.Exists(home.PathFor(slot + "/user.txt")), "backend neighbor preserved on retry");
            Check(store.Read(DocumentKind.Config, json.ConfigDocument, ContractValidation.Config) == config, "default selection unchanged");
            Check(store.Read(DocumentKind.Servers, json.ServersDocument, ContractValidation.Servers)!.Servers.Single().Backend!.BackendId == id, "server selection broken without reassignment");
            Check(new ServerManager(home).View("s").Diagnostics.Any(d => d.Contains("ausente")), "backend missing diagnosed");
            var externalBackend = backend with { Id = Guid.NewGuid(), ServerPath = externalPath, Ownership = "external", Name = "external", TracksLatest = false, Pinned = false, OwnedFiles = null };
            SaveBackends(externalBackend);
            Check(removal.Backend(externalBackend.Id).External && File.Exists(externalPath), "external backend declared official preserved");
            SaveModels(reused);
            var aliases = reused with { Id = Guid.NewGuid(), Name = "owner/repo:alias" }; SaveModels(reused, aliases);
            Reject(() => removal.Model(reused.Name), "ownership-conflict"); SaveModels(reused);
            var selected = new SelectedModel("owner/repo:Q6", null, new("owner/repo", "main", new string('a', 40), [new("pending.gguf", 1)]));
            var downloads = new ModelInstaller(home); var operation = downloads.Create(selected);
            // A prepared operation reserves its target ID against removal.
            var reserved = Managed(selected.Name, ["pending.gguf"]) with { Id = operation.ModelInstall!.ModelId };
            SaveModels(reused, reserved); Reject(() => removal.Model(reserved.Name), "operation-pending");
            Check(File.Exists(paths.File(reserved, reserved.Files[0])), "operation rejection precedes delete");

            var interrupted = Managed("owner/repo:interrupt", ["interrupt.gguf"]); SaveModels(reused, interrupted);
            var writes = 0;
            var faultyStore = new LocalStore(home, stage => { if (stage == WriteStage.BeforeReplace && ++writes == 2) throw new IOException("injected final catalog failure"); });
            var interruptedResult = new ResourceRemoval(home, faultyStore).Model(interrupted.Name);
            Check(!interruptedResult.Complete && interruptedResult.FreedBytes == 10 && interruptedResult.Pending.Single().Contains("registro"), "failure after delete reported with bytes");
            Check(!File.Exists(paths.File(interrupted, interrupted.Files[0])) && store.Read(DocumentKind.Models, json.ModelsDocument, ContractValidation.Models)!.Models.Single(m => m.Id == interrupted.Id).RemovalPending, "ownership survives final publication failure");
            Check(new ResourceRemoval(home).Model(interrupted.Name).FreedBytes == 0, "interrupted delete not recounted");

            var empty = Managed("owner/repo:empty", ["empty/unit.gguf"]); SaveModels(reused, empty);
            Check(removal.Model(empty.Name).Complete && !Directory.Exists(home.PathFor("models/owner/repo/empty")), "only empty folders removed");

            var before = Environment.GetEnvironmentVariable("COQUENA_HOME");
            try
            {
                Environment.SetEnvironmentVariable("COQUENA_HOME", home.Root);
                var output = new StringWriter(); var error = new StringWriter();
                Check(Coquena.Cli.Run(["backend", "uninstall", "bad-id"], output, error) == 2, "strict backend CLI");
                Check(Coquena.Cli.Run(["model", "remove"], output, error) == 2, "strict model CLI");
                Check(Coquena.ModelCli.Run(["companion", "remove", reused.Name], output, error) == 1, "M2 API scopes companions");
                SaveModels(reused);
                Check(Coquena.ModelCli.Run(["remove", reused.Name], output, error) == 0 && output.ToString().Contains("Espacio efectivamente"), "M2 API bytes output");
                if (executable is not null)
                {
                    SaveModels(external);
                    var info = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    var environment = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (System.Collections.DictionaryEntry pair in Environment.GetEnvironmentVariables()) environment[(string)pair.Key] = (string)pair.Value!;
                    info.Environment.Clear(); foreach (var pair in environment) info.Environment[pair.Key] = pair.Value;
                    foreach (var arg in new[] { "model", "remove", external.Name }) info.ArgumentList.Add(arg);
                    using var process = ModelEngineProcess.Start(info)!;
                    var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(10000)) { process.Kill(true); throw new Exception("Published removal timed out"); }
                    Check(process.ExitCode == 0 && stdout.Result.Contains("archivos conservados") && stderr.Result.Length == 0 && File.Exists(externalPath), "published CLI external removal");
                }
            }
            finally { Environment.SetEnvironmentVariable("COQUENA_HOME", before); }
            Check(backend == ContractJson.Read(ContractJson.Write(backend, json.BackendRecord), json.BackendRecord, ContractValidation.Backend), "inventory value equality after serialization");
            try { ContractValidation.Backend(backend with { OwnedFiles = ["../outside"] }); throw new Exception("Unsafe inventory accepted"); }
            catch (ContractException) { Check(true, "inventory path validation"); }
        }
        finally { Directory.Delete(root, true); }
        Console.WriteLine($"OK: {checks} comprobaciones de retirada.");
    }
}
