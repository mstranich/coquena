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
        : scope.Global ? ["Servicios", "Presets", "Modelos", "Backends", "Parámetros", "Actualizaciones", "Mantenimiento"]
        : !scope.HasRecipe ? ["Receta"]
        : ["Recetas", "Presets", "Servicio", "Modelos", "Backends", "Parámetros", "Actualizaciones", "Mantenimiento"];

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
            plain = "  " + view + (badge is null ? "" : " " + badge);
            text = "  " + Style("nav", view) + (badge is null ? "" : " " + Style("foc", badge));
        }
        else
        {
            var glyph = !scope.Accessible ? "✗" : node.Scope == focused ? "▾" : "▸";
            var name = DashboardPresentation.Fit(scope.Name, TreeWidth - 4 - (scope.Here ? 7 : 0));
            plain = " " + glyph + " " + name + (scope.Here ? " · aquí" : "");
            text = " " + Style("mut", glyph + " " + name) + (scope.Here ? " " + Style("mut", "· aquí") : "");
        }
        return selected ? Style("focb", plain.PadRight(TreeWidth)) : text;
    }

    public static Page Recipes(string scope, string? destination, IReadOnlyList<Combination> items, IReadOnlyList<string> files)
    {
        var rows = new List<string> { " " + Style("mut", "  " + Cell("COMBINACIÓN", 28) + " " + Cell("ID", 8) + " ESTADO") };
        rows.AddRange(items.Select(item => "  " + Escape(Cell(item.Files, 28) + " " + Cell(item.Id, 8)) + " " +
            (item.Current ? Style("ok", "● actual") : item.Private ? Style("warn", "privada") : Style("mut", "conocida"))));
        if (items.Count == 0) rows.Add(" " + Style("mut", "Sin combinaciones registradas."));
        rows.Add("");
        rows.Add(" " + Off("+ Nueva combinación…") + "  " + Off("Editar") + "  " + Off("Olvidar"));
        rows.Add("");
        if (files.Count > 0) rows.Add(" " + Style("mut", "archivos") + " " + string.Join(" " + Style("mut", "→") + " ", files.Select(Escape)));
        rows.Add(" " + Style("mut", "[ ] preparación privada"));
        rows.Add("");
        rows.Add(" " + Off("▲ Up") + "  " + Off("▼ Down") + "  " + Off("Importar") + "  " + Off("Ver en vivo"));
        return new("Recetas de " + Escape(scope) + (destination is null ? "" : " " + Style("mut", "→") + " " + Escape(destination)), rows);
    }

    public static Page Presets(string scope, IReadOnlyList<Preset> items)
    {
        var rows = new List<string> { " " + Style("mut", "  " + Cell("PRESET", 20) + Cell("MODELO", 22) + "EN VIVO") };
        rows.AddRange(items.Select(item => "  " + Escape(Cell(item.Name, 20) + Cell(item.Model, 22)) +
            (item.Live is { } live ? Style(live == "loaded" ? "ok" : "mut", (live == "loaded" ? "● " : "○ ") + live) : Style("mut", "○ no publ."))));
        if (items.Count == 0) rows.Add(" " + Style("mut", "Sin presets."));
        rows.Add("");
        rows.Add(" " + Off("Clonar…") + "  " + Off("Editar YAML") + "  " + Off("Eliminar") + "  " + Off("Ver en vivo"));
        return new("Presets de " + Escape(scope), rows);
    }

    public static Page Services(string scope, bool global, IReadOnlyList<Service> items)
    {
        var rows = new List<string> { " " + Style("mut", "  " + Cell("SERVICIO", 24) + "ESTADO") };
        rows.AddRange(items.Select(item => " " + (item.Default ? "★" : " ") + " " + Escape(Cell(item.Name, 24)) +
            (item.State is { } state ? Style(item.StateClass ?? "mut", state) : "")));
        if (items.Count == 0) rows.Add(" " + Style("mut", global ? "Sin servicios registrados." : "La receta no tiene servicio registrado."));
        rows.Add("");
        rows.Add(" " + Off("★ Predeterminado") + "  " + Off("Eliminar") + "  " + Off("Editar YAML") + "  " + Off("Ver en vivo"));
        return new((global ? "Servicios de " : "Servicio de ") + Escape(scope) + "{pad}" + Off("+ Agregar"), rows);
    }

    public static Page Models(string scope, IReadOnlyList<Model> items)
    {
        var rows = new List<string>
        {
            " (•) inventario  ( ) " + Style("mut", "Hugging Face"),
            "",
            " " + Style("mut", "  " + Cell("MODELO", 32) + Cell("ORIGEN", 12) + "TAMAÑO")
        };
        rows.AddRange(items.Select(item =>
        {
            var name = item.Name + (item.Companion ? " ·c" : "");
            return "  " + Escape(Cell(name, 32)) + (item.External ? Style("warn", Cell("externo", 12)) : Escape(Cell("gestionado", 12))) +
                Escape(Size(item.Size)) + (item.Protected ? " " + Style("mut", "■") : "");
        }));
        if (items.Count == 0) rows.Add(" " + Style("mut", "Sin modelos en el inventario."));
        rows.Add("");
        rows.Add(" " + Off("Quitar protección") + "  " + Off("Verificar") + "  " + Off("Eliminar"));
        return new("Modelos de " + Escape(scope) + "{pad}" + Off("+ Agregar local…"), rows);
    }

    public static Page Backends(string scope, IReadOnlyList<Backend> items)
    {
        var rows = new List<string> { " " + Style("mut", "  " + Cell("VARIANTE", 14) + Cell("VERSIÓN", 10) + Cell("ORIGEN", 10) + "TAMAÑO") };
        rows.AddRange(items.Select(item => "  " + Escape(Cell(item.Variant, 14) + Cell(item.Version, 10)) +
            (item.External ? Style("warn", Cell("externo", 10)) : Escape(Cell("oficial", 10))) + Escape(Size(item.Size)) +
            (item.Protected ? " " + Style("mut", "■") : "")));
        if (items.Count == 0) rows.Add(" " + Style("mut", "Sin backends en el ámbito."));
        rows.Add("");
        rows.Add(" " + Off("Quitar protección") + "  " + Off("Eliminar"));
        return new("Backends de " + Escape(scope) + "{pad}" + Off("+ Backend"), rows);
    }

    public static Page Parameters(string scope, IReadOnlyList<Parameter> items, int room)
    {
        var rows = new List<string>
        {
            " " + Style("mut", "filtro") + " " + Style("mut", "[                ]") + "  " + Style("mut", items.Count + " parámetros"),
            "",
            " " + Style("mut", " " + Cell("PARÁMETRO", 22) + Cell("TIPO", 9) + Cell("APLICA", 10) + "PREDET.")
        };
        var shown = Math.Max(0, room - 6);
        rows.AddRange(items.Take(shown).Select(item => "  " + Escape(Cell(item.Name, 22) + Cell(item.Type, 9) + Cell(item.Application, 10) + (item.Default ?? "—"))));
        if (items.Count == 0) rows.Add(" " + Style("mut", "Sin definiciones."));
        else if (items.Count > shown) rows.Add(" " + Style("mut", $"… {items.Count - shown} más"));
        rows.Add("");
        rows.Add(" " + Off("Definir…") + "  " + Off("Editar") + "  " + Off("Quitar definición"));
        return new("Parámetros de " + Escape(scope), rows);
    }

    // With operation ids the controls are enabled and carry "pause:", "resume:" or "cancel:" plus the id.
    public static Page Updates(string scope, DateTimeOffset? planned, IReadOnlyList<Update> items)
    {
        var rows = new List<string>();
        if (planned is null) rows.Add(" " + Style("mut", "Sin plan de actualización registrado."));
        else
        {
            rows.Add(" " + Style("mut", "    " + Cell("RECURSO", 22) + Cell("ACTUAL", 12) + Cell("PROPUESTA", 11) + "ESTADO"));
            rows.AddRange(items.Select(item =>
            {
                var state = item.Diagnostic is not null ? Style("warn", "▲ " + item.Diagnostic)
                    : item.Proposed != "—" && item.Proposed != item.Current ? Style("ok", "↑ nueva") : Style("mut", "—");
                return " " + Style("mut", "[ ]") + " " + Escape(Cell(item.Resource, 22) + Cell(item.Current, 12) + Cell(item.Proposed, 11)) + state;
            }));
        }
        rows.Add("");
        rows.Add(" " + Off("Comprobar") + "  " + Off("Actualizar"));
        var title = "Actualizaciones de " + Escape(scope) + (planned is { } at
            ? "  " + Style("mut", "plan " + at.ToLocalTime().ToString("dd/MM HH:mm", CultureInfo.InvariantCulture)) : "");
        return new(title, rows);
    }

    public static Page Maintenance(string scope) =>
        new("Mantenimiento de " + Escape(scope), ["", " " + Off("Verificar") + "  " + Off("Limpiar")]);

    public static Page NoRecipe(string folder) =>
        new(Escape(folder) + "  " + Style("mut", "sin coquena.yaml"),
            ["", " " + Style("btnf", " Inicializar ", "init"), "", " " + Style("mut", "Crea el archivo de configuración de esta carpeta (coquena.yaml) con el nombre " + folder + ".")]);

    public static Page Inaccessible(string name) =>
        new(Escape(name), ["", " " + Style("err", "✗") + " " + Style("mut", "La ruta del proyecto ya no se puede resolver.")]);

    public static Page Problem(string title, string message) =>
        new(Escape(title), ["", " " + Style("err", "✗") + " " + Escape(message)]);

    private static string Cell(string text, int width) => DashboardPresentation.Fit(text, width - 1).PadRight(width);

    private static string Size(long? bytes) => bytes is { } value ? DashboardPresentation.Compact(value) : "—";
}
