using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Coquena.Configuration;
using Coquena.Contracts;
using Coquena.Storage;

namespace Coquena.Servers;

public sealed partial class ServerRuntime
{
    public const long DefaultLogBytes = 100L * 1024 * 1024;
    private void ValidateLast(LastLogDocument value)
    {
        if (value.SchemaVersion != 1 || value.ServerId == Guid.Empty || value.InstanceId == Guid.Empty)
            throw new ContractException("invalid-document", "Referencia de último log inválida.");
    }
    private LastLogDocument? Last(string server)
    {
        var last = store.Read(DocumentKind.LastLog, Json.LastLogDocument, ValidateLast, server: server);
        if (last is not null && last.LogPath != $"logs/{server}/{last.InstanceId:D}.log")
            throw new ConfigurationException("Ruta de último log incierta.");
        return last;
    }
    private void RememberLog(string server, InstanceDocument instance) =>
        store.UpdateLocked(DocumentKind.LastLog, Json.LastLogDocument, ValidateLast,
            _ => new(1, instance.ServerId, instance.InstanceId, instance.LogPath), server: server);

    internal void RetainLogs(string server, InstanceDocument? active, TextWriter output)
    {
        var last = Last(server);
        var limit = Manager.RetentionLocked(server).MaxBytes;
        var directory = home.PathFor($"logs/{server}");
        if (!Directory.Exists(directory)) return;
        var files = Directory.GetFiles(directory, "*.log").Select(path => new FileInfo(home.PathFor($"logs/{server}/{Path.GetFileName(path)}")))
            .Where(file => Guid.TryParseExact(Path.GetFileNameWithoutExtension(file.Name), "D", out _)).OrderBy(file => file.CreationTimeUtc).ThenBy(file => file.Name, StringComparer.Ordinal).ToArray();
        var total = files.Sum(file => file.Length);
        foreach (var file in files)
        {
            if (total <= limit) break;
            if ($"logs/{server}/{file.Name}" == active?.LogPath || $"logs/{server}/{file.Name}" == last?.LogPath) continue;
            try { var length = file.Length; file.Delete(); total -= length; }
            catch (IOException e) { output.WriteLine($"Diagnóstico: no se pudo retirar log {file.Name}: {e.Message}"); }
        }
        if (total > limit) output.WriteLine($"Diagnóstico: logs de {server} superan {ByteSize.Format(limit)}; se conservan logs protegidos.");
    }

    public int Logs(string? name, bool follow, TextWriter output, Func<bool>? cancelled = null)
    {
        var path = store.Exclusive(() =>
        {
            var server = Manager.Named(name);
            var active = RecoverLocked(server.Name);
            if (active is not null) RememberLog(server.Name, active);
            var last = Last(server.Name);
            if (last is not null && last.ServerId != server.Id) throw new ConfigurationException("Último log pertenece a otra definición de servidor.");
            RetainLogs(server.Name, active, output);
            return home.PathFor(active?.LogPath ?? last?.LogPath ?? throw new ConfigurationException("No hay log disponible para este servidor."));
        });
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        // Recent output is bounded even when the active log exceeds retention.
        if (stream.Length > 65536)
        {
            stream.Seek(-65536, SeekOrigin.End);
            int b; while ((b = stream.ReadByte()) >= 0 && b != '\n') { }
        }
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096);
        var buffer = new char[4096];
        var remaining = 65536;
        do
        {
            if (cancelled?.Invoke() == true) break;
            var read = reader.Read(buffer, 0, follow ? buffer.Length : Math.Min(buffer.Length, remaining));
            if (read > 0) { output.Write(buffer, 0, read); output.Flush(); }
            else if (!follow) break;
            else Thread.Sleep(100);
            if (!follow && (remaining -= read) <= 0) break;
        } while (true);
        return 0;
    }

    public void Ps(TextWriter output, HttpClient? client = null)
    {
        var snapshots = store.Exclusive(() =>
        {
            var result = new List<(string Name, InstanceDocument Instance, string[] Models)>();
            var directory = home.PathFor("run");
            if (!Directory.Exists(directory)) return result;
            foreach (var folder in Directory.GetDirectories(directory))
            {
                Home.CheckLinks(folder); var name = Path.GetFileName(folder);
                try
                {
                    var active = RecoverLocked(name);
                    if (active is null) continue;
                    var ini = home.PathFor(active.IniPath);
                    var models = File.Exists(ini) && new FileInfo(ini).Length <= 1024 * 1024 ? File.ReadLines(ini).Where(line => line.StartsWith('[') && line.EndsWith(']'))
                        .Select(line => line[1..^1]).Where(line => line != "*").ToArray() : [];
                    result.Add((name, active, models));
                }
                catch (ConfigurationException e) { output.WriteLine($"Servidor {name}: identidad incierta; {e.Message}"); }
            }
            return result;
        });
        using var owned = client is null ? new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false }) : null;
        client ??= owned!;
        foreach (var snapshot in snapshots)
        {
            var instance = snapshot.Instance;
            output.WriteLine($"Instancia {instance.InstanceId:D}; servidor {snapshot.Name}; PID {instance.Pid}; {instance.Host}:{instance.Port}; {instance.Mode}.");
            if (!instance.Metrics) { output.WriteLine("Métricas desactivadas."); continue; }
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            var host = instance.Host is "0.0.0.0" ? "127.0.0.1" : instance.Host is "::" ? "::1" : instance.Host;
            var address = new UriBuilder("http", host, instance.Port).Uri;
            try
            {
                using var models = JsonDocument.Parse(Fetch(client, new(address, "models"), deadline.Token));
                var statuses = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var model in models.RootElement.GetProperty("data").EnumerateArray())
                    statuses.Add(model.GetProperty("id").GetString()!, model.GetProperty("status").GetProperty("value").GetString()!);
                foreach (var model in snapshot.Models)
                {
                    if (!statuses.TryGetValue(model, out var status)) { output.WriteLine($"Modelo {model}: ausente en router."); continue; }
                    if (status != "loaded") { output.WriteLine($"Modelo {model}: {status}; sin métricas (modelo no cargado)."); continue; }
                    try
                    {
                        var metrics = ParseMetrics(Fetch(client, new(address, $"metrics?model={Uri.EscapeDataString(model)}&autoload=false"), deadline.Token));
                        output.WriteLine($"Modelo {model}: " + string.Join("; ", metrics.Select(pair => $"{pair.Key}={pair.Value.ToString(CultureInfo.InvariantCulture)}")));
                    }
                    catch (Exception e) when (MetricError(e)) { output.WriteLine($"Modelo {model}: métricas no disponibles: {e.Message}"); }
                }
                if (snapshot.Models.Length == 0) output.WriteLine("Diagnóstico: presets de la ejecución no disponibles; sin métricas.");
            }
            catch (Exception e) when (MetricError(e)) { output.WriteLine($"Métricas no disponibles: {e.Message}"); }
        }
        if (snapshots.Count == 0) output.WriteLine("Sin instancias activas verificadas.");
    }
    private static bool MetricError(Exception e) => e is HttpRequestException or OperationCanceledException or JsonException or InvalidOperationException or KeyNotFoundException or FormatException or IOException or ArgumentException;
    private static string Fetch(HttpClient client, Uri uri, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        using var body = response.Content.ReadAsStreamAsync(token).GetAwaiter().GetResult();
        using var bytes = new MemoryStream(); var buffer = new byte[4096]; int read;
        while ((read = body.ReadAsync(buffer, token).AsTask().GetAwaiter().GetResult()) > 0)
        {
            if (bytes.Length + read > 1024 * 1024) throw new FormatException("Respuesta excede 1 MiB.");
            bytes.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(bytes.ToArray());
    }
    public static Dictionary<string, double> ParseMetrics(string text)
    {
        string[] wanted = ["llamacpp:prompt_tokens_seconds", "llamacpp:predicted_tokens_seconds", "llamacpp:tokens_predicted_total", "llamacpp:prompt_tokens_total"];
        var result = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim(); if (line.Length == 0 || line[0] == '#') continue;
            Match sample;
            try { sample = MetricSample().Match(line); }
            catch (RegexMatchTimeoutException) { throw new FormatException("Formato Prometheus excede el plazo de análisis."); }
            if (!sample.Success) throw new FormatException("Formato Prometheus inválido.");
            var key = sample.Groups[1].Value;
            if (!wanted.Contains(key)) continue;
            if (!double.TryParse(sample.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value) || value < 0 || !result.TryAdd(key, value))
                throw new FormatException("Métrica inválida o ambigua.");
        }
        if (result.Count == 0) throw new FormatException("Respuesta sin métricas reconocidas.");
        return result;
    }
    [GeneratedRegex("^([a-zA-Z_:][a-zA-Z0-9_:]*)(?:\\{(?:[a-zA-Z_][a-zA-Z0-9_]*=\"(?:[^\"\\\\]|\\\\.)*\"(?:, *[a-zA-Z_][a-zA-Z0-9_]*=\"(?:[^\"\\\\]|\\\\.)*\")*)?\\})?[ \\t]+([^ \\t]+)(?:[ \\t]+-?[0-9]+)?$", RegexOptions.CultureInvariant, 100)]
    private static partial Regex MetricSample();
}
