using Coquena.Backends;
using Coquena.Configuration;
using Coquena.Contracts;
using Coquena.Observation;
using Coquena.Storage;

namespace Coquena.Tests;

// Configuración: hidden bar resources, their persistence and the Sistema rows at 80×24.
internal static class SettingsPresentationTests
{
    public static void Run(Action<bool, string> check)
    {
        LivePresentation.Gauge cpu = new("CPU", [0.4], 0.42, null, null, null), ram = new("RAM", [0.5], 0.57, "18,2G/32G", null, null);
        LivePresentation.Gauge[] gpus = [new("GPU0", [0.9], 0.91, "20,1G/24G", "+0,4G", 0.8), new("GPU1", [0.1], 0.12, "0,1G/0,5G", "+1,1G", 0.2)];
        var (first, second) = LivePresentation.HardwareBar(cpu, ram, gpus, false);
        var (hiddenFirst, hiddenSecond) = LivePresentation.HardwareBar(cpu, ram, gpus, false,
            new HashSet<string> { "cpu", "gpu0.memory", "gpu1.use", "gpu1.memory", "gpu1.shared" });
        var (plainFirst, plainSecond, plainHidden, plainHiddenSecond) =
            (Markup.Plain(first), Markup.Plain(second), Markup.Plain(hiddenFirst), Markup.Plain(hiddenSecond));
        check(plainFirst.Contains("CPU") && plainFirst.Contains("RAM") && plainSecond.Contains("GPU1"), "the full bar shows every resource");
        check(!plainHidden.Contains("CPU") && plainHidden.Contains("RAM 18,2G/32G".Split(' ')[0]),
            "a hidden CPU leaves the bar and RAM stays: " + plainHidden);
        check(plainHiddenSecond.Contains("GPU0") && plainHiddenSecond.Contains("91%") && !plainHiddenSecond.Contains("20,1G") &&
              plainHiddenSecond.Contains("+0,4G") && !plainHiddenSecond.Contains("GPU1"),
            "hidden GPU readings leave the bar and a GPU without readings disappears: " + plainHiddenSecond);

        var keys = SettingsPresentation.BarResources(2);
        check(keys.Length == 8 && keys.All(ContractValidation.BarResource) && !ContractValidation.BarResource("gpu.use") &&
              !ContractValidation.BarResource("gpu0.vram") && !ContractValidation.BarResource("disk"),
            "bar keys are cpu, ram and three readings per GPU");
        check(SettingsPresentation.BarLabel("gpu1.memory") == "GPU1 dedicada" && SettingsPresentation.BarLabel("cpu") == "CPU",
            "bar boxes use the preview labels");

        var context = new HardwareContext("win-x64", "CPU de prueba", 32UL * 1024 * 1024 * 1024, ["Adaptador A"], 13000,
            [new("GPU A", 89)], new("compatible", "Driver CUDA disponible."), new("unknown", "API Vulkan no disponible o incompleta; no se deduce soporte a partir del fabricante."));
        var rows = SettingsPresentation.System("0.3.0", context, false, ["GPU0  GPU A · CUDA"], [], 56);
        var text = rows.Select(Markup.Plain).ToArray();
        check(rows.Count <= 20 && rows.All(row => Markup.Width(row) <= 56), "Sistema fits the right column of 80×24");
        check(text.Contains(" Plataforma  win-x64") && text.Contains(" RAM         32 GiB") && text.Any(row => row.Contains("✓ compatible  driver 13.0")) &&
              text.Any(row => row.Contains("GPU A · SM 8.9")) && text.Any(row => row.Contains("? sin confirmar")),
            "Sistema shows machine, adapters and acceleration of the probe: " + string.Join(" / ", text));
        check(rows.SelectMany(Markup.Parse).Any(span => span.Action == "probe"), "Sondear de nuevo is an action");
        check(SettingsPresentation.System("0.3.0", null, false, [], [], 56).Select(Markup.Plain).Any(row => row.Contains("Consultando")),
            "Sistema without a finished probe says so");
    }

    // The preference round-trips through config.json and invalid keys are refused.
    public static void RunStore(Action<bool, string> check)
    {
        var root = Path.Combine(Path.GetTempPath(), "coquena-settings-" + Guid.NewGuid().ToString("N"));
        try
        {
            var manager = new ConfigurationManager(new Home(root));
            manager.SetHiddenBarResources(["gpu0.memory", "cpu", "cpu"]);
            check(manager.HiddenBarResources().SequenceEqual(["cpu", "gpu0.memory"]), "hidden bar resources are saved once and sorted");
            manager.SetHiddenBarResources([]);
            check(manager.HiddenBarResources().Length == 0 && !File.ReadAllText(Path.Combine(root, "config.json")).Contains("hidden", StringComparison.OrdinalIgnoreCase),
                "showing every resource leaves no preference in config.json");
            var refused = false;
            try { manager.SetHiddenBarResources(["disk"]); }
            catch (Exception error) when (error is ContractException or StorageException) { refused = true; }
            check(refused && manager.HiddenBarResources().Length == 0, "an unknown bar resource is refused");
        }
        finally { try { Directory.Delete(root, true); } catch (IOException) { } }
    }
}
