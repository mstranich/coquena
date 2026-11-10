using System.Net;
using Coquena.Models;
using Coquena.Observation;
using Coquena.Projects;
using Coquena.Storage;

namespace Coquena.Tests;

internal static class ResourceDashboardTests
{
    public static async Task Run(Action<bool, string> check)
    {
        using var client = new HttpClient(new SearchFixture());
        var source = new HuggingFaceStore(client);
        var first = await source.SearchAsync("a & b");
        check(first.Repositories.SequenceEqual(["fixture/one"]) && first.Next is not null, "search returns a bounded page and validated continuation");
        var second = await source.SearchAsync("a & b", first.Next);
        check(second.Repositories.Length == 0 && second.Next is null, "empty final page is distinct from a failed search");
        try { await source.SearchAsync("a", new Uri("https://example.org/api/models")); check(false, "foreign continuation accepted"); }
        catch (ModelSourceException error) { check(error.Code == "invalid-pagination", "search rejects foreign continuation before network"); }
        var root = Path.Combine(Path.GetTempPath(), "coquena-resource-dashboard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var context = ScopeContext.Global(new Home(Path.Combine(root, "home")));
            var file = Path.Combine(root, "external.gguf");
            File.WriteAllBytes(file, ScopedModelTests.Gguf());
            var models = new ScopedModels(context);
            var added = models.AddExternal(file, "local");
            var inventory = DashboardCli.Inventory(context, false);
            check(inventory.Length == 1 && inventory[0].Id == added.InstallationId && inventory[0].External,
                "inventory captures external installation identity without a recipe");
            var page = DashboardCli.ResourcePage("Global", false, inventory, inventory[0].Id, "", 58, 19);
            var actions = page.Rows.Append(page.Title).SelectMany(Markup.Parse).Select(span => span.Action).OfType<string>().ToArray();
            check(actions.Contains("resource-verify") && actions.Contains("resource-remove") && actions.Contains("model-add") && actions.Contains("hf"),
                "inventory offers the complete non-YAML route");
            check(page.Rows.Count <= 19 && !actions.Contains("edit"), "80x24 inventory keeps detail and actions within the content area");
            var report = await new ResourceVerification(context).VerifyAsync([new("local", "model", added.ResourceId, added.InstallationId)], checksum: true);
            check(!report.Verified && report.Resources[0]!["status"]!.GetValue<string>() == "no-reference", "external without baseline is not certified by a successful read");
            models.Protect(added.InstallationId, false);
            var removal = models.Remove(added.ResourceId, false);
            check(removal.Complete && removal.FreedBytes == 0 && File.Exists(file), "explicit external removal keeps bytes and reports zero reclaimed");
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class SearchFixture : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            if (!uri.Query.Contains("cursor="))
            {
                if (!uri.Query.Contains("search=a%20%26%20b") || !uri.Query.Contains("filter=gguf")) throw new Exception("search query was not encoded");
                var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[{\"id\":\"fixture/one\"}]") };
                response.Headers.Add("Link", "<https://huggingface.co/api/models?filter=gguf&cursor=next&limit=20>; rel=\"next\"");
                return Task.FromResult(response);
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("[]") });
        }
    }
}
