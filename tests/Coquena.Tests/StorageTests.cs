using System.Diagnostics;
using System.Text.Json;
using Coquena.Contracts;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class StorageTests
{
    private static readonly ContractJsonContext Json = ContractJsonContext.Default;
    public static void Worker(string root)
    {
        var store = new LocalStore(new Home(root));
        for (var i = 0; i < 20; i++)
            store.Update(DocumentKind.Config, Json.ConfigDocument, ContractValidation.Config,
                old => old! with { BackendRetention = new(old.BackendRetention.MaxBuilds + 1) });
    }
    public static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-storage-" + Guid.NewGuid().ToString("N"));
        var home = new Home(root);
        try
        {
            Require(Home.Resolve(null, root).Root == Path.Combine(root, ".coquena"));
            Require(Home.Resolve(root, "invalid-profile").Root == root);
            Fail(() => Home.Resolve("relative", root), "invalid-home");
            Fail(() => Home.Resolve("", root), "invalid-home");
            Fail(() => Home.Resolve(null, null), "invalid-home");
            foreach (var path in new[] { "../escape", "a/../b", "C:\\outside", "/outside", "a:stream", "CON.json", "LPT1", "trailing.", "a//b" })
                Fail(() => home.PathFor(path), "unsafe-path");
            Require(!Directory.Exists(root));
            Require(new Home(Path.GetPathRoot(root)!).PathFor("coquena-test").EndsWith("coquena-test"));
            var store = new LocalStore(home);
            Require(Read(store) is null);
            Require(!Directory.Exists(root));
            Save(store, _ => new(1, null, null, new(5)));
            foreach (var area in new[] { "catalogs", "run", "logs", "downloads", "models", "backends" })
                Require(Directory.Exists(home.PathFor(area)));
            Require(File.ReadAllText(home.PathFor("config.json")).Contains("\n  \"schemaVersion\""));
            Parallel.Invoke(() => Worker(root), () => Worker(root));
            Require(Read(store)!.BackendRetention.MaxBuilds == 45);
            // Real independent processes, not merely two tasks sharing an address space.
            using (var first = StartWorker(root))
            using (var second = StartWorker(root))
            {
                Require(first.WaitForExit(15000) && second.WaitForExit(15000));
                Require(first.ExitCode == 0 && second.ExitCode == 0);
            }
            Require(Read(store)!.BackendRetention.MaxBuilds == 85);
            using (var lease = new FileStream(home.PathFor(".writer.lock"), FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Fail(() => Save(store, x => x!), "writer-busy");
            using (var cancelled = new CancellationTokenSource())
            {
                cancelled.Cancel();
                try { store.Update(DocumentKind.Config, Json.ConfigDocument, ContractValidation.Config, x => x!, cancellation: cancelled.Token); }
                catch (OperationCanceledException) { goto Cancelled; }
                throw new Exception("Cancelación ignorada.");
            }
            Cancelled:
            var original = File.ReadAllText(home.PathFor("config.json"));
            var before = new LocalStore(home, stage => { if (stage == WriteStage.BeforeReplace) throw new IOException("fixture"); });
            try { Save(before, x => x! with { BackendRetention = new(99) }); }
            catch (IOException) { }
            Require(File.ReadAllText(home.PathFor("config.json")) == original);
            var after = new LocalStore(home, stage => { if (stage == WriteStage.AfterReplace) throw new IOException("fixture"); });
            Fail(() => Save(after, x => x! with { BackendRetention = new(99) }), "published-incomplete-audit", true);
            Require(Read(store)!.BackendRetention.MaxBuilds == 99);
            var manual = new LocalStore(home, stage =>
            {
                if (stage == WriteStage.BeforeReplace) File.WriteAllText(home.PathFor("config.json"), original);
            });
            Fail(() => Save(manual, x => x! with { BackendRetention = new(100) }), "stale-document");
            Require(File.ReadAllText(home.PathFor("config.json")) == original);
            File.WriteAllText(home.PathFor("config.json"), "{");
            Corrupt(() => Read(store)); Corrupt(() => Save(store, _ => new(1, null, null, new(5))));
            Require(File.ReadAllText(home.PathFor("config.json")) == "{");
            File.WriteAllText(home.PathFor("config.json"), original.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 77"));
            Corrupt(() => Read(store), "unknown-schema");
            File.WriteAllText(home.PathFor("config.json"), original);
            File.WriteAllBytes(home.PathFor("config.json"), [0xff]);
            Fail(() => Read(store), "invalid-document");
            File.WriteAllText(home.PathFor("config.json"), original);
            const int journalLimit = 262144;
            var lineSeed = File.ReadLines(home.PathFor("logs/transactions.jsonl")).First() + "\n";
            File.WriteAllText(home.PathFor("logs/transactions.jsonl"),
                string.Concat(Enumerable.Repeat(lineSeed, journalLimit / System.Text.Encoding.UTF8.GetByteCount(lineSeed))));
            for (var i = 0; i < 40; i++) Save(store, x => x!);
            foreach (var journal in new[] { "logs/transactions.jsonl", "logs/transactions.1.jsonl" })
            {
                Require(new FileInfo(home.PathFor(journal)).Length <= journalLimit);
                foreach (var line in File.ReadLines(home.PathFor(journal)))
                {
                    using var parsed = JsonDocument.Parse(line);
                    Require(parsed.RootElement.EnumerateObject().Count() == 4);
                    Require(!line.Contains(root) && !line.Contains("http"));
                }
            }
            // Audit failure is reported as post-publication, never as rollback.
            Directory.CreateDirectory(home.PathFor("logs/transactions.jsonl.block"));
            File.Delete(home.PathFor("logs/transactions.jsonl"));
            Directory.Move(home.PathFor("logs/transactions.jsonl.block"), home.PathFor("logs/transactions.jsonl"));
            Fail(() => Save(store, x => x! with { BackendRetention = new(101) }), "published-incomplete-audit", true);
            Require(Read(store)!.BackendRetention.MaxBuilds == 101);
            Directory.Delete(home.PathFor("logs/transactions.jsonl"));
            File.WriteAllText(home.PathFor("logs/transactions.jsonl"), "{\"partial\":");
            Fail(() => Save(store, x => x! with { BackendRetention = new(102) }), "published-incomplete-audit", true);
            Require(Read(store)!.BackendRetention.MaxBuilds == 102);
            Require(File.ReadAllText(home.PathFor("logs/transactions.jsonl")) == "{\"partial\":");
            File.Delete(home.PathFor("logs/transactions.jsonl"));
            store.Update(DocumentKind.Servers, Json.ServersDocument, ContractValidation.Servers, _ => new(1, []));
            var serversJson = File.ReadAllText(home.PathFor("catalogs/servers.json"));
            File.WriteAllText(home.PathFor("catalogs/servers.json"), "{bad");
            Corrupt(() => store.Read(DocumentKind.Servers, Json.ServersDocument, ContractValidation.Servers));
            Corrupt(() => store.Update(DocumentKind.Servers, Json.ServersDocument, ContractValidation.Servers, _ => new(1, [])));
            Require(File.ReadAllText(home.PathFor("catalogs/servers.json")) == "{bad");
            File.WriteAllText(home.PathFor("catalogs/servers.json"), serversJson);
            store.Update(DocumentKind.Models, Json.ModelsDocument, ContractValidation.Models, _ => new(1, []));
            store.Update(DocumentKind.Backends, Json.BackendsDocument, ContractValidation.Backends, _ => new(1, []));
            store.Update(DocumentKind.Presets, Json.PresetsDocument, ContractValidation.Presets, _ => new(1, []));
            Require(store.Read(DocumentKind.Servers, Json.ServersDocument, ContractValidation.Servers)!.Servers.Length == 0);
            var id = Guid.NewGuid();
            var instance = new InstanceDocument(1, id, Guid.NewGuid(), Guid.NewGuid(), "fixture", 123, "detached", "127.0.0.1", 8080,
                true, [Guid.NewGuid()], [Guid.NewGuid()], "run/fixture/fixture.ini", "logs/fixture/fixture.log");
            store.Update(DocumentKind.Instance, Json.InstanceDocument, ContractValidation.Instance, _ => instance, server: "fixture");
            Require(ContractJson.Write(store.Read(DocumentKind.Instance, Json.InstanceDocument, ContractValidation.Instance, server: "fixture")!, Json.InstanceDocument)
                == ContractJson.Write(instance, Json.InstanceDocument));
            var selection = new RemoteSelection("fixture/tiny", "main", "pinned", [new("tiny.gguf", 1)]);
            var operation = new OperationDocument(1, id, "model", "fixture", selection, null,
                [new("tiny.gguf", $"downloads/{id:D}/files/tiny.part", "models/fixture/tiny/tiny.gguf", 0, 1, null, null, null, "pending")],
                "pending", "transfer", DateTimeOffset.UtcNow, null);
            store.Update(DocumentKind.Operation, Json.OperationDocument, ContractValidation.Operation, _ => operation, operation: id);
            Require(File.Exists(home.PathFor($"downloads/{id:D}/operation.json")));
            Require(Directory.Exists(home.PathFor($"downloads/{id:D}/files")));
            Fail(() => store.Update(DocumentKind.Operation, Json.OperationDocument, ContractValidation.Operation,
                _ => operation, operation: Guid.NewGuid()), "operation-id-mismatch");
            Fail(() => store.Read(DocumentKind.Instance, Json.InstanceDocument, ContractValidation.Instance, server: "../x"), "unsafe-path");
            Require(Directory.GetFiles(root, "*.tmp", SearchOption.AllDirectories).Length == 0);
            Console.WriteLine("OK: home, persistencia, escritores concurrentes/procesos, corrupción, fallos y JSONL limitado.");
        }
        finally
        {
            Require(Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                && Path.GetFileName(root).StartsWith("coquena-storage-", StringComparison.Ordinal));
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
    private static Process StartWorker(string root)
    {
        var exe = Environment.ProcessPath!;
        var start = new ProcessStartInfo(exe) { UseShellExecute = false };
        start.ArgumentList.Add("--storage-worker"); start.ArgumentList.Add(root);
        return Process.Start(start)!;
    }
    private static ConfigDocument? Read(LocalStore store) => store.Read(DocumentKind.Config, Json.ConfigDocument, ContractValidation.Config);
    private static void Save(LocalStore store, Func<ConfigDocument?, ConfigDocument> change)
        => store.Update(DocumentKind.Config, Json.ConfigDocument, ContractValidation.Config, change);
    private static void Require(bool result) { if (!result) throw new Exception("Falló prueba de persistencia."); }
    private static void Fail(Action run, string code, bool published = false)
    {
        try { run(); } catch (StorageException error) when (error.Code == code && error.Published == published) { return; }
        throw new Exception($"Falta diagnóstico {code}.");
    }
    private static void Corrupt(Action run, string code = "invalid-document")
    {
        try { run(); } catch (ContractException error) when (error.Code == code) { return; }
        throw new Exception($"Falta diagnóstico {code}.");
    }
}
