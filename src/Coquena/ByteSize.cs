using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Coquena;

/// <summary>Shared byte parsing and human presentation. Store the original quantity.</summary>
public static partial class ByteSize
{
    private static readonly string[] Units = ["B", "KiB", "MiB", "GiB", "TiB", "PiB", "EiB"];

    [GeneratedRegex(@"\A([0-9]+)(?:\.([0-9]+))? *(B|[KMGTPE](?:i?B)?)?\z", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static long Parse(string text)
    {
        var match = Pattern().Match(text);
        if (!match.Success || match.Groups[2].Success && !match.Groups[3].Success)
            throw new FormatException("Tamaño inválido; use bytes, KiB/MiB/GiB/TiB/PiB/EiB o KB/MB/GB/TB/PB/EB (base 1024), k/m/g/t/p/e (base 1000) y punto decimal; sin distinguir mayúsculas.");
        var suffix = match.Groups[3].Value;
        var unit = suffix.Length == 0 || suffix.Equals("B", StringComparison.OrdinalIgnoreCase)
            ? 0 : "KMGTPE".IndexOf(char.ToUpperInvariant(suffix[0])) + 1;
        var scale = suffix.Length == 1 && unit > 0 ? 1000 : 1024;
        var fraction = match.Groups[2].Value;
        var numerator = BigInteger.Parse(match.Groups[1].Value + fraction, CultureInfo.InvariantCulture) * BigInteger.Pow(scale, unit);
        var bytes = BigInteger.DivRem(numerator, BigInteger.Pow(10, fraction.Length), out var remainder);
        if (remainder != 0 || bytes < 0 || bytes > long.MaxValue)
            throw new FormatException("El tamaño debe representar bytes enteros entre 0 B y 8 EiB (exclusivo).");
        return (long)bytes;
    }

    public static string Format(decimal bytes, IFormatProvider? culture = null)
    {
        if (bytes < 0) throw new ArgumentOutOfRangeException(nameof(bytes));
        var unit = 0;
        while (bytes >= 1024 && unit < Units.Length - 1) { bytes /= 1024; unit++; }
        bytes = decimal.Round(bytes, 2, MidpointRounding.AwayFromZero);
        if (bytes >= 1024 && unit < Units.Length - 1) { bytes /= 1024; unit++; }
        return bytes.ToString("0.##", culture ?? CultureInfo.CurrentCulture) + " " + Units[unit];
    }
}
