namespace Coquena;

internal static class CliHelp
{
    public static void Write(TextWriter output)
    {
        var color = ReferenceEquals(output, Console.Out) && !Console.IsOutputRedirected &&
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")) &&
            Environment.GetEnvironmentVariable("TERM") != "dumb";
        foreach (var line in Messages.Help.ToString().Split('\n'))
        {
            var heading = line.Length > 0 && !char.IsWhiteSpace(line[0]);
            var command = line.StartsWith("  ", StringComparison.Ordinal) && line.Length > 2 && !char.IsWhiteSpace(line[2]);
            if (color && (heading || command))
            {
                var split = command ? line.IndexOf("  ", 2, StringComparison.Ordinal) : -1;
                if (split < 0) split = line.Length;
                output.Write(heading ? "\u001b[1m" : "\u001b[36m");
                output.Write(line[..split]);
                output.Write("\u001b[0m");
                output.WriteLine(line[split..]);
            }
            else output.WriteLine(line);
        }
    }
}
