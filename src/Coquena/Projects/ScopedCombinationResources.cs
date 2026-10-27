using System.Text.Json.Nodes;
using Coquena.Backends;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Models;
using Coquena.Recipes;

namespace Coquena.Projects;

// Acquires only requested resources in their owning scope. The service target's
// backend is never copied into a consumer's inventory.
public sealed class ScopedCombinationResources(ScopeContext context, HardwareContext hardware,
    HuggingFaceStore source, IDownloadTransport transport, IModelSelectionInteraction interaction,
    IRemoteGgufInspector? inspector = null, bool force = false, Action<string>? warning = null,
    ScopedBackends? backends = null) : ICombinationResources
{
    private readonly ScopedModels models = new(context);
    private readonly ScopedBackends backends = backends ?? new(context);

    public async Task<PreparationResource> PrepareAsync(PreparationRequest request, JsonObject? exact, CancellationToken cancellation)
    {
        if (request.Kind == "backend")
        {
            var value = request.Intent;
            var intent = new BackendIntent(value.Get("version")?.Text, value.Get("variant")?.Text, value.Get("executable")?.Text);
            var pinned = exact?["selection"] is JsonObject backendSelection
                ? ContractJson.Read(backendSelection.ToJsonString(), ContractJsonContext.Default.BackendDownloadSelection,
                    s => ScopedBackends.CheckSelection(s, hardware, force, warning)) : null;
            var prepared = await backends.PrepareCombinationAsync(intent, hardware, transport, pinned, force, warning, cancellation);
            var installed = Installation(backends.List(), prepared.ResourceId, prepared.InstallationId);
            var resolution = new JsonObject { ["origin"] = prepared.Ownership == "managed" ? "official" : "external" };
            if (installed["selection"]?["backendSelection"] is { } selected) resolution["selection"] = selected.DeepClone();
            if (prepared.Ownership == "managed") resolution["artifacts"] = ReferenceFiles(installed);
            return new(request.Key, request.Kind, prepared.ResourceId, prepared.InstallationId, prepared.Executable, resolution);
        }
        var intentValue = request.Intent;
        ModelIntent modelIntent;
        if (request.Reference?.Canonical == true)
            modelIntent = new(request.Key, request.Kind, ModelRequest.Parse(request.Reference.Name), "main", []);
        else if (intentValue.Kind == RecipeKind.String)
            modelIntent = new(request.Key, request.Kind, ModelRequest.Parse(intentValue.Text!), "main", []);
        else
        {
            var repository = intentValue.Get("repository")?.Text;
            var type = intentValue.Get("type")?.Text;
            var quantization = intentValue.Get("quantization")?.Text;
            var parsed = repository is null ? null : ModelRequest.Parse(repository + (type is null ? "" : "#" + type) + (quantization is null ? "" : ":" + quantization));
            modelIntent = new(request.Key, request.Kind, parsed, intentValue.Get("revision")?.Text ?? "main",
                intentValue.Get("files")?.Items.Select(f => f.Text!).ToArray() ?? [], type);
        }
        PreparedModel model;
        if (modelIntent.Request is null) model = models.PrepareLocal(modelIntent);
        else
        {
            SelectedModel? selected = null;
            if (exact?["selection"] is JsonObject modelSelection)
            {
                var selection = ContractJson.Read(modelSelection.ToJsonString(), ContractJsonContext.Default.RemoteSelection, ContractValidation.Remote);
                selected = new(exact["name"]!.GetValue<string>(), exact["companion-type"]?.GetValue<string>(), selection);
            }
            // Explicit canonical external registrations are usable in place. Do
            // not search other scopes, match bytes, or adopt unmanaged files.
            if (selected is null && request.Reference?.Canonical == true)
            {
                var external = models.List(request.Kind == "companion").SingleOrDefault(r => r!["ownership"]!.GetValue<string>() == "external" &&
                    r["logical-key"]!.GetValue<string>().Equals(request.Reference.Name, StringComparison.OrdinalIgnoreCase));
                if (external is not null)
                {
                    var path = models.ResolveReference(request.Reference);
                    return new(request.Key, request.Kind, external["resource-id"]!.GetValue<string>(),
                        external["installations"]!.AsArray().Single()!["installation-id"]!.GetValue<string>(), path,
                        new JsonObject { ["origin"] = "external" });
                }
            }
            selected ??= await models.SelectAsync(modelIntent, source, interaction, inspector, cancellation)
                ?? throw new OperationCanceledException(cancellation);
            model = await models.AcquireAsync(selected, transport, false, cancellation: cancellation, restoreMissing: true);
            if (request.Reference?.Canonical != true) models.Bind(request.Key, request.Kind, selected, model);
        }
        var installation = Installation(models.List(request.Kind == "companion"), model.ResourceId, model.InstallationId);
        var record = new JsonObject { ["origin"] = modelIntent.Request is null ? "external" : "hugging-face" };
        if (installation["selection"]?["modelSelection"] is { } resolved)
        {
            record["selection"] = resolved.DeepClone();
            record["artifacts"] = ReferenceFiles(installation);
            record["name"] = models.List(request.Kind == "companion").Single(r => r!["resource-id"]!.GetValue<string>() == model.ResourceId)!["logical-key"]!.DeepClone();
            if (modelIntent.Request?.CompanionType is { } type) record["companion-type"] = type;
        }
        return new(request.Key, request.Kind, model.ResourceId, model.InstallationId, model.EntryPath, record);
    }

    private static JsonNode Installation(JsonArray resources, string resourceId, string installationId) =>
        resources.Single(r => r!["resource-id"]!.GetValue<string>() == resourceId)!["installations"]!.AsArray()
            .Single(i => i!["installation-id"]!.GetValue<string>() == installationId)!;
    private static JsonArray ReferenceFiles(JsonNode installation) => new(installation["files"]!.AsArray().Select(file =>
        (JsonNode?)new JsonObject { ["path"] = file!["path"]!.DeepClone(), ["size"] = file["size"]!.DeepClone(),
            ["identity"] = file["integrity"]!.DeepClone() }).ToArray());
}
