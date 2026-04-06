using ClankYankers.Server.Core.Contracts;
using ClankYankers.Server.Core.Models;
using Docker.DotNet;
using Docker.DotNet.Models;
using System.Text;
using System.Threading.Channels;
using AppHostConfig = ClankYankers.Server.Core.Models.HostConfig;

namespace ClankYankers.Server.Infrastructure.Backplanes;

public sealed class DockerBackplane(ILogger<DockerBackplane> logger) : IBackplane
{
    public string Kind => "docker";

    public async Task<IInteractiveSession> StartAsync(
        string sessionId,
        AppHostConfig host,
        LaunchSpec launchSpec,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(host.DockerEndpoint))
        {
            throw new InvalidOperationException($"Host '{host.Id}' is missing a Docker endpoint.");
        }

        if (string.IsNullOrWhiteSpace(host.DockerImage))
        {
            throw new InvalidOperationException($"Host '{host.Id}' is missing a Docker image.");
        }

        var client = new DockerClientConfiguration(new Uri(host.DockerEndpoint)).CreateClient();
        await EnsureImageAsync(client, host.DockerImage, cancellationToken);

        var createResponse = await client.Containers.CreateContainerAsync(new CreateContainerParameters
        {
            Image = host.DockerImage,
            Tty = true,
            OpenStdin = true,
            AttachStdin = true,
            AttachStdout = true,
            AttachStderr = true,
            StdinOnce = false,
            WorkingDir = host.WorkingDirectory,
            Cmd = [launchSpec.FileName, .. launchSpec.Arguments]
        }, cancellationToken);

        var started = await client.Containers.StartContainerAsync(createResponse.ID, new ContainerStartParameters(), cancellationToken);
        if (!started)
        {
            throw new InvalidOperationException($"Docker container '{createResponse.ID}' did not start.");
        }

        var stream = await client.Containers.AttachContainerAsync(createResponse.ID, true, new ContainerAttachParameters
        {
            Stream = true,
            Stdin = true,
            Stdout = true,
            Stderr = true
        }, cancellationToken);

        logger.LogInformation("Started Docker session {SessionId} in container {ContainerId}", sessionId, createResponse.ID);
        return new DockerInteractiveSession(sessionId, createResponse.ID, client, stream, launchSpec);
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

    private sealed class DockerInteractiveSession : IInteractiveSession
    {
        private readonly DockerClient _client;
        private readonly MultiplexedStream _stream;
        private readonly Channel<TerminalOutputChunk> _output = Channel.CreateUnbounded<TerminalOutputChunk>();
        private readonly CancellationTokenSource _shutdown = new();
        private readonly TaskCompletionSource<int?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _pumpTask;
        private readonly string _containerId;
        private readonly LaunchSpec _launchSpec;

        public DockerInteractiveSession(
            string sessionId,
            string containerId,
            DockerClient client,
            MultiplexedStream stream,
            LaunchSpec launchSpec)
        {
            SessionId = sessionId;
            _containerId = containerId;
            _client = client;
            _stream = stream;
            _launchSpec = launchSpec;
            _pumpTask = Task.Run(PumpAsync);
        }

        public string SessionId { get; }

        public ChannelReader<TerminalOutputChunk> Output => _output.Reader;

        public Task<int?> Completion => _completion.Task;

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
                // Docker can report already-stopped here; cleanup still proceeds below.
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
                // Best-effort cleanup on shutdown.
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
                // Best-effort cleanup on shutdown.
            }

            _stream.Dispose();
            _client.Dispose();
            _shutdown.Dispose();
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
                    await _output.Writer.WriteAsync(new TerminalOutputChunk(data), _shutdown.Token);
                }

                var waitResponse = await _client.Containers.WaitContainerAsync(_containerId, cancellationToken: CancellationToken.None);
                _completion.TrySetResult((int)waitResponse.StatusCode);
                _output.Writer.TryComplete();
            }
            catch (Exception exception)
            {
                _completion.TrySetException(exception);
                _output.Writer.TryComplete(exception);
            }
        }
    }
}
