using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;

namespace ClankYankers.Server.IntegrationTests;

public sealed class SessionEndpointTests
{
    [Fact]
    public async Task Post_sessions_rejects_relative_local_working_directory_overrides()
    {
        using var harness = new SessionFactoryHarness();
        using var client = harness.Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/sessions", new
        {
            backplaneId = "local",
            hostId = "local-host",
            connectorId = "shell",
            workingDirectory = "relative-path",
            cols = 120,
            rows = 34
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(["Local workspace folder must be an absolute path."], problem.Errors["workingDirectory"]);
    }

    [Fact]
    public async Task Post_sessions_rejects_relative_docker_working_directory_overrides()
    {
        using var harness = new SessionFactoryHarness();
        using var client = harness.Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/sessions", new
        {
            backplaneId = "docker",
            hostId = "docker-local",
            connectorId = "shell",
            workingDirectory = "workspace",
            cols = 120,
            rows = 34
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(["Docker workspace folder must be an absolute path such as /workspace."], problem.Errors["workingDirectory"]);
    }

    [Fact]
    public async Task Post_sessions_rejects_windows_style_docker_working_directory_overrides()
    {
        using var harness = new SessionFactoryHarness();
        using var client = harness.Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/sessions", new
        {
            backplaneId = "docker",
            hostId = "docker-local",
            connectorId = "shell",
            workingDirectory = @"C:\workspace",
            cols = 120,
            rows = 34
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(["Docker workspace folder must be an absolute path such as /workspace."], problem.Errors["workingDirectory"]);
    }

    [Fact]
    public async Task Post_sessions_rejects_blank_permission_mode_overrides()
    {
        using var harness = new SessionFactoryHarness();
        using var client = harness.Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/sessions", new
        {
            backplaneId = "local",
            hostId = "local-host",
            connectorId = "shell",
            permissionMode = "   ",
            cols = 120,
            rows = 34
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(["Permission mode cannot be blank."], problem.Errors["permissionMode"]);
    }

    [Fact]
    public async Task Post_sessions_rejects_blank_allowed_tool_entries()
    {
        using var harness = new SessionFactoryHarness();
        using var client = harness.Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/sessions", new
        {
            backplaneId = "local",
            hostId = "local-host",
            connectorId = "shell",
            allowedTools = new[] { "Read", "  " },
            cols = 120,
            rows = 34
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(["Allowed tools cannot contain blank entries."], problem.Errors["allowedTools"]);
    }

    [Fact]
    public async Task Post_sessions_rejects_unknown_permission_modes()
    {
        using var harness = new SessionFactoryHarness();
        using var client = harness.Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/sessions", new
        {
            backplaneId = "local",
            hostId = "local-host",
            connectorId = "claude",
            permissionMode = "shipIt",
            cols = 120,
            rows = 34
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(["Unsupported permission mode 'shipIt'."], problem.Errors["permissionMode"]);
    }

    [Fact]
    public async Task Post_sessions_rejects_unknown_claude_agents()
    {
        using var harness = new SessionFactoryHarness();
        using var client = harness.Factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/sessions", new
        {
            backplaneId = "local",
            hostId = "local-host",
            connectorId = "claude",
            agent = "missing-agent",
            cols = 120,
            rows = 34
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(["Unknown Claude agent 'missing-agent'."], problem.Errors["agent"]);
    }

    private sealed class SessionFactoryHarness : IDisposable
    {
        private readonly string webRootPath = Path.Combine(Path.GetTempPath(), $"clanky-sessions-{Guid.NewGuid():N}");

        public SessionFactoryHarness()
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
