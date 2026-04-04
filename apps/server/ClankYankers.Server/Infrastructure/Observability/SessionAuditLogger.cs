using ClankYankers.Server.Core.Contracts;
using ClankYankers.Server.Core.Events;
using System.Text.Json;

namespace ClankYankers.Server.Infrastructure.Observability;

public sealed class SessionAuditLogger
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly string _logPath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SessionAuditLogger(
        IHostEnvironment environment,
        IEventBus eventBus,
        ILogger<SessionAuditLogger> logger)
    {
        _logPath = Path.Combine(environment.ContentRootPath, "..", "..", "..", "data", "logs", "sessions.ndjson");
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);

        eventBus.Subscribe<SessionLifecycleEvent>(WriteAsync);
        eventBus.Subscribe<CommandExecutionEvent>(WriteAsync);
        eventBus.Subscribe<RuntimeErrorEvent>(WriteAsync);

        logger.LogInformation("Audit log initialized at {Path}", _logPath);
    }

    private async Task WriteAsync<TEvent>(TEvent eventData, CancellationToken cancellationToken)
    {
        var line = JsonSerializer.Serialize(eventData, SerializerOptions);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await File.AppendAllTextAsync(_logPath, line + Environment.NewLine, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }
}
