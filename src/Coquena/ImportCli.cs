using Coquena.Backends;
using Coquena.Downloads;
using Coquena.Models;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Servers;
using Coquena.Storage;

namespace Coquena;

public static class ImportCli
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try { return RunAsync(args, output, error).GetAwaiter().GetResult(); }
        catch (Exception e) when (e is StorageException or RecipeException or BackendException or ModelSourceException or DownloadException or
            Contracts.ContractException or Configuration.ConfigurationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or ArgumentException or HttpRequestException or OperationCanceledException)
        {
            var code = e switch
            {
                StorageException s => s.Code,
                RecipeException r => r.Code,
                BackendException b => b.Code,
                ModelSourceException m => m.Code,
                DownloadException d => d.Code,
                OperationCanceledException => "cancelled",
                _ => "import-error"
            };
            var detail = e is StorageException or RecipeException or BackendException or ModelSourceException or DownloadException or OperationCanceledException ? e.Message : "La operación no se completó; consultar el estado local conservado.";
            error.WriteLine($"Error: {code}. {detail}");
            return e is OperationCanceledException ? 130 : code.Contains("integrity", StringComparison.Ordinal) ? 6 :
                code == "file-unreadable" ? 7 :
                code.Contains("conflict", StringComparison.Ordinal) || code is "import-busy" or "publication-pending" or "interaction-required" ? 3 :
                code is "publication-unsupported" or "unsupported-argument-destination" or "backend-incompatible" ? 5 :
                code is "publication-rejected" or "preset-load-failed" || e is System.ComponentModel.Win32Exception ? 8 :
                e is ArgumentException or RecipeException ? 2 : e is HttpRequestException ? 4 : 7;
        }
    }

    private static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error)
    {
        var positional = new List<string>(); var files = new List<string>(); var env = new List<string>(); var replace = new List<string>();
        string? select = null, basePath = null; bool force = false, asDefault = false, end = false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i] switch { "-f" => "--file", "--base" => "--project-directory", "-g" => "--global", _ => args[i] };
            if (end) { positional.Add(arg); continue; }
            if (arg == "--") { end = true; continue; }
            if (arg is "--file" or "--env-file" or "--replace" or "--select" or "--project-directory")
            {
                if (++i == args.Length || arg is "--select" or "--project-directory" && !seen.Add(arg)) throw new ArgumentException("Opción inválida o repetida.");
                switch (arg)
                {
                    case "--file": files.Add(args[i]); break;
                    case "--env-file": env.Add(args[i]); break;
                    case "--replace": replace.Add(args[i]); break;
                    case "--select": select = args[i]; break;
                    default: basePath = args[i]; break;
                }
            }
            else if (arg is "--force" or "--as-default" or "--private" or "--global")
            {
                if (!seen.Add(arg)) throw new ArgumentException("Opción repetida.");
                force |= arg == "--force"; asDefault |= arg == "--as-default";
            }
            else if (arg.StartsWith('-')) throw new ArgumentException("Opción de import desconocida.");
            else positional.Add(arg);
        }
        if (positional.Count != 2 || positional[0] != "import") throw new ArgumentException("Uso: import <receta.yaml> [--select categorías] [--replace entidad] [--as-default] [--force].");
        var source = new RecipeSelection(Environment.CurrentDirectory, new[] { positional[1] }.Concat(files), basePath, env);
        var context = ScopeContext.Global(Home.Resolve());
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(120) };
        using var transport = new OfficialArtifactTransport();
        var resources = new ScopedCombinationResources(context, HardwareProbe.Inspect(), new HuggingFaceStore(http), transport,
            new ConsoleModelSelectionInteraction(), warning: message => error.WriteLine(message), allowNewLocalRevision: true);
        IPresetRouter? Router(string id)
        {
            var store = new ScopeStore(context);
            var active = store.Read().Documents.GetValueOrDefault(store.RuntimeDocumentPath(id, "active"))?["data"];
            if (active?["execution"]?["arguments"]?.AsArray().Any(a => a?.GetValue<string>() == "--models-preset") != true)
                throw new StorageException("publication-unsupported", "La instancia no ofrece INI recargable; no se reinicia al importar.");
            var host = active["endpoint"]!["host"]!.GetValue<string>() switch { "0.0.0.0" => "127.0.0.1", "::" => "::1", var h => h };
            return new LlamaPresetRouter(http, new UriBuilder("http", host, active["endpoint"]!["port"]!.GetValue<int>()).Uri);
        }
        bool Confirm(string message)
        {
            output.WriteLine(message); output.WriteLine("[a] Aplicar ahora aceptando el riesgo, [c] Cancelar:");
            return Console.ReadLine()?.Trim().Equals("a", StringComparison.OrdinalIgnoreCase) == true;
        }
        var result = await new RecipeImporter(context).ImportAsync(source, resources, select, replace, force, asDefault,
            interactive: !Console.IsInputRedirected, confirm: Confirm, router: Router);
        output.WriteLine(result.ReconciledOnly ? "Se observó la operación anterior sin reenviar reload. Repetir import para aplicar una nueva candidata o completar remanentes." : result.Confirmed ? "Intención importada; presets seleccionados confirmados o conservados para el próximo inicio. Servicio no iniciado." :
            "Intención importada; publicación incompleta. Pendientes: " + string.Join(", ", result.Pending));
        if (result.Confirmed && result.Pending.Length != 0) output.WriteLine("Se conservan remanentes anteriores no seleccionados: " + string.Join(", ", result.Pending));
        return result.ExitCode != 0 ? result.ExitCode : result.Confirmed ? 0 : result.WarmupFailed ? 8 : 9;
    }
}
