using System.Diagnostics;
using Coquena.Contracts;
using Coquena.Servers;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class ServerRemovalTests
{
    public static void Run(string? executable)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-server-removal-" + Guid.NewGuid());
        var json = ContractJsonContext.Default; var checks = 0;
        try
        {
            var home = new Home(root); var store = new LocalStore(home); var manager = new ServerManager(home);
            manager.Create("default"); var other = manager.Create("other");
            var log = home.PathFor("logs/default/preserved.log"); Directory.CreateDirectory(Path.GetDirectoryName(log)!); File.WriteAllText(log, "preserved");
            var preset = new PresetRecord(Guid.NewGuid(), "unrelated", Guid.NewGuid(), []);
            store.Update(DocumentKind.Presets, json.PresetsDocument, ContractValidation.Presets, _ => new(1, [preset]));
            manager.Presets(null, [preset.Name], true);
            var presetsBefore = File.ReadAllText(home.PathFor("catalogs/presets.json"));
            Check(Cli(home, ["remove", "extra"]) == 2, "strict removal grammar");
            Check(Cli(home, ["default", "remove"]) == 0, "remove explicit default");
            Check(manager.Default() is null && manager.List().Single().Server.Id == other.Id, "designation cleared without selecting other");
            Check(File.ReadAllText(log) == "preserved" && File.ReadAllText(home.PathFor("catalogs/presets.json")) == presetsBefore, "logs and referenced presets preserved");
            manager.EnsureInitial(); Check(manager.List().Length == 1, "default not recreated");
            Check(Cli(home, ["default", "remove"]) == 1 && Cli(home, ["remove"]) == 1, "missing target/default diagnosed");
            manager.Create("default"); manager.Default("other");
            Check(Cli(home, ["default", "remove"]) == 0 && manager.Default()?.Id == other.Id, "explicit default name preserves other designation");
            Check(Cli(home, ["remove"]) == 0 && manager.Default() is null && manager.List().Length == 0, "omitted target removes designated server");
            manager.EnsureInitial(); Check(manager.List().Length == 0, "empty catalog remains empty");
            var endedServer = manager.Create("ended");
            var endedId = Guid.NewGuid();
            var oldExecution = new InstanceDocument(1, endedId, endedServer.Id, Guid.NewGuid(), "b1", Environment.ProcessId,
                "detached", "127.0.0.1", 18085, true, [preset.Id], [preset.ModelId!.Value], $"run/ended/{endedId:D}.ini", $"logs/ended/{endedId:D}.log",
                Process.GetCurrentProcess().StartTime.ToFileTimeUtc() - 1, Environment.ProcessPath);
            store.Update(DocumentKind.Instance, json.InstanceDocument, ContractValidation.Instance, _ => oldExecution, server: "ended");
            Check(Cli(home, ["ended", "remove"]) == 0, "verified ended execution does not block removal");
            manager.Create("ended");
            Check(Cli(home, ["ended", "remove"]) == 0, "ended previous server identity does not block reused name");

            foreach (var stage in new[] { WriteStage.BeforeReplace, WriteStage.AfterReplace, WriteStage.AfterServerCheckpoint })
            {
                var faultHome = new Home(Path.Combine(root, stage.ToString())); var clean = new LocalStore(faultHome); var m = new ServerManager(faultHome);
                var selected = m.Create("default"); m.Create("keep");
                var beforeServers = File.ReadAllText(faultHome.PathFor("catalogs/servers.json"));
                var beforeConfig = File.ReadAllText(faultHome.PathFor("config.json"));
                var faulty = new ServerManager(faultHome, new LocalStore(faultHome, point => { if (point == stage) throw new IOException("injected removal publication fault"); }));
                using var output = new StringWriter(); using var error = new StringWriter();
                Check(ServerCli.Run(["default", "remove"], output, error, faulty) == 1, "fault reported at " + stage);
                var servers = clean.Read(DocumentKind.Servers, json.ServersDocument, ContractValidation.Servers)!;
                var config = clean.Read(DocumentKind.Config, json.ConfigDocument, ContractValidation.Config)!;
                if (stage == WriteStage.BeforeReplace)
                    Check(servers.Servers.Any(s => s.Id == selected.Id) && config.DefaultServerId == selected.Id &&
                        File.ReadAllText(faultHome.PathFor("catalogs/servers.json")) == beforeServers && File.ReadAllText(faultHome.PathFor("config.json")) == beforeConfig,
                        "failure before commit preserves both documents");
                else
                {
                    Check(servers.Servers.All(s => s.Id != selected.Id) && config.DefaultServerId is null, "read sees coherent committed snapshots at " + stage);
                    Check(error.ToString().Contains("publicad"), "published pending checkpoint diagnosed");
                    Check(new ServerManager(faultHome).List().Single().Server.Name == "keep" && !File.Exists(faultHome.PathFor("catalogs/server-removal-transaction.json")), "fresh invocation recovers checkpoints");
                    clean.Update(DocumentKind.Config, json.ConfigDocument, ContractValidation.Config, old => old! with { LogRetention = new(123) });
                    Check(clean.Read(DocumentKind.Config, json.ConfigDocument, ContractValidation.Config)!.DefaultServerId is null, "next writer does not restore removed designation");
                }
            }
            Console.WriteLine($"OK: {checks} comprobaciones de retirada de servidores" + (executable is null ? "." : " con CLI nativa."));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }

        int Cli(Home home, string[] args)
        {
            if (executable is null)
            {
                using var output = new StringWriter(); using var error = new StringWriter();
                return ServerCli.Run(args, output, error, new ServerManager(home));
            }
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            var variables = start.Environment.ToArray(); start.Environment.Clear();
            foreach (var group in variables.GroupBy(v => v.Key, StringComparer.OrdinalIgnoreCase)) start.Environment[group.Key] = group.Last().Value;
            start.Environment["COQUENA_HOME"] = home.Root;
            start.ArgumentList.Add("server"); foreach (var arg in args) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(10000)) { process.Kill(true); throw new Exception("server remove CLI timeout"); }
            stdout.GetAwaiter().GetResult(); stderr.GetAwaiter().GetResult(); return process.ExitCode;
        }
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception("Server removal: " + message); }
    }
}
