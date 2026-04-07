using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ClankYankers.Server.IntegrationTests;

public sealed class SpaHostingTests
{
    [Fact]
    public async Task Production_root_and_deep_links_resolve_to_the_spa_shell()
    {
        using var harness = new SpaFactoryHarness();
        using var client = harness.Factory.CreateClient();

        var rootResponse = await client.GetAsync("/");
        rootResponse.EnsureSuccessStatusCode();

        Assert.Equal("text/html", rootResponse.Content.Headers.ContentType?.MediaType);
        var rootMarkup = await rootResponse.Content.ReadAsStringAsync();
        Assert.Contains("<div id=\"root\">", rootMarkup, StringComparison.Ordinal);

        var deepLinkResponse = await client.GetAsync("/workspace");
        deepLinkResponse.EnsureSuccessStatusCode();

        Assert.Equal("text/html", deepLinkResponse.Content.Headers.ContentType?.MediaType);
        var deepLinkMarkup = await deepLinkResponse.Content.ReadAsStringAsync();
        Assert.Contains("ClankYankers SPA shell", deepLinkMarkup, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Production_spa_fallback_preserves_api_routes()
    {
        using var harness = new SpaFactoryHarness();
        using var client = harness.Factory.CreateClient();

        var response = await client.GetAsync("/api/health");
        response.EnsureSuccessStatusCode();

        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        var payload = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"status\":\"ok\"", payload, StringComparison.Ordinal);

        var missingApiResponse = await client.GetAsync("/api/does-not-exist");
        Assert.Equal(HttpStatusCode.NotFound, missingApiResponse.StatusCode);

        var missingSocketResponse = await client.GetAsync("/ws/missing");
        Assert.Equal(HttpStatusCode.NotFound, missingSocketResponse.StatusCode);
    }

    private sealed class SpaFactoryHarness : IDisposable
    {
        private readonly string webRootPath = Path.Combine(Path.GetTempPath(), $"clanky-spa-{Guid.NewGuid():N}");

        public SpaFactoryHarness()
        {
            Directory.CreateDirectory(webRootPath);
            File.WriteAllText(
                Path.Combine(webRootPath, "index.html"),
                """
                <!doctype html>
                <html lang="en">
                <head>
                  <meta charset="utf-8" />
                  <title>ClankYankers SPA shell</title>
                </head>
                <body>
                  <div id="root"></div>
                </body>
                </html>
                """);

            Factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Production");
                    builder.UseSetting(WebHostDefaults.WebRootKey, webRootPath);
                });
        }

        public WebApplicationFactory<Program> Factory { get; }

        public void Dispose()
        {
            Factory.Dispose();

            if (Directory.Exists(webRootPath))
            {
                Directory.Delete(webRootPath, recursive: true);
            }
        }
    }
}
