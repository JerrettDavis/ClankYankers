using System.Diagnostics;
using System.Net.Sockets;

namespace ClankYankers.Server.IntegrationTests.Support;

internal sealed class SshTestContainer : IAsyncDisposable
{
    private const string ImageTag = "clanky-ssh-fixture:latest";
    private static readonly SemaphoreSlim BuildGate = new(1, 1);

    private SshTestContainer(string authDirectory, string containerId, int port, string privateKeyPath, string certificatePath, string hostKeyFingerprint)
    {
        AuthDirectory = authDirectory;
        ContainerId = containerId;
        Port = port;
        PrivateKeyPath = privateKeyPath;
        CertificatePath = certificatePath;
        HostKeyFingerprint = hostKeyFingerprint;
    }

    public static string Username => "clanky";

    public static string Password => "clanky-password";

    public string AuthDirectory { get; }

    public string ContainerId { get; }

    public int Port { get; }

    public string PrivateKeyPath { get; }

    public string CertificatePath { get; }

    public string HostKeyFingerprint { get; }

    public static bool IsAvailable() =>
        TerminalTestHelpers.DockerAvailable()
        && ToolAvailable("ssh-keygen")
        && ToolAvailable("ssh-keyscan");

    public static async Task<SshTestContainer> StartAsync()
    {
        if (!IsAvailable())
        {
            throw new InvalidOperationException("SSH test container prerequisites are not available.");
        }

        await EnsureImageAsync();

        var authDirectory = Path.Combine(Path.GetTempPath(), $"clanky-ssh-auth-{Guid.NewGuid():N}");
        Directory.CreateDirectory(authDirectory);
        await GenerateAuthArtifactsAsync(authDirectory);

        var containerId = (await RunProcessAsync(
            "docker",
            $"run -d -p 127.0.0.1::22 -v \"{authDirectory}:/test-auth\" -e SSH_TEST_USER={Username} -e SSH_TEST_PASSWORD={Password} {ImageTag}",
            throwOnFailure: true)).Trim();
        var portOutput = (await RunProcessAsync("docker", $"port {containerId} 22/tcp", throwOnFailure: true)).Trim();
        var port = ParsePublishedPort(portOutput);

        await WaitForPortAsync(port);
        var hostKeyFingerprint = await ReadHostKeyFingerprintAsync(authDirectory, port);

        return new SshTestContainer(
            authDirectory,
            containerId,
            port,
            Path.Combine(authDirectory, "id_ed25519"),
            Path.Combine(authDirectory, "id_ed25519-cert.pub"),
            hostKeyFingerprint);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await RunProcessAsync("docker", $"rm -f {ContainerId}", throwOnFailure: false);
        }
        finally
        {
            if (Directory.Exists(AuthDirectory))
            {
                Directory.Delete(AuthDirectory, recursive: true);
            }
        }
    }

    private static bool ToolAvailable(string fileName)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = "-V",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            process!.WaitForExit(2000);
            return process.ExitCode == 0 || process.ExitCode == 1;
        }
        catch
        {
            return false;
        }
    }

    private static async Task EnsureImageAsync()
    {
        await BuildGate.WaitAsync();
        try
        {
            var inspectExitCode = await RunProcessExitCodeAsync("docker", $"image inspect {ImageTag}");
            if (inspectExitCode == 0)
            {
                return;
            }

            var dockerfileDirectory = Path.Combine(FindRepositoryRoot(), "tests", "support", "ssh");
            await RunProcessAsync("docker", $"build -t {ImageTag} \"{dockerfileDirectory}\"", throwOnFailure: true);
        }
        finally
        {
            BuildGate.Release();
        }
    }

    private static async Task GenerateAuthArtifactsAsync(string authDirectory)
    {
        var privateKeyPath = Path.Combine(authDirectory, "id_ed25519");
        var caKeyPath = Path.Combine(authDirectory, "ssh_ca");

        await RunProcessAsync("ssh-keygen", $"-q -t ed25519 -N \"\" -f \"{privateKeyPath}\"", throwOnFailure: true);
        await RunProcessAsync("ssh-keygen", $"-q -t ed25519 -N \"\" -f \"{caKeyPath}\"", throwOnFailure: true);
        await RunProcessAsync(
            "ssh-keygen",
            $"-q -s \"{caKeyPath}\" -I clanky-test -n {Username} -V +52w \"{privateKeyPath}.pub\"",
            throwOnFailure: true);

        File.Copy($"{privateKeyPath}.pub", Path.Combine(authDirectory, "authorized_keys"), overwrite: true);
        File.Copy($"{caKeyPath}.pub", Path.Combine(authDirectory, "trusted_user_ca_keys.pub"), overwrite: true);
    }

    private static async Task<string> ReadHostKeyFingerprintAsync(string authDirectory, int port)
    {
        var knownHostPath = Path.Combine(authDirectory, "hostkey.scan");
        var keyscanOutput = await RunProcessAsync("ssh-keyscan", $"-p {port} 127.0.0.1", throwOnFailure: true);
        await File.WriteAllTextAsync(knownHostPath, keyscanOutput);

        var fingerprintOutput = await RunProcessAsync("ssh-keygen", $"-lf \"{knownHostPath}\" -E sha256", throwOnFailure: true);
        var firstLine = fingerprintOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()
            ?? throw new InvalidOperationException("ssh-keygen did not return a host fingerprint.");
        var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            throw new InvalidOperationException($"Unable to parse ssh-keygen fingerprint output: {firstLine}");
        }

        return parts[1];
    }

    private static async Task WaitForPortAsync(int port)
    {
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (DateTime.UtcNow < deadline)
        {
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync("127.0.0.1", port);
                return;
            }
            catch
            {
                await Task.Delay(250);
            }
        }

        throw new TimeoutException($"Timed out waiting for SSH container port {port}.");
    }

    private static int ParsePublishedPort(string portOutput)
    {
        var separatorIndex = portOutput.LastIndexOf(':');
        if (separatorIndex < 0 || !int.TryParse(portOutput[(separatorIndex + 1)..], out var port))
        {
            throw new InvalidOperationException($"Unable to parse published SSH port from '{portOutput}'.");
        }

        return port;
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

    private static async Task<int> RunProcessExitCodeAsync(string fileName, string arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static async Task<string> RunProcessAsync(string fileName, string arguments, bool throwOnFailure)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (throwOnFailure && process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Command '{fileName} {arguments}' failed with exit code {process.ExitCode}.{Environment.NewLine}{stderr}{stdout}");
        }

        return string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
    }
}
