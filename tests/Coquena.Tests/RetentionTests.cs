using System.Diagnostics;
using System.Globalization;
using Coquena.Configuration;
using Coquena.Contracts;
using Coquena.Servers;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class RetentionTests
{
    public static void Run(string? executable)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-retention-" + Guid.NewGuid());
        var home = new Home(root);
        var manager = new ConfigurationManager(home);
        var servers = new ServerManager(home);
        var checks = 0;
        try
        {
            servers.EnsureInitial(); servers.Create("other");
            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("es-AR");
                foreach (var pair in new (string Text, long Bytes)[] { ("1", 1), ("100MB", 100000000), ("100MiB", 104857600), ("100mib", 104857600), ("100 MiB", 104857600), ("1.5GiB", 1610612736), ("0.5KiB", 512), ("1.000B", 1), ("9223372036854775807", long.MaxValue), ("9007199254740993B", 9007199254740993) })
                    Check(LogRetentionPolicy.Parse(pair.Text) == pair.Bytes, "exact size " + pair.Text);
                foreach (var bad in new[] { "0", "-1", "1,5GiB", "1,000", "0.1KiB", "1e3", "1TB", "9223372036854775808", "1.1B", "+1", "1 MiB extra" })
                {
                    try { LogRetentionPolicy.Parse(bad); throw new Exception("accepted " + bad); }
                    catch (ConfigurationException) { checks++; }
                }
            }
            finally { CultureInfo.CurrentCulture = culture; }
            Check(Invoke(0, "config", "log-retention", "show").Contains("inicial"), "initial policy");
            Invoke(0, "config", "log-retention", "set", "1.5GiB");
            Check(Invoke(0, "server", "log-retention", "show").Contains("1610612736 bytes"), "default server inherits global");
            Invoke(0, "server", "other", "log-retention", "set", "512B");
            Invoke(0, "config", "log-retention", "set", "200MiB");
            Check(Invoke(0, "server", "other", "log-retention", "show").Contains("512 bytes") && servers.Retention("other").Origin == "propio", "override independent of global changes");
            Check(new ServerManager(home).Retention(null).MaxBytes == 209715200, "persisted global policy");
            var log = home.PathFor("logs/other/" + Guid.NewGuid() + ".log"); Directory.CreateDirectory(Path.GetDirectoryName(log)!); File.WriteAllText(log, "keep");
            Invoke(0, "server", "other", "log-retention", "set", "1");
            Invoke(0, "server", "other", "log-retention", "show");
            Check(File.Exists(log), "setting and showing do not clean logs");
            var before = File.ReadAllText(home.PathFor("catalogs/servers.json"));
            Invoke(1, "server", "other", "log-retention", "set", "0.1KiB");
            Invoke(2, "server", "other", "log-retention", "set", "1", "extra");
            Check(File.ReadAllText(home.PathFor("catalogs/servers.json")) == before, "invalid CLI leaves catalog unchanged");
            Invoke(0, "server", "other", "log-retention", "unset");
            Check(servers.Retention("other").Origin == "global", "unset restores global");
            Invoke(0, "config", "log-retention", "unset");
            Check(manager.Retention().Origin == "inicial" && servers.Retention("other").MaxBytes == 104857600, "global unset restores initial");
            Invoke(2, "config", "set", "ctx-size", "512");
            Invoke(2, "config", "show", "--family", "cpu");
            var failing = new LocalStore(home, stage => { if (stage == WriteStage.BeforeReplace) throw new IOException("fixture"); });
            var config = File.ReadAllText(home.PathFor("config.json"));
            before = File.ReadAllText(home.PathFor("catalogs/servers.json"));
            try { new ConfigurationManager(home, failing).SetRetention(10); throw new Exception("expected write failure"); }
            catch (IOException) { checks++; }
            try { new ServerManager(home, failing).SetRetention("other", 10); throw new Exception("expected write failure"); }
            catch (IOException) { checks++; }
            Check(File.ReadAllText(home.PathFor("config.json")) == config && File.ReadAllText(home.PathFor("catalogs/servers.json")) == before, "failed writes preserve both documents");
            try { servers.SetRetention("other", 0); throw new Exception("expected invalid policy"); }
            catch (ContractException) { checks++; }
            Check(File.ReadAllText(home.PathFor("catalogs/servers.json")) == before, "invalid contract does not write");
            servers.Set("default", "models-max", "2"); servers.Set("other", "models-max", "3");
            Check(servers.View("default").Effective["models-max"].GetInt32() == 2 && servers.View("other").Effective["models-max"].GetInt32() == 3, "process values independent");
            Console.WriteLine($"OK: {checks} comprobaciones de políticas de retención" + (executable is null ? "." : " con CLI Native AOT."));
        }
        finally { Directory.Delete(root, true); }

        void Check(bool ok, string message) { if (!ok) throw new Exception("Retention: " + message); checks++; }
        string Invoke(int expected, params string[] arguments)
        {
            string stdout, stderr; int code;
            if (executable is null)
            {
                using var output = new StringWriter(); using var error = new StringWriter();
                code = arguments[0] == "server" ? ServerCli.Run(arguments[1..], output, error, servers) : ConfigurationCli.Run(arguments[0], arguments[1..], output, error, manager);
                stdout = output.ToString(); stderr = error.ToString();
            }
            else
            {
                var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                start.Environment["COQUENA_HOME"] = root;
                foreach (var argument in arguments) start.ArgumentList.Add(argument);
                using var process = Process.Start(start)!;
                var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(10000)) { process.Kill(true); throw new Exception("CLI timeout"); }
                code = process.ExitCode; stdout = output.GetAwaiter().GetResult(); stderr = error.GetAwaiter().GetResult();
            }
            Check(code == expected && (expected == 0 ? stderr.Length == 0 : stderr.Contains("Error:")), string.Join(' ', arguments) + " " + stdout + " " + stderr);
            return stdout;
        }
    }
}
