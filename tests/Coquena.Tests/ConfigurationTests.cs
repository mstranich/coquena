using System.Diagnostics;
using System.Text;
using Coquena.Configuration;
using Coquena.Contracts;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class ConfigurationTests
{
    public static async Task BackendSmoke(string executable)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-ini-backend-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        Process? process = null;
        Task<string>? stdout = null, stderr = null;
        try
        {
            var model = Path.Combine(root, "fixture.gguf");
            File.WriteAllBytes(model, Fixture(null));
            var common = new Dictionary<string, string>
            {
                ["ctx-size"] = "1024", ["threads"] = "1", ["threads-batch"] = "1", ["n-predict"] = "16",
                ["n-gpu-layers"] = "0", ["temperature"] = "0.7", ["top-k"] = "40", ["top-p"] = "0.9",
                ["min-p"] = "0.05", ["device"] = "none", ["flash-attn"] = "off", ["cache-type-k"] = "f16",
                ["cache-type-v"] = "f16", ["load-on-startup"] = "false", ["stop-timeout"] = "1"
            };
            var iniPath = Path.Combine(root, "two-presets.ini");
            var ini = ConfigurationManager.Render(common,
                [("first", new() { ["model"] = model, ["ctx-size"] = "2048" }), ("second", new() { ["model"] = model })]);
            File.WriteAllText(iniPath, ini, new UTF8Encoding(false));
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start(); var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            var start = new ProcessStartInfo(Path.GetFullPath(executable))
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var argument in new[] { "--models-preset", iniPath, "--host", "127.0.0.1", "--port", port.ToString(), "--no-models-autoload" })
                start.ArgumentList.Add(argument);
            // Avoid environment-provided model/remote selection; this smoke only parses INI and lists unloaded presets.
            foreach (var key in start.Environment.Keys.Where(k => k.StartsWith("LLAMA_", StringComparison.Ordinal)).ToArray()) start.Environment.Remove(key);
            process = Process.Start(start)!;
            stdout = process.StandardOutput.ReadToEndAsync(); stderr = process.StandardError.ReadToEndAsync();
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            var watch = Stopwatch.StartNew();
            while (watch.Elapsed < TimeSpan.FromSeconds(15) && !process.HasExited)
            {
                try
                {
                    var response = await client.GetStringAsync($"http://127.0.0.1:{port}/models");
                    using var parsed = System.Text.Json.JsonDocument.Parse(response);
                    var names = parsed.RootElement.GetProperty("data").EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToArray();
                    if (!names.Contains("first") || !names.Contains("second")) throw new Exception("Router did not expose both presets: " + response);
                    Console.WriteLine("OK: llama-server b11482 aceptó INI generado con 15 parámetros de modelo, dos presets y carga desactivada; /models confirmó ambos nombres.");
                    return;
                }
                catch (Exception error) when (error is HttpRequestException or TaskCanceledException) { await Task.Delay(100); }
            }
            throw new Exception("llama-server did not accept presets within 15 seconds.");
        }
        catch
        {
            if (process is not null && !process.HasExited) { process.Kill(true); process.WaitForExit(); }
            if (stdout is not null) Console.Error.WriteLine(await stdout);
            if (stderr is not null) Console.Error.WriteLine(await stderr);
            throw;
        }
        finally
        {
            if (process is not null)
            {
                if (!process.HasExited) { process.Kill(true); process.WaitForExit(); }
                process.Dispose();
            }
            Directory.Delete(root, true);
        }
    }

    public static void Run(string? executable = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-configuration-" + Guid.NewGuid());
        var home = new Home(root);
        var store = new LocalStore(home);
        var manager = new ConfigurationManager(home);
        var checks = 0;
        try
        {
            var first = Model("fixture/first:Q4", ["part-00001-of-00002.gguf", "part-00002-of-00002.gguf"]);
            var second = Model("fixture/second:Q8", ["single.gguf"]);
            foreach (var model in new[] { first, second })
                for (var i = 0; i < model.Files.Length; i++)
                {
                    var path = home.PathFor(model.Files[i].LocalPath);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    File.WriteAllBytes(path, Fixture(model.Files.Length == 1 ? null : i));
                }
            store.Update(DocumentKind.Models, ContractJsonContext.Default.ModelsDocument, ContractValidation.Models, _ => new(1, [first, second]));
            var backend = new BackendRecord(Guid.NewGuid(), "b11482", "fixture", "ggml", "ggml-org/llama.cpp", "cuda@13", "cuda", "win-x64", "backends/fixture/server.exe");
            store.Update(DocumentKind.Backends, ContractJsonContext.Default.BackendsDocument, ContractValidation.Backends, _ => new(1, [backend]));
            store.Update(DocumentKind.Config, ContractJsonContext.Default.ConfigDocument, ContractValidation.Config,
                _ => new(1, Guid.NewGuid(), new("fixed", backend.Id), new(7)));
            var chat = manager.Create(first.Name, "chat");
            var coding = manager.Create(second.Name, "coding");
            Reject(() => manager.Create(second.Name, "CHAT"), "duplicate case-insensitive name");
            Reject(() => manager.Create(first.Name, "*"), "reserved section");
            Reject(() => manager.Create(first.Name, "default"), "upstream reserved section");
            Reject(() => manager.Create(first.Name, "chat:Q4"), "upstream colon normalization");
            manager.Set("chat", "c", "8192");
            manager.Set("chat", "temp", "0.7");
            manager.Set("coding", "load-on-startup", "false");
            var sameModel = manager.Create(first.Name, "same-model");
            manager.Set("same-model", "c", "2048");
            Check(manager.View("chat").Effective["ctx-size"] == "8192" && manager.View("same-model").Effective["ctx-size"] == "2048", "presets of same model independent");
            manager.Set("CHAT", "LLAMA_ARG_CTX_SIZE", null);
            Check(!manager.View("chat").Effective.ContainsKey("ctx-size"), "unset removes assignment without inheritance");
            // Model-level values are never incorporated, even if supplied in stored metadata.
            store.Update(DocumentKind.Models, ContractJsonContext.Default.ModelsDocument, ContractValidation.Models,
                _ => new(1, [first with { Parameters = new() { ["ctx-size"] = "9999" } }, second]));
            var ini = manager.Compose(backend, [chat.Id, coding.Id]);
            Check(ini.Ini.StartsWith("version = 1\n\n[*]\n\n") && !ini.Ini.Contains("9999"), "no shared parameters in composition");
            Check(ini.Ini.Contains("[chat]\nmodel = " + home.PathFor(first.Files[0].LocalPath)), "first shard absolute reference");
            Check(ini.Ini.Contains("[coding]\nload-on-startup = false\nmodel = " + home.PathFor(second.Files[0].LocalPath)), "explicit second preset");
            Check(ini.Ini.Contains("temperature = 0.7") && !ini.Ini.Contains("temp ="), "canonical key");
            Check(ini.ResourceIds.SequenceEqual(new[] { first.Id, second.Id }), "both resources");
            Check(ini.Ini == manager.Compose(backend, [chat.Id, coding.Id]).Ini, "deterministic output");
            foreach (var key in new[] { "port", "--host", "LLAMA_ARG_ENDPOINT_METRICS", "models-max", "no-models-autoload", "models-preset", "model", "m" })
                Reject(() => manager.Set("chat", key, "1"), "scope/resource boundary " + key);
            manager.Set("chat", "future-option", "on");
            var unknown = manager.Compose(backend, [chat.Id]);
            Check(unknown.Ini.Contains("future-option = on") && unknown.Diagnostics.Single().Contains("desconocido"), "unknown kept with warning");
            Check(ParameterCatalog.Availability("ctx-size", backend) == "admitido" &&
                ParameterCatalog.Availability("ctx-size", backend with { Build = "b11483" }) == "desconocido" &&
                ParameterCatalog.Availability("ctx-size", backend with { Repository = "other/repo" }) == "desconocido", "evidence bound to exact release/origin");
            manager.Set("chat", "future-option", null);
            foreach (var value in new[] { "x\n[other]", "x#truncated", "x;truncated", " trailing ", "x\0" })
                Reject(() => manager.Set("chat", "temperature", value), "INI round-trip guard");
            Reject(() => manager.Set("chat", "bad=key", "x"), "key injection");
            Reject(() => manager.Compose(backend, []), "empty explicit list");
            Reject(() => manager.Compose(backend, [chat.Id, chat.Id]), "duplicate selected preset");
            Reject(() => manager.Compose(backend, [Guid.NewGuid()]), "broken preset ID");
            var saved = File.ReadAllText(home.PathFor("config.json"));
            Check(saved.Contains("\"maxBuilds\": 7") && saved.Contains(backend.Id.ToString()), "config edits preserve unrelated defaults");
            Check(!File.ReadAllText(home.PathFor("catalogs/presets.json")).Contains("LocalPath") &&
                File.ReadAllText(home.PathFor("catalogs/presets.json")).Contains(first.Id.ToString()), "stable model ID stored without paths");
            Parallel.For(0, 8, i => new ConfigurationManager(home).Set("chat", "concurrent-" + i, i.ToString()));
            Check(manager.View("chat").Own.Keys.Count(k => k.StartsWith("concurrent-", StringComparison.Ordinal)) == 8, "concurrent writers preserve all updates");
            for (var i = 0; i < 8; i++) manager.Set("chat", "concurrent-" + i, null);
            var beforeFailure = File.ReadAllText(home.PathFor("catalogs/presets.json"));
            var failingStore = new LocalStore(home, stage => { if (stage == WriteStage.BeforeReplace) throw new IOException("fixture"); });
            try { new ConfigurationManager(home, failingStore).Set("chat", "temp", "0.2"); throw new Exception("expected write failure"); }
            catch (IOException) { checks++; }
            Check(File.ReadAllText(home.PathFor("catalogs/presets.json")) == beforeFailure, "failed preset write preserves assignments and model references");
            manager.ChangeModel("chat", second.Name);
            Check(manager.View("chat").Preset.Id == chat.Id && manager.View("chat").Model!.Id == second.Id &&
                manager.View("chat").Own["temperature"] == "0.7", "rebind retains preset identity and own values");
            manager.ChangeModel("chat", first.Name);
            store.Update(DocumentKind.Models, ContractJsonContext.Default.ModelsDocument, ContractValidation.Models,
                _ => new(1, [first with { Id = Guid.NewGuid() }, second]));
            Check(manager.View("chat").Diagnostics.Single().Contains("referencia rota"), "same name cannot repair removed ID");
            Reject(() => manager.Compose(backend, [chat.Id, coding.Id]), "broken model rejects entire composition");
            store.Update(DocumentKind.Models, ContractJsonContext.Default.ModelsDocument, ContractValidation.Models, _ => new(1, [first, second]));
            File.Delete(home.PathFor(first.Files[1].LocalPath));
            Reject(() => manager.Compose(backend, [chat.Id, coding.Id]), "missing shard rejects whole INI");
            File.WriteAllBytes(home.PathFor(first.Files[1].LocalPath), Fixture(1));
            using (var locked = new FileStream(home.PathFor(second.Files[0].LocalPath), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Reject(() => manager.Compose(backend, [chat.Id, coding.Id]), "inaccessible selected file");
            File.WriteAllBytes(home.PathFor(second.Files[0].LocalPath), [0]);
            Reject(() => manager.Compose(backend, [chat.Id, coding.Id]), "invalid selected file");
            File.WriteAllBytes(home.PathFor(second.Files[0].LocalPath), Fixture(null));
            manager.Remove("coding");
            var replacement = manager.Create(second.Name, "coding");
            Check(replacement.Id != coding.Id, "recreate never reuses ID");
            Reject(() => manager.Compose(backend, [coding.Id]), "old references stay broken");

            // Every documented operation exercised via managed CLI or published executable,
            // with a fresh process for each call to prove source-generated persistence.
            Cli("preset", "create", first.Name, "native-chat");
            Cli("preset", "create", second.Name, "native-code");
            Cli("preset", "set", "native-chat", "c", "2048");
            Check(Cli("preset", "show", "native-chat").Contains("ctx-size = 2048 | propio"), "CLI own value");
            Cli("preset", "unset", "native-chat", "c");
            Check(!Cli("preset", "show", "native-chat").Contains("ctx-size ="), "CLI persisted unset has no inheritance");
            BadCli("config", "show");
            BadCli("config", "unset", "c", "--preset", "native-chat");
            BadCli("preset", "set", "native-chat", "temperature", "--ref", "missing");
            Cli("preset", "model", "set", "native-code", first.Name);
            Check(Cli("preset", "list", "--model", first.Name).Contains("native-code"), "CLI model filter and rebind");
            Check(Cli("param", "show", "ngl", "--backend", backend.Id.ToString()).Contains("n-gpu-layers") &&
                Cli("param", "show", "c", "--version", "b11482").Contains("admitido"), "CLI catalog aliases and selectors");
            Check(Cli("param", "show", "future-option").Contains("desconocido"), "CLI unknown definition");
            BadCli("config", "set", "port", "8080");
            BadCli("config", "set", "temperature", "--ref", "missing");
            BadCli("config", "show", "--model", first.Name, "--preset", "native-chat");
            BadCli("param", "list", "--backend", backend.Id.ToString(), "--version", "b11482");
            BadCli("preset", "clone", "chat", "clone");
            Cli("preset", "remove", "native-code");
            var composed = new ConfigurationManager(home).Compose(backend, [manager.View("native-chat").Preset.Id, replacement.Id]);
            Check(composed.Ini.Contains("[native-chat]") && composed.Ini.Contains("[coding]"), "CLI-to-INI two explicit presets");
            store.Update(DocumentKind.Presets, ContractJsonContext.Default.PresetsDocument, ContractValidation.Presets,
                old => old! with { Presets = old.Presets.Select(p => p.Id == chat.Id ? p with { Parameters = new() { ["temperature"] = "bad#literal" } } : p).ToArray() });
            manager.Set("chat", "temp", null);
            Check(manager.View("chat").Own.Count == 0, "unset repairs stale unsupported literal");
            File.WriteAllText(home.PathFor("catalogs/presets.json"), "{broken");
            RejectContract(() => manager.Set("chat", "c", "128"));
            Check(File.ReadAllText(home.PathFor("catalogs/presets.json")) == "{broken", "corruption never replaced");
            Console.WriteLine($"OK: {checks} comprobaciones config/presets/INI" + (executable is null ? "." : " con CLI Native AOT."));
        }
        finally { Directory.Delete(root, true); }

        void Check(bool value, string name) { if (!value) throw new Exception("Configuration: " + name); checks++; }
        void Reject(Action action, string name)
        {
            try { action(); } catch (ConfigurationException) { checks++; return; }
            throw new Exception("Configuration accepted: " + name);
        }
        void RejectContract(Action action)
        {
            try { action(); } catch (ContractException) { checks++; return; }
            throw new Exception("Configuration accepted corrupt document");
        }
        string Cli(params string[] arguments) => Invoke(arguments, 0);
        void BadCli(params string[] arguments) => Invoke(arguments, 2);
        string Invoke(string[] arguments, int expected)
        {
            string stdout, stderr; int code;
            if (executable is null)
            {
                using var output = new StringWriter(); using var error = new StringWriter();
                code = ConfigurationCli.Run(arguments[0], arguments[1..], output, error, manager);
                stdout = output.ToString(); stderr = error.ToString();
            }
            else
            {
                var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                start.Environment["COQUENA_HOME"] = root;
                foreach (var argument in arguments) start.ArgumentList.Add(argument);
                using var process = Process.Start(start)!;
                var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(10000)) { process.Kill(true); throw new Exception("Configuration CLI timeout"); }
                stdout = output.GetAwaiter().GetResult(); stderr = error.GetAwaiter().GetResult(); code = process.ExitCode;
            }
            Check(code == expected && (expected == 0 ? stderr.Length == 0 : stderr.Contains("Error:")), $"CLI {string.Join(' ', arguments)}: {stdout} {stderr}");
            return stdout;
        }

        ModelRecord Model(string name, string[] filenames)
        {
            var repository = name.Split(':')[0];
            var remote = filenames.Select(f => new RemoteFile(f, null)).ToArray();
            return new(Guid.NewGuid(), name, new(repository, "main", new string('a', 40), remote),
                filenames.Select(f => new InstalledFile(f, $"models/{repository}/{f}", 0)).ToArray(), []);
        }
    }

    internal static byte[] Fixture(int? shard)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write("GGUF"u8); writer.Write(3u); writer.Write(1ul); writer.Write(shard.HasValue ? 4ul : 1ul);
        Text("general.architecture"); writer.Write(8u); Text("test");
        if (shard.HasValue)
        {
            Text("split.no"); writer.Write(2u); writer.Write((ushort)shard.Value);
            Text("split.count"); writer.Write(2u); writer.Write((ushort)2);
            Text("split.tensors.count"); writer.Write(5u); writer.Write(2);
        }
        return stream.ToArray();
        void Text(string value) { var bytes = Encoding.UTF8.GetBytes(value); writer.Write((ulong)bytes.Length); writer.Write(bytes); }
    }
}
