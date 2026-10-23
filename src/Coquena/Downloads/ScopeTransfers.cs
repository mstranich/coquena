using System.IO.Compression;
using System.Text.Json.Nodes;
using Coquena.Backends;
using Coquena.Contracts;
using Coquena.Models;
using Coquena.Projects;
using Coquena.Storage;

namespace Coquena.Downloads;

public enum ScopeTransferStage { Prepared, PhysicalPublished, InventoryPublished }

// A pinned selection enters here. Resource resolution and combination locks are separate callers.
public sealed class ScopeTransfers
{
    public ScopeContext Context { get; }
    public DownloadManager Downloads { get; }
    private readonly Home home;
    private readonly LocalStore checkpoints;
    private readonly ScopeStore inventory;
    private readonly Action<ScopeTransferStage>? fault;
    private static readonly ContractJsonContext Json = ContractJsonContext.Default;

    public ScopeTransfers(ScopeContext context, Action<ScopeTransferStage>? fault = null)
    {
        Context = context;
        home = new Home(context);
        inventory = new ScopeStore(context);
        checkpoints = new LocalStore(home);
        Downloads = new DownloadManager(home, checkpoints);
        this.fault = fault;
    }

    public OperationDocument Create(OperationDocument selection, string origin = "explicit", string[]? requiredFiles = null)
    {
        if (selection.BackendInstall is not null || selection.ModelInstall is not null || selection.ScopePublication is not null ||
            origin is not ("explicit" or "automatic")) Fail("invalid-scoped-selection");
        if (selection.ModelSelection is { } model && (model.Commit.Length != 40 || !model.Commit.All(Uri.IsHexDigit))) Fail("unpinned-selection");
        if (selection.ResourceKind == "backend" && (requiredFiles is null || requiredFiles.Length == 0 ||
            !requiredFiles.Any(p => p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))) Fail("backend-requirements-missing");
        var expected = selection.ModelSelection?.Files ?? selection.BackendSelection?.Artifacts ?? [];
        if (expected.Length != selection.Files.Length || expected.Length == 0) Fail("invalid-scoped-selection");
        for (var i = 0; i < expected.Length; i++)
        {
            Safe(expected[i].Path);
            var f = selection.Files[i];
            if (f.SourcePath != expected[i].Path || f.ExpectedSize != expected[i].Size || f.Identity != expected[i].Identity ||
                f.PartialPath != $"downloads/{selection.Id:D}/files/{i}.part") Fail("invalid-scoped-selection");
        }
        foreach (var path in requiredFiles ?? []) Safe(path);
        var snapshot = inventory.Read();
        var resources = snapshot.Documents["local/inventory.json"]["data"]!["resources"]!.AsArray();
        var matches = resources.Where(r => r!["logical-key"]!.GetValue<string>() == selection.Name &&
            r["kind"]!.GetValue<string>() == selection.ResourceKind).ToArray();
        if (matches.Length > 1 || matches.Any(r => r!["ownership"]!.GetValue<string>() != "managed")) Fail("resource-ownership-conflict");
        var resourceId = matches.Length == 0 ? Guid.NewGuid() : Guid.Parse(matches[0]!["resource-id"]!.GetValue<string>());
        var installationId = Guid.NewGuid();
        var prefix = $"{(selection.ResourceKind == "backend" ? "backends" : "models")}/installations/{installationId:D}";
        return Downloads.Create(selection with { ScopePublication = new(resourceId, installationId, origin, requiredFiles ?? []),
            Files = selection.Files.Select(f => f with { DestinationPath = prefix + "/" + f.SourcePath }).ToArray() });
    }

    public Task<OperationDocument?> RunAsync(Guid id, IDownloadTransport transport,
        Action<OperationDocument>? progress = null, CancellationToken cancellation = default,
        Func<OperationDocument, int, CancellationToken, Task<bool>>? restart = null)
        => Downloads.RunAsync(id, transport, restart, progress, cancellation, PublishAsync, BeforeCancel);

    public Task ControlAsync(Guid id, bool cancel, CancellationToken cancellation = default)
        => Downloads.ControlAsync(id, cancel, cancellation, BeforeCancel);

    public bool Published(OperationDocument operation) => PublishedInstallation(operation) is not null;

    private string Area(OperationDocument operation) => $"downloads/{operation.Id:D}";
    private string Destination(OperationDocument operation) =>
        $"{(operation.ResourceKind == "backend" ? "backends" : "models")}/installations/{operation.ScopePublication!.InstallationId:D}";
    private void Safe(string path)
    {
        try { _ = Context.PathFor("tmp/" + path); }
        catch (StorageException e) when (e.Code == "unsafe-path")
        { throw new DownloadException("unsafe-path", "Ruta de preparación no autorizada."); }
    }
    private static void Fail(string code) => throw new DownloadException(code, "Preparación del ámbito no confirmada; se conserva evidencia.");
    private OperationDocument Save(OperationDocument operation) => checkpoints.Update(DocumentKind.Operation, Json.OperationDocument,
        ContractValidation.Operation, _ => operation, operation: operation.Id);

    private async Task<OperationDocument?> PublishAsync(OperationDocument operation, CancellationToken cancellation)
    {
        if (operation.ScopePublication is null || operation.ScopePublication.ResourceId == Guid.Empty ||
            operation.ScopePublication.InstallationId == Guid.Empty) Fail("invalid-scoped-selection");
        if (operation.Phase == "cleanup")
        {
            var installed = PublishedInstallation(operation);
            if (installed is null) Fail("published-installation-missing");
            await VerifyTree(Context.PathFor(Destination(operation)), installed!["files"]!.AsArray(), cancellation);
            Downloads.Cleanup(operation, installed: true);
            return null;
        }
        var area = Area(operation);
        var manifestPath = Context.PathFor(area + "/publication.json");
        var prepared = Context.PathFor(area + "/prepared");
        var destination = Context.PathFor(Destination(operation));
        JsonObject manifest;
        if (!File.Exists(manifestPath))
        {
            if (Directory.Exists(destination)) Fail("destination-collision");
            // Only this executor owns its preparation; a failed extraction can be retried from verified inputs.
            if (Directory.Exists(prepared)) DeletePreparation(prepared);
            Directory.CreateDirectory(prepared);
            var extraction = new ExtractionBudget();
            foreach (var file in operation.Files)
            {
                var partial = Context.PathFor(file.PartialPath);
                using var sourceGuard = new FileStream(partial, FileMode.Open, FileAccess.Read, FileShare.Read);
                var check = await FileIntegrity.VerifyAsync(partial, file.LocalIntegrity, cancellation);
                if (file.State != "transferred" || check.Status != "verified" ||
                    FileIntegrity.Compare(check.Observed!, file.Identity).Status == "mismatch") Fail("partial-integrity-failed");
                if (operation.ResourceKind == "backend") await Extract(partial, prepared, extraction, cancellation);
                else
                {
                    var target = Child(prepared, file.SourcePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    await Copy(partial, target, cancellation);
                }
            }
            if (operation.ResourceKind is "model" or "companion")
            {
                var unit = GgufInspection.InspectPaths(operation.Files.Select(f => Child(prepared, f.SourcePath)).ToArray(), cancellation);
                if (unit.Status != "complete") Fail("invalid-gguf-unit");
            }
            foreach (var required in operation.ScopePublication!.RequiredFiles)
                if (!File.Exists(Child(prepared, required))) Fail("backend-required-file-missing");
            var files = new JsonArray();
            foreach (var path in Directory.GetFiles(prepared, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                var observed = await FileIntegrity.VerifyAsync(path, null, cancellation);
                if (observed.Observed is null) Fail("prepared-integrity-failed");
                files.Add((JsonNode)new JsonObject { ["path"] = Path.GetRelativePath(prepared, path).Replace('\\', '/'),
                    ["size"] = new FileInfo(path).Length, ["integrity"] = Identity(observed.Observed!) });
            }
            var physical = PhysicalDirectory.Read(prepared);
            manifest = ProjectDocuments.Create("operation", 1, new JsonObject
            {
                ["operation-id"] = operation.Id.ToString("D"), ["scope-id"] = inventory.Read().ScopeId,
                ["selection"] = Selection(operation), ["destination"] = Destination(operation), ["files"] = files,
                ["volume"] = physical.Volume, ["file-id"] = physical.FileId
            });
            ScopeStore.Replace(manifestPath, ProjectDocuments.Bytes(manifest));
            fault?.Invoke(ScopeTransferStage.Prepared);
        }
        else manifest = ReadManifest(manifestPath);
        var data = manifest["data"]!;
        if (data["operation-id"]?.GetValue<string>() != operation.Id.ToString("D") ||
            data["scope-id"]?.GetValue<string>() != inventory.Read().ScopeId ||
            data["destination"]?.GetValue<string>() != Destination(operation) ||
            !JsonNode.DeepEquals(data["selection"], Selection(operation))) Fail("publication-manifest-conflict");
        operation = Save(operation with { Phase = "publishing" });
        var actualDirectory = PhysicalDirectory.Read(Directory.Exists(destination) ? destination : prepared);
        if (data["volume"]?.GetValue<string>() != actualDirectory.Volume ||
            data["file-id"]?.GetValue<string>() != actualDirectory.FileId) Fail("publication-directory-conflict");
        if (!Directory.Exists(destination))
        {
            await VerifyTree(prepared, data["files"]!.AsArray(), cancellation);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            // A slot is unique and never replaces an earlier revision or an external destination.
            for (var attempt = 0; ; attempt++)
            {
                try { Directory.Move(prepared, destination); break; }
                catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33 && attempt < 2)
                { await Task.Delay(50 * (attempt + 1), cancellation); }
            }
            fault?.Invoke(ScopeTransferStage.PhysicalPublished);
        }
        var guards = new List<FileStream>();
        try
        {
            await VerifyTree(destination, data["files"]!.AsArray(), cancellation, guards);
            PublishInventory(operation, data["files"]!.AsArray());
        }
        finally { foreach (var guard in guards) guard.Dispose(); }
        fault?.Invoke(ScopeTransferStage.InventoryPublished);
        operation = Save(operation with { State = "completed", Phase = "cleanup" });
        // Reuse the transfer engine's owned cleanup while its executor lease is still held.
        await BeforeCancel(operation);
        Downloads.Cleanup(operation, installed: true);
        return null;
    }

    private void PublishInventory(OperationDocument operation, JsonArray files)
    {
        var snapshot = inventory.Read();
        var docs = snapshot.Documents.ToDictionary(p => p.Key, p => (JsonObject)p.Value.DeepClone());
        var document = docs["local/inventory.json"];
        var resources = document["data"]!["resources"]!.AsArray();
        var plan = operation.ScopePublication!;
        var resource = resources.SingleOrDefault(r => r!["resource-id"]!.GetValue<string>() == plan.ResourceId.ToString("D"));
        if (resources.Any(r => r != resource && r!["logical-key"]!.GetValue<string>() == operation.Name && r["kind"]!.GetValue<string>() == operation.ResourceKind))
            Fail("resource-ownership-conflict");
        if (resource is null)
        {
            resource = new JsonObject { ["resource-id"] = plan.ResourceId.ToString("D"), ["logical-key"] = operation.Name,
                ["kind"] = operation.ResourceKind, ["ownership"] = "managed", ["installations"] = new JsonArray() };
            resources.Add(resource);
        }
        if (resource["ownership"]!.GetValue<string>() != "managed" || resource["logical-key"]!.GetValue<string>() != operation.Name ||
            resource["kind"]!.GetValue<string>() != operation.ResourceKind) Fail("resource-ownership-conflict");
        var installations = resource["installations"]!.AsArray();
        var installation = new JsonObject { ["installation-id"] = plan.InstallationId.ToString("D"), ["operation-id"] = operation.Id.ToString("D"),
            ["location"] = Destination(operation), ["origin"] = plan.Origin, ["availability"] = "available",
            ["selection"] = Selection(operation), ["files"] = files.DeepClone() };
        var existing = installations.SingleOrDefault(i => i!["installation-id"]!.GetValue<string>() == plan.InstallationId.ToString("D"));
        if (existing is not null)
        {
            if (!JsonNode.DeepEquals(existing, installation)) Fail("installation-conflict");
            return;
        }
        installations.Add((JsonNode)installation);
        // Every document advances with the complete generation required by ScopeStore.
        foreach (var doc in docs.Values) doc["revision"] = checked(doc["revision"]!.GetValue<long>() + 1);
        inventory.Commit(snapshot.Revision, docs);
    }

    private async Task<bool> BeforeCancel(OperationDocument operation)
    {
        var destination = Context.PathFor(Destination(operation));
        var published = PublishedInstallation(operation) is not null;
        if (Directory.Exists(destination) && !published)
        {
            // A physical slot pending inventory commit is preparation; prove its manifest before removal.
            var path = Context.PathFor(Area(operation) + "/publication.json");
            if (!File.Exists(path)) Fail("publication-manifest-missing");
            var data = ReadManifest(path)["data"]!;
            var physical = PhysicalDirectory.Read(destination);
            if (data["destination"]?.GetValue<string>() != Destination(operation) ||
                data["scope-id"]?.GetValue<string>() != inventory.Read().ScopeId ||
                data["operation-id"]?.GetValue<string>() != operation.Id.ToString("D") ||
                data["volume"]?.GetValue<string>() != physical.Volume || data["file-id"]?.GetValue<string>() != physical.FileId ||
                !JsonNode.DeepEquals(data["selection"], Selection(operation))) Fail("publication-manifest-conflict");
            await VerifyTree(destination, data["files"]!.AsArray(), CancellationToken.None);
            DeletePreparation(destination);
        }
        return false;
    }

    private JsonNode? PublishedInstallation(OperationDocument operation)
    {
        foreach (var resource in inventory.Read().Documents["local/inventory.json"]["data"]!["resources"]!.AsArray())
            foreach (var installation in resource!["installations"]!.AsArray())
                if (installation!["operation-id"]?.GetValue<string>() == operation.Id.ToString("D"))
                {
                    if (resource["resource-id"]!.GetValue<string>() != operation.ScopePublication!.ResourceId.ToString("D") ||
                        installation["installation-id"]?.GetValue<string>() != operation.ScopePublication.InstallationId.ToString("D") ||
                        installation["location"]?.GetValue<string>() != Destination(operation) ||
                        !JsonNode.DeepEquals(installation["selection"], Selection(operation))) Fail("installation-conflict");
                    return installation;
                }
        return null;
    }

    private string Child(string root, string relative)
    {
        Safe(relative);
        return Context.PathFor(Path.GetRelativePath(Context.Root, root).Replace('\\', '/') + "/" + relative);
    }
    private JsonObject ReadManifest(string path)
    {
        try
        {
            var document = ProjectDocuments.Read(File.ReadAllBytes(path), "operation");
            var data = document["data"]!;
            ProjectDocuments.Uuid(data["scope-id"]);
            foreach (var key in new[] { "destination", "volume", "file-id" })
                if (string.IsNullOrEmpty(data[key]?.GetValue<string>())) ProjectDocuments.Invalid();
            if (data["selection"] is not JsonObject || data["files"] is not JsonArray { Count: > 0 }) ProjectDocuments.Invalid();
            foreach (var file in data["files"]!.AsArray())
            {
                Safe(file!["path"]!.GetValue<string>());
                if (file["size"]?.GetValue<long>() is not >= 0) ProjectDocuments.Invalid();
                var hash = file["integrity"]!;
                if (!FileIntegrity.Comparable(new(hash["algorithm"]!.GetValue<string>(), hash["domain"]!.GetValue<string>(), hash["value"]!.GetValue<string>()))) ProjectDocuments.Invalid();
            }
            return document;
        }
        catch (Exception e) when (e is InvalidOperationException or NullReferenceException or FormatException)
        { throw new StorageException("invalid-project-document", "Manifiesto de preparación inválido; se conserva.", inner: e); }
    }
    private static JsonObject Identity(ContentIdentity identity) => new()
    { ["algorithm"] = identity.Algorithm, ["domain"] = identity.Domain, ["value"] = identity.Value };
    private static JsonNode Selection(OperationDocument operation)
    {
        var node = JsonNode.Parse(ContractJson.Write(operation, Json.OperationDocument))!.AsObject();
        foreach (var key in new[] { "files", "state", "phase", "updatedAt", "diagnostic", "executor" }) node.Remove(key);
        return node;
    }
    private async Task VerifyTree(string root, JsonArray files, CancellationToken cancellation, List<FileStream>? guards = null)
    {
        if (!Directory.Exists(root) || files.Count == 0) Fail("prepared-integrity-failed");
        var actual = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
        if (actual.Length != files.Count) Fail("prepared-integrity-failed");
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var path = Child(root, file!["path"]!.GetValue<string>());
            if (guards is not null) guards.Add(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read));
            if (!seen.Add(path) || !File.Exists(path) || new FileInfo(path).Length != file["size"]!.GetValue<long>()) Fail("prepared-integrity-failed");
            var hash = file["integrity"]!;
            var result = await FileIntegrity.VerifyAsync(path, new(hash["algorithm"]!.GetValue<string>(), hash["domain"]!.GetValue<string>(), hash["value"]!.GetValue<string>()), cancellation);
            if (result.Status != "verified") Fail("prepared-integrity-failed");
        }
    }
    private void DeletePreparation(string root)
    {
        _ = Context.PathFor(Path.GetRelativePath(Context.Root, root).Replace('\\', '/'));
        var entries = Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories);
        foreach (var entry in entries) _ = Context.PathFor(Path.GetRelativePath(Context.Root, entry).Replace('\\', '/'));
        foreach (var entry in entries.Where(File.Exists)) File.Delete(entry);
        foreach (var entry in entries.Where(Directory.Exists).OrderByDescending(p => p.Length)) Directory.Delete(entry);
        Directory.Delete(root);
    }
    private static async Task Copy(string source, string destination, CancellationToken cancellation)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await input.CopyToAsync(output, cancellation);
        output.Flush(true);
    }
    private sealed class ExtractionBudget { public int Entries; public long Bytes; }
    private async Task Extract(string archivePath, string prepared, ExtractionBudget budget, CancellationToken cancellation)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > BackendInstaller.MaxEntries - budget.Entries) Fail("extraction-limit");
        budget.Entries += archive.Entries.Count;
        foreach (var entry in archive.Entries)
        {
            cancellation.ThrowIfCancellationRequested();
            if (entry.Length > BackendInstaller.MaxExtractedBytes - budget.Bytes ||
                (entry.ExternalAttributes >> 16 & 0xf000) == 0xa000) Fail("unsafe-archive");
            budget.Bytes += entry.Length;
            var relative = entry.FullName.TrimEnd('/');
            var target = Child(prepared, relative);
            if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(target); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await using var input = entry.Open();
            await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[65536]; long written = 0;
            while (true)
            {
                var read = await input.ReadAsync(buffer, cancellation);
                if (read == 0) break;
                written += read;
                if (written > entry.Length) Fail("extraction-limit");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellation);
            }
            if (written != entry.Length) Fail("invalid-archive");
            output.Flush(true);
        }
    }
}
