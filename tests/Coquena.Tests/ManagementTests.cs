using Coquena.Backends;
using Coquena.Observation;
using Coquena.Projects;
using Coquena.Recipes;
using Coquena.Servers;
using Coquena.Storage;
using System.Text.Json.Nodes;
using Coquena.Contracts;
using Coquena.Downloads;

namespace Coquena.Tests;

internal static class ManagementTests
{
    public static async Task Run(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-management-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var home = new Home(Path.Combine(root, "home"));
        var context = ScopeContext.Global(home);
        var store = new ScopeStore(context);
        var registry = new RecipeRegistry(store);
        var executable = Path.Combine(root, "external's backend.exe");
        File.WriteAllText(executable, "external fixture; not executed");
        try
        {
            var source = new DashboardCli.DefinitionsSource(home, root);
            check(source.Places()[0].Scope.Global, "an empty home exposes Global so the first service can be created");
            var output = new StringWriter();
            var settings = new ServiceCreationOptions("127.0.0.1", 61483, "http://127.0.0.1:61483/health", new JsonObject { ["metrics"] = true, ["slots"] = true });
            await PublicationCli.EnsureGlobalAsync(context, output, serviceName: "first", backend: new(null, null, executable), options: settings);
            var first = registry.Services().Single();
            var selection = new RecipeImporter(context).ServiceSelection(first.ServiceId);
            check(BackendIntent.FromRecipe(RecipeReader.Read(selection)).Executable == executable,
                "external creation persists an escaped path without copying or preparing files");
            check(store.Read().State?["data"]?["aliases"]?["default"]?.GetValue<string>() == first.ServiceId,
                "the first configured service becomes default");
            check(!new ScopedServiceProcess(store).Reconcile(first.ServiceId) && DashboardCli.TransferList(context).Length == 0,
                "creating a configured service starts neither an instance nor a download");
            await PublicationCli.EnsureGlobalAsync(context, output, serviceName: "second", backend: new(null, null, executable));
            var second = registry.Services().Single(service => service.Name == "second");
            check(store.Read().State?["data"]?["aliases"]?["default"]?.GetValue<string>() == first.ServiceId,
                "a second service preserves the existing default");
            try { await PublicationCli.EnsureGlobalAsync(context, output, serviceName: "first", backend: new(null, null, executable)); check(false, "duplicate accepted"); }
            catch (StorageException error) { check(error.Code == "service-name-conflict", "duplicate names are rejected before replacement"); }
            try { await PublicationCli.EnsureGlobalAsync(context, output, serviceName: "missing", backend: new(null, null, executable + ".absent")); check(false, "missing executable accepted"); }
            catch (BackendException error) { check(error.Code == "backend-executable-absent" && registry.Services().Length == 2, "missing external executable creates no service"); }
            var place = source.Places()[0];
            var definitions = DashboardCli.ServiceDefinitions(place, [], new Dictionary<int, ServiceSnapshot>());
            check(definitions.Length == 2 && definitions.All(service => service.Problem is null && service.Backend == "externo" && service.State is null),
                "service definitions remain readable before execution without inventing an observed state");
            check(definitions.Single(service => service.Id == first.ServiceId).Port == "61483" &&
                definitions.Single(service => service.Id == first.ServiceId).Arguments.Contains("metrics"), "the details read persisted service.server arguments");
            try { _ = (settings with { HealthUrl = null }).Server(true); check(false, "external without health accepted"); }
            catch (ArgumentException) { check(true, "external creation options require explicit health before saving"); }
            try { _ = (settings with { HealthUrl = "http://user:secret@localhost/health" }).Server(true); check(false, "credentials accepted"); }
            catch (ArgumentException) { check(true, "health URLs reject embedded credentials"); }
            try { _ = (settings with { Port = 65536 }).Server(false); check(false, "invalid port accepted"); }
            catch (ArgumentException) { check(true, "port validation applies to official and external services"); }
            var page = ManagementPresentation.Services("Global", true, definitions, first.ServiceId);
            var actions = page.Rows.Append(page.Title).SelectMany(Markup.Parse).Where(span => span.Action is not null).Select(span => span.Action).ToArray();
            check(actions.Contains("create") && actions.Contains("default") && actions.Contains("remove") && actions.Contains("live") && !actions.Contains("edit"),
                "management exposes service operations while YAML editing stays disabled");
            var localPage = ManagementPresentation.Services("project", false, definitions, first.ServiceId);
            check(!localPage.Rows.Append(localPage.Title).SelectMany(Markup.Parse).Any(span => span.Action is "create" or "default"),
                "global creation and default assignment are not offered in a project");
            registry.SetDefaultService(second.ServiceId);
            check(DashboardCli.ServiceDefinitions(place, [], new Dictionary<int, ServiceSnapshot>()).Single(service => service.Default).Id == second.ServiceId,
                "default changes are read back by identity");
            new ScopedServiceProcess(store).Remove(first.ServiceId);
            check(registry.Services().Length == 1 && File.Exists(selection.Files[^1]) && File.Exists(executable),
                "removing a stopped service preserves its recipe and external executable");
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            try { await PublicationCli.EnsureGlobalAsync(context, output, serviceName: "cancelled", backend: new(null, null, executable), cancellation: cancellation.Token); check(false, "cancel ignored"); }
            catch (OperationCanceledException) { check(registry.Services().Length == 1, "cancelled creation does not register a service"); }
            var officialContext = ScopeContext.Global(new Home(Path.Combine(root, "official-home")));
            var candidate = new BackendCandidate("b11899", "cpu", HardwareProbe.Inspect().Platform, "compatible", "fixture metadata; no download", [], true);
            await PublicationCli.EnsureGlobalAsync(officialContext, output, () => Task.FromResult<BackendCandidate?>(candidate), "official",
                new("b11899", "cpu"), options: settings with { HealthUrl = null });
            var officialService = new RecipeRegistry(new ScopeStore(officialContext)).Services().Single();
            var officialRecipe = RecipeReader.Read(new RecipeImporter(officialContext).ServiceSelection(officialService.ServiceId));
            check(BackendIntent.FromRecipe(officialRecipe) == new BackendIntent("b11899", "cpu") &&
                officialRecipe.Root.Get("service")?.Get("server")?.Get("args")?.Get("port")?.Text == "61483" && DashboardCli.TransferList(officialContext).Length == 0,
                "official creation preserves explicit build, variant and process settings without a download");

            var id = Guid.NewGuid(); var other = Guid.NewGuid();
            var start = DateTimeOffset.UtcNow;
            DownloadsPresentation.Item Transfer(Guid key, string name, string kind, string state, int minutes, string? diagnostic = null) =>
                new(key, name, kind, state, "transfer", 5, 10, diagnostic, start.AddMinutes(minutes), state == "downloading" ? 1_000_000 : null);
            DownloadsPresentation.Group[] groups =
            [
                new("Global", false, [Transfer(Guid.NewGuid(), "gemma", "model", "downloading", 1), Transfer(Guid.NewGuid(), "cuda@13", "backend", "downloading", 0)]),
                new("código", true,
                [
                    Transfer(other, "same name", "companion", "failed", 9, "connection-interrupted"), Transfer(id, "same name", "companion", "paused", 1),
                    Transfer(Guid.NewGuid(), "older", "model", "downloading", 2), Transfer(Guid.NewGuid(), "newer", "model", "downloading", 5),
                    Transfer(Guid.NewGuid(), "done", "model", "completed", 7), Transfer(Guid.NewGuid(), "gone", "model", "cancelled", 8)
                ]),
                new("vacío", false, [Transfer(Guid.NewGuid(), "finished", "model", "completed", 0)])
            ];
            var downloadRows = DownloadsPresentation.Rows(groups, ["No se pudieron leer las descargas de notas."]).ToList();
            var plain = downloadRows.Select(Markup.Plain).Select(row => row.Trim()).Where(row => row.Length > 0 && !row.StartsWith("❚❚ Pausar", StringComparison.Ordinal) &&
                !row.StartsWith("Reanudar", StringComparison.Ordinal) && !row.StartsWith("Reintentar", StringComparison.Ordinal) && !row.StartsWith("Cancelar", StringComparison.Ordinal))
                .Select(row => row.Split("  ")[0]).ToArray();
            check(plain.SequenceEqual(["Global", "BACKENDS", "cuda@13", "MODELOS", "gemma", "código (esta carpeta)", "MODELOS", "newer", "older",
                    "AUXILIARES", "same name", "same name", "▲ No se pudieron leer las descargas de notas."]),
                "Descargas groups by scope then backends, models and companions; newest first; hides finished and empty scopes");
            var controls = downloadRows.SelectMany(Markup.Parse).Select(span => span.Action).OfType<string>().ToArray();
            check(controls.Contains("resume:1:" + id) && controls.Contains("cancel:1:" + id) && controls.Contains("resume:1:" + other) &&
                controls.Contains("cancel:1:" + other) && controls.Contains("pause:0:" + groups[0].Items[1].Id),
                "every row acts on its own scope and operation; failed ones offer Reintentar and Cancelar");
            check(downloadRows.IndexOf(downloadRows.First(row => Markup.Plain(row).Contains("✗ " + Messages.ConnectionCut))) >
                downloadRows.IndexOf(downloadRows.First(row => Markup.Plain(row).Contains("❚❚ pausada"))), "failed transfers go after paused ones");
            check(downloadRows.All(row => Markup.Width(row) <= 80), "transfer rows fit 80 columns");
            check(Markup.Plain(DownloadsPresentation.Title(groups)) == "Descargas  4 activas · 1 pausada · 1 con error" &&
                DownloadsPresentation.Indicator(groups.SelectMany(group => group.Items).ToArray()).StartsWith("⇣ 4 · 50% · ", StringComparison.Ordinal) &&
                DownloadsPresentation.Indicator(groups.SelectMany(group => group.Items).ToArray()).EndsWith("/s", StringComparison.Ordinal) &&
                Markup.Plain(DownloadsPresentation.Rows([], [])[0]).Trim() == Messages.NoDownloads &&
                Markup.Plain(DownloadsPresentation.Title([])) == "Descargas" && DownloadsPresentation.Failure("model-transfer-interrupted") == Messages.TransferCut && DownloadsPresentation.Failure("cleanup-pending") == Messages.TransferFailed && DownloadCli.ErrorLine("model-transfer-interrupted").StartsWith(Messages.Error("model-transfer-interrupted", Messages.TransferCut), StringComparison.Ordinal) && DownloadCli.ErrorLine("invalid-selector").EndsWith("(invalid-selector).", StringComparison.Ordinal) && DownloadCli.ErrorLine("transfer-paused").Contains("(transfer-paused)") && DownloadCli.ErrorLine("transfer-cancelled") == Messages.DownloadCancelled,
                "title, ⇣ indicator and empty tab");
            check(Markup.Plain(Markup.Fit(LivePresentation.Tabs(3, 1, 0, "⇣ 1 · 68%"), 80)).Contains(" ⇣ 1 · 68% ") &&
                Markup.Parse(Markup.Fit(LivePresentation.Tabs(3, 1, 0, "⇣ 1 · 68%"), 80)).Any(span => span.Class == "sel" && span.Action == "downloads"),
                "the ⇣ tab is highlighted while active");
            var localFolder = Path.Combine(root, "unregistered"); Directory.CreateDirectory(localFolder);
            File.WriteAllText(Path.Combine(localFolder, "coquena.yaml"), "name: fresh\nservice:\n  backend:\n    version: latest\n    variant: cpu\n");
            var localPlace = new DashboardCli.DefinitionsSource(home, localFolder).Places().Single(place => place.Scope.Here);
            var localDefinition = DashboardCli.ServiceDefinitions(localPlace, [], new Dictionary<int, ServiceSnapshot>()).Single();
            var freshPage = ManagementPresentation.Services("fresh", false, [localDefinition], localDefinition.Key);
            check(localDefinition.Id is null && localDefinition.State == "sin registrar" && !Directory.Exists(Path.Combine(localFolder, ".coquena")) &&
                freshPage.Rows.SelectMany(Markup.Parse).Any(span => span.Action == "live") && !freshPage.Rows.SelectMany(Markup.Parse).Any(span => span.Action == "remove"),
                "a fresh local service definition can navigate to En vivo without inventing registration or allowing removal");
            await Transfers(context, root, check);
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class Transport(byte[] bytes, TaskCompletionSource? started = null) : IDownloadTransport
    {
        public async Task<HttpResponseMessage> OpenAsync(OperationDocument operation, int file, long offset, CancellationToken cancellation)
        {
            if (started is not null) { started.TrySetResult(); await Task.Delay(Timeout.Infinite, cancellation); }
            var response = new HttpResponseMessage(offset == 0 ? System.Net.HttpStatusCode.OK : System.Net.HttpStatusCode.PartialContent)
            { Content = new ByteArrayContent(bytes[(int)offset..]) };
            if (offset > 0) response.Content.Headers.ContentRange = new System.Net.Http.Headers.ContentRangeHeaderValue(offset, bytes.Length - 1, bytes.Length);
            return response;
        }
    }

    private static async Task Transfers(ScopeContext context, string root, Action<bool, string> check)
    {
        byte[] bytes = [.. "GGUF"u8.ToArray(), 3, 0, 0, 0, .. new byte[16]];
        var id = Guid.NewGuid();
        var remote = new RemoteFile("weights.gguf", bytes.Length,
            new ContentIdentity("sha256", "file-bytes", Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes))));
        var operation = new OperationDocument(1, id, "model", "management-fixture", new("fixture/model", "main", new string('a', 40), [remote]), null,
            [new(remote.Path, $"downloads/{id:D}/files/0.part", "models/weights.gguf", 0, bytes.Length, remote.Identity, null, null, "pending")],
            "pending", "transfer", DateTimeOffset.UtcNow, null);
        var transfers = DashboardCli.Transfers(context);
        new RecipeRegistry(new ScopeStore(context)).RegisterResource("management-fixture", "model", "managed");
        transfers.Create(operation);
        var otherFolder = Path.Combine(root, "other"); Directory.CreateDirectory(otherFolder);
        var otherContext = ScopeContext.Project(Path.Combine(otherFolder, "coquena.yaml"));
        var otherTransfers = DashboardCli.Transfers(otherContext);
        new RecipeRegistry(new ScopeStore(otherContext)).RegisterResource("management-fixture", "model", "managed");
        otherTransfers.Create(operation);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = DashboardCli.ResumeTransfer(context, id, null, CancellationToken.None, new Transport(bytes, started));
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await transfers.ControlAsync(id, false);
        check((await running).Contains("paused") && transfers.Downloads.Status(id).State == "paused" &&
            otherTransfers.Downloads.Status(id).State == "pending", "the TUI transfer worker can be paused without controlling another scope's same GUID");
        var completed = await DashboardCli.ResumeTransfer(context, id, null, CancellationToken.None, new Transport(bytes));
        check(completed.Contains("publicada") && DashboardCli.TransferList(context).Length == 0,
            "the TUI resume worker verifies and publishes the resource then removes its terminal operation");
        await otherTransfers.ControlAsync(id, true);
        check(DashboardCli.TransferList(otherContext).Length == 0 && new Coquena.Models.ScopedModels(context).List().Count == 1,
            "cancelling the other operation preserves the published installation");
    }
}
