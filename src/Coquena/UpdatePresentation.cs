using System.Text.Json.Nodes;

namespace Coquena;

// One resource line of a readable report: its code and versions, independent of wording.
public sealed record ReportLine(string Kind, string Key, string Code, string? From, string? To);

// One summary of a readable report: the overall code, the count and the resource lines.
public sealed record ReportSummary(string Code, int Count, IReadOnlyList<ReportLine> Lines);

// Readable summaries of update and upgrade reports. --json keeps the exact document.
// Summary() decides what to say with codes; Write*() turns those codes into text,
// so tests and a future translation depend on the codes, not on the wording.
public static class UpdatePresentation
{
    public static ReportSummary UpdateSummary(JsonObject report)
    {
        var lines = Resources(report).Select(r => Line(r, Status(r))).ToArray();
        var available = lines.Count(l => l.Code == "update-available");
        return new(lines.Length == 0 ? "no-resources" : available == 0 ? "all-current" : "updates-available", available, lines);
    }

    public static ReportSummary UpgradeSummary(JsonObject report)
    {
        var lines = Resources(report).Select(r => Line(r, r["upgrade-status"]?.GetValue<string>() switch
        {
            "blocked" when r["upgrade-allowed"]?.GetValue<bool>() == false => "fixed-version",
            "blocked" => Status(r),
            var status => status ?? ""
        })).ToArray();
        var applied = report["applied"]?.GetValue<int>() ?? 0;
        var blocked = lines.Any(l => l.Code is not ("skipped" or "ready" or "prepared-for-next-application"));
        return new(applied > 0 ? "applied" : blocked ? "none-applied" : "nothing-to-do", applied, lines);
    }

    public static void WriteUpdate(JsonObject report, TextWriter output)
    {
        var summary = UpdateSummary(report);
        output.WriteLine(summary.Code switch
        {
            "no-resources" => Messages.UpdateNoResources,
            "all-current" => Messages.UpdateAllCurrent,
            _ => summary.Count == 1 ? Messages.UpdateOneAvailable : Messages.UpdateManyAvailable.Format(summary.Count)
        });
        foreach (var line in summary.Lines) output.WriteLine($"  {Glyph(line.Code)} {Name(line)}: {Text(line)}");
        if (summary.Code == "updates-available") output.WriteLine(Messages.UpdateHowToApply);
    }

    public static void WriteUpgrade(JsonObject report, TextWriter output)
    {
        var summary = UpgradeSummary(report);
        output.WriteLine(summary.Code switch
        {
            "nothing-to-do" => Messages.UpgradeNothing,
            "none-applied" => Messages.UpgradeNoneApplied,
            _ => summary.Count == 1 ? Messages.UpgradeOneApplied : Messages.UpgradeManyApplied.Format(summary.Count)
        });
        foreach (var line in summary.Lines) output.WriteLine($"  {Glyph(line.Code)} {Name(line)}: {Text(line)}");
        if (summary.Code == "applied") output.WriteLine(Messages.UpgradeRestartNotice);
    }

    private static JsonObject[] Resources(JsonObject report) =>
        report["resources"]?.AsArray().Select(r => r!.AsObject()).ToArray() ?? [];

    private static string Status(JsonObject resource) => resource["status"]?.GetValue<string>() ?? "";

    // Backend: release numbers. Models: short commit of the repository revision.
    private static ReportLine Line(JsonObject resource, string code)
    {
        var kind = resource["kind"]?.GetValue<string>() ?? "model";
        var key = resource["key"]?.GetValue<string>() ?? "";
        if (resource["current"] is not JsonObject current || resource["proposed"] is not JsonObject proposed) return new(kind, key, code, null, null);
        if (kind == "backend") return new(kind, key, code, current["release"]?.ToString(), proposed["release"]?.ToString());
        static string? Short(JsonNode? commit) => commit?.GetValue<string>() is { Length: > 7 } text ? text[..7] : commit?.ToString();
        return new(kind, key, code, Short(current["commit"]), Short(proposed["commit"]));
    }

    private static string Name(ReportLine line) =>
        line.Kind switch { "backend" => "Backend", "companion" => Messages.UpdateCompanion.Format(line.Key), _ => Messages.UpdateModel.Format(line.Key) };

    private static string Glyph(string code) => code switch
    {
        "update-available" => "↑",
        "unchanged" or "not-applicable" or "ready" or "prepared-for-next-application" => "✔",
        "skipped" => "·",
        "fixed-version" => "✖",
        _ => "⚠"
    };

    private static string Change(ReportLine line) => line.From is null || line.To is null ? Messages.UpdateNewVersion :
        line.Kind == "backend" ? $"{line.From} → {line.To}" : Messages.UpdateRevision.Format(line.From, line.To);

    private static string Text(ReportLine line) => line.Code switch
    {
        "update-available" or "ready" => Change(line),
        "prepared-for-next-application" => Messages.UpdateNextStart.Format(Change(line)),
        "skipped" => Messages.UpdateSkipped,
        "fixed-version" => Messages.UpdateFixedVersion,
        "unchanged" => Messages.UpdateUnchanged,
        "not-applicable" => Messages.UpdateNotApplicable,
        "not-prepared" => Messages.UpdateNotPrepared,
        "inconclusive" => Messages.UpdateInconclusive,
        "query-failed" => Messages.UpdateQueryFailed,
        "intervention-required" => Messages.UpdateIntervention,
        "orphaned" => Messages.UpdateOrphaned,
        var other => other
    };
}
