using System.Diagnostics;
using Coquena.Backends;
using Coquena.Configuration;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Servers;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class ServerTests
{
    public static void Run(string? executable)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-servers-" + Guid.NewGuid());
        var home = new Home(root); var store = new LocalStore(home);
        var manager = new ServerManager(home); var json = ContractJsonContext.Default;
        var checks = 0;
        try
        {
            Check(CliTest(["default", "list"], 2).Length == 0 && !Directory.Exists(root), "invalid grammar before storage");
            CliTest(["create", "default"], 0);
            Check(manager.Default()?.Name == "default", "explicit initial default");
            Check(manager.View(null).Effective["port"].GetInt32() == 9931, "new catalog starts at 9931");
            Check(manager.View(null).Effective["metrics"].GetBoolean(), "metrics default");
            Check(manager.View(null).Effective["host"].GetString() == "127.0.0.1", "host default");
            Reject(() => manager.Compose(null), "missing backend");
            Check(CliTest(["create", "default"], 1).Length == 0, "duplicate default");
            var lts = manager.Create("lts");
            Check(lts.Parameters["port"].GetInt32() == 9932, "automatic max plus one");
            CliTest(["default", "lts"], 0);
            CliTest(["set", "host", "localhost"], 0);
            Check(manager.View("lts").Effective["host"].GetString() == "localhost", "omitted name resolves designation");
            CliTest(["default", "set", "metrics", "false"], 0);
            Check(!manager.View("default").Effective["metrics"].GetBoolean() && manager.View(null).Effective["metrics"].GetBoolean(), "explicit default action");
            CliTest(["default", "default"], 0);
            Check(manager.Default()?.Name == "default", "designate named default");
            CliTest(["default", "unset", "metrics"], 0);
            Check(manager.View("default").Effective["metrics"].GetBoolean(), "unset static default");
            Reject(() => manager.Create("show"), "reserved name");
            Reject(() => manager.Create("LTS"), "case-insensitive duplicate");
            Reject(() => manager.Create("collision", 9932), "port collision");
            Reject(() => manager.Set(null, "port", "9932"), "edit collision");
            Reject(() => manager.Set(null, "metrics", "yes"), "boolean type");
            Reject(() => manager.Set(null, "threads", "1"), "model parameter rejected");
            Reject(() => manager.Set(null, "models-preset", "custom.ini"), "INI owned by manager");
            Reject(() => manager.Set(null, "made-up", "1"), "catalog scope");
            Reject(() => manager.Create("CON"), "unsafe Windows name");
            var untouched = File.ReadAllText(home.PathFor("catalogs/servers.json"));
            var faulty = new ServerManager(home, new LocalStore(home, stage =>
            { if (stage == WriteStage.BeforeReplace) throw new IOException("injected persistence failure"); }));
            Reject(() => faulty.Set("lts", "port", null), "failed reassignment write");
            Check(untouched == File.ReadAllText(home.PathFor("catalogs/servers.json")), "failed write preserves port");
            var high = manager.Create("high", 65535);
            var before = File.ReadAllText(home.PathFor("catalogs/servers.json"));
            Reject(() => manager.Create("overflow"), "overflow create");
            Reject(() => manager.Set("lts", "port", null), "overflow unset");
            Check(before == File.ReadAllText(home.PathFor("catalogs/servers.json")), "overflow leaves previous document");
            manager.Set("high", "port", "9000");
            Check(manager.Set("lts", "port", null).Parameters["port"].GetInt32() == 9933, "unset uses catalog max including own");
            Parallel.For(0, 6, i => new ServerManager(home).Create("parallel" + i));
            var ports = manager.List().Select(s => s.Server.Parameters["port"].GetInt32()).ToArray();
            Check(ports.Distinct().Count() == ports.Length && ports.Max() == 9939, "concurrent allocation unique");

            var backend = new BackendRecord(Guid.NewGuid(), "b11482", "fixture", "ggml", "ggml-org/llama.cpp", "cpu", "cpu", "win-x64", "backends/fixture/server.exe", true);
            Directory.CreateDirectory(Path.GetDirectoryName(home.PathFor(backend.ServerPath))!);
            File.WriteAllText(home.PathFor(backend.ServerPath), "fixture");
            store.Update(DocumentKind.Backends, json.BackendsDocument, ContractValidation.Backends, _ => new(1, [backend]));
            new BackendInstaller(home).Use("cpu:latest");
            Reject(() => manager.Compose(null), "empty server");
            var model = new ModelRecord(Guid.NewGuid(), "fixture/model:Q4", new("fixture/model", "main", new string('a', 40), [new("single.gguf", null)]),
                [new("single.gguf", "models/fixture/model/single.gguf", 0)], []);
            store.Update(DocumentKind.Models, json.ModelsDocument, ContractValidation.Models, _ => new(1, [model]));
            var path = home.PathFor(model.Files[0].LocalPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, ConfigurationTests.Fixture(null));
            var config = new ConfigurationManager(home);
            var chat = config.Create(model.Name, "chat"); var code = config.Create(model.Name, "code");
            CliTest(["preset", "add", "chat", "code"], 0);
            Check(manager.View(null).Server.PresetIds.SequenceEqual(new[] { chat.Id, code.Id }), "explicit preset list");
            CliTest(["preset", "add", "chat"], 0);
            Check(manager.View(null).Presets.Length == 2, "duplicate association idempotent");
            Reject(() => manager.Presets(null, ["chat", "absent"], true), "atomic preset names");
            var plan = manager.Compose(null);
            Check(plan.Backend.Id == backend.Id && plan.Presets.Ini.Contains("[chat]") && plan.Presets.Ini.Contains("[code]"), "real composition every preset");
            Check(plan.Presets.ResourceIds.SequenceEqual(new[] { model.Id }), "effective resource IDs");
            var own = manager.Use(null, "oficial/cpu:latest");
            Check(own.Backend is { Kind: "latest", Origin: "ggml", Variant: "cpu" }, "latest intention normalized");
            manager.Use(null, "cpu:b11482");
            Check(manager.View(null).Server.Backend?.BackendId == backend.Id, "pin intention");
            var newer = backend with { Id = Guid.NewGuid(), Build = "b11483", TracksLatest = true };
            store.Update(DocumentKind.Backends, json.BackendsDocument, ContractValidation.Backends, _ => new(1, [backend with { TracksLatest = false }, newer]));
            Check(manager.Compose(null).Backend.Id == backend.Id, "fixed keeps build");
            manager.Use(null, "cpu:latest");
            Check(manager.Compose(null).Backend.Id == newer.Id, "latest resolves installed current");
            CliTest(["use", "default"], 0);
            Check(manager.View(null).Server.Backend is null && manager.Compose(null).Backend.Id == newer.Id, "inherit global selection");
            store.Update(DocumentKind.Config, json.ConfigDocument, ContractValidation.Config, old => old! with { DefaultBackend = new("fixed", backend.Id) });
            Check(manager.Compose(null).Backend.Id == backend.Id, "inherit refreshed global");
            CliTest(["use", "cpu:latest"], 0);
            Check(manager.Compose(null).Backend.Id == newer.Id, "own selection overrides global");
            CliTest(["use", "default"], 0);
            var seen = false;
            manager.Compose(null, (b, ids) => { seen = b.Id == backend.Id && ids.SequenceEqual(new[] { chat.Id, code.Id }); return new("injected", [], []); });
            Check(seen, "injected composition receives full selection");
            config.Remove("code");
            Check(manager.View(null).Diagnostics.Any(d => d.Contains("Preset ausente")), "show missing preset");
            Reject(() => manager.Compose(null), "no omitted broken preset");
            File.Delete(path);
            manager.Presets(null, ["chat"], false);
            manager.Presets("lts", ["chat"], true);
            Reject(() => manager.Compose("lts"), "missing model file");
            store.Update(DocumentKind.Backends, json.BackendsDocument, ContractValidation.Backends, _ => new(1, [newer]));
            Check(manager.View(null).Backend is null, "missing fixed selection no substitute");
            Reject(() => manager.Compose(null), "missing default selection no fallback");
            CliTest(["show"], 0);
            store.Update(DocumentKind.Servers, json.ServersDocument, ContractValidation.Servers, old => old! with { Servers = old.Servers.Where(s => s.Name != "default").ToArray() });
            Check(CliTest(["default", "show"], 1).Length == 0, "removed default not recreated");
            Check(manager.List().All(s => s.Server.Name != "default"), "catalog stays explicit");
            store.Update(DocumentKind.Servers, json.ServersDocument, ContractValidation.Servers, _ => new(1, []));
            store.Update(DocumentKind.Config, json.ConfigDocument, ContractValidation.Config, old => old! with { DefaultServerId = null });
            manager.EnsureInitial();
            Check(manager.List().Length == 0, "empty persisted catalog not initialized again");
            Check(manager.Create("legacy", 8080).Parameters["port"].GetInt32() == 8080, "explicit legacy port");
            manager.EnsureInitial();
            Check(new ServerManager(home).View("legacy").Effective["port"].GetInt32() == 8080, "persisted legacy port preserved");
            Check(manager.Set("legacy", "port", null).Parameters["port"].GetInt32() == 8081, "legacy unset remains max plus one below 9931");
            store.Update(DocumentKind.Servers, json.ServersDocument, ContractValidation.Servers, _ => new(1, []));
            CliTest(["create", "default"], 0);
            Check(manager.Default() is null, "recreating name does not silently select it");
            manager.Default("default");
            manager.EnsureInitial();
            Check(manager.List().Length == 1, "initialization idempotent");
            store.Update(DocumentKind.Servers, json.ServersDocument, ContractValidation.Servers, _ => new(1, []));
            File.Delete(home.PathFor("catalogs/servers.json"));
            Reject(manager.EnsureInitial, "missing catalog with existing designation is not empty");
            Console.WriteLine($"OK: {checks} comprobaciones de servidores" + (executable is null ? "." : " con CLI nativa."));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }

        string CliTest(string[] args, int expected)
        {
            string stdout, stderr; int exit;
            if (executable is null)
            {
                using var output = new StringWriter(); using var error = new StringWriter();
                exit = ServerCli.Run(args, output, error, manager); stdout = output.ToString(); stderr = error.ToString();
            }
            else
            {
                var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                start.Environment["COQUENA_HOME"] = root;
                start.ArgumentList.Add("server"); foreach (var arg in args) start.ArgumentList.Add(arg);
                using var process = Process.Start(start)!;
                var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(10000)) { process.Kill(true); throw new Exception("CLI server timeout"); }
                stdout = output.GetAwaiter().GetResult(); stderr = error.GetAwaiter().GetResult(); exit = process.ExitCode;
            }
            Check(exit == expected && (expected == 0 ? stderr.Length == 0 : stderr.Contains("Error:")), $"CLI {string.Join(' ', args)}: {exit} {stderr}");
            return stdout;
        }
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception("Server test: " + message); }
        void Reject(Action action, string message)
        {
            try { action(); } catch (Exception e) when (e is ConfigurationException or DownloadException or BackendException or StorageException or ContractException or IOException) { checks++; return; }
            throw new Exception("Server test expected rejection: " + message);
        }
    }
}
