using Coquena.Contracts;
using Coquena.Storage;

namespace Coquena.Models;

public sealed class ExternalModels(Home home)
{
    public void SaveMatch(ModelRecord original, InformativeMatch match)
    {
        var store = new LocalStore(home);
        store.Update(DocumentKind.Models, ContractJsonContext.Default.ModelsDocument, ContractValidation.Models, catalog =>
        {
            ModelInstaller.CheckUpgradeMutation(home, store, original.Id);
            var current = catalog?.Models.SingleOrDefault(m => m.Id == original.Id);
            if (current is null || ContractJson.Write(current, ContractJsonContext.Default.ModelRecord) !=
                ContractJson.Write(original, ContractJsonContext.Default.ModelRecord))
                throw new ModelSourceException("model-changed", "El registro cambió durante la selección; no se guarda equivalencia.");
            return catalog! with { Models = catalog.Models.Select(m => m.Id == original.Id ? m with { InformativeMatch = match } : m).ToArray() };
        });
    }
    public ModelRecord Add(string path, string name, bool companion = false)
    {
        var request = ModelRequest.Parse(name);
        if (request.Quantization is null || companion != (request.CompanionType is not null))
            throw new ModelSourceException("invalid-external-name", "Indicar nombre canónico con cuantización y tipo sólo para companions.");
        if (!path.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
            throw new ModelSourceException("invalid-external-path", "Indicar un archivo GGUF o su primer shard.");
        var unit = GgufInspection.Inspect(path);
        if (unit.Status is "invalid" or "unsupported")
            throw new ModelSourceException("external-gguf-" + unit.Status, string.Join("; ", unit.Diagnostics));
        var files = unit.Expected.Select(p =>
        {
            try
            {
                var info = new FileInfo(p);
                return info.Exists ? new InstalledFile("", p, info.Length, LastWriteUtc: info.LastWriteTimeUtc)
                    : new InstalledFile("", p, 0);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            { return new InstalledFile("", p, 0); }
        }).ToArray();
        var record = new ModelRecord(Guid.NewGuid(), name, null, files, [], Ownership: "external", CompanionType: request.CompanionType);
        var store = new LocalStore(home);
        var paths = new ModelPaths(home);
        return store.Exclusive(() =>
        {
            // The same lease coordinates registrations, publication and download reservations.
            var catalog = store.Read(DocumentKind.Models, ContractJsonContext.Default.ModelsDocument, ContractValidation.Models) ?? new(1, []);
            if (catalog.Models.Any(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                m.Files.Any(f => files.Any(n => paths.File(m, f).Equals(n.LocalPath, StringComparison.OrdinalIgnoreCase)))))
                throw new ModelSourceException("model-collision", "Nombre o rutas ya registrados.");
            foreach (var directory in Directory.GetDirectories(home.PathFor("downloads")))
            {
                Home.CheckLinks(directory);
                if (!Guid.TryParseExact(Path.GetFileName(directory), "D", out var id))
                    throw new ModelSourceException("invalid-operation-directory", "Directorio de operación desconocido.");
                var op = store.Read(DocumentKind.Operation, ContractJsonContext.Default.OperationDocument, ContractValidation.Operation, operation: id);
                if (op is null) throw new ModelSourceException("orphan-operation", "Operación sin plan.");
                if ((op.ResourceKind is "model" or "companion") && (op.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    op.Files.Any(f => files.Any(n => paths.Destination(op, f).Equals(n.LocalPath, StringComparison.OrdinalIgnoreCase)))))
                    throw new ModelSourceException("model-reserved", "Nombre o rutas reservados por una descarga.");
            }
            store.UpdateLocked(DocumentKind.Models, ContractJsonContext.Default.ModelsDocument, ContractValidation.Models,
                _ => catalog with { Models = catalog.Models.Append(record).ToArray() });
            return record;
        });
    }
}
