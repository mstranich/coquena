using System.Globalization;
using Coquena.Backends;

namespace Coquena.Observation;

// Rows of Configuración (docs/design/tui E, F and G) that do not need input widgets.
internal static class SettingsPresentation
{
    public static readonly string[] Categories = ["Coquena", "TUI", "Sistema"];

    // Keys of the bottom bar that Configuración › TUI can hide, by GPU number of the bar.
    public static string[] BarResources(int gpus) =>
        ["cpu", "ram", .. Enumerable.Range(0, gpus).SelectMany(n => new[] { $"gpu{n}.use", $"gpu{n}.memory", $"gpu{n}.shared" })];

    public static string BarLabel(string key) => key switch
    {
        "cpu" => "CPU",
        "ram" => "RAM",
        _ => key[..key.IndexOf('.')].ToUpperInvariant() + " " + key[(key.IndexOf('.') + 1)..] switch
        {
            "use" => "uso",
            "memory" => "dedicada",
            _ => "compartida"
        }
    };

    // G · Sistema: version, machine, adapters with telemetry and acceleration of the last probe.
    public static List<string> System(string version, HardwareContext? context, bool failed,
        IReadOnlyList<string> adapters, IReadOnlyList<string> diagnostics, int width)
    {
        const int Label = 13;
        var rows = new List<string>
        {
            Field("Coquena", version),
            Field("Plataforma", context?.Platform ?? "—"),
            Field("CPU", context?.Cpu ?? "—"),
            Field("RAM", context?.MemoryBytes is { } bytes ? Memory(bytes) : "—"),
            "",
            " " + Markup.Style("mut", "ADAPTADORES")
        };
        if (adapters.Count > 0) rows.AddRange(adapters.Select(name => "  " + Markup.Escape(name)));
        else if (context is { DisplayAdapters.Length: > 0 }) rows.AddRange(context.DisplayAdapters.Select(name => "  " + Markup.Escape(name)));
        else rows.Add("  —");
        rows.AddRange(diagnostics.Select(text => "  " + Markup.Style("mut", Markup.Escape(text))));
        rows.Add("");
        rows.Add(" " + Markup.Style("mut", "ACELERACIÓN"));
        if (context is null) rows.Add("  " + Markup.Style("mut", failed ? "Sondeo no disponible." : "Consultando CUDA y Vulkan…"));
        else
        {
            var driver = context.CudaDriverVersion is { } version1
                ? "driver " + (version1 / 1000).ToString(CultureInfo.InvariantCulture) + "." + (version1 % 1000 / 10).ToString(CultureInfo.InvariantCulture) : null;
            Acceleration("CUDA", context.Cuda, driver,
                context.CudaDevices.Select(device => device.Name + " · SM " + device.ComputeCapability / 10 + "." + device.ComputeCapability % 10));
            Acceleration("Vulkan", context.Vulkan, null, []);
        }
        rows.Add("");
        rows.Add(" " + Markup.Style("btn", " Sondear de nuevo ", "probe"));
        return rows;

        string Field(string name, string value) => " " + Markup.Style("mut", name.PadRight(Label - 1)) + Markup.Escape(value);

        void Acceleration(string name, AccelerationEvidence evidence, string? detail, IEnumerable<string> devices)
        {
            var (glyph, tone, text) = evidence.State switch
            {
                "compatible" => ("✓", "ok", "compatible"),
                "incompatible" => ("✗", "err", "incompatible"),
                _ => ("?", "warn", "sin confirmar")
            };
            rows.Add("  " + name.PadRight(8) + Markup.Style(tone, glyph + " " + text) + (detail is null ? "" : "  " + Markup.Escape(detail)));
            foreach (var device in devices) rows.Add("          " + Markup.Escape(device));
            foreach (var line in DashboardPresentation.Wrap(evidence.Diagnostic, Math.Max(10, width - 11)))
                rows.Add("          " + Markup.Style("mut", Markup.Escape(line)));
        }
    }

    public static string Memory(ulong bytes) =>
        (bytes / 1024d / 1024 / 1024).ToString("0", CultureInfo.InvariantCulture) + " GiB";
}
