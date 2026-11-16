using System.Globalization;
using static Coquena.Observation.Markup;

namespace Coquena.Observation;

// Rows of the Definiciones tab in the notation of the TUI preview (docs/design/tui).
// Read-only layer: every button of the preview is drawn disabled ("off", no action).
internal static class DefinitionsPresentation
{
    public const int TreeWidth = LivePresentation.TreeWidth;

    internal sealed record Scope(string Name, bool Global, bool Here, bool Accessible, bool HasRecipe);
    internal sealed record Node(int Scope, string? View);
    internal sealed record Page(string Title, IReadOnlyList<string> Rows);

    internal sealed record Combination(string Files, string Id, bool Private, bool Current);
    internal sealed record Preset(string Name, string Model, string? Live);
    internal sealed record Service(string Name, bool Default, string? State, string? StateClass);
    internal sealed record Model(string Name, bool Companion, bool External, long? Size, bool Protected);
    internal sealed record Backend(string Variant, string Version, bool External, long? Size, bool Protected);
    internal sealed record Parameter(string Name, string Type, string Application, string? Default);
    internal sealed record Download(string Name, string Kind, string State, string Phase, long Done, long? Total, string? Diagnostic);
    internal sealed record Update(string Resource, string Current, string Proposed, string? Diagnostic);

    public static string Off(string text) => Style("off", " " + text + " ");

    public static string[] Views(Scope scope) => !scope.Accessible ? []
        // Presets come after the entities they join: what is used first, then the presets that relate it.
        : scope.Global ? ["backends", "services", "models", "parameters", "presets", "updates", "maintenance"]
        : !scope.HasRecipe ? ["recipe", "maintenance"]
        : ["recipes", "backends", "service", "models", "parameters", "presets", "updates", "maintenance"];

    // Scope headers and, under the focused scope only, its views.
    public static IReadOnlyList<Node> Tree(IReadOnlyList<Scope> scopes, int focused)
    {
        var nodes = new List<Node>();
        for (var i = 0; i < scopes.Count; i++)
        {
            nodes.Add(new(i, null));
            if (i == focused) nodes.AddRange(Views(scopes[i]).Select(view => new Node(i, view)));
        }
        return nodes;
    }

    public static string TreeRow(Node node, IReadOnlyList<Scope> scopes, int focused, bool selected, string? badge = null)
    {
        var scope = scopes[node.Scope];
        string text, plain;
        if (node.View is { } view)
        {
            plain = "  " + Messages.ViewLabel(view) + (badge is null ? "" : " " + badge);
            text = "  " + Style("nav", Messages.ViewLabel(view)) + (badge is null ? "" : " " + Style("foc", badge));
        }
        else
        {
            var glyph = !scope.Accessible ? "✗" : node.Scope == focused ? "▾" : "▸";
            var name = DashboardPresentation.Fit(scope.Name, TreeWidth - 4 - (scope.Here ? 7 : 0));
            plain = " " + glyph + " " + name + (scope.Here ? Messages.DefinitionsHerePlain.ToString() : "");
            text = " " + Style("mut", glyph + " " + name) + (scope.Here ? " " + Style("mut", Messages.DefinitionsHereTag.ToString()) : "");
        }
        return selected ? Style("focb", plain.PadRight(TreeWidth)) : text;
    }

    public static Page Recipes(string scope, string? destination, IReadOnlyList<Combination> items, IReadOnlyList<string> files)
    {
        var rows = new List<string> { " " + Style("mut", "  " + Cell(Messages.DefinitionsCombinationColumn.ToString(), 28) + " " + Cell("ID", 8) + Messages.DefinitionsStateColumn.ToString()) };
        rows.AddRange(items.Select(item => "  " + Escape(Cell(item.Files, 28) + " " + Cell(item.Id, 8)) + " " +
            (item.Current ? Style("ok", Messages.DefinitionsStateCurrent.ToString()) : item.Private ? Style("warn", Messages.LabelsPrivate.ToString()) : Style("mut", Messages.LabelsKnown.ToString()))));
        if (items.Count == 0) rows.Add(" " + Style("mut", Messages.DefinitionsNoCombinations.ToString()));
        rows.Add("");
        rows.Add(" " + Off(Messages.DefinitionsNewCombinationButton.ToString()) + " " + Off(Messages.LabelsEdit.ToString()) + " " + Off(Messages.LabelsForget.ToString()));
        rows.Add("");
        if (files.Count > 0) rows.Add(" " + Style("mut", Messages.LabelsFiles.ToString()) + " " + string.Join(" " + Style("mut", "→") + " ", files.Select(Escape)));
        rows.Add(" " + Style("mut", Messages.DefinitionsPrivatePreparationBox.ToString()));
        rows.Add("");
        rows.Add(" " + Off("▲ Up") + " " + Off("▼ Down") + " " + Off(Messages.LabelsImport.ToString()) + " " + Off(Messages.AuthoringViewLiveButton.ToString()));
        return new(Messages.RecipesInScope.Format(Escape(scope)) + (destination is null ? "" : " " + Style("mut", "→") + " " + Escape(destination)), rows);
    }

    public static Page Presets(string scope, IReadOnlyList<Preset> items)
    {
        var rows = new List<string> { " " + Style("mut", "  " + Cell("PRESET", 20) + Cell(Messages.LabelsMODEL.ToString(), 22) + Messages.DefinitionsLiveColumn.ToString()) };
        rows.AddRange(items.Select(item => "  " + Escape(Cell(item.Name, 20) + Cell(item.Model, 22)) +
            (item.Live is { } live ? Style(live == "loaded" ? "ok" : "mut", (live == "loaded" ? "● " : "○ ") + live) : Style("mut", Messages.DefinitionsUnpublished.ToString()))));
        if (items.Count == 0) rows.Add(" " + Style("mut", Messages.DefinitionsNoPresets.ToString()));
        rows.Add("");
        rows.Add(" " + Off(Messages.LabelsClone.ToString()) + " " + Off(Messages.AuthoringEditYamlButton.ToString()) + " " + Off(Messages.LabelsRemove.ToString()) + " " + Off(Messages.AuthoringViewLiveButton.ToString()));
        return new(Messages.PresetsInScope.Format(Escape(scope)), rows);
    }

    public static Page Services(string scope, bool global, IReadOnlyList<Service> items)
    {
        var rows = new List<string> { " " + Style("mut", "  " + Cell(Messages.LabelsSERVICE.ToString(), 24) + Messages.LabelsSTATE.ToString()) };
        rows.AddRange(items.Select(item => " " + (item.Default ? "★" : " ") + " " + Escape(Cell(item.Name, 24)) +
            (item.State is { } state ? Style(item.StateClass ?? "mut", state) : "")));
        if (items.Count == 0) rows.Add(" " + Style("mut", global ? Messages.DefinitionsNoRegisteredServices.ToString() : Messages.DefinitionsNoRecipeService.ToString()));
        rows.Add("");
        rows.Add(" " + Off(Messages.DefinitionsDefaultButton.ToString()) + " " + Off(Messages.LabelsRemove.ToString()) + " " + Off(Messages.AuthoringEditYamlButton.ToString()) + " " + Off(Messages.AuthoringViewLiveButton.ToString()));
        return new((global ? Messages.ServicesInScope.Format(Escape(scope)) : Messages.ServiceInScope.Format(Escape(scope))) + "{pad}" + Off(Messages.DefinitionsAddButton.ToString()), rows);
    }

    public static Page Models(string scope, IReadOnlyList<Model> items)
    {
        var rows = new List<string>
        {
            Messages.DefinitionsInventoryRadio.ToString() + Style("mut", "Hugging Face"),
            "",
            " " + Style("mut", "  " + Cell(Messages.LabelsMODEL.ToString(), 32) + Cell(Messages.LabelsSOURCE.ToString(), 12) + Messages.DefinitionsSizeColumn.ToString())
        };
        rows.AddRange(items.Select(item =>
        {
            var name = item.Name + (item.Companion ? " ·c" : "");
            return "  " + Escape(Cell(name, 32)) + (item.External ? Style("warn", Cell(Messages.LabelsExternal.ToString(), 12)) : Escape(Cell(Messages.LabelsManaged2.ToString(), 12))) +
                Escape(Size(item.Size)) + (item.Protected ? " " + Style("mut", "■") : "");
        }));
        if (items.Count == 0) rows.Add(" " + Style("mut", Messages.DefinitionsNoModels.ToString()));
        rows.Add("");
        rows.Add(" " + Off(Messages.ResourceRemoveProtectionButton.ToString()) + " " + Off(Messages.LabelsVerify.ToString()) + " " + Off(Messages.LabelsRemove.ToString()));
        return new(Messages.ModelsInScope.Format(Escape(scope)) + "{pad}" + Off(Messages.ResourceAddLocalButton.ToString()), rows);
    }

    public static Page Backends(string scope, IReadOnlyList<Backend> items)
    {
        var rows = new List<string> { " " + Style("mut", "  " + Cell(Messages.LabelsVARIANT.ToString(), 14) + Cell(Messages.DefinitionsVersionColumn.ToString(), 10) + Cell(Messages.LabelsSOURCE.ToString(), 10) + Messages.DefinitionsSizeColumn.ToString()) };
        rows.AddRange(items.Select(item => "  " + Escape(Cell(item.Variant, 14) + Cell(item.Version, 10)) +
            (item.External ? Style("warn", Cell(Messages.LabelsExternal.ToString(), 10)) : Escape(Cell("oficial", 10))) + Escape(Size(item.Size)) +
            (item.Protected ? " " + Style("mut", "■") : "")));
        if (items.Count == 0) rows.Add(" " + Style("mut", Messages.DefinitionsNoBackends.ToString()));
        rows.Add("");
        rows.Add(" " + Off(Messages.ResourceRemoveProtectionButton.ToString()) + " " + Off(Messages.LabelsRemove.ToString()));
        return new(Messages.BackendsInScope.Format(Escape(scope)) + "{pad}" + Off("+ Backend"), rows);
    }

    public static Page Parameters(string scope, IReadOnlyList<Parameter> items, int room)
    {
        var rows = new List<string>
        {
            " " + Style("mut", Messages.LabelsFilter2.ToString()) + " " + Style("mut", "[                ]") + " " + Style("mut", Messages.AuthoringParameterCount.Format(items.Count)),
            "",
            " " + Style("mut", " " + Cell(Messages.DefinitionsParameterColumn.ToString(), 22) + Cell(Messages.LabelsTYPE.ToString(), 9) + Cell(Messages.LabelsAPPLIES.ToString(), 10) + "PREDET.")
        };
        var shown = Math.Max(0, room - 6);
        rows.AddRange(items.Take(shown).Select(item => "  " + Escape(Cell(item.Name, 22) + Cell(item.Type, 9) + Cell(item.Application, 10) + (item.Default ?? "—"))));
        if (items.Count == 0) rows.Add(" " + Style("mut", Messages.DefinitionsNoDefinitions.ToString()));
        else if (items.Count > shown) rows.Add(" " + Style("mut", Messages.MoreRows(items.Count - shown)));
        rows.Add("");
        rows.Add(" " + Off(Messages.LabelsDefine.ToString()) + " " + Off(Messages.LabelsEdit.ToString()) + " " + Off(Messages.AuthoringRemoveDefinitionButton.ToString()));
        return new(Messages.ParametersInScope.Format(Escape(scope)), rows);
    }

    // With operation ids the controls are enabled and carry "pause:", "resume:" or "cancel:" plus the id.
    public static Page Updates(string scope, DateTimeOffset? planned, IReadOnlyList<Update> items)
    {
        var rows = new List<string>();
        if (planned is null) rows.Add(" " + Style("mut", Messages.NoUpdatePlan));
        else
        {
            rows.Add(" " + Style("mut", "    " + Cell(Messages.LabelsRESOURCE.ToString(), 22) + Cell(Messages.LabelsCURRENT.ToString(), 12) + Cell(Messages.LabelsPROPOSED.ToString(), 11) + Messages.LabelsSTATE.ToString()));
            rows.AddRange(items.Select(item =>
            {
                var state = item.Diagnostic is not null ? Style("warn", "▲ " + item.Diagnostic)
                    : item.Proposed != "—" && item.Proposed != item.Current ? Style("ok", Messages.UpdatesStateNew.ToString()) : Style("mut", "—");
                return " " + Style("mut", "☐") + " " + Escape(Cell(item.Resource, 22) + Cell(item.Current, 12) + Cell(item.Proposed, 11)) + state;
            }));
        }
        rows.Add("");
        rows.Add(" " + Off(Messages.LabelsCheck.ToString()) + " " + Off(Messages.LabelsUpdate.ToString()));
        var title = Messages.UpdatesInScope.Format(Escape(scope)) + (planned is { } at
            ? "  " + Style("mut", "plan " + at.ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.InvariantCulture)) : "");
        return new(title, rows);
    }

    public static Page Maintenance(string scope) =>
        new(Messages.MaintenanceInScope.Format(Escape(scope)), ["", " " + Off(Messages.LabelsVerify.ToString()) + " " + Off(Messages.LabelsClean.ToString())]);

    public static Page NoRecipe(string folder) =>
        new(Escape(folder) + " " + Style("mut", Messages.DefinitionsNoCoquenaYaml.ToString()),
            ["", " " + Style("btnf", Messages.DefinitionsInitializeButton.ToString(), "init"), "", " " + Style("mut", Messages.DefinitionsInitFolderDescription.Format(folder))]);

    // A known project that no longer resolves offers only Olvidar, under its message.
    public static Page Inaccessible(string name, bool forgettable = false) =>
        new(Escape(name), forgettable
            ? ["", " " + Style("err", "✗") + " " + Style("mut", Messages.ProjectPathGone), "", " " + ManagementPresentation.Button(Messages.MaintenanceForget, "project-forget") + " " + Style("mut", Messages.ForgetProjectHint)]
            : ["", " " + Style("err", "✗") + " " + Style("mut", Messages.ProjectPathGone)]);

    public static Page Problem(string title, string message) =>
        new(Escape(title), ["", " " + Style("err", "✗") + " " + Escape(message)]);

    private static string Cell(string text, int width) => DashboardPresentation.Fit(text, width - 1).PadRight(width);

    private static string Size(long? bytes) => bytes is { } value ? DashboardPresentation.Compact(value) : "—";
}
