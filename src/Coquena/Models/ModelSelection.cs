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
    public string Name(string repository) => repository + (CompanionType is null ? "" : "#" + CompanionType) + ":" + Quantization;
}
public sealed record ModelCandidates(ModelVariant[] Variants, string[] Diagnostics);
public sealed record SelectedModel(string Name, string? CompanionType, RemoteSelection Selection);

public static class ModelSelection
{
    // Names discover candidates only. 008 validates GGUF structure; 009 verifies bytes.
    public static ModelCandidates Discover(RepositoryInventory inventory)
    {
        List<string> diagnostics = [];
        List<(RepositoryFile File, string Quant, string? Type, string Key, int Index, int Total)> parts = [];
        foreach (var file in inventory.Files.Where(f => f.Path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)))
        {
            HuggingFaceStore.ValidatePath(file.Path);
            var slash = file.Path.LastIndexOf('/');
            var folder = slash < 0 ? "" : file.Path[..(slash + 1)];
            var stem = file.Path[(slash + 1)..^5];
            string? type = stem.StartsWith("mmproj-", StringComparison.OrdinalIgnoreCase) ? "mmproj" :
                stem.StartsWith("mtp-", StringComparison.OrdinalIgnoreCase) ? "mtp" : null;
            var shard = Regex.Match(stem, "^(.*)-([0-9]{5})-of-([0-9]{5})$", RegexOptions.CultureInvariant);
            if (!shard.Success && Regex.IsMatch(stem, "-[0-9]+-of-[0-9]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                diagnostics.Add($"{file.Path}: nombres de partes no convencionales; no se inventa conjunto ni orden.");
                continue;
            }
            var prefix = shard.Success ? shard.Groups[1].Value : stem;
            var quant = Regex.Match(prefix,
                "(?:^|-)((?:IQ[0-9]+_[A-Z0-9_]+|Q[0-9]+(?:_[A-Z0-9]+)*|TQ[0-9]+_[A-Z0-9_]+|(?:MXFP4|NVFP4)(?:_[A-Z0-9]+)*|BF16|F16|F32|F64|FP16|FP32)(?:-[A-Z0-9]+)*)$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
            if (!quant.Success)
            {
                diagnostics.Add($"{file.Path}: variante no convencional; no se inventa cuantización ni conjunto.");
                continue;
            }
            parts.Add((file, quant.Groups[1].Value, type, folder + prefix,
                shard.Success ? int.Parse(shard.Groups[2].Value) : 1,
                shard.Success ? int.Parse(shard.Groups[3].Value) : 1));
        }
        List<ModelVariant> variants = [];
        foreach (var group in parts.GroupBy(p => p.Key, StringComparer.Ordinal))
        {
            var first = group.First();
            var ordered = group.OrderBy(p => p.Index).ToArray();
            if (first.Total < 1 || group.Any(p => p.Total != first.Total) || ordered.Length != first.Total ||
                ordered.Where((p, i) => p.Index != i + 1).Any())
            {
                diagnostics.Add($"{group.Key}: conjunto incompleto o contradictorio; no se ofrece descargar.");
                continue;
            }
            variants.Add(new(first.Quant, first.Type,
                $"{first.Quant} — {string.Join(", ", ordered.Select(p => p.File.Path))}", ordered.Select(p => p.File).ToArray()));
        }
        return new(variants.OrderBy(v => v.CompanionType, StringComparer.Ordinal)
            .ThenBy(v => v.Quantization, StringComparer.OrdinalIgnoreCase).ThenBy(v => v.Label, StringComparer.Ordinal).ToArray(),
            diagnostics.ToArray());
    }
}

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

/// <summary>007 menu: returns exactly one pinned unit, never allocates a GUID or transfers weights.</summary>
public sealed class ModelSelectionMenu(HuggingFaceStore store)
{
    public async Task<SelectedModel?> SelectAsync(ModelRequest request, IModelSelectionInteraction interaction,
        CancellationToken cancellation = default)
    {
        var revision = await store.ResolveAsync(request.Repository, cancellation: cancellation);
        var inventory = await store.InventoryAsync(revision, cancellation);
        var discovered = ModelSelection.Discover(inventory);
        foreach (var diagnostic in discovered.Diagnostics) interaction.Show(diagnostic);
        var variants = discovered.Variants.Where(v => v.CompanionType == request.CompanionType &&
            (request.Quantization is null || v.Quantization.Equals(request.Quantization, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (variants.Length == 0) interaction.Show(request.Quantization is null ?
            "No hay GGUF seleccionables para la unidad solicitada." : "Cuantización solicitada inexistente o no seleccionable; no se sustituye.");
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
            // The consumer starts the action with this result; never ask for another confirmation.
            return new(selected.Name(revision.Repository), selected.CompanionType,
                new(revision.Repository, revision.RequestedRevision, revision.Commit, selected.Files.Select(f => f.ToRemoteFile()).ToArray()));
        }
    }
}
