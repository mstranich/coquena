using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Coquena.Storage;

namespace Coquena.Projects;

public sealed record ScopeSnapshot(long Revision, string? ScopeId, JsonObject? State, IReadOnlyDictionary<string, JsonObject> Documents);
public sealed record ScopeChange(ScopeStore Store, long ExpectedRevision, IReadOnlyDictionary<string, JsonObject> Documents, string? DefaultServiceId = null, Action? Validate = null);
public enum ScopeWriteStage { Prepared, Decided, ParticipantPublished, ProjectionsPublished }

// A generation is immutable; state.json is the only publication pointer. Cross-scope
// commits have one durable decision, with pending markers checked by every reader.
public sealed class ScopeStore(ScopeContext context)
{
    public ScopeContext Context { get; } = context;
    internal T Locked<T>(Func<T> action) => WithLocks([this], action);
    private string StatePath => Context.PathFor("local/state.json");
    private string PendingPath => Context.PathFor("local/pending.json");
    private static StorageException Conflict() => new("scope-conflict", "Cambió el ámbito; releer antes de volver a preparar.");

    public ScopeSnapshot Read()
    {
        if (PhysicalDirectory.Read(Context.Base) != Context.PhysicalBase) throw new StorageException("scope-moved", "Cambió la identidad física de la base.");
        if (!Directory.Exists(Context.Root)) return Empty();
        try { return WithLocks([this], () => ReadLocked()); }
        catch (StorageException error) when (error.Code == "scope-copy-detected")
        {
            // A demonstrable copy gets a fresh scope and empty ownership tables.
            // Its old pointer is retained by the operation and immutable generation.
            CommitCore([new(this, -1, new Dictionary<string, JsonObject>
            {
                ["local/recipes.json"] = ProjectDocuments.Create("recipe-registry", 1, new JsonObject { ["recipes"] = new JsonArray() }),
                ["local/inventory.json"] = ProjectDocuments.Create("inventory", 1, new JsonObject { ["resources"] = new JsonArray() })
            })], null, detachCopy: true);
            return WithLocks([this], () => ReadLocked());
        }
    }

    public void Commit(long expectedRevision, IReadOnlyDictionary<string, JsonObject> documents,
        Action<ScopeWriteStage>? fault = null) => CommitTogether([new(this, expectedRevision, documents)], fault);

    public static void CommitTogether(IReadOnlyList<ScopeChange> changes, Action<ScopeWriteStage>? fault = null)
        => CommitCore(changes, fault, false);

    private static void CommitCore(IReadOnlyList<ScopeChange> changes, Action<ScopeWriteStage>? fault, bool detachCopy)
    {
        if (changes.Count == 0) throw new ArgumentException("Se requiere un ámbito.");
        WithLocks(changes.Select(x => x.Store).ToArray(), () =>
        {
            var tx = ProjectDocuments.Id();
            var participants = new JsonArray();
            foreach (var change in changes)
            {
                var store = change.Store;
                var old = store.ReadLocked(detachCopy);
                if (!detachCopy && old.Revision != change.ExpectedRevision) throw Conflict();
                change.Validate?.Invoke();
                var generation = ProjectDocuments.Id();
                var scopeId = old.ScopeId ?? ProjectDocuments.Id();
                var entries = new JsonObject();
                foreach (var (path, document) in change.Documents)
                {
                    ValidateDocumentPath(path);
                    var kind = document["kind"]!.GetValue<string>();
                    var expectedKind = path switch
                    {
                        "local/inventory.json" => "inventory", "local/recipes.json" => "recipe-registry",
                        "local/active.json" => "active", "local/last-good.json" => "last-good",
                        "local/update-plan.json" => "update-plan", _ => IsResolutionPath(path) ? "resolution" : "combination-index"
                    };
                    if (kind != expectedKind) ProjectDocuments.Invalid();
                    if (kind == "inventory") document["data"]!["scope-id"] = scopeId;
                    var bytes = ProjectDocuments.Bytes(document);
                    ProjectDocuments.Read(bytes, kind);
                    if (old.Documents.TryGetValue(path, out var previous) &&
                        document["revision"]!.GetValue<long>() != checked(previous["revision"]!.GetValue<long>() + 1)) throw Conflict();
                    if (!old.Documents.ContainsKey(path) && document["revision"]!.GetValue<long>() != 1) throw Conflict();
                    entries[path] = new JsonObject { ["kind"] = kind, ["revision"] = document["revision"]!.DeepClone(), ["sha256"] = Hash(bytes) };
                }
                // The caller supplies a complete next generation, so omission cannot
                // silently discard evidence or protections from a previous generation.
                if (old.Documents.Keys.Any(path => !change.Documents.ContainsKey(path)))
                    throw new StorageException("missing-generation-document", "La generación no conserva todos los documentos anteriores.");
                var data = new JsonObject
                {
                    ["scope-id"] = scopeId, ["generation"] = generation, ["transaction-id"] = tx,
                    ["base-volume"] = store.Context.PhysicalBase.Volume, ["base-file-id"] = store.Context.PhysicalBase.FileId,
                    ["root-volume"] = store.Context.PhysicalRoot.Volume, ["root-file-id"] = store.Context.PhysicalRoot.FileId,
                    ["documents"] = entries
                };
                if (store.Context.IsGlobal)
                {
                    data["services"] = change.Documents.TryGetValue("local/recipes.json", out var registry)
                        ? new JsonArray(registry["data"]!["recipes"]!.AsArray().SelectMany(r => r!["entities"]?.AsArray() ?? new JsonArray())
                            .Where(e => e!["kind"]!.GetValue<string>() == "service").Select(e => e!.DeepClone()).ToArray()) : new JsonArray();
                    data["aliases"] = old.State?["data"]?["aliases"]?.DeepClone() ?? new JsonObject();
                    if (change.DefaultServiceId is not null)
                    {
                        ProjectDocuments.Uuid(JsonValue.Create(change.DefaultServiceId));
                        if (!data["services"]!.AsArray().Any(s => s!["id"]!.GetValue<string>() == change.DefaultServiceId))
                            throw new StorageException("unknown-service", "El alias requiere un servicio global registrado.");
                        data["aliases"]!["default"] = change.DefaultServiceId;
                    }
                }
                else if (change.DefaultServiceId is not null) throw new StorageException("global-alias-required", "Default pertenece al ámbito global.");
                var state = ProjectDocuments.Create("state", checked(old.Revision + 1), data);
                foreach (var (path, document) in change.Documents)
                    WriteNew(store.Context.PathFor($"local/generations/{generation}/{path}"), ProjectDocuments.Bytes(document));
                WriteNew(store.Context.PathFor($"local/generations/{generation}/state.json"), ProjectDocuments.Bytes(state));
                participants.Add((JsonNode)new JsonObject
                {
                    ["base"] = store.Context.Base, ["root"] = store.Context.Root, ["global"] = store.Context.IsGlobal,
                    ["volume"] = store.Context.PhysicalRoot.Volume, ["file-id"] = store.Context.PhysicalRoot.FileId,
                    ["previous"] = old.State?.DeepClone(), ["next"] = state
                });
            }
            var coordinator = changes[0].Store;
            var manifestPath = coordinator.Context.PathFor($"local/operations/{tx}/operation.json");
            var manifest = ProjectDocuments.Create("operation", 1, new JsonObject
            {
                ["operation-id"] = tx, ["type"] = "scope-publication", ["phase"] = "prepared",
                ["participants"] = participants
            });
            WriteNew(manifestPath, ProjectDocuments.Bytes(manifest));
            var marker = ProjectDocuments.Create("operation", 1, new JsonObject
            {
                ["operation-id"] = tx, ["coordinator"] = manifestPath,
                ["manifest-sha256"] = Hash(ProjectDocuments.Bytes(manifest))
            });
            foreach (var change in changes) Replace(change.Store.PendingPath, ProjectDocuments.Bytes(marker));
            fault?.Invoke(ScopeWriteStage.Prepared);
            // Immutable decision is written only once, after every pending marker.
            WriteNew(Path.Combine(Path.GetDirectoryName(manifestPath)!, "commit.json"), ProjectDocuments.Bytes(Decision(marker, "committed", 2)));
            try
            {
                fault?.Invoke(ScopeWriteStage.Decided);
                Complete(manifestPath, marker, fault);
            }
            catch (Exception error)
            {
                throw new StorageException("scope-commit-pending", "Commit decidido; reconciliar antes de reintentar. No se revierte la selección.", true, error);
            }
            return true;
        });
    }

    private ScopeSnapshot ReadLocked(bool detachCopy = false)
    {
        if (!File.Exists(StatePath))
        {
            foreach (var projection in new[] { "local/inventory.json", "local/recipes.json", "local/active.json" })
                if (File.Exists(Context.PathFor(projection)))
                    throw new StorageException("missing-state-pointer", "Existen proyecciones sin estado publicado; no se acepta catálogo vacío.");
            return Empty();
        }
        var state = ProjectDocuments.Read(File.ReadAllBytes(StatePath), "state");
        var data = state["data"]!.AsObject();
        var scopeId = ProjectDocuments.Uuid(data["scope-id"]);
        var gen = ProjectDocuments.Uuid(data["generation"]);
        ProjectDocuments.Uuid(data["transaction-id"]);
        var generationState = Context.PathFor($"local/generations/{gen}/state.json");
        if (!File.Exists(generationState) || !File.ReadAllBytes(generationState).AsSpan().SequenceEqual(ProjectDocuments.Bytes(state)))
            throw new StorageException("invalid-generation", "Falta la generación publicada o no coincide con su estado.");
        var documents = LoadDocuments(Context, state);
        var sameBase = data["base-volume"]?.GetValue<string>() == Context.PhysicalBase.Volume &&
            data["base-file-id"]?.GetValue<string>() == Context.PhysicalBase.FileId;
        var sameRoot = data["root-volume"]?.GetValue<string>() == Context.PhysicalRoot.Volume &&
            data["root-file-id"]?.GetValue<string>() == Context.PhysicalRoot.FileId;
        if (!sameBase || !sameRoot)
        {
            if (!sameBase && !sameRoot)
            {
                if (detachCopy) return new(state["revision"]!.GetValue<long>(), null, state, new Dictionary<string, JsonObject>());
                throw new StorageException("scope-copy-detected", "Copia física comprobada; se requiere identidad independiente.");
            }
            throw new StorageException("scope-identity-conflict", "Identidad física incierta; no se adoptan publicaciones anteriores.");
        }
        return new(state["revision"]!.GetValue<long>(), scopeId, state, documents);
    }

    private static Dictionary<string, JsonObject> LoadDocuments(ScopeContext context, JsonObject state)
    {
        var data = state["data"]!.AsObject();
        var gen = ProjectDocuments.Uuid(data["generation"]);
        if (data["documents"] is not JsonObject entries) throw new StorageException("invalid-generation", "Falta el índice de documentos de la generación.");
        var result = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var (path, entry) in entries)
        {
            ValidateDocumentPath(path);
            var physical = context.PathFor($"local/generations/{gen}/{path}");
            if (!File.Exists(physical)) throw new StorageException("invalid-generation", "Falta un documento comprometido; se conserva evidencia.");
            var bytes = File.ReadAllBytes(physical);
            if (Hash(bytes) != entry!["sha256"]!.GetValue<string>()) throw new StorageException("invalid-generation", "Hash de generación discrepante.");
            var document = ProjectDocuments.Read(bytes, entry["kind"]!.GetValue<string>());
            if (document["revision"]!.GetValue<long>() != entry["revision"]!.GetValue<long>()) throw new StorageException("invalid-generation", "Revisión de generación discrepante.");
            result.Add(path, document);
        }
        return result;
    }

    private static T WithLocks<T>(IReadOnlyList<ScopeStore> initial, Func<T> action)
    {
        var stores = initial.ToList();
        for (var attempt = 0; attempt < 32; attempt++)
        {
            foreach (var store in stores) store.Context.Ensure();
            var ordered = stores.GroupBy(x => x.Context.PhysicalRoot).Select(x => x.First())
                .OrderBy(x => x.Context.PhysicalRoot.Volume, StringComparer.Ordinal)
                .ThenBy(x => x.Context.PhysicalRoot.FileId, StringComparer.Ordinal).ToArray();
            if (attempt == 0 && ordered.Length != initial.Count) throw new StorageException("duplicate-scope", "Dos participantes apuntan al mismo ámbito físico.");
            var leases = new List<FileStream>();
            try
            {
                foreach (var store in ordered) leases.Add(Acquire(store.Context.PathFor("local/writer.lock")));
                var markers = ordered.Where(x => File.Exists(x.PendingPath))
                    .Select(x => ProjectDocuments.Read(File.ReadAllBytes(x.PendingPath), "operation")).ToArray();
                var expanded = false;
                foreach (var marker in markers)
                {
                    var manifest = Manifest(marker);
                    foreach (var node in manifest["data"]!["participants"]!.AsArray())
                    {
                        var participant = Participant(node!);
                        if (!ordered.Any(x => x.Context.PhysicalRoot == participant.Context.PhysicalRoot))
                        { stores.Add(participant); expanded = true; }
                    }
                }
                if (expanded) continue; // release all leases before acquiring a larger ordered set
                foreach (var marker in markers)
                    Complete(marker["data"]!["coordinator"]!.GetValue<string>(), marker, null);
                return action();
            }
            finally { foreach (var lease in leases.AsEnumerable().Reverse()) lease.Dispose(); }
        }
        throw new StorageException("scope-contention", "No se pudo estabilizar el conjunto de ámbitos.");
    }

    private static JsonObject Manifest(JsonObject marker)
    {
        var data = marker["data"]!.AsObject();
        ProjectDocuments.Uuid(data["operation-id"]);
        var path = data["coordinator"]!.GetValue<string>();
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) throw new StorageException("missing-coordinator", "No se puede localizar el coordinador; se conserva la operación pendiente.");
        var tx = data["operation-id"]!.GetValue<string>();
        if (Path.GetFileName(path) != "operation.json" || Path.GetFileName(Path.GetDirectoryName(path)) != tx ||
            Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path))) != "operations")
            throw new StorageException("invalid-operation", "Ruta de coordinador fuera del layout de operaciones.");
        var bytes = File.ReadAllBytes(path);
        if (Hash(bytes) != data["manifest-sha256"]!.GetValue<string>()) throw new StorageException("invalid-operation", "Manifiesto coordinador discrepante.");
        var manifest = ProjectDocuments.Read(bytes, "operation");
        if (manifest["data"]!["operation-id"]!.GetValue<string>() != data["operation-id"]!.GetValue<string>()) throw new StorageException("invalid-operation", "Identidad de operación discrepante.");
        return manifest;
    }

    private static ScopeStore Participant(JsonNode node)
    {
        var context = ScopeContext.At(node["base"]!.GetValue<string>(), node["root"]!.GetValue<string>(), node["global"]!.GetValue<bool>());
        if (context.PhysicalRoot.Volume != node["volume"]!.GetValue<string>() || context.PhysicalRoot.FileId != node["file-id"]!.GetValue<string>())
            throw new StorageException("participant-identity-conflict", "Un participante no conserva identidad física; no se publica ni libera su evidencia.");
        return new(context);
    }

    private static void Complete(string manifestPath, JsonObject marker, Action<ScopeWriteStage>? fault)
    {
        var manifest = Manifest(marker);
        var decision = Path.Combine(Path.GetDirectoryName(manifestPath)!, "commit.json");
        var completed = Path.Combine(Path.GetDirectoryName(manifestPath)!, "completed.json");
        var committed = File.Exists(decision);
        if (committed && !File.ReadAllBytes(decision).AsSpan().SequenceEqual(ProjectDocuments.Bytes(Decision(marker, "committed", 2))))
            throw new StorageException("invalid-operation", "Decisión de commit discrepante.");
        if (File.Exists(completed))
        {
            if (!File.ReadAllBytes(completed).AsSpan().SequenceEqual(ProjectDocuments.Bytes(Decision(marker, committed ? "committed" : "aborted", 3))))
                throw new StorageException("invalid-operation", "Finalización de operación discrepante.");
            foreach (var node in manifest["data"]!["participants"]!.AsArray())
                RemoveMarker(Participant(node!), marker);
            return;
        }
        // Verify every participant and generation before touching any pointer.
        var participants = manifest["data"]!["participants"]!.AsArray().Select(node =>
        {
            var store = Participant(node!);
            var next = node!["next"]!.AsObject();
            var previous = node["previous"] as JsonObject;
            var current = File.Exists(store.StatePath) ? ProjectDocuments.Read(File.ReadAllBytes(store.StatePath), "state") : null;
            if (!JsonNode.DeepEquals(current, previous) && !JsonNode.DeepEquals(current, next)) throw Conflict();
            if (committed)
            {
                var generation = ProjectDocuments.Uuid(next["data"]!["generation"]);
                var saved = store.Context.PathFor($"local/generations/{generation}/state.json");
                if (!File.Exists(saved) || !File.ReadAllBytes(saved).AsSpan().SequenceEqual(ProjectDocuments.Bytes(next)))
                    throw new StorageException("invalid-generation", "Estado de generación discrepante.");
                _ = LoadDocuments(store.Context, next);
            }
            return (Store: store, Next: next);
        }).ToArray();
        if (committed)
        {
            foreach (var item in participants)
            {
                Replace(item.Store.StatePath, ProjectDocuments.Bytes(item.Next));
                fault?.Invoke(ScopeWriteStage.ParticipantPublished);
            }
            foreach (var item in participants)
                foreach (var (path, doc) in LoadDocuments(item.Store.Context, item.Next))
                    Replace(item.Store.Context.PathFor(path), ProjectDocuments.Bytes(doc));
            fault?.Invoke(ScopeWriteStage.ProjectionsPublished);
        }
        // Prepared without decision: leave immutable candidates and manifest for
        // diagnosis, never adopt them by existence. No resource deletion here.
        WriteNew(completed, ProjectDocuments.Bytes(Decision(marker, committed ? "committed" : "aborted", 3)));
        foreach (var item in participants) RemoveMarker(item.Store, marker);
    }

    private static void RemoveMarker(ScopeStore store, JsonObject marker)
    {
        if (!File.Exists(store.PendingPath)) return;
        var observed = ProjectDocuments.Read(File.ReadAllBytes(store.PendingPath), "operation");
        // A completed transaction must not remove a newer transaction's marker.
        if (JsonNode.DeepEquals(observed, marker)) File.Delete(store.PendingPath);
    }

    private static JsonObject Decision(JsonObject marker, string phase, int revision)
    {
        var data = marker["data"]!.DeepClone().AsObject();
        data["phase"] = phase;
        return ProjectDocuments.Create("operation", revision, data);
    }

    private static FileStream Acquire(string path)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            try { return new(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException error) when ((error.HResult & 0xffff) is 32 or 33)
            {
                if (watch.ElapsedMilliseconds >= 3000) throw new StorageException("writer-busy", "Otro escritor mantiene el ámbito ocupado.", inner: error);
                Thread.Sleep(25);
            }
        }
    }
    private static ScopeSnapshot Empty() => new(0, null, null, new Dictionary<string, JsonObject>());
    private static string Hash(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static void ValidateDocumentPath(string path)
    {
        if (path is not ("local/inventory.json" or "local/recipes.json" or "local/combinations.json" or "combinations.json" or "local/active.json" or "local/last-good.json" or "local/update-plan.json") && !IsResolutionPath(path))
            throw new StorageException("invalid-projection-path", "Documento fuera de las proyecciones admitidas del ámbito.");
    }
    private static bool IsResolutionPath(string path)
    {
        var parts = path.Split('/');
        return parts.Length == 3 && parts[0] == "combinations" && parts[2] == "resolved.json" && IsCombinationId(parts[1]) ||
            parts.Length == 4 && parts[0] == "combinations" && parts[2] == "local" && parts[3] == "resolved.json" && IsCombinationId(parts[1]);
    }
    internal static bool IsCombinationId(string id) => id.Length == 64 && id.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static void WriteNew(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(bytes); stream.Flush(true);
    }
    internal static void Replace(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + "." + ProjectDocuments.Id() + ".tmp";
        WriteNew(temp, bytes);
        try { if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}
