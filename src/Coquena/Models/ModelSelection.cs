using System.Text.RegularExpressions;
using Coquena.Contracts;

namespace Coquena.Models;

public sealed record ModelRequest(string Repository, string? Quantization, string? CompanionType)
{
    public static ModelRequest Parse(string selector)
    {
        if (selector.Length > 512 || selector.Count(c => c == ':') > 1 || selector.Count(c => c == '#') > 1)
            throw new ModelSourceException("invalid-selector", "Selector inválido.");
        var colon = selector.IndexOf(':');
        var unit = colon < 0 ? selector : selector[..colon];
        var quant = colon < 0 ? null : selector[(colon + 1)..];
        var hash = unit.IndexOf('#');
        var repo = hash < 0 ? unit : unit[..hash];
        var type = hash < 0 ? null : unit[(hash + 1)..];
        HuggingFaceStore.ValidateRepository(repo);
        if ((quant is not null && !Regex.IsMatch(quant, "^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$")) ||
            (type is not null && type is not ("mmproj" or "mtp")))
            throw new ModelSourceException("invalid-selector", "Cuantización o tipo companion no reconocido; tipos iniciales: mmproj, mtp.");
        return new(repo, quant, type);
    }
}

public sealed record ModelVariant(string Quantization, string? CompanionType, string Label, RepositoryFile[] Files)
{
    public ModelEvidence[] Evidence { get; init; } = [];
    public string Name(string repository) => repository + (CompanionType is null ? "" : "#" + CompanionType) + ":" + Quantization;
}
public sealed record ModelCandidates(ModelVariant[] Variants, string[] Diagnostics);
public sealed record SelectedModel(string Name, string? CompanionType, RemoteSelection Selection);

public interface IModelSelectionInteraction
{
    bool IsInteractive { get; }
    void Show(string text);
    // Zero-based choice; null means cancel/EOF. Options already label the action.
    Task<int?> ChooseAsync(IReadOnlyList<string> options, CancellationToken cancellation);
}

public sealed class ConsoleModelSelectionInteraction : IModelSelectionInteraction
{
    public bool IsInteractive => !Console.IsInputRedirected;
    public void Show(string text) => Console.WriteLine(text);
    public Task<int?> ChooseAsync(IReadOnlyList<string> options, CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        for (var i = 0; i < options.Count; i++) Console.WriteLine($"{i + 1}. {options[i]}");
        Console.Write("Opción: ");
        var input = Console.ReadLine();
        cancellation.ThrowIfCancellationRequested();
        return Task.FromResult<int?>(input is null ? null : int.TryParse(input, out var value) && value > 0 && value <= options.Count ? value - 1 : -1);
    }
}

/// <summary>Returns one pinned unit after bounded metadata inspection, without creating installation state.</summary>
public sealed class ModelSelectionMenu(HuggingFaceStore store, IRemoteGgufInspector? inspector = null)
{
    public async Task<SelectedModel?> SelectAsync(ModelRequest request, IModelSelectionInteraction interaction,
        CancellationToken cancellation = default, string requestedRevision = "main")
    {
        var revision = await store.ResolveAsync(request.Repository, requestedRevision, cancellation);
        var inventory = await store.InventoryAsync(revision, cancellation);
        using var remote = inspector is null ? new RemoteGgufInspector() : null;
        var resolver = new ModelResolver(inspector ?? remote!);
        var discovered = await resolver.DiscoverAsync(inventory, interaction.Show, cancellation);
        foreach (var diagnostic in discovered.Diagnostics) interaction.Show(diagnostic);
        var variants = discovered.Variants.Where(v => v.CompanionType == request.CompanionType &&
            (request.Quantization is null || v.Quantization.Equals(request.Quantization, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (variants.Length == 0) interaction.Show(request.Quantization is null ?
            "No se resolvieron GGUF seleccionables para la unidad solicitada; revisar los diagnósticos de exploración." : "Cuantización solicitada inexistente o no seleccionable según la exploración; puede ser parcial/no concluyente. No se sustituye.");
        if (!interaction.IsInteractive)
            throw new ModelSourceException("interaction-required", "La selección necesita entrada interactiva; no se eligió ni creó trabajo.");
        var options = variants.Select(v => "Descargar " + v.Label).Concat(["Mostrar README", "Cancelar"]).ToArray();
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            var choice = await interaction.ChooseAsync(options, cancellation);
            if (choice is null || choice == variants.Length + 1) return null;
            if (choice < 0 || choice >= options.Length) { interaction.Show("Opción inválida."); continue; }
            if (choice == variants.Length)
            {
                try { interaction.Show(await store.ReadmeAsync(revision, cancellation)); }
                catch (ModelSourceException error) { interaction.Show($"README: {error.Code}: {error.Message}"); }
                continue;
            }
            var selected = variants[choice.Value];
            try
            {
                var evidence = await resolver.ValidateAsync(revision, new(selected.Files), interaction.Show, cancellation);
                var verified = ModelSelection.Variant(new(selected.Files), evidence);
                if (verified is null || verified.Quantization != selected.Quantization || verified.CompanionType != selected.CompanionType)
                    throw new ModelSourceException("selection-conflict", string.Join("; ", evidence.Select(e => e.Diagnostic ?? "Rol/etiqueta cambió tras inspección.")));
            }
            catch (ModelSourceException e) { interaction.Show(e.Code + ": " + e.Message); continue; }
            // The consumer starts the action with this result; never ask for another confirmation.
            return new(selected.Name(revision.Repository), selected.CompanionType,
                new(revision.Repository, revision.RequestedRevision, revision.Commit, selected.Files.Select(f => f.ToRemoteFile()).ToArray()));
        }
    }
}
