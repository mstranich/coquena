using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Models;
using Coquena.Storage;

namespace Coquena;

public static class ModelCli
{
    public static int Run(string[] args, TextWriter output, TextWriter error, ModelInstaller? installer = null,
        HuggingFaceStore? source = null, IDownloadTransport? transport = null,
        IModelSelectionInteraction? interaction = null, CancellationToken cancellation = default,
        IRemoteGgufInspector? inspector = null)
    {
        Guid? active = null;
        var companion = args.Length > 0 && args[0] == "companion";
        if (companion) args = args[1..];
        if (!(args.Length == 1 && args[0] == "list" || args.Length == 2 && args[0] == "download" ||
            args.Length is 1 or 2 && args[0] == "update" ||
            args.Length == 2 && args[0] is "refresh" or "verify" ||
            args.Length == 3 && args[0] == "verify" && args[2] == "--set-baseline" ||
            args.Length is 4 or 5 && args[0] == "add" && args[2] == "--name" &&
                (args.Length == 4 || args[4] == "--try-match")))
        { error.WriteLine("Error: argumentos de model inválidos. Consulte coquena --help."); return 2; }
        try
        {
            // Validate before any local or remote side effects.
            var request = args[0] == "download" ? ModelRequest.Parse(args[1]) : null;
            if (request is not null && companion != (request.CompanionType is not null)) throw new ModelSourceException("invalid-selector", "Use model download para principales y model companion download con #mmproj o #mtp para auxiliares.");
            installer ??= new ModelInstaller(Home.Resolve());
            if (args[0] == "update")
            {
                var units = installer.Updates.Scope(args.Length == 2 ? args[1] : null, companion);
                using var updateClient = source is null ? HuggingFaceStore.CreateClient() : null;
                source ??= new(updateClient!);
                using var stoppedUpdate = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                ConsoleCancelEventHandler updateHandler = (_, e) => { e.Cancel = true; stoppedUpdate.Cancel(); };
                Console.CancelKeyPress += updateHandler;
                var failed = false;
                try
                {
                    foreach (var unit in units)
                    {
                        if (stoppedUpdate.IsCancellationRequested) { failed = true; break; }
                        if (unit.Ownership == "external") { output.WriteLine($"{unit.Name} | no aplica: externo sin seguimiento."); continue; }
                        try
                        {
                            var checkedUnit = installer.Updates.CheckAsync(unit, source, interaction ?? new Interaction(output), stoppedUpdate.Token, inspector).GetAwaiter().GetResult();
                            var update = checkedUnit.Update!;
                            output.WriteLine($"{unit.Name} | {update.State} | intento {update.LastAttempt:O} | último éxito {update.LastSuccess:O} | {update.Diagnostic}");
                            if (update.State == "query-failed") output.WriteLine($"  Observación anterior: {update.LastSuccessfulState ?? "sin consulta exitosa"} | {update.LastSuccessfulDiagnostic}; plan anterior no es comprobación actual.");
                            if (update.Plan is { } plan)
                            {
                                output.WriteLine($"  Plan: {plan.TargetName} @ {plan.Target.Commit}");
                                foreach (var action in plan.Actions) output.WriteLine($"  {action.TargetPath} | {action.Action} | origen {action.SourcePath ?? "nuevo contenido"}");
                            }
                            failed |= update.State is "query-failed" or "intervention-required" or "orphaned";
                        }
                        catch (Exception e) when (e is ModelSourceException or StorageException or ContractException or IOException or UnauthorizedAccessException)
                        { output.WriteLine($"{unit.Name} | error por unidad: {e.Message}"); failed = true; }
                    }
                    if (units.Length == 0) output.WriteLine("Sin unidades registradas en el ámbito solicitado.");
                    return failed ? 1 : 0;
                }
                finally { Console.CancelKeyPress -= updateHandler; }
            }
            if (args[0] == "refresh")
            {
                var entry = installer.Refresh(args[1], companion);
                output.WriteLine($"Observaciones locales actualizadas: {entry.Record.RefreshedAt:O}");
                Show(entry);
                return entry.Availability == "available" ? 0 : 1;
            }
            if (args[0] == "verify")
            {
                var baseline = args.Length == 3;
                if (baseline) output.WriteLine("Aviso: la base local sólo detecta cambios posteriores; no certifica procedencia ni corrección original.");
                using var stoppedVerify = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                ConsoleCancelEventHandler verifyHandler = (_, e) => { e.Cancel = true; stoppedVerify.Cancel(); };
                Console.CancelKeyPress += verifyHandler;
                try
                {
                    var record = installer.VerifyAsync(args[1], companion, baseline, stoppedVerify.Token).GetAwaiter().GetResult();
                    var check = record.IntegrityCheck!;
                    output.WriteLine($"{record.Name} | integridad {check.Status} | {check.CheckedAt:O}");
                    foreach (var file in check.Files) output.WriteLine($"  {file.LocalPath} | {file.Status} | {file.Diagnostic}");
                    return check.Status is "verified" or "baseline-set" ? 0 : 1;
                }
                finally { Console.CancelKeyPress -= verifyHandler; }
            }
            if (args[0] == "add")
            {
                var record = installer.AddExternal(args[1], args[3], companion);
                output.WriteLine($"Registrado externo: {record.Id:D} | {record.Name}");
                Show(installer.List().Single(m => m.Record.Id == record.Id));
                if (args.Length == 5)
                {
                    using var client = source is null ? HuggingFaceStore.CreateClient() : null;
                    using var stoppedMatch = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                    ConsoleCancelEventHandler matchHandler = (_, e) => { e.Cancel = true; stoppedMatch.Cancel(); };
                    Console.CancelKeyPress += matchHandler;
                    try
                    {
                        var match = ExternalMatching.SelectAsync(record, source ?? new(client!), interaction ?? new Interaction(output), stoppedMatch.Token).GetAwaiter().GetResult();
                        if (match is not null)
                        {
                            installer.SaveInformativeMatch(record, match);
                            output.WriteLine("Equivalencia informativa guardada; externo sin seguimiento ni upgrade.");
                        }
                        else output.WriteLine("Alta local conservada sin emparejamiento.");
                    }
                    catch (Exception e) when (e is ModelSourceException or StorageException or ContractException or IOException or UnauthorizedAccessException or OperationCanceledException or HttpRequestException)
                    { output.WriteLine($"Alta local conservada sin emparejamiento: {e.Message}"); }
                    finally { Console.CancelKeyPress -= matchHandler; }
                }
                return 0;
            }
            if (args[0] == "list")
            {
                var entries = installer.List().Where(m => companion ? m.Record.CompanionType is not null : m.Record.CompanionType is null).ToArray();
                foreach (var entry in entries) Show(entry);
                if (entries.Length == 0) output.WriteLine(companion ? "Sin companions registrados." : "Sin modelos instalados.");
                return 0;
            }
            using var stopped = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; stopped.Cancel(); };
            Console.CancelKeyPress += handler;
            try
            {
                using var client = source is null ? HuggingFaceStore.CreateClient() : null;
                source ??= new(client!);
                interaction ??= new Interaction(output);
                var selected = new ModelSelectionMenu(source, inspector).SelectAsync(request!, interaction, stopped.Token).GetAwaiter().GetResult();
                if (selected is null) { output.WriteLine("Selección cancelada; no se creó operación."); return 0; }
                var operation = installer.Create(selected);
                active = operation.Id;
                output.WriteLine($"Operación {operation.Id:D} en primer plano.");
                using var provider = transport is null ? new HuggingFaceTransport() : null;
                var result = installer.Downloads.RunAsync(operation.Id, transport ?? provider!, progress: value => DownloadCli.Show(value, output),
                    cancellation: stopped.Token, finalize: installer.FinalizeAsync, beforeCancel: installer.CancelAsync).GetAwaiter().GetResult();
                if (result is null)
                {
                    output.WriteLine($"{operation.Id:D}: {(installer.IsInstalled(operation.ModelInstall!.ModelId) ? "instalación" : "cancelación")} y limpieza completadas."); return 0;
                }
                if (result.State is "paused" or "failed") output.WriteLine($"coquena download resume {operation.Id:D}");
                return result.State == "failed" ? 1 : 0;
            }
            finally { Console.CancelKeyPress -= handler; }
        }
        catch (Exception e) when (e is ModelSourceException or DownloadException or StorageException or ContractException or IOException or UnauthorizedAccessException or OperationCanceledException or ArgumentException or NotSupportedException)
        {
            var code = e switch { ModelSourceException s => s.Code, DownloadException d => d.Code, StorageException s => s.Code,
                ContractException c => c.Code, OperationCanceledException => "selection-interrupted", _ => "model-io-failed" };
            error.WriteLine($"Error: {code}. {e.Message} Consulte coquena --help.");
            if (active is { } id) output.WriteLine($"coquena download resume {id:D}");
            return 1;
        }
        void Show(LocalModel entry)
        {
            output.WriteLine($"{entry.Record.Id:D} | {entry.Record.Name} | {entry.Record.Ownership} | commit {entry.Record.Selection?.Commit ?? "no aplica"} | {entry.Availability}");
            foreach (var file in entry.Record.Files) output.WriteLine($"  {file.LocalPath} | {(entry.Record.Ownership == "external" && file.LastWriteUtc is null ? "tamaño no observado" : $"{file.Size} bytes")}");
            if (entry.Changed) output.WriteLine("  Aviso: tamaño/fecha cambiaron; no se redefine integridad.");
            if (entry.Record.IntegrityCheck is { } check) output.WriteLine($"  Última integridad: {check.Status} | {check.CheckedAt:O}");
            if (entry.Record.InformativeMatch is { } match)
                output.WriteLine($"  Equivalencia informativa: {match.Candidate.Repository}@{match.Candidate.Commit} | {string.Join(", ", match.Candidate.Files.Select(f => f.Path))} | no certificada; sin seguimiento");
            if (entry.Record.Update is { } update)
                output.WriteLine($"  Seguimiento: {update.State} | último intento {update.LastAttempt:O} | último éxito {update.LastSuccess:O} | {update.Diagnostic}");
            foreach (var diagnostic in entry.Diagnostics ?? []) output.WriteLine("  " + diagnostic);
            if (entry.Availability != "available") output.WriteLine("  No servible: revisar rutas, lectura y cabeceras GGUF; no se reparan archivos externos.");
        }
    }

    private sealed class Interaction(TextWriter output) : IModelSelectionInteraction
    {
        public bool IsInteractive => !Console.IsInputRedirected;
        public void Show(string text) => output.WriteLine(text);
        public async Task<int?> ChooseAsync(IReadOnlyList<string> options, CancellationToken cancellation)
        {
            for (var i = 0; i < options.Count; i++) output.WriteLine($"{i + 1}. {options[i]}");
            output.Write("Opción: ");
            var line = await Console.In.ReadLineAsync(cancellation);
            return line is null ? null : int.TryParse(line, out var n) && n > 0 && n <= options.Count ? n - 1 : -1;
        }
    }
}
