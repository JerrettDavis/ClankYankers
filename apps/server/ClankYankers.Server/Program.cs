using ClankYankers.Server.Core.Contracts;
using ClankYankers.Server.Features.Config;
using ClankYankers.Server.Features.Sessions;
using ClankYankers.Server.Infrastructure.Backplanes;
using ClankYankers.Server.Infrastructure.Config;
using ClankYankers.Server.Infrastructure.Connectors;
using ClankYankers.Server.Infrastructure.Observability;
using ClankYankers.Server.Infrastructure.Pty;
using ClankYankers.Server.Infrastructure.Registry;

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
});

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy
            .WithOrigins(
                "http://localhost:5173",
                "http://127.0.0.1:5173",
                "http://localhost:4173",
                "http://127.0.0.1:4173")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials());
});

builder.Services.AddSingleton<IConfigStore, JsonConfigStore>();
builder.Services.AddSingleton<IEventBus, InMemoryEventBus>();
builder.Services.AddSingleton<SessionAuditLogger>();
builder.Services.AddSingleton<IPtyProcessFactory, WindowsConPtyProcessFactory>();
builder.Services.AddSingleton<IBackplane, LocalBackplane>();
builder.Services.AddSingleton<IBackplane, DockerBackplane>();
builder.Services.AddSingleton<IAgentConnector, ShellConnector>();
builder.Services.AddSingleton<IAgentConnector, OllamaConnector>();
builder.Services.AddSingleton<IAgentConnector, ClaudeConnector>();
builder.Services.AddSingleton<BackplaneRegistry>();
builder.Services.AddSingleton<ConnectorRegistry>();
builder.Services.AddSingleton<SessionRegistry>();
builder.Services.AddSingleton<SessionOrchestrator>();

var app = builder.Build();

app.UseCors();
app.UseWebSockets();

_ = app.Services.GetRequiredService<SessionAuditLogger>();

var api = app.MapGroup("/api");
api.MapGet("/health", () => Results.Ok(new { status = "ok" }));
ConfigEndpoints.Map(api);
SessionEndpoints.Map(api);

app.MapGet("/", () => Results.Ok(new { name = "ClankYankers.Server" }));
app.Map("/ws/session/{sessionId}", SessionWebSocketHandler.HandleAsync);

app.Run();

public partial class Program;
