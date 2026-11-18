using System.Globalization;

namespace Coquena.Observation;

// How often observers read a service: config observer-interval, --interval and Configuración › Coquena.
public static class ObserverInterval
{
    public const double MinimumSeconds = 0.25, MaximumSeconds = 60;
    public static readonly TimeSpan Default = TimeSpan.FromSeconds(2);
    // service logs -f shows a single service, so its status line reads faster by default.
    public static readonly TimeSpan Follow = TimeSpan.FromMilliseconds(500);

    public static bool Valid(double seconds) => seconds >= MinimumSeconds && seconds <= MaximumSeconds;

    // Accepts a decimal point or a decimal comma: 0.5 and 0,5 are the same value.
    public static double Parse(string text) =>
        double.TryParse(text.Trim().Replace(',', '.'), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds) && Valid(seconds)
            ? seconds : throw new ArgumentException(Messages.ObserverIntervalInvalid.Format(Format(MinimumSeconds), Format(MaximumSeconds)));

    // Regional formats follow the operating system; the language only changes texts.
    public static string Format(double seconds) => seconds.ToString("0.##", CultureInfo.CurrentCulture);

    // Removes --interval <seconds> from the arguments; null when it is absent.
    public static TimeSpan? Take(List<string> args)
    {
        var index = args.IndexOf("--interval");
        if (index < 0) return null;
        if (index + 1 >= args.Count || args.IndexOf("--interval", index + 1) >= 0) throw new ArgumentException(Messages.ObserverIntervalInvalid.Format(Format(MinimumSeconds), Format(MaximumSeconds)));
        var seconds = Parse(args[index + 1]);
        args.RemoveRange(index, 2);
        return TimeSpan.FromSeconds(seconds);
    }
}
