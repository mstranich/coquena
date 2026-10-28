using Coquena.Backends;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Models;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Servers;
using Coquena.Storage;

namespace Coquena;

public static class ScopedServiceCli
{
    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            var command = ServiceCommand.Parse(args);
            if (command.Action == "help")
            {
                output.WriteLine("service: list, default set, start, stop, restart, remove.");
                return 0;
            }
            var home = Home.Resolve();
            if (command.Action == "list") return List(command, home, output);
            if (command.Action == "default-set")
            {
                var registry = new RecipeRegistry(new ScopeStore(ScopeContext.Global(home)));
                registry.SetDefaultService(registry.ResolveGlobalService(command.Selector!));
                output.WriteLine($"Servicio predeterminado: {command.Selector}.");
                return 0;
            }
            var target = Resolve(command, home);
            if (!target.Context.IsGlobal) new ProjectIndex(home).Refresh(new ScopeStore(target.Context));
            return Operate(command, target, output, error);
        }
        catch (Exception e) when (e is StorageException or RecipeException or BackendException or DownloadException or ContractException or
            Configuration.ConfigurationException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or ArgumentException or HttpRequestException or OperationCanceledException)
        {
            return Failure(e, error);
        }
    }

    private sealed record Target(ScopeContext Context, RecipeSelection Selection, ServiceIdentity Service);

    private static RecipeSelection Selection(ServiceCommand command) => new(Environment.CurrentDirectory,
        command.Files, command.BasePath, command.EnvFiles);

    private static int List(ServiceCommand command, Home home, TextWriter output)
    {
        var registry = new RecipeRegistry(new ScopeStore(ScopeContext.Global(home)));
        foreach (var service in registry.Services()) output.WriteLine($"global | {service.Name} | {service.ServiceId}");
        if (!command.Global)
        {
            var selection = Selection(command);
            var local = new RecipeRegistry(new ScopeStore(ScopeContext.Project(selection.Files[0], selection.Base)));
            foreach (var service in local.Services()) output.WriteLine($"proyecto | {service.Name} | {service.ServiceId}");
        }
        return 0;
    }

    private static Target Resolve(ServiceCommand command, Home home)
    {
        if (command.Selector == "self")
        {
            var selection = Selection(command);
            var context = ScopeContext.Project(selection.Files[0], selection.Base);
            var registry = new RecipeRegistry(new ScopeStore(context));
            if (command.Action is not ("start" or "restart"))
                return new(context, selection, registry.FindProjectService(selection.Files));
            if (command.Action == "start")
            {
                try { return new(context, selection, registry.FindProjectService(selection.Files)); }
                catch (StorageException e) when (e.Code == "unknown-service") { }
            }
            var recipe = RecipeReader.Read(selection);
            if (recipe.Root.Get("service") is null) throw new StorageException("self-requires-service", "La receta no define service.");
            return new(context, selection, registry.EnsureProjectService(selection.Files, recipe.Root.Get("name")?.Text));
        }
        var global = ScopeContext.Global(home);
        var globalRegistry = new RecipeRegistry(new ScopeStore(global));
        var id = globalRegistry.ResolveGlobalService(command.Selector!);
        var service = globalRegistry.Services().SingleOrDefault(s => s.ServiceId == id)
            ?? throw new StorageException("service-conflict", "Cambió el servicio seleccionado; no se elige sustituto.");
        var saved = new RecipeSelection(global.Base, [global.PathFor($"services/{id}/recipe.yaml")], global.Base);
        return new(global, saved, service);
    }

    private static int Operate(ServiceCommand command, Target target, TextWriter output, TextWriter error)
    {
        var service = target.Service;
        var lifecycle = new ScopedServiceProcess(new ScopeStore(target.Context), output);
        if (command.Action == "remove")
        {
            lifecycle.Remove(service.ServiceId);
            output.WriteLine($"Servicio {service.Name} retirado; archivos y logs conservados.");
            return 0;
        }
        var interrupt = new ServerInterrupt(output);
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) =>
        {
            e.Cancel = true;
            interrupt.Receive();
            cancellation.Cancel();
        };
        Console.CancelKeyPress += handler;
        try
        {
            bool ForceRequested() => interrupt.Count > 1;
            if (command.Action == "stop")
            {
                lifecycle.Stop(service.ServiceId, ForceRequested);
                output.WriteLine("Servicio detenido.");
                return 0;
            }
            if (command.Action == "start" && lifecycle.Reconcile(service.ServiceId))
            {
                output.WriteLine("Servicio ya ejecutándose.");
                return 0;
            }
            Task<ScopedStartPlan> Prepare(CancellationToken token) => PrepareAsync(command, target, error, token);
            var instance = command.Action == "restart"
                ? lifecycle.RestartAsync(service.ServiceId, Prepare, ForceRequested, cancellation.Token).GetAwaiter().GetResult()
                : lifecycle.StartAsync(service.ServiceId, Prepare(cancellation.Token).GetAwaiter().GetResult(), cancellation.Token, ForceRequested).GetAwaiter().GetResult();
            output.WriteLine($"Servicio {service.Name} iniciado; instancia {instance}.");
            return command.Detached ? 0 : lifecycle.Foreground(service.ServiceId, instance, output, interrupt);
        }
        finally { Console.CancelKeyPress -= handler; }
    }

    private static async Task<ScopedStartPlan> PrepareAsync(ServiceCommand command, Target target, TextWriter error, CancellationToken cancellation)
    {
        using var http = new HttpClient();
        using var transport = new OfficialArtifactTransport();
        var resources = new ScopedCombinationResources(target.Context, HardwareProbe.Inspect(), new HuggingFaceStore(http), transport,
            new ConsoleModelSelectionInteraction(), warning: message => error.WriteLine(message));
        var prepared = await new CombinationPreparation(target.Context).PrepareServiceAsync(target.Selection, resources,
            command.Private || target.Context.IsGlobal, cancellation: cancellation);
        var recipe = RecipeReader.Read(target.Selection);
        var id = Guid.NewGuid();
        var logs = target.Context.IsGlobal
            ? target.Context.PathFor($"services/{target.Service.ServiceId}/logs/{id:D}.log")
            : target.Context.PathFor($"logs/{id:D}.log");
        return ScopedServicePlan.Compose(target.Context, recipe, prepared, logs) with { InstanceId = id };
    }

    private static int Failure(Exception e, TextWriter error)
    {
        var code = e switch
        {
            StorageException s => s.Code, RecipeException r => r.Code, BackendException b => b.Code,
            DownloadException d => d.Code, ContractException c => c.Code, OperationCanceledException => "cancelled", _ => "service-error"
        };
        error.WriteLine($"Error: {code}. {e.Message}");
        if (e is OperationCanceledException) return 130;
        if (e is RecipeException or ArgumentException || code is "unknown-service" or "self-requires-service" or "invalid-start-plan" or "invalid-endpoint" or "invalid-health-url" or "reserved-service-name") return 2;
        if (code is "scope-conflict" or "service-conflict" or "instance-conflict" or "service-in-use" or "service-busy" or "writer-busy" or "instance-already-registered" or "intention-conflict" or "service-name-conflict") return 3;
        if (code.Contains("integrity", StringComparison.Ordinal)) return 6;
        if (code is "backend-incompatible" or "unsupported-platform" or "health-required") return 5;
        if (e is HttpRequestException || code is "remote-unavailable" or "remote-timeout" or "remote-rate-limit") return 4;
        if (e is Configuration.ConfigurationException or System.ComponentModel.Win32Exception || code is "health-timeout" or "process-exited" or "service-already-running") return 8;
        if (e is IOException or UnauthorizedAccessException or DownloadException) return 7;
        return 1;
    }
}
