using Coquena.Backends;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Models;
using Coquena.Projects;
using Coquena.Storage;
using System.Text.Json.Nodes;

namespace Coquena;

public static class ScopeDownloadCli
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            var global = false; var json = false; string? basePath = null; string? firstFile = null;
            var command = new List<string>();
            for (var i = 0; i < args.Length; i++)
            {
                if (args[i] is "--global" or "-g" && !global) global = true;
                else if (args[i] == "--json" && !json) json = true;
                else if (args[i] is "--base" or "-f" or "--file")
                {
                    var option = args[i];
                    if (++i == args.Length) return Invalid(error);
                    if (option == "--base" && basePath is null) basePath = args[i];
                    else if (option is "-f" or "--file" && firstFile is null) firstFile = args[i];
                    else return Invalid(error);
                }
                else command.Add(args[i]);
            }
            if (global && (basePath is not null || firstFile is not null)) return Invalid(error);
            var values = command.ToArray();
            if (!(values.Length == 1 && values[0] is "list" or "status" ||
                values.Length == 2 && values[0] == "list" && values[1] is "--model" or "--companion" or "--backend" ||
                values.Length == 2 && values[0] is "status" or "show" or "pause" or "resume" or "cancel" &&
                    Guid.TryParse(values[1], out var parsed) && parsed != Guid.Empty)) return Invalid(error);
            var context = global ? ScopeContext.Global(Home.Resolve()) :
                ScopeContext.Project(firstFile ?? Path.Combine(basePath ?? Environment.CurrentDirectory, "coquena.yaml"), basePath);
            // No recipe parsing or downloads on a status query; a scope is selected by its base.
            if (!File.Exists(context.PathFor("local/state.json")))
            {
                if (Directory.Exists(context.PathFor("downloads")) && Directory.GetFileSystemEntries(context.PathFor("downloads")).Length != 0)
                    throw new StorageException("missing-state-pointer", "Hay transferencias sin identidad de ámbito.");
                if (values.Length == 1 || values[0] == "list") { if (json) output.WriteLine("[]"); else output.WriteLine("Sin operaciones de descarga."); return 0; }
                throw new DownloadException("not-found", "Operación inexistente en el ámbito seleccionado.");
            }
            var transfers = new ScopeTransfers(context);
            if (values.Length == 1 || values[0] == "list")
            {
                var operations = transfers.Downloads.List(values.Length == 2 ? values[1][2..] : null);
                if (json) output.WriteLine(new JsonArray(operations.Select(Structured).Cast<JsonNode?>().ToArray()).ToJsonString());
                else { foreach (var operation in operations) DownloadCli.Show(operation, output); if (operations.Length == 0) output.WriteLine("Sin operaciones de descarga."); }
                return 0;
            }
            var id = Guid.Parse(values[1]);
            if (values[0] is "status" or "show") { Show(transfers.Downloads.Status(id)); return 0; }
            if (values[0] is "pause" or "cancel")
            {
                transfers.ControlAsync(id, values[0] == "cancel").GetAwaiter().GetResult();
                if (json) output.WriteLine(new JsonObject { ["operation-id"] = id.ToString("D"), ["action"] = values[0], ["confirmed"] = true }.ToJsonString());
                else output.WriteLine($"{id:D}: {values[0]} confirmado en el ámbito seleccionado.");
                return 0;
            }
            var selected = transfers.Downloads.Status(id);
            using var official = selected.ResourceKind == "backend" ? new OfficialArtifactTransport() : null;
            using var huggingFace = selected.ResourceKind == "backend" ? null : new HuggingFaceTransport();
            IDownloadTransport transport = official ?? (IDownloadTransport)huggingFace!;
            using var stopped = new CancellationTokenSource();
            ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; stopped.Cancel(); };
            Console.CancelKeyPress += handler;
            try
            {
                var result = transfers.RunAsync(id, transport, Show, stopped.Token,
                    async (_, index, cancellation) =>
                    {
                        if (json || Console.IsInputRedirected) return false;
                        output.WriteLine($"Archivo {index + 1}: ¿reiniciar con la misma selección? [s/N]");
                        return (await Console.In.ReadLineAsync(cancellation))?.Trim().Equals("s", StringComparison.OrdinalIgnoreCase) == true;
                    }).GetAwaiter().GetResult();
                if (result is not null) Show(result);
                else
                {
                    var published = transfers.Published(selected);
                    if (json) output.WriteLine(new JsonObject { ["operation-id"] = id.ToString("D"), ["state"] = published ? "completed" : "cancelled" }.ToJsonString());
                    else output.WriteLine($"{id:D}: {(published ? "instalación publicada y limpieza completada" : "cancelación y limpieza completadas")}.");
                }
                return result?.State == "failed" ? 1 : 0;
            }
            finally { Console.CancelKeyPress -= handler; }

            void Show(OperationDocument operation)
            { if (json) output.WriteLine(Structured(operation).ToJsonString()); else DownloadCli.Show(operation, output); }
        }
        catch (Exception e) when (e is DownloadException or StorageException or ContractException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            var code = e switch { DownloadException d => d.Code, StorageException s => s.Code, ContractException c => c.Code, _ => "local-io-failed" };
            error.WriteLine($"Error: {code}. Estado conservado en el ámbito seleccionado."); return 1;
        }
    }
    private static int Invalid(TextWriter error) { error.WriteLine("Error: argumentos de download inválidos. Consulte coquena --help."); return 2; }
    private static JsonObject Structured(OperationDocument operation) => JsonNode.Parse(ContractJson.Write(operation, ContractJsonContext.Default.OperationDocument))!.AsObject();
}
