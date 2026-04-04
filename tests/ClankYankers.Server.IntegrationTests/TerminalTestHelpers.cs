using ClankYankers.Server.Core.Models;
using System.Diagnostics;
using System.Threading.Channels;

namespace ClankYankers.Server.IntegrationTests;

internal static class TerminalTestHelpers
{
    public static async Task<string> ReadUntilContainsAsync(
        ChannelReader<TerminalOutputChunk> reader,
        string expected,
        TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        var output = string.Empty;

        while (stopwatch.Elapsed < timeout)
        {
            while (reader.TryRead(out var chunk))
            {
                output += chunk.Data;
                if (output.Contains(expected, StringComparison.OrdinalIgnoreCase))
                {
                    return output;
                }
            }

            var waitTask = reader.WaitToReadAsync(CancellationToken.None).AsTask();
            var completed = await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromMilliseconds(100)));
            if (completed != waitTask)
            {
                continue;
            }

            if (!await waitTask)
            {
                break;
            }
        }

        throw new TimeoutException($"Timed out waiting for '{expected}'. Output so far:{Environment.NewLine}{output}");
    }

    public static bool DockerAvailable()
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "version --format {{.Server.Version}}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            process!.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
