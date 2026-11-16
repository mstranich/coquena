using System.Text.Json.Nodes;
using System.Text.Json;
using Coquena.Backends;
using Coquena.Contracts;
using Coquena.Models;
using Coquena.Observation;
using Coquena.Projects;
using Coquena.Storage;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using static Coquena.Observation.Markup;

namespace Coquena;

internal static partial class DashboardCli
{
    internal sealed class MaintenanceView
    {
        public CleanupPlan? Plan;
        public string? Verification;
        public string? Cleanup;
        public HashSet<string> Kinds = ["model", "companion", "backend"];
        public bool Downloads;
        public CleanupSelection Selection => new(Kinds.Order().ToArray(), [], Downloads, Kinds.Count > 0);
        public void Toggle(string kind)
        {
            if (kind == "downloads") Downloads = !Downloads;
            else if (kind is "model" or "companion" or "backend") { if (!Kinds.Remove(kind)) Kinds.Add(kind); }
            Plan = null;
        }
    }

    internal static DefinitionsPresentation.Page MaintenancePage(string scope, MaintenanceView state, bool busy = false, bool forgettable = false, bool verifiable = true)
    {
        // While Verificar or Limpiar runs, both actions are disabled and the spinner shows the wait.
        string Act(string text, string action) => busy ? DefinitionsPresentation.Off(text) : ManagementPresentation.Button(text, action);
        var rows = new List<string>();
        if (busy) rows.Add(" " + Markup.Style("warn", DashboardPresentation.Spinner() + " ") + Markup.Style("mut", Messages.MaintenanceWorking));
        // A folder without recipe has nothing to verify.
        if (verifiable)
        {
            rows.Add(" " + Act(Messages.MaintenanceVerify, "maintenance-verify") + " " + Markup.Style("mut", Messages.MaintenanceVerifyHint));
            if (state.Verification is not null) rows.AddRange(state.Verification.Split('\n').Select(row => " " + Escape(row)));
            rows.Add("");
        }
        rows.Add(" " + Act(Messages.MaintenancePreview, "maintenance-preview") + " " + Markup.Style("mut", Messages.MaintenanceCleanupHint));
        rows.Add(" " + string.Join(" ", new[] { ("model", Messages.MaintenanceModels), ("companion", Messages.MaintenanceCompanions), ("backend", Messages.MaintenanceBackends) }
            .Select(item => Act((state.Kinds.Contains(item.Item1) ? "☑ " : "☐ ") + item.Item2, "maintenance-filter-" + item.Item1))));
        rows.Add(" " + Act((state.Downloads ? "☑ " : "☐ ") + Messages.MaintenanceDownloads, "maintenance-filter-downloads"));
        if (state.Plan is { } plan)
        {
            rows.Add(" " + Markup.Style("mut", Messages.MaintenanceColumns));
            foreach (var item in plan.Items) rows.Add(" " + Escape(Messages.Kind(item.Kind) + " " + item.InstallationId) + " " + Escape(ByteSize.Format(item.Bytes)));
            foreach (var item in plan.Downloads ?? []) rows.Add(" " + Escape(Messages.MaintenanceDownloadName(item.Name)) + " " + Escape(ByteSize.Format(item.Bytes)));
            foreach (var note in plan.Diagnostics) rows.Add(" " + Markup.Style("warn", Markup.Escape(Messages.PruneKept(note))));
            if (plan.Items.Count + (plan.Downloads?.Count ?? 0) == 0) rows.Add(" " + Markup.Style("mut", Messages.MaintenanceEmpty));
            rows.Add(" " + Markup.Style("hi", Messages.MaintenanceRecoverable(plan.Bytes)));
            if (plan.Items.Count + (plan.Downloads?.Count ?? 0) > 0) rows.Add(" " + Act(Messages.MaintenanceDelete, "maintenance-clean"));
        }
        if (state.Cleanup is not null) rows.AddRange(state.Cleanup.Split('\n').Select(row => " " + Escape(row)));
        if (forgettable) { rows.Add(""); rows.Add(" " + Act(Messages.MaintenanceForget, "project-forget") + " " + Markup.Style("mut", Messages.ForgetProjectHint)); }
        return new(Messages.MaintenanceTitle(Escape(scope)), rows);
    }

    private sealed class HubView
    {
        public bool Active;
        public string Query = "", Revision = "main";
        public List<string> Results = [];
        public Uri? Next;
        public bool Searched;
        public string? Repository;
        public RepositoryRevision? Resolved;
        public ModelVariant[] Variants = [];
        public int Selected;
        public int ResultOffset;
        public string? Status;
        public string[] Diagnostics = [];
        public string? Readme;
    }

    // The Hub search asks for pages of 20; 4 visible rows divide them evenly.
    private const int HubStep = 4;

    // While a query runs, its network actions are disabled and a status line says what is pending.
    private static DefinitionsPresentation.Page HubPage(string scope, HubView hub, int width, bool busy = false)
    {
        string Net(string text, string action) => busy ? DefinitionsPresentation.Off(text) : ManagementPresentation.Button(text, action);
        string Glyph(string text, string action, bool enabled) => enabled && !busy ? ManagementPresentation.Button(text, action) : DefinitionsPresentation.Off(text);
        var rows = new List<string>
        {
            " " + ManagementPresentation.Button(Messages.LabelsInventory.ToString(), "inventory") + " " + Markup.Style("sel", " Hugging Face "),
            " " + Net(Messages.HubSearchButton.Format(hub.Query.Length == 0 ? "GGUF" : hub.Query), "hf-search") +
                " " + Net(Messages.LabelsRepository2.ToString(), "hf-repository")
        };
        if (busy) rows.Add(" " + Markup.Style("warn", DashboardPresentation.Spinner() + " ") +
            Markup.Style("mut", DashboardPresentation.Fit(hub.Status ?? Messages.LabelsQuerying3.ToString(), width - 5)));
        foreach (var (repository, index) in hub.Results.Select((repository, index) => (repository, index)).Skip(hub.ResultOffset).Take(HubStep))
            rows.Add(" " + (busy ? Markup.Style("off", DashboardPresentation.Fit(repository, width - 3))
                : Markup.Style(repository == hub.Repository ? "sel" : "nav", DashboardPresentation.Fit(repository, width - 3), "hf-result:" + index)));
        if (hub.Searched && hub.Results.Count == 0) rows.Add(" " + Markup.Style("mut", Messages.HubNoResults.ToString()));
        // One pager on the left over a single list; "+" means the Hub has more to load.
        if (hub.Results.Count > 3 || hub.Next is not null)
            rows.Add(" " + Glyph("‹", "hf-previous-results", hub.ResultOffset > 0) +
                Glyph("›", "hf-more-results", hub.ResultOffset + HubStep < hub.Results.Count || hub.Next is not null) + " " +
                Markup.Style("mut", Messages.HubResultsRange.Format(Math.Min(hub.ResultOffset + 1, hub.Results.Count), Math.Min(hub.ResultOffset + HubStep, hub.Results.Count), hub.Results.Count, (hub.Next is null ? "" : "+"))));
        if (hub.Repository is not null)
        {
            rows.Add(""); rows.Add(" " + Markup.Style("hi", hub.Repository));
            rows.Add(" " + Net(Messages.HubRevisionButton.Format(hub.Revision), "hf-revision") + " " + ManagementPresentation.Button("README", "hf-readme"));
            if (hub.Resolved is not null) rows.Add(" " + Markup.Style("mut", "commit " + hub.Resolved.Commit));
            if (hub.Variants.Length > 0)
            {
                var selected = hub.Variants[Math.Clamp(hub.Selected, 0, hub.Variants.Length - 1)];
                rows.Add(" " + ManagementPresentation.Button(selected.Label + " ▾", "hf-variant"));
                rows.Add(" " + Markup.Style("mut", Messages.HubSelectedFilesSummary.Format(selected.Files.Length, ByteSize.Format(selected.Files.Sum(file => file.Size)), selected.CompanionType ?? Messages.LabelsModel.ToString())));
                rows.Add(" " + ManagementPresentation.Button(Messages.LabelsFiles2.ToString(), "hf-files"));
                if (hub.Readme is not null)
                    rows.AddRange(hub.Readme.Split('\n').Where(line => line.Length > 0).Take(2).Select(line => " " + Escape(DashboardPresentation.Fit(line.TrimEnd('\r'), width - 2))));
                rows.Add(" " + ManagementPresentation.Button(Messages.LabelsDownload2.ToString(), "hf-download") + " " + ManagementPresentation.Button(Messages.LabelsDownloads.ToString(), "downloads"));
            }
            foreach (var diagnostic in hub.Diagnostics) rows.Add(" " + Markup.Style("warn", diagnostic));
        }
        return new(Messages.ModelsInScope.Format(Escape(scope)) + " · Hugging Face", rows);
    }

    private sealed record LocalModelForm(string Path, string Name, string? Type, bool Match);

    private static LocalModelForm? AddLocalModelForm(IApplication app, string scope)
    {
        using var dialog = Modal(app, 74, 17, Messages.ResourceAddLocalModelTitle.Format(scope));
        var path = new TextField { X = 13, Y = 0, Width = Dim.Fill(9) };
        var browse = new Button { X = Pos.AnchorEnd(8), Y = 0, Text = " … ", NoDecorations = true, ShadowStyle = ShadowStyles.None };
        browse.Accepting += (_, args) =>
        {
            args.Handled = true;
            using var picker = new OpenDialog { Title = Messages.ResourceChooseGgufDialogTitle.ToString(), OpenMode = OpenMode.File,
                AllowsMultipleSelection = false, MustExist = true, FileOperationsHandler = null!,
                BorderStyle = LineStyle.None, ShadowStyle = ShadowStyles.None, Width = Dim.Percent(90), Height = Dim.Percent(85) };
            if (path.Text.Length > 0) picker.Path = path.Text;
            app.Run(picker);
            if (!picker.Canceled) path.Text = picker.Path;
        };
        var name = new TextField { X = 13, Y = 3, Width = Dim.Fill(2) };
        var type = new OptionSelector { X = 13, Y = 5, Labels = [Messages.LabelsModel.ToString(), "mmproj", "mtp"], Orientation = Orientation.Horizontal };
        var match = new CheckBox { X = 1, Y = 9, Text = Messages.ResourceFindMatchOnHub.ToString() };
        var problem = new Label { X = 1, Y = 12, Width = Dim.Fill(1), Height = 2 };
        dialog.Add(new Label { X = 1, Y = 0, Text = Messages.LabelsFile.ToString() }, path, browse,
            new Label { X = 1, Y = 1, Width = Dim.Fill(1), Text = Messages.ResourceFirstShardHint.ToString() },
            new Label { X = 1, Y = 3, Text = Messages.LabelsName.ToString() }, name,
            new Label { X = 1, Y = 5, Text = Messages.LabelsType.ToString() }, type,
            new Label { X = 1, Y = 7, Width = Dim.Fill(1), Text = Messages.ResourceRegisterExternalHint.ToString() }, match,
            new Label { X = 1, Y = 10, Width = Dim.Fill(1), Text = Messages.ResourceMatchingSyntaxHint.ToString() }, problem);
        dialog.AddButton(new Button { Text = Messages.DashboardCancelButton.ToString(), NoDecorations = true, ShadowStyle = ShadowStyles.None });
        var add = new Button { Text = Messages.ManagementAddButton.ToString(), NoDecorations = true, ShadowStyle = ShadowStyles.None };
        add.Accepting += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(path.Text) || string.IsNullOrWhiteSpace(name.Text))
            { problem.Text = Messages.ResourceFileOrNameMissing.ToString(); args.Handled = true; }
        };
        dialog.AddButton(add);
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")))
        {
            Paint(dialog, Palette.Text, Palette.Card);
            foreach (var view in dialog.SubViews) Paint(view, view is Label ? Palette.Muted : Palette.Text,
                view is TextField ? Palette.Base : view is Button ? Palette.Border : Palette.Card);
            Paint(problem, Palette.Danger, Palette.Card);
        }
        app.Run(dialog);
        return dialog.Result == 1 ? new(path.Text.Trim(), name.Text.Trim(), type.Value == 1 ? "mmproj" : type.Value == 2 ? "mtp" : null,
            match.Value == CheckState.Checked) : null;
    }

    private static string VerificationText(VerificationReport report) =>
        (report.Verified ? Messages.ResourceAuditCompleted.ToString() : Messages.ResourceAuditNeedsReview.ToString()) + "\n" +
        string.Join("\n", report.Resources.Select(resource => resource!["key"] + ": " + resource["status"] + "\n" +
            string.Join("\n", resource["files"]!.AsArray().Select(file => "  " + file!["path"] + ": " + file["status"]))));

    private static string CleanupText(CleanupResult result) => Messages.ResourceFreedAmount.Format(ByteSize.Format(result.FreedBytes)) +
        (result.Diagnostics.Count == 0 ? "" : "\n▲ " + string.Join("\n", result.Diagnostics.Select(n => Messages.PruneFailed(n.Name, n.Code))));

    private static string[] ResourceDetails(InventoryRow item, ScopeContext context)
    {
        var rows = new List<string> { item.Label, Messages.ResourceDetailScope.Format(context.IsGlobal ? "Global" : context.Base),
            Messages.ResourceDetailOwnership.Format(item.External ? Messages.LabelsExternal2.ToString() : Messages.LabelsManaged.ToString()),
            Messages.ResourceDetailInstallation.Format(item.Id), Messages.ResourceDetailId.Format(item.ResourceId) };
        foreach (var file in item.Installation["files"] as JsonArray ?? [])
            rows.Add((file?["path"]?.ToString() ?? "—") + " · " + (file?["size"] is { } size ? ByteSize.Format(size.GetValue<long>()) : Messages.ResourceUnknownSize.ToString()));
        if (item.Installation["verification"] is { } verification)
        {
            rows.Add(Messages.ResourceLastAudit.Format(verification["checked-at"], verification["mode"], verification["status"]));
            rows.AddRange((verification["files"] as JsonArray ?? []).Select(file => file!["path"] + ": " + file["status"]));
        }
        else rows.Add(Messages.ResourceNoRecordedAudit.ToString());
        if (item.Resource["informative-match"] is not null) rows.Add(Messages.ModelExternalSameAs.Format(item.Resource["informative-match"]?["candidate"]?["repository"]));
        rows.AddRange(item.References.Select(reference => Messages.ResourceLocalReference.Format(reference)));
        return rows.ToArray();
    }

    // Forms keep the visual language of S: card background, base fields, no borders.
    private static string[]? ResourceForm(IApplication app, string title, (string Label, string Value)[] fields, string accept)
    {
        using var dialog = Modal(app, 74, fields.Length * 2 + 3, title);
        var inputs = fields.Select((field, index) =>
        {
            dialog.Add(new Label { X = 1, Y = index * 2, Text = field.Label });
            var input = new TextField { X = 14, Y = index * 2, Width = Dim.Fill(2), Text = field.Value };
            dialog.Add(input); return input;
        }).ToArray();
        dialog.AddButton(new Button { Text = Messages.DashboardCancelButton.ToString(), NoDecorations = true, ShadowStyle = ShadowStyles.None });
        dialog.AddButton(new Button { Text = " " + accept + " ", NoDecorations = true, ShadowStyle = ShadowStyles.None });
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")))
        {
            Paint(dialog, Palette.Text, Palette.Card);
            foreach (var view in dialog.SubViews) Paint(view, view is Label ? Palette.Muted : Palette.Text,
                view is TextField ? Palette.Base : view is Button ? Palette.Border : Palette.Card);
        }
        app.Run(dialog);
        return dialog.Result == 1 ? inputs.Select(input => input.Text.Trim()).ToArray() : null;
    }

    internal sealed record InventoryRow(JsonNode Resource, JsonNode Installation)
    {
        public string[] References { get; init; } = [];
        public string ResourceId => Resource["resource-id"]!.GetValue<string>();
        public string Id => Installation["installation-id"]!.GetValue<string>();
        public string Kind => Resource["kind"]!.GetValue<string>();
        public bool External => Resource["ownership"]?.GetValue<string>() == "external";
        public bool Protected => Installation["protected"]?.GetValue<bool>() == true;
        public string Name => Resource["identity"]?.GetValue<string>() ?? Resource["logical-key"]!.GetValue<string>();
        public long Size => (Installation["files"] as JsonArray ?? []).Sum(file => file?["size"]?.GetValue<long>() ?? 0);
        public string Label => Kind == "backend" ?
            (Installation["selection"]?["backendSelection"]?["variant"]?.GetValue<string>() ?? Name) + " · " +
            (Installation["selection"]?["backendSelection"]?["release"]?.GetValue<string>() ?? Messages.LabelsExternal.ToString()) : Name;
    }

    internal static InventoryRow[] Inventory(ScopeContext context, bool backend)
    {
        IEnumerable<JsonNode?> resources = backend ? new ScopedBackends(context).List() :
            new ScopedModels(context).Query().Concat(new ScopedModels(context).Query(true));
        var documents = new ScopeStore(context).Read().Documents;
        return resources.SelectMany(resource => resource!["installations"]!.AsArray()
            .Select(installation => new InventoryRow(resource, installation!)
            {
                References = documents.Where(document => document.Key != "local/inventory.json" &&
                    ContainsIdentity(document.Value, installation!["installation-id"]!.GetValue<string>()))
                    .Select(document => document.Key).ToArray()
            })).ToArray();
    }

    private static bool ContainsIdentity(JsonNode? node, string id) => node switch
    {
        JsonValue value => value.TryGetValue<string>(out var text) && text == id,
        JsonObject obj => obj.Any(pair => ContainsIdentity(pair.Value, id)),
        JsonArray array => array.Any(item => ContainsIdentity(item, id)),
        _ => false
    };

    internal static DefinitionsPresentation.Page ResourcePage(string scope, bool backend, IReadOnlyList<InventoryRow> items,
        string? selected, string filter, int width, int height = 20, HardwareContext? hardware = null)
    {
        var rows = new List<string>();
        if (!backend) rows.Add(" " + Markup.Style("sel", Messages.ResourceInventoryTab.ToString()) + " " + ManagementPresentation.Button("Hugging Face", "hf") +
            " " + ManagementPresentation.Button(Messages.DashboardFilterValue.Format(filter.Length == 0 ? Messages.LabelsAll.ToString() : filter), "model-filter"));
        rows.Add(" " + Markup.Style("mut", DashboardPresentation.Fit(backend ? Messages.ResourceBackendsTableHeader.ToString() : Messages.ResourceModelsTableHeader.ToString(), width - 2)));
        var position = Math.Max(0, items.ToList().FindIndex(item => item.Id == selected));
        var visible = Math.Max(1, Math.Min(5, height - 12));
        var start = position / visible * visible;
        foreach (var item in items.Skip(start).Take(visible))
        {
            var label = DashboardPresentation.Fit(item.Label, Math.Max(8, width - 24)).PadRight(Math.Max(8, width - 24));
            rows.Add(" " + Markup.Style(item.Id == selected ? "sel" : "nav", label, "resource:" + item.Id) + " " +
                Markup.Style("mut", item.External ? Messages.ResourceExternalBadge.ToString() : Messages.LabelsManaged2.ToString()) + " " + Escape(ByteSize.Format(item.Size)));
        }
        if (items.Count > visible)
            rows.Add(" " + ManagementPresentation.Button("◂", "resource-prev") + " " + Markup.Style("mut", $"{start + 1}–{Math.Min(items.Count, start + visible)} / {items.Count}") +
                " " + ManagementPresentation.Button("▸", "resource-next"));
        if (items.Count == 0) rows.Add(" " + Markup.Style("mut", (filter.Length > 0 ? Messages.ResourceNoneForFilter.ToString() : Messages.ResourceNoneInScope.ToString())));
        if (items.FirstOrDefault(item => item.Id == selected) is { } current)
        {
            rows.Add("");
            rows.Add(" " + Markup.Style("hi", current.Label) + "  " + Markup.Style(current.Protected ? "ok" : "mut", current.Protected ? Messages.ResourceProtectedBadge.ToString() : Messages.ResourceUnprotectedBadge.ToString()));
            var installation = current.Installation;
            var selection = installation["selection"]?[backend ? "backendSelection" : "modelSelection"];
            rows.Add(" " + Markup.Style("mut", Messages.ResourcePathLabel.ToString()) + Escape(installation["executable"]?.ToString() ?? installation["location"]?.ToString() ?? Messages.ResourceExternalFilesValue.ToString()));
            if (!backend)
            {
                rows.Add(" " + Markup.Style("mut", Messages.ResourceAvailabilityLabel.ToString()) + Escape(installation["observed-availability"]?.ToString() ?? Messages.ResourceUnobservedValue.ToString()) +
                    " · " + Escape(installation["integrity-status"]?.ToString() ?? Messages.ResourceIntegrityUnobserved.ToString()));
                rows.Add(" " + Markup.Style("mut", Messages.ResourceSourceRevisionLabel.ToString()) + Escape(selection?["repository"]?.ToString() ?? (current.External ? Messages.LabelsExternal.ToString() : "—")) +
                    " · " + Escape(selection?["commit"]?.ToString() ?? "—"));
            }
            else rows.Add(" " + Markup.Style("mut", Messages.ResourceCompatibilityLabel.ToString()) + Escape(BackendCompatibility(current, hardware)));
            rows.Add(" " + Markup.Style("mut", Messages.ResourceLocalReferencesLabel.ToString()) + Escape(current.References.Length == 0 ? Messages.ResourceNoReferencesRecorded.ToString() : string.Join(", ", current.References)));
            rows.Add(" " + ManagementPresentation.Button(Messages.ResourceFilesAndObservationsButton.ToString(), "resource-detail"));
            rows.Add(" " + (backend && current.External ? Markup.Style("mut", Messages.ResourceExternalOwnershipPreserved.ToString()) :
                ManagementPresentation.Button(current.Protected ? Messages.ResourceRemoveProtectionButton.ToString() : Messages.LabelsProtect.ToString(), "resource-protect")) +
                (!backend ? " " + ManagementPresentation.Button(Messages.LabelsVerify.ToString(), "resource-verify") : ""));
            rows.Add(" " + ManagementPresentation.Button(Messages.LabelsRemove.ToString(), "resource-remove"));
        }
        return new(Messages.DashboardScopeOf.Format(backend ? "Backends" : Messages.LabelsModels.ToString(), Escape(scope)) + "{pad}" +
            ManagementPresentation.Button(backend ? "+ Backend" : Messages.ResourceAddLocalButton.ToString(), backend ? "backend" : "model-add") + " ", rows);
    }
    private static string BackendCompatibility(InventoryRow item, HardwareContext? hardware)
    {
        if (item.External) return Messages.LabelsExternal.ToString();
        if (hardware is null) return Messages.ResourceProbeUnavailable.ToString();
        var warnings = new List<string>();
        try
        {
            var selection = item.Installation["selection"]!["backendSelection"]!.Deserialize(ContractJsonContext.Default.BackendDownloadSelection)!;
            ScopedBackends.CheckSelection(selection, hardware, false, warnings.Add);
            return warnings.Count == 0 ? Messages.ResourceCompatibleUnverified.ToString() : string.Join(" · ", warnings);
        }
        catch (BackendException error) { return "▲ " + error.Message; }
    }
}
