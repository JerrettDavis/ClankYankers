using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.Sockets;
using ClankYankers.Remote.Contracts;

namespace ClankYankers.Server.IntegrationTests.Support;

internal sealed class RemoteDaemonProcessHarness : IAsyncDisposable
{
    private readonly Process _process;
    private readonly HttpClient _httpClient;

    private RemoteDaemonProcessHarness(Process process, HttpClient httpClient, Uri baseUri, string? accessToken)
    {
        _process = process;
        _httpClient = httpClient;
        BaseUri = baseUri;
        AccessToken = accessToken;
    }

    public Uri BaseUri { get; }

    public string? AccessToken { get; }

    public static async Task<RemoteDaemonProcessHarness> StartAsync(string? accessToken = null)
    {
        var port = GetOpenPort();
        var baseUri = new Uri($"http://127.0.0.1:{port}");
        var daemonDllPath = ResolveDaemonDllPath();

        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{daemonDllPath}\" --urls {baseUri}",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            startInfo.Environment["CLANK_DAEMON_ACCESS_TOKEN"] = accessToken;
        }

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start the remote daemon process.");
        var httpClient = new HttpClient { BaseAddress = baseUri };
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
        }

        var harness = new RemoteDaemonProcessHarness(process, httpClient, baseUri, accessToken);
        await harness.WaitForReadyAsync();
        return harness;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
        }
        finally
        {
            _process.Dispose();
            _httpClient.Dispose();
        }
    }

    private async Task WaitForReadyAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var info = await _httpClient.GetFromJsonAsync<RemoteNodeInfoResponse>("/api/node/info");
                if (info is not null)
                {
                    return;
                }
            }
            catch
            {
                await Task.Delay(250);
            }
        }

        var stderr = await _process.StandardError.ReadToEndAsync();
        throw new TimeoutException($"Timed out waiting for remote daemon readiness.{Environment.NewLine}{stderr}");
    }

    private static int GetOpenPort()
    {
        using var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string ResolveDaemonDllPath()
    {
        var repositoryRoot = FindRepositoryRoot();
        var configuration = Environment.GetEnvironmentVariable("CLANK_TEST_BUILD_CONFIG") ?? "Release";
        var daemonDllPath = Path.Combine(
            repositoryRoot,
            "apps",
            "daemon",
            "ClankYankers.Daemon",
            "bin",
            configuration,
            "net10.0",
            "ClankYankers.Daemon.dll");

        if (!File.Exists(daemonDllPath))
        {
            throw new InvalidOperationException(
                $"Daemon binary not found at '{daemonDllPath}'. Build ClankYankers.Daemon in {configuration} first or set CLANK_TEST_BUILD_CONFIG.");
        }

        return daemonDllPath;
    }

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ClankYankers.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
