using ClankYankers.Server.Core.Models;
using Renci.SshNet;
using Renci.SshNet.Common;

namespace ClankYankers.Server.Infrastructure.Ssh;

internal static class SshConnectionFactory
{
    public static SshClient CreateClient(HostConfig host)
    {
        var address = host.SshAddress?.Trim();
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new InvalidOperationException($"Host '{host.Id}' is missing an SSH address.");
        }

        var username = host.SshUsername?.Trim();
        if (string.IsNullOrWhiteSpace(username))
        {
            throw new InvalidOperationException($"Host '{host.Id}' is missing an SSH username.");
        }

        var authMethods = BuildAuthenticationMethods(host, username);
        if (authMethods.Count == 0)
        {
            throw new InvalidOperationException($"Host '{host.Id}' is missing SSH authentication settings.");
        }

        var connectionInfo = new Renci.SshNet.ConnectionInfo(address, host.SshPort ?? 22, username, authMethods.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        var client = new SshClient(connectionInfo)
        {
            KeepAliveInterval = TimeSpan.FromSeconds(15)
        };

        client.HostKeyReceived += (_, eventArgs) =>
        {
            eventArgs.CanTrust = CanTrustHostKey(host, eventArgs);
        };

        return client;
    }

    private static List<AuthenticationMethod> BuildAuthenticationMethods(HostConfig host, string username)
    {
        var authMethods = new List<AuthenticationMethod>();
        var password = string.IsNullOrWhiteSpace(host.SshPassword) ? null : host.SshPassword;

        if (!string.IsNullOrWhiteSpace(password))
        {
            authMethods.Add(new PasswordAuthenticationMethod(username, password));
        }

        if (!string.IsNullOrWhiteSpace(host.SshPrivateKeyPath))
        {
            var privateKey = string.IsNullOrWhiteSpace(host.SshCertificatePath)
                ? new PrivateKeyFile(host.SshPrivateKeyPath, host.SshPrivateKeyPassphrase)
                : new PrivateKeyFile(host.SshPrivateKeyPath, host.SshPrivateKeyPassphrase, host.SshCertificatePath);

            authMethods.Add(new PrivateKeyAuthenticationMethod(username, privateKey));
        }

        if (host.SshUseKeyboardInteractive)
        {
            var keyboardInteractive = new KeyboardInteractiveAuthenticationMethod(username);
            keyboardInteractive.AuthenticationPrompt += (_, eventArgs) =>
            {
                foreach (var prompt in eventArgs.Prompts)
                {
                    if (password is not null &&
                        prompt.Request.Contains("password", StringComparison.OrdinalIgnoreCase))
                    {
                        prompt.Response = password;
                    }
                }
            };

            authMethods.Add(keyboardInteractive);
        }

        return authMethods;
    }

    private static bool CanTrustHostKey(HostConfig host, HostKeyEventArgs eventArgs)
    {
        if (host.SshAllowAnyHostKey)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(host.SshTrustedCaFingerprint) &&
            string.Equals(
                NormalizeFingerprint(host.SshTrustedCaFingerprint),
                NormalizeFingerprint(eventArgs.Certificate?.CertificateAuthorityKeyFingerPrint),
                StringComparison.Ordinal))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(host.SshHostKeyFingerprint))
        {
            var expected = NormalizeFingerprint(host.SshHostKeyFingerprint);
            return string.Equals(expected, NormalizeFingerprint(eventArgs.FingerPrintMD5), StringComparison.Ordinal)
                || string.Equals(expected, NormalizeFingerprint(eventArgs.FingerPrintSHA256), StringComparison.Ordinal);
        }

        return false;
    }

    private static string NormalizeFingerprint(string? value) =>
        (value ?? string.Empty)
            .Trim()
            .Replace("SHA256:", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("MD5:", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace(":", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
}
