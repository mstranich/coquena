using System.Globalization;
using Coquena.Backends;
using Coquena.Contracts;
using Coquena.Downloads;
using Coquena.Models;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class ByteSizeTests
{
    public static void Run()
    {
        var checks = 0;
        var invariant = CultureInfo.InvariantCulture;
        var units = new[] { "B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB" };
        long factor = 1;
        for (var i = 0; i < units.Length; i++)
        {
            Check(ByteSize.Parse("1" + units[i]) == factor, "parse " + units[i]);
            Check(ByteSize.Format(factor, invariant) == "1 " + units[i], "format " + units[i]);
            if (i > 0)
            {
                var alias = units[i].Replace("i", "");
                Check(ByteSize.Parse("1" + alias) == factor && ByteSize.Parse("1" + alias.ToLowerInvariant()) == factor, "binary alias " + alias);
                Check(ByteSize.Parse("1" + units[i].ToLowerInvariant()) == factor, "case insensitive IEC");
                Check(ByteSize.Parse("1" + alias[0]) == (long)System.Numerics.BigInteger.Pow(1000, i), "decimal short unit");
                Check(ByteSize.Parse("1" + char.ToLowerInvariant(alias[0])) == (long)System.Numerics.BigInteger.Pow(1000, i), "lowercase decimal unit");
                Check(ByteSize.Format(factor - 1, invariant) == (i == 1 ? "1023 B" : "1 " + units[i]), "rounding boundary");
                Check(ByteSize.Format(factor * 1.5m, invariant) == "1.5 " + units[i], "fraction");
            }
            if (i < units.Length - 1) factor *= 1024;
        }
        foreach (var text in new[] { "1Ki", "1MiBB", "1XB", "1.001kB", "0.0001k", "10e", "8EB", "-1B", "+1B", "1e3B", "1,5GiB", "0.1KiB", "1.5", "8EiB", "9223372036854775808", "1B\n", "", "1 B extra" })
        {
            try { ByteSize.Parse(text); throw new Exception("Accepted invalid size: " + text); }
            catch (FormatException) { checks++; }
        }
        Check(ByteSize.Parse("0") == 0 && ByteSize.Format(0, invariant) == "0 B", "zero");
        Check(ByteSize.Parse("9007199254740993B") == 9007199254740993, "beyond double precision");
        Check(ByteSize.Parse("9223372036854775807") == long.MaxValue, "maximum");
        Check(ByteSize.Format(long.MaxValue, invariant) == "8 EiB", "rounded maximum");
        Check(ByteSize.Parse("0.5KiB") == 512, "exact fractional bytes");
        Check(ByteSize.Parse("1.5mB") == 1572864 && ByteSize.Parse("1.5m") == 1500000, "fractional binary alias versus decimal");
        Check(ByteSize.Parse("0.001k") == 1 && ByteSize.Parse("1.000 e") == 1000000000000000000, "exact decimal bytes");
        Check(ByteSize.Format(ByteSize.Parse("1MB"), invariant) == "1 MiB" && ByteSize.Format(ByteSize.Parse("1m"), invariant) == "976.56 KiB", "normalized output");
        Check(ByteSize.Format(1048570, invariant) == "1023.99 KiB", "before promotion");
        Check(ByteSize.Format(1048571, invariant) == "1 MiB", "promotion");
        Check(ByteSize.Format(1536, CultureInfo.GetCultureInfo("es-AR")) == "1,5 KiB", "local decimal");
        Check(ByteSize.Format(ulong.MaxValue, invariant) == "16 EiB", "unsigned memory");
        try { ByteSize.Format(-1); throw new Exception("Accepted negative size"); }
        catch (ArgumentOutOfRangeException) { checks++; }
        using (var output = new StringWriter())
        using (var error = new StringWriter())
        {
            var hardware = new HardwareContext("win-x64", "fixture", 16UL * 1024 * 1024 * 1024,
                [], null, [], new("unknown", "fixture"), new("unknown", "fixture"));
            Check(BackendCli.Run(["system", "probe"], output, error, () => hardware) == 0 &&
                output.ToString().Contains("RAM física: 16 GiB"), "RAM CLI");
        }
        var repositoryFile = new RepositoryFile("tiny-Q4_0.gguf", 1610612736, new string('a', 40), null, null);
        var variant = ModelSelection.Variant(new([repositoryFile]),
            [new(repositoryFile.Path, new string('b', 40), ModelRole.Principal, "Q4_0", "Q4_0", EvidenceSource.Name, EvidenceState.Candidate, null)]);
        Check(variant!.Label.Contains(ByteSize.Format(repositoryFile.Size)) && !variant.Label.Contains(" GB"), "model menu");
        var root = Path.Combine(@"D:\tmp", "coquena-042-" + Guid.NewGuid().ToString("N"));
        try
        {
            var manager = new DownloadManager(new Home(root));
            var id = Guid.NewGuid();
            var file = new RemoteFile("tiny.gguf", 1610612736);
            manager.Create(new(1, id, "model", "fixture", new("owner/repo", "main", new string('a', 40), [file]), null,
                [new(file.Path, $"downloads/{id:D}/files/tiny.part", "models/fixture/tiny.gguf", 0, file.Size, null, null, null, "pending")],
                "pending", "transfer", DateTimeOffset.UtcNow, null));
            var before = File.ReadAllText(Path.Combine(root, "downloads", id.ToString("D"), "operation.json"));
            using var output = new StringWriter(); using var error = new StringWriter();
            Check(DownloadCli.Run(["status", id.ToString("D")], output, error, manager) == 0 &&
                output.ToString().Contains("0 B/" + ByteSize.Format(file.Size!.Value)), "download progress and total");
            Check(File.ReadAllText(Path.Combine(root, "downloads", id.ToString("D"), "operation.json")) == before, "display preserves exact operation");
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        Console.WriteLine($"OK: {checks} comprobaciones de unidades legibles.");

        void Check(bool condition, string label)
        { if (!condition) throw new Exception("ByteSize: " + label); checks++; }
    }
}
