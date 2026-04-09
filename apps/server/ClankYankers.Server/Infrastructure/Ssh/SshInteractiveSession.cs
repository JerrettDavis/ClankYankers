using ClankYankers.Server.Core.Contracts;
using ClankYankers.Server.Core.Models;
using Renci.SshNet;
using System.Text;
using System.Threading.Channels;

namespace ClankYankers.Server.Infrastructure.Ssh;

internal sealed class SshInteractiveSession : IInteractiveSession
{
    private readonly SshClient _client;
    private readonly ShellStream _shellStream;
    private readonly Channel<TerminalOutputChunk> _output = Channel.CreateUnbounded<TerminalOutputChunk>();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TaskCompletionSource<int?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _pumpTask;

    public SshInteractiveSession(string sessionId, SshClient client, ShellStream shellStream)
    {
        SessionId = sessionId;
        _client = client;
        _shellStream = shellStream;
        _pumpTask = Task.Run(PumpAsync);
    }

    public string SessionId { get; }

    public ChannelReader<TerminalOutputChunk> Output => _output.Reader;

    public Task<int?> Completion => _completion.Task;

    public async Task InitializeAsync(HostConfig host, LaunchSpec launchSpec, CancellationToken cancellationToken)
    {
        var bootstrapCommand = SshBootstrapCommandBuilder.Build(host, launchSpec);
        if (string.IsNullOrEmpty(bootstrapCommand))
        {
            return;
        }

        await WriteInputAsync(bootstrapCommand, cancellationToken);
    }

    public async Task WriteInputAsync(string data, CancellationToken cancellationToken)
    {
        var buffer = Encoding.UTF8.GetBytes(data);
        await _shellStream.WriteAsync(buffer, cancellationToken);
        await _shellStream.FlushAsync(cancellationToken);
    }

    public Task ResizeAsync(int cols, int rows, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _shellStream.ChangeWindowSize((uint)cols, (uint)rows, 0, 0);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _shutdown.Cancel();

        if (_client.IsConnected)
        {
            _client.Disconnect();
        }

        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();

        try
        {
            if (_client.IsConnected)
            {
                _client.Disconnect();
            }
        }
        catch
        {
            // Disconnection failures are surfaced by the session pump.
        }

        try
        {
            await _pumpTask;
        }
        catch
        {
            // Completion is already published to the output and completion task.
        }

        _shellStream.Dispose();
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
                var bytesRead = await _shellStream.ReadAsync(buffer, _shutdown.Token);
                if (bytesRead == 0)
                {
                    break;
                }

                var chunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                await _output.Writer.WriteAsync(new TerminalOutputChunk(chunk), _shutdown.Token);
            }

            _completion.TrySetResult(null);
            _output.Writer.TryComplete();
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
            _completion.TrySetResult(null);
            _output.Writer.TryComplete();
        }
        catch (Exception exception)
        {
            _completion.TrySetException(exception);
            _output.Writer.TryComplete(exception);
        }
    }
}
