using Coquena.Contracts;
using Coquena.Storage;

namespace Coquena.Backends;

public sealed record BackendUpdateResult(string Selector, BackendUpdateObservation? Observation,
    bool Persisted, string? Error = null);

// One durable observation per latest filter, independently of fixed selections of its current build.
public sealed class BackendUpdater(Home home, LocalStore? storage = null)
{
    private readonly LocalStore store = storage ?? new(home);
    private static readonly ContractJsonContext Json = ContractJsonContext.Default;
    private BackendRecord[] Backends() => store.Read(DocumentKind.Backends, Json.BackendsDocument,
        ContractValidation.Backends)?.Backends ?? [];
    private BackendUpdateObservation[] Observations() => store.Read(DocumentKind.BackendUpdates,
        Json.BackendUpdatesDocument, ContractValidation.BackendUpdates)?.Updates ?? [];
    private static string Key(BackendRecord backend) => $"{backend.Origin}/{backend.Variant}/{backend.Platform}";
    private static string Key(BackendUpdateObservation update) => $"{update.Origin}/{update.Variant}/{update.Platform}";
    private static bool Latest(BackendRecord b) => b.Ownership == "managed" && b.Origin == "ggml" &&
        Coquena.Configuration.ParameterManager.Official(b.Repository) && b.TracksLatest;
    private static string Fingerprint(BackendUpdateObservation? value) =>
        ContractJson.Write(new BackendUpdatesDocument(1, value is null ? [] : [value]), Json.BackendUpdatesDocument);

    public BackendUpdateObservation? Observation(BackendRecord backend) =>
        Latest(backend) ? Observations().SingleOrDefault(o => Key(o) == Key(backend) &&
            o.BackendId == backend.Id && o.InstalledBuild == backend.Build) : null;

    public async Task<BackendUpdateResult[]> UpdateAsync(string? selector, HardwareContext hardware,
        OfficialBackendSource source, CancellationToken cancellation = default)
    {
        if (hardware.Platform != "win-x64") throw new BackendException("unsupported-platform", "Se requiere Windows x64 nativo.");
        string? variant = null;
        if (selector is not null)
        {
            var normalized = selector.StartsWith("oficial/", StringComparison.Ordinal) ? selector[8..] :
                selector.StartsWith("ggml/", StringComparison.Ordinal) ? selector[5..] : selector;
            var parts = normalized.Split(':');
            if (parts.Length != 2 || parts[1] != "latest" || !BackendCatalog.ValidFamily(parts[0]))
                throw new BackendException("invalid-latest-selector", "Use [ggml/|oficial/]cpu|vulkan|cuda@<mayor>:latest; pins y externos no se consultan.");
            variant = parts[0];
        }
        var snapshot = store.Inspect(() => (Backends: Backends().Where(b => Latest(b) &&
            (variant is null || b.Variant == variant)).OrderBy(Key, StringComparer.Ordinal).ToArray(), Updates: Observations()));
        if (selector is not null && snapshot.Backends.Length == 0)
            throw new BackendException("latest-not-registered", "No existe ese seguimiento latest registrado.");
        List<BackendUpdateResult> results = [];
        foreach (var group in snapshot.Backends.GroupBy(Key))
        {
            cancellation.ThrowIfCancellationRequested();
            var backend = group.First();
            var label = $"ggml/{backend.Variant}:latest";
            if (group.Count() != 1)
            { results.Add(new(label, null, false, "ambiguous-latest")); continue; }
            BackendUpdateObservation observation;
            var expected = Fingerprint(snapshot.Updates.SingleOrDefault(o => Key(o) == group.Key));
            try
            {
                if (backend.Platform != hardware.Platform || !BackendCatalog.ValidFamily(backend.Variant))
                    throw new BackendException("invalid-tracking-filter", "Filtro fuera de las familias/plataforma oficiales.");
                var resolution = await source.ResolveAsync(hardware, backend.Variant, "latest", cancellation);
                var candidate = resolution.Selected ?? throw new BackendException("no-candidate", "Sin candidato dentro del filtro registrado.");
                var newer = BackendCatalog.BuildNumber(candidate.Release) > BackendCatalog.BuildNumber(backend.Build);
                observation = new("ggml", backend.Variant, backend.Platform, backend.Id, backend.Build,
                    DateTimeOffset.UtcNow, newer ? "available" : "unchanged",
                    newer ? "Novedad detectada; instalación intacta." : "Sin build posterior al instalado dentro del filtro consultado.",
                    newer ? new(candidate.Release, candidate.Artifacts.Select(a => new RemoteFile(a.Name, a.Size)).ToArray(),
                        candidate.Compatibility, candidate.Diagnostic) : null);
            }
            catch (BackendException e)
            {
                observation = new("ggml", backend.Variant, backend.Platform, backend.Id, backend.Build,
                    DateTimeOffset.UtcNow, "failed", e.Code, null);
            }
            try
            {
                store.Exclusive(() =>
                {
                    cancellation.ThrowIfCancellationRequested();
                    var current = Backends().Where(b => Latest(b) && Key(b) == group.Key).ToArray();
                    if (current.Length != 1 || current[0].Id != backend.Id || current[0].Build != backend.Build)
                        throw new BackendException("stale-update", "El seguimiento cambió durante la consulta; reintente.");
                    store.UpdateLocked(DocumentKind.BackendUpdates, Json.BackendUpdatesDocument, ContractValidation.BackendUpdates,
                        old =>
                        {
                            var updates = old?.Updates ?? [];
                            if (Fingerprint(updates.SingleOrDefault(o => Key(o) == group.Key)) != expected)
                                throw new BackendException("stale-update", "Otra consulta guardó una observación; reintente.");
                            return new(1, updates.Where(o => Key(o) != group.Key).Append(observation).ToArray());
                        }, cancellation: cancellation);
                    return true;
                });
                results.Add(new(label, observation, true));
            }
            catch (Exception e) when (e is BackendException or StorageException or ContractException or IOException or UnauthorizedAccessException)
            {
                results.Add(new(label, null, false, e switch
                { BackendException b => b.Code, StorageException s => s.Code, ContractException c => c.Code, _ => "update-not-saved" }));
            }
        }
        return results.ToArray();
    }
}
