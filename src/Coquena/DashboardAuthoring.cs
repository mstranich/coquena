using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using Coquena.Observation;
using Coquena.Projects;
using Coquena.Recipes;
using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Coquena;

internal static partial class DashboardCli
{
    internal sealed record PresetRow(string Id, string Name, EffectiveRecipe Recipe, RecipeValue Value,
        int? Target, string? State, JsonNode? Published);

    internal static PresetRow[] PresetRows(ScopeContext context, IReadOnlyList<RecipeSelection> selections,
        IReadOnlyDictionary<int, ServiceSnapshot> snapshots)
    {
        var scopeId = new ScopeStore(context).Read().ScopeId;
        var rows = new List<PresetRow>();
        foreach (var selection in selections)
        {
            var recipe = RecipeReader.Read(selection);
            foreach (var pair in recipe.Root.Get("presets")?.Fields ?? new Dictionary<string, RecipeValue>())
            {
                if (pair.Value.Kind == RecipeKind.Null) continue;
                var name = context.IsGlobal ? pair.Key : recipe.Name + "/" + pair.Key;
                JsonNode? published = null; int? target = null; string? state = null;
                foreach (var snapshot in snapshots)
                {
                    if (scopeId is null) continue;
                    var contribution = (snapshot.Value.ActiveComposition?["preset-composition"] as JsonArray ?? [])
                        .FirstOrDefault(item => item?["scope-id"]?.GetValue<string>() == scopeId &&
                            (item?["presets"] as JsonArray ?? []).Any(preset => preset?["name"]?.GetValue<string>() == name));
                    if (contribution is null) continue;
                    published = contribution["presets"]!.AsArray().First(preset => preset?["name"]?.GetValue<string>() == name);
                    target = snapshot.Key;
                    state = snapshot.Value.Entries.FirstOrDefault(entry => entry.Name == name)?.State;
                    break;
                }
                var id = CombinationIdentity.FromSelection(selection).Id + ":" + Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(pair.Key));
                rows.Add(new(id, pair.Key, recipe, pair.Value, target, state, published));
            }
        }
        return rows.ToArray();
    }

    internal static DefinitionsPresentation.Page PresetsPage(string scope, IReadOnlyList<PresetRow> presets, string? selected, int width, string combination = "Combinación")
    {
        var rows = new List<string> { " " + Markup.Style("mut", "PRESET                 MODELO              EN VIVO") };
        var chosen = presets.FirstOrDefault(item => item.Id == selected) ?? presets.FirstOrDefault();
        var position = chosen is null ? 0 : presets.ToList().IndexOf(chosen);
        foreach (var item in presets.Skip(position / 4 * 4).Take(4))
            rows.Add(" " + Markup.Style(item.Id == chosen?.Id ? "sel" : "nav", DashboardPresentation.Fit(item.Name, 21).PadRight(22), "preset:" + item.Id) +
                Markup.Escape(DashboardPresentation.Fit(item.Value.Get("args")?.Get("model")?.Redacted()?.ToString() ?? "—", 18).PadRight(19)) +
                Markup.Style(item.State == "loaded" ? "ok" : "mut", item.State ?? "sin señal"));
        if (presets.Count == 0) rows.Add(" " + Markup.Style("mut", "Sin presets en la selección."));
        if (presets.Count > 4) rows.Add(" " + ManagementPresentation.Button("◂", "preset-prev") + " " + ManagementPresentation.Button("▸", "preset-next"));
        if (chosen is not null)
        {
            var layers = chosen.Recipe.Selection;
            var legend = DashboardPresentation.Fit(LayerLegend(layers), Math.Max(1, 52 - Math.Min(21, chosen.Name.Length)));
            rows.Add(""); rows.Add(" " + Markup.Style("hi", DashboardPresentation.Fit(chosen.Name, 21)) + " " + Markup.Style("mut", "capas ") + Markup.Escape(legend));
            var args = chosen.Value.Get("args");
            foreach (var argument in args?.Fields.Where(pair => pair.Key == "model" || pair.Value.Text?.StartsWith("companion://", StringComparison.Ordinal) == true) ?? [])
                rows.Add(" " + Markup.Style("mut", argument.Key + " ") + Markup.Style("mut", LayerMark(layers, argument.Value.Origin.File) + " ") + Markup.Style("nav", argument.Value.Redacted()?.ToString() ?? "—", "preset-resource:" + Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(argument.Key))));
            rows.Add(" " + Markup.Style("mut", "ARGUMENTO       PUBLICADO          RECETA          CAPA"));
            var published = PublishedValues(chosen.Published?["ini"]?.GetValue<string>());
            var schema = ArgumentSchema.Load(chosen.Recipe);
            foreach (var pair in args?.Fields.Where(pair => pair.Key != "model").Take(4) ?? [])
            {
                var emitted = schema.Definitions.GetValueOrDefault(pair.Key)?.Get("emit")?.Get("ini")?.Get("name")?.Text ?? pair.Key;
                var prior = pair.Value.Sensitive ? "<redacted>" : published.GetValueOrDefault(emitted) ?? "—";
                rows.Add(" " + Markup.Escape(DashboardPresentation.Fit(pair.Key, 14).PadRight(15) + DashboardPresentation.Fit(prior, 18).PadRight(19) +
                    DashboardPresentation.Fit(pair.Value.Redacted()?.ToString() ?? "null", 15).PadRight(16) + LayerMark(layers, pair.Value.Origin.File)));
            }
            rows.Add(" " + Markup.Style("mut", "Publicado: snapshot registrado; — sin evidencia."));
            rows.Add(" " + ManagementPresentation.Button("Clonar…", "preset-clone") + " " + ManagementPresentation.Button("Editar YAML", "preset-edit") + " " + ManagementPresentation.Button("Eliminar", "preset-remove"));
            rows.Add(" " + (chosen.Target is null ? DefinitionsPresentation.Off("Ver en vivo") : ManagementPresentation.Button("Ver en vivo", "preset-live")) + " " + ManagementPresentation.Button("Detalle…", "preset-detail"));
        }
        return new("Presets de " + Markup.Escape(scope) + "{pad}" + ManagementPresentation.CombinationButton(combination), rows);
    }

    internal static Dictionary<string, string> PublishedValues(string? ini) => (ini ?? "").Split('\n')
        .Where(line => line.Contains('=') && !line.TrimStart().StartsWith('#'))
        .Select(line => line.Split('=', 2)).GroupBy(pair => pair[0].Trim(), StringComparer.Ordinal)
        .ToDictionary(group => group.Key, group => group.Last()[1].Trim(), StringComparer.Ordinal);

    internal sealed record DefinitionRow(string Id, string Name, EffectiveRecipe Recipe, RecipeValue Value, string[] Uses);

    internal static DefinitionRow[] DefinitionRows(IReadOnlyList<RecipeSelection> selections)
    {
        var recipes = selections.Select(selection => RecipeReader.Read(selection)).ToArray();
        var schemas = recipes.Select(recipe => ArgumentSchema.Load(recipe)).ToArray();
        var uses = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        for (var index = 0; index < recipes.Length; index++)
        {
            var recipe = recipes[index]; var schema = schemas[index];
            var blocks = (recipe.Root.Get("presets")?.Fields ?? new Dictionary<string, RecipeValue>()).Select(preset => (Name: recipe.Name + "/" + preset.Key, Args: preset.Value.Get("args"))).ToList();
            blocks.Add((recipe.Name + "/servicio", recipe.Root.Get("service")?.Get("server")?.Get("args")));
            foreach (var block in blocks)
                foreach (var key in block.Args?.Fields.Keys ?? [])
                {
                    var name = schema.Canonical(key);
                    if (!schema.Definitions.TryGetValue(name, out var definition)) continue;
                    var id = Id(name, definition);
                    if (!uses.TryGetValue(id, out var owners)) uses[id] = owners = new(StringComparer.Ordinal);
                    owners.Add(block.Name);
                }
        }
        return recipes.SelectMany((recipe, index) => schemas[index].Definitions.Select(pair =>
            new DefinitionRow(Id(pair.Key, pair.Value), pair.Key, recipe, pair.Value, uses.GetValueOrDefault(Id(pair.Key, pair.Value))?.ToArray() ?? [])))
            .DistinctBy(row => row.Id).ToArray();

        static string Id(string name, RecipeValue value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value.Origin.File + "\n" + name)));
    }

    internal static DefinitionsPresentation.Page ParametersPage(string scope, IReadOnlyList<DefinitionRow> definitions, string? selected, string filter, string combination = "Combinación", RecipeSelection? layers = null)
    {
        var rows = new List<string> { " " + ManagementPresentation.Button("filtro: " + (filter.Length == 0 ? "todos" : filter), "param-filter") + " " + Markup.Style("mut", definitions.Count + " parámetros"),
            " " + Markup.Style("mut", "PARÁMETRO          TIPO      ÁMBITO     PREDET.  " + (layers is null ? "" : "CAPA")) };
        var chosen = definitions.FirstOrDefault(row => row.Id == selected) ?? definitions.FirstOrDefault();
        var index = chosen is null ? 0 : definitions.ToList().IndexOf(chosen);
        foreach (var item in definitions.Skip(index / 4 * 4).Take(4))
            rows.Add(" " + Markup.Style(item.Id == chosen?.Id ? "sel" : "nav", DashboardPresentation.Fit(item.Name, 18).PadRight(19), "param:" + item.Id) + Markup.Escape(
                DashboardPresentation.Fit(item.Value.Get("type")?.Text ?? "any-of", 9).PadRight(10) +
                DashboardPresentation.Fit(item.Value.Get("scope")?.Text ?? "ambos", 10).PadRight(11) + DashboardPresentation.Fit(item.Value.Get("default")?.Redacted()?.ToString() ?? "—", 8).PadRight(9) + (layers is null ? "" : LayerMark(layers, item.Value.Origin.File))));
        if (definitions.Count > 4) rows.Add(" " + ManagementPresentation.Button("◂", "param-prev") + " " + ManagementPresentation.Button("▸", "param-next"));
        if (chosen is null) rows.Add(" " + Markup.Style("mut", "Sin definiciones efectivas para este filtro."));
        else
        {
            // Detail rows are clipped here so the canvas never cuts them without an ellipsis.
            const int detail = 54;
            var name = DashboardPresentation.Fit(chosen.Name, detail);
            rows.Add(""); rows.Add(" " + Markup.Style("hi", name) + " " + Markup.Style("mut", DashboardPresentation.Fit(chosen.Value.Get("aliases")?.Items is { Count: > 0 } aliases ?
                "alias " + string.Join(", ", aliases.Select(item => item.Redacted()?.ToString())) : "sin alias", Math.Max(0, detail - name.Length - 1))));
            rows.Add(" " + Markup.Escape(DashboardPresentation.Fit(chosen.Value.Get("description")?.Redacted()?.ToString() ?? "Sin descripción.", detail)));
            rows.Add(" " + Markup.Style("mut", "origen ") + Markup.Escape(DashboardPresentation.Fit((layers is null ? "" : LayerMark(layers, chosen.Value.Origin.File) + " ") + Path.GetFileName(chosen.Value.Origin.File), detail - 7)));
            var uses = chosen.Uses.Length == 0 ? "sin asignaciones en la selección" : chosen.Uses.Length + ": " + string.Join(", ", chosen.Uses);
            rows.Add(" " + Markup.Style("mut", "usado por ") + Markup.Escape(DashboardPresentation.Fit(uses, detail - 10)));
        }
        rows.Add(" " + ManagementPresentation.Button("Definir…", "param-define") + " " +
            (chosen is null ? DefinitionsPresentation.Off("Editar") : ManagementPresentation.Button("Editar", "param-edit")) + " " +
            (chosen is null ? DefinitionsPresentation.Off("Quitar definición") : ManagementPresentation.Button("Quitar definición", "param-remove")));
        if (chosen is not null) rows.Add(" " + ManagementPresentation.Button("Detalle…", "param-detail"));
        return new("Parámetros de " + Markup.Escape(scope) + "{pad}" + ManagementPresentation.CombinationButton(combination), rows);
    }

    internal static string ParameterFile(EffectiveRecipe recipe) => recipe.Selection.Files
        .Where((_, index) => recipe.Layers[index].Get("arg-defs") is not null).LastOrDefault() ?? recipe.Selection.Files[^1];

    internal sealed class UpdateView
    {
        public JsonObject? Plan;
        public string? Problem;
        public string? Identity;
        public string? Notice;
        public bool Unprepared;
        public HashSet<string> Chosen { get; } = new(StringComparer.Ordinal);
    }

    internal static string UpdateSelector(JsonNode observation) => observation["kind"]!.GetValue<string>() + "://" +
        (observation["kind"]!.GetValue<string>() == "backend" ? observation["recipe"] + "/" + observation["alias"] : observation["key"]!.GetValue<string>());

    internal static void ReadUpdates(ScopeContext context, RecipeSelection[] selections, UpdateView state)
    {
        (state.Problem, state.Notice, state.Unprepared) = (null, null, false);
        if (selections.Length == 0) { state.Plan = null; state.Problem = "No hay intenciones seleccionadas."; return; }
        var plans = new RecipeUpdatePlans(context);
        // A plan needs the identity created when the combination is first used; Comprobar would only fail.
        if (!plans.Prepared)
        {
            (state.Plan, state.Unprepared) = (null, true);
            state.Notice = "La combinación todavía no se preparó. Usala en Up o Iniciar para poder comprobar novedades.";
            return;
        }
        state.Plan = plans.ReadRecorded(selections);
        if (state.Plan is null) { state.Notice = "Todavía no hay un plan para esta combinación. Comprobar consulta las novedades y guarda el plan."; return; }
        try { _ = plans.ReadValid(selections); }
        // The CLI message names the update command; the view says what happened and what to press.
        catch (Coquena.Storage.StorageException error) when (error.Code == "stale-update-plan")
        { state.Problem = "El plan quedó desactualizado: cambiaron la combinación o los recursos instalados desde que se comprobó. Volvé a pulsar Comprobar."; }
        catch (Exception error) when (ManagementError(error)) { state.Problem = error.Message; }
        var identity = state.Plan["created-at"]!.GetValue<string>() + state.Plan["combination-id"]!.GetValue<string>();
        if (identity == state.Identity) return;
        state.Identity = identity; state.Chosen.Clear();
        foreach (var item in state.Plan["resources"]!.AsArray().Where(item => item?["sensitive"]?.GetValue<bool>() != true && item?["status"]?.GetValue<string>() == "update-available" && item?["upgrade-allowed"]?.GetValue<bool>() != false))
            state.Chosen.Add(UpdateSelector(item!));
    }

    internal static DefinitionsPresentation.Page UpdatesPage(string scope, UpdateView state, IReadOnlyList<string> impact, bool busy = false, string combination = "Combinación")
    {
        var rows = new List<string>();
        if (busy) rows.Add(" " + Markup.Style("warn", DashboardPresentation.Spinner() + " ") + Markup.Style("mut", "Trabajando… Podés cambiar de vista; la operación sigue."));
        if (state.Notice is { } notice) rows.AddRange(DashboardPresentation.Wrap(notice, 54).Take(3).Select(line => " " + Markup.Style("mut", line)));
        if (state.Problem is { } problem) rows.AddRange(DashboardPresentation.Wrap(problem, 54).Take(3).Select(line => " " + Markup.Style("warn", line)));
        if (state.Plan is { } plan)
        {
            var date = DateTimeOffset.TryParse(plan["created-at"]?.ToString(), out var created) ? created.ToLocalTime().ToString("dd/MM HH:mm") : "fecha no disponible";
            rows.Add(" " + Markup.Style("mut", "Comprobado el " + date + (state.Problem is null ? " · ✓ al día" : " · ▲ ya no se puede aplicar")));
            rows.Add(" " + Markup.Style("mut", "   RECURSO               ACTUAL    PROPUESTA  ESTADO"));
            var report = RecipeUpdatePlans.Report(plan)["resources"]!.AsArray();
            if (report.Count == 0) rows.Add(" " + Markup.Style("mut", "Sin recursos en este plan."));
            var raw = plan["resources"]!.AsArray();
            for (var index = 0; index < report.Count; index++)
            {
                var item = report[index]!;
                var selectable = state.Problem is null && raw[index]?["sensitive"]?.GetValue<bool>() != true && item["status"]?.GetValue<string>() == "update-available" && item["upgrade-allowed"]?.GetValue<bool>() != false;
                var selector = selectable ? UpdateSelector(raw[index]!) : "";
                rows.Add(" " + (selectable ? Markup.Style("nav", state.Chosen.Contains(selector) ? "☑" : "☐", "update-toggle:" + Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes(selector))) : Markup.Style("mut", " — ")) + " " +
                    Markup.Escape(DashboardPresentation.Fit(item["kind"] + " " + (item["alias"] ?? item["key"]), 21).PadRight(22) + Version(item["current"]).PadRight(10) + Version(item["proposed"]).PadRight(11)) +
                    Markup.Style(item["upgrade-allowed"]?.GetValue<bool>() == false ? "warn" : "mut", item["upgrade-allowed"]?.GetValue<bool>() == false ? "fijado" : UpdateStatus(item["status"]?.ToString())));
                if (item["diagnostic"] is { } diagnostic)
                    rows.AddRange(DashboardPresentation.Wrap("▲ " + diagnostic, 52).Select(line => "   " + Markup.Style("warn", Markup.Escape(line))));
            }
        }
        rows.Add(" " + (busy ? DefinitionsPresentation.Off(DashboardPresentation.Spinner() + " Comprobar") : state.Unprepared ? DefinitionsPresentation.Off("Comprobar") : ManagementPresentation.Button("Comprobar", "update-check")) + " " + (!busy && state.Plan is not null && state.Problem is null && state.Chosen.Count > 0 ?
            ManagementPresentation.Button("Actualizar " + state.Chosen.Count, "update-apply") : DefinitionsPresentation.Off("Actualizar")) + " " + ManagementPresentation.Button("Descargas", "downloads"));
        rows.Add(""); rows.Add(" " + Markup.Style("hi", "Impacto en ejecución"));
        rows.Add(" " + Markup.Style("mut", "Actualizar no reinicia ni cambia los servicios que están en marcha."));
        rows.AddRange(impact.Select(text => " " + Markup.Escape(text)));
        return new("Actualizaciones de " + Markup.Escape(scope) + "{pad}" + ManagementPresentation.CombinationButton(combination), rows);

        static string Version(JsonNode? value) => DashboardPresentation.Fit(value is JsonObject ? value["release"]?.ToString() ?? value["commit"]?.ToString() ?? "—" : value?.ToString() ?? "—", 9);
    }

    private static string UpdateStatus(string? status) => status switch
    {
        "unchanged" => "sin cambios", "update-available" => "↑ nueva", "not-applicable" => "no aplica",
        "not-prepared" => "sin preparar", "inconclusive" => "▲ inconcluso", "query-failed" => "✗ falló",
        "orphaned" => "▲ sin origen", "ambiguous" => "▲ ambiguo", "skipped" => "omitido", "blocked" => "bloqueado",
        "prepared-for-next-application" => "preparado para la próxima aplicación", _ => status ?? "—"
    };

    private static string UpdateReportText(JsonObject report) => string.Join("\n", report["resources"]!.AsArray().Select(item =>
        item!["kind"] + " " + item["key"] + ": " + UpdateStatus(item["upgrade-status"]?.ToString() ?? item["status"]?.ToString()) +
        (item["diagnostic"] is { } diagnostic ? " · " + diagnostic : "")));

    internal sealed record ImportChoice(string Categories, string[] Replacements, bool Default);

    internal static string[] ExistingImportEntities(ScopeContext global, EffectiveRecipe source)
    {
        var selected = RecipeImportSelection.Create(source);
        var snapshot = new ScopeStore(global).Read();
        var roots = snapshot.Documents.Where(pair => pair.Value["kind"]?.GetValue<string>() == "recipe-intention")
            .Select(pair => new RecipeYaml(global.PathFor(pair.Key)).Read()).ToArray();
        return selected.Entities.Where(entity =>
        {
            var parts = entity.Selector.Split(':', 2); var kind = parts[0]; var key = parts[1];
            if (kind == "service") return new RecipeRegistry(new ScopeStore(global)).Services().Any(service => service.Name == key);
            return roots.Any(root => (kind == "param" ? root.Get("arg-defs")?.Get("definitions") : root.Get(kind == "preset" ? "presets" : kind == "model" ? "models" : "companions"))?.Get(key) is not null);
        }).Select(entity => entity.Selector).ToArray();
    }

    internal static bool HasDefaultGlobalService(ScopeContext global)
    {
        try { _ = new RecipeRegistry(new ScopeStore(global)).ResolveGlobalService("default"); return true; }
        catch (Coquena.Storage.StorageException) { return false; }
    }

    private static ImportChoice? ImportForm(IApplication app, EffectiveRecipe source, ScopeContext global)
    {
        var available = RecipeImportSelection.Create(source);
        var collisions = ExistingImportEntities(global, source);
        using var dialog = Modal(app, 76, 16 + Math.Clamp(collisions.Length, 1, 4), "Importar " + source.Name + " en Global");
        var checks = new Dictionary<string, CheckBox>();
        string[] categories = ["presets", "model", "params", "service"];
        // Spanish captions for the CLI --select values, which stay unchanged.
        var captions = new Dictionary<string, string> { ["presets"] = "Presets", ["model"] = "Modelos", ["params"] = "Parámetros", ["service"] = "Servicio" };
        for (var index = 0; index < categories.Length; index++)
        {
            var category = categories[index];
            var entries = available.Entities.Where(entity => category switch { "presets" => entity.Selector.StartsWith("preset:", StringComparison.Ordinal),
                "model" => entity.Selector.StartsWith("model:", StringComparison.Ordinal) || entity.Selector.StartsWith("companion:", StringComparison.Ordinal),
                "params" => entity.Selector.StartsWith("param:", StringComparison.Ordinal), _ => entity.Selector.StartsWith("service:", StringComparison.Ordinal) }).Select(entity => entity.Selector.Split(':', 2)[1]);
            checks[category] = new CheckBox { X = 1, Y = index, Width = Dim.Fill(1), Text = captions[category] + (entries.Any() ? " · " + string.Join(", ", entries) : ""), Enabled = available.Categories.Contains(category),
                Value = available.Categories.Contains(category) ? CheckState.Checked : CheckState.UnChecked };
            dialog.Add(checks[category]);
        }
        var details = new Button { X = 1, Y = 4, Text = " Detalle de categorías… ", NoDecorations = true, ShadowStyle = ShadowStyles.None };
        dialog.Add(new Label { X = 1, Y = 6, Text = "Ya existen en Global; marcá las que querés reemplazar:" });
        var list = new ListView { X = 1, Y = 7, Width = Dim.Fill(1), Height = Math.Clamp(collisions.Length, 1, 4) };
        var replacements = new HashSet<string>(StringComparer.Ordinal);
        void Refresh() => list.SetSource(new ObservableCollection<string>(collisions.Length == 0 ? ["Ninguna; nada se reemplaza."]
            : collisions.Select(entity => (replacements.Contains(entity) ? "☑ " : "☐ ") + entity)));
        var toggle = new Button { X = 1, Y = Pos.Bottom(list), Enabled = collisions.Length > 0, Text = " Conservar / Reemplazar ", NoDecorations = true, ShadowStyle = ShadowStyles.None };
        toggle.Accepting += (_, args) => { args.Handled = true; if (list.SelectedItem is int row && row >= 0 && row < collisions.Length) { var entity = collisions[row]; if (!replacements.Add(entity)) replacements.Remove(entity); Refresh(); list.SelectedItem = row; } };
        var asDefault = new CheckBox { X = 1, Y = Pos.Bottom(toggle) + 1, Text = "usar como servicio predeterminado", Enabled = available.Categories.Contains("service") };
        // Empty unless a condition already holds; errors of Importar replace it.
        var status = new Label { X = 1, Y = Pos.Bottom(asDefault) + 1, Width = Dim.Fill(1), Height = 2, Text = HasDefaultGlobalService(global) ? ""
            : "▲ Global no tiene servicio predeterminado: para importar presets, importá también el servicio." };
        details.Accepting += (_, args) => { args.Handled = true; Choose(app, "Entidades de la combinación de origen", available.Entities.Select(entity => entity.Selector).ToArray(), null); };
        dialog.Add(list, toggle, asDefault, status, details);
        dialog.AddButton(new Button { Text = " Cancelar ", NoDecorations = true, ShadowStyle = ShadowStyles.None });
        var accept = new Button { Text = " Importar ", NoDecorations = true, ShadowStyle = ShadowStyles.None };
        ImportChoice? result = null;
        accept.Accepting += (_, args) =>
        {
            try
            {
                var selected = string.Join(',', checks.Where(pair => pair.Value.Value == CheckState.Checked).Select(pair => pair.Key));
                if (selected.Length == 0) throw new ArgumentException("Seleccioná al menos una categoría.");
                // Presets are published to a Global service: without one the import would fail after confirming.
                if (checks["presets"].Value == CheckState.Checked && checks["service"].Value != CheckState.Checked && !HasDefaultGlobalService(global))
                    throw new ArgumentException("Los presets requieren un servicio global predeterminado: importá también el servicio o crealo en Global › Servicio.");
                var entities = RecipeImportSelection.Create(source, selected).Entities.Select(entity => entity.Selector).ToHashSet(StringComparer.Ordinal);
                var replace = replacements.Where(entities.Contains).ToArray();
                _ = RecipeImportSelection.Create(source, selected, replace, asDefault: asDefault.Value == CheckState.Checked);
                result = new(selected, replace, asDefault.Value == CheckState.Checked);
            }
            catch (Exception error) when (ManagementError(error)) { args.Handled = true; status.Text = error.Message; }
        };
        dialog.AddButton(accept); Refresh(); if (collisions.Length > 0) list.SelectedItem = 0;
        PaintForm(dialog); app.Run(dialog);
        return dialog.Result == 1 ? result : null;
    }

    internal sealed record CombinationChoice(RecipeSelection Selection, IReadOnlySet<string> NewFiles, bool Private);

    private static CombinationChoice? CombinationForm(IApplication app, RecipeSelection current, bool privateSelection = false)
    {
        var entries = new List<(string Path, bool Env, bool Included)>();
        foreach (var path in current.Files) entries.Add((path, false, true));
        foreach (var path in current.EnvFiles) entries.Add((path, true, true));
        foreach (var path in Directory.EnumerateFiles(current.Base).Where(path =>
                     path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(path).StartsWith(".env", StringComparison.Ordinal)))
            if (!entries.Any(entry => entry.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) entries.Add((path, Path.GetFileName(path).StartsWith(".env", StringComparison.Ordinal), false));
        var newFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var dialog = Modal(app, 76, 19, "Nueva combinación · YAML en orden");
        var list = new ListView { X = 1, Y = 0, Width = Dim.Fill(1), Height = Dim.Fill(7) };
        var status = new Label { X = 1, Y = Pos.AnchorEnd(4), Width = Dim.Fill(1), Height = 1, Text = "Se guarda cuando la uses con Up o Iniciar. Los .env usan preparación privada." };
        var isPrivate = new CheckBox { X = 1, Y = Pos.AnchorEnd(3), Text = "preparación privada (referencias externas)",
            Value = CombinationIdentity.FromSelection(current, privateSelection).Private ? CheckState.Checked : CheckState.UnChecked };
        void Refresh()
        {
            var selected = list.SelectedItem ?? 0; var order = 0;
            list.SetSource(new ObservableCollection<string>(entries.Select(entry =>
                (entry.Included ? "☑ " : "☐ ") + (entry.Env ? ".env " : entry.Included ? (++order).ToString() + " · " : "— · ") +
                Path.GetRelativePath(current.Base, entry.Path) + (newFiles.Contains(entry.Path) ? " · nuevo" : ""))));
            list.SelectedItem = Math.Clamp(selected, 0, Math.Max(0, entries.Count - 1));
        }
        Button Action(string text, int x, Action action)
        {
            var button = new Button { X = x, Y = Pos.AnchorEnd(6), Text = text, NoDecorations = true, ShadowStyle = ShadowStyles.None };
            button.Accepting += (_, args) => { args.Handled = true; try { action(); Refresh(); } catch (Exception error) when (ManagementError(error)) { status.Text = error.Message; } };
            dialog.Add(button); return button;
        }
        void Add(bool create, bool env)
        {
            var fields = ResourceForm(app, create ? "Archivo nuevo" : "Agregar archivo existente", [("ruta", env ? ".env" : "")], "Agregar");
            if (fields is null) return;
            var path = Path.GetFullPath(fields[0], current.Base);
            if (entries.Any(entry => entry.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("El archivo ya está en la lista.");
            if (create && File.Exists(path) || !create && !File.Exists(path)) throw new ArgumentException(create ? "El archivo ya existe." : "No existe el archivo.");
            if (!env && !path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase) && !path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Elegí un archivo YAML.");
            if (!Directory.Exists(Path.GetDirectoryName(path))) throw new ArgumentException("No existe la carpeta destino.");
            entries.Add((path, env, true)); if (create) newFiles.Add(path);
        }
        Action("Marcar", 1, () => { if (list.SelectedItem is int row && row >= 0 && row < entries.Count) entries[row] = entries[row] with { Included = !entries[row].Included }; });
        Action("↑", 11, () => { if (list.SelectedItem is int row && row > 0) { (entries[row - 1], entries[row]) = (entries[row], entries[row - 1]); list.SelectedItem = row - 1; } });
        Action("↓", 16, () => { if (list.SelectedItem is int row && row >= 0 && row + 1 < entries.Count) { (entries[row + 1], entries[row]) = (entries[row], entries[row + 1]); list.SelectedItem = row + 1; } });
        Action("+ YAML…", 21, () => Add(false, false));
        Action("Nuevo…", 34, () => Add(true, false));
        Action("+ .env…", 46, () => Add(false, true));
        dialog.Add(list, status, isPrivate);
        dialog.AddButton(new Button { Text = " Cancelar ", NoDecorations = true, ShadowStyle = ShadowStyles.None });
        CombinationChoice? result = null;
        var accept = new Button { Text = " Aceptar ", NoDecorations = true, ShadowStyle = ShadowStyles.None };
        accept.Accepting += (_, args) =>
        {
            try
            {
                var yaml = entries.Where(entry => !entry.Env && entry.Included).Select(entry => entry.Path).ToArray();
                if (yaml.Length == 0) throw new ArgumentException("Seleccioná al menos un YAML.");
                var selection = new RecipeSelection(current.Base, yaml, current.Base, entries.Where(entry => entry.Env && entry.Included).Select(entry => entry.Path));
                result = new(selection, newFiles.Where(path => yaml.Contains(path, StringComparer.OrdinalIgnoreCase)).ToHashSet(StringComparer.OrdinalIgnoreCase),
                    CombinationIdentity.FromSelection(selection, isPrivate.Value == CheckState.Checked).Private);
            }
            catch (Exception error) when (ManagementError(error)) { args.Handled = true; status.Text = error.Message; }
        };
        dialog.AddButton(accept); Refresh();
        PaintForm(dialog); app.Run(dialog);
        return dialog.Result == 1 ? result : null;
    }

    private static void PaintForm(Dialog dialog)
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR"))) return;
        Paint(dialog, Palette.Text, Palette.Card);
        foreach (var view in dialog.SubViews) Paint(view, view is Label ? Palette.Muted : Palette.Text,
            view is TextField or ListView ? Palette.Base : view is Button ? Palette.Border : Palette.Card);
    }
}
