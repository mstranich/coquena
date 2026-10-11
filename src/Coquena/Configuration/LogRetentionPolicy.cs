using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using Coquena.Contracts;

namespace Coquena.Configuration;

public sealed record RetentionView(long MaxBytes, string Origin);

public static partial class LogRetentionPolicy
{
    public const long InitialBytes = 104857600;
    [GeneratedRegex(@"^([0-9]+)(?:\.([0-9]+))? *(B|KB|MB|GB|KiB|MiB|GiB)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SizePattern();

    public static long Parse(string text)
    {
        var match = SizePattern().Match(text);
        if (!match.Success) throw new ConfigurationException("Tamaño inválido; use bytes o B, KB/MB/GB, KiB/MiB/GiB y punto decimal.");
        var factor = match.Groups[3].Value.ToUpperInvariant() switch
        {
            "KB" => 1000L, "MB" => 1000000L, "GB" => 1000000000L,
            "KIB" => 1024L, "MIB" => 1048576L, "GIB" => 1073741824L, _ => 1L
        };
        var fraction = match.Groups[2].Value;
        var numerator = BigInteger.Parse(match.Groups[1].Value + fraction, CultureInfo.InvariantCulture) * factor;
        var bytes = BigInteger.DivRem(numerator, BigInteger.Pow(10, fraction.Length), out var remainder);
        if (remainder != 0 || bytes < 1 || bytes > long.MaxValue)
            throw new ConfigurationException("El tamaño debe representar bytes enteros entre 1 y 9223372036854775807.");
        return (long)bytes;
    }

    public static RetentionView Resolve(LogRetention? global, LogRetention? own = null) =>
        new(own?.MaxBytes ?? global?.MaxBytes ?? InitialBytes, own is not null ? "propio" : global is not null ? "global" : "inicial");

    public static void Print(RetentionView view, TextWriter output)
    {
        var units = new[] { "B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB" };
        var value = (decimal)view.MaxBytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        output.WriteLine($"Retención: {view.MaxBytes.ToString(CultureInfo.InvariantCulture)} bytes | {value.ToString("0.###", CultureInfo.InvariantCulture)} {units[unit]} | origen: {view.Origin}.");
    }
}
