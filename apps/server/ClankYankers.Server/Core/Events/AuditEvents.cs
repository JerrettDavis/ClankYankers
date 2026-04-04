namespace ClankYankers.Server.Core.Events;

public abstract record AuditEvent(string EventType, DateTimeOffset Timestamp);

public sealed record SessionLifecycleEvent(
    string SessionId,
    string State,
    string BackplaneId,
    string HostId,
    string ConnectorId,
    DateTimeOffset Timestamp,
    int? ExitCode = null,
    string? Error = null) : AuditEvent("session.lifecycle", Timestamp);

public sealed record CommandExecutionEvent(
    string SessionId,
    string DisplayCommand,
    DateTimeOffset Timestamp) : AuditEvent("command.execution", Timestamp);

public sealed record RuntimeErrorEvent(
    string SessionId,
    string Message,
    DateTimeOffset Timestamp) : AuditEvent("runtime.error", Timestamp);
