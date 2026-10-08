using Coquena.Backends;

namespace Coquena;

public static class BackendCli
{
    public static int Run(string[] args, TextWriter output, TextWriter error,
        Func<HardwareContext>? inspect = null, OfficialBackendSource? source = null,
        CancellationToken cancellation = default)
    {
        var system = args.Length == 1 && args[0] == "system";
        if (!system && !TryRemote(args, out _, out _))
        {
            error.WriteLine("Error: argumentos inválidos. Uso: coquena system | backend remote [cpu|vulkan|cuda@<mayor>] [--release bNNNN|latest]. Consulte coquena --help.");
            return 2;
        }
        HttpClient? client = null;
        try
        {
            var hardware = (inspect ?? HardwareProbe.Inspect)();
            if (system)
            {
                output.WriteLine($"Plataforma nativa: {hardware.Platform}");
                output.WriteLine($"CPU: {Safe(hardware.Cpu)}");
                output.WriteLine(hardware.MemoryBytes is ulong ram ? $"RAM física: {ram / (1024 * 1024)} MiB" : "RAM física: desconocida");
                foreach (var adapter in hardware.DisplayAdapters) output.WriteLine($"Adaptador: {Safe(adapter)} (el nombre no prueba aceleración)");
                output.WriteLine($"CUDA: {hardware.Cuda.State} — {Safe(hardware.Cuda.Diagnostic)}");
                if (hardware.CudaDriverVersion is int version) output.WriteLine($"Driver admite CUDA: {version / 1000}.{version % 1000 / 10}");
                foreach (var device in hardware.CudaDevices) output.WriteLine($"Dispositivo CUDA: {Safe(device.Name)}, SM {device.ComputeCapability / 10}.{device.ComputeCapability % 10}");
                output.WriteLine($"Vulkan: {hardware.Vulkan.State} — {Safe(hardware.Vulkan.Diagnostic)}");
                output.WriteLine("Sondeo local; no valida un binario llama-server ni garantiza inferencia.");
                return 0;
            }
            if (hardware.Platform != "win-x64")
                throw new BackendException("unsupported-platform", "El recorrido M1 está limitado a Windows x64 nativo.");
            TryRemote(args, out var family, out var release);
            if (source is null)
            {
                client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
                source = new(client);
            }
            using var stopped = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; stopped.Cancel(); };
            Console.CancelKeyPress += handler;
            BackendResolution resolution;
            try { resolution = source.ResolveAsync(hardware, family, release!, stopped.Token).GetAwaiter().GetResult(); }
            finally { Console.CancelKeyPress -= handler; }
            output.WriteLine($"Origen oficial ggml-org/llama.cpp; filtro: {family ?? "automático"}, {release}.");
            if (resolution.Selected is { } selected)
            {
                output.WriteLine($"{(family is null ? "Elección automática" : "Selección explícita")}: ggml/{selected.Variant}:{selected.Release} ({selected.Platform}) [{selected.Compatibility}]");
                foreach (var artifact in selected.Artifacts) output.WriteLine($"Artefacto: {artifact.Name} ({artifact.Size} bytes)");
            }
            output.WriteLine("Alternativas consultadas (otros SO/arquitecturas se rechazan siempre):");
            foreach (var candidate in resolution.Alternatives)
                output.WriteLine($"  ggml/{candidate.Variant}:{candidate.Release} {candidate.Platform} [{candidate.Compatibility}] — {Safe(candidate.Diagnostic)}");
            output.WriteLine("Sólo consulta; no descarga, instala ni modifica la selección predeterminada.");
            if (resolution.Selected is not null) return 0;
            throw new BackendException("no-candidate", "El release consultado no ofrece una selección válida dentro del filtro solicitado.");
        }
        catch (Exception e) when (e is BackendException or OperationCanceledException)
        {
            var code = e is BackendException b ? b.Code : "query-cancelled";
            error.WriteLine($"Error: {code}. {Safe(e is BackendException ? e.Message : "Consulta cancelada; sin cambios locales.")} Consulte coquena --help.");
            return 1;
        }
        finally { client?.Dispose(); }
    }

    private static bool TryRemote(string[] args, out string? family, out string? release)
    {
        family = null; release = "latest";
        if (args.Length < 2 || args[0] != "backend" || args[1] != "remote") return false;
        var hasRelease = false;
        for (var i = 2; i < args.Length; i++)
        {
            if (args[i] == "--release")
            {
                if (hasRelease || ++i == args.Length) return false;
                release = args[i]; hasRelease = true;
                try { if (release != "latest") _ = BackendCatalog.BuildNumber(release); }
                catch (BackendException) { return false; }
            }
            else
            {
                if (family is not null || !BackendCatalog.ValidFamily(args[i])) return false;
                family = args[i];
            }
        }
        return true;
    }

    private static string Safe(string value) => new(value.Select(c => char.IsControl(c) ? ' ' : c).ToArray());
}
