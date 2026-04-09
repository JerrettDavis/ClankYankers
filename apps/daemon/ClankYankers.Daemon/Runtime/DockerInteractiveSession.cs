using ClankYankers.Daemon.Contracts;
using ClankYankers.Remote.Contracts;
using Docker.DotNet;
using Docker.DotNet.Models;
using System.Text;
using System.Threading.Channels;

namespace ClankYankers.Daemon.Runtime;

internal sealed class DockerInteractiveSession : IDaemonInteractiveSession
{
    private readonly DockerClient _client;
    private readonly MultiplexedStream _stream;
    private readonly Channel<string> _output = Channel.CreateUnbounded<string>();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TaskCompletionSource<int?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly string _containerId;
    private readonly Task _pumpTask;

    private DockerInteractiveSession(string sessionId, string containerId, DockerClient client, MultiplexedStream stream)
    {
        SessionId = sessionId;
        _containerId = containerId;
        _client = client;
        _stream = stream;
        _pumpTask = Task.Run(PumpAsync);
    }

    public string SessionId { get; }

    public ChannelReader<string> Output => _output.Reader;

    public Task<int?> Completion => _completion.Task;

    public static async Task<IDaemonInteractiveSession> StartAsync(
        StartRemoteSessionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DockerEndpoint))
        {
            throw new InvalidOperationException("Docker executor requests must include a Docker endpoint.");
        }

        if (string.IsNullOrWhiteSpace(request.DockerImage))
        {
            throw new InvalidOperationException("Docker executor requests must include a Docker image.");
        }

        var client = new DockerClientConfiguration(new Uri(request.DockerEndpoint)).CreateClient();
        await EnsureImageAsync(client, request.DockerImage, cancellationToken);

        var environment = request.Environment
            .Where(pair => pair.Value is not null)
            .Select(pair => $"{pair.Key}={pair.Value}")
            .ToList();

        string? containerId = null;

        try
        {
            var createResponse = await client.Containers.CreateContainerAsync(new CreateContainerParameters
            {
                Image = request.DockerImage,
                Tty = true,
                OpenStdin = true,
                AttachStdin = true,
                AttachStdout = true,
                AttachStderr = true,
                StdinOnce = false,
                WorkingDir = request.WorkingDirectory,
                Env = environment.Count > 0 ? environment : null,
                Cmd = [request.FileName, .. request.Arguments]
            }, cancellationToken);

            containerId = createResponse.ID;

            var started = await client.Containers.StartContainerAsync(containerId, new ContainerStartParameters(), cancellationToken);
            if (!started)
            {
                throw new InvalidOperationException($"Docker container '{containerId}' did not start.");
            }

            var stream = await client.Containers.AttachContainerAsync(containerId, true, new ContainerAttachParameters
            {
                Stream = true,
                Stdin = true,
                Stdout = true,
                Stderr = true
            }, cancellationToken);

            return new DockerInteractiveSession(request.SessionId, containerId, client, stream);
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(containerId))
            {
                try
                {
                    await client.Containers.RemoveContainerAsync(containerId, new ContainerRemoveParameters
                    {
                        Force = true
                    }, cancellationToken);
                }
                catch
                {
                }
            }

            client.Dispose();
            throw;
        }
    }

    public async Task WriteInputAsync(string data, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(data);
        await _stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
    }

    public Task ResizeAsync(int cols, int rows, CancellationToken cancellationToken) =>
        _client.Containers.ResizeContainerTtyAsync(
            _containerId,
            new ContainerResizeParameters
            {
                Width = cols,
                Height = rows
            },
            cancellationToken);

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _client.Containers.StopContainerAsync(_containerId, new ContainerStopParameters(), cancellationToken);
        }
        catch
        {
        }
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();

        try
        {
            await StopAsync(CancellationToken.None);
        }
        catch
        {
        }

        try
        {
            await _pumpTask;
        }
        catch
        {
        }

        try
        {
            await _client.Containers.RemoveContainerAsync(_containerId, new ContainerRemoveParameters
            {
                Force = true
            });
        }
        catch
        {
        }

        _stream.Dispose();
        _client.Dispose();
        _shutdown.Dispose();
    }

    private static async Task EnsureImageAsync(DockerClient client, string image, CancellationToken cancellationToken)
    {
        var images = await client.Images.ListImagesAsync(new ImagesListParameters { All = true }, cancellationToken);
        if (images.Any(imageSummary => imageSummary.RepoTags?.Contains(image, StringComparer.OrdinalIgnoreCase) == true))
        {
            return;
        }

        await client.Images.CreateImageAsync(
            new ImagesCreateParameters { FromImage = image },
            null,
            new Progress<JSONMessage>(),
            cancellationToken);
    }

    private async Task PumpAsync()
    {
        var buffer = new byte[4096];

        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                var result = await _stream.ReadOutputAsync(buffer, 0, buffer.Length, _shutdown.Token);
                if (result.EOF)
                {
                    break;
                }

                var data = Encoding.UTF8.GetString(buffer, 0, result.Count);
                await _output.Writer.WriteAsync(data, _shutdown.Token);
            }

            var waitResponse = await _client.Containers.WaitContainerAsync(_containerId, cancellationToken: CancellationToken.None);
            _completion.TrySetResult((int)waitResponse.StatusCode);
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            _completion.TrySetResult(null);
        }
        catch (ObjectDisposedException) when (_shutdown.IsCancellationRequested)
        {
            _completion.TrySetResult(null);
        }
        catch (Exception exception)
        {
            _completion.TrySetException(exception);
            _output.Writer.TryComplete(exception);
        }
        finally
        {
            _output.Writer.TryComplete();
        }
    }
}
