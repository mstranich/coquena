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
        if (!(args.Length == 1 && args[0] == "list" || !companion && args.Length == 2 && args[0] == "download" ||
            args.Length == 4 && args[0] == "add" && args[2] == "--name"))
        { error.WriteLine("Error: argumentos de model inválidos. Consulte coquena --help."); return 2; }
        try
        {
            // Validate before any local or remote side effects.
            var request = args[0] == "download" ? ModelRequest.Parse(args[1]) : null;
            if (request?.CompanionType is not null) throw new ModelSourceException("outside-m1", "Companions fuera de M1.");
            installer ??= new ModelInstaller(Home.Resolve());
            if (args[0] == "add")
            {
                var record = installer.AddExternal(args[1], args[3], companion);
                output.WriteLine($"Registrado externo: {record.Id:D} | {record.Name}");
                Show(installer.List().Single(m => m.Record.Id == record.Id));
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
