using System.Diagnostics;
using System.Text;
using Coquena.Configuration;
using Coquena.Contracts;
using Coquena.Models;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class ExternalModelTests
{
    public static void Run(string? executable = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-external-models-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var home = new Home(Path.Combine(root, "home"));
        var installer = new ModelInstaller(home); var external = new ExternalModels(home);
        var json = ContractJsonContext.Default; var store = new LocalStore(home); var checks = 0;
        void Check(bool value, string why) { checks++; if (!value) throw new Exception("External model: " + why); }
        void Reject(Action action)
        {
            var before = File.ReadAllText(home.PathFor("catalogs/models.json"));
            try { action(); throw new Exception("Unexpected acceptance"); }
            catch (Exception e) when (e is ModelSourceException or ConfigurationException)
            { Check(File.ReadAllText(home.PathFor("catalogs/models.json")) == before, "rejection preserves catalog"); }
        }
        var backend = new BackendRecord(Guid.NewGuid(), "b11482", "v", "ggml", "ggml-org/llama.cpp", "cpu", "cpu", "win-x64", "fixture.exe");
        try
        {
            var file = Path.Combine(root, "arbitrary.gguf"); var bytes = Fixture();
            File.WriteAllBytes(file, bytes); var date = File.GetLastWriteTimeUtc(file);
            var single = external.Add(file, "User/Repo:Q4_K_M");
            Check(single.Ownership == "external" && single.Selection is null && single.Files[0].Integrity is null && single.Files[0].LocalPath == file, "local ownership and absolute path without remote/integrity");
            Check(File.ReadAllBytes(file).SequenceEqual(bytes) && File.GetLastWriteTimeUtc(file) == date && Directory.GetFiles(root).Length == 1, "no external writes/copies/sidecars");
            Check(installer.List().Single() is { Availability: "available", Changed: false }, "single available");
            Reject(() => external.Add(file, "other/repo:Q4_K_M"));
            var copy = Path.Combine(root, "copy.gguf"); File.WriteAllBytes(copy, bytes);
            Reject(() => external.Add(copy, "user/repo:q4_k_m"));
            Reject(() => external.Add(copy, "user/repo"));
            Reject(() => external.Add(copy, "user/repo#mmproj:BF16"));
            Reject(() => external.Add(copy, "user/repo:BF16", true));
            var companion = external.Add(copy, "User/Repo#mmproj:BF16", true);
            Check(companion.CompanionType == "mmproj", "independent companion");
            var config = new ConfigurationManager(home);
            var preset = config.Create("user/repo:q4_k_m", "external");
            Check(preset.ModelId == single.Id && config.Compose(backend, [preset.Id]).Ini.Contains(file), "ID resolves to absolute INI path");
            Reject(() => config.Create(companion.Name, "wrong"));
            File.SetLastWriteTimeUtc(file, date.AddMinutes(2));
            var beforeList = File.ReadAllText(home.PathFor("catalogs/models.json"));
            Check(installer.List().Single(m => m.Record.Id == single.Id) is { Availability: "available", Changed: true }, "changed warning without blocking");
            Check(config.Compose(backend, [preset.Id]).Ini.Contains(file) && beforeList == File.ReadAllText(home.PathFor("catalogs/models.json")), "lookup does not refresh/rebase");
            File.Delete(file);
            Check(installer.List().Single(m => m.Record.Id == single.Id).Availability == "inaccessible", "external removal diagnosed");
            Reject(() => config.Compose(backend, [preset.Id]));
            File.WriteAllBytes(file, bytes);
            var first = Path.Combine(root, "model-00001-of-00002.gguf");
            var second = Path.Combine(root, "model-00002-of-00002.gguf");
            File.WriteAllBytes(first, Fixture(0, 2));
            var shards = external.Add(first, "user/shards:Q4_K_M");
            Check(shards.Files.Length == 2 && shards.Files[1].LocalPath == second && installer.List().Single(m => m.Record.Id == shards.Id).Availability == "incomplete", "missing siblings retained");
            var splitPreset = config.Create(shards.Name, "split");
            Reject(() => config.Compose(backend, [splitPreset.Id]));
            File.WriteAllBytes(second, Fixture(1, 2));
            Check(config.Compose(backend, [splitPreset.Id]).Ini.Contains(first), "shards become servible without refresh");
            using (var locked = new FileStream(second, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Check(installer.List().Single(m => m.Record.Id == shards.Id).Availability == "inaccessible", "locked shard diagnosed");
            File.WriteAllBytes(second, Fixture(0, 2));
            Check(installer.List().Single(m => m.Record.Id == shards.Id).Availability == "invalid", "replaced contradictory header");
            Reject(() => config.Compose(backend, [splitPreset.Id]));
            Reject(() => external.Add(second, "user/second:Q4_K_M"));
            var bad = Path.Combine(root, "bad.gguf"); File.WriteAllText(bad, "not gguf");
            Reject(() => external.Add(bad, "user/bad:Q4_K_M"));
            File.WriteAllBytes(bad, Fixture(0, 2));
            Reject(() => external.Add(bad, "user/unconventional:Q4_K_M"));
            var missing = external.Add(Path.Combine(root, "missing.gguf"), "user/missing:BF16");
            Check(installer.List().Single(m => m.Record.Id == missing.Id).Availability == "inaccessible", "unavailable local reference retained");
            var lockedPath = Path.Combine(root, "locked.gguf"); File.WriteAllBytes(lockedPath, bytes);
            using (var locked = new FileStream(lockedPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var record = external.Add(lockedPath, "user/locked:BF16");
                Check(installer.List().Single(m => m.Record.Id == record.Id).Availability == "inaccessible", "locked registration retained");
            }
            var managedPath = home.PathFor("models/user/managed/managed.gguf");
            Directory.CreateDirectory(Path.GetDirectoryName(managedPath)!); File.WriteAllBytes(managedPath, bytes);
            var managed = new ModelRecord(Guid.NewGuid(), "user/managed:BF16", new("user/managed", "main", new string('b', 40), [new("managed.gguf", bytes.Length)]),
                [new("managed.gguf", "models/user/managed/managed.gguf", bytes.Length, LastWriteUtc: File.GetLastWriteTimeUtc(managedPath))], []);
            store.Update(DocumentKind.Models, json.ModelsDocument, ContractValidation.Models, old => old! with { Models = old.Models.Append(managed).ToArray() });
            Reject(() => external.Add(Path.Combine(root, "free.gguf"), managed.Name));
            Reject(() => external.Add(managedPath, "user/shared:BF16"));
            var beforeMove = ContractJson.Write(new ModelsDocument(1, installer.List().Select(m => m.Record).Where(m => m.Ownership == "external").ToArray()), json.ModelsDocument);
            Check(new ModelDirectoryManager(home).Change("relocated", TextWriter.Null, () => "s", () => true), "directory change with external catalog");
            var afterMove = ContractJson.Write(new ModelsDocument(1, installer.List().Select(m => m.Record).Where(m => m.Ownership == "external").ToArray()), json.ModelsDocument);
            Check(beforeMove == afterMove && File.Exists(file) && File.Exists(first), "directory change preserves external records/files");
            Check(!File.Exists(managedPath) && installer.List().Single(m => m.Record.Id == managed.Id).Availability == "available", "mixed catalog moves only managed files");
            using var output = new StringWriter(); using var error = new StringWriter();
            Check(ModelCli.Run(["companion", "list"], output, error, installer) == 0 && output.ToString().Contains(companion.Name) && !output.ToString().Contains(single.Name + " |"), "companion listing separate");
            output.GetStringBuilder().Clear();
            Check(ModelCli.Run(["list"], output, error, installer) == 0 && !output.ToString().Contains(companion.Name), "principal listing separate");
            Check(ModelCli.Run(["add", file, "--name", "user/new:BF16", "--try-match", "extra"], output, error, installer) == 2, "unsupported flags rejected before registration");
            // A pending managed download reserves canonical names and physical destinations.
            var pending = installer.Create(new("user/pending:BF16", null, new("user/pending", "main", new string('a', 40), [new("pending.gguf", 1)])));
            Reject(() => external.Add(Path.Combine(root, "unregistered.gguf"), pending.Name));
            Reject(() => external.Add(new ModelPaths(home).Destination(pending, pending.Files[0]), "user/physical:BF16"));
            var persisted = store.Read(DocumentKind.Models, json.ModelsDocument, ContractValidation.Models)!;
            Check(persisted.Models.Single(m => m.Id == single.Id).Name == single.Name, "ID and spelling preserved across reads");
            if (executable is not null)
            {
                var nativeHome = new Home(Path.Combine(root, "native"));
                (int Code, string Out) Native(params string[] args)
                {
                    var start = new ProcessStartInfo(executable) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
                    var env = start.Environment.ToArray(); start.Environment.Clear();
                    foreach (var group in env.GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase)) start.Environment[group.Key] = group.Last().Value;
                    start.Environment["COQUENA_HOME"] = nativeHome.Root;
                    foreach (var arg in args) start.ArgumentList.Add(arg);
                    using var process = ModelEngineProcess.Start(start)!;
                    var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(20000)) { process.Kill(true); throw new Exception("Native timeout"); }
                    return (process.ExitCode, stdout.GetAwaiter().GetResult() + stderr.GetAwaiter().GetResult());
                }
                Check(Native("model", "add", file, "--name", single.Name).Code == 0, "native add");
                Check(Native("model", "companion", "add", copy, "--name", companion.Name).Code == 0, "native companion add");
                Check(Native("model", "add", copy, "--name", "user/repo:q4_k_m").Code == 1, "native collision");
                Check(Native("model", "companion", "list").Out.Contains(companion.Name), "native persisted companion list");
                Check(Native("preset", "create", single.Name, "native").Code == 0, "native stable preset reference");
                File.Delete(file);
                Check(Native("model", "list").Out.Contains("inaccessible"), "native missing diagnosis");
                Check(Native("model", "add", bad, "--name", "user/bad:BF16").Code == 1, "native unconventional split diagnostic");
            }
            Console.WriteLine($"OK: {checks} comprobaciones de modelos externos.");
        }
        finally { Directory.Delete(root, true); }
    }

    internal static byte[] Fixture(int? index = null, int count = 1)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(0x46554747u); writer.Write(3u); writer.Write(1ul); writer.Write(index.HasValue ? 4ul : 1ul);
        void Text(string text) { var bytes = Encoding.UTF8.GetBytes(text); writer.Write((ulong)bytes.Length); writer.Write(bytes); }
        Text("general.architecture"); writer.Write(8u); Text("test");
        if (index is { } i)
        {
            Text("split.no"); writer.Write(2u); writer.Write((ushort)i);
            Text("split.count"); writer.Write(2u); writer.Write((ushort)count);
            Text("split.tensors.count"); writer.Write(5u); writer.Write(count);
        }
        return stream.ToArray();
    }
}
