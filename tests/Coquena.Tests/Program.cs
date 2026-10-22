using System.Diagnostics;
using Coquena;

// Match the product's output contract, including fixture and failure diagnostics.
Console.OutputEncoding = new System.Text.UTF8Encoding(false);

if (args.Length == 1 && args[0] == "--recipe-tests")
{ Coquena.Tests.RecipeTests.Run(); return; }

if (args.Length == 1 && args[0] == "--project-persistence-tests")
{ Coquena.Tests.ProjectPersistenceTests.Run(); return; }
if (args.Length == 4 && args[0] == "--project-crash-worker")
{ Coquena.Tests.ProjectPersistenceTests.Worker(args); return; }
if (args.Length == 4 && args[0] == "--project-writer-worker")
{ Coquena.Tests.ProjectPersistenceTests.WriterWorker(args); return; }

if (args.Length == 1 && args[0] == "--model-selection-tests")
{ await Coquena.Tests.ModelSourceTests.Run(); await Coquena.Tests.ResolutionTests.Run(); return; }

if (args.Length is 1 or 2 && args[0] == "--byte-size-tests")
{ Coquena.Tests.ByteSizeTests.Run(); Coquena.Tests.RetentionTests.Run(args.Length == 2 ? Path.GetFullPath(args[1]) : null); return; }
if (args.Length is 1 or 2 && args[0] == "--help-tests")
{ Coquena.Tests.HelpTests.Run(null); if (args.Length == 2) Coquena.Tests.HelpTests.Run(Path.GetFullPath(args[1])); return; }

if (args.Length is 1 or 2 && args[0] == "--mtp-tests")
{ await Coquena.Tests.ResolutionTests.Run(); await Coquena.Tests.MtpResolutionTests.Run(args.Length == 2 ? Path.GetFullPath(args[1]) : null); return; }

if (args.Length is 1 or 2 && args[0] == "--backend-publication-tests")
{ await Coquena.Tests.InstallerTests.Run(args.Length == 2 ? Path.GetFullPath(args[1]) : null); await Coquena.Tests.BackendPublicationTests.Run(); return; }

if (args.Length == 1 && args[0] == "--contention-tests")
{ await Coquena.Tests.ContentionTests.Run(); return; }
if (args.Length == 3 && args[0] == "--contention-crash-worker")
{ await Coquena.Tests.ContentionTests.CrashWorker(args[1], Guid.Parse(args[2])); return; }
if (args.Length is 1 or 2 && args[0] == "--download-tests")
{ await Coquena.Tests.DownloadTests.Run(args.Length == 2 ? Path.GetFullPath(args[1]) : null); return; }

if (args.Length == 1 && args[0] == "--runtime-child")
{
    Console.CancelKeyPress += (_, e) => e.Cancel = true;
    Thread.Sleep(Timeout.Infinite); return;
}

if (args.Length is 1 or 2 && args[0] == "--removal-tests")
{ Coquena.Tests.RemovalTests.Run(args.Length == 2 ? Path.GetFullPath(args[1]) : null); return; }
if (args.Length is 1 or 2 && args[0] == "--server-removal-tests")
{ Coquena.Tests.ServerRemovalTests.Run(args.Length == 2 ? Path.GetFullPath(args[1]) : null); return; }

if (args.Contains("--models-preset")) { Coquena.Tests.ServerRuntimeTests.Fixture(args); return; }
if (args.Length == 5 && args[0] == "--runtime-console-signal")
{ Environment.ExitCode = Coquena.Tests.ServerRuntimeTests.ConsoleSignal(args); return; }
if (args.Length == 2 && args[0] == "--internal-server-signal")
{ Environment.ExitCode = Coquena.Servers.ServerRuntime.SignalWorker(Coquena.Storage.Home.Resolve(), Guid.Parse(args[1])); return; }
if (args.Length is 1 or 2 && args[0] == "--runtime-tests")
{ Coquena.Tests.ServerRuntimeTests.Run(args.Length == 2 ? Path.GetFullPath(args[1]) : null); return; }

if (args.Length is 1 or 2 && args[0] == "--server-tests")
{
    Coquena.Tests.ServerTests.Run(args.Length == 2 ? Path.GetFullPath(args[1]) : null);
    return;
}

if (args.Length == 1 && args[0] == "--version") { Console.WriteLine($"version: {Environment.GetEnvironmentVariable("COQUENA_FIXTURE_BUILD") ?? "1"} (abcdef)"); return; }
if (args.Length is 1 or 2 && args[0] == "--local-integrity-tests")
{
    await Coquena.Tests.LocalIntegrityTests.Run(args.Length == 2 ? Path.GetFullPath(args[1]) : null);
    return;
}
if (args.Length is 1 or 2 && args[0] == "--external-model-tests")
{
    Coquena.Tests.ExternalModelTests.Run(args.Length == 2 ? Path.GetFullPath(args[1]) : null);
    return;
}
if (args.Length is 1 or 2 && args[0] == "--external-matching-tests")
{
    Coquena.Tests.ExternalMatchingTests.Run(args.Length == 2 ? Path.GetFullPath(args[1]) : null);
    return;
}
if (args.Length is 1 or 2 && args[0] == "--model-update-tests")
{
    await Coquena.Tests.ModelUpdateTests.Run(args.Length == 2 ? Path.GetFullPath(args[1]) : null);
    return;
}
if (args.Length is 1 or 2 && args[0] == "--model-upgrade-tests")
{
    await Coquena.Tests.ModelUpgradeTests.Run(args.Length == 2 ? Path.GetFullPath(args[1]) : null);
    return;
}
if (args.Length is 1 or 2 && args[0] == "--backend-upgrade-tests")
{
    await Coquena.Tests.BackendUpgradeTests.Run(args.Length == 2 ? Path.GetFullPath(args[1]) : null);
    return;
}
if (args.Length is 1 or 2 && args[0] == "--backend-update-tests")
{
    await Coquena.Tests.BackendUpdateTests.Run(args.Length == 2 ? Path.GetFullPath(args[1]) : null);
    return;
}
if (args.Length == 2 && args[0] == "--configuration-backend-smoke")
{
    await Coquena.Tests.ConfigurationTests.BackendSmoke(args[1]);
    return;
}
if (args.Length is 1 or 2 && args[0] == "--configuration-tests")
{
    Coquena.Tests.ConfigurationTests.Run(args.Length == 2 ? Path.GetFullPath(args[1]) : null);
    return;
}
if (args.Length is 1 or 2 && args[0] == "--parameter-tests")
{
    Coquena.Tests.ParameterTests.Run(args.Length == 2 ? Path.GetFullPath(args[1]) : null);
    return;
}
if (args.Length is 1 or 2 && args[0] == "--backend-directory-tests")
{
    await Coquena.Tests.BackendDirectoryTests.Run(args.Length == 2 ? Path.GetFullPath(args[1]) : null);
    return;
}
if (args.Length is 1 or 2 && args[0] == "--model-directory-tests")
{
    await Coquena.Tests.ModelDirectoryTests.Run(args.Length == 2 ? Path.GetFullPath(args[1]) : null);
    return;
}
if (args.Length is 2 or 3 && args[0] == "--hf-resolve")
{
    await Coquena.Tests.ResolutionTests.Live(args[1], args.Length == 3 ? args[2] : null);
    return;
}

if (args.Length == 2 && args[0] == "--hf-store")
{
    await Coquena.Tests.ModelSourceTests.Live(args[1]);
    return;
}

if (args.Length is 1 or 2 && args[0] == "--model-install-tests")
{
    await Coquena.Tests.ModelInstallerTests.Run(args.Length == 2 ? Path.GetFullPath(args[1]) : null);
    return;
}

if (args.Length == 3 && args[0] == "--download-worker")
{
    Coquena.Tests.DownloadTests.Worker(args[1], Guid.Parse(args[2]));
    return;
}

if (args.Length == 2 && args[0] == "--storage-worker")
{
    Coquena.Tests.StorageTests.Worker(args[1]);
    return;
}

string[][] cases = [[], ["--help"], ["-h"], ["help"], ["invalid"],
    ["backend"], ["--json"], ["--help", "extra"], ["invalid", "--help"],
    ["system"], ["system", "extra"], ["system", "probe", "extra"],
    ["system", "probe", "--json"], ["system", "probe", "--help"]];
var executable = args.Length == 1 ? Path.GetFullPath(args[0]) : null;
if (args.Length > 1 || (executable is not null && !File.Exists(executable)))
    throw new ArgumentException("Uso: Coquena.Tests [ruta-binario-nativo]");

foreach (var input in cases)
{
    var expected = input.Length == 0 ||
        (input.Length == 1 && input[0] is "--help" or "-h" or "help") ? 0 : 2;
    using var output = new StringWriter();
    using var error = new StringWriter();
    Verify(Cli.Run(input, output, error), output.ToString(), error.ToString());

    if (executable is not null)
    {
        var start = new ProcessStartInfo(executable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in input) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new Exception("No inició el binario.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(10_000))
        {
            process.Kill(entireProcessTree: true);
            throw new Exception("El binario excedió 10 segundos.");
        }
        Verify(process.ExitCode, await stdout, await stderr);
    }

    void Verify(int code, string stdout, string stderr)
    {
        if (code != expected || (expected == 0
            ? !stdout.Contains("Uso: coquena") || !stdout.Contains("download") || !stdout.Contains("system probe") || stderr.Length != 0
            : stdout.Length != 0 || !stderr.Contains("Error:") || !stderr.Contains("coquena --help")))
            throw new Exception($"Falló [{string.Join(", ", input)}]: code={code}, out={stdout}, err={stderr}");
    }
}

Console.WriteLine($"OK: {cases.Length} casos administrados" +
    (executable is null ? "." : $" y {cases.Length} casos del ejecutable publicado."));

Coquena.Tests.ContractTests.Run();
Coquena.Tests.HelpTests.Run(null);
if (executable is not null) Coquena.Tests.HelpTests.Run(executable);
Coquena.Tests.RemovalTests.Run(executable);
Coquena.Tests.ConfigurationTests.Run(executable);
Coquena.Tests.ParameterTests.Run(executable);
Coquena.Tests.ByteSizeTests.Run();
Coquena.Tests.RetentionTests.Run(executable);
Coquena.Tests.ServerTests.Run(executable);
Coquena.Tests.ServerRemovalTests.Run(executable);
Coquena.Tests.ServerRuntimeTests.Run(executable);
Coquena.Tests.StorageTests.Run();
Coquena.Tests.ProjectPersistenceTests.Run();
Coquena.Tests.RecipeTests.Run();
await Coquena.Tests.BackendTests.Run(executable);
await Coquena.Tests.DownloadTests.Run(executable);
await Coquena.Tests.ContentionTests.Run();

await Coquena.Tests.InstallerTests.Run(executable);
await Coquena.Tests.BackendPublicationTests.Run();
await Coquena.Tests.ExternalBackendTests.Run(executable);
await Coquena.Tests.BackendUpdateTests.Run(executable);
await Coquena.Tests.BackendUpgradeTests.Run(executable);
await Coquena.Tests.BackendDirectoryTests.Run(executable);
await Coquena.Tests.ResolutionTests.Run();
await Coquena.Tests.MtpResolutionTests.Run(executable);
await Coquena.Tests.ModelSourceTests.Run();
await Coquena.Tests.GgufTests.Run();
await Coquena.Tests.ModelInstallerTests.Run(executable);

await Coquena.Tests.ModelDirectoryTests.Run(executable);
Coquena.Tests.ExternalModelTests.Run(executable);
await Coquena.Tests.LocalIntegrityTests.Run(executable);
Coquena.Tests.ExternalMatchingTests.Run(executable);
await Coquena.Tests.ModelUpdateTests.Run(executable);
await Coquena.Tests.ModelUpgradeTests.Run(executable);
