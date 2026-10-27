using System.Diagnostics;

namespace Coquena.Tests;

// Retained M2 engines are tested through their API worker, not as the public
// scoped product CLI. Other command families still invoke the product.
internal static class ModelEngineProcess
{
    internal static Process? Start(ProcessStartInfo start)
    {
        if (start.ArgumentList.Count > 0 && start.ArgumentList[0] == "model")
        {
            var args = start.ArgumentList.Skip(1).ToArray(); start.ArgumentList.Clear();
            start.FileName = Environment.ProcessPath!;
            if (Path.GetFileNameWithoutExtension(start.FileName).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "Coquena.Tests.dll"));
            start.ArgumentList.Add("--m2-model-api");
            foreach (var arg in args) start.ArgumentList.Add(arg);
        }
        return Process.Start(start);
    }
}
