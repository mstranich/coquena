using System.Text.Json.Nodes;
using Coquena.Backends;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Storage;

namespace Coquena;

public static class ScopedBackendCli
{
    public static int Run(string[] args, TextWriter output, TextWriter error,
        Func<HardwareContext>? inspect = null,
        Func<HardwareContext, string, string, CancellationToken, Task<BackendResolution>>? resolve = null,
        IDownloadTransport? transport = null, Func<string, CancellationToken, Task<string>>? probe = null)
    {
        if (args.Length == 0) { output.WriteLine("backend (alias runtime): show, list, download, protect, unprotect. Consulte coquena --help."); return 0; }
        var action = args[0];
        if (action is not ("show" or "list" or "download" or "protect" or "unprotect")) return Invalid(error);
        try
        {
            var global = false; var json = false; var force = false;
            string? projectDirectory = null, service = null, version = null, variant = null, installation = null;
            var files = new List<string>(); var env = new List<string>(); var seen = new HashSet<string>();
            for (var i = 1; i < args.Length; i++)
            {
                var option = args[i] switch { "-g" => "--global", "-f" => "--file", "--base" => "--project-directory", _ => args[i] };
                if (option is "--global" or "--json" or "--force")
                {
                    if (!seen.Add(option)) return Invalid(error);
                    if (option == "--global") global = true;
                    else if (option == "--json") json = true;
                    else force = true;
                }
                else if (option is "--file" or "--env-file" or "--project-directory" or "--service" or "--version" or "--variant")
                {
                    if (++i == args.Length || option is not ("--file" or "--env-file") && !seen.Add(option)) return Invalid(error);
                    switch (option)
                    {
                        case "--file": files.Add(args[i]); break;
                        case "--env-file": env.Add(args[i]); break;
                        case "--project-directory": projectDirectory = args[i]; break;
                        case "--service": service = args[i]; break;
                        case "--version": version = args[i]; break;
                        case "--variant": variant = args[i]; break;
                    }
                }
                else if (action is "protect" or "unprotect" && installation is null && Guid.TryParseExact(args[i], "D", out var id) && id != Guid.Empty)
                    installation = args[i];
                else return Invalid(error);
            }
            if (global && (files.Count != 0 || env.Count != 0 || projectDirectory is not null) || !global && service is not null ||
                force && action != "download" || (version is not null || variant is not null) && action != "download" ||
                action is "protect" or "unprotect" && installation is null || service is not null && action != "show") return Invalid(error);
            BackendIntent? intent = action == "download" ? new(version, variant) : null;
            intent?.Validate(); // Reject before creating a home or scope.
            var selection = global ? null : new RecipeSelection(Environment.CurrentDirectory, files, projectDirectory, env);
            var context = global ? ScopeContext.Global(Home.Resolve()) : ScopeContext.Project(selection!.Files[0], selection.Base);
            var backends = new ScopedBackends(context, resolve, probe);
            if (action == "show")
            {
                JsonNode value;
                if (!global)
                {
                    var recipe = RecipeReader.Read(selection!);
                    _ = BackendIntent.FromRecipe(recipe);
                    value = recipe.Root.Get("service")!.Get("backend")!.Redacted()!;
                }
                else
                {
                    var registry = new RecipeRegistry(new ScopeStore(context));
                    var serviceId = registry.ResolveGlobalService(service ?? "default");
                    var entity = new ScopeStore(context).Read().Documents["local/recipes.json"]["data"]!["recipes"]!.AsArray()
                        .SelectMany(r => r!["entities"]!.AsArray()).Single(e => e!["id"]!.GetValue<string>() == serviceId)!;
                    value = entity["backend-intent-sensitive"]?.GetValue<bool>() == true ? JsonValue.Create("<redacted>")! :
                        entity["backend-intent"]?.DeepClone() ?? throw new BackendException("backend-required", "El servicio no conserva intención de backend.");
                }
                output.WriteLine(value.ToJsonString()); return 0;
            }
            if (action == "list")
            {
                var resources = backends.List();
                if (json) output.WriteLine(resources.ToJsonString());
                else
                {
                    foreach (var resource in resources)
                        foreach (var revision in resource!["installations"]!.AsArray())
                            output.WriteLine($"{revision!["installation-id"]!.GetValue<string>()} | {resource["ownership"]!.GetValue<string>()} | {resource["logical-key"]!.GetValue<string>()} | protegido={revision["protected"]?.GetValue<bool>() ?? revision["origin"]?.GetValue<string>() == "explicit"}");
                    if (resources.Count == 0) output.WriteLine("Sin backends en el ámbito seleccionado.");
                }
                return 0;
            }
            if (action is "protect" or "unprotect")
            {
                backends.Protect(installation!, action == "protect");
                output.WriteLine(new JsonObject { ["installation-id"] = installation, ["protected"] = action == "protect",
                    ["references-preserved"] = true }.ToJsonString());
                if (!json) output.WriteLine("Se conservan las referencias de intención, activo y recuperación; no se borran archivos.");
                return 0;
            }
            using var official = transport is null ? new OfficialArtifactTransport() : null;
            using var stopped = new CancellationTokenSource();
            ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; stopped.Cancel(); };
            Console.CancelKeyPress += handler;
            try
            {
                var prepared = backends.DownloadAsync(intent!, (inspect ?? HardwareProbe.Inspect)(), transport ?? official!,
                    force: force, warning: message => error.WriteLine(message), cancellation: stopped.Token).GetAwaiter().GetResult();
                output.WriteLine(new JsonObject { ["resource-id"] = prepared.ResourceId, ["installation-id"] = prepared.InstallationId,
                    ["build"] = prepared.Build, ["variant"] = prepared.Variant, ["ownership"] = prepared.Ownership }.ToJsonString());
                return 0;
            }
            finally { Console.CancelKeyPress -= handler; }
        }
        catch (Exception e) when (e is BackendException or RecipeException or StorageException or DownloadException or ContractException or
            IOException or UnauthorizedAccessException or OperationCanceledException or ArgumentException or System.Net.Http.HttpRequestException)
        {
            var code = e switch { BackendException b => b.Code, RecipeException r => r.Code, StorageException s => s.Code,
                DownloadException d => d.Code, ContractException c => c.Code, _ => "backend-preparation-failed" };
            error.WriteLine($"Error: {code}. Consulte coquena --help.");
            if (e is OperationCanceledException) return 130;
            if (code is "backend-incompatible" or "unsupported-platform" or "no-candidate") return 5;
            if (code is "backend-integrity-invalid") return 6;
            if (e is RecipeException || code is "invalid-backend-selection" or "invalid-family" or "invalid-release" or "unknown-service" or "unknown-installation") return 2;
            if (code is "scope-conflict" or "writer-busy" or "executor-busy") return 3;
            if (e is System.Net.Http.HttpRequestException || e is BackendException && code is "remote-unavailable" or "remote-timeout" or
                "remote-rate-limit" or "remote-status" or "release-not-found" or "discovery-limit" or "invalid-metadata" or "metadata-limit") return 4;
            return 7;
        }
    }
    private static int Invalid(TextWriter error) { error.WriteLine("Error: argumentos de backend inválidos. Consulte coquena --help."); return 2; }
}
