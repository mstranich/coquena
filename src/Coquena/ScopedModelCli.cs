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
        if (args.Length == 0) { output.WriteLine("model [companion]: list, show, add, download, verify, baseline, remove, protect, unprotect."); return 0; }
        try
        {
            var action = args[0];
            if (action is not ("list" or "show" or "add" or "download" or "remove" or "protect" or "unprotect" or "verify" or "baseline")) return Invalid(error);
            var global = false; var json = false; var tryMatch = false; var checksum = false; string? basePath = null, value = null, name = null, type = null;
            var files = new List<string>(); var env = new List<string>(); var seen = new HashSet<string>();
            for (var i = 1; i < args.Length; i++)
            {
                var option = args[i] switch { "-g" => "--global", "-f" => "--file", _ => args[i] };
                if (option is "--global" or "--json" or "--try-match" or "--checksum")
                {
                    if (!seen.Add(option) || option == "--checksum" && action != "verify") return Invalid(error);
                    if (option == "--global") global = true; else if (option == "--json") json = true; else if (option == "--try-match") tryMatch = true; else checksum = true;
                }
                else if (option is "--base" or "--file" or "--env-file" or "--name" or "--type")
                {
                    if (++i == args.Length || option is "--base" or "--name" or "--type" && !seen.Add(option)) return Invalid(error);
                    if (option == "--base") basePath = args[i]; else if (option == "--name") name = args[i]; else if (option == "--type") type = args[i]; else if (option == "--file") files.Add(args[i]); else env.Add(args[i]);
                }
                else if (!option.StartsWith('-') && value is null) value = option;
                else return Invalid(error);
            }
            if (global && (basePath is not null || files.Count > 0 || env.Count > 0) || action == "list" && value is not null ||
                action is "add" or "download" or "remove" or "protect" or "unprotect" && value is null ||
                action == "add" && name is null || action != "add" && (name is not null || tryMatch || type is not null)) return Invalid(error);
            ModelRequest? request = action == "download" ? ModelRequest.Parse(value!) : null;
            if (request is not null && companion != (request.CompanionType is not null)) return Invalid(error);
            if (action is "protect" or "unprotect" && (!Guid.TryParseExact(value, "D", out var id) || id == Guid.Empty)) return Invalid(error);
            var selection = global ? null : new RecipeSelection(Environment.CurrentDirectory, files, basePath, env);
            var context = global ? ScopeContext.Global(Home.Resolve()) : ScopeContext.Project(selection!.Files[0], selection.Base);
            var models = new ScopedModels(context);
            if (action is "verify" or "baseline")
            {
                if (value is null || tryMatch || name is not null || type is not null) return Invalid(error);
                value = models.ResolveKey(value, companion);
                var resource = models.List(companion).SingleOrDefault(r => r!["logical-key"]!.GetValue<string>().Equals(value, StringComparison.OrdinalIgnoreCase) || r["resource-id"]!.GetValue<string>() == value)
                    ?? throw new ModelSourceException("unknown-resource", "Recurso inexistente en el ámbito.");
                var installation = resource["installations"]!.AsArray().Last()!;
                var verifier = new ResourceVerification(context);
                var resourceId = resource["resource-id"]!.GetValue<string>(); var installationId = installation["installation-id"]!.GetValue<string>();
                if (action == "baseline")
                {
                    var baseline = verifier.SetBaselineAsync(resourceId, installationId).GetAwaiter().GetResult();
                    output.WriteLine(json ? baseline.ToJsonString() : Messages.BaselineSet(value!));
                    return 0;
                }
                var report = verifier.VerifyAsync([new(value!, companion ? "companion" : "model", resourceId, installationId)], checksum: checksum).GetAwaiter().GetResult();
                if (json) output.WriteLine(report.Resources.ToJsonString()); else VerifyCli.WriteReport(report, checksum, output);
                return report.Verified ? 0 : 6;
            }
            if (action == "add")
            {
                var added = models.AddExternal(value!, name!, companion, type: type,
                    projectName: !global && File.Exists(selection!.Files[0]) ? RecipeReader.ProjectName(selection) : null);
                if (json) output.WriteLine(new JsonObject { ["resource-id"] = added.ResourceId, ["installation-id"] = added.InstallationId,
                    ["ownership"] = "external", ["provenance-certified"] = false }.ToJsonString());
                else output.WriteLine(Messages.ExternalAdded(name!, added.InstallationId));
                if (tryMatch)
                {
                    using var matchClient = source is null ? HuggingFaceStore.CreateClient() : null;
                    try { models.TryMatchAsync(added, source ?? new(matchClient!), interaction ?? new SelectionInteraction(output, !Console.IsInputRedirected, json)).GetAwaiter().GetResult(); }
                    catch (Exception e) when (e is ModelSourceException or System.Net.Http.HttpRequestException or IOException or OperationCanceledException)
                    { error.WriteLine("Aviso: emparejamiento no confirmado; alta externa conservada y utilizable según disponibilidad."); }
                }
                return 0;
            }
            if (action == "show" && !global)
            {
                var recipe = RecipeReader.Read(selection!);
                if (value is not null && File.Exists(context.PathFor("local/state.json"))) value = models.ResolveKey(value, companion);
                if (value?.StartsWith(recipe.Name + "/", StringComparison.Ordinal) == true) value = value[(recipe.Name.Length + 1)..];
                var entries = recipe.Root.Get(companion ? "companions" : "models");
                var shown = value is null ? entries?.Redacted() ?? new JsonObject()
                    : entries?.Get(value)?.Redacted() ?? throw new ModelSourceException("unknown-alias", "Alias inexistente.");
                if (json) output.WriteLine(shown.ToJsonString()); else TextTree.Write(shown, output);
                return 0;
            }
            if (action is "show" or "list")
            {
                var resources = models.Query(companion);
                if (value is not null) resources = new JsonArray(resources.Where(r => r!["resource-id"]?.GetValue<string>() == value ||
                    r["logical-key"]!.GetValue<string>().Equals(value, StringComparison.OrdinalIgnoreCase) ||
                    r["identity"]?.GetValue<string>().Equals(value, StringComparison.OrdinalIgnoreCase) == true).Select(r => r!.DeepClone()).ToArray());
                if (json) output.WriteLine(resources.ToJsonString());
                else if (action == "show") TextTree.Write(resources, output);
                else
                {
                    foreach (var resource in resources)
                        foreach (var installation in resource!["installations"]!.AsArray())
                            output.WriteLine($"{resource["identity"]!.GetValue<string>()} | {resource["resource-id"]!.GetValue<string>()} | {installation!["installation-id"]!.GetValue<string>()} | {installation["observed-availability"]!.GetValue<string>()} | cambio={installation["changed"]!.GetValue<bool>()}" +
                                (resource["ownership"]?.GetValue<string>() == "external" ? (installation["baseline"] is null ? " | externo; sin baseline de integridad" : " | externo; baseline local sin procedencia certificada") +
                                    (resource["informative-match"] is null ? "" : "; equivalencia informativa no certificada") : ""));
                    foreach (var operation in resources.SelectMany(r => r!["operations"] as JsonArray ?? []))
                        output.WriteLine($"{operation!["id"]!.GetValue<string>()} | {operation["state"]!.GetValue<string>()} | {operation["phase"]!.GetValue<string>()}");
                    if (resources.Count == 0) output.WriteLine(companion ? "No hay auxiliares instalados aquí." : "No hay modelos instalados aquí.");
                }
                return 0;
            }
            if (action is "protect" or "unprotect")
            {
                // Check role before changing protection through the shared inventory API.
                if (!models.List(companion).Any(r => r!["installations"]!.AsArray().Any(i => i!["installation-id"]!.GetValue<string>() == value)))
                    throw new ModelSourceException("unknown-installation", "Instalación inexistente en esta categoría.");
                models.Protect(value!, action == "protect");
                if (json) output.WriteLine(new JsonObject { ["installation-id"] = value, ["protected"] = action == "protect", ["references-preserved"] = true }.ToJsonString());
                else output.WriteLine(Messages.ProtectionChanged(value!, action == "protect"));
                return 0;
            }
            if (action == "remove")
            {
                var result = models.Remove(value!, companion);
                if (json) output.WriteLine(new JsonObject { ["complete"] = result.Complete, ["freed-bytes"] = result.FreedBytes,
                    ["pending"] = new JsonArray(result.Pending.Select(p => (JsonNode?)JsonValue.Create(p)).ToArray()) }.ToJsonString());
                else
                {
                    output.WriteLine(Messages.PruneFreed(result.FreedBytes));
                    foreach (var pending in result.Pending) output.WriteLine(Messages.RemovePending(pending));
                }
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
                var external = models.List(companion).SingleOrDefault(r => r!["logical-key"]!.GetValue<string>().Equals(selected.Name, StringComparison.OrdinalIgnoreCase) && r["ownership"]!.GetValue<string>() == "external");
                var replace = false;
                if (external is not null)
                {
                    var prompt = interaction ?? new SelectionInteraction(output, !Console.IsInputRedirected, json);
                    if (!prompt.IsInteractive) throw new ModelSourceException("interaction-required", "La transición requiere confirmación explícita.");
                    var choice = prompt.ChooseAsync(["Descargar copia gestionada conservando los archivos externos", "Cancelar; conservar registro externo"], stopped.Token).GetAwaiter().GetResult();
                    if (choice is null or 1) return 0;
                    if (choice != 0) throw new ModelSourceException("invalid-choice", "Selección inválida.");
                    replace = true;
                }
                var result = models.AcquireAsync(selected, transport ?? provider!, progress: operation =>
                {
                    if (json) output.WriteLine(ContractJson.Write(operation, ContractJsonContext.Default.OperationDocument));
                    else DownloadCli.Show(operation, output);
                }, cancellation: stopped.Token, replaceExternal: replace).GetAwaiter().GetResult();
                if (json) output.WriteLine(new JsonObject { ["resource-id"] = result.ResourceId, ["installation-id"] = result.InstallationId }.ToJsonString());
                else output.WriteLine(Messages.ModelReady(selected.Name, result.InstallationId));
                return 0;
            }
            finally { Console.CancelKeyPress -= handler; }
        }
        catch (Exception e) when (e is ModelSourceException or DownloadException or StorageException or RecipeException or ContractException or IOException or UnauthorizedAccessException or OperationCanceledException or System.Net.Http.HttpRequestException or ArgumentException)
        {
            var code = e switch { ModelSourceException m => m.Code, DownloadException d => d.Code, StorageException s => s.Code,
                RecipeException r => r.Code, ContractException c => c.Code, _ => "model-operation-failed" };
            error.WriteLine(DownloadCli.ErrorLine(code));
            if (e is ModelSourceException { Code: "resource-in-use" }) error.WriteLine(e.Message);
            return e is OperationCanceledException ? 130 : e is ArgumentException || code is "invalid-selector" or "unknown-resource" or "unknown-alias" or "unknown-installation" ? 2 : 1;
        }
    }
    private static int Invalid(TextWriter error) { error.WriteLine("Error: argumentos de model inválidos. Ver coquena --help."); return 2; }
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
