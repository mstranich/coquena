using System.Diagnostics;
using Coquena.Backends;
using Coquena.Configuration;
using Coquena.Contracts;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class ExternalBackendTests
{
    public static async Task Run(string? executable)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-external-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var home = new Home(Path.Combine(root, "home")); var store = new LocalStore(home);
        var json = ContractJsonContext.Default; var checks = 0;
        void Check(bool value, string why) { checks++; if (!value) throw new Exception("External backend: " + why); }
        async Task Reject(Func<Task> action)
        {
            var before = File.ReadAllText(home.PathFor("catalogs/backends.json"));
            try { await action(); throw new Exception("Unexpected acceptance"); }
            catch (Exception e) when (e is BackendException or ContractException or IOException or Coquena.Downloads.DownloadException or OperationCanceledException)
            { Check(File.ReadAllText(home.PathFor("catalogs/backends.json")) == before, "failure preserves catalog"); }
        }
        try
        {
            var file = Path.Combine(root, "binary.exe"); File.WriteAllText(file, "untouched");
            var installer = new BackendInstaller(home, probe: (_, _) => Task.FromResult("version: 1 (abcdef)"));
            var managedId = Guid.NewGuid();
            var managed = new BackendRecord(managedId, "b1", "version: 1 (abcdef)", "ggml", "ggml-org/llama.cpp", "cpu", "cpu", "win-x64", $"backends/{managedId:D}/{Guid.NewGuid():D}/fixture.exe", true);
            Directory.CreateDirectory(Path.GetDirectoryName(home.PathFor(managed.ServerPath))!); File.WriteAllText(home.PathFor(managed.ServerPath), "managed");
            store.Update(DocumentKind.Backends, json.BackendsDocument, ContractValidation.Backends, _ => new(1, [managed]));
            installer.Use("cpu:latest");
            var external = await installer.RegisterAsync(file, "Local", "cpu");
            Check(external.Ownership == "external" && external.Repository == "ggml-org/llama.cpp" && external.Build == "b1", "identity and implicit distribution");
            Check(File.ReadAllText(file) == "untouched" && !Directory.Exists(home.PathFor("backends/" + external.Id)), "no file copy");
            Check(installer.Use(null)?.Kind == "latest", "registration preserves selection");
            Check(installer.Resolve("LOCAL").BackendId == external.Id && installer.Resolve("cpu:b1").BackendId == managed.Id, "case insensitive name separated from selectors");
            Check(installer.Resolve("oficial/cpu:latest").Kind == "latest", "external excluded from latest");
            installer.Use("local"); Check(installer.List().Single(b => b.Id == external.Id).Pinned == false, "external never pinned");
            var manager = new Coquena.Servers.ServerManager(home); manager.EnsureInitial(); manager.Use(null, "LOCAL");
            Check(manager.View(null).Backend?.Id == external.Id, "server resolves external");
            await Reject(() => installer.RegisterAsync(file, "local", "cpu"));
            foreach (var name in new[] { "default", "DEFAULT", "a/b", "a:b", "", " x" })
                await Reject(() => installer.RegisterAsync(file, name, "cpu"));
            await Reject(() => installer.RegisterAsync(file, "other", "cuda"));
            await Reject(() => installer.RegisterAsync(file, "other", "cpu", "invalid"));
            await Reject(() => installer.RegisterAsync(file + ".missing", "other", "cpu"));
            await Reject(() => new BackendInstaller(home, probe: (_, _) => Task.FromResult("not a version")).RegisterAsync(file, "other", "cpu"));
            await Reject(() => new BackendInstaller(home, probe: (_, _) => Task.FromResult("version: 1 (abcdef)\nversion: 2 (abcdef)")).RegisterAsync(file, "other", "cpu"));
            var faultStore = new LocalStore(home, stage => { if (stage == WriteStage.BeforeReplace) throw new IOException("fixture"); });
            await Reject(() => new BackendInstaller(home, faultStore, (_, _) => Task.FromResult("version: 1 (abcdef)")).RegisterAsync(file, "other", "cpu"));
            Check(installer.Use(null)?.BackendId == external.Id, "failed registration preserves default");
            var custom = await installer.RegisterAsync(file, "custom", "vulkan", "lab/fork");
            Check(custom.Repository == "lab/fork" && !ParameterManager.OfficialBackend(custom), "declared distribution used for parameters");
            var rocm = await installer.RegisterAsync(file, "rocm-local", "rocm");
            Check(rocm.Family == "rocm" && !ParameterManager.OfficialBackend(rocm), "external family declaration does not enable official downloads");
            Check(new BackendDirectoryManager(home).Change("relocated", TextWriter.Null, () => "s", () => true), "managed directory transfer succeeds with external records");
            Check(installer.List().Single(b => b.Id == external.Id) == external && File.ReadAllText(file) == "untouched", "transfer preserves official external path and bytes");
            var operation = installer.Create(new("b1", "cpu", "win-x64", "compatible", "fixture", [new("test.zip", 1, "https://example.invalid/test")], true), "b1");
            Check(operation.BackendInstall?.BackendId == managed.Id, "reinstall excludes external with same release");

            if (executable is not null)
            {
                var nativeHome = new Home(Path.Combine(root, "native"));
                (int Code, string Out, string Error) Run(params string[] args)
                {
                    var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                    var env = start.Environment.ToArray(); start.Environment.Clear();
                    foreach (var group in env.GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)) start.Environment[group.Key] = group.Last().Value;
                    start.Environment["COQUENA_HOME"] = nativeHome.Root;
                    foreach (var arg in args) start.ArgumentList.Add(arg);
                    using var process = Process.Start(start)!;
                    var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(25000)) { process.Kill(true); throw new Exception("Native timeout"); }
                    return (process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
                }
                var fixture = Path.Combine(AppContext.BaseDirectory, "Coquena.Tests.exe");
                Check(Run("backend", "register", fixture, "--name", "native", "--family", "cpu").Code == 0, "native real version probe and register");
                Check(Run("backend", "list").Out.Contains(fixture), "native persisted listing");
                Check(Run("backend", "use", "NATIVE").Code == 0, "native selection");
                Check(Run("backend", "register", fixture, "--name", "Native", "--family", "cpu").Code == 1, "native duplicate rejected");
                Check(Run("backend", "use", "cpu:b1").Code == 1, "native external excluded from selector");
                Check(Run("backend", "register", fixture, "--name", "x", "--name", "y", "--family", "cpu").Code == 2, "duplicate options rejected");
            }
            Console.WriteLine($"OK: {checks} comprobaciones de backends externos.");
        }
        finally { Directory.Delete(root, true); }
    }
}
