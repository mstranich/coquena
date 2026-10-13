using System.Net;
using System.Net.Http.Headers;
using Coquena.Contracts;
using Coquena.Storage;
using Coquena.Models;

namespace Coquena.Downloads;

public class DownloadException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

// Provider resolves an ephemeral location from the PINNED selection on every attempt.
// No URL, credential or provider object is written to operation.json.
public interface IDownloadTransport
{
    Task<HttpResponseMessage> OpenAsync(OperationDocument operation, int file, long offset,
        CancellationToken cancellation);
}

public sealed class HttpDownloadTransport : IDownloadTransport, IDisposable
{
    private readonly HttpClient client;
    private readonly Func<OperationDocument, int, CancellationToken, Task<Uri>> resolve;
    public HttpDownloadTransport(Func<OperationDocument, int, CancellationToken, Task<Uri>> resolve)
        : this(new HttpClientHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.None, UseCookies = false }, resolve) { }
    // Injected handlers are trusted test/provider code, never persisted.
    public HttpDownloadTransport(HttpMessageHandler handler, Func<OperationDocument, int, CancellationToken, Task<Uri>> resolve)
    {
        client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        this.resolve = resolve;
    }
    public void Dispose() => client.Dispose();
    public async Task<HttpResponseMessage> OpenAsync(OperationDocument operation, int file, long offset,
        CancellationToken cancellation)
    {
        var uri = await resolve(operation, file, cancellation);
        if (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback))
            throw new DownloadException("unsafe-location", "Se requiere HTTPS (HTTP sólo para fixtures loopback).");
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.AcceptEncoding.ParseAdd("identity");
        if (offset > 0)
        {
            request.Headers.Range = new RangeHeaderValue(offset, null);
            var entry = operation.Files[file];
            if (entry.ETag is not null && EntityTagHeaderValue.TryParse(entry.ETag, out var tag) && !tag.IsWeak)
                request.Headers.IfRange = new RangeConditionHeaderValue(tag);
            else if (DateTimeOffset.TryParse(entry.LastModified, out var date))
                request.Headers.IfRange = new RangeConditionHeaderValue(date);
        }
        // No automatic redirect, decompression, cookies or ambient credentials.
        return await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation);
    }
}

public sealed class DownloadManager(Home home, LocalStore? storage = null)
{
    internal Home StorageHome => home;
    private readonly LocalStore store = storage ?? new LocalStore(home);
    private static readonly ContractJsonContext Json = ContractJsonContext.Default;
    private string Area(Guid id) => home.PathFor($"downloads/{id:D}");
    private string Control(Guid id, string action) => home.PathFor($"downloads/{id:D}/{action}.request");
    private OperationDocument Read(Guid id) => store.Read(DocumentKind.Operation, Json.OperationDocument,
        ContractValidation.Operation, operation: id) ?? throw new DownloadException("not-found", "Operación inexistente.");
    private OperationDocument Save(OperationDocument value) => store.Update(DocumentKind.Operation,
        Json.OperationDocument, ContractValidation.Operation, old => old is null
            ? throw new DownloadException("not-found", "Operación inexistente.") : value, operation: value.Id);

    private void Paths(OperationDocument value)
    {
        ContractValidation.Operation(value);
        var partials = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var destinations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var prefix = $"downloads/{value.Id:D}/files/";
        foreach (var file in value.Files)
        {
            if (!file.PartialPath.StartsWith(prefix, StringComparison.Ordinal) ||
                !file.PartialPath.EndsWith(".part", StringComparison.Ordinal) ||
                !partials.Add(home.PathFor(file.PartialPath)) || !destinations.Add((value.ResourceKind is "model" or "companion") ? new ModelPaths(home).Destination(value, file) : home.PathFor(file.DestinationPath)) ||
                !file.DestinationPath.StartsWith(value.ResourceKind == "backend" ? "backends/" : "models/", StringComparison.Ordinal))
                throw new DownloadException("unsafe-path", "Rutas de operación inválidas o en colisión.");
        }
    }

    public OperationDocument Create(OperationDocument selection)
    {
        Paths(selection);
        if (selection.State != "pending" || selection.Files.Any(f => f.ConfirmedBytes != 0 || f.State != "pending"))
            throw new DownloadException("invalid-selection", "Nueva operación debe estar pendiente y sin bytes.");
        return store.Exclusive(() => CreateLocked(selection));
    }

    internal OperationDocument CreateLocked(OperationDocument selection)
    {
        Paths(selection);
        if (selection.State != "pending" || selection.Files.Any(f => f.ConfirmedBytes != 0 || f.State != "pending"))
            throw new DownloadException("invalid-selection", "Nueva operación debe estar pendiente y sin bytes.");
        return store.UpdateLocked(DocumentKind.Operation, Json.OperationDocument, ContractValidation.Operation,
            old => old is null && !Directory.Exists(Area(selection.Id)) ? selection :
                throw new DownloadException("already-exists", "GUID ya registrado o preparación huérfana; no se sobrescribe."), operation: selection.Id);
    }

    // The OS handle, not PID or the lock file's mere existence, proves executor ownership.
    private FileStream Lease(Guid id) => store.Exclusive(() =>
    {
        _ = Read(id);
        try { return new FileStream(home.PathFor($"downloads/{id:D}/executor.lock"), FileMode.OpenOrCreate,
            FileAccess.ReadWrite, FileShare.Delete); }
        catch (IOException e) when ((e.HResult & 0xffff) is 32 or 33)
        { throw new DownloadException("executor-busy", "Otro ejecutor controla este GUID."); }
    });

    public OperationDocument Status(Guid id)
    {
        var value = Read(id);
        Paths(value);
        if (value.Phase is "publishing" or "cleanup" or "cancelling-publication" || value.Phase == "resource-validation" && value.Files.All(f => f.State == "transferred")) return value;
        if (value.State is not ("downloading" or "verifying")) return value;
        FileStream lease;
        try { lease = Lease(id); }
        catch (DownloadException e) when (e.Code == "executor-busy") { return Read(id); }
        using (lease)
        {
            value = Read(id);
            if (value.State is not ("downloading" or "verifying")) return value;
            var consistent = value.Files.All(f => Length(f) == f.ConfirmedBytes);
            return Save(value with { State = consistent ? "paused" : "failed", Phase = "transfer",
                Diagnostic = consistent ? "executor-interrupted" : "checkpoint-inconsistent", UpdatedAt = DateTimeOffset.UtcNow });
        }
    }

    public OperationDocument[] List(string? kind = null)
    {
        var directory = home.PathFor("downloads");
        if (!Directory.Exists(directory)) return [];
        var results = new List<OperationDocument>();
        foreach (var path in Directory.GetDirectories(directory).Order(StringComparer.Ordinal))
        {
            Home.CheckLinks(path);
            if (!Guid.TryParseExact(Path.GetFileName(path), "D", out var id))
                throw new DownloadException("invalid-operation-directory", "Directorio de operación no reconocido.");
            try { var value = Status(id); if (kind is null || value.ResourceKind == kind) results.Add(value); }
            catch (DownloadException e) when (e.Code == "not-found" && !Directory.Exists(path)) { } // concurrent cleanup
        }
        return results.ToArray();
    }

    // Pure snapshot for a caller already holding the home writer lease. Status
    // may recover an interrupted executor and acquire that lease itself.
    internal OperationDocument[] SnapshotLocked()
    {
        var directory = home.PathFor("downloads");
        if (!Directory.Exists(directory)) return [];
        List<OperationDocument> values = [];
        foreach (var path in Directory.GetDirectories(directory))
        {
            Home.CheckLinks(path);
            if (!Guid.TryParseExact(Path.GetFileName(path), "D", out var id))
                throw new DownloadException("invalid-operation-directory", "Directorio de operación no reconocido.");
            values.Add(Read(id));
        }
        return values.ToArray();
    }

    public async Task ControlAsync(Guid id, bool cancel, CancellationToken cancellation = default,
        Func<OperationDocument, Task<bool>>? beforeCancel = null)
    {
        FileStream? lease = null;
        try { lease = Lease(id); }
        catch (DownloadException e) when (e.Code == "executor-busy")
        {
            store.Exclusive(() => { _ = Read(id); File.WriteAllText(Control(id, cancel ? "cancel" : "pause"), ""); return 0; });
            // Confirm only AFTER ownership can be acquired and the writer has stopped.
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            while (lease is null)
            {
                await Task.Delay(50, timeout.Token);
                if (!Directory.Exists(Area(id))) return;
                try { lease = Lease(id); }
                catch (DownloadException busy) when (busy.Code == "executor-busy") { }
                catch (DownloadException missing) when (missing.Code == "not-found" && !Directory.Exists(Area(id))) { return; }
            }
        }
        using (lease)
        {
            var value = Read(id); Paths(value);
            if (cancel)
            {
                if (beforeCancel is not null && await beforeCancel(value)) return;
                Cleanup(value);
            }
            else
            {
                if (value.State == "cancelled") throw new DownloadException("cleanup-pending", "Cancelación pendiente de limpieza.");
                Save(value with { State = "paused", UpdatedAt = DateTimeOffset.UtcNow });
                RemoveRequest(id, "pause");
            }
        }
    }

    private void RemoveRequest(Guid id, string action) { var path = Control(id, action); if (File.Exists(path)) File.Delete(path); }
    private long Length(TransferRecord file) => File.Exists(home.PathFor(file.PartialPath)) ? new FileInfo(home.PathFor(file.PartialPath)).Length : 0;
    private void Cleanup(OperationDocument value)
    {
        value = Save(value with { State = "cancelled", Phase = "cleanup", Diagnostic = "cleanup-pending", UpdatedAt = DateTimeOffset.UtcNow });
        try
        {
            store.Exclusive(() =>
            {
                // Leave operation.json until all potentially locked payload files have gone.
                var area = Area(value.Id);
                var entries = Directory.GetFileSystemEntries(area, "*", SearchOption.AllDirectories);
                foreach (var entry in entries) Home.CheckLinks(entry);
                foreach (var entry in entries.Where(File.Exists))
                    if (entry != Path.Combine(area, "operation.json") && entry != Path.Combine(area, "executor.lock")) File.Delete(entry);
                foreach (var entry in entries.Where(Directory.Exists).OrderByDescending(x => x.Length)) Directory.Delete(entry);
                File.Delete(Path.Combine(area, "operation.json"));
                File.Delete(Path.Combine(area, "executor.lock")); // handle is delete-shared but retains executor exclusion
                Directory.Delete(area);
                store.RecordCancellation(value.Id);
                return 0;
            });
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or StorageException)
        {
            // Restore the recovery record even if removal failed after deleting its JSON.
            store.Update(DocumentKind.Operation, Json.OperationDocument, ContractValidation.Operation,
                _ => value, operation: value.Id);
            throw new DownloadException("cleanup-pending", "Limpieza incompleta; registro recuperable conservado.");
        }
    }

    public async Task<OperationDocument?> RunAsync(Guid id, IDownloadTransport transport,
        Func<OperationDocument, int, CancellationToken, Task<bool>>? restart = null,
        Action<OperationDocument>? progress = null, CancellationToken cancellation = default,
        Func<OperationDocument, CancellationToken, Task<OperationDocument?>>? finalize = null,
        Func<OperationDocument, Task<bool>>? beforeCancel = null)
    {
        using var lease = Lease(id);
        var value = Read(id); Paths(value);
        if (value.Phase == "cancelling-publication")
            throw new DownloadException("cancel-required", "Publicación en reversión; repita download cancel para completar limpieza.");
        if (value.State == "completed" && !(finalize is not null && value.Phase == "cleanup"))
            throw new DownloadException("cleanup-pending", "Operación terminada pendiente de limpieza del consumidor.");
        if (value.State == "cancelled") throw new DownloadException("cleanup-pending", "Cancelar para completar limpieza pendiente.");
        using var stopped = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        // External controls are requests; only this executor mutates checkpoints while writing.
        var monitoring = Monitor();
        async Task Monitor()
        {
            try
            {
                while (!stopped.IsCancellationRequested)
                {
                    if (File.Exists(Control(id, "pause")) || File.Exists(Control(id, "cancel"))) { stopped.Cancel(); break; }
                    await Task.Delay(50, stopped.Token);
                }
            }
            catch (OperationCanceledException) { }
        }
        try
        {
            if (value.ModelInstall?.Previous is not null)
                value = await new ModelInstaller(home, store).PrepareUpgradeAsync(value, stopped.Token);
            if (finalize is not null && value.Phase is "publishing" or "cleanup")
                return await finalize(value, stopped.Token);
            value = Save(value with { State = "downloading", Phase = "transfer", Diagnostic = null, UpdatedAt = DateTimeOffset.UtcNow });
            for (var i = 0; i < value.Files.Length; i++)
            {
                stopped.Token.ThrowIfCancellationRequested();
                var file = value.Files[i];
                var length = Length(file);
                if (length != file.ConfirmedBytes)
                    throw new DownloadException("checkpoint-inconsistent", "Parcial y checkpoint difieren; no se reutilizan bytes sin evidencia.");
                if (file.State == "transferred")
                {
                    if (value.ModelInstall is not null)
                    {
                        var check = await FileIntegrity.VerifyAsync(home.PathFor(file.PartialPath), file.LocalIntegrity, stopped.Token);
                        if (check.Status != "verified") throw new DownloadException("partial-integrity-failed", "Archivo completo cambió; no se reutiliza ni se redefine su referencia.");
                    }
                    continue;
                }
                for (var attempt = 0; ; attempt++)
                {
                    try
                    {
                        await Transfer(i);
                        break;
                    }
                    catch (TransientException e) when (attempt < 2)
                    {
                        await Task.Delay(e.Delay, stopped.Token);
                    }
                }
            }
            value = Save(value with { State = "verifying", Phase = "resource-validation", UpdatedAt = DateTimeOffset.UtcNow });
            progress?.Invoke(value);
            return finalize is null ? value : await finalize(value, stopped.Token);
        }
        catch (OperationCanceledException) when (stopped.IsCancellationRequested)
        {
            value = Read(id);
            // Transfer's finally flushed and persisted the exact length before reaching here.
            if (File.Exists(Control(id, "cancel")))
            {
                if (beforeCancel is not null && await beforeCancel(value)) return null;
                Cleanup(value); return null;
            }
            value = Save(value with { State = "paused", Files = value.Files.Select(f => f.State == "downloading" ? f with { State = "paused" } : f).ToArray(),
                Diagnostic = null, UpdatedAt = DateTimeOffset.UtcNow });
            RemoveRequest(id, "pause"); progress?.Invoke(value); return value;
        }
        catch (Exception e) when (e is DownloadException or HttpRequestException or IOException or InvalidDataException)
        {
            value = Read(id);
            if (value.Phase == "cleanup") throw;
            // Fixed diagnostic codes only: exception text may contain credentials/ephemeral URLs.
            var code = e is DownloadException d ? d.Code : "transfer-io-failed";
            value = Save(value with { State = "failed", Files = value.Files.Select(f => f.State == "downloading" ? f with { State = "failed" } : f).ToArray(),
                Diagnostic = code, UpdatedAt = DateTimeOffset.UtcNow });
            progress?.Invoke(value); return value;
        }
        finally { stopped.Cancel(); await monitoring; }

        async Task Transfer(int index)
        {
            var file = value.Files[index];
            using var response = await Open(index, file.ConfirmedBytes);
            if (response.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Redirect or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                throw new DownloadException("redirect-requires-provider", "El proveedor debe resolver redirecciones sin reenviar credenciales.");
            if ((int)response.StatusCode is 408 or 429 or 500 or 502 or 503 or 504)
            {
                var retry = response.Headers.RetryAfter;
                var wait = retry?.Delta ?? (retry?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromMilliseconds(250));
                if (wait > TimeSpan.FromSeconds(30)) throw new DownloadException("retry-after-exceeds-limit", "Espera del servidor excede límite; reanudar explícitamente.");
                throw new TransientException(wait < TimeSpan.Zero ? TimeSpan.Zero : wait);
            }
            var tag = response.Headers.ETag?.ToString();
            var modified = response.Content.Headers.LastModified?.ToString("O");
            if ((file.ETag is not null && file.ETag != tag) || (file.ETag is null && file.LastModified is not null && file.LastModified != modified))
                throw new DownloadException("content-changed", "Validador cambió; no cambiar selección ni reiniciar.");
            if (file.ConfirmedBytes > 0 && response.StatusCode is HttpStatusCode.OK or HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                if (restart is null || !await restart(value, index, stopped.Token))
                    throw new DownloadException("restart-required", "Range no continúa; requiere aceptación explícita para reiniciar este archivo.");
                stopped.Token.ThrowIfCancellationRequested();
                // Persist zero first. Crash before truncation is diagnosed as ahead, never appended.
                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && file.ETag is null && file.LastModified is null)
                    throw new DownloadException("continuity-unknown", "416 sin evidencia de continuidad; no se descarta el parcial.");
                file = file with { ConfirmedBytes = 0, State = "pending" };
                value.Files[index] = file; value = Save(value);
                using (var reset = new FileStream(home.PathFor(file.PartialPath), FileMode.Create, FileAccess.Write)) reset.Flush(true);
                await Transfer(index); return;
            }
            if (response.Content.Headers.ContentEncoding.Count != 0)
                throw new DownloadException("encoded-response", "Respuesta codificada no reutilizable por offset.");
            long? end = null;
            if (response.StatusCode == HttpStatusCode.PartialContent)
            {
                var range = response.Content.Headers.ContentRange;
                if (range is null || range.Unit != "bytes" || range.From != file.ConfirmedBytes || range.To is null ||
                    range.To < range.From || (range.Length is not null && range.To + 1 != range.Length) ||
                    (file.ExpectedSize is not null && range.Length != file.ExpectedSize) ||
                    (response.Content.Headers.ContentLength is { } count && count != range.To - range.From + 1))
                    throw new DownloadException("invalid-range", "Content-Range incoherente; no se añadió contenido.");
                end = range.To + 1;
            }
            else if (response.StatusCode != HttpStatusCode.OK || file.ConfirmedBytes != 0)
                throw new DownloadException("http-failed", "Respuesta HTTP no apta para transferencia.");
            else end = response.Content.Headers.ContentLength;
            if (file.ExpectedSize is { } size && end is { } announced && size != announced)
                throw new DownloadException("size-mismatch", "Tamaño anunciado no coincide con selección.");
            file = file with { ETag = tag, LastModified = modified, State = "downloading" };
            value.Files[index] = file; value = Save(value);
            var path = home.PathFor(file.PartialPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var target = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, 65536, FileOptions.Asynchronous);
            if (target.Length != file.ConfirmedBytes) throw new DownloadException("checkpoint-inconsistent", "Parcial cambió antes de abrir escritor.");
            using var hash = value.ModelInstall is not null ? new FileHashSession() : null;
            if (hash is not null)
            {
                target.Position = 0;
                await hash.ReadPrefixAsync(target, file.ConfirmedBytes, stopped.Token);
            }
            target.Position = file.ConfirmedBytes;
            var nextCheckpoint = DateTimeOffset.UtcNow.AddSeconds(1);
            try
            {
                await using var source = await response.Content.ReadAsStreamAsync(stopped.Token);
                var buffer = new byte[65536];
                while (true)
                {
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(stopped.Token);
                    idle.CancelAfter(TimeSpan.FromSeconds(30));
                    int read;
                    try { read = await source.ReadAsync(buffer, idle.Token); }
                    catch (OperationCanceledException) when (!stopped.IsCancellationRequested) { throw new TransientException(TimeSpan.FromMilliseconds(250)); }
                    catch (Exception e) when (e is IOException or HttpRequestException) { throw new TransientException(TimeSpan.FromMilliseconds(250)); }
                    if (read == 0) break;
                    if ((end is { } limit && target.Position + read > limit) || (file.ExpectedSize is { } total && target.Position + read > total))
                        throw new DownloadException("size-mismatch", "Respuesta excede los límites esperados.");
                    await target.WriteAsync(buffer.AsMemory(0, read), stopped.Token);
                    hash?.Append(buffer.AsSpan(0, read));
                    if (DateTimeOffset.UtcNow >= nextCheckpoint)
                    {
                        Checkpoint(); progress?.Invoke(value); nextCheckpoint = DateTimeOffset.UtcNow.AddSeconds(1);
                    }
                }
                if ((end is not null && target.Position != end) || (file.ExpectedSize is not null && target.Position != file.ExpectedSize))
                    throw new TransientException(TimeSpan.FromMilliseconds(250));
                if (hash is not null)
                {
                    var digest = hash.Finish();
                    if (FileIntegrity.Compare(digest, file.Identity).Status == "mismatch")
                        throw new DownloadException("remote-hash-mismatch", "SHA-256 remoto no coincide; no publicar ni redefinir referencia.");
                    target.Flush(true);
                    file = file with { LocalIntegrity = digest, VerifiedWriteUtc = File.GetLastWriteTimeUtc(path) };
                }
                file = file with { State = "transferred" };
            }
            finally { Checkpoint(); }
            void Checkpoint()
            {
                target.Flush(true);
                file = file with { ConfirmedBytes = target.Length };
                value.Files[index] = file;
                value = Save(value with { UpdatedAt = DateTimeOffset.UtcNow });
            }
        }
        async Task<HttpResponseMessage> Open(int index, long offset)
        {
            using var headers = CancellationTokenSource.CreateLinkedTokenSource(stopped.Token);
            headers.CancelAfter(TimeSpan.FromSeconds(30));
            try { return await transport.OpenAsync(value, index, offset, headers.Token); }
            catch (OperationCanceledException) when (!stopped.IsCancellationRequested) { throw new TransientException(TimeSpan.FromMilliseconds(250)); }
            catch (HttpRequestException) { throw new TransientException(TimeSpan.FromMilliseconds(250)); }
        }
    }
    private sealed class TransientException(TimeSpan delay) : DownloadException("retries-exhausted", "Reintentos agotados.")
    {
        public TimeSpan Delay { get; } = delay;
    }
}
