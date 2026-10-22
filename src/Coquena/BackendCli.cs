using Coquena.Contracts;
using Coquena.Backends;
using Coquena.Downloads;
using Coquena.Storage;

namespace Coquena;

public static class BackendCli
{
    public static int Run(string[] args, TextWriter output, TextWriter error,
        Func<HardwareContext>? inspect = null, OfficialBackendSource? source = null,
        CancellationToken cancellation = default)
    {
        if (args.Length >= 2 && args[0] == "backend" && args[1] == "uninstall")
            return ResourceRemoval.Cli(args[2..], output, error, backend: true);
        if (args.Length >= 2 && args[0] == "backend" && args[1] == "upgrade")
            return Upgrade(args, output, error, cancellation);
        if (args.Length >= 2 && args[0] == "backend" && args[1] == "update")
            return Update(args, output, error, inspect, source, cancellation);
        if (args.Length >= 2 && args[0] == "backend" && args[1] is "list" or "use" or "install" or "register")
            return Installed(args, output, error, inspect, source, cancellation);
        var system = args.Length == 2 && args[0] == "system" && args[1] == "probe";
        if (!system && !TryRemote(args, out _, out _))
        {
            error.WriteLine("Error: argumentos inválidos. Uso: coquena system probe | backend remote [cpu|vulkan|cuda@<mayor>] [--release bNNNN|latest]. Consulte coquena --help.");
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
                output.WriteLine(hardware.MemoryBytes is ulong ram ? $"RAM física: {ByteSize.Format(ram)}" : "RAM física: desconocida");
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
                foreach (var artifact in selected.Artifacts) output.WriteLine($"Artefacto: {artifact.Name} ({(artifact.Size is long size ? ByteSize.Format(size) : "desconocido")})");
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

    private static int Installed(string[] args, TextWriter output, TextWriter error,
        Func<HardwareContext>? inspect, OfficialBackendSource? source, CancellationToken cancellation)
    {
        var action = args[1];
        string? path = null, name = null, familyDeclared = null, distribution = null;
        if (action == "register")
        {
            if (args.Length < 7) { error.WriteLine("Error: argumentos inválidos. Consulte coquena --help."); return 2; }
            path = args[2];
            for (var i = 3; i < args.Length; i += 2)
            {
                if (i + 1 >= args.Length) { error.WriteLine("Error: argumentos inválidos. Consulte coquena --help."); return 2; }
                switch (args[i])
                {
                    case "--name" when name is null: name = args[i + 1]; break;
                    case "--family" when familyDeclared is null: familyDeclared = args[i + 1]; break;
                    case "--distribution" when distribution is null: distribution = args[i + 1]; break;
                    default: error.WriteLine("Error: argumentos inválidos. Consulte coquena --help."); return 2;
                }
            }
            if (name is null || familyDeclared is null) { error.WriteLine("Error: argumentos inválidos. Consulte coquena --help."); return 2; }
        }
        var remote = args.ToArray(); remote[1] = "remote";
        if ((action == "list" && args.Length != 2) || (action == "use" && args.Length is not (2 or 3)) ||
            (action == "install" && !TryRemote(remote, out _, out _)))
        { error.WriteLine("Error: argumentos inválidos. Consulte coquena --help."); return 2; }
        try
        {
            var installer = new BackendInstaller(Home.Resolve());
            if (action == "register")
            {
                var registered = installer.RegisterAsync(path!, name!, familyDeclared!, distribution, cancellation).GetAwaiter().GetResult();
                output.WriteLine($"Backend externo: {Safe(registered.Name!)} | {registered.Id:D} | {registered.Repository} | {registered.Variant}:{registered.Build} | {Safe(registered.ServerPath)}");
                output.WriteLine("Distribución y familia declaradas; el sondeo no certifica procedencia ni aceleración.");
                return 0;
            }
            if (action == "list")
            {
                var records = installer.List();
                foreach (var b in records) output.WriteLine($"{b.Id:D} | {Safe(b.Name ?? b.Origin + "/" + b.Variant + ":" + b.Build)} | {b.Ownership} | {b.Repository} | {b.Variant}:{b.Build} | {b.Platform} | latest={b.TracksLatest} pin={b.Pinned} | {Safe(new BackendPaths(Home.Resolve()).Server(b))}");
                var updater = new BackendUpdater(Home.Resolve());
                foreach (var b in records.Where(b => b.Ownership == "managed" && b.TracksLatest))
                    if (updater.Observation(b) is { } observation) ShowUpdate(output, $"{b.Origin}/{b.Variant}:latest", observation);
                if (records.Length == 0) output.WriteLine("Sin backends instalados.");
                return 0;
            }
            if (action == "use")
            {
                var selected = installer.Use(args.Length == 3 ? args[2] : null);
                output.WriteLine(selected is null ? "Sin backend predeterminado." : selected.Kind == "latest"
                    ? $"Backend predeterminado: {selected.Origin}/{selected.Variant}:latest" : $"Backend predeterminado fijado: {selected.BackendId:D}");
                return 0;
            }
            var hardware = (inspect ?? HardwareProbe.Inspect)();
            if (hardware.Platform != "win-x64") throw new DownloadException("unsupported-platform", "M1 requiere win-x64 nativo.");
            TryRemote(remote, out var family, out var release);
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
            source ??= new(client);
            using var stopped = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; stopped.Cancel(); };
            Console.CancelKeyPress += handler;
            try
            {
                var resolution = source.ResolveAsync(hardware, family, release!, stopped.Token).GetAwaiter().GetResult();
                var candidate = resolution.Selected ?? throw new DownloadException("no-candidate", "Sin candidato instalable.");
                output.WriteLine($"Selección: ggml/{candidate.Variant}:{candidate.Release} [{candidate.Compatibility}] — {Safe(candidate.Diagnostic)}");
                foreach (var alternative in resolution.Alternatives) output.WriteLine($"Alternativa: {alternative.Variant}:{alternative.Release} {alternative.Platform} [{alternative.Compatibility}]");
                var operation = installer.Create(candidate, release!);
                output.WriteLine($"Operación {operation.Id:D}; descarga/reinstalación en primer plano.");
                // DownloadCli owns Ctrl+C and the same consumer on resume; no second selection.
                return DownloadCli.Run(["resume", operation.Id.ToString("D")], output, error, cancellation: stopped.Token);
            }
            finally { Console.CancelKeyPress -= handler; }
        }
        catch (Exception e) when (e is DownloadException or BackendException or StorageException or ContractException or IOException or UnauthorizedAccessException or OperationCanceledException or ArgumentException)
        {
            var code = e switch { DownloadException d => d.Code, BackendException b => b.Code,
                StorageException s => s.Code, ContractException c => c.Code, _ => "installation-not-confirmed" };
            error.WriteLine($"Error: {code}. Consulte coquena --help y download status."); return 1;
        }
    }

    private static int Upgrade(string[] args, TextWriter output, TextWriter error, CancellationToken cancellation)
    {
        if (args.Length is not (2 or 3)) { error.WriteLine("Error: argumentos inválidos. Consulte coquena --help."); return 2; }
        try
        {
            var home = Home.Resolve();
            var installer = new BackendInstaller(home, retentionReport: output.WriteLine);
            var results = new BackendUpgrader(home, consumer: installer).UpgradeAsync(args.Length == 3 ? args[2] : null,
                (operation, token) =>
                {
                    var code = DownloadCli.Run(["resume", operation.Id.ToString("D")], output, error, cancellation: token);
                    return Task.FromResult(code == 0 && installer.Published(operation));
                }, output.WriteLine, cancellation).GetAwaiter().GetResult();
            foreach (var result in results)
            {
                output.WriteLine($"{result.Selector}: {result.State}{(result.OperationId is { } id ? $" | operación {id:D}" : "")}");
                if (result.State == "failed") error.WriteLine($"Error: {result.Diagnostic}. Consulte coquena --help y download status.");
            }
            if (results.Length == 0) output.WriteLine("Sin seguimientos latest oficiales registrados.");
            return results.Any(r => r.State is "failed" or "pending") ? 1 : 0;
        }
        catch (Exception e) when (e is BackendException or StorageException or ContractException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            error.WriteLine($"Error: {e.Message} Consulte coquena --help.");
            return e is BackendException { Code: "invalid-latest-selector" } ? 2 : 1;
        }
    }

    private static int Update(string[] args, TextWriter output, TextWriter error,
        Func<HardwareContext>? inspect, OfficialBackendSource? source, CancellationToken cancellation)
    {
        if (args.Length is not (2 or 3))
        { error.WriteLine("Error: argumentos inválidos. Uso: coquena backend update [selector-latest]. Consulte coquena --help."); return 2; }
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
            source ??= new(client);
            using var stopped = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
            ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; stopped.Cancel(); };
            Console.CancelKeyPress += handler;
            BackendUpdateResult[] results;
            try
            {
                results = new BackendUpdater(Home.Resolve()).UpdateAsync(args.Length == 3 ? args[2] : null,
                    (inspect ?? HardwareProbe.Inspect)(), source, stopped.Token).GetAwaiter().GetResult();
            }
            finally { Console.CancelKeyPress -= handler; }
            foreach (var result in results)
            {
                if (result.Persisted && result.Observation is { } observation) ShowUpdate(output, result.Selector, observation);
                if (!result.Persisted || result.Observation?.State == "failed")
                    error.WriteLine($"Error: {Safe(result.Selector)} — {Safe(result.Error ?? result.Observation!.Diagnostic)}. Consulte coquena --help.");
            }
            if (results.Length == 0) output.WriteLine("Sin seguimientos latest oficiales registrados; pins y externos excluidos.");
            output.WriteLine("Consulta de metadatos; no descarga ni cambia el build instalado o las selecciones fijadas.");
            return results.Any(r => !r.Persisted || r.Observation?.State == "failed") ? 1 : 0;
        }
        catch (Exception e) when (e is BackendException or StorageException or ContractException or IOException or UnauthorizedAccessException or OperationCanceledException)
        {
            var code = e switch { BackendException b => b.Code, StorageException s => s.Code,
                ContractException c => c.Code, OperationCanceledException => "query-cancelled", _ => "update-not-saved" };
            error.WriteLine($"Error: {code}. Consulte coquena --help.");
            return code == "invalid-latest-selector" ? 2 : 1;
        }
    }

    private static void ShowUpdate(TextWriter output, string selector, BackendUpdateObservation observation)
    {
        output.WriteLine($"{Safe(selector)} ({observation.Platform}) | instalado={observation.InstalledBuild} | consulta={observation.CheckedAt:O} | {observation.State} | {Safe(observation.Diagnostic)}");
        if (observation.Candidate is not { } candidate) return;
        output.WriteLine($"  Candidato guardado: {candidate.Build} [{candidate.Compatibility}] — {Safe(candidate.Diagnostic)}");
        foreach (var artifact in candidate.Artifacts) output.WriteLine($"  Artefacto: {artifact.Path} ({(artifact.Size is long size ? ByteSize.Format(size) : "desconocido")})");
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
