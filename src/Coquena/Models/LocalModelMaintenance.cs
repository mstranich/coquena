using Coquena.Contracts;
using Coquena.Storage;

namespace Coquena.Models;

public sealed class LocalModelMaintenance(Home home, Action<int>? afterRead = null)
{
    private readonly LocalStore store = new(home);
    private readonly ModelPaths paths = new(home);
    private static readonly ContractJsonContext Json = ContractJsonContext.Default;
    private ModelsDocument Catalog() => store.Read(DocumentKind.Models, Json.ModelsDocument, ContractValidation.Models) ?? new(1, []);
    private ModelRecord Find(string name, bool companion) => Catalog().Models.SingleOrDefault(m =>
        m.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && companion == (m.CompanionType is not null))
        ?? throw new ModelSourceException("model-not-found", "Unidad no registrada en este catálogo.");

    private ModelRecord Save(ModelRecord original, ModelRecord updated) => store.Exclusive(() =>
    {
        ModelInstaller.CheckUpgradeMutation(home, store, original.Id);
        var catalog = Catalog();
        var current = catalog.Models.SingleOrDefault(m => m.Id == original.Id);
        if (current is null || ContractJson.Write(current, Json.ModelRecord) != ContractJson.Write(original, Json.ModelRecord))
            throw new ModelSourceException("model-changed", "El registro cambió durante la operación; vuelva a intentarlo.");
        store.UpdateLocked(DocumentKind.Models, Json.ModelsDocument, ContractValidation.Models,
            _ => catalog with { Models = catalog.Models.Select(m => m.Id == original.Id ? updated : m).ToArray() });
        return updated;
    });

    public LocalModel Refresh(string name, bool companion = false)
    {
        var original = Find(name, companion);
        var files = original.Files.Select(file =>
        {
            try
            {
                using var stream = new FileStream(paths.File(original, file), FileMode.Open, FileAccess.Read, FileShare.Read);
                return file with { Size = stream.Length, LastWriteUtc = File.GetLastWriteTimeUtc(paths.File(original, file)) };
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return file; }
        }).ToArray();
        Save(original, original with { Files = files, RefreshedAt = DateTimeOffset.UtcNow });
        return new ModelInstaller(home).List().Single(m => m.Record.Id == original.Id);
    }

    public async Task<ModelRecord> VerifyAsync(string name, bool companion = false, bool setBaseline = false,
        CancellationToken cancellation = default)
    {
        var original = Find(name, companion);
        if (setBaseline && original.Ownership != "external")
            throw new ModelSourceException("managed-baseline-rejected", "No se sustituyen las referencias de una descarga gestionada.");
        var checks = new List<LocalFileCheck>();
        var stamps = new List<(long Size, DateTime Date)?>();
        foreach (var file in original.Files)
        {
            cancellation.ThrowIfCancellationRequested();
            var path = paths.File(original, file);
            try
            {
                var info = new FileInfo(path);
                // Exists hides permission/IO errors; use an operation that preserves them.
                _ = File.GetAttributes(path);
                stamps.Add((info.Length, info.LastWriteTimeUtc));
                var result = await FileIntegrity.VerifyAsync(path, setBaseline ? null : file.Integrity, cancellation);
                checks.Add(new(file.LocalPath, result.Status, result.Observed, result.Diagnostic));
            }
            catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
            { stamps.Add(null); checks.Add(new(file.LocalPath, "missing-file", null, "Archivo esperado ausente.")); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { stamps.Add(null); checks.Add(new(file.LocalPath, "read-error", null, e.Message)); }
            afterRead?.Invoke(checks.Count - 1);
        }
        // Earlier parts must still match their observation when completing the whole unit.
        for (var i = 0; i < checks.Count; i++)
        {
            if (checks[i].Observed is null) continue;
            try
            {
                var info = new FileInfo(paths.File(original, original.Files[i]));
                if (stamps[i] is not { } stamp || !info.Exists || info.Length != stamp.Size || info.LastWriteTimeUtc != stamp.Date)
                    checks[i] = checks[i] with { Status = "changed-during-read", Observed = null, Diagnostic = "El conjunto cambió durante la lectura." };
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { checks[i] = checks[i] with { Status = "read-error", Observed = null, Diagnostic = e.Message }; }
        }
        var success = setBaseline && checks.All(c => c.Observed is not null);
        if (success) checks = checks.Select(c => c with { Status = "baseline-set", Diagnostic = null }).ToList();
        var status = new[] { "missing-file", "read-error", "changed-during-read", "mismatch", "no-reference" }
            .FirstOrDefault(s => checks.Any(c => c.Status == s)) ?? (success ? "baseline-set" : "verified");
        var updated = original with { IntegrityCheck = new(DateTimeOffset.UtcNow, status, setBaseline, checks.ToArray()) };
        if (success) updated = updated with { Files = original.Files.Select((f, i) => f with { Integrity = checks[i].Observed }).ToArray(),
            BaselineAt = updated.IntegrityCheck!.CheckedAt };
        cancellation.ThrowIfCancellationRequested();
        return Save(original, updated);
    }
}
