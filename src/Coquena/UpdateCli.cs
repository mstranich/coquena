using Coquena.Backends;
using Coquena.Models;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Storage;
using System.Text.Json.Nodes;

namespace Coquena;

public static class UpdateCli
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try { return RunAsync(args, output, cancellation.Token).GetAwaiter().GetResult(); }
        catch (Exception e) when (e is ArgumentException or RecipeException or StorageException or ModelSourceException or BackendException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            error.WriteLine("Error: " + (e switch { RecipeException r => r.Code, StorageException s => s.Code,
                ModelSourceException m => m.Code, BackendException b => b.Code, OperationCanceledException => "cancelled", ArgumentException => "invalid-selection", _ => "update-failed" }));
            return e is OperationCanceledException ? 130 : 2;
        }
        finally { Console.CancelKeyPress -= handler; }
    }

    private static async Task<int> RunAsync(string[] args, TextWriter output, CancellationToken cancellation)
    {
        var files = new List<string>(); var env = new List<string>(); var selectors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? directory = null; var seen = new HashSet<string>(); var global = false;
        if (args.Any(a => a is "--help" or "-h"))
        { output.WriteLine("update [-f archivo]... [--env-file archivo]... [--project-directory ruta] [--select referencia]...\nupdate --global [--select referencia]...\nReferencias: backend://variante, backend://alias, backend://proyecto/alias, model://clave, companion://clave"); return 0; }
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i] switch { "-f" => "--file", "--base" => "--project-directory", "-g" => "--global", _ => args[i] };
            if (arg is "update" or "--private" or "--global") { if (!seen.Add(arg)) throw new ArgumentException(); global |= arg == "--global"; continue; }
            if (arg is not ("--file" or "--env-file" or "--project-directory" or "--select") || ++i == args.Length) throw new ArgumentException();
            switch (arg)
            {
                case "--file": files.Add(args[i]); break;
                case "--env-file": env.Add(args[i]); break;
                case "--select": selectors.Add(args[i]); break;
                default: if (directory is not null) throw new ArgumentException(); directory = args[i]; break;
            }
        }
        if (global && (files.Count > 0 || env.Count > 0 || directory is not null || seen.Contains("--private"))) throw new ArgumentException();
        foreach (var selector in selectors) _ = ResourceSelector.Parse(selector);
        var selection = global ? null : new RecipeSelection(Environment.CurrentDirectory, files, directory, env);
        var context = global ? ScopeContext.Global(Home.Resolve()) : ScopeContext.Project(selection!.Files[0], selection.Base);
        var selections = global ? GlobalSelections(context) : [selection!];
        using var client = HuggingFaceStore.CreateClient();
        using var github = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(60) };
        var source = new OfficialBackendSource(github);
        var hardware = HardwareProbe.Inspect();
        var probe = new ResourceUpdateProbe(new(client), (family, token) => source.ResolveAsync(hardware, family, "latest", token), new ConsoleModelSelectionInteraction());
        var result = await new RecipeUpdatePlans(context).UpdateAsync(selections, selectors, probe, cancellation);
        // Local fingerprints and snapshots are not part of the CLI report.
        output.WriteLine(RecipeUpdatePlans.Report(result).ToJsonString());
        return result["resources"]!.AsArray().Any(r => r!["status"]!.GetValue<string>() is not ("unchanged" or "update-available" or "not-applicable")) ? 6 : 0;
    }

    internal static RecipeSelection[] GlobalSelections(ScopeContext context)
    {
        var state = new ScopeStore(context).Read();
        var paths = state.Documents.Where(p => p.Value["kind"]!.GetValue<string>() == "recipe-intention").Select(p => p.Key).Order(StringComparer.Ordinal).ToArray();
        return paths.Select(path => new RecipeSelection(context.Base,
            path == "recipe.yaml" || !paths.Contains("recipe.yaml") ? [context.PathFor(path)] : [context.PathFor("recipe.yaml"), context.PathFor(path)], context.Base)
            { ImportedScope = context, FilterUnusedModels = path != "recipe.yaml" }).ToArray();
    }
}
