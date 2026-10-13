using Coquena.Contracts;

namespace Coquena.Models;

public static class ExternalMatching
{
    // Inventory metadata only: no resolve/weights requests, full hashes or inferred provenance.
    public static async Task<InformativeMatch?> SelectAsync(ModelRecord local, HuggingFaceStore source,
        IModelSelectionInteraction interaction, CancellationToken cancellation)
    {
        if (!interaction.IsInteractive)
        {
            interaction.Show("Sin interacción: registro externo conservado sin emparejamiento.");
            return null;
        }
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        budget.CancelAfter(TimeSpan.FromSeconds(30));
        var request = ModelRequest.Parse(local.Name);
        var revision = await source.ResolveAsync(request.Repository, cancellation: budget.Token);
        var inventory = await source.InventoryAsync(revision, budget.Token);
        var observed = GgufInspection.Inspect(local.Files[0].LocalPath, budget.Token);
        if (observed.Status != "complete")
        {
            interaction.Show("Metadatos locales incompletos/no disponibles; registro conservado sin emparejamiento.");
            return null;
        }
        var candidates = ModelSelection.Discover(inventory).Variants
            .Where(v => v.CompanionType == local.CompanionType &&
                v.Quantization.Equals(request.Quantization, StringComparison.OrdinalIgnoreCase) &&
                v.Files.Length == local.Files.Length &&
                v.Files.Where((f, i) => f.Size != local.Files[i].Size).Any() == false).ToArray();
        // A recognized local file_type is additional evidence, never a remote-byte certificate.
        candidates = candidates.Where(v => observed.Parts.All(p => p.Header?.FileTypeId is not { } id ||
            v.Evidence.All(e => e.BaseQuantization is null || ModelSelection.FileType(id) == e.BaseQuantization))).ToArray();
        if (local.CompanionType != "mmproj" && observed.Parts.Any(p =>
            p.Header?.Hints.TryGetValue("general.architecture", out var architecture) == true && architecture == "clip"))
            candidates = [];
        if (candidates.Length == 0)
        {
            interaction.Show("Sin candidatos por metadatos; registro externo conservado.");
            return null;
        }
        if (candidates.Length > 128)
        {
            interaction.Show("Demasiados candidatos (límite 128); registro conservado sin emparejar.");
            return null;
        }
        string[] reasons = ["Repositorio y cuantización declarados; rol candidato por nombre remoto",
            "Mismo número de archivos y tamaños por parte; cabecera local sin contradicción conocida"];
        interaction.Show("Equivalencia informativa: no certifica bytes, procedencia ni revisión; no habilita update/upgrade.");
        var options = candidates.Select(v => $"{revision.Repository}@{revision.Commit} | {string.Join(", ", v.Files.Select(f => f.Path))} | {string.Join("; ", reasons)}").Append("Cancelar emparejamiento").ToArray();
        var choice = await interaction.ChooseAsync(options, cancellation);
        if (choice is null || choice == candidates.Length) return null;
        if (choice < 0 || choice >= candidates.Length)
            throw new ModelSourceException("invalid-choice", "Selección inválida; alta local conservada.");
        return new(new(revision.Repository, revision.RequestedRevision, revision.Commit,
            candidates[choice.Value].Files.Select(f => f.ToRemoteFile()).ToArray()), reasons, DateTimeOffset.UtcNow);
    }
}
