using ClankYankers.Remote.Contracts;
using ClankYankers.Server.Core.Contracts;
using ClankYankers.Server.Core.Models;
using ClankYankers.Server.Infrastructure.RemoteDaemon;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;

namespace ClankYankers.Server.Infrastructure.Backplanes;

public sealed class RemoteBackplane(ILogger<RemoteBackplane> logger) : IBackplane
{
    public string Kind => "remote";

    public async Task<IInteractiveSession> StartAsync(
        string sessionId,
        HostConfig host,
        LaunchSpec launchSpec,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(host.RemoteDaemonUrl))
        {
            throw new InvalidOperationException($"Host '{host.Id}' is missing a remote daemon URL.");
        }

        var daemonUri = new Uri(host.RemoteDaemonUrl, UriKind.Absolute);
        var httpClient = CreateHttpClient(host, daemonUri);
        RemoteSessionStartedResponse? started = null;
        ClientWebSocket? webSocket = null;

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                "/api/sessions",
                new StartRemoteSessionRequest
                {
                    SessionId = sessionId,
                    DisplayCommand = launchSpec.DisplayCommand,
                    FileName = launchSpec.FileName,
                    Arguments = launchSpec.Arguments,
                    WorkingDirectory = launchSpec.WorkingDirectory,
                    Environment = launchSpec.Environment,
                    Cols = launchSpec.Cols,
                    Rows = launchSpec.Rows,
                    ExecutorKind = host.RemoteExecutorKind ?? "process",
                    DockerEndpoint = host.RemoteDockerEndpoint,
                    DockerImage = host.RemoteDockerImage
                },
                cancellationToken);
            response.EnsureSuccessStatusCode();

            started = await response.Content.ReadFromJsonAsync<RemoteSessionStartedResponse>(cancellationToken);
            if (started is null)
            {
                throw new InvalidOperationException("Remote daemon did not return session startup metadata.");
            }

            webSocket = CreateWebSocket(host, daemonUri);
            await webSocket.ConnectAsync(ToWebSocketUri(daemonUri, started.StreamPath), cancellationToken);

            logger.LogInformation("Started remote daemon session {SessionId} against {DaemonUrl}", sessionId, daemonUri);
            return new RemoteDaemonInteractiveSession(sessionId, httpClient, webSocket);
        }
        catch
        {
            webSocket?.Dispose();

            if (started is not null)
            {
                try
                {
                    using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    using var stopResponse = await httpClient.PostAsync($"/api/sessions/{started.SessionId}/stop", content: null, stopCts.Token);
                    _ = stopResponse.IsSuccessStatusCode;
                }
                catch
                {
                }
            }

            httpClient.Dispose();
            throw;
        }
    }

    private static HttpClient CreateHttpClient(HostConfig host, Uri baseUri)
    {
        var handler = new HttpClientHandler();
        if (host.RemoteAllowInsecureTls)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        var client = new HttpClient(handler)
        {
            BaseAddress = baseUri
        };

        if (!string.IsNullOrWhiteSpace(host.RemoteAccessToken))
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", host.RemoteAccessToken);
        }

        return client;
    }

    private static ClientWebSocket CreateWebSocket(HostConfig host, Uri baseUri)
    {
        var socket = new ClientWebSocket();

        if (!string.IsNullOrWhiteSpace(host.RemoteAccessToken))
        {
            socket.Options.SetRequestHeader("Authorization", $"Bearer {host.RemoteAccessToken}");
        }

        if (host.RemoteAllowInsecureTls)
        {
            socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        }

        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        socket.Options.SetRequestHeader("Origin", $"{baseUri.Scheme}://{baseUri.Authority}");
        return socket;
    }

    private static Uri ToWebSocketUri(Uri baseUri, string streamPath)
    {
        var absoluteUri = new Uri(baseUri, streamPath);
        var builder = new UriBuilder(absoluteUri)
        {
            Scheme = absoluteUri.Scheme.ToLowerInvariant() switch
            {
                "https" => "wss",
                "http" => "ws",
                _ => throw new InvalidOperationException($"Unsupported daemon URL scheme '{absoluteUri.Scheme}'. Use http:// or https://.")
            }
        };

        return builder.Uri;
    }
}
