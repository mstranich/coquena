using Coquena.Contracts;
using Coquena.Storage;
using Coquena.Downloads;

namespace Coquena.Backends;

public sealed record BackendUpgradeResult(string Selector, string State, Guid? OperationId = null, string? Diagnostic = null);

public sealed class BackendUpgrader(Home home, LocalStore? storage = null, BackendInstaller? consumer = null)
{
    private readonly LocalStore store = storage ?? new(home);
    private static readonly ContractJsonContext Json = ContractJsonContext.Default;
    private BackendRecord[] Records() => store.Read(DocumentKind.Backends, Json.BackendsDocument, ContractValidation.Backends)?.Backends ?? [];
    internal static void CheckObservation(LocalStore store, BackendUpdateObservation expected)
    {
        var actual = store.Read(DocumentKind.BackendUpdates, Json.BackendUpdatesDocument, ContractValidation.BackendUpdates)?.Updates
            .SingleOrDefault(o => o.Origin == expected.Origin && o.Variant == expected.Variant && o.Platform == expected.Platform);
        string Write(BackendUpdateObservation? o) => ContractJson.Write(new BackendUpdatesDocument(1, o is null ? [] : [o]), Json.BackendUpdatesDocument);
        if (Write(actual) != Write(expected)) throw new DownloadException("stale-upgrade-plan", "La observación cambió; consulte update antes de reintentar.");
    }
    public async Task<BackendUpgradeResult[]> UpgradeAsync(string? selector,
        Func<OperationDocument, CancellationToken, Task<bool>> run, Action<string>? report = null, CancellationToken cancellation = default)
    {
        string? variant = null;
        if (selector is not null)
        {
            var value = selector.StartsWith("oficial/", StringComparison.Ordinal) ? selector[8..] : selector.StartsWith("ggml/", StringComparison.Ordinal) ? selector[5..] : selector;
            var parts = value.Split(':');
            if (parts.Length != 2 || parts[1] != "latest" || !BackendCatalog.ValidFamily(parts[0]))
                throw new BackendException("invalid-latest-selector", "Use un selector oficial latest.");
            variant = parts[0];
        }
        var snapshot = store.Inspect(() => (Records: Records().Where(b => b.Ownership == "managed" && b.Origin == "ggml" &&
            Coquena.Configuration.ParameterManager.Official(b.Repository) && b.TracksLatest && (variant is null || variant == b.Variant)).ToArray(),
            Updates: store.Read(DocumentKind.BackendUpdates, Json.BackendUpdatesDocument, ContractValidation.BackendUpdates)?.Updates ?? []));
        if (selector is not null && snapshot.Records.Length == 0) throw new BackendException("latest-not-registered", "Seguimiento ausente.");
        var installer = consumer ?? new BackendInstaller(home, store, retentionReport: report);
        List<BackendUpgradeResult> results = [];
        foreach (var group in snapshot.Records.GroupBy(b => (b.Origin, b.Variant, b.Platform)).OrderBy(g => g.Key.Variant, StringComparer.Ordinal))
        {
            cancellation.ThrowIfCancellationRequested();
            var baseline = group.First(); var label = $"ggml/{baseline.Variant}:latest";
            Guid? operationId = null;
            try
            {
                if (group.Count() != 1) throw new BackendException("ambiguous-latest", "Seguimiento ambiguo.");
                if (baseline.Platform != "win-x64" || !BackendCatalog.ValidFamily(baseline.Variant))
                    throw new BackendException("invalid-tracking-filter", "Filtro fuera de las familias/plataforma oficiales.");
                var observation = snapshot.Updates.SingleOrDefault(o => o.Origin == baseline.Origin && o.Variant == baseline.Variant && o.Platform == baseline.Platform);
                if (observation is null || observation.BackendId != baseline.Id || observation.InstalledBuild != baseline.Build)
                    throw new BackendException("update-required", "Consulte update para el build actual.");
                if (observation.State == "unchanged") { results.Add(new(label, "unchanged")); continue; }
                if (observation.Candidate is not { } candidate || observation.State != "available")
                    throw new BackendException("update-required", "La consulta no tiene candidato aplicable.");
                if (BackendCatalog.BuildNumber(candidate.Build) <= BackendCatalog.BuildNumber(baseline.Build))
                    throw new BackendException("invalid-upgrade", "El candidato debe ser posterior.");
                var previousTarget = store.Inspect(() => Records().SingleOrDefault(b => b.Ownership == "managed" && b.Origin == baseline.Origin &&
                    b.Repository == baseline.Repository && b.Variant == baseline.Variant && b.Platform == baseline.Platform && b.Build == candidate.Build));
                var targetPath = previousTarget is null ? null : new BackendPaths(home).Server(previousTarget);
                var targetInfo = targetPath is null ? null : new FileInfo(targetPath);
                var targetLength = targetInfo?.Length;
                var targetWrite = targetInfo?.LastWriteTimeUtc;
                if (previousTarget is not null) await installer.ValidateInstalledAsync(previousTarget, cancellation);
                var operation = store.Exclusive(() =>
                {
                    cancellation.ThrowIfCancellationRequested();
                    CheckObservation(store, observation);
                    var records = Records();
                    if (records.Any(b => b.RemovalPending && (b.Id == baseline.Id || b.Id == previousTarget?.Id)))
                        throw new StorageException("removal-pending", "Reintente uninstall antes de upgrade.");
                    if (records.SingleOrDefault(b => b.Id == baseline.Id) != baseline || records.Count(b => b.TracksLatest && b.Origin == baseline.Origin &&
                        b.Variant == baseline.Variant && b.Platform == baseline.Platform) != 1) throw new DownloadException("stale-latest-plan", "El seguimiento cambió.");
                    if (new DownloadManager(home, store).SnapshotLocked().Any(o => o.BackendInstall?.Upgrade is { } u && u.Origin == baseline.Origin &&
                        u.Variant == baseline.Variant && u.Platform == baseline.Platform && o.State != "cancelled"))
                        throw new DownloadException("upgrade-pending", "Reanude o cancele la operación existente.");
                    if (previousTarget is not null)
                    {
                        if (records.SingleOrDefault(b => b.Id == previousTarget.Id) != previousTarget) throw new DownloadException("stale-install-plan", "La instalación cambió.");
                        var currentFile = new FileInfo(targetPath!);
                        if (!currentFile.Exists || currentFile.Length != targetLength || currentFile.LastWriteTimeUtc != targetWrite)
                            throw new DownloadException("version-changed", "El ejecutable cambió durante la validación.");
                        BackendPaths.CheckTree(new BackendPaths(home).Slot(previousTarget));
                        store.UpdateLocked(DocumentKind.Backends, Json.BackendsDocument, ContractValidation.Backends,
                            old => old! with { Backends = old.Backends.Select(b => b.Id == previousTarget.Id ? b with { TracksLatest = true } :
                                b.Id == baseline.Id ? b with { TracksLatest = false } : b).ToArray() });
                        return null;
                    }
                    var selected = new BackendCandidate(candidate.Build, baseline.Variant, baseline.Platform, candidate.Compatibility, candidate.Diagnostic,
                        candidate.Artifacts.Select(a => new ReleaseAsset(a.Path, a.Size!.Value, "")).ToArray(), true);
                    return installer.CreateLocked(selected, "latest", observation);
                });
                if (operation is null)
                {
                    report?.Invoke($"{label}: reutilizado {candidate.Build}; pin e instancias conservados.");
                    new BackendBuildRetention(home, store).Apply(baseline.Repository, baseline.Family, baseline.Platform, report);
                    results.Add(new(label, "reused"));
                }
                else
                {
                    operationId = operation.Id;
                    results.Add(new(label, await run(operation, cancellation) ? "installed" : "pending", operation.Id));
                }
            }
            catch (Exception e) when (e is BackendException or DownloadException or StorageException or ContractException or IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                var published = Records().Any(b => b.TracksLatest && b.Origin == baseline.Origin && b.Variant == baseline.Variant &&
                    b.Platform == baseline.Platform && b.Id != baseline.Id);
                results.Add(new(label, "failed", operationId, published ? "upgrade-published-cleanup-pending" : e switch { BackendException b => b.Code, DownloadException d => d.Code, StorageException s => s.Code,
                    ContractException c => c.Code, _ => "upgrade-not-confirmed" }));
            }
        }
        return results.ToArray();
    }
}
