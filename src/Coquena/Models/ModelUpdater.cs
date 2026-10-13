using Coquena.Contracts;
using Coquena.Storage;

namespace Coquena.Models;

public sealed record ModelUpdateComparison(string State, string Diagnostic, ModelUpdatePlan? Plan,
    ModelUnit[] Candidates, bool ExactCandidates = false);

// Pure comparison. Remote identity domains are never replaced by size, commit or local hashes.
public static class ModelUpdateComparer
{
    public static bool Comparable(ContentIdentity? a, ContentIdentity? b) =>
        a is not null && b is not null && a.Algorithm == b.Algorithm && a.Domain == b.Domain &&
        a.Algorithm switch
        {
            "sha256" => a.Domain == "file-bytes" && HuggingFaceStore.Hex(a.Value, 64) && HuggingFaceStore.Hex(b.Value, 64),
            "sha1" => a.Domain == "git-blob" && HuggingFaceStore.Hex(a.Value, 40) && HuggingFaceStore.Hex(b.Value, 40),
            "xet" => a.Domain == "xet-file" && HuggingFaceStore.Hex(a.Value, 64) && HuggingFaceStore.Hex(b.Value, 64),
            _ => false
        };
    public static bool Same(ContentIdentity? a, ContentIdentity? b) => Comparable(a, b) &&
        string.Equals(a!.Value, b!.Value, StringComparison.OrdinalIgnoreCase);

    public static ModelUpdateComparison Compare(ModelRecord installed, RepositoryInventory inventory, bool complete = true)
    {
        if (installed.Ownership == "external") return new("not-applicable", "Externo sin seguimiento.", null, []);
        var baseline = installed.Selection!;
        var followed = installed.Update?.Followed ?? baseline;
        var files = followed.Files.Select(f => inventory.Files.SingleOrDefault(r => r.Path == f.Path)).ToArray();
        var units = ModelSelection.Units(inventory, []);
        if (files.All(f => f is not null))
        {
            var target = files.Select(f => f!).ToArray();
            if (!units.Any(u => u.Files.Select(f => f.Path).SequenceEqual(target.Select(f => f.Path))))
                return new("intervention-required", "La selección no constituye un conjunto GGUF completo.", null, []);
            // A manual successor establishes independent tracking; reuse still compares installed bytes.
            var reference = installed.Update?.FollowedName is not null ? followed : baseline;
            var comparable = target.Length == reference.Files.Length && target.Where((f, i) =>
                !Comparable(reference.Files[i].Identity, f.ToRemoteFile().Identity)).Any() == false;
            if (!comparable) return new("intervention-required", "Identidades ausentes/incomparables; no se infiere igualdad por commit o tamaño.", null, []);
            var plan = Plan(installed, inventory.Revision, new(target), installed.Update?.FollowedName is not null, true);
            var unchanged = plan.Actions.All(a => a.Action == "reuse" && a.TargetPath == a.SourcePath) &&
                baseline.Repository == inventory.Revision.Repository;
            return new(unchanged ? "unchanged" : "update-available", unchanged ? "Contenido seleccionado sin cambios." :
                "Cambios de contenido/rutas identificados en commit fijo.", unchanged ? null : plan, []);
        }
        if (!complete) return new("query-failed", "Inventario incompleto; no prueba ausencia ni reubicación única.", null, []);
        var exact = units.Where(u => u.Files.Length == followed.Files.Length && u.Files.Where((f, i) =>
            !Same(followed.Files[i].Identity, f.ToRemoteFile().Identity)).Any() == false).ToArray();
        if (exact.Length == 1) return new("update-available", "Reubicación exacta única del conjunto completo; reutilizar sin descarga.",
            Plan(installed, inventory.Revision, exact[0], installed.Update?.FollowedName is not null, true), []);
        if (exact.Length > 1) return new("intervention-required", "Varias reubicaciones exactas; elegir cuál seguir.", null, exact, true);
        var candidates = ModelSelection.Discover(inventory).Variants.Where(v => v.CompanionType == installed.CompanionType)
            .Select(v => new ModelUnit(v.Files)).ToArray();
        if (candidates.Length > 0) return new("intervention-required", "Rutas desaparecidas; candidatos sin sucesión demostrada.", null, candidates);
        // Unclassified GGUF may still be successors: do not declare orphan based on filename discovery alone.
        var unknown = units.Where(u => u.Files.Any(f => ModelSelection.Classify(inventory.Revision, f).State == EvidenceState.Inconclusive)).ToArray();
        return unknown.Length > 0 || inventory.Files.Any(f => f.Path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
            ? new("intervention-required", "Sin sucesor resoluble; evidencia GGUF incompleta o de otro rol.", null, [])
            : new("orphaned", "Inventario completo sin selección seguida ni candidatos.", null, []);
    }

    public static ModelUpdatePlan Plan(ModelRecord installed, RepositoryRevision revision, ModelUnit target, bool explicitChoice,
        bool preserveName = false, string? targetName = null)
    {
        var baseline = installed.Selection!;
        var selection = new RemoteSelection(revision.Repository, revision.RequestedRevision, revision.Commit,
            target.Files.Select(f => f.ToRemoteFile()).ToArray());
        var actions = selection.Files.Select(f =>
        {
            var same = baseline.Files.FirstOrDefault(old => Same(old.Identity, f.Identity));
            return new ModelUpdateAction(f.Path, same?.Path, same is null ? "download" : "reuse");
        }).ToArray();
        var variant = ModelSelection.Variant(target, target.Files.Select(f => ModelSelection.Classify(revision, f)).ToArray());
        var name = targetName ?? (explicitChoice && !preserveName ? variant?.Name(revision.Repository) : null) ?? revision.Repository +
            (installed.CompanionType is null ? "" : "#" + installed.CompanionType) + ":" +
            ModelRequest.Parse(installed.Update?.FollowedName ?? installed.Name).Quantization;
        return new(baseline, selection, name, actions, explicitChoice);
    }
}

public sealed class ModelUpdater(Home home)
{
    private readonly LocalStore store = new(home);
    private static readonly ContractJsonContext Json = ContractJsonContext.Default;
    public ModelRecord[] Scope(string? name, bool companions)
    {
        var records = (store.Read(DocumentKind.Models, Json.ModelsDocument, ContractValidation.Models) ?? new(1, [])).Models;
        if (name is null) return records.Where(m => !companions || m.CompanionType is not null).ToArray();
        var found = records.SingleOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase) &&
            (companions ? m.CompanionType is not null : m.CompanionType is null));
        return found is null ? throw new ModelSourceException("model-not-found", "Unidad no registrada en el ámbito solicitado.") : [found];
    }

    public async Task<ModelRecord> CheckAsync(ModelRecord original, HuggingFaceStore source,
        IModelSelectionInteraction interaction, CancellationToken cancellation = default, IRemoteGgufInspector? inspector = null)
    {
        if (original.Ownership == "external") return original;
        var attempt = DateTimeOffset.UtcNow;
        ModelUpdateObservation observation;
        try
        {
            var followed = original.Update?.Followed ?? original.Selection!;
            var revision = await source.ResolveAsync(followed.Repository, followed.RequestedRevision, cancellation);
            var inventory = await source.InventoryAsync(revision, cancellation);
            var result = ModelUpdateComparer.Compare(original, inventory);
            if (result.State == "intervention-required" && followed.Files.Any(f => inventory.Files.All(r => r.Path != f.Path)))
            {
                var candidates = result.Candidates;
                Dictionary<string, string> names = new(StringComparer.Ordinal);
                // Exact identity candidates need no header reads. Others use the existing bounded GGUF resolver.
                if (!result.ExactCandidates)
                {
                    using var remote = inspector is null ? new RemoteGgufInspector() : null;
                    var discovered = await new ModelResolver(inspector ?? remote!).DiscoverAsync(inventory, interaction.Show, cancellation);
                    var selectable = discovered.Variants.Where(v => v.CompanionType == original.CompanionType).ToArray();
                    candidates = selectable.Select(v => new ModelUnit(v.Files)).ToArray();
                    foreach (var variant in selectable) names[variant.Files[0].Path] = variant.Name(revision.Repository);
                    if (candidates.Length == 0 && discovered.Diagnostics.Length == 0)
                        result = new("orphaned", "Selección ausente sin sucesor ni candidatos en inventario completo.", null, []);
                }
                if (candidates.Length > 0 && candidates.Length <= 128 && interaction.IsInteractive)
                {
                    interaction.Show(result.Diagnostic + " Elegir fija seguimiento y plan; no modifica pesos ni prueba sucesión.");
                    var options = candidates.Select(u =>
                    {
                        var p = ModelUpdateComparer.Plan(original, revision, u, true, result.ExactCandidates,
                            names.GetValueOrDefault(u.Files[0].Path));
                        return $"{p.TargetName} @ {revision.Commit} | " + string.Join("; ", p.Actions.Select(a =>
                            $"{a.TargetPath}: {a.Action}")) + " | " + u.Files.Sum(f => (decimal)f.Size) + " bytes";
                    }).Append("Cancelar; conservar seguimiento sin resolver").ToArray();
                    var choice = await interaction.ChooseAsync(options, cancellation);
                    if (choice is >= 0 && choice < candidates.Length)
                        result = new("update-available", "Destino elegido explícitamente; no prueba identidad retrospectiva.",
                            ModelUpdateComparer.Plan(original, revision, candidates[choice.Value], true, result.ExactCandidates,
                                names.GetValueOrDefault(candidates[choice.Value].Files[0].Path)), []);
                    else if (choice is not null && choice != candidates.Length)
                        throw new ModelSourceException("invalid-choice", "Selección inválida.");
                }
            }
            observation = new(attempt, DateTimeOffset.UtcNow, result.State, result.Diagnostic, result.State,
                result.Plan, result.Plan?.Target ?? original.Update?.Followed, result.Diagnostic,
                result.Plan?.ExplicitChoice == true ? result.Plan.TargetName : original.Update?.FollowedName);
        }
        catch (Exception e) when (e is ModelSourceException or HttpRequestException or IOException or OperationCanceledException)
        {
            observation = new(attempt, original.Update?.LastSuccess, "query-failed", e.Message,
                original.Update?.LastSuccessfulState, original.Update?.Plan, original.Update?.Followed,
                original.Update?.LastSuccessfulDiagnostic, original.Update?.FollowedName);
        }
        return store.Update(DocumentKind.Models, Json.ModelsDocument, ContractValidation.Models, document =>
        {
            ModelInstaller.CheckUpgradeMutation(home, store, original.Id);
            var current = document?.Models.SingleOrDefault(m => m.Id == original.Id);
            if (current is null || ContractJson.Write(current, Json.ModelRecord) != ContractJson.Write(original, Json.ModelRecord))
                throw new ModelSourceException("model-changed", "Unidad modificada durante consulta; no se guarda observación obsoleta.");
            return document! with { Models = document.Models.Select(m => m.Id == original.Id ? m with { Update = observation } : m).ToArray() };
        }).Models.Single(m => m.Id == original.Id);
    }
}
