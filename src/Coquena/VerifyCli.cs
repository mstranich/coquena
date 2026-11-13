using System.Text.Json.Nodes;
using Coquena.Models;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Storage;

namespace Coquena;

public static class VerifyCli
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try { return RunAsync(args, output, cancellation.Token).GetAwaiter().GetResult(); }
        catch (Exception e) when (e is ArgumentException or RecipeException or StorageException or ModelSourceException or
            Coquena.Backends.BackendException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            var code = e switch { RecipeException r => r.Code, StorageException s => s.Code, ModelSourceException m => m.Code,
                Coquena.Backends.BackendException b => b.Code, OperationCanceledException => "cancelled", ArgumentException => "invalid-selection", _ => "verification-io-error" };
            // With --json, an invalid recipe is reported as data on the output, like a report.
            if (e is RecipeProblemsException problems && args.Contains("--json")) { output.WriteLine(problems.ToJson().ToJsonString()); return 2; }
            error.WriteLine(e switch
            {
                RecipeException { Phase: "validate" } invalid => RecipeValidation.Message(invalid),
                RecipeException => $"Error en la receta: {e.Message}",
                _ => $"Error: {code}. La verificación no se completó."
            });
            return e is OperationCanceledException ? 130 : e is ArgumentException or RecipeException or ModelSourceException or Coquena.Backends.BackendException || code == "ambiguous-resource-selector" ? 2 :
                code.Contains("conflict", StringComparison.Ordinal) ? 3 : 7;
        }
        finally { Console.CancelKeyPress -= handler; }
    }

    internal static void WriteReport(VerificationReport report, bool checksum, TextWriter output)
    {
        var checksumRan = checksum && report.Resources.All(resource => resource!["mode"]!.GetValue<string>() == "checksum");
        if (checksum && !checksumRan) output.WriteLine("SHA-256 omitido. Corregir los problemas del catálogo y repetir verify --checksum.");
        output.WriteLine(report.Verified ? Messages.VerifyAllOk(report.Resources.Count, checksumRan) : Messages.VerifyProblems(report.Resources.Count, checksumRan));
        foreach (var resource in report.Resources)
        {
            var status = resource!["status"]!.GetValue<string>();
            output.WriteLine($"  {resource["kind"]} {resource["key"]} — {Describe(status, checksumRan)}");
            foreach (var file in resource["files"]!.AsArray())
            {
                var fileStatus = file!["status"]!.GetValue<string>();
                if (fileStatus is not ("verified" or "quick-ok"))
                    output.WriteLine($"    {file["path"]} — {Describe(fileStatus, checksumRan)}");
            }
            if (resource["known-failures"] is JsonObject failures)
                foreach (var failure in failures)
                    output.WriteLine($"    Fallo pendiente ({(failure.Key switch { "$unit" => "estructura", "$integrity" => "integridad", _ => failure.Key })}). Revisar el recurso y repetir con --checksum.");
        }
    }

    private static string Describe(string status, bool checksum) => Messages.VerifyStatus(status, checksum);

    internal static async Task<int> RunAsync(string[] args, TextWriter output, CancellationToken cancellation)
    {
        var files = new List<string>(); var env = new List<string>(); var selectors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(); string? basePath = null; bool global = false, unused = false, privateSelection = false, checksum = false, json = false;
        foreach (var arg in args) if (arg is "--help" or "-h")
        { output.WriteLine("verify [-f archivo] [--env-file archivo] [--project-directory ruta] [--select referencia]... [--checksum] [--json]\nverify --global [--select referencia]... [--include-unused] [--checksum] [--json]\nReferencias: backend://variante, backend://alias, backend://proyecto/alias, model://clave, companion://clave"); return 0; }
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i] switch { "-g" => "--global", "-f" => "--file", "--base" => "--project-directory", _ => args[i] };
            if (arg == "verify") { if (!seen.Add(arg)) throw new ArgumentException(); continue; }
            if (arg is "--global" or "--include-unused" or "--private" or "--checksum" or "--json")
            {
                if (!seen.Add(arg)) throw new ArgumentException();
                global |= arg == "--global"; unused |= arg == "--include-unused"; privateSelection |= arg == "--private";
                checksum |= arg == "--checksum"; json |= arg == "--json";
            }
            else if (arg is "--file" or "--env-file" or "--project-directory" or "--select")
            {
                if (++i == args.Length) throw new ArgumentException();
                if (arg == "--file") files.Add(args[i]); else if (arg == "--env-file") env.Add(args[i]);
                else if (arg == "--select") selectors.Add(args[i]); else { if (basePath is not null) throw new ArgumentException(); basePath = args[i]; }
            }
            else throw new ArgumentException();
        }
        if (global && (files.Count > 0 || env.Count > 0 || basePath is not null)) throw new ArgumentException();
        if (!global && unused) throw new ArgumentException("--include-unused requiere --global.");
        var filters = selectors.Select(ResourceSelector.Parse).ToArray();
        var selection = global ? null : new RecipeSelection(Environment.CurrentDirectory, files, basePath, env);
        var context = global ? ScopeContext.Global(Home.Resolve()) : ScopeContext.Project(selection!.Files[0], selection.Base);
        var selected = new List<VerificationSelection>();
        void AddRequest(EffectiveRecipe recipe, PreparationRequest request, bool privateContext)
        {
            var binding = CombinationPreparation.VerificationBinding(context, recipe, request, privateContext);
            selected.Add(binding with { Request = request });
        }
        if (global)
        {
            var state = new ScopeStore(context).Read();
            var records = state.Documents.GetValueOrDefault("local/recipes.json")?["data"]?["import-resolutions"] as JsonObject ?? [];
            foreach (var record in records)
            {
                var kind = record.Key.StartsWith("service:", StringComparison.Ordinal) ? "backend" : record.Key.Split(':')[0];
                var key = kind == "backend" ? "backend" : record.Key[(kind.Length + 1)..];
                selected.Add(new(key, kind, record.Value!["resource-id"]!.GetValue<string>(), record.Value["installation-id"]!.GetValue<string>()));
            }
            foreach (var service in new RecipeRegistry(new ScopeStore(context)).Services())
            {
                var (recipe, blocks) = RecipeValidation.Require(new RecipeImporter(context).ServiceSelection(service.ServiceId));
                foreach (var request in CombinationPreparation.Requests(recipe, blocks))
                    AddRequest(recipe, request, true);
            }
            foreach (var document in state.Documents.Where(p => p.Key.StartsWith("consumers/", StringComparison.Ordinal) && p.Value["kind"]!.GetValue<string>() == "recipe-intention"))
            {
                var common = state.Documents.ContainsKey("recipe.yaml") ? new[] { context.PathFor("recipe.yaml") } : [];
                var saved = new RecipeSelection(context.Base, common.Append(context.PathFor(document.Key)), context.Base)
                    { ImportedScope = context, FilterUnusedModels = true };
                var (recipe, blocks) = RecipeValidation.Require(saved);
                foreach (var request in CombinationPreparation.Requests(recipe, blocks))
                    AddRequest(recipe, request, true);
            }
        }
        if (global)
        {
            var inventoryData = new ScopeStore(context).Read().Documents.GetValueOrDefault("local/inventory.json")?["data"];
            var inventory = inventoryData?["resources"] as JsonArray ?? [];
            foreach (var alias in inventoryData?["model-aliases"] as JsonArray ?? [])
            {
                var kind = alias!["kind"]!.GetValue<string>(); var key = alias["key"]!.GetValue<string>();
                if (filters.Any(f => f.Kind == kind && f.Name.Equals(key, StringComparison.OrdinalIgnoreCase)))
                    selected.Add(new(key, kind, alias["resource-id"]!.GetValue<string>(), alias["installation-id"]!.GetValue<string>()));
            }
            foreach (var resource in inventory)
            {
                var kind = resource!["kind"]!.GetValue<string>(); var key = kind == "backend" ? "backend" : resource["logical-key"]!.GetValue<string>();
                if (!unused && !filters.Any(f => f.Matches(kind, key, resource, resource["installations"]!.AsArray().LastOrDefault()))) continue;
                foreach (var installation in unused ? resource["installations"]!.AsArray() : resource["installations"]!.AsArray().TakeLast(1))
                    selected.Add(new(key, kind, resource["resource-id"]!.GetValue<string>(), installation!["installation-id"]!.GetValue<string>()));
            }
        }
        else
        {
            // The recipe goes first: YAML and structure, then resources and, if asked, SHA-256.
            var (recipe, blocks) = RecipeValidation.Require(selection!);
            if (recipe.Root.Get("service") is not null) _ = Coquena.Backends.BackendIntent.FromRecipe(recipe);
            foreach (var request in CombinationPreparation.Requests(recipe, blocks))
                AddRequest(recipe, request, privateSelection);
        }
        // Validate every selector before reading any file or writing observations.
        var catalog = new ScopeStore(context).Read().Documents.GetValueOrDefault("local/inventory.json")?["data"]?["resources"] as JsonArray ?? [];
        bool Matches(ResourceSelector filter, VerificationSelection s)
        {
            var resource = catalog.SingleOrDefault(r => r!["resource-id"]!.GetValue<string>() == s.ResourceId);
            var installation = resource?["installations"]?.AsArray().SingleOrDefault(i => i!["installation-id"]!.GetValue<string>() == s.InstallationId);
            return filter.Matches(s.Kind, s.Key, resource, installation, s.Request);
        }
        foreach (var filter in filters)
        {
            if (!selected.Any(s => Matches(filter, s))) throw new ArgumentException("Selector desconocido.");
            filter.CheckAmbiguity(selected.Where(s => Matches(filter, s)).Select(s => s.Request?.ProjectName));
        }
        if (filters.Length > 0) selected = selected.Where(s => filters.Any(f => Matches(f, s))).ToList();
        var report = await new ResourceVerification(context).VerifyAsync(selected, checksum: checksum, cancellation: cancellation);
        // One shape for every outcome: the recipe is valid here; resources carry their status.
        if (json) output.WriteLine(new JsonObject { ["valid"] = true, ["resources"] = report.Resources.DeepClone() }.ToJsonString());
        else WriteReport(report, checksum, output);
        return report.Verified ? 0 : 6;
    }
}
