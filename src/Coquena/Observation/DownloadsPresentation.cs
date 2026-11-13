using static Coquena.Observation.Markup;

namespace Coquena.Observation;

// N · Descargas: Global and the current project, by scope and category, ordered by state.
internal static class DownloadsPresentation
{
    internal sealed record Item(Guid Id, string Name, string Kind, string State, string Phase, long Done, long? Total,
        string? Diagnostic, DateTimeOffset Started, double? Speed);

    internal sealed record Group(string Scope, bool Here, IReadOnlyList<Item> Items);

    private const int NameWidth = 28, BarWidth = 15, ButtonColumn = 42;
    private static readonly string[] Categories = ["backend", "model", "companion"];

    // Same visibility as `download list`: only operations in progress or stopped.
    public static bool Visible(string state) => state is not ("completed" or "cancelled");

    // Downloading first, then paused or interrupted, failed at the end.
    public static int Rank(string state) => state == "failed" ? 2 : state is "paused" or "interrupted" ? 1 : 0;

    public static string Title(IReadOnlyList<Group> groups)
    {
        var items = groups.SelectMany(group => group.Items).Where(item => Visible(item.State)).ToArray();
        // Only the counts that are not zero.
        string? Count(int rank, string one, string many)
        {
            var count = items.Count(item => Rank(item.State) == rank);
            return count == 0 ? null : count + " " + (count == 1 ? one : many);
        }
        var parts = new[] { Count(0, "activa", "activas"), Count(1, "pausada", "pausadas"), Count(2, "con error", "con error") }.OfType<string>().ToArray();
        return "Descargas" + (parts.Length == 0 ? "" : "  " + Style("mut", string.Join(" · ", parts)));
    }

    // Actions are "verb:group:operation" so the caller resolves the scope of each row.
    public static IReadOnlyList<string> Rows(IReadOnlyList<Group> groups, IReadOnlyList<string> problems)
    {
        var rows = new List<string>();
        for (var g = 0; g < groups.Count; g++)
        {
            var group = groups[g];
            var visible = group.Items.Where(item => Visible(item.State)).ToArray();
            if (visible.Length == 0) continue;
            rows.Add(" " + Style("hi", group.Scope) + (group.Here ? " " + Style("mut", "(esta carpeta)") : ""));
            foreach (var kind in Categories)
            {
                var items = visible.Where(item => item.Kind == kind)
                    .OrderBy(item => Rank(item.State)).ThenByDescending(item => item.Started).ToArray();
                if (items.Length == 0) continue;
                rows.Add("  " + Style("mut", Category(kind)));
                foreach (var item in items) rows.AddRange(ItemRows(item, g));
            }
        }
        rows.AddRange(problems.Select(problem => " " + Style("warn", "▲ " + problem)));
        if (rows.Count == 0) rows.Add(" " + Style("mut", Messages.NoDownloads));
        return rows;
    }

    public static string Category(string kind) => kind switch
    {
        "backend" => "BACKENDS",
        "model" => "MODELOS",
        _ => "AUXILIARES"
    };

    private static IEnumerable<string> ItemRows(Item item, int group)
    {
        string Button(string text, string verb) => Style("btn", " " + text + " ", verb + ":" + group + ":" + item.Id.ToString("D"));
        var name = "   " + Escape(DashboardPresentation.Fit(item.Name, NameWidth - 1).PadRight(NameWidth)) + " ";
        var rank = Rank(item.State);
        if (rank == 2)
        {
            yield return name + Style("err", "✗ " + Failure(item.Diagnostic));
            yield return new string(' ', ButtonColumn) + Button("Reintentar", "resume") + " " + Button("Cancelar", "cancel");
            yield break;
        }
        var ratio = item.Total is > 0 ? Math.Clamp(item.Done / (double)item.Total.Value, 0, 1) : (double?)null;
        var filled = ratio is { } r ? (int)Math.Floor(r * BarWidth) : 0;
        var bar = new string('█', filled) + new string('░', BarWidth - filled);
        var percent = ratio is { } shown ? (" " + LivePresentation.Number(shown * 100, "0") + "%").PadLeft(5) : "     ";
        var state = rank == 1
            ? Style("warn", item.State == "paused" ? "❚❚ pausada" : "▲ detenida")
            : Escape(Progress(item));
        yield return name + Style(rank == 1 ? "mut" : "foc", bar) + percent + "  " + state;
        yield return new string(' ', ButtonColumn) +
            (rank == 1 ? Button("Reanudar", "resume") : Button("❚❚ Pausar", "pause")) + " " + Button("Cancelar", "cancel");
    }

    // Speed and remaining time while transferring; otherwise the current phase.
    public static string Progress(Item item)
    {
        if (item.State == "verifying") return "verificando integridad";
        switch (item.Phase)
        {
            case "publishing": return "instalando";
            case "resource-validation": return "comprobando";
            case "extraction": return "extrayendo";
            case "version-probe": return "comprobando versión";
            case "cleanup": return "terminando";
            case "cancelling-publication": return "cancelando";
        }
        if (item.State == "pending") return "en espera";
        if (item.Speed is not { } speed || speed <= 0) return "descargando";
        var text = DashboardPresentation.Compact(speed) + "/s";
        if (item.Total is { } total && total > item.Done) text += " · " + Remaining(TimeSpan.FromSeconds((total - item.Done) / speed));
        return text;
    }

    // Engine diagnostics are codes; a person reads what happened instead.
    public static string Failure(string? diagnostic) => diagnostic switch
    {
        null or "" => Messages.TransferFailed,
        "connection-interrupted" or "response-stream-failed" or "headers-timeout" or "read-timeout" => Messages.ConnectionCut,
        "http-failed" => "el servidor rechazó la descarga",
        "not-found" => "el archivo ya no está disponible",
        "size-mismatch" or "remote-hash-mismatch" or "partial-integrity-failed" or "prepared-integrity-failed" or "checkpoint-integrity-failed"
            => "el archivo descargado no coincide con el original",
        "transfer-io-failed" => "no se pudo escribir en el disco",
        "executor-interrupted" => "la descarga se detuvo inesperadamente",
        "transfer-cancelled" => "la descarga fue cancelada",
        _ when diagnostic.EndsWith("-transfer-interrupted", StringComparison.Ordinal) => Messages.TransferCut,
        _ when diagnostic.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-') => Messages.TransferFailed,
        _ => diagnostic
    };

    public static string Remaining(TimeSpan time) =>
        time.TotalHours >= 1 ? (int)time.TotalHours + " h " + time.Minutes + " min"
        : time.TotalMinutes >= 1 ? (int)time.TotalMinutes + " min " + time.Seconds + " s"
        : Math.Max(1, (int)time.TotalSeconds) + " s";

    // ⇣ in the tab bar: operations in progress and their overall progress.
    public static string Indicator(IReadOnlyList<Item> items)
    {
        var active = items.Where(item => Visible(item.State) && Rank(item.State) == 0).ToArray();
        var text = "⇣ " + active.Length;
        var known = active.Where(item => item.Total is > 0).ToArray();
        if (known.Length > 0)
            text += " · " + LivePresentation.Number(100d * known.Sum(item => item.Done) / known.Sum(item => item.Total!.Value), "0") + "%";
        var speed = active.Sum(item => item.Speed ?? 0);
        if (speed > 0) text += " · " + DashboardPresentation.Compact(speed) + "/s";
        return text;
    }
}
