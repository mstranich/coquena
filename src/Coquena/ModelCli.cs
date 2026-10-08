using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Models;
using Coquena.Storage;

namespace Coquena;

public static class ModelCli
{
    public static int Run(string[] args, TextWriter output, TextWriter error, ModelInstaller? installer = null,
        HuggingFaceStore? source = null, IDownloadTransport? transport = null,
        IModelSelectionInteraction? interaction = null, CancellationToken cancellation = default)
    {
        Guid? active = null;
        if (!(args.Length == 1 && args[0] == "list" || args.Length == 2 && args[0] == "download"))
        { error.WriteLine("Error: argumentos de model inválidos. Consulte coquena --help."); return 2; }
        try
        {
            // Validate before any local or remote side effects.
            var request = args[0] == "download" ? ModelRequest.Parse(args[1]) : null;
            if (request?.CompanionType is not null) throw new ModelSourceException("outside-m1", "Companions fuera de M1.");
            installer ??= new ModelInstaller(Home.Resolve());
            if (args[0] == "list")
            {
                var entries = installer.List();
                foreach (var entry in entries)
                {
                    output.WriteLine($"{entry.Record.Id:D} | {entry.Record.Name} | {entry.Availability} | commit {entry.Record.Selection.Commit}");
                    foreach (var file in entry.Record.Files) output.WriteLine($"  {file.LocalPath} | {file.Size} bytes");
                    if (entry.Changed) output.WriteLine("  Aviso: tamaño/fecha cambiaron; no se redefine integridad.");
                }
                if (entries.Length == 0) output.WriteLine("Sin modelos instalados.");
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
                var selected = new ModelSelectionMenu(source).SelectAsync(request!, interaction, stopped.Token).GetAwaiter().GetResult();
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
        catch (Exception e) when (e is ModelSourceException or DownloadException or StorageException or ContractException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            var code = e switch { ModelSourceException s => s.Code, DownloadException d => d.Code, StorageException s => s.Code,
                ContractException c => c.Code, OperationCanceledException => "selection-interrupted", _ => "model-io-failed" };
            error.WriteLine($"Error: {code}. Consulte coquena --help.");
            if (active is { } id) output.WriteLine($"coquena download resume {id:D}");
            return 1;
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
