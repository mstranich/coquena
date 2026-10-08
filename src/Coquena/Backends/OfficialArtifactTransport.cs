using System.Net;
using Coquena.Contracts;
using Coquena.Downloads;

namespace Coquena.Backends;

// Redirects are explicit, bounded and unauthenticated. Never carry ambient credentials.
public sealed class OfficialArtifactTransport : IDownloadTransport, IDisposable
{
    private readonly HttpClient metadata = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { Timeout = TimeSpan.FromSeconds(20) };
    private readonly HttpDownloadTransport transfer;
    public OfficialArtifactTransport()
    {
        transfer = new(new Redirects(), ResolveAsync);
    }
    private async Task<Uri> ResolveAsync(OperationDocument operation, int file, CancellationToken cancellation)
    {
        var selected = operation.BackendSelection ?? throw new DownloadException("invalid-selection", "Backend requerido.");
        try
        {
            var release = await new OfficialBackendSource(metadata).PinnedAsync(selected.Release, cancellation);
            foreach (var expected in selected.Artifacts)
            {
                var match = release.Assets.SingleOrDefault(a => a.Name == expected.Path);
                if (match is null || expected.Size != match.Size) throw new DownloadException("artifact-changed", "Artefacto fijado ausente o tamaño cambiado.");
            }
            return new(release.Assets.Single(a => a.Name == operation.Files[file].SourcePath).Url);
        }
        catch (BackendException e) { throw new DownloadException(e.Code, "No se pudo resolver el artefacto fijado."); }
    }
    public Task<HttpResponseMessage> OpenAsync(OperationDocument operation, int file, long offset, CancellationToken cancellation)
        => transfer.OpenAsync(operation, file, offset, cancellation);
    public void Dispose() { transfer.Dispose(); metadata.Dispose(); }

    private sealed class Redirects : HttpMessageHandler
    {
        private readonly HttpMessageInvoker sender = new(new HttpClientHandler
            { AllowAutoRedirect = false, UseCookies = false, AutomaticDecompression = DecompressionMethods.None });
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellation)
        {
            var uri = request.RequestUri!;
            for (var hop = 0; hop <= 5; hop++)
            {
                if (uri.Scheme != "https" || !(uri.Host == "github.com" || uri.Host.EndsWith(".githubusercontent.com", StringComparison.Ordinal)) ||
                    !string.IsNullOrEmpty(uri.UserInfo)) throw new DownloadException("unsafe-redirect", "Destino de descarga no permitido.");
                using var next = new HttpRequestMessage(HttpMethod.Get, uri);
                foreach (var header in request.Headers) next.Headers.TryAddWithoutValidation(header.Key, header.Value);
                var result = await sender.SendAsync(next, cancellation);
                if ((int)result.StatusCode is not (301 or 302 or 303 or 307 or 308)) return result;
                var location = result.Headers.Location;
                result.Dispose();
                if (location is null || hop == 5) throw new DownloadException("redirect-limit", "Redirección inválida o límite excedido.");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
            }
            throw new DownloadException("redirect-limit", "Límite excedido.");
        }
        protected override void Dispose(bool disposing) { if (disposing) sender.Dispose(); base.Dispose(disposing); }
    }
}
