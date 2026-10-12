using System.Diagnostics;
using System.Text.Json;
using Coquena.Configuration;
using Coquena.Contracts;
using Coquena.Servers;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class ParameterTests
{
    public static void Run(string? executable = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-parameters-" + Guid.NewGuid());
        var home = new Home(root); var store = new LocalStore(home);
        var manager = new ParameterManager(home); var config = new ConfigurationManager(home); var servers = new ServerManager(home);
        var json = ContractJsonContext.Default; var checks = 0;
        using var report = new StringWriter();
        try
        {
            Check(manager.List().Length == 22 && !Directory.Exists(root), "factory read does not initialize home");
            Check(!manager.ChangeDefinition("ctx-size", d => d with { Description = "consulta" }, "descripción", true, report, () => throw new Exception("dry-run prompted")) && !Directory.Exists(root), "dry-run without home performs no writes");
            Check(!manager.Reset(true, report, () => throw new Exception("reset dry-run prompted")) && !Directory.Exists(root), "reset dry-run without home performs no writes");
            var model = new ModelRecord(Guid.NewGuid(), "fixture/model:Q4", new("fixture/model", "main", new string('a', 40), [new("fixture.gguf", null)]),
                [new("fixture.gguf", "models/fixture/model/fixture.gguf", 0)], []);
            var path = home.PathFor(model.Files[0].LocalPath); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllBytes(path, ConfigurationTests.Fixture(null));
            store.Update(DocumentKind.Models, json.ModelsDocument, ContractValidation.Models, _ => new(1, [model]));
            var backend = new BackendRecord(Guid.NewGuid(), "b11482", "fixture", "ggml", "ggml-org/llama.cpp", "cuda@13", "cuda", "win-x64", "backends/fixture/server.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(home.PathFor(backend.ServerPath))!); File.WriteAllText(home.PathFor(backend.ServerPath), "fixture");
            var other = backend with { Id = Guid.NewGuid(), Build = "b11483" };
            store.Update(DocumentKind.Backends, json.BackendsDocument, ContractValidation.Backends, _ => new(1, [backend, other]));
            store.Update(DocumentKind.Config, json.ConfigDocument, ContractValidation.Config, _ => new(1, null, new("fixed", backend.Id), new(5)));
            var one = config.Create(model.Name, "one"); var two = config.Create(model.Name, "two");
            servers.Create("first"); servers.Create("second"); servers.Presets("first", ["one", "two"], true); servers.Presets("second", ["one"], true);
            Cli(0, "define", "custom", "--type", "decimal", "--scope", "model", "--target", "ini", "--default", "0.7", "--description", "personalizado", "--alias=-x");
            Check(!config.View("one").Own.ContainsKey("custom"), "definition/default do not assign values");
            config.Set("one", "preexisting-unknown", "fraction");
            var state = State();
            Cli(2, "define", "preexisting-unknown", "--type", "integer", "--scope", "model", "--target", "ini");
            Check(State() == state && config.View("one").Own["preexisting-unknown"] == "fraction", "new definition rejects incompatible preexisting unknown assignment without deletion");
            config.Set("one", "preexisting-unknown", null);
            Check(Cli(0, "show", "-x").Contains("default: 0.7"), "persisted metadata and dash alias");
            state = State();
            Cli(2, "define", "custom", "--type", "string", "--scope", "model", "--target", "ini");
            Cli(2, "edit", "missing", "--description", "x");
            Check(State() == state, "duplicate define and missing edit preserve state");
            config.Set("one", "-x", "0.2"); config.Set("two", "custom", "0.8");
            state = State(); Cli(2, "remove", "custom");
            Check(State() == state, "missing interactive input preserves definition and assignments");
            Cli(0, "edit", "custom", "--description", "nueva");
            Check(manager.Find("custom") is { Default: "0.7", Type: "decimal" } d && d.Aliases.SequenceEqual(["-x"]), "edit preserves omitted fields");
            Cli(0, "edit", "custom", "--alias=-y", "--alias=--custom-alias");
            Check(manager.Find("custom")!.Aliases.SequenceEqual(["-y", "--custom-alias"]), "repeated aliases replace list");
            Cli(0, "edit", "custom", "--default", "0.9");
            Check(config.View("one").Own["custom"] == "0.2", "changing default does not alter assignments");
            config.Set("one", "custom", null); Check(!config.View("one").Own.ContainsKey("custom"), "unset does not restore default");
            config.Set("one", "custom", "0.2");
            Cli(0, "edit", "custom", "--clear-default", "--clear-description", "--clear-aliases");
            Check(manager.Find("custom") is { Default: null, Description: "" } clear && clear.Aliases.Length == 0, "explicit metadata clearing");
            state = State();
            foreach (var args in new string[][]
            {
                ["edit", "custom", "--clear-default", "--default", "1"], ["edit", "custom", "--clear-description", "--description", "x"],
                ["edit", "custom", "--clear-aliases", "--alias=x"], ["edit", "custom", "--alias", "-x"],
                ["edit", "custom", "--type", "invalid"], ["edit", "custom", "--default", "NaN"],
                ["define", "bad", "--type", "enum", "--scope", "model", "--target", "ini"],
                ["edit", "custom", "--values", "one"], ["edit", "custom", "--type", "enum", "--values", "a", "--default", "b"],
                ["support", "set", "custom", "lab/vulkan"], ["support", "set", "custom", "ggml/rocm"],
                ["support", "set", "custom", "--from", "b1200", "--until", "b1000"], ["support", "set", "custom", "--from", "1.0.0"],
                ["support", "set", "custom", "--backend", Guid.NewGuid().ToString()], ["support", "remove", "custom", "--from", "b1000"],
                ["list", "--distribution", "lab/repo", "--version", "b11482"], ["list", "--distribution", "ggml"],
                ["list", "--backend", backend.Id.ToString(), "--version", "b11482"]
            }) Cli(2, args);
            Check(State() == state, "invalid metadata/options/selectors leave state unchanged");
            foreach (var protectedName in ParameterManager.Protected)
            { Cli(2, "edit", protectedName, "--description", "x"); Cli(2, "remove", protectedName); Cli(2, "support", "set", protectedName); }
            Check(State() == state, "protected definitions preserved");
            Cli(0, "define", "choice", "--type", "enum", "--scope", "model", "--target", "ini", "--values", "a", "--values", "b", "--default", "a");
            config.Set("one", "choice", "a"); config.Set("two", "choice", "b");
            state = State(); var impact = Cli(0, "edit", "choice", "--values", "a", "--dry-run");
            Check(impact.Contains("two") && impact.Contains(two.Id.ToString()) && impact.Contains("choice = b") && !impact.Contains("Eliminar | preset one"), "dry-run identifies only incompatible assignment");
            Check(State() == state, "dry-run leaves documents unchanged");
            var redirected = home.PathFor("impact.txt"); File.WriteAllText(redirected, impact);
            Check(!File.ReadAllText(redirected).Contains('\u001b') && File.ReadAllText(redirected).Contains("Después:"), "impact can be saved as plain text with requested definition");
            Check(!manager.ChangeDefinition("choice", d => d with { Choices = ["a"] }, "enumeración", false, report, () => false) && State() == state, "cancel preserves definition and assignments");
            var calls = 0;
            Check(manager.ChangeDefinition("choice", d => d with { Choices = ["a"] }, "enumeración", false, report, () => { calls++; return true; }) && calls == 1,
                "confirmed enum change applies");
            Check(config.View("one").Own["choice"] == "a" && !config.View("two").Own.ContainsKey("choice"), "compatible preserved incompatible cascaded");
            config.Set("two", "choice", "a");
            using var preview = new StringWriter(); using var actual = new StringWriter();
            manager.ChangeDefinition("choice", _ => null, "misma eliminación", true, preview);
            manager.ChangeDefinition("choice", _ => null, "misma eliminación", false, actual, () => false);
            Check(actual.ToString().StartsWith(preview.ToString(), StringComparison.Ordinal), "dry-run and actual operation share impact analysis");
            var confirmations = 0;
            manager.ChangeDefinition("choice", _ => null, "eliminar", false, report, () =>
            { if (++confirmations == 1) config.Set("one", "choice", null); return true; });
            Check(confirmations == 2 && !config.View("two").Own.ContainsKey("choice"), "concurrent assignment change recalculates and reconfirms impact");
            Cli(0, "define", "reconfirm", "--type", "enum", "--scope", "model", "--target", "ini", "--values", "a", "--values", "b");
            config.Set("one", "reconfirm", "b");
            confirmations = 0;
            manager.ChangeDefinition("reconfirm", d => d with { Choices = ["a"] }, "enumeración", false, report, () =>
            {
                if (++confirmations == 1) manager.ChangeDefinition("reconfirm", d => d with { Description = "concurrente" }, "metadatos", false, report);
                return confirmations == 1;
            });
            Check(confirmations == 2 && manager.Find("reconfirm")!.Choices!.SequenceEqual(["a", "b"]) && config.View("one").Own["reconfirm"] == "b", "catalog change invalidates previous confirmation and cancellation preserves latest state");
            config.Set("one", "custom", "1");
            Cli(0, "edit", "custom", "--type", "integer", "--dry-run");
            manager.ChangeDefinition("custom", d => d with { Type = "integer" }, "tipo", false, report, () => true);
            Check(config.View("one").Own["custom"] == "1" && !config.View("two").Own.ContainsKey("custom"), "type conversion removes fractional values only");
            Reject(() => config.Set("one", "custom", "1.2"), "integer refuses fraction");
            config.Set("one", "custom", "-3"); Check(config.View("one").Own["custom"] == "-3", "integer allows negative values");
            Cli(0, "support", "set", "custom", "--from", "b9", "--until", "b10000");
            Check(Availability("custom", "b9") == "admitido" && Availability("custom", "b10000") == "admitido" && Availability("custom", "b8") == "no admitido" && Availability("custom", "b10001") == "no admitido", "numeric builds inclusive range");
            Cli(0, "support", "set", "custom", "oficial/cuda@13", "--from", "b11482", "--until", "b11482");
            Check(Availability("custom", "b11482") == "admitido" && Availability("custom", "b11483") == "no admitido", "variant-specific exact version");
            Cli(0, "support", "set", "custom", "ggml/vulkan", "--from", "b1000");
            Check(Availability("custom", "b999", "vulkan") == "no admitido" && Availability("custom", "b1000", "vulkan") == "admitido" && Availability("custom", "b999999", "vulkan") == "admitido", "open upper limit");
            Cli(0, "support", "set", "custom", "ggml/cpu", "--until", "b1000");
            Check(Availability("custom", "b1", "cpu") == "admitido" && Availability("custom", "b1001", "cpu") == "no admitido", "open lower limit");
            Cli(0, "support", "set", "custom", "--backend", backend.Id.ToString(), "--from", "b12000");
            Check(ParameterManager.Availability(manager.Find("custom"), backend) == "no admitido" && ParameterManager.Availability(manager.Find("custom"), other) == "no admitido", "local precedence and different builds same family");
            Check(!config.Compose(backend, [one.Id]).Ini.Contains("custom =") && config.View("one").Own["custom"] == "-3", "inapplicable omitted while persisted assignment remains");
            Cli(0, "support", "remove", "custom", "--backend", backend.Id.ToString());
            Check(config.Compose(backend, [one.Id]).Ini.Contains("custom = -3"), "local removal preserves other associations");
            Cli(0, "support", "set", "custom");
            Check(Availability("custom", "b11483") == "no admitido", "specific range does not fall back to permissive general");
            var supportImpact = Cli(0, "support", "set", "custom", "ggml/cuda@13", "--from", "b12000", "--dry-run");
            Check(supportImpact.Contains("Conservar/omitir con warning") && supportImpact.Contains("first") && supportImpact.Contains("second"), "support impact reports executions and preserved assignments");
            var supportCount = manager.Find("custom")!.Support!.Length;
            Cli(0, "support", "remove", "custom");
            Check(manager.Find("custom")!.Support!.Length == supportCount - 1 && config.View("one").Own.ContainsKey("custom"), "remove general preserves specifics and values");
            Cli(0, "support", "set", "custom", "--backend", backend.Id.ToString());
            store.Update(DocumentKind.Backends, json.BackendsDocument, ContractValidation.Backends, _ => new(1, [other]));
            Check(Cli(0, "show", "custom").Contains("referencia local ausente"), "dangling ID diagnosed");
            Cli(0, "edit", "custom", "--description", "con referencia ausente");
            Cli(2, "support", "set", "custom", "--backend", backend.Id.ToString());
            Cli(2, "support", "set", "custom", "--backend", backend.Id.ToString(), "--until", "b1000");
            Cli(0, "support", "remove", "custom", "--backend", backend.Id.ToString());
            store.Update(DocumentKind.Backends, json.BackendsDocument, ContractValidation.Backends, _ => new(1, [backend, other]));
            var nonofficial = backend with { Id = Guid.NewGuid(), Origin = "lab", Repository = "lab/repo" };
            store.Update(DocumentKind.Backends, json.BackendsDocument, ContractValidation.Backends, old => old! with { Backends = [.. old.Backends, nonofficial] });
            Cli(2, "show", "custom", "--backend", nonofficial.Id.ToString());
            Cli(2, "support", "set", "custom", "--backend", nonofficial.Id.ToString());
            Check(ParameterManager.Availability(manager.Find("ctx-size"), other) == "desconocido", "factory missing evidence is not incompatibility");
            Cli(0, "edit", "models-autoload", "--type", "enum", "--values", "true", "--values", "false");
            servers.Set("first", "models-autoload", "false");
            Check(ServerRuntime.Arguments(servers.Compose("first"), "fixture.ini").Contains("false"), "edited factory type governs CLI emission");
            Cli(0, "edit", "models-autoload", "--type", "boolean");
            Check(ServerRuntime.Arguments(servers.Compose("first"), "fixture.ini").Contains("--no-models-autoload"), "compatible retained string assignment normalizes boolean at composition");
            config.Set("one", "c", "4096");
            Cli(0, "edit", "ctx-size", "--description", "editado de fábrica");
            Check(config.View("one").Own["ctx-size"] == "4096" && manager.Find("ctx-size")!.Description == "editado de fábrica", "initial definition editable with assignments intact");
            Cli(0, "define", "alias-state", "--type", "integer", "--scope", "model", "--target", "ini", "--alias=--old-alias");
            store.Update(DocumentKind.Presets, json.PresetsDocument, ContractValidation.Presets, old => old! with { Presets = old.Presets.Select(p => p.Id == one.Id ?
                p with { Parameters = p.Parameters.Append(new("old-alias", "2")).ToDictionary() } : p).ToArray() });
            Cli(0, "edit", "alias-state", "--clear-aliases");
            Check(config.View("one").Own["alias-state"] == "2" && !config.View("one").Own.ContainsKey("old-alias"), "stored legacy alias is canonicalized before alias removal");
            Cli(0, "define", "process-option", "--type", "decimal", "--scope", "server", "--target", "cli");
            servers.Set("first", "process-option", "-1.5"); servers.Set("second", "process-option", "-2.5");
            Reject(() => config.Set("one", "process-option", "1"), "process parameter refused in preset");
            Reject(() => servers.Set("first", "custom", "1"), "model parameter refused in server");
            Check(ServerRuntime.Arguments(servers.Compose("first"), "fixture.ini").Contains("-1.5"), "decimal server emission");
            Cli(0, "define", "process-ini", "--type", "string", "--scope", "server", "--target", "ini");
            servers.Set("first", "process-ini", "local");
            var composed = servers.Compose("first");
            Check(composed.Presets.Ini.Contains("[*]\nprocess-ini = local") && !ServerRuntime.Arguments(composed, "fixture.ini").Contains("--process-ini"), "server INI destination is not duplicated in CLI");
            Cli(0, "define", "toggle", "--type", "boolean", "--scope", "model", "--target", "ini");
            config.Set("one", "toggle", "0"); config.Set("two", "toggle", "true"); Reject(() => config.Set("one", "toggle", "2"), "boolean refuses arbitrary integer");
            Cli(0, "define", "no-value", "--type", "flag", "--scope", "server", "--target", "cli");
            servers.Set("first", "no-value", "true");
            var arguments = ServerRuntime.Arguments(servers.Compose("first"), "fixture.ini");
            Check(arguments.Contains("--no-value") && !arguments.Contains("True"), "flag emits no argument value");
            Cli(0, "define", "model-cli", "--type", "integer", "--scope", "model", "--target", "cli");
            config.Set("one", "model-cli", "2");
            Check(config.Compose(backend, [one.Id]).CliArguments!.SequenceEqual(["--model-cli", "2"]), "model CLI target emitted outside INI");
            Reject(() => config.Compose(backend, [one.Id, two.Id]), "unrepresentable distinct model CLI values rejected");
            config.Set("two", "model-cli", "2"); Check(config.Compose(backend, [one.Id, two.Id]).CliArguments!.Length == 2, "identical model CLI values emitted once");
            config.Set("one", "model-cli", null); config.Set("two", "model-cli", null);
            var instances = home.PathFor("run/first/preserved.ini"); Directory.CreateDirectory(Path.GetDirectoryName(instances)!); File.WriteAllText(instances, "active fixture");
            var modelBytes = File.ReadAllBytes(path);
            Cli(0, "remove", "process-option", "--dry-run");
            manager.ChangeDefinition("process-option", _ => null, "eliminar", false, report, () => true);
            Check(!servers.View("first").Server.Parameters.ContainsKey("process-option") && !servers.View("second").Server.Parameters.ContainsKey("process-option"), "cascade multiple servers");
            Check(servers.List().Length == 2 && config.List().Length == 2 && File.ReadAllText(instances) == "active fixture", "entities and active execution file preserved");
            Cli(0, "define", "scope-change", "--type", "integer", "--scope", "model", "--target", "ini");
            config.Set("one", "scope-change", "10");
            state = State();
            Check(!manager.ChangeDefinition("scope-change", d => d with { Application = "server", Destination = "CLI" }, "ámbito", false, report, () => false) && State() == state, "scope change cancellation preserves model assignments");
            manager.ChangeDefinition("scope-change", d => d with { Application = "server", Destination = "CLI" }, "ámbito", false, report, () => true);
            Check(!config.View("one").Own.ContainsKey("scope-change"), "scope change removes assignments from wrong scope after confirmation");
            servers.Set("first", "scope-change", "-10"); Check(servers.View("first").Server.Parameters["scope-change"].GetInt64() == -10, "integer server accepts negatives outside special constraints");
            // Exercise publication before checkpoint and interruption between all three
            // catalogs, including stale historical assignments in the wrong scope.
            foreach (var stage in new[] { WriteStage.AfterReplace, WriteStage.AfterParameterCheckpoint })
            {
                var name = "transaction-" + stage.ToString().ToLowerInvariant();
                Cli(0, "define", name, "--type", "integer", "--scope", "model", "--target", "ini");
                config.Set("one", name, "1"); config.Set("two", name, "2");
                store.Update(DocumentKind.Servers, json.ServersDocument, ContractValidation.Servers, old => old! with
                { Servers = old.Servers.Select(s => s with { Parameters = s.Parameters.Append(new(name, JsonSerializer.SerializeToElement(3, json.Int32))).ToDictionary() }).ToArray() });
                var faulting = new ParameterManager(home, new LocalStore(home, observed => { if (observed == stage) throw new IOException("interrupted publication"); }));
                try { faulting.ChangeDefinition(name, _ => null, "eliminación triple", false, report, () => true); throw new Exception("expected published failure"); }
                catch (StorageException e) { Check(e.Published, "triple transaction reports committed failure " + stage); }
                var readStore = new LocalStore(home);
                Check(manager.Find(name) is null && readStore.Read(DocumentKind.Presets, json.PresetsDocument, ContractValidation.Presets)!.Presets.All(p => !p.Parameters.ContainsKey(name)) &&
                    readStore.Read(DocumentKind.Servers, json.ServersDocument, ContractValidation.Servers)!.Servers.All(s => !s.Parameters.ContainsKey(name)), "three committed catalogs consistent via fresh store " + stage);
                var beforeRead = File.ReadAllBytes(home.PathFor("catalogs/parameter-transaction.json"));
                Cli(0, "remove", "alias-state", "--dry-run");
                Check(File.ReadAllBytes(home.PathFor("catalogs/parameter-transaction.json")).SequenceEqual(beforeRead), "dry-run does not checkpoint committed transaction " + stage);
                config.Set("one", "toggle", "0");
                Check(!File.Exists(home.PathFor("catalogs/parameter-transaction.json")), "writer recovers triple transaction " + stage);
            }
            state = State();
            var failing = new ParameterManager(home, new LocalStore(home, s => { if (s == WriteStage.BeforeReplace) throw new IOException("fixture before commit"); }));
            Reject(() => failing.ChangeDefinition("custom", _ => null, "eliminar", false, report, () => true), "failure before commit");
            Check(State() == state && manager.Find("custom") is not null && config.View("one").Own.ContainsKey("custom"), "failed publication preserves definition and assignment");
            var checkpoint = new ParameterManager(home, new LocalStore(home, s => { if (s == WriteStage.AfterParameterCheckpoint) throw new IOException("fixture interrupted checkpoint"); }));
            try { checkpoint.ChangeDefinition("custom", _ => null, "eliminar", false, report, () => true); throw new Exception("expected committed failure"); }
            catch (StorageException e) { Check(e.Published, "failure after commit reports published state"); }
            Check(manager.Find("custom") is null && !store.Read(DocumentKind.Presets, json.PresetsDocument, ContractValidation.Presets)!.Presets.Single(p => p.Id == one.Id).Parameters.ContainsKey("custom"), "fresh readers observe complete committed snapshot after interrupted checkpoint");
            Check(File.Exists(home.PathFor("catalogs/parameter-transaction.json")), "recovery marker retained");
            config.Set("one", "toggle", "1");
            Check(!File.Exists(home.PathFor("catalogs/parameter-transaction.json")) && manager.Find("custom") is null, "next writer recovers checkpoint before mutation");
            var referenceImpact = Cli(0, "remove", "model", "--dry-run");
            Check(referenceImpact.Contains(model.Id.ToString()) && referenceImpact.Contains(one.Id.ToString()) && referenceImpact.Contains(two.Id.ToString()), "reference impact contains IDs for repair");
            manager.ChangeDefinition("model", _ => null, "eliminar referencia", false, report, () => true);
            Check(config.List().All(p => p.Preset.ModelId is null) && File.ReadAllBytes(path).SequenceEqual(modelBytes) &&
                store.Read(DocumentKind.Models, json.ModelsDocument, ContractValidation.Models)!.Models.Single().Id == model.Id, "remove model assignment preserves resource and files");
            Reject(() => config.Compose(backend, [one.Id]), "missing model assignment diagnosed at composition");
            var corrupt = File.ReadAllText(home.PathFor("catalogs/parameters.json"));
            File.WriteAllText(home.PathFor("catalogs/parameters.json"), corrupt.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99"));
            Reject(() => manager.List(), "corrupt catalog not replaced by factory");
            File.WriteAllText(home.PathFor("catalogs/parameters.json"), corrupt);
            // A single reset combines removals, restored definitions and incompatible
            // assignments across all entities, using the same publication boundary.
            manager.ChangeDefinition("ctx-size", d => d with { Type = "string", Default = "edited", Aliases = ["context-custom"] }, "tipo", false, report, () => true);
            config.Set("one", "ctx-size", "invalid-integer"); config.Set("two", "ctx-size", "2048");
            manager.ChangeDefinition("temp", _ => null, "retirar", false, report, () => true);
            config.Set("one", "temp", "0.4");
            config.Set("two", "temp", "invalid-decimal");
            config.Set("one", "toggle", "true"); servers.Set("first", "scope-change", "5");
            manager.Support("ctx-size", new(From: "b11483", Until: "b11483"), false, false, report);
            store.Update(DocumentKind.Servers, json.ServersDocument, ContractValidation.Servers, old => old! with
            { Servers = old.Servers.Select(s => s.Name == "second" ? s with { Backend = new("fixed", other.Id), PresetIds = [one.Id, two.Id] } : s).ToArray() });
            state = State();
            var resetImpact = ResetCli(0, "--dry-run");
            Check(State() == state && resetImpact.Contains("invalid-integer") && resetImpact.Contains("Recuperar: temp") &&
                resetImpact.Contains("first") && resetImpact.Contains("second"), "reset redirected impact covers restoration and multiple entities without writes");
            Check(resetImpact.Contains("Conservar/disponibilidad desconocida") && resetImpact.Contains("2048"), "reset applicability change reports preserved assignment separately from cascade");
            ResetCli(2, "--unknown"); ResetCli(2, "--dry-run", "--dry-run");
            Check(State() == state, "reset invalid grammar preserves state");
            using var cancelled = new StringWriter();
            Check(!manager.Reset(false, cancelled, () => false) && State() == state, "reset cancellation preserves all catalogs");
            ResetCli(2); Check(State() == state, "reset requires interactive input even through CLI");
            using var appliedReport = new StringWriter();
            Check(manager.Reset(false, appliedReport, () => true) && appliedReport.ToString().StartsWith(resetImpact, StringComparison.Ordinal), "reset dry-run and real operation share impact");
            Check(ContractJson.Write(new(1, manager.List()), json.ParametersDocument) == ContractJson.Write(ParameterManager.Factory(), json.ParametersDocument), "reset restores exact bundled factory catalog");
            Check(!config.View("one").Own.ContainsKey("ctx-size") && config.View("two").Own["ctx-size"] == "2048" &&
                config.View("one").Own["temperature"] == "0.4" && !config.View("one").Own.ContainsKey("toggle") &&
                !config.View("two").Own.ContainsKey("temperature") &&
                !servers.View("first").Server.Parameters.ContainsKey("scope-change"), "reset removes only retired or incompatible assignments and preserves restored compatible values");
            Check(config.List().Length == 2 && servers.List().Length == 2 && config.List().All(p => p.Preset.ModelId is null) &&
                File.ReadAllBytes(path).SequenceEqual(modelBytes) && File.ReadAllText(instances) == "active fixture", "reset preserves entities resources active files and does not restore removed model assignments");
            var resetConfirmations = 0;
            Check(!manager.Reset(false, report, () => false), "unchanged factory still requires confirmation");
            manager.Reset(false, report, () => { resetConfirmations++; if (resetConfirmations == 1) config.Set("two", "ctx-size", "3072"); return true; });
            Check(resetConfirmations == 2 && config.View("two").Own["ctx-size"] == "3072", "reset recalculates and reconfirms after concurrent assignment change");
            foreach (var stage in new[] { WriteStage.BeforeReplace, WriteStage.AfterReplace, WriteStage.AfterParameterCheckpoint })
            {
                manager.Define(new("reset-custom", "integer", "model", "INI", "", [], Choices: [], Support: []));
                config.Set("one", "reset-custom", "1");
                store.Update(DocumentKind.Servers, json.ServersDocument, ContractValidation.Servers, old => old! with
                { Servers = old.Servers.Select(s => s with { Parameters = s.Parameters.Append(new("reset-custom", JsonSerializer.SerializeToElement(3, json.Int32))).ToDictionary() }).ToArray() });
                state = State();
                var fault = new ParameterManager(home, new LocalStore(home, observed => { if (observed == stage) throw new IOException("reset publication fault"); }));
                try { fault.Reset(false, report, () => true); throw new Exception("expected reset failure"); }
                catch (StorageException e) { Check(e.Published == (stage != WriteStage.BeforeReplace), "reset publication status " + stage); }
                catch (IOException) when (stage == WriteStage.BeforeReplace) { Check(true, "reset failure precedes publication"); }
                if (stage == WriteStage.BeforeReplace)
                {
                    Check(State() == state, "reset failure before commit preserves state");
                    manager.Reset(false, report, () => true);
                }
                else Check(new ParameterManager(home).Find("reset-custom") is null &&
                    store.Read(DocumentKind.Presets, json.PresetsDocument, ContractValidation.Presets)!.Presets.All(p => !p.Parameters.ContainsKey("reset-custom")) &&
                    store.Read(DocumentKind.Servers, json.ServersDocument, ContractValidation.Servers)!.Servers.All(s => !s.Parameters.ContainsKey("reset-custom")), "reset committed catalogs consistent after fault " + stage);
                config.Set("two", "ctx-size", "2048");
                Check(!File.Exists(home.PathFor("catalogs/parameter-transaction.json")), "reset next writer recovers checkpoint " + stage);
            }
            Console.WriteLine($"OK: {checks} comprobaciones del catálogo de parámetros" + (executable is null ? "." : " con CLI nativa."));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }

        string Availability(string name, string build, string variant = "cuda@13") => ParameterManager.Availability(manager.Find(name), backend: null, "ggml", build, variant);
        string ResetCli(int expected, params string[] options)
        {
            using var output = new StringWriter(); using var error = new StringWriter();
            int code; string stdout; string stderr;
            if (executable is null)
            {
                code = ConfigurationCli.Run("config", ["param-reset", .. options], output, error, config,
                    parameterConfirmation: () => throw new ConfigurationException("Se requiere entrada interactiva."));
                stdout = output.ToString(); stderr = error.ToString();
            }
            else
            {
                var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
                start.Environment["COQUENA_HOME"] = root;
                foreach (var arg in new[] { "config", "param-reset" }.Concat(options)) start.ArgumentList.Add(arg);
                using var process = Process.Start(start)!; process.StandardInput.Close();
                var result = process.StandardOutput.ReadToEndAsync(); var diagnostic = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(10000)) { process.Kill(true); throw new Exception("Reset CLI timeout"); }
                code = process.ExitCode; stdout = result.GetAwaiter().GetResult(); stderr = diagnostic.GetAwaiter().GetResult();
            }
            Check(code == expected && (expected == 0 ? stderr.Length == 0 : stderr.Contains("Error:")), "reset CLI: " + stderr);
            return stdout;
        }
        string State() => string.Join("\n", new[] { "catalogs/parameters.json", "catalogs/presets.json", "catalogs/servers.json" }.Select(p => File.Exists(home.PathFor(p)) ? File.ReadAllText(home.PathFor(p)) : "absent"));
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception("Parameters: " + message); }
        void Reject(Action action, string message)
        {
            try { action(); } catch (Exception e) when (e is ConfigurationException or ContractException or StorageException or IOException) { checks++; return; }
            throw new Exception("Parameters accepted: " + message);
        }
        string Cli(int expected, params string[] args)
        {
            string stdout, stderr; int code;
            if (executable is null)
            {
                using var output = new StringWriter(); using var error = new StringWriter();
                code = ParameterCli.Run(args, output, error, manager, config, () => throw new ConfigurationException("Se requiere entrada interactiva."));
                stdout = output.ToString(); stderr = error.ToString();
            }
            else
            {
                var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
                start.Environment["COQUENA_HOME"] = root;
                start.ArgumentList.Add("param"); foreach (var arg in args) start.ArgumentList.Add(arg);
                using var process = Process.Start(start)!; process.StandardInput.Close();
                var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(10000)) { process.Kill(true); throw new Exception("Parameter CLI timeout"); }
                stdout = output.GetAwaiter().GetResult(); stderr = error.GetAwaiter().GetResult(); code = process.ExitCode;
            }
            Check(code == expected && (expected == 0 ? stderr.Length == 0 : stderr.Contains("Error:")), $"CLI {string.Join(' ', args)}: {code} {stdout} {stderr}");
            return stdout;
        }
    }
}
