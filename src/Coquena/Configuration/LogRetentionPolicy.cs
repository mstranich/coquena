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
            if (bytes == 0) throw new FormatException(Messages.RetentionPositive);
            return bytes;
        }
        catch (FormatException error) { throw new ConfigurationException(error.Message); }
    }

    public static RetentionView Resolve(LogRetention? global, LogRetention? own = null) =>
        new(own?.MaxBytes ?? global?.MaxBytes ?? InitialBytes, own is not null ? "own" : global is not null ? "global" : "initial");

    public static void Print(RetentionView view, TextWriter output)
    {
        output.WriteLine(Messages.RetentionShow.Format(ByteSize.Format(view.MaxBytes), Messages.RetentionOrigin(view.Origin)));
    }
}
