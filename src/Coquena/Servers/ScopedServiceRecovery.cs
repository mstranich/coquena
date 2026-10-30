using System.Text.Json.Nodes;
using Coquena.Backends;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Models;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Storage;

namespace Coquena.Servers;

public enum ServiceRecoveryChoice { Cancel, Retry, LastGood }

public sealed partial class ScopedServiceProcess
{
    // One lifecycle lease covers preparation, candidate and recovery. The snapshot
    // is captured before attempting the candidate, never reread from today's YAML.
    public async Task<string> RunAsync(string serviceId, bool restart, bool detached,
        Func<CancellationToken, Task<ScopedStartPlan>> prepare,
        Func<JsonObject, CancellationToken, Task<ScopedStartPlan>> restore,
        Func<string, bool, ServiceRecoveryChoice>? decide = null,
        CancellationToken cancellation = default, Func<bool>? forceRequested = null)
    {
        using var lease = Lease(serviceId);
        var running = ReconcileLocked(serviceId);
        var before = store.Read();
        if (PresetPublication.Service(before, serviceId)["publication-operation"] is not null)
            throw new StorageException("publication-pending", "Reconciliar la publicación antes de iniciar o reiniciar.");
        var activePath = store.RuntimeDocumentPath(serviceId, "active");
        var originalInstance = running ? before.Documents[activePath]["data"]!["instance-id"]!.GetValue<string>() : null;
        if (running && !restart) return originalInstance!;
        var good = before.Documents.GetValueOrDefault(store.RuntimeDocumentPath(serviceId, "last-good"))?["data"]?.DeepClone().AsObject();
        if (good is not null && good["service-id"]?.GetValue<string>() != serviceId)
            throw new StorageException("instance-conflict", "La última buena no corresponde al servicio seleccionado.");
        while (true)
        {
            var stoppedOriginal = false;
            var candidateId = Guid.NewGuid().ToString("D");
            try
            {
                cancellation.ThrowIfCancellationRequested();
                var plan = await prepare(cancellation);
                plan = plan with { InstanceId = Guid.ParseExact(candidateId, "D") };
                ValidatePlan(plan);
                ValidateBindings(plan);
                plan.ValidateIntent?.Invoke();
                cancellation.ThrowIfCancellationRequested();
                if (running) { StopLocked(serviceId, forceRequested, originalInstance); stoppedOriginal = true; }
                var result = await StartLockedAsync(serviceId, plan, cancellation, forceRequested);
                RecordRecovery(serviceId, null, "candidate-succeeded", result);
                ReportRecovery(serviceId, "Candidata funcional; diagnóstico de fallo eliminado.", warning: false);
                return result;
            }
            catch (Exception failure) when (Recoverable(failure))
            {
                var code = FailureCode(failure);
                RecordRecovery(serviceId, code, "candidate-failed", candidateId);
                // Failed preparation must never interrupt the original service.
                if (running && !stoppedOriginal)
                {
                    ReportRecovery(serviceId, $"Candidata fallida ({code}); se conserva la instancia activa {originalInstance}.");
                    if (!detached && decide?.Invoke(code, false) == ServiceRecoveryChoice.Retry) continue;
                    return originalInstance!;
                }
                if (ReconcileLocked(serviceId))
                    StopLocked(serviceId, forceRequested, candidateId);
                running = false;
                ReportRecovery(serviceId, $"Candidata fallida ({code}).");
                var choice = detached ? ServiceRecoveryChoice.LastGood : decide?.Invoke(code, good is not null) ?? ServiceRecoveryChoice.Cancel;
                if (choice == ServiceRecoveryChoice.Retry) continue;
                if (choice != ServiceRecoveryChoice.LastGood || good is null)
                {
                    ReportRecovery(serviceId, "Sin recuperación; no hay última buena disponible o se canceló la operación.");
                    throw;
                }
                var recoveryId = Guid.NewGuid().ToString("D");
                try
                {
                    cancellation.ThrowIfCancellationRequested();
                    var recovery = await restore(good.DeepClone().AsObject(), cancellation);
                    recovery = recovery with { InstanceId = Guid.ParseExact(recoveryId, "D"), ValidateIntent = null };
                    ValidatePlan(recovery);
                    ValidateBindings(recovery);
                    var result = await StartLockedAsync(serviceId, recovery, cancellation, forceRequested);
                    RecordRecovery(serviceId, code, "last-good-succeeded", result);
                    ReportRecovery(serviceId, $"Última buena recuperada; instancia {result}. La candidata conserva su diagnóstico de fallo.");
                    return result;
                }
                catch (Exception recoveryFailure) when (Recoverable(recoveryFailure) || FailureCode(recoveryFailure) is
                    "invalid-start-plan" or "resource-content-conflict" or "resource-integrity-invalid" or "snapshot-resource-unavailable")
                {
                    if (ReconcileLocked(serviceId)) StopLocked(serviceId, forceRequested, recoveryId);
                    RecordRecovery(serviceId, code, "last-good-failed", recoveryId, FailureCode(recoveryFailure));
                    ReportRecovery(serviceId, $"Última buena fallida ({FailureCode(recoveryFailure)}); recuperación terminada sin más intentos.");
                    if (FailureCode(recoveryFailure).Contains("integrity", StringComparison.Ordinal)) throw;
                    throw new StorageException("service-recovery-failed", $"Falló candidata ({code}) y última buena ({FailureCode(recoveryFailure)}).", inner: recoveryFailure);
                }
            }
        }
    }

    private void RecordRecovery(string serviceId, string? failure, string result, string instanceId, string? recoveryFailure = null)
    {
        var state = store.Read(); var docs = RecipeRegistry.Next(state);
        var service = docs["local/recipes.json"]["data"]!["recipes"]!.AsArray()
            .SelectMany(recipe => recipe!["entities"]!.AsArray())
            .Single(entity => entity!["kind"]!.GetValue<string>() == "service" && entity["id"]!.GetValue<string>() == serviceId)!;
        service["recovery"] = new JsonObject { ["candidate-failed"] = failure is not null,
            ["diagnostic-code"] = failure, ["result"] = result, ["instance-id"] = instanceId,
            ["recovery-diagnostic-code"] = recoveryFailure, ["checked-at"] = DateTimeOffset.UtcNow.ToString("O") };
        store.Commit(state.Revision, docs);
    }

    private void ReportRecovery(string serviceId, string message, bool warning = true)
    {
        var line = $"[Recuperación] {message}";
        (warning ? diagnostics ?? output : output)?.WriteLine(line);
        var path = store.Context.PathFor($"local/runtime/{serviceId}/recovery.log");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {line}{Environment.NewLine}");
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        { (diagnostics ?? output)?.WriteLine("[Aviso] No se pudo escribir el log de recuperación."); }
    }

    private static string FailureCode(Exception failure) => failure switch
    {
        StorageException e => e.Code, BackendException e => e.Code, RecipeException e => e.Code,
        DownloadException e => e.Code, ModelSourceException e => e.Code, ContractException e => e.Code, _ => "service-error"
    };

    private static bool Recoverable(Exception failure)
    {
        var code = FailureCode(failure);
        if (code.Contains("integrity", StringComparison.Ordinal)) return false;
        if (failure is StorageException)
            return code is "process-exited" or "health-timeout" or "backend-missing" or "unknown-resource" or "snapshot-resource-missing" or "endpoint-unavailable";
        return failure is BackendException or RecipeException or DownloadException or ModelSourceException or HttpRequestException or
            IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception;
    }
}
