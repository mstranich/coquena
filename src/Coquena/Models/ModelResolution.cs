using System.Text.RegularExpressions;

namespace Coquena.Models;

public enum ModelRole { Principal, Mmproj, Mtp, Unknown }
public enum EvidenceSource { Name, Header, NameAndHeader }
public enum EvidenceState { Candidate, MetadataVerified, Inconclusive, Conflict }
public sealed record ModelEvidence(string Path, string Commit, ModelRole Role, string? BaseQuantization,
    string? PublisherLabel, EvidenceSource Source, EvidenceState State, string? Diagnostic);
public sealed record ModelUnit(RepositoryFile[] Files);

public static class ModelSelection
{
    // llama_ftype at 71ad0590f4808b6202f9213d166913858c73b1bc/include/llama.h.
    // Removed IDs and GUESSED=1024 are deliberately unknown. These are not ggml tensor types.
    public static string? FileType(uint id) => id switch
    {
        0 => "F32", 1 => "F16", 2 => "Q4_0", 3 => "Q4_1", 7 => "Q8_0", 8 => "Q5_0", 9 => "Q5_1",
        10 => "Q2_K", 11 => "Q3_K_S", 12 => "Q3_K_M", 13 => "Q3_K_L", 14 => "Q4_K_S", 15 => "Q4_K_M",
        16 => "Q5_K_S", 17 => "Q5_K_M", 18 => "Q6_K", 19 => "IQ2_XXS", 20 => "IQ2_XS", 21 => "Q2_K_S",
        22 => "IQ3_XS", 23 => "IQ3_XXS", 24 => "IQ1_S", 25 => "IQ4_NL", 26 => "IQ3_S", 27 => "IQ3_M",
        28 => "IQ2_S", 29 => "IQ2_M", 30 => "IQ4_XS", 31 => "IQ1_M", 32 => "BF16",
        36 => "TQ1_0", 37 => "TQ2_0", 38 => "MXFP4_MOE", 39 => "NVFP4", 40 => "Q1_0", 41 => "Q2_0", _ => null
    };
    // Publisher labels documented by Unsloth, with exact paths pinned in 015 fixtures.
    private static readonly string[] Recipes = ["Q2_K_XL", "Q3_K_XL", "Q4_K_L", "Q4_K_XL", "Q5_K_XL", "Q6_K_S", "Q6_K_XL", "Q8_K_XL"];
    private static readonly string[] DynamicLabels = Recipes.Concat(["IQ2_M", "IQ2_XXS", "IQ3_S", "IQ3_XXS", "IQ4_NL", "IQ4_XS"])
        .Select(t => "UD-" + t).ToArray();
    private static readonly string[] CanonicalTokens = Enumerable.Range(0, 42).Select(i => FileType((uint)i))
        .Where(q => q is not null).Select(q => q!).Concat(Recipes).ToArray();
    private static readonly string[] Tokens = CanonicalTokens.Concat(DynamicLabels)
        .Concat(["IQ1_S-XL", "MXFP4", "FP16", "FP32", "F64", "Q4", "Q8"])
        .OrderByDescending(q => q.Length).ToArray();
    public static string? NameQuantization(string path)
    {
        var stem = path.Split('/')[^1][..^5];
        var found = Tokens.Where(token => Regex.IsMatch(stem, "(?:^|[-_.])" + Regex.Escape(token) + (token is "Q4" or "Q8" ? "(?=$|[-.])" : "(?=$|[-_.])"),
            RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)).ToArray();
        // Prefer the longest full token, but never merge different quantizations in one name.
        var full = found.Where(t => !found.Any(other => other.Length > t.Length &&
            (other.StartsWith(t, StringComparison.Ordinal) || other == "UD-" + t))).ToArray();
        return full.Length == 1 ? full[0] : null;
    }
    public static ModelRole NameRole(string path)
    {
        var mmproj = Regex.IsMatch(path, "(?:^|[/_.-])mmproj(?=$|[/_.-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        // Explicit draft markers anywhere, or mtp- at the start of the basename,
        // or an exact mtp directory. An internal MTP token is also used by principals.
        var mtp = path.Split('/')[^1].StartsWith("mtp-", StringComparison.OrdinalIgnoreCase) ||
            Regex.IsMatch(path, "(?:^|[/_.-])mtp-?draft(?=$|[/_.-])|(?:^|/)mtp/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return mmproj && mtp ? ModelRole.Unknown : mmproj ? ModelRole.Mmproj : mtp ? ModelRole.Mtp : ModelRole.Principal;
    }
    private static string? Base(string label)
    {
        if (label.StartsWith("UD-",StringComparison.Ordinal)) label=label[3..];
        return Recipes.Contains(label,StringComparer.Ordinal) ? null : label switch { "IQ1_S-XL" => "IQ1_S", "FP16" => "F16", "FP32" => "F32", _ => label };
    }
    public static ModelEvidence Classify(RepositoryRevision revision, RepositoryFile file, GgufHeader? header = null)
    {
        var label = NameQuantization(file.Path); var role = NameRole(file.Path);
        var quant = header?.FileTypeId is { } id ? FileType(id) : null;
        var source = header is null ? EvidenceSource.Name : label is null ? EvidenceSource.Header : EvidenceSource.NameAndHeader;
        var state = header is null ? EvidenceState.Candidate : EvidenceState.MetadataVerified;
        string? diagnostic = null;
        if (header is not null && quant is null && label is not null)
        { source = EvidenceSource.Name; state = EvidenceState.Candidate; diagnostic = "Cuantización candidata por nombre; cabecera sin file_type reconocido."; }
        if (role == ModelRole.Unknown) { state = EvidenceState.Conflict; diagnostic = "Rol auxiliar contradictorio."; }
        if (header?.Hints.TryGetValue("general.architecture", out var architecture) == true && architecture == "clip")
        {
            if (role == ModelRole.Mtp) { state = EvidenceState.Conflict; diagnostic = "Rol mtp contradice arquitectura clip."; }
            else role = ModelRole.Mmproj;
        }
        var publisherRecipe = label is not null && (label.StartsWith("UD-",StringComparison.Ordinal) || Base(label) is null);
        if (label is not null && quant is not null && !publisherRecipe && Base(label) != quant)
        { state = EvidenceState.Conflict; diagnostic = $"Nombre {label} contradice general.file_type {quant}."; }
        if (publisherRecipe && state != EvidenceState.Conflict)
        { state = EvidenceState.Candidate; diagnostic = "Etiqueta del publicador conservada; file_type describe predominancia, no certifica la receta dinámica."; }
        if (header?.FileTypeId is { } unknown && quant is null && state != EvidenceState.Conflict)
            diagnostic = $"general.file_type={unknown} desconocido; no se inventa etiqueta.";
        label ??= quant;
        if (role == ModelRole.Mtp && state != EvidenceState.Conflict)
        {
            state = EvidenceState.Candidate;
            diagnostic = (diagnostic is null ? "" : diagnostic + " ") +
                "Rol auxiliar MTP candidato por nombre/carpeta; cabecera no certifica rol ni compatibilidad.";
        }
        if (label is null && state != EvidenceState.Conflict)
        {
            state = EvidenceState.Inconclusive;
            diagnostic = (diagnostic is null ? "" : diagnostic + " ") + "Sin cuantización resoluble por nombre/metadatos.";
        }
        return new(file.Path, revision.Commit, role, label is null ? null : quant ?? Base(label), label, source, state, diagnostic);
    }
    public static ModelUnit[] Units(RepositoryInventory inventory, List<string> diagnostics)
    {
        List<(RepositoryFile File, string Key, int Index, int Total)> parts = [];
        foreach (var file in inventory.Files.Where(f => f.Path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)))
        {
            HuggingFaceStore.ValidatePath(file.Path);
            var stem = file.Path[..^5];
            var m = Regex.Match(stem, "^(.*)-([0-9]{5})-of-([0-9]{5})$", RegexOptions.CultureInvariant);
            if (!m.Success && Regex.IsMatch(stem, "-[0-9]+-of-[0-9]+$", RegexOptions.CultureInvariant))
            { diagnostics.Add(file.Path + ": división no convencional; no se inventa agrupación."); continue; }
            parts.Add((file, m.Success ? m.Groups[1].Value : stem, m.Success ? int.Parse(m.Groups[2].Value) : 1,
                m.Success ? int.Parse(m.Groups[3].Value) : 1));
        }
        List<ModelUnit> units = [];
        foreach (var group in parts.GroupBy(p => p.Key, StringComparer.Ordinal))
        {
            var ordered = group.OrderBy(p => p.Index).ToArray(); var total = ordered[0].Total;
            if (total is < 1 or > 65535 || ordered.Length != total || ordered.Any(p => p.Total != total) || ordered.Where((p, i) => p.Index != i + 1).Any())
            { diagnostics.Add(group.Key + ": conjunto incompleto/contradictorio; no seleccionable."); continue; }
            units.Add(new(ordered.Select(p => p.File).ToArray()));
        }
        return units.ToArray();
    }
    public static ModelVariant? Variant(ModelUnit unit, ModelEvidence[] evidence)
    {
        var first = evidence[0];
        if (evidence.Any(e => e.PublisherLabel is null || e.State is EvidenceState.Conflict or EvidenceState.Inconclusive ||
            e.PublisherLabel != first.PublisherLabel || e.Role != first.Role)) return null;
        var type = first.Role switch { ModelRole.Mmproj => "mmproj", ModelRole.Mtp => "mtp", _ => null };
        var total = unit.Files.Aggregate(0m, (sum, f) => sum + f.Size);
        return new(first.PublisherLabel!, type,
            $"{first.PublisherLabel} — {string.Join(", ", unit.Files.Select(f => f.Path))} — {total / 1000000000m:F2} GB / {total / 1073741824m:F2} GiB — {first.Source}/{first.State}" +
                (first.Role == ModelRole.Mtp ? " — rol MTP candidato por nombre/carpeta; compatibilidad no certificada" : ""), unit.Files)
            { Evidence = evidence };
    }
    public static ModelCandidates Discover(RepositoryInventory inventory)
    {
        List<string> diagnostics = [];
        var variants = Units(inventory, diagnostics).Select(u =>
        {
            var evidence = u.Files.Select(f => Classify(inventory.Revision, f)).ToArray();
            foreach (var e in evidence.Where(e => e.Diagnostic is not null)) diagnostics.Add(e.Path + ": " + e.Diagnostic);
            return Variant(u, evidence);
        }).Where(v => v is not null).Select(v => v!).ToArray();
        return Sorted(variants, diagnostics);
    }
    internal static ModelCandidates Sorted(IEnumerable<ModelVariant> variants, List<string> diagnostics) => new(
        variants.OrderBy(v => v.CompanionType, StringComparer.Ordinal).ThenBy(v => v.Quantization, StringComparer.OrdinalIgnoreCase)
            .ThenBy(v => v.Label, StringComparer.Ordinal).ToArray(), diagnostics.ToArray());
}

public sealed class ModelResolver(IRemoteGgufInspector inspector, long unitBytes = 134217728)
{
    public async Task<ModelCandidates> DiscoverAsync(RepositoryInventory inventory, Action<string> show, CancellationToken cancellation)
    {
        List<string> diagnostics = []; List<ModelVariant> variants = [];
        foreach (var unit in ModelSelection.Units(inventory, diagnostics))
        {
            var evidence = unit.Files.Select(f => ModelSelection.Classify(inventory.Revision, f)).ToArray();
            // Conventional names are cheap candidates. Ambiguous/arbitrary names get a chance through metadata.
            if (evidence.Any(e => e.PublisherLabel is null))
            {
                try { evidence = await ValidateAsync(inventory.Revision, unit, show, cancellation); }
                catch (ModelSourceException e) { diagnostics.Add(unit.Files[0].Path + ": " + e.Code + ": " + e.Message); continue; }
            }
            foreach (var e in evidence.Where(e => e.Diagnostic is not null)) diagnostics.Add(e.Path + ": " + e.Diagnostic);
            var variant = ModelSelection.Variant(unit, evidence); if (variant is not null) variants.Add(variant);
        }
        return ModelSelection.Sorted(variants, diagnostics);
    }
    public async Task<ModelEvidence[]> ValidateAsync(RepositoryRevision revision, ModelUnit unit, Action<string> show, CancellationToken cancellation)
    {
        List<GgufHeader> headers = []; List<ModelEvidence> evidence = [];
        (inspector as RemoteGgufInspector)?.BeginUnit();
        var initialBytes = (inspector as RemoteGgufInspector)?.Statistics.Bytes ?? 0;
        for (var i = 0; i < unit.Files.Length; i++)
        {
            show("Inspeccionando metadatos GGUF (sin instalar pesos): " + unit.Files[i].Path);
            var header = await inspector.InspectAsync(revision, unit.Files[i], cancellation);
            if (inspector is RemoteGgufInspector remote && remote.Statistics.Bytes - initialBytes > unitBytes)
                throw new ModelSourceException("inspection-limit", "Presupuesto por unidad agotado.");
            var namedSplit = Regex.IsMatch(unit.Files[i].Path, "-[0-9]{5}-of-[0-9]{5}\\.gguf$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            if ((unit.Files.Length > 1 || namedSplit) && (header.SplitIndex != i || header.SplitCount != unit.Files.Length || header.SplitTensorCount is null) ||
                unit.Files.Length == 1 && header.SplitCount > 1)
                throw new ModelSourceException("split-conflict", "Selección y metadatos split contradictorios.");
            headers.Add(header); evidence.Add(ModelSelection.Classify(revision, unit.Files[i], header));
        }
        if (headers.Select(h => h.Version).Distinct().Count() > 1 || headers.Where(h => h.FileTypeId.HasValue).Select(h => h.FileTypeId).Distinct().Count() > 1)
            throw new ModelSourceException("metadata-conflict", "Metadatos GGUF contradictorios entre partes.");
        foreach (var key in new[] { "general.architecture", "general.name", "general.basename", "general.uuid" })
            if (headers.Where(h => h.Hints.ContainsKey(key)).Select(h => h.Hints[key]).Distinct(StringComparer.Ordinal).Count() > 1)
                throw new ModelSourceException("metadata-conflict", "Pista compartida contradictoria: " + key);
        var tensorSum = headers.Aggregate(0m, (sum, h) => sum + h.TensorCount);
        if (headers.Count > 1 && (headers.Select(h => h.SplitTensorCount).Distinct().Count() > 1 ||
            tensorSum != headers[0].SplitTensorCount))
            throw new ModelSourceException("split-conflict", "Cantidad global de tensores contradictoria.");
        return evidence.ToArray();
    }
}
