using System.Text.Json.Nodes;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Models;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Storage;

namespace Coquena;

public static class ScopedModelCli
{
    public static int Run(string[] args, TextWriter output, TextWriter error, HuggingFaceStore? source = null,
        IDownloadTransport? transport = null, IModelSelectionInteraction? interaction = null, IRemoteGgufInspector? inspector = null)
    {
        var companion = args.Length > 0 && args[0] == "companion";
        if (companion) args = args[1..];
        if (args.Length == 0) { output.WriteLine("model [companion]: list, show, download, remove, protect, unprotect."); return 0; }
        try
        {
            var action = args[0];
            if (action is not ("list" or "show" or "download" or "remove" or "protect" or "unprotect")) return Invalid(error);
            var global = false; var json = false; string? basePath = null, value = null;
            var files = new List<string>(); var env = new List<string>(); var seen = new HashSet<string>();
            for (var i = 1; i < args.Length; i++)
            {
                var option = args[i] switch { "-g" => "--global", "-f" => "--file", _ => args[i] };
                if (option is "--global" or "--json")
                {
                    if (!seen.Add(option)) return Invalid(error);
                    if (option == "--global") global = true; else json = true;
                }
                else if (option is "--base" or "--file" or "--env-file")
                {
                    if (++i == args.Length || option == "--base" && !seen.Add(option)) return Invalid(error);
                    if (option == "--base") basePath = args[i]; else if (option == "--file") files.Add(args[i]); else env.Add(args[i]);
                }
                else if (!option.StartsWith('-') && value is null) value = option;
                else return Invalid(error);
            }
            if (global && (basePath is not null || files.Count > 0 || env.Count > 0) || action == "list" && value is not null ||
                action is "download" or "remove" or "protect" or "unprotect" && value is null) return Invalid(error);
            ModelRequest? request = action == "download" ? ModelRequest.Parse(value!) : null;
            if (request is not null && companion != (request.CompanionType is not null)) return Invalid(error);
            if (action is "protect" or "unprotect" && (!Guid.TryParseExact(value, "D", out var id) || id == Guid.Empty)) return Invalid(error);
            var selection = global ? null : new RecipeSelection(Environment.CurrentDirectory, files, basePath, env);
            var context = global ? ScopeContext.Global(Home.Resolve()) : ScopeContext.Project(selection!.Files[0], selection.Base);
            var models = new ScopedModels(context);
            if (action == "show" && !global)
            {
                var recipe = RecipeReader.Read(selection!);
                var entries = recipe.Root.Get(companion ? "companions" : "models");
                if (value is null) output.WriteLine(entries?.Redacted()?.ToJsonString() ?? "{}");
                else output.WriteLine(entries?.Get(value)?.Redacted()?.ToJsonString() ?? throw new ModelSourceException("unknown-alias", "Alias inexistente."));
                return 0;
            }
            if (action is "show" or "list")
            {
                var resources = models.Query(companion);
                if (value is not null) resources = new JsonArray(resources.Where(r => r!["resource-id"]?.GetValue<string>() == value ||
                    r["logical-key"]!.GetValue<string>().Equals(value, StringComparison.OrdinalIgnoreCase)).Select(r => r!.DeepClone()).ToArray());
                if (json || action == "show") output.WriteLine(resources.ToJsonString());
                else
                {
                    foreach (var resource in resources)
                        foreach (var installation in resource!["installations"]!.AsArray())
                            output.WriteLine($"{resource["logical-key"]!.GetValue<string>()} | {resource["resource-id"]!.GetValue<string>()} | {installation!["installation-id"]!.GetValue<string>()} | {installation["observed-availability"]!.GetValue<string>()} | cambio={installation["changed"]!.GetValue<bool>()}");
                    foreach (var operation in resources.SelectMany(r => r!["operations"] as JsonArray ?? []))
                        output.WriteLine($"{operation!["id"]!.GetValue<string>()} | {operation["state"]!.GetValue<string>()} | {operation["phase"]!.GetValue<string>()}");
                    if (resources.Count == 0) output.WriteLine("Sin recursos instalados en el ámbito seleccionado.");
                }
                return 0;
            }
            if (action is "protect" or "unprotect")
            {
                // Check role before changing protection through the shared inventory API.
                if (!models.List(companion).Any(r => r!["installations"]!.AsArray().Any(i => i!["installation-id"]!.GetValue<string>() == value)))
                    throw new ModelSourceException("unknown-installation", "Instalación inexistente en esta categoría.");
                models.Protect(value!, action == "protect");
                output.WriteLine(new JsonObject { ["installation-id"] = value, ["protected"] = action == "protect", ["references-preserved"] = true }.ToJsonString());
                return 0;
            }
            if (action == "remove")
            {
                var result = models.Remove(value!, companion);
                output.WriteLine(new JsonObject { ["complete"] = result.Complete, ["freed-bytes"] = result.FreedBytes,
                    ["pending"] = new JsonArray(result.Pending.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray()) }.ToJsonString());
                return result.Complete ? 0 : 1;
            }
            using var client = source is null ? HuggingFaceStore.CreateClient() : null;
            using var provider = transport is null ? new HuggingFaceTransport() : null;
            using var stopped = new CancellationTokenSource();
            ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; stopped.Cancel(); };
            Console.CancelKeyPress += handler;
            try
            {
                var selected = new ModelSelectionMenu(source ?? new(client!), inspector).SelectAsync(request!,
                    interaction ?? new SelectionInteraction(output, !Console.IsInputRedirected, json), stopped.Token).GetAwaiter().GetResult();
                if (selected is null) return 0;
                var result = models.AcquireAsync(selected, transport ?? provider!, progress: operation =>
                {
                    if (json) output.WriteLine(ContractJson.Write(operation, ContractJsonContext.Default.OperationDocument));
                    else DownloadCli.Show(operation, output);
                }, cancellation: stopped.Token).GetAwaiter().GetResult();
                output.WriteLine(new JsonObject { ["resource-id"] = result.ResourceId, ["installation-id"] = result.InstallationId }.ToJsonString());
                return 0;
            }
            finally { Console.CancelKeyPress -= handler; }
        }
        catch (Exception e) when (e is ModelSourceException or DownloadException or StorageException or RecipeException or ContractException or IOException or UnauthorizedAccessException or OperationCanceledException or System.Net.Http.HttpRequestException)
        {
            var code = e switch { ModelSourceException m => m.Code, DownloadException d => d.Code, StorageException s => s.Code,
                RecipeException r => r.Code, ContractException c => c.Code, _ => "model-operation-failed" };
            error.WriteLine($"Error: {code}. Estado conservado en el ámbito seleccionado.");
            if (e is ModelSourceException { Code: "resource-in-use" }) error.WriteLine(e.Message);
            return e is OperationCanceledException ? 130 : code is "invalid-selector" or "unknown-resource" or "unknown-alias" or "unknown-installation" ? 2 : 1;
        }
    }
    private static int Invalid(TextWriter error) { error.WriteLine("Error: argumentos de model inválidos. Consulte coquena --help."); return 2; }
    private sealed class SelectionInteraction(TextWriter output, bool interactive, bool structured) : IModelSelectionInteraction
    {
        public bool IsInteractive => interactive && !structured;
        public void Show(string text) { if (!structured) output.WriteLine(text); }
        public async Task<int?> ChooseAsync(IReadOnlyList<string> options, CancellationToken cancellation)
        {
            for (var i = 0; i < options.Count; i++) output.WriteLine($"{i + 1}. {options[i]}");
            var answer = await Console.In.ReadLineAsync(cancellation);
            return answer is null ? null : int.TryParse(answer, out var index) ? index - 1 : -1;
        }
    }
}
