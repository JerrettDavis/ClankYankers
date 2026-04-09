using ClankYankers.Remote.Contracts;
using ClankYankers.Server.Core.Contracts;
using ClankYankers.Server.Core.Models;
using System.Buffers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace ClankYankers.Server.Infrastructure.RemoteDaemon;

internal sealed class RemoteDaemonInteractiveSession : IInteractiveSession
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly ClientWebSocket _webSocket;
    private readonly Channel<TerminalOutputChunk> _output = Channel.CreateUnbounded<TerminalOutputChunk>();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TaskCompletionSource<int?> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Task _pumpTask;

    public RemoteDaemonInteractiveSession(string sessionId, HttpClient httpClient, ClientWebSocket webSocket)
    {
        SessionId = sessionId;
        _httpClient = httpClient;
        _webSocket = webSocket;
        _pumpTask = Task.Run(PumpAsync);
    }

    public string SessionId { get; }

    public ChannelReader<TerminalOutputChunk> Output => _output.Reader;

    public Task<int?> Completion => _completion.Task;

    public async Task WriteInputAsync(string data, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"/api/sessions/{SessionId}/input",
            new RemoteSessionInputRequest(data),
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task ResizeAsync(int cols, int rows, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsJsonAsync(
            $"/api/sessions/{SessionId}/resize",
            new RemoteSessionResizeRequest(cols, rows),
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsync($"/api/sessions/{SessionId}/stop", content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async ValueTask DisposeAsync()
    {
        if (!_completion.Task.IsCompleted)
        {
            try
            {
                using var stopCts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                using var stopResponse = await _httpClient.PostAsync($"/api/sessions/{SessionId}/stop", content: null, stopCts.Token);
                _ = stopResponse.IsSuccessStatusCode;
            }
            catch
            {
            }
        }

        if (!_pumpTask.IsCompleted)
        {
            await Task.WhenAny(_pumpTask, Task.Delay(TimeSpan.FromSeconds(3)));
        }

        _shutdown.Cancel();

        try
        {
            if (_webSocket.State == WebSocketState.Open)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "disposing", CancellationToken.None);
            }
        }
        catch
        {
        }

        _webSocket.Dispose();
        _httpClient.Dispose();

        try
        {
            await _pumpTask;
        }
        catch
        {
        }

        _shutdown.Dispose();
    }

    private async Task PumpAsync()
    {
        var buffer = new byte[4096];
        var messageBuffer = new ArrayBufferWriter<byte>();

        try
        {
            while (!_shutdown.IsCancellationRequested && _webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                var result = await _webSocket.ReceiveAsync(buffer, _shutdown.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                messageBuffer.Write(buffer.AsSpan(0, result.Count));
                if (!result.EndOfMessage)
                {
                    continue;
                }

                var envelope = JsonSerializer.Deserialize<RemoteSessionEnvelope>(messageBuffer.WrittenSpan, SerializerOptions);
                messageBuffer.Clear();

                if (envelope is null)
                {
                    continue;
                }

                switch (envelope.Type)
                {
                    case "output" when envelope.Data is not null:
                        await _output.Writer.WriteAsync(new TerminalOutputChunk(envelope.Data), _shutdown.Token);
                        break;
                    case "exit":
                        _completion.TrySetResult(envelope.ExitCode);
                        _output.Writer.TryComplete();
                        return;
                    case "error":
                        var error = new InvalidOperationException(envelope.Error ?? "Remote daemon reported an error.");
                        _completion.TrySetException(error);
                        _output.Writer.TryComplete(error);
                        return;
                }
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
