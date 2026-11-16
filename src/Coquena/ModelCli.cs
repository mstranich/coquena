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
        if (args.Length > 0 && args[0] == "remove")
            return ResourceRemoval.Cli(args[1..], output, error, backend: false, companion: companion);
        if (!(args.Length == 1 && args[0] == "list" || args.Length == 2 && args[0] == "download" ||
            args.Length is 1 or 2 && args[0] is "update" or "upgrade" ||
            args.Length == 2 && args[0] is "refresh" or "verify" ||
            args.Length == 3 && args[0] == "verify" && args[2] == "--set-baseline" ||
            args.Length is 4 or 5 && args[0] == "add" && args[2] == "--name" &&
                (args.Length == 4 || args[4] == "--try-match")))
        { error.WriteLine(Messages.ModelInvalidArguments); return 2; }
        try
        {
            // Validate before any local or remote side effects.
            var request = args[0] == "download" ? ModelRequest.Parse(args[1]) : null;
            if (request is not null && companion != (request.CompanionType is not null)) throw new ModelSourceException("invalid-selector", Messages.ModelDownloadKind);
            installer ??= new ModelInstaller(Home.Resolve());
            if (args[0] == "upgrade")
            {
                var units = installer.Updates.Scope(args.Length == 2 ? args[1] : null, companion);
                using var stoppedUpgrade = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                ConsoleCancelEventHandler upgradeHandler = (_, e) => { e.Cancel = true; stoppedUpgrade.Cancel(); };
                Console.CancelKeyPress += upgradeHandler;
                using var provider = transport is null ? new HuggingFaceTransport() : null;
                var failed = false;
                try
                {
                    foreach (var unit in units)
                    {
                        if (stoppedUpgrade.IsCancellationRequested) break;
                        if (unit.Ownership == "external") { output.WriteLine($"{unit.Name} | {Messages.ExternalNotApplicable}."); continue; }
                        if (unit.Update is not { State: "update-available", Plan: not null })
                        {
                            output.WriteLine($"{unit.Name} | {unit.Update?.State ?? Messages.ModelNoPlan}");
                            failed |= unit.Update?.State != "unchanged";
                            continue;
                        }
                        Guid? operationId = null;
                        try
                        {
                            var operation = installer.CreateUpgrade(unit); operationId = operation.Id;
                            output.WriteLine(Messages.ModelOperation.Format(unit.Name, operation.Id.ToString("D"), operation.ModelSelection!.Commit));
                            var result = installer.Downloads.RunAsync(operation.Id, transport ?? provider!, progress: o => DownloadCli.Show(o, output),
                                cancellation: stoppedUpgrade.Token, finalize: installer.FinalizeAsync, beforeCancel: installer.CancelAsync).GetAwaiter().GetResult();
                            if (result is null) output.WriteLine(Messages.ModelUpgradeDone.Format(unit.Name));
                            else { output.WriteLine($"coquena download resume {operation.Id:D}"); failed |= result.State == "failed"; }
                        }
                        catch (Exception e) when (e is DownloadException or StorageException or ContractException or IOException or UnauthorizedAccessException or ModelSourceException)
                        {
                            output.WriteLine(Messages.ModelUnitFailure.Format(unit.Name, e.Message)); failed = true;
                            if (operationId is { } id) output.WriteLine($"coquena download resume {id:D}");
                        }
                    }
                    if (units.Length == 0) output.WriteLine(Messages.NoUnits);
                    return failed ? 1 : 0;
                }
                finally { Console.CancelKeyPress -= upgradeHandler; }
            }
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
                        if (unit.Ownership == "external") { output.WriteLine(Messages.ModelExternalNoTracking.Format(unit.Name, Messages.ExternalNotApplicable)); continue; }
                        try
                        {
                            var checkedUnit = installer.Updates.CheckAsync(unit, source, interaction ?? new Interaction(output), stoppedUpdate.Token, inspector).GetAwaiter().GetResult();
                            var update = checkedUnit.Update!;
                            output.WriteLine(Messages.ModelUpdateRow.Format(unit.Name, update.State, update.LastAttempt.ToString("O"), update.LastSuccess?.ToString("O"), update.Diagnostic));
                            if (update.State == "query-failed") output.WriteLine(Messages.ModelPreviousObservation.Format(update.LastSuccessfulState ?? Messages.ModelNoSuccessfulQuery, update.LastSuccessfulDiagnostic));
                            if (update.Plan is { } plan)
                            {
                                output.WriteLine($"  Plan: {plan.TargetName} @ {plan.Target.Commit}");
                                foreach (var action in plan.Actions) output.WriteLine(Messages.ModelPlanAction.Format(action.TargetPath, action.Action, action.SourcePath ?? Messages.ModelNewContent));
                            }
                            failed |= update.State is "query-failed" or "intervention-required" or "orphaned";
                        }
                        catch (Exception e) when (e is ModelSourceException or StorageException or ContractException or IOException or UnauthorizedAccessException)
                        { output.WriteLine(Messages.ModelUnitError.Format(unit.Name, e.Message)); failed = true; }
                    }
                    if (units.Length == 0) output.WriteLine(Messages.NoUnits);
                    return failed ? 1 : 0;
                }
                finally { Console.CancelKeyPress -= updateHandler; }
            }
            if (args[0] == "refresh")
            {
                var entry = installer.Refresh(args[1], companion);
                output.WriteLine(Messages.DisplayLocalObservationsUpdatedO.Format(entry.Record.RefreshedAt));
                Show(entry);
                return entry.Availability == "available" ? 0 : 1;
            }
            if (args[0] == "verify")
            {
                var baseline = args.Length == 3;
                if (baseline) output.WriteLine(Messages.ModelBaselineSaved);
                using var stoppedVerify = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
                ConsoleCancelEventHandler verifyHandler = (_, e) => { e.Cancel = true; stoppedVerify.Cancel(); };
                Console.CancelKeyPress += verifyHandler;
                try
                {
                    var record = installer.VerifyAsync(args[1], companion, baseline, stoppedVerify.Token).GetAwaiter().GetResult();
                    var check = record.IntegrityCheck!;
                    output.WriteLine(Messages.DisplayIntegrityO.Format(record.Name, check.Status, check.CheckedAt));
                    foreach (var file in check.Files) output.WriteLine($"  {file.LocalPath} | {file.Status} | {file.Diagnostic}");
                    return check.Status is "verified" or "baseline-set" ? 0 : 1;
                }
                finally { Console.CancelKeyPress -= verifyHandler; }
            }
            if (args[0] == "add")
            {
                var record = installer.AddExternal(args[1], args[3], companion);
                output.WriteLine(Messages.DisplayExternalResourceRegisteredD.Format(record.Id, record.Name));
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
                            output.WriteLine(Messages.ModelMatchSaved);
                        }
                        else output.WriteLine(Messages.DisplayLocalRegistrationPreserved.Format(Messages.NotPaired));
                    }
                    catch (Exception e) when (e is ModelSourceException or StorageException or ContractException or IOException or UnauthorizedAccessException or OperationCanceledException or HttpRequestException)
                    { output.WriteLine(Messages.DisplayLocalRegistrationPreservedDetail.Format(Messages.NotPaired, e.Message)); }
                    finally { Console.CancelKeyPress -= matchHandler; }
                }
                return 0;
            }
            if (args[0] == "list")
            {
                var entries = installer.List().Where(m => companion ? m.Record.CompanionType is not null : m.Record.CompanionType is null).ToArray();
                foreach (var entry in entries) Show(entry);
                if (entries.Length == 0) output.WriteLine(companion ? Messages.CompanionNoneRegistered : Messages.ModelNoneInstalled);
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
                if (selected is null) { output.WriteLine(Messages.ModelSelectionCancelled); return 0; }
                var previous = installer.ExternalTransition(selected.Name);
                if (previous is not null)
                {
                    interaction.Show(Messages.ModelRegisteredExternal.Format(previous.Name));
                    int? choice;
                    do
                    {
                        choice = interaction.IsInteractive ? interaction.ChooseAsync(
                            [Messages.ModelDownloadManaged, Messages.ModelKeepExternalCancel], stopped.Token).GetAwaiter().GetResult() : null;
                        if (choice is not null && choice is not (0 or 1)) interaction.Show(Messages.InvalidOption);
                    } while (choice is not null && choice is not (0 or 1));
                    if (choice != 0) { output.WriteLine(Messages.ModelTransitionCancelled); return 0; }
                }
                var operation = installer.Create(selected, previous);
                active = operation.Id;
                output.WriteLine(Messages.OperationForeground.Format(operation.Id.ToString("D")));
                using var provider = transport is null ? new HuggingFaceTransport() : null;
                var result = installer.Downloads.RunAsync(operation.Id, transport ?? provider!, progress: value => DownloadCli.Show(value, output),
                    cancellation: stopped.Token, finalize: installer.FinalizeAsync, beforeCancel: installer.CancelAsync).GetAwaiter().GetResult();
                if (result is null)
                {
                    output.WriteLine(Messages.OperationFinished.Format(operation.Id.ToString("D"), Messages.InstallationOrCancellation(installer.IsInstalled(operation.ModelInstall!.ModelId)), Messages.CleanupCompleted)); return 0;
                }
                if (result.State is "paused" or "failed") output.WriteLine($"coquena download resume {operation.Id:D}");
                return result.State == "failed" ? 1 : 0;
            }
            finally { Console.CancelKeyPress -= handler; }
        }
        catch (Exception e) when (e is ModelSourceException or DownloadException or StorageException or ContractException or IOException or UnauthorizedAccessException or OperationCanceledException or ArgumentException or NotSupportedException or Coquena.Configuration.ConfigurationException)
        {
            var code = e switch { ModelSourceException s => s.Code, DownloadException d => d.Code, StorageException s => s.Code,
                ContractException c => c.Code, OperationCanceledException => "selection-interrupted", _ => "model-io-failed" };
            error.WriteLine(Messages.ErrorCodeMessageSeeHelp.Format(code, e.Message));
            if (active is { } id) output.WriteLine($"coquena download resume {id:D}");
            return 1;
        }
        void Show(LocalModel entry)
        {
            output.WriteLine(Messages.ModelRow.Format(entry.Record.Id.ToString("D"), entry.Record.Name, entry.Record.Ownership, entry.Record.Selection?.Commit ?? Messages.NotApplicable, entry.Availability));
            foreach (var file in entry.Record.Files) output.WriteLine($"  {file.LocalPath} | {(entry.Record.Ownership == "external" && file.LastWriteUtc is null ? Messages.ModelSizeNotObserved : ByteSize.Format(file.Size))}");
            if (entry.Changed) output.WriteLine(Messages.ModelChangedWarning);
            if (entry.Record.IntegrityCheck is { } check) output.WriteLine(Messages.ModelLastIntegrity.Format(check.Status, check.CheckedAt.ToString("O")));
            if (entry.Record.InformativeMatch is { } match)
                output.WriteLine(Messages.ModelInformativeMatch.Format(match.Candidate.Repository, match.Candidate.Commit, string.Join(", ", match.Candidate.Files.Select(f => f.Path))));
            if (entry.Record.Update is { } update)
                output.WriteLine(Messages.ModelTracking.Format(update.State, update.LastAttempt.ToString("O"), update.LastSuccess?.ToString("O"), update.Diagnostic));
            foreach (var diagnostic in entry.Diagnostics ?? []) output.WriteLine("  " + diagnostic);
            if (entry.Availability != "available") output.WriteLine(Messages.ModelNotServable);
        }
    }

    private sealed class Interaction(TextWriter output) : IModelSelectionInteraction
    {
        public bool IsInteractive => !Console.IsInputRedirected;
        public void Show(string text) => output.WriteLine(text);
        public async Task<int?> ChooseAsync(IReadOnlyList<string> options, CancellationToken cancellation)
        {
            for (var i = 0; i < options.Count; i++) output.WriteLine($"{i + 1}. {options[i]}");
            output.Write(Messages.OptionPrompt);
            var line = await Console.In.ReadLineAsync(cancellation);
            return line is null ? null : int.TryParse(line, out var n) && n > 0 && n <= options.Count ? n - 1 : -1;
        }
    }
}
