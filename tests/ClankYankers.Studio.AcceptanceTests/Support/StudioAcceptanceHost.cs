using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text;

namespace ClankYankers.Studio.AcceptanceTests.Support;

internal sealed class StudioAcceptanceHost : IAsyncDisposable
{
    private static readonly Uri ApiHealthUri = new("http://127.0.0.1:5023/api/health");
    private static readonly Uri WebUri = new("http://127.0.0.1:5173/");
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(3),
    };

    private Process? _apiProcess;
    private Process? _webProcess;
    private bool _ownsApiProcess;
    private bool _ownsWebProcess;
    private bool _started;

    public static StudioAcceptanceHost Instance { get; } = new();

    public static string BaseUrl => WebUri.ToString().TrimEnd('/');

    public async Task EnsureStartedAsync()
    {
        await Gate.WaitAsync();
        try
        {
            if (_started)
            {
                return;
            }

            var repoRoot = FindRepoRoot();
            if (!await IsHealthyAsync(ApiHealthUri))
            {
                _apiProcess = StartProcess(
                    fileName: "dotnet",
                    arguments: "run --project apps\\server\\ClankYankers.Server --urls http://127.0.0.1:5023",
                    workingDirectory: repoRoot);
                _ownsApiProcess = true;
            }

            await WaitForHealthyAsync(ApiHealthUri, "server API");

            if (!await IsHealthyAsync(WebUri))
            {
                _webProcess = StartProcess(
                    fileName: ResolveNpmCommand(),
                    arguments: "run dev -- --host 127.0.0.1 --port 5173",
                    workingDirectory: Path.Combine(repoRoot, "apps", "web"));
                _ownsWebProcess = true;
            }

            await WaitForHealthyAsync(WebUri, "web app");
            _started = true;
        }
        finally
        {
            Gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await Gate.WaitAsync();
        try
        {
            _started = false;
            await StopProcessAsync(_webProcess, _ownsWebProcess);
            await StopProcessAsync(_apiProcess, _ownsApiProcess);
            _webProcess = null;
            _apiProcess = null;
            _ownsWebProcess = false;
            _ownsApiProcess = false;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static Process StartProcess(string fileName, string arguments, string workingDirectory)
    {
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true,
        };

        var outputBuffer = new StringBuilder();
        process.OutputDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                outputBuffer.AppendLine(args.Data);
            }
        };
        process.ErrorDataReceived += (_, args) =>
        {
            if (!string.IsNullOrWhiteSpace(args.Data))
            {
                outputBuffer.AppendLine(args.Data);
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process: {fileName} {arguments}");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    private static async Task StopProcessAsync(Process? process, bool shouldStop)
    {
        if (process is null || !shouldStop)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
        catch (InvalidOperationException)
        {
        }
        finally
        {
            process.Dispose();
        }
    }

    private static async Task<bool> IsHealthyAsync(Uri uri)
    {
        try
        {
            using var response = await HttpClient.GetAsync(uri);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private static async Task WaitForHealthyAsync(Uri uri, string serviceName)
    {
        for (var attempt = 0; attempt < 120; attempt++)
        {
            if (await IsHealthyAsync(uri))
            {
                return;
            }

            await Task.Delay(1000);
        }

        throw new TimeoutException($"{serviceName} did not become ready at {uri}.");
    }

    private static string FindRepoRoot()
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

        throw new DirectoryNotFoundException("Could not locate the repository root from the test output directory.");
    }

    private static string ResolveNpmCommand()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return "npm.cmd";
        }

        return "npm";
    }
}
