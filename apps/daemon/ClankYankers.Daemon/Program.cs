using ClankYankers.Daemon.Services;
using ClankYankers.Remote.Contracts;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var configuredAccessToken =
    builder.Configuration["AccessToken"]
    ?? builder.Configuration["CLANK_DAEMON_ACCESS_TOKEN"]
    ?? Environment.GetEnvironmentVariable("CLANK_DAEMON_ACCESS_TOKEN");

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
});
builder.Services.AddSingleton<DaemonSessionManager>();
builder.Services.AddSingleton<SelfUpdateService>();

var app = builder.Build();
app.UseWebSockets();
app.Use(async (context, next) =>
{
    if (string.IsNullOrWhiteSpace(configuredAccessToken))
    {
        await next();
        return;
    }

    var authorization = context.Request.Headers.Authorization.ToString();
    var expected = $"Bearer {configuredAccessToken}";
    var authorizationBytes = Encoding.UTF8.GetBytes(authorization);
    var expectedBytes = Encoding.UTF8.GetBytes(expected);
    if (authorizationBytes.Length != expectedBytes.Length
        || !CryptographicOperations.FixedTimeEquals(authorizationBytes, expectedBytes))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }

    await next();
});

app.MapGet("/api/node/info", () =>
{
    var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0";
    return Results.Ok(new RemoteNodeInfoResponse(version, ["process", "docker"], SupportsSelfUpdate: true, SupportsDocker: true));
});

app.MapPost("/api/sessions", async (
    StartRemoteSessionRequest request,
    DaemonSessionManager sessionManager,
    CancellationToken cancellationToken) =>
{
    var started = await sessionManager.StartAsync(request, cancellationToken);
    return Results.Created($"/api/sessions/{started.SessionId}", started);
});

app.MapPost("/api/sessions/{sessionId}/input", async (
    string sessionId,
    RemoteSessionInputRequest request,
    DaemonSessionManager sessionManager,
    CancellationToken cancellationToken) =>
{
    if (!sessionManager.TryGet(sessionId, out var session) || session is null)
    {
        return Results.NotFound();
    }

    await session.WriteInputAsync(request.Data, cancellationToken);
    return Results.Accepted();
});

app.MapPost("/api/sessions/{sessionId}/resize", async (
    string sessionId,
    RemoteSessionResizeRequest request,
    DaemonSessionManager sessionManager,
    CancellationToken cancellationToken) =>
{
    if (!sessionManager.TryGet(sessionId, out var session) || session is null)
    {
        return Results.NotFound();
    }

    await session.ResizeAsync(request.Cols, request.Rows, cancellationToken);
    return Results.Accepted();
});

app.MapPost("/api/sessions/{sessionId}/stop", async (
    string sessionId,
    DaemonSessionManager sessionManager,
    CancellationToken cancellationToken) =>
{
    if (!sessionManager.TryGet(sessionId, out var session) || session is null)
    {
        return Results.NotFound();
    }

    await session.StopAsync(cancellationToken);
    return Results.Accepted();
});

app.MapPost("/api/node/self-update", async (
    RemoteSelfUpdateRequest request,
    SelfUpdateService selfUpdateService,
    CancellationToken cancellationToken) =>
{
    await selfUpdateService.ScheduleAsync(request, cancellationToken);
    return Results.Accepted();
});

app.Map("/ws/session/{sessionId}", async (HttpContext context, string sessionId, DaemonSessionManager sessionManager) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    if (!sessionManager.TryGet(sessionId, out var session) || session is null)
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }

    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    var cancellationToken = context.RequestAborted;

    try
    {
        await foreach (var chunk in session.Output.ReadAllAsync(cancellationToken))
        {
            await SendEnvelopeAsync(socket, new RemoteSessionEnvelope
            {
                Type = "output",
                Data = chunk
            }, cancellationToken);
        }

        var exitCode = await session.Completion;
        await SendEnvelopeAsync(socket, new RemoteSessionEnvelope
        {
            Type = "exit",
            ExitCode = exitCode
        }, cancellationToken);
    }
    catch (Exception exception)
    {
        if (socket.State == WebSocketState.Open)
        {
            await SendEnvelopeAsync(socket, new RemoteSessionEnvelope
            {
                Type = "error",
                Error = exception.Message
            }, cancellationToken);
        }
    }
    finally
    {
        if (socket.State == WebSocketState.Open)
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "completed", CancellationToken.None);
        }
    }
});

app.Run();

static async Task SendEnvelopeAsync(WebSocket socket, RemoteSessionEnvelope envelope, CancellationToken cancellationToken)
{
    var payload = JsonSerializer.SerializeToUtf8Bytes(envelope);
    await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken);
}
