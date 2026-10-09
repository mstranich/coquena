using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Coquena;
using Coquena.Configuration;
using Coquena.Contracts;
using Coquena.Servers;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class ServerRuntimeTests
{
    internal static void Fixture(string[] args)
    {
        var directory = AppContext.BaseDirectory;
        var behavior = File.Exists(Path.Combine(directory, "behavior.txt")) ? File.ReadAllText(Path.Combine(directory, "behavior.txt")) : "normal";
        if (behavior == "crash") { Console.Error.WriteLine("fixture crashed"); Environment.Exit(7); }
        var host = args[Array.IndexOf(args, "--host") + 1];
        var port = int.Parse(args[Array.IndexOf(args, "--port") + 1]);
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { ExclusiveAddressUse = true };
        socket.Bind(new IPEndPoint(IPAddress.Parse(host), port)); socket.Listen(2);
        var stop = 0;
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; Console.Error.WriteLine("fixture received Ctrl+C");
            if (behavior != "hang") Interlocked.Exchange(ref stop, 1);
        };
        if (behavior == "hang")
        {
            using var child = Process.Start(new ProcessStartInfo(Path.Combine(directory, "Coquena.Tests.exe"))
            { UseShellExecute = false, ArgumentList = { "--runtime-child" } })!;
            File.WriteAllText(Path.Combine(directory, "child.pid"), child.Id.ToString());
        }
        Console.WriteLine("fixture stdout ready"); Console.Error.WriteLine("fixture stderr ready");
        Console.WriteLine("arguments: " + string.Join('|', args));
        while (Volatile.Read(ref stop) == 0) Thread.Sleep(20);
        Console.WriteLine("fixture graceful exit");
    }

    internal static int ConsoleSignal(string[] args)
    {
        var instance = new InstanceDocument(1, Guid.Parse(args[4]), Guid.NewGuid(), Guid.NewGuid(), "test", int.Parse(args[1]),
            "foreground", "127.0.0.1", 1, true, [], [], "unused", "unused", long.Parse(args[2]), args[3]);
        return WindowsServerProcess.Signal(instance);
    }

    public static void Run(string? executable = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-runtime-" + Guid.NewGuid());
        var home = new Home(root); var store = new LocalStore(home); var json = ContractJsonContext.Default;
        var manager = new ServerManager(home); var runtime = new ServerRuntime(home); var checks = 0;
        var backendDirectory = home.PathFor("backends/fixture");
        var fixtureExecutable = Path.Combine(AppContext.BaseDirectory, "Coquena.Tests.exe");
        var product = executable ?? Path.Combine(AppContext.BaseDirectory, "coquena.exe");
        if (!File.Exists(product)) product = Path.GetFullPath("src/Coquena/bin/Debug/net10.0/coquena.exe");
        var live = new List<InstanceDocument>();
        try
        {
            home.Ensure(); Directory.CreateDirectory(backendDirectory);
            // A managed apphost needs its adjacent assemblies/config. A native fixture
            // requires only its executable. No external backend or model is downloaded.
            foreach (var source in Directory.GetFiles(AppContext.BaseDirectory))
                if (Path.GetExtension(source) is ".dll" or ".json" || Path.GetFileName(source) == "Coquena.Tests.exe")
                    File.Copy(source, Path.Combine(backendDirectory, Path.GetFileName(source)), true);
            var backend = new BackendRecord(Guid.NewGuid(), "b11482", "fixture", "ggml", "ggml-org/llama.cpp", "cpu", "cpu", "win-x64", "backends/fixture/Coquena.Tests.exe", true);
            store.Update(DocumentKind.Backends, json.BackendsDocument, ContractValidation.Backends, _ => new(1, [backend]));
            manager.EnsureInitial(); new Coquena.Backends.BackendInstaller(home).Use("cpu:latest");
            manager.Set(null, "port", FreePort().ToString());
            var model = new ModelRecord(Guid.NewGuid(), "fixture/model:Q4", new("fixture/model", "main", new string('a', 40), [new("single.gguf", null)]),
                [new("single.gguf", "models/fixture/model/single.gguf", 0)], []);
            Directory.CreateDirectory(Path.GetDirectoryName(home.PathFor(model.Files[0].LocalPath))!);
            File.WriteAllBytes(home.PathFor(model.Files[0].LocalPath), ConfigurationTests.Fixture(null));
            store.Update(DocumentKind.Models, json.ModelsDocument, ContractValidation.Models, _ => new(1, [model]));
            new ConfigurationManager(home).Create("fixture/model:Q4", "chat"); manager.Presets(null, ["chat"], true);
            using var output = new StringWriter();

            var start = runtime.Start(null, true, output); var first = start.Instance; live.Add(first);
            Wait(() => Log(first).Contains("ready"));
            Check(start.Started && first.Mode == "detached" && first.ProcessCreated > 0 && File.Exists(home.PathFor(first.IniPath)), "published identity and INI");
            Check(Log(first).Contains("stderr ready") && Log(first).Contains("--metrics"), "stdout/stderr and static flags");
            Check(first.Port == manager.View(null).Effective["port"].GetInt32() && Log(first).Contains("--port|" + first.Port), "start emits persisted explicit port");
            Check(runtime.Recover("default")?.InstanceId == first.InstanceId, "fresh manager recovers detached");
            ObservabilityTests.Run(home, runtime, first);
            var followId = Guid.NewGuid(); var followCapture = Path.Combine(root, "follow.txt");
            using (var follower = WindowsServerProcess.Create(product, ["server", "logs", "-f"], followCapture, followId, root))
            {
                follower.Activate();
                try
                {
                    Wait(() => ReadText(followCapture).Contains("ready"));
                    Send(follower, followId); Wait(() => !ProcessAlive(follower.Pid));
                    Check(runtime.Recover("default")?.InstanceId == first.InstanceId && !Log(first).Contains("received Ctrl+C"), "real Ctrl+C ends logs follower only");
                }
                finally { if (ProcessAlive(follower.Pid)) { using var process = Process.GetProcessById(follower.Pid); process.Kill(true); process.WaitForExit(); } }
            }
            var duplicates = new List<ServerStartResult>();
            Parallel.For(0, 4, _ =>
            {
                try { var result = new ServerRuntime(home).Start(null, true, TextWriter.Null); lock (duplicates) duplicates.Add(result); }
                catch (ConfigurationException) { }
            });
            Check(duplicates.All(s => !s.Started && s.Instance.InstanceId == first.InstanceId), "concurrent start never duplicates");
            Check(Cli(["server", "start", "-d"]).Code == 0 && Read()?.InstanceId == first.InstanceId, "second CLI start preserves instance");
            var other = manager.Create("other", FreePort()); manager.Presets("other", ["chat"], true);
            var second = runtime.Start("other", true, output).Instance; live.Add(second); Wait(() => Log(second).Contains("ready"));
            manager.Default("other");
            Check(Cli(["server", "default", "stop"]).Code == 0, "explicit default name stops named server despite another designation");
            Check(Read() is null && !File.Exists(home.PathFor(first.IniPath)) && Log(first).Contains("graceful exit"), "graceful stop cleans active state and preserves log");
            Check(Cli(["server", "default", "logs"]).Out.Contains("graceful exit"), "logs available after stop using last reference despite another default");
            File.AppendAllText(home.PathFor(first.LogPath), new string('x', 70000) + "\nrecent UTF8 á fin\n");
            var recent = Cli(["server", "default", "logs"]);
            Check(recent.Code == 0 && recent.Out.Contains("recent UTF8 á fin") && recent.Out.Length < 66000, $"recent logs bounded and UTF8 intact: code={recent.Code}, out={recent.Out}, error={recent.Error}");
            var logJson = ContractJsonContext.Default.LastLogDocument;
            store.Update(DocumentKind.LastLog, logJson, _ => { }, old => old! with { ServerId = Guid.NewGuid() }, server: "default");
            Check(Cli(["server", "default", "logs"]).Code == 1, "last log does not reconnect another server ID");
            store.Update(DocumentKind.LastLog, logJson, _ => { }, old => old! with { ServerId = first.ServerId }, server: "default");
            Check(runtime.Recover("other")?.InstanceId == second.InstanceId, "stop does not affect another server");
            var byId = runtime.Start("default", true, output).Instance; live.Add(byId); Wait(() => Log(byId).Contains("ready"));
            Check(Cli(["server", "stop"]).Code == 0 && runtime.Recover("other") is null && runtime.Recover("default")?.InstanceId == byId.InstanceId,
                "omitted name stops designated default only");
            store.Update(DocumentKind.Config, json.ConfigDocument, ContractValidation.Config, old => old! with { DefaultServerId = null });
            Check(Cli(["server", "stop"]).Code == 1 && manager.Default() is null, "absent designation errors without choosing or recreating a default");
            Check(Cli(["server", "stop", "--id", byId.InstanceId.ToString("D")]).Code == 0 && Read() is null, "explicit ID stops exact instance without default designation");
            manager.Default("default");
            var byName = runtime.Start("other", true, output).Instance; live.Add(byName); Wait(() => Log(byName).Contains("ready"));
            Check(Cli(["server", "other", "stop"]).Code == 0 && runtime.Recover("other") is null, "named stop resolves its sole instance");
            var alreadyStopped = Cli(["server", "other", "stop"]);
            Check(alreadyStopped.Code == 0 && alreadyStopped.Out.Contains("sin instancia activa"), "named stop is idempotent for stopped server");
            Check(Cli(["server", "missing", "stop"]).Code == 1, "missing named server is diagnosed");

            var consoleId = Guid.NewGuid(); var consoleLog = Path.Combine(root, "detached-console.txt");
            int consolePid;
            using (var console = WindowsServerProcess.Create(product, ["server", "start", "-d"], consoleLog, consoleId, root))
            {
                consolePid = console.Pid; console.Activate(); Wait(() => !ProcessAlive(consolePid));
            }

            var afterConsole = Read()!; live.Add(afterConsole); Wait(() => Log(afterConsole).Contains("ready"));
            Check(!ProcessAlive(consolePid) && runtime.Recover("default")?.InstanceId == afterConsole.InstanceId, "detached survives owning console closing");
            runtime.Stop(afterConsole.InstanceId, output);

            var concurrentCli = new (int Code, string Out, string Error)[4];
            Parallel.For(0, concurrentCli.Length, i => concurrentCli[i] = Cli(["server", "start", "-d"]));
            var concurrentInstance = Read()!; live.Add(concurrentInstance); Wait(() => Log(concurrentInstance).Contains("ready"));
            Check(concurrentCli.Count(result => result.Code == 0 && result.Out.StartsWith("Instancia ")) == 1,
                "concurrent CLI invocations publish one execution");
            Check(concurrentCli.All(result => result.Code is 0 or 1) && runtime.Recover("default")?.InstanceId == concurrentInstance.InstanceId,
                "concurrent losers report active/busy without another process");
            runtime.Stop(concurrentInstance.InstanceId, output);

            var failing = new ServerRuntime(home, new LocalStore(home, stage =>
            { if (stage == WriteStage.BeforeReplace) throw new IOException("injected instance publication failure"); }));
            try { failing.Start(null, true, output); throw new Exception("expected failed publication"); }
            catch (IOException) { Check(Read() is null && !Directory.GetFiles(home.PathFor("run/default"), "*.ini").Any(), "failed publication removes INI and suspended process never runs"); }

            var afterPublish = new ServerRuntime(home, new LocalStore(home, stage =>
            { if (stage == WriteStage.AfterReplace) throw new IOException("injected after durable publication"); }));
            try { afterPublish.Start(null, true, output); throw new Exception("expected incomplete audit"); }
            catch (StorageException e)
            {
                var abandoned = Read()!; live.Add(abandoned);
                Check(e.Published && abandoned.ProcessCreated > 0, "published failure retains recoverable identity");
                Wait(() => !ProcessAlive(abandoned.Pid));
                Check(runtime.Recover("default") is null && !File.Exists(home.PathFor(abandoned.IniPath)), "staging job kills suspended child and recovery cleans published record");
            }

            using (var occupied = new TcpListener(IPAddress.Loopback, 0))
            {
                occupied.Start(); var port = ((IPEndPoint)occupied.LocalEndpoint).Port;
                manager.Set(null, "port", port.ToString());
                Reject(() => runtime.Start(null, true, output), "occupied port");
                Check(Read() is null && manager.View(null).Effective["port"].GetInt32() == port, "no port reassignment or instance");
            }
            manager.Set(null, "port", FreePort().ToString()); manager.Set(null, "metrics", "false");
            var cli = Cli(["server", "start", "-d"]); Check(cli.Code == 0, "detached CLI returns prompt");
            var detached = Read()!; live.Add(detached); Wait(() => Log(detached).Contains("ready"));
            Check(runtime.Recover("default")?.InstanceId == detached.InstanceId && !Log(detached).Contains("--metrics"), "child survives CLI exit and disabled metrics");
            var ps = Cli(["server", "ps"]);
            Check(ps.Code == 0 && ps.Out.Contains(detached.InstanceId.ToString()) && ps.Out.Contains("desactivadas"), "CLI ps displays verified instance with disabled metrics");
            var newer = backend with { Id = Guid.NewGuid(), Build = "b11483" };
            store.Update(DocumentKind.Backends, json.BackendsDocument, ContractValidation.Backends, _ => new(1, [backend with { TracksLatest = false }, newer]));
            manager.Set(null, "models-autoload", "false");
            var restarted = runtime.Restart(null, output); live.Add(restarted); Wait(() => Log(restarted).Contains("ready"));
            Check(restarted.InstanceId != detached.InstanceId && restarted.BackendId == newer.Id && restarted.Mode == "detached", "restart resolves current installed latest");
            Check(Log(restarted).Contains("--no-models-autoload") && Read()?.InstanceId == restarted.InstanceId, "restart applies saved settings");
            Check(restarted.Port == detached.Port && restarted.Port == manager.View(null).Effective["port"].GetInt32() && Log(restarted).Contains("--port|" + restarted.Port), "restart preserves and emits persisted port");
            Check(!File.Exists(home.PathFor(detached.IniPath)) && File.Exists(home.PathFor(detached.LogPath)), "restart preserves previous log");

            store.Update(DocumentKind.Instance, json.InstanceDocument, ContractValidation.Instance, _ => restarted with { ExecutablePath = Path.Combine(root, "unrelated.exe") }, server: "default");
            Reject(() => runtime.Start(null, true, output), "uncertain identity blocks start");
            Reject(() => runtime.Stop(restarted.InstanceId, output), "uncertain identity blocks stop");
            Reject(() => runtime.StopServer("default", output), "uncertain identity blocks named stop too");
            Check(ProcessAlive(restarted.Pid) && File.Exists(home.PathFor(restarted.IniPath)), "uncertainty leaves process and files untouched");
            Check(Cli(["server", "ps"]).Out.Contains("identidad incierta") && ProcessAlive(restarted.Pid), "ps diagnoses uncertain identity without process action");
            store.Update(DocumentKind.Instance, json.InstanceDocument, ContractValidation.Instance, _ => restarted, server: "default");
            runtime.Stop(restarted.InstanceId, output);

            var saved = restarted with { InstanceId = Guid.NewGuid(), Pid = Environment.ProcessId, ProcessCreated = Process.GetCurrentProcess().StartTime.ToFileTimeUtc() - 1 };
            saved = saved with { IniPath = $"run/default/{saved.InstanceId:D}.ini", LogPath = $"logs/default/{saved.InstanceId:D}.log" };
            File.WriteAllText(home.PathFor(saved.IniPath), "fixture"); File.WriteAllText(home.PathFor(saved.LogPath), "old log");
            store.Update(DocumentKind.Instance, json.InstanceDocument, ContractValidation.Instance, _ => saved, server: "default");
            Check(runtime.Recover("default") is null && !File.Exists(home.PathFor(saved.IniPath)) && File.Exists(home.PathFor(saved.LogPath)), "PID reuse proves old execution ended without killing reused PID");

            Behavior("crash"); var crash = runtime.Start(null, true, output).Instance; live.Add(crash);
            Wait(() => !ProcessAlive(crash.Pid));
            Check(runtime.Recover("default") is null && !File.Exists(home.PathFor(crash.IniPath)) && Log(crash).Contains("crashed"), "unexpected exit cleaned without automatic restart");
            Behavior("hang"); var hanging = runtime.Start(null, true, output).Instance; live.Add(hanging); Wait(() => Log(hanging).Contains("ready"));
            var watch = Stopwatch.StartNew(); runtime.Stop(hanging.InstanceId, output);
            Check(watch.Elapsed.TotalSeconds >= 9.9 && watch.Elapsed.TotalSeconds < 14 && !ProcessAlive(hanging.Pid), "ten second policy forces unresponsive process");
            Check(!ProcessAlive(int.Parse(File.ReadAllText(Path.Combine(backendDirectory, "child.pid")))), "forced closure terminates router children too");
            Check(Log(hanging).Contains("received Ctrl+C") && output.ToString().Contains("[Cierre forzado]"), "signal delivered and force label");

            // Actual Windows console events reach the CLI in an isolated hidden
            // console. One/two keypresses use the same handler as a user keyboard.
            Behavior("normal"); Foreground(false);
            Behavior("hang"); Foreground(true);
            Behavior("normal");
            var stoppedRestart = runtime.Restart(null, output); live.Add(stoppedRestart); Wait(() => Log(stoppedRestart).Contains("ready"));
            Check(stoppedRestart.Mode == "detached", "restart starts a stopped server detached");
            File.Delete(home.PathFor(model.Files[0].LocalPath));
            Reject(() => runtime.Restart(null, output), "restart rejects saved missing model after stopping old instance");
            Check(Read() is null && !ProcessAlive(stoppedRestart.Pid) && manager.View(null).Server.PresetIds.Length == 1,
                "failed restart leaves stopped server configured without substitute");
            Check(Cli(["server", "stop", "invalid"]).Code == 2 && Cli(["server", "restart", "-d"]).Code == 2, "strict new grammar");
            Check(Cli(["server", "ps", "extra"]).Code == 2 && Cli(["server", "other", "ps"]).Code == 2 && Cli(["server", "logs", "--id", first.InstanceId.ToString()]).Code == 2, "strict observability grammar");
            foreach (var invalid in new string[][]
            {
                ["server", "stop", first.InstanceId.ToString("D")],
                ["server", "stop", "--id"], ["server", "stop", "--id", "invalid"],
                ["server", "stop", "--id", Guid.Empty.ToString("D")],
                ["server", "other", "stop", "--id", first.InstanceId.ToString("D")],
                ["server", "stop", "--id", first.InstanceId.ToString("D"), "extra"]
            }) Check(Cli(invalid).Code == 2, "stop rejects positional ID, conflicting selectors and malformed flags");
            Console.WriteLine($"OK: {checks} comprobaciones de ciclo de procesos" + (executable is null ? "." : " con CLI nativa."));

            void Foreground(bool twice)
            {
                var controllerId = Guid.NewGuid(); var capture = Path.Combine(root, controllerId + ".txt");
                using var controller = WindowsServerProcess.Create(product, ["server", "start"], capture, controllerId, root); controller.Activate();
                InstanceDocument? active = null;
                try
                {
                    Wait(() => { active = Read(); return active is not null && ReadText(capture).Contains("[Primer plano]") && Log(active).Contains("ready"); });
                    live.Add(active!);
                    Send(controller, controllerId); Wait(() => ReadText(capture).Contains("[Ctrl+C recibido]"));
                    Check(ReadText(capture).Contains("segundo Ctrl+C") && ReadText(capture).Contains("10 s"), "foreground label and first key acknowledgment: " + ReadText(capture));
                    if (twice)
                    {
                        Wait(() => Log(active!).Contains("received Ctrl+C"));
                        var elapsed = Stopwatch.StartNew(); Send(controller, controllerId);
                        Wait(() => !ProcessAlive(controller.Pid), 3000);
                        Check(elapsed.Elapsed.TotalSeconds < 3 && ReadText(capture).Contains("Segundo Ctrl+C recibido"), "second Ctrl+C immediately forces closure");
                    }
                    else { Wait(() => !ProcessAlive(controller.Pid)); Check(Log(active!).Contains("graceful exit"), "first Ctrl+C stops foreground gracefully"); }
                    Check(!ProcessAlive(active!.Pid) && Read() is null && !File.Exists(home.PathFor(active.IniPath)), "foreground cleanup confirmed");
                    if (twice) Check(!ProcessAlive(int.Parse(File.ReadAllText(Path.Combine(backendDirectory, "child.pid")))), "double Ctrl+C terminates router children");
                }
                finally
                {
                    if (ProcessAlive(controller.Pid))
                    {
                        using var p = Process.GetProcessById(controller.Pid); p.Kill(true); p.WaitForExit();
                    }
                }
            }
            void Send(WindowsServerProcess controller, Guid controllerId)
            {
                var start = new ProcessStartInfo(fixtureExecutable) { UseShellExecute = false, CreateNoWindow = true };
                foreach (var arg in new[] { "--runtime-console-signal", controller.Pid.ToString(), controller.Created.ToString(), controller.Executable, controllerId.ToString("D") }) start.ArgumentList.Add(arg);
                using var sender = Process.Start(start)!;
                if (!sender.WaitForExit(3000)) { sender.Kill(); throw new Exception("signal helper timeout"); }
                Check(sender.ExitCode == 0, "console event delivered");
            }
        }
        finally
        {
            foreach (var instance in live)
            {
                try
                {
                    WindowsServerProcess.Force(instance);
                    Wait(() => !ProcessAlive(instance.Pid), 3000);
                }
                catch (ConfigurationException) { }
            }
            // Terminating a Windows job can release child image mappings after
            // its root process handle signals exit. Allow bounded cleanup retry.
            for (var attempt = 0; Directory.Exists(root); attempt++)
            {
                try { Directory.Delete(root, true); }
                catch (Exception e) when (attempt < 30 && e is IOException or UnauthorizedAccessException) { Thread.Sleep(100); }
            }
        }

        InstanceDocument? Read() => store.Read(DocumentKind.Instance, json.InstanceDocument, ContractValidation.Instance, server: "default");
        string Log(InstanceDocument instance) => ReadText(home.PathFor(instance.LogPath));
        void Behavior(string value) => File.WriteAllText(Path.Combine(backendDirectory, "behavior.txt"), value);
        (int Code, string Out, string Error) Cli(string[] arguments)
        {
            var start = new ProcessStartInfo(product) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            start.Environment["COQUENA_HOME"] = root; foreach (var arg in arguments) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(15000)) { process.Kill(true); throw new Exception("runtime CLI timeout"); }
            return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
        }
        void Check(bool condition, string message) { checks++; if (!condition) throw new Exception("Runtime test: " + message); }
        void Reject(Action action, string message)
        {
            try { action(); } catch (ConfigurationException) { checks++; return; }
            throw new Exception("Expected runtime rejection: " + message);
        }
    }
    private static string ReadText(string path)
    {
        if (!File.Exists(path)) return "";
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream); return reader.ReadToEnd();
    }
    private static bool ProcessAlive(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
        catch (ArgumentException) { return false; }
    }
    private static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start(); return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
    private static void Wait(Func<bool> condition, int milliseconds = 5000)
    {
        var watch = Stopwatch.StartNew();
        while (!condition()) { if (watch.ElapsedMilliseconds > milliseconds) throw new Exception("Runtime fixture timeout"); Thread.Sleep(25); }
    }
}
