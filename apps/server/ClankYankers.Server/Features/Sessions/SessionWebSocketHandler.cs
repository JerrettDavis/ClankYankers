using ClankYankers.Server.Core.Models;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace ClankYankers.Server.Features.Sessions;

public static class SessionWebSocketHandler
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);

    public static async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var orchestrator = context.RequestServices.GetRequiredService<SessionOrchestrator>();
        var sessionId = context.Request.RouteValues["sessionId"]?.ToString();

        if (string.IsNullOrWhiteSpace(sessionId) || !orchestrator.TryGet(sessionId, out var session) || session is null)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var subscription = session.Attach();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);

        var sendTask = SendLoopAsync(socket, subscription.Reader, linkedCts.Token);
        var receiveTask = ReceiveLoopAsync(socket, session, linkedCts.Token);

        await Task.WhenAny(sendTask, receiveTask);
        linkedCts.Cancel();
        session.Detach(subscription.Id);

        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Session detached", CancellationToken.None);
        }
    }

    private static async Task SendLoopAsync(
        WebSocket socket,
        System.Threading.Channels.ChannelReader<TerminalServerMessage> reader,
        CancellationToken cancellationToken)
    {
        await foreach (var message in reader.ReadAllAsync(cancellationToken))
        {
            var json = JsonSerializer.Serialize(message, JsonSerializerOptions);
            var buffer = Encoding.UTF8.GetBytes(json);
            await socket.SendAsync(buffer, WebSocketMessageType.Text, true, cancellationToken);
        }
    }

    private static async Task ReceiveLoopAsync(
        WebSocket socket,
        Session session,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[4096];

        while (!cancellationToken.IsCancellationRequested)
        {
            using var stream = new MemoryStream();
            WebSocketReceiveResult result;

            do
            {
                result = await socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                await stream.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken);
            }
            while (!result.EndOfMessage);

            var json = Encoding.UTF8.GetString(stream.GetBuffer(), 0, (int)stream.Length);
            var message = JsonSerializer.Deserialize<TerminalClientMessage>(json, JsonSerializerOptions);

            if (message is null)
            {
                continue;
            }

            switch (message.Type)
            {
                case "input" when message.Data is not null:
                    await session.WriteInputAsync(message.Data, cancellationToken);
                    break;
                case "resize" when message.Cols is not null && message.Rows is not null:
                    await session.ResizeAsync(message.Cols.Value, message.Rows.Value, cancellationToken);
                    break;
            }
        }
    }
}
