using Coquena.Contracts;

namespace Coquena.Configuration;

public sealed record RetentionView(long MaxBytes, string Origin);

public static class LogRetentionPolicy
{
    public const long InitialBytes = 104857600;
    public static long Parse(string text)
    {
        try
        {
            var bytes = ByteSize.Parse(text);
            if (bytes == 0) throw new FormatException("La retención debe ser mayor que 0 B.");
            return bytes;
        }
        catch (FormatException error) { throw new ConfigurationException(error.Message); }
    }

    public static RetentionView Resolve(LogRetention? global, LogRetention? own = null) =>
        new(own?.MaxBytes ?? global?.MaxBytes ?? InitialBytes, own is not null ? "propio" : global is not null ? "global" : "inicial");

    public static void Print(RetentionView view, TextWriter output)
    {
        output.WriteLine($"Retención: {ByteSize.Format(view.MaxBytes)} | origen: {view.Origin}.");
    }
}
