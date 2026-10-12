using Coquena.Contracts;
using Coquena.Storage;

namespace Coquena.Models;

// Operation paths remain logical (models/repository/file). New catalog paths
// are relative to their stored base; absent bases preserve legacy home paths.
public sealed class ModelPaths(Home home)
{
    public string Root(string? location)
    {
        var result = new Home(Path.GetFullPath(location ?? "models", home.Root)).Root;
        var volume = Path.GetPathRoot(result)!;
        if (!result.Equals(Path.TrimEndingDirectorySeparator(volume), StringComparison.OrdinalIgnoreCase))
            _ = new Home(volume).PathFor(Path.GetRelativePath(volume, result));
        return result;
    }
    public string Resolve(string? location, string logical)
    {
        if (!logical.StartsWith("models/", StringComparison.Ordinal))
            throw new StorageException("unsafe-model-path", "Ruta de modelo fuera de su instalación.");
        return new Home(Root(location)).PathFor(logical[7..]);
    }
    public string File(ModelRecord model, InstalledFile file)
    {
        if (model.Ownership == "external") return Path.GetFullPath(file.LocalPath);
        var logical = $"models/{model.Selection!.Repository}/{file.RemotePath}";
        if (file.LocalPath != (model.InstallationDirectory is null ? logical : logical[7..]))
            throw new StorageException("unsafe-model-path", "Ruta de modelo fuera de su unidad gestionada.");
        return model.InstallationDirectory is null ? home.PathFor(file.LocalPath)
            : new Home(Root(model.InstallationDirectory)).PathFor(file.LocalPath);
    }
    public string Destination(OperationDocument operation, TransferRecord file) => Resolve(operation.ModelInstall?.InstallationDirectory, file.DestinationPath);
}
