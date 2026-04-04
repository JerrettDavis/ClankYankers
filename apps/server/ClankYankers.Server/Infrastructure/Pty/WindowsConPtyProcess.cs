using ClankYankers.Server.Core.Contracts;
using ClankYankers.Server.Core.Models;
using Pty.Net;
using System.Text;
using System.Threading.Channels;

namespace ClankYankers.Server.Infrastructure.Pty;

internal sealed class WindowsConPtyProcess : IInteractiveSession
{
    private readonly IPtyConnection _connection;
    private readonly Channel<TerminalOutputChunk> _output = Channel.CreateUnbounded<TerminalOutputChunk>();
    private readonly TaskCompletionSource<int?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _pumpTask;
    private readonly Task _exitMonitorTask;
    private int _stopped;

    private WindowsConPtyProcess(
        string sessionId,
        IPtyConnection connection)
    {
        SessionId = sessionId;
        _connection = connection;
        _connection.ProcessExited += OnProcessExited;
        _pumpTask = Task.Run(PumpOutputAsync);
        _exitMonitorTask = Task.Run(MonitorExitAsync);
    }

    public string SessionId { get; }

    public ChannelReader<TerminalOutputChunk> Output => _output.Reader;

    public Task<int?> Completion => _completion.Task;

    public static Task<IInteractiveSession> StartAsync(
        string sessionId,
        LaunchSpec launchSpec,
        CancellationToken cancellationToken) =>
        StartInternalAsync(sessionId, launchSpec, cancellationToken);

    private static async Task<IInteractiveSession> StartInternalAsync(
        string sessionId,
        LaunchSpec launchSpec,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("ConPTY local sessions currently require Windows.");
        }

        var environment = launchSpec.Environment
            .Where(pair => pair.Value is not null)
            .ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.OrdinalIgnoreCase);

        var connection = await PtyProvider.SpawnAsync(new PtyOptions
        {
            Name = sessionId,
            Rows = launchSpec.Rows,
            Cols = launchSpec.Cols,
            Cwd = launchSpec.WorkingDirectory ?? Environment.CurrentDirectory,
            App = launchSpec.FileName,
            CommandLine = [.. launchSpec.Arguments],
            VerbatimCommandLine = false,
            ForceWinPty = false,
            Environment = environment
        }, cancellationToken);

        return new WindowsConPtyProcess(sessionId, connection);
    }

    public async Task WriteInputAsync(string data, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var bytes = Encoding.UTF8.GetBytes(data);
        await _connection.WriterStream.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
        await _connection.WriterStream.FlushAsync(cancellationToken);
    }

    public Task ResizeAsync(int cols, int rows, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _connection.Resize(cols, rows);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1)
        {
            await WaitForCompletionAsync(cancellationToken);
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            _connection.Kill();
        }
        catch (InvalidOperationException) when (_completion.Task.IsCompleted)
        {
        }

        await WaitForCompletionAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();

        try
        {
            if (Interlocked.Exchange(ref _stopped, 1) == 0 && !_completion.Task.IsCompleted)
            {
                _connection.Kill();
            }
        }
        catch (InvalidOperationException)
        {
        }

        TryDisposeConnection();

        try
        {
            await Task.WhenAll(_pumpTask, _exitMonitorTask);
        }
        catch
        {
            // Shutdown should not throw from disposal.
        }

        _output.Writer.TryComplete();
        _shutdown.Dispose();
    }

    private async Task PumpOutputAsync()
    {
        var buffer = new byte[4096];

        try
        {
            while (true)
            {
                using var readCts = CreateReadCancellationTokenSource();
                var read = await _connection.ReaderStream.ReadAsync(buffer, 0, buffer.Length, readCts.Token);
                if (read == 0)
                {
                    break;
                }

                var data = Encoding.UTF8.GetString(buffer, 0, read);
                await _output.Writer.WriteAsync(new TerminalOutputChunk(data), CancellationToken.None);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (OperationCanceledException) when (_completion.Task.IsCompleted)
        {
        }
        catch (ObjectDisposedException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (_completion.Task.IsCompleted)
        {
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

    private void OnProcessExited(object? sender, EventArgs args)
    {
        TrySetCompletion();
    }

    private async Task MonitorExitAsync()
    {
        try
        {
            while (!_shutdown.IsCancellationRequested && !_completion.Task.IsCompleted)
            {
                if (_connection.WaitForExit(250))
                {
                    TrySetCompletion();
                    return;
                }

                await Task.Delay(50, _shutdown.Token);
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _completion.TrySetException(exception);
            _output.Writer.TryComplete(exception);
        }
    }

    private void TrySetCompletion()
    {
        try
        {
            _completion.TrySetResult(_connection.ExitCode);
        }
        catch (InvalidOperationException) when (_shutdown.IsCancellationRequested)
        {
            _completion.TrySetResult(null);
        }
        catch (ObjectDisposedException) when (_shutdown.IsCancellationRequested)
        {
            _completion.TrySetResult(null);
        }
    }

    private void TryDisposeConnection()
    {
        try
        {
            _connection.Dispose();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private CancellationTokenSource CreateReadCancellationTokenSource()
    {
        var readCts = CancellationTokenSource.CreateLinkedTokenSource(_shutdown.Token);
        if (_completion.Task.IsCompleted)
        {
            readCts.CancelAfter(TimeSpan.FromMilliseconds(100));
        }

        return readCts;
    }

    private async Task WaitForCompletionAsync(CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            await _completion.Task.WaitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"Timed out waiting for PTY session '{SessionId}' to stop.");
        }
    }
}
