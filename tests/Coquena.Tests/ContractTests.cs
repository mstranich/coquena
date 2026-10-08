using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Coquena.Contracts;

namespace Coquena.Tests;

internal static class ContractTests
{
    public static void Run()
    {
        var context = ContractJsonContext.Default;
        var id = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var backendId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var modelId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var presetId = Guid.Parse("44444444-4444-4444-4444-444444444444");
        var backend = new BackendRecord(backendId, "b1234", "version-fixture", "ggml",
            "https://github.com/ggml-org/llama.cpp", "cpu", "cpu", "win-x64", "backends/b1234/llama-server.exe");
        Round(backend, context.BackendRecord, ContractValidation.Backend);
        var latest = new BackendSelection("latest", Origin: "ggml", Variant: "cpu");
        var fixedSelection = new BackendSelection("fixed", backendId);
        Round(new ConfigDocument(1, id, latest, new(5)), context.ConfigDocument, ContractValidation.Config);
        var single = new RemoteSelection("fixture/tiny", "main", new string('a', 40), [new("tiny.gguf", 16, new("sha256", "file-bytes", new string('a', 64)))]);
        var split = single with { Files = [new("tiny-00001-of-00002.gguf", 8), new("tiny-00002-of-00002.gguf", 8)] };
        var model = new ModelRecord(modelId, "fixture/tiny:Q4_K_M", split,
            [new(split.Files[0].Path, "models/fixture/tiny/" + split.Files[0].Path, 8),
             new(split.Files[1].Path, "models/fixture/tiny/" + split.Files[1].Path, 8)], []);
        Round(model, context.ModelRecord, ContractValidation.Model);
        Round(model with { Selection = single, Files = [new("tiny.gguf", "models/fixture/tiny/tiny.gguf", 16)] },
            context.ModelRecord, ContractValidation.Model);
        Round(new PresetRecord(presetId, "chat", modelId, new() { ["ctx-size"] = "4096" }),
            context.PresetRecord, ContractValidation.Preset);
        var servers = new ServersDocument(1,
            [new(id, "default", null, [], new() { ["port"] = Element("8080") }),
             new(backendId, "lts", fixedSelection, [presetId], new() { ["port"] = Element("8081"), ["metrics"] = Element("false") })]);
        var json = Round(servers, context.ServersDocument, ContractValidation.Servers);
        if (!json.Contains("\"backend\": null") || json.Contains("\"host\"")) throw new Exception("Herencia/defaults duplicados.");
        var latestJson = ContractJson.Write(new ConfigDocument(1, id, latest, new(5)), context.ConfigDocument);
        if (latestJson.Contains("backendId")) throw new Exception("latest incluye ejecución concreta.");
        var instance = new InstanceDocument(1, id, backendId, backendId, "b1234", 12345,
            "detached", "127.0.0.1", 8081, false, [presetId], [modelId], "run/lts/1.ini", "logs/lts/1.log");
        Round(instance, context.InstanceDocument, ContractValidation.Instance);
        var operation = new OperationDocument(1, id, "model", model.Name, split, null,
            split.Files.Select(f => new TransferRecord(f.Path, "downloads/1/files/" + f.Path + ".part",
                "models/fixture/tiny/" + f.Path, 4, f.Size, null, "\"transport-tag\"", null, "paused")).ToArray(),
            "paused", "transfer", DateTimeOffset.Parse("2026-10-07T12:00:00Z"), "Interrupción de fixture");
        Round(operation, context.OperationDocument, ContractValidation.Operation);
        var artifacts = new RemoteFile[] { new("backend.zip", 16), new("libraries.zip", null) };
        var backendOperation = operation with { ResourceKind = "backend", ModelSelection = null,
            BackendSelection = new("ggml", "b1234", "cpu", "cpu", "win-x64", artifacts),
            Files = artifacts.Select(f => new TransferRecord(f.Path, "downloads/1/files/" + f.Path + ".part",
                "backends/b1234/" + f.Path, 0, f.Size, null, null, null, "pending")).ToArray() };
        Round(backendOperation, context.OperationDocument, ContractValidation.Operation);
        // A changed display name cannot rewrite stable references; unknown IDs remain broken.
        Round(servers with { Servers = [servers.Servers[1] with { Name = "renamed", PresetIds = [Guid.NewGuid()] }] },
            context.ServersDocument, ContractValidation.Servers);
        Invalid(json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 999"), "unknown-schema");
        Invalid(json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": \"1\""));
        Invalid(json.Replace("\"schemaVersion\": 1,", ""));
        Invalid(json.Replace("\"servers\": [", "\"servers\": null, \"ignored\": ["));
        Invalid(json.Replace("8080", "65536"));
        Invalid(json.Replace("8080", "0"));
        Invalid(json.Replace("8081", "8080"));
        Invalid(json.Replace("\"metrics\": false", "\"metrics\": \"false\""));
        Invalid(json.Replace("\"kind\": \"fixed\"", "\"kind\": \"unknown\""));
        Invalid(json.Replace("\"schemaVersion\": 1", "\"schemaVersion\": 1, \"schemaVersion\": 1"));
        Invalid(json.Replace("\"name\": \"lts\"", "\"name\": \"DEFAULT\""));
        Invalid(json.Replace("\"servers\": [", "\"extra\": true, \"servers\": ["));
        Invalid("null"); Invalid("{"); Invalid("[]");
        Invalid(json.Replace("\"port\": 8080", "\"port\": null"));
        Invalid(json.Replace("\"port\": 8080", "\"port\": 8080, \"host\": []"));
        Invalid(json.Replace("\"port\": 8080", "\"other\": 8080"));
        Fails(() => Round(model with { Files = [model.Files[0]] }, context.ModelRecord, ContractValidation.Model));
        Fails(() => Round(operation with { ModelSelection = null }, context.OperationDocument, ContractValidation.Operation));
        Fails(() => Round(operation with { Files = [operation.Files[0] with { ConfirmedBytes = 9 }, operation.Files[1]] },
            context.OperationDocument, ContractValidation.Operation));
        Fails(() => Round(backendOperation with { BackendSelection = backendOperation.BackendSelection! with { Release = "latest" } },
            context.OperationDocument, ContractValidation.Operation));
        Fails(() => Round(backendOperation with { Files = [backendOperation.Files[0] with { Identity = new("sha256", "file-bytes", "abc") }, backendOperation.Files[1]] },
            context.OperationDocument, ContractValidation.Operation));
        Fails(() => Round(instance with { Mode = "unknown" }, context.InstanceDocument, ContractValidation.Instance));
        Console.WriteLine("OK: contratos M1, round-trip y diagnósticos negativos (sin E/S de proveedor).");

        void Invalid(string input, string code = "invalid-document") => Fails(() =>
            ContractJson.Read(input, context.ServersDocument, ContractValidation.Servers), code);
    }
    private static JsonElement Element(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
    private static string Round<T>(T value, JsonTypeInfo<T> type, Action<T> validate)
    {
        var json = ContractJson.Write(value, type);
        var read = ContractJson.Read(json, type, validate);
        if (ContractJson.Write(read, type) != json) throw new Exception("Round-trip inestable.");
        return json;
    }
    private static void Fails(Action action, string code = "invalid-document")
    {
        try { action(); }
        catch (ContractException error) when (error.Code == code) { return; }
        throw new Exception($"Falta diagnóstico {code}.");
    }
}
