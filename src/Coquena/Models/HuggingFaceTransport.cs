using System.Net;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using Coquena.Contracts;
using Coquena.Downloads;

namespace Coquena.Models;

// Conventional HTTP/LFS-compatible route, even for Xet-backed files. Public access only.
public sealed class HuggingFaceTransport : IDownloadTransport, IDisposable
{
    private readonly HttpDownloadTransport transfer;
    public HuggingFaceTransport(HttpMessageHandler? handler = null)
    {
        transfer = new(new Redirects(handler), (operation, index, _) =>
        {
            var selected = operation.ModelSelection ?? throw new DownloadException("invalid-selection", "Selección fijada requerida.");
            HuggingFaceStore.ValidateRepository(selected.Repository);
            if (!HuggingFaceStore.Hex(selected.Commit, 40)) throw new DownloadException("invalid-commit", "Commit fijado requerido.");
            var path = selected.Files[index].Path; HuggingFaceStore.ValidatePath(path);
            return Task.FromResult(new Uri($"https://huggingface.co/{selected.Repository}/resolve/{selected.Commit}/{string.Join('/', path.Split('/').Select(Uri.EscapeDataString))}"));
        });
    }
    public Task<HttpResponseMessage> OpenAsync(OperationDocument operation, int file, long offset, CancellationToken cancellation)
        => transfer.OpenAsync(operation, file, offset, cancellation);
    public void Dispose() => transfer.Dispose();

    internal sealed class Redirects(HttpMessageHandler? handler, Action? onRequest = null) : HttpMessageHandler
    {
        private readonly HttpMessageInvoker sender = new(handler ?? new SocketsHttpHandler
        { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None, MaxResponseDrainSize = 0 });
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            var uri = request.RequestUri!;
            for (var hop = 0; hop <= 5; hop++)
            {
                if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 || uri.Fragment.Length != 0 ||
                    !(uri.Host == "huggingface.co" || uri.Host.EndsWith(".huggingface.co", StringComparison.Ordinal) ||
                      uri.Host == "hf.co" || uri.Host.EndsWith(".hf.co", StringComparison.Ordinal)))
                    throw new DownloadException("unsafe-redirect", "Destino HF/CDN no permitido.");
                using var next = new HttpRequestMessage(HttpMethod.Get, uri);
                // Explicit safe header set, no Authorization, cookies or ambient credentials.
                next.Headers.AcceptEncoding.ParseAdd("identity");
                next.Headers.Range = request.Headers.Range;
                next.Headers.IfRange = request.Headers.IfRange;
                onRequest?.Invoke();
                var response = await sender.SendAsync(next, cancellation);
                if (response.RequestMessage?.RequestUri is { } observed && observed != uri)
                { response.Dispose(); throw new DownloadException("unexpected-redirect", "Cliente siguió redirección automática."); }
                if (response.Headers.TryGetValues("X-Repo-Commit", out var commits) &&
                    commits.Any(c => c != request.RequestUri!.AbsolutePath.Split('/')[4]))
                { response.Dispose(); throw new DownloadException("content-changed", "Commit de respuesta no corresponde."); }
                if ((int)response.StatusCode is not (301 or 302 or 303 or 307 or 308))
                {
                    if (response.StatusCode == HttpStatusCode.TooManyRequests && response.Headers.TryGetValues("RateLimit", out var rates))
                    {
                        var resets = Regex.Matches(string.Join(",", rates), "(?:^|;)\\s*t=([0-9]+)(?=;|,|$)", RegexOptions.CultureInvariant);
                        if (resets.Count == 0) { response.Dispose(); throw new DownloadException("remote-rate-limit", "RateLimit no reconocido; no reintentar antes del reset."); }
                        var wait = response.Headers.RetryAfter?.Delta ??
                            (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.Zero);
                        foreach (Match reset in resets)
                        {
                            if (!int.TryParse(reset.Groups[1].Value, out var seconds))
                            { response.Dispose(); throw new DownloadException("remote-rate-limit", "Espera del servidor fuera de límites."); }
                            if (TimeSpan.FromSeconds(seconds) > wait) wait = TimeSpan.FromSeconds(seconds);
                        }
                        response.Headers.RetryAfter = new RetryConditionHeaderValue(wait < TimeSpan.Zero ? TimeSpan.Zero : wait);
                    }
                    return response;
                }
                var location = response.Headers.Location; response.Dispose();
                if (location is null || hop == 5) throw new DownloadException("redirect-limit", "Redirección no resuelta.");
                uri = location.IsAbsoluteUri ? location : new(uri, location);
            }
            throw new DownloadException("redirect-limit", "Redirecciones excedidas.");
        }
        protected override void Dispose(bool disposing) { if (disposing) sender.Dispose(); base.Dispose(disposing); }
    }
}
