using System.Text.Json.Nodes;
using Coquena.Backends;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Models;
using Coquena.Observation;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Servers;
using Coquena.Storage;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Coquena;

internal static partial class DashboardCli
{
    internal static ManagementPresentation.Service[] ServiceDefinitions(DefinitionsSource.Place place,
        IReadOnlyList<Target> targets, IReadOnlyDictionary<int, ServiceSnapshot> snapshots, RecipeSelection? chosen = null)
    {
        // In a project, the service is read from the combination chosen in the dropdown.
        var store = new ScopeStore(place.Context!);
        var state = store.Read();
        var registry = new RecipeRegistry(store);
        var defaultId = state.State?["data"]?["aliases"]?["default"]?.GetValue<string>();
        var services = registry.Services();
        if (services.Length == 0 && (chosen ?? place.Recipe) is { } declared && RecipeReader.Read(declared).Root.Get("service") is not null)
            return [Describe(null)];
        return services.Select(service => Describe(service)).ToArray();

        ManagementPresentation.Service Describe(ServiceIdentity? service)
        {
            string backend = "", port = "", file = "", arguments = "", consumers = "";
            string? problem = null;
            try
            {
                var selection = chosen ?? (service is null ? place.Recipe! : ServiceSelection(place, service));
                file = chosen is null ? string.Join(" + ", selection.Files) : "capas " + LayerLegend(selection);
                var definition = RecipeReader.Read(selection).Root.Get("service");
                var intent = definition?.Get("backend")?.Redacted();
                backend = intent?["executable"] is not null ? "externo" :
                    string.Join(" · ", new[] { intent?["variant"]?.GetValue<string>(), intent?["version"]?.GetValue<string>() }.OfType<string>());
                if (chosen is not null && definition?.Get("backend") is { } declaredBackend) backend += " " + LayerMark(selection, declaredBackend.Origin.File);
                port = definition?.Get("server")?.Get("args")?.Get("port")?.Redacted()?.ToString() ?? "";
                if (port.Length == 0 && intent?["executable"] is null) port = "9931";
                arguments = definition?.Get("server")?.Get("args")?.Redacted()?.ToJsonString() ?? "";
                if (chosen is not null && definition?.Get("server")?.Get("args") is { } declaredArgs) arguments += " " + LayerMark(selection, declaredArgs.Origin.File);
                // Names come from published preset names, never inferred from a filesystem directory.
                var record = service is null ? new JsonObject() : PresetPublication.Service(state, service.ServiceId);
                consumers = string.Join(", ", (record["consumers"] as JsonArray ?? []).SelectMany(consumer => consumer?["presets"] as JsonArray ?? [])
                    .Select(preset => preset?["name"]?.GetValue<string>()).OfType<string>());
            }
            catch (Exception error) when (ManagementError(error)) { problem = error.Message; }
            var index = service is null ? -1 : targets.ToList().FindIndex(target => target.Service?.ServiceId == service.ServiceId && SameScope(target.Store.Context, place.Context!));
            var observed = index >= 0 && snapshots.TryGetValue(index, out var snapshot) ? snapshot : null;
            var status = observed is null ? default : LivePresentation.ServiceState(observed);
            return new ManagementPresentation.Service(service?.ServiceId, service?.Name ?? place.Scope.Name, place.Scope.Global && service?.ServiceId == defaultId,
                backend, port, service is null ? "sin registrar" : observed is null ? null : status.Item1 + " " + status.Item3, observed is null ? null : status.Item2,
                file, arguments, consumers, problem);
        }
    }

    internal static RecipeSelection ServiceSelection(DefinitionsSource.Place place, ServiceIdentity service) => place.Scope.Global
        ? new RecipeImporter(place.Context!).ServiceSelection(service.ServiceId)
        : new RecipeSelection(place.Context!.Base, new RecipeRegistry(new ScopeStore(place.Context)).Get(service.RecipeId).Files, place.Context.Base);

    internal static bool SameScope(ScopeContext first, ScopeContext second) => first.IsGlobal == second.IsGlobal &&
        first.PhysicalBase == second.PhysicalBase;

    internal static bool ManagementError(Exception error) => ServiceObservation.ObservationError(error) ||
        error is BackendException or DownloadException or ModelSourceException or ContractException or RecipeException or ArgumentException;

    internal static ScopeTransfers Transfers(ScopeContext context) => new(context, validateBackend: new ScopedBackends(context).ValidateOfficial);

    internal static OperationDocument[] TransferList(ScopeContext context)
    {
        if (File.Exists(context.PathFor("local/state.json"))) return Transfers(context).Downloads.List();
        if (Directory.Exists(context.PathFor("downloads")) && Directory.EnumerateFileSystemEntries(context.PathFor("downloads")).Any())
            throw new StorageException("missing-state-pointer", "Hay transferencias sin identidad de ámbito.");
        return [];
    }

    internal static DefinitionsPresentation.Download TransferRow(OperationDocument operation) => new(operation.Name,
        operation.ResourceKind, operation.State, operation.Phase, operation.Files.Sum(file => file.ConfirmedBytes),
        operation.Files.All(file => file.ExpectedSize is not null) ? operation.Files.Sum(file => file.ExpectedSize!.Value) : null, operation.Diagnostic);

    // Capture the selected scope/identity before leaving the UI thread. No current-directory changes.
    internal static async Task<string> ResumeTransfer(ScopeContext context, Guid id, IApplication? app, CancellationToken cancellation,
        IDownloadTransport? transport = null)
    {
        var transfers = Transfers(context);
        var selected = transfers.Downloads.Status(id);
        if (selected.BackendSelection is { } backend && selected.Phase != "cleanup")
            ScopedBackends.CheckSelection(backend, HardwareProbe.Inspect(), false, null);
        using var official = transport is null && selected.ResourceKind == "backend" ? new OfficialArtifactTransport() : null;
        using var huggingFace = transport is null && selected.ResourceKind != "backend" ? new HuggingFaceTransport() : null;
        var result = await transfers.RunAsync(id, transport ?? official ?? (IDownloadTransport)huggingFace!, _ => { }, cancellation,
            async (_, file, token) => app is not null && await AskOnUi(app, "Reiniciar archivo", $"Archivo {file + 1}: reiniciar con la misma selección y descartar su parcial?",
                "Reiniciar", token));
        if (result?.State == "failed") throw new DownloadException("transfer-failed", result.Diagnostic ?? "Falló la transferencia.");
        if (result is not null) return "Estado: " + result.State + " · " + result.Phase + (result.Diagnostic is { } diagnostic ? " · " + diagnostic : "");
        var candidate = RecipeUpgrade.Pending(new ScopeStore(context).Read().Documents, id)?["candidate"] is not null;
        return candidate ? "Candidata preparada; upgrade debe publicar el conjunto." : transfers.Published(selected)
            ? "Instalación publicada y limpieza completada." : "Cancelación y limpieza completadas.";
    }

    private static async Task<bool> AskOnUi(IApplication app, string title, string text, string accept, CancellationToken cancellation)
    {
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        app.Invoke(() =>
        {
            if (cancellation.IsCancellationRequested) { answer.TrySetCanceled(cancellation); return; }
            try { answer.TrySetResult(Query(app, title, text, "Cancelar", accept) == 1); }
            catch (Exception error) { answer.TrySetException(error); }
        });
        return await answer.Task.WaitAsync(cancellation);
    }

    private sealed record BackendFormResult(BackendIntent Intent);

    // An installed backend of the scope, as the add-service form offers it.
    internal sealed record InstalledBackend(string Label, BackendIntent Intent);

    internal static InstalledBackend[] InstalledBackends(ScopeContext context) =>
        new ScopedBackends(context).List().SelectMany(resource => resource!["installations"]!.AsArray().Select(installation =>
        {
            if (resource["ownership"]?.GetValue<string>() == "external")
            {
                var path = installation!["executable"]!.GetValue<string>();
                return new InstalledBackend("externo · " + Path.GetFileName(path), new BackendIntent(null, null, path));
            }
            var selection = installation!["selection"]!["backendSelection"]!;
            string variant = selection["variant"]!.GetValue<string>(), release = selection["release"]!.GetValue<string>();
            return new InstalledBackend(variant + " · " + release, new BackendIntent(release, variant));
        })).ToArray();

    private sealed record ServiceFormResult(string Name, BackendIntent Intent, ServiceCreationOptions Options);

    // T: a service only chooses a backend already installed in its scope. Downloading or
    // registering one belongs to Backends; without any, the form only offers to go there.
    private static ServiceFormResult? ServiceForm(IApplication app, string scope, string? fixedName,
        IReadOnlyList<InstalledBackend> backends, out bool goToBackends)
    {
        using var dialog = Modal(app, 74, 18, "Agregar servicio a " + scope);
        var color = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
        const int field = 12;
        var captions = new List<Label>();
        Label Caption(int x, int y, string text) { var label = new Label { X = x, Y = y - 2, Text = text }; captions.Add(label); return label; }
        var name = new TextField { X = field, Y = 0, Width = Dim.Fill(2), Text = fixedName ?? "", ReadOnly = fixedName is not null };
        var list = new ListView { X = field, Y = 1, Width = Dim.Fill(2), Height = 4 };
        list.SetSource(new System.Collections.ObjectModel.ObservableCollection<string>(backends.Select(item => item.Label)));
        if (backends.Count > 0) list.SelectedItem = 0;
        var go = new Button { X = field, Y = 5, Text = " Ir a backends ", NoDecorations = true, ShadowStyle = ShadowStyles.None };
        var host = new TextField { X = field, Y = 7, Width = 15, Text = "127.0.0.1" };
        var port = new TextField { X = field + 24, Y = 7, Width = 8, Text = "9931" };
        var health = new TextField { X = field, Y = 8, Width = Dim.Fill(2) };
        var arguments = new JsonObject();
        var argumentButton = new Button { X = 1, Y = 9, Text = " Argumentos de proceso… ", NoDecorations = true, ShadowStyle = ShadowStyles.None };
        argumentButton.Accepting += (_, args) => { args.Handled = true; EditProcessArguments(app, arguments); };
        var status = new Label { X = 1, Y = 13, Width = Dim.Fill(1), Height = 2 };
        var leave = false;
        go.Accepting += (_, args) => { args.Handled = true; leave = true; app.RequestStop(dialog); };
        dialog.Add(Caption(1, 2, "nombre"), name, Caption(1, 3, "backend"), list, go,
            Caption(1, 9, "host"), host, Caption(field + 17, 9, "puerto"), port, Caption(1, 10, "URL salud"), health, argumentButton,
            Caption(1, 13, backends.Count == 0 ? "Sin backends en " + scope + ": descargalos o registralos en Backends."
                : "Sólo backends ya preparados en " + scope + "."),
            Caption(1, 14, "Crear guarda la definición; no inicia el proceso."), status);
        ServiceFormResult? result = null;
        var cancel = new Button { Text = " Cancelar ", NoDecorations = true, ShadowStyle = ShadowStyles.None };
        var accept = new Button { Text = " Agregar ", NoDecorations = true, ShadowStyle = ShadowStyles.None, Enabled = backends.Count > 0 };
        // Validate before accepting, so errors leave the user's form intact.
        accept.Accepting += (_, args) =>
        {
            try
            {
                if (list.SelectedItem is not int row || row < 0 || row >= backends.Count) throw new ArgumentException("Elegir un backend.");
                if (string.IsNullOrWhiteSpace(name.Text)) throw new ArgumentException("Ingresá un nombre de servicio.");
                if (!int.TryParse(port.Text, out var number)) throw new ArgumentException("Puerto inválido.");
                var intent = backends[row].Intent;
                var options = new ServiceCreationOptions(host.Text.Trim(), number, health.Text.Trim(), arguments);
                _ = options.Server(intent.Executable is not null);
                result = new(name.Text.Trim(), intent, options);
            }
            catch (Exception error) when (ManagementError(error)) { status.Text = error.Message; args.Handled = true; }
        };
        dialog.AddButton(cancel);
        dialog.AddButton(accept);
        if (color)
        {
            Paint(dialog, Palette.Text, Palette.Card);
            foreach (var view in dialog.SubViews) Paint(view, view is TextField or ListView ? Palette.Accent : Palette.Text,
                view switch { TextField or ListView => Palette.Base, Button => Palette.Border, _ => Palette.Card });
            foreach (var label in captions) Paint(label, Palette.Muted, Palette.Card);
            Paint(status, Palette.Danger, Palette.Card);
            foreach (var button in new[] { cancel, accept }) Paint(button, Palette.Text, Palette.Border);
        }
        app.Run(dialog);
        goToBackends = leave;
        return !leave && dialog.Result == 1 ? result : null;
    }

    // K: downloads an official backend or registers an external one in the scope, without
    // assigning it to a service. All work runs after acceptance, outside the main loop.
    private static BackendFormResult? BackendForm(IApplication app, string scope, HardwareContext? hardware)
    {
        using var dialog = Modal(app, 74, 12, "Agregar backend a " + scope);
        var color = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"));
        const int field = 12;
        var captions = new List<Label>();
        Label Caption(int x, int y, string text) { var label = new Label { X = x, Y = y - 2, Text = text }; captions.Add(label); return label; }
        var external = new CheckBox { X = field, Y = 0, Text = "ejecutable externo (sin copiar archivos)" };
        var variant = new TextField { X = field, Y = 1, Width = 12, Text = "cpu" };
        var version = new TextField { X = field + 21, Y = 1, Width = Dim.Fill(2), Text = "latest" };
        var executable = new TextField { X = field, Y = 4, Width = Dim.Fill(2), Enabled = false };
        var chooseVariant = new Button { X = 1, Y = 3, Text = " Elegir variante… ", NoDecorations = true, ShadowStyle = ShadowStyles.None };
        chooseVariant.Accepting += (_, args) =>
        {
            args.Handled = true;
            string[] variants = ["cpu", "vulkan", "cuda@12", "cuda@13"];
            var selected = Choose(app, "Variante · evidencia del sistema", variants.Select(item => item + " · " + (item == "cpu" ?
                "arquitectura nativa" : item == "vulkan" ? hardware?.Vulkan.Diagnostic ?? "sin sondeo" : hardware?.Cuda.Diagnostic ?? "sin sondeo")).ToArray(), "Elegir");
            if (selected is int row) variant.Text = variants[row];
        };
        var status = new Label { X = 1, Y = 7, Width = Dim.Fill(1), Height = 2 };
        dialog.Add(external, variant, version, executable, status, chooseVariant,
            Caption(1, 4, hardware is null ? "compatibilidad: se comprobará al resolver la selección" :
                $"sondeo: CUDA {hardware.Cuda.State} · Vulkan {hardware.Vulkan.State}"),
            Caption(1, 2, "origen"), Caption(1, 3, "variante"), Caption(field + 13, 3, "versión"), Caption(1, 6, "ejecutable"),
            Caption(1, 8, "Prepara el backend en el ámbito; no lo asigna a un servicio."));
        external.ValueChanging += (_, change) =>
        {
            var value = change.NewValue == CheckState.Checked;
            executable.Enabled = value;
            variant.Enabled = version.Enabled = chooseVariant.Enabled = !value;
        };
        BackendFormResult? result = null;
        var cancel = new Button { Text = " Cancelar ", NoDecorations = true, ShadowStyle = ShadowStyles.None };
        var accept = new Button { Text = " Agregar ", NoDecorations = true, ShadowStyle = ShadowStyles.None };
        accept.Accepting += (_, args) =>
        {
            try
            {
                var intent = external.Value == CheckState.Checked ? new BackendIntent(null, null, executable.Text.Trim())
                    : new BackendIntent(version.Text.Trim(), variant.Text.Trim());
                intent.Validate();
                result = new(intent);
            }
            catch (Exception error) when (ManagementError(error)) { status.Text = error.Message; args.Handled = true; }
        };
        dialog.AddButton(cancel);
        dialog.AddButton(accept);
        if (color)
        {
            Paint(dialog, Palette.Text, Palette.Card);
            foreach (var view in dialog.SubViews) Paint(view, view is TextField ? Palette.Accent : Palette.Text, view switch { TextField => Palette.Base, Button => Palette.Border, _ => Palette.Card });
            foreach (var label in captions) Paint(label, Palette.Muted, Palette.Card);
            Paint(status, Palette.Danger, Palette.Card);
            foreach (var button in new[] { chooseVariant, cancel, accept }) Paint(button, Palette.Text, Palette.Border);
        }
        app.Run(dialog);
        return dialog.Result == 1 ? result : null;
    }

    private static void EditProcessArguments(IApplication app, JsonObject arguments)
    {
        using var dialog = Modal(app, 72, 16, "Argumentos de proceso · cambios sólo en este formulario");
        var list = new ListView { X = 1, Y = 0, Width = Dim.Fill(1), Height = 7 };
        var name = new TextField { X = 10, Y = 8, Width = 24 };
        var value = new TextField { X = 10, Y = 9, Width = Dim.Fill(1) };
        var status = new Label { X = 1, Y = 11, Width = Dim.Fill(1), Height = 2 };
        dialog.Add(list,
            new Label { X = 1, Y = 8, Text = "Nombre" }, name, new Label { X = 1, Y = 9, Text = "Valor" }, value, status);
        void Refresh() => list.SetSource(new System.Collections.ObjectModel.ObservableCollection<string>(arguments.Select(item => item.Key + " = " + item.Value)));
        Refresh();
        var set = new Button { Text = " Asignar ", NoDecorations = true, ShadowStyle = ShadowStyles.None };
        var remove = new Button { Text = " Quitar ", NoDecorations = true, ShadowStyle = ShadowStyles.None };
        set.Accepting += (_, args) =>
        {
            args.Handled = true;
            try
            {
                var key = name.Text.Trim();
                if (key.Length == 0 || key.StartsWith('-') || key.Any(char.IsWhiteSpace) || key is "host" or "port")
                    throw new ArgumentException("Nombre sin guiones iniciales; host/port tienen sus propios campos.");
                JsonNode? parsed;
                try { parsed = JsonNode.Parse(value.Text); }
                catch (System.Text.Json.JsonException) { parsed = JsonValue.Create(value.Text); }
                if (parsed is JsonObject) throw new ArgumentException("Usar texto, número, true/false o una lista.");
                arguments[key] = parsed;
                status.Text = "Asignado al formulario; se guardará al crear el servicio.";
                Refresh();
            }
            catch (Exception error) when (ManagementError(error)) { status.Text = error.Message; }
        };
        remove.Accepting += (_, args) => { args.Handled = true; if (list.SelectedItem is int row && row >= 0 && row < arguments.Count) arguments.Remove(arguments.ElementAt(row).Key); Refresh(); };
        dialog.AddButton(set); dialog.AddButton(remove);
        dialog.AddButton(new Button { Text = " Volver ", NoDecorations = true, ShadowStyle = ShadowStyles.None });
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")))
        {
            Paint(dialog, Palette.Text, Palette.Card);
            foreach (var view in dialog.SubViews) Paint(view, Palette.Text, view is TextField or ListView ? Palette.Base : Palette.Card);
        }
        app.Run(dialog);
    }
}
