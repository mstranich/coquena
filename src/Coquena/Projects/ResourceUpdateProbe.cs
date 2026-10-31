using System.Text.Json.Nodes;
using Coquena.Backends;
using Coquena.Contracts;
using Coquena.Models;

namespace Coquena.Projects;

// Queries metadata only. Neither inventory nor installed bytes are changed here.
public sealed class ResourceUpdateProbe(HuggingFaceStore models,
    Func<string, CancellationToken, Task<BackendResolution>> backend,
    IModelSelectionInteraction interaction)
{
    public async Task<JsonObject> CheckAsync(PreparationRequest request, JsonObject resource,
        JsonObject installation, CancellationToken cancellation = default)
    {
        var result = new JsonObject { ["key"] = request.Key, ["kind"] = request.Kind,
            ["resource-id"] = resource["resource-id"]!.DeepClone(),
            ["installation-id"] = installation["installation-id"]!.DeepClone(),
            ["checked-at"] = DateTimeOffset.UtcNow.ToString("O") };
        if (resource["ownership"]!.GetValue<string>() == "external")
        { result["status"] = "not-applicable"; return result; }
        try
        {
            if (request.Kind == "backend")
            {
                var current = installation["selection"]!["backendSelection"]!;
                result["current"] = current.DeepClone();
                var family = current["variant"]!.GetValue<string>();
                var candidate = (await backend(family, cancellation)).Selected;
                if (candidate is null) { result["status"] = "inconclusive"; return result; }
                if (candidate.Variant != family || candidate.Platform != current["platform"]!.GetValue<string>() || !candidate.Complete)
                    throw new BackendException("candidate-filter-conflict", "El candidato no conserva la variante/plataforma.");
                result["compatibility"] = candidate.Compatibility;
                result["diagnostic"] = candidate.Diagnostic;
                var newer = BackendCatalog.BuildNumber(candidate.Release) > BackendCatalog.BuildNumber(current["release"]!.GetValue<string>());
                result["status"] = newer ? "update-available" : "unchanged";
                result["upgrade-allowed"] = request.Intent.Get("version")?.Text is null or "latest";
                if (newer) result["proposed"] = new JsonObject { ["origin"] = current["origin"]!.DeepClone(),
                    ["release"] = candidate.Release, ["family"] = current["family"]!.DeepClone(),
                    ["variant"] = family, ["platform"] = candidate.Platform,
                    ["artifacts"] = new JsonArray(candidate.Artifacts.Select(a => (JsonNode)new JsonObject { ["path"] = a.Name, ["size"] = a.Size }).ToArray()) };
                return result;
            }
            var selection = ContractJson.Read(installation["selection"]!["modelSelection"]!.ToJsonString(),
                ContractJsonContext.Default.RemoteSelection, ContractValidation.Remote);
            result["current"] = JsonNode.Parse(ContractJson.Write(selection, ContractJsonContext.Default.RemoteSelection));
            var name = resource["logical-key"]!.GetValue<string>();
            var parsed = ModelRequest.Parse(name);
            var original = new ModelRecord(Guid.Parse(resource["resource-id"]!.GetValue<string>()), name, selection, [], [], CompanionType: parsed.CompanionType);
            var revision = await models.ResolveAsync(selection.Repository, selection.RequestedRevision, cancellation);
            var inventory = await models.InventoryAsync(revision, cancellation);
            var comparison = ModelUpdateComparer.Compare(original, inventory);
            if (comparison.Plan is { } proposed && proposed.Actions.Any(a => a.Action != "reuse") && !Compatible(proposed.Target.Files.Select(f => f.Path)))
                comparison = new("intervention-required", "Las rutas actuales contradicen la cuantización o el tipo; no se cambia la selección.", null, []);
            var candidates = comparison.Candidates.Where(u => comparison.ExactCandidates || Compatible(u.Files.Select(f => f.Path))).ToArray();
            if (comparison.State == "intervention-required" && candidates.Length > 0)
            {
                result["candidates"] = new JsonArray(candidates.Select(u => (JsonNode)new JsonArray(u.Files.Select(f => (JsonNode)JsonValue.Create(f.Path)!).ToArray())).ToArray());
                if (interaction.IsInteractive && candidates.Length <= 128)
                {
                    interaction.Show(comparison.Diagnostic);
                    var choice = await interaction.ChooseAsync(candidates.Select(u => string.Join(", ", u.Files.Select(f => f.Path)))
                        .Append("Cancelar; conservar selección sin resolver").ToArray(), cancellation);
                    if (choice is >= 0 && choice < candidates.Length)
                        comparison = new("update-available", "Destino elegido explícitamente; no acredita sucesión.",
                            ModelUpdateComparer.Plan(original, revision, candidates[choice.Value], true, true), []);
                    else if (choice is not null && choice != candidates.Length) throw new ArgumentException("Selección inválida.");
                }
            }
            result["status"] = comparison.State;
            result["diagnostic"] = comparison.Diagnostic;
            result["observed-commit"] = revision.Commit;
            if (comparison.Plan is { } plan)
            {
                result["proposed"] = JsonNode.Parse(ContractJson.Write(plan.Target, ContractJsonContext.Default.RemoteSelection));
                result["explicit-choice"] = plan.ExplicitChoice;
                result["actions"] = new JsonArray(plan.Actions.Select(a => (JsonNode)new JsonObject { ["target-path"] = a.TargetPath,
                    ["source-path"] = a.SourcePath, ["action"] = a.Action }).ToArray());
            }
            return result;

            bool Compatible(IEnumerable<string> paths) => paths.All(path =>
                (ModelSelection.NameQuantization(path) is not string quant || quant.Equals(parsed.Quantization, StringComparison.OrdinalIgnoreCase)) &&
                ModelSelection.NameRole(path) == (parsed.CompanionType == "mmproj" ? ModelRole.Mmproj : parsed.CompanionType == "mtp" ? ModelRole.Mtp : ModelRole.Principal));
        }
        catch (Exception error) when (error is ModelSourceException or BackendException or HttpRequestException or IOException)
        {
            result["status"] = "query-failed";
            result["diagnostic"] = error switch { ModelSourceException m => m.Code, BackendException b => b.Code, _ => "remote-unavailable" };
            return result;
        }
    }
}
