using Coquena.Backends;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Storage;
using Coquena.Models;

namespace Coquena;

public static class DownloadCli
{
    // Consumers inject their pinned-location transport when integrated in 006/009.
    public static int Run(string[] args, TextWriter output, TextWriter error,
        DownloadManager? manager = null, IDownloadTransport? transport = null,
        TextReader? input = null, CancellationToken cancellation = default)
    {
        if (!Valid(args))
        {
            error.WriteLine(Messages.DownloadInvalidOptions); return 2;
        }
        try
        {
            manager ??= new DownloadManager(Home.Resolve());
            if (args[0] == "list" || (args[0] == "status" && args.Length == 1))
            {
                var values = manager.List(args.Length == 2 ? args[1][2..] : null);
                foreach (var value in values) Show(value, output);
                if (values.Length == 0) output.WriteLine(Messages.DownloadNone);
                return 0;
            }
            var id = Guid.Parse(args[1]);
            if (args[0] == "status") { Show(manager.Status(id), output); return 0; }
            if (args[0] is "pause" or "cancel")
            {
                var current = manager.Status(id);
                var consumer = current.BackendInstall is not null ? new BackendInstaller(manager.StorageHome, retentionReport: output.WriteLine) : null;
                var modelConsumer = current.ModelInstall is not null ? new ModelInstaller(manager.StorageHome) : null;
                manager.ControlAsync(id, args[0] == "cancel", cancellation, consumer is not null ? consumer.CancelAsync : modelConsumer is null ? null : modelConsumer.CancelAsync).GetAwaiter().GetResult();
                var published = consumer is not null && consumer.Published(current) ||
                    modelConsumer is not null && modelConsumer.Published(current);
                output.WriteLine(Messages.DownloadDone.Format(id.ToString("D"), args[0] == "cancel" ? published ? Messages.DownloadPublishedNoRevert : Messages.DownloadCancelCleanup : Messages.DownloadPauseSaved));
                if (args[0] == "pause") output.WriteLine($"coquena download resume {id:D}");
                return 0;
            }
            var operation = manager.Status(id);
            BackendInstaller? installer = null;
            OfficialArtifactTransport? official = null;
            ModelInstaller? modelInstaller = operation.ModelInstall is not null ? new ModelInstaller(manager.StorageHome) : null;
            HuggingFaceTransport? hf = null;
            if (transport is null && operation.ResourceKind == "backend" && operation.BackendInstall is not null)
            {
                installer = new BackendInstaller(manager.StorageHome, retentionReport: output.WriteLine);
                official = new OfficialArtifactTransport(); transport = official;
            }
            using var officialLifetime = official;
            if (modelInstaller is not null)
            {
                modelInstaller.CheckSpace(operation);
                if (transport is null) { hf = new(); transport = hf; }
            }
            using var hfLifetime = hf;
            if (transport is null)
                throw new DownloadException("provider-not-integrated", Messages.DownloadProviderMissing);
            using var stopped = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; stopped.Cancel(); };
            Console.CancelKeyPress += handler;
            try
            {
                output.WriteLine(Messages.OperationForeground.Format(id.ToString("D")));
                var result = manager.RunAsync(id, transport, async (_, index, token) =>
                {
                    // No input means no destructive restart. Redirection is never consent.
                    if (input is null && Console.IsInputRedirected) return false;
                    output.WriteLine(Messages.DownloadRangeRestart.Format(index + 1));
                    var line = await (input ?? Console.In).ReadLineAsync(token);
                    return Messages.IsYes(line);
                }, value => Show(value, output), stopped.Token,
                    installer is not null ? installer.FinalizeAsync : modelInstaller is null ? null : modelInstaller.FinalizeAsync,
                    installer is not null ? installer.CancelAsync : modelInstaller is null ? null : modelInstaller.CancelAsync).GetAwaiter().GetResult();
                if (result is null)
                {
                    var installed = installer is not null && installer.Published(operation) ||
                        modelInstaller is not null && modelInstaller.Published(operation);
                    output.WriteLine(Messages.OperationFinished.Format(id.ToString("D"), Messages.InstallationOrCancellation(installed), Messages.CleanupCompleted)); return 0;
                }
                if (result.State is "paused" or "failed") output.WriteLine($"coquena download resume {id:D}");
                return result.State == "failed" ? 1 : 0;
            }
            finally { Console.CancelKeyPress -= handler; }
        }
        catch (Exception e) when (e is DownloadException or StorageException or ContractException or ModelSourceException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            var code = e switch { DownloadException d => d.Code, StorageException s => s.Code,
                ContractException c => c.Code, ModelSourceException m => m.Code, OperationCanceledException => "control-not-confirmed", _ => "local-io-failed" };
            error.WriteLine(Messages.DownloadNotConfirmed.Format(code));
            return 1;
        }
    }

    private static bool Valid(string[] args) =>
        (args.Length == 1 && args[0] is "list" or "status") ||
        (args.Length == 2 && args[0] == "list" && args[1] is "--model" or "--companion" or "--backend") ||
        (args.Length == 2 && args[0] is "status" or "pause" or "resume" or "cancel" && Guid.TryParse(args[1], out var id) && id != Guid.Empty);

    // Keeps the code for diagnosis and says what happened when it is a known transfer problem.
    internal static string ErrorLine(string code)
    {
        if (code == "transfer-cancelled") return Messages.DownloadCancelled;
        if (code == "transfer-paused") return Messages.Error(code, Messages.DownloadPaused);
        var text = Coquena.Observation.DownloadsPresentation.FailureText(code);
        return text is null
            ? Messages.Error(code, Messages.DownloadUnfinished)
            : Messages.DownloadFailedWith.Format(text, code);
    }

    internal static void Show(OperationDocument value, TextWriter output)
    {
        output.WriteLine($"{value.Id:D} | {value.ResourceKind} | {value.Name} | {value.State}/{value.Phase}");
        decimal bytes = 0, total = 0; var known = true;
        foreach (var file in value.Files)
        {
            bytes += file.ConfirmedBytes;
            if (file.ExpectedSize is { } size) total += size; else known = false;
            output.WriteLine($"  {file.SourcePath}: {ByteSize.Format(file.ConfirmedBytes)}/{(file.ExpectedSize is long expected ? ByteSize.Format(expected) : Messages.UnknownSize)} | {file.State} | {file.DestinationPath}");
        }
        output.WriteLine($"  Total: {ByteSize.Format(bytes)}/{(known ? ByteSize.Format(total) : Messages.UnknownSize)}");
        if (value.Diagnostic is not null) output.WriteLine(Messages.DownloadDiagnostic.Format(value.Diagnostic));
        if (value.Diagnostic?.StartsWith("checkpoint-write-failed-", StringComparison.Ordinal) == true)
        {
            output.WriteLine(Messages.DownloadProgressNotSaved);
            if (value.Diagnostic.EndsWith("0x80070497", StringComparison.Ordinal))
                output.WriteLine(Messages.DownloadReplaceFailed);
        }
        if (value.LastIncident is { } incident)
        {
            output.WriteLine(Messages.DownloadLastIncident.Format(incident.At.ToString("O"), incident.Reason, incident.FileIndex + 1, incident.Offset));
            output.WriteLine(Messages.DownloadFailures.Format(incident.ConsecutiveFailures, incident.RetryLimit, incident.WaitMilliseconds));
        }
        if (value.Phase == "resource-validation") output.WriteLine(value.State == "running" ? Messages.DownloadVerifying : Messages.DownloadToVerify);
    }
}
