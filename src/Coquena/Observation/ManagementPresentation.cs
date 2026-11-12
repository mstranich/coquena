using static Coquena.Observation.Markup;

namespace Coquena.Observation;

internal static class ManagementPresentation
{
    internal sealed record Service(string? Id, string Name, bool Default, string Backend, string Port,
        string? State, string? Tone, string File, string Arguments, string Consumers, string? Problem)
    {
        public string Key => Id ?? "recipe"; // A local definition is not a registered service identity.
    }

    public static string Button(string text, string action) => Style("btn", " " + text + " ", action);

    // The combination in use, as a dropdown control at the right of the view title.
    public static string CombinationButton(string label) =>
        Button(DashboardPresentation.Fit(label, 30) + " ▾", "author-selection");

    public static DefinitionsPresentation.Page Services(string scope, bool global, IReadOnlyList<Service> services, string? selected, int width = 56, string? combination = null)
    {
        // Columns of the preview: name 15, backend 17, port 8, then the state.
        var rows = new List<string> { " " + Style("mut", "  " + Cell("SERVICIO", 13) + Cell("BACKEND", 17) + Cell("PUERTO", 8) + "ESTADO") };
        foreach (var service in services)
        {
            var line = (service.Default ? "★ " : "  ") + Cell(service.Name, 13) + Cell(service.Backend, 17) + Cell(service.Port, 8);
            var state = service.State ?? "";
            rows.Add(service.Key == selected
                ? Style("focb", " " + DashboardPresentation.Fit(line + state, Math.Max(1, width - 1)).PadRight(Math.Max(1, width - 1)), "service:" + service.Key)
                : " " + Style("nav", line, "service:" + service.Key) + Style(service.Tone ?? "mut", state));
        }
        if (services.Count == 0) rows.Add(" " + Style("mut", "Sin servicios registrados."));
        if (services.FirstOrDefault(service => service.Key == selected) is { } item)
        {
            rows.Add("");
            rows.Add(" " + Style("hi", item.Name) + (item.Default ? " " + Style("mut", "· predeterminado") : ""));
            AddDetail("archivo", item.File);
            AddDetail("args", item.Arguments);
            AddDetail("consumidores", item.Consumers);
            if (item.Problem is { } problem) rows.AddRange(DashboardPresentation.Wrap(problem, Math.Max(10, width - 2)).Select(line => " " + Style("err", line)));
            rows.Add("");
            rows.Add(" " + (global ? Button("★ Predeterminado", "default") : DefinitionsPresentation.Off("★ Predeterminado")) +
                "  " + (item.Id is null ? DefinitionsPresentation.Off("Eliminar") : Button("Eliminar", "remove")));
            rows.Add(" " + DefinitionsPresentation.Off("Editar YAML") + " " + Button("Ver en vivo", "live"));
        }
        return new((global ? "Servicios de " : "Servicio de ") + Escape(scope) +
            (global ? "{pad}" + Button("+ Agregar", "create") : combination is null ? "" : "{pad}" + CombinationButton(combination)), rows);

        static string Cell(string text, int size) => DashboardPresentation.Fit(text, size - 1).PadRight(size);

        void AddDetail(string caption, string text)
        {
            const int column = 16; // "   " + caption padded to 13, as in the preview.
            var room = Math.Max(10, width - column);
            var wrapped = DashboardPresentation.Wrap(text, room).SelectMany(line => line.Length == 0 ? new[] { "" } :
                Enumerable.Range(0, (line.Length + room - 1) / room).Select(part => line.Substring(part * room, Math.Min(room, line.Length - part * room)))).ToArray();
            rows.Add("   " + Style("mut", Cell(caption, column - 3)) + Escape(wrapped.FirstOrDefault() ?? ""));
            rows.AddRange(wrapped.Skip(1).Select(line => new string(' ', column) + Escape(line)));
        }
    }

}
