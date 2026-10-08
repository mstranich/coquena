using System.Globalization;
using System.Text.RegularExpressions;

namespace Coquena.Backends;

public sealed class BackendException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed record CudaDevice(string Name, int ComputeCapability);
public sealed record AccelerationEvidence(string State, string Diagnostic);
public sealed record HardwareContext(string Platform, string Cpu, ulong? MemoryBytes,
    string[] DisplayAdapters, int? CudaDriverVersion, CudaDevice[] CudaDevices,
    AccelerationEvidence Cuda, AccelerationEvidence Vulkan);
public sealed record ReleaseAsset(string Name, long Size, string Url);
public sealed record OfficialRelease(string Tag, ReleaseAsset[] Assets);
public sealed record BackendCandidate(string Release, string Variant, string Platform,
    string Compatibility, string Diagnostic, ReleaseAsset[] Artifacts, bool Complete, int ToolkitVersion = 0);
public sealed record BackendResolution(BackendCandidate? Selected, BackendCandidate[] Alternatives);

// Pure selection: metadata and evidence are injectable; no network or filesystem here.
public static partial class BackendCatalog
{
    [GeneratedRegex(@"^b([1-9][0-9]*)$", RegexOptions.CultureInvariant)]
    private static partial Regex BuildPattern();
    [GeneratedRegex(@"^llama-(b[1-9][0-9]*)-bin-win-(cpu|vulkan|cuda-([1-9][0-9]*)\.([0-9]+))-(x64|arm64)\.zip$", RegexOptions.CultureInvariant)]
    private static partial Regex ArtifactPattern();

    public static long BuildNumber(string release)
    {
        var match = BuildPattern().Match(release);
        return match.Success && long.TryParse(match.Groups[1].Value, NumberStyles.None,
            CultureInfo.InvariantCulture, out var number) ? number :
            throw new BackendException("invalid-release", "Use latest o un release oficial bNNNN.");
    }

    public static bool ValidFamily(string? family) => family is null or "cpu" or "vulkan" ||
        (family.StartsWith("cuda@", StringComparison.Ordinal) &&
         int.TryParse(family.AsSpan(5), NumberStyles.None, CultureInfo.InvariantCulture, out var major) &&
         major > 0 && family == $"cuda@{major}");

    public static BackendResolution Resolve(IEnumerable<OfficialRelease> releases,
        HardwareContext hardware, string? family = null, string release = "latest")
    {
        if (!ValidFamily(family)) throw new BackendException("invalid-family", "Familias: cpu, vulkan, cuda@<mayor>.");
        if (release != "latest") _ = BuildNumber(release);
        var candidates = releases.Where(r => release == "latest" || r.Tag == release)
            .SelectMany(r => Candidates(r, hardware)).ToArray();
        var eligible = candidates.Where(c => c.Platform == hardware.Platform && c.Complete &&
            (family is null ? c.Compatibility == "compatible" : c.Variant == family));
        var selected = eligible.OrderByDescending(c => BuildNumber(c.Release))
            .ThenByDescending(c => Preference(c, hardware))
            .ThenByDescending(c => c.ToolkitVersion)
            .ThenBy(c => c.Artifacts[0].Name, StringComparer.Ordinal).FirstOrDefault();
        return new(selected, candidates.OrderByDescending(c => BuildNumber(c.Release))
            .ThenBy(c => c.Variant, StringComparer.Ordinal).ThenBy(c => c.Platform, StringComparer.Ordinal).ToArray());
    }

    private static int Preference(BackendCandidate c, HardwareContext hardware) =>
        c.Compatibility == "compatible" && c.Variant.StartsWith("cuda@", StringComparison.Ordinal) && hardware.CudaDevices.Length > 0
            ? 100 + int.Parse(c.Variant.AsSpan(5), CultureInfo.InvariantCulture)
            : c.Variant == "vulkan" && c.Compatibility == "compatible" ? 1 : 0;

    private static IEnumerable<BackendCandidate> Candidates(OfficialRelease release, HardwareContext hardware)
    {
        _ = BuildNumber(release.Tag);
        if (release.Assets.Select(a => a.Name).Distinct(StringComparer.Ordinal).Count() != release.Assets.Length)
            throw new BackendException("invalid-metadata", "Nombres de artefactos duplicados.");
        foreach (var asset in release.Assets)
        {
            var m = ArtifactPattern().Match(asset.Name);
            if (!m.Success || m.Groups[1].Value != release.Tag) continue;
            var platform = $"win-{m.Groups[5].Value}";
            var cuda = m.Groups[3].Success;
            int major = 0, minor = 0;
            if (cuda && (!int.TryParse(m.Groups[3].Value, out major) || major > 99 ||
                !int.TryParse(m.Groups[4].Value, out minor) || minor > 99))
                throw new BackendException("invalid-metadata", "Versión de toolkit inválida en artefacto CUDA.");
            var variant = cuda ? $"cuda@{m.Groups[3].Value}" : m.Groups[2].Value;
            ReleaseAsset[] artifacts = [asset];
            var complete = true;
            if (cuda)
            {
                var suffix = $"bin-win-cuda-{m.Groups[3].Value}.{m.Groups[4].Value}-{m.Groups[5].Value}.zip";
                var dependencies = release.Assets.Where(a => a.Name == $"cudart-llama-{suffix}" ||
                    a.Name == $"cudart-llama-{release.Tag}-{suffix}").ToArray();
                complete = dependencies.Length == 1;
                if (complete) artifacts = [asset, dependencies[0]];
            }
            var evidence = platform != hardware.Platform ? new AccelerationEvidence("incompatible", "SO o arquitectura distintos de la plataforma nativa.")
                : !cuda ? variant == "cpu" ? new("compatible", "CPU de arquitectura nativa; fallback portable oficial.") : hardware.Vulkan
                : CudaCompatibility(hardware, major, minor);
            yield return new(release.Tag, variant, platform, evidence.State,
                complete ? evidence.Diagnostic : evidence.Diagnostic + " Falta una dependencia cudart inequívoca del mismo release/toolkit/plataforma.", artifacts, complete, major * 1000 + minor * 10);
        }
    }

    private static AccelerationEvidence CudaCompatibility(HardwareContext hardware, int major, int minor)
    {
        if (hardware.Cuda.State != "compatible") return hardware.Cuda;
        if (major is not (12 or 13)) return new("unknown", "Sin regla verificada para esta versión mayor de CUDA.");
        if (hardware.CudaDriverVersion is not int driver || hardware.CudaDevices.Length == 0)
            return new("unknown", "Falta evidencia del driver o de dispositivos CUDA.");
        var minimumSm = major == 13 ? 75 : 50;
        var devices = hardware.CudaDevices.Where(d => d.ComputeCapability >= minimumSm).ToArray();
        if (devices.Length == 0) return new("incompatible", $"Ningún dispositivo detectado alcanza SM {minimumSm} requerido por CUDA {major}.");
        if (driver / 1000 < major) return new("incompatible", $"Driver admite CUDA {driver / 1000}.{driver % 1000 / 10}, menor que CUDA {major}.");
        if (driver < major * 1000 + minor * 10)
            return new("unknown", "El driver es de la misma versión mayor, pero no demuestra soporte del toolkit completo; compatibilidad menor no presumida.");
        if (devices.All(d => d.ComputeCapability >= 100) && major == 12 && minor < 8)
            return new("unknown", "Toolkit anterior al soporte nativo Blackwell (12.8); no se presume cobertura mediante PTX del binario concreto.");
        if (devices.All(d => d.ComputeCapability > 121))
            return new("unknown", "Dispositivos posteriores a las arquitecturas verificadas; no se presume cobertura del binario.");
        return new("compatible", $"Driver CUDA y dispositivo SM >= {minimumSm} detectados; comprobación básica, no garantía de inferencia ni del binario concreto.");
    }
}
