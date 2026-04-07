namespace ClankYankers.Server.Features.Sessions;

public sealed class SessionLaunchValidationException : InvalidOperationException
{
    public SessionLaunchValidationException(IDictionary<string, string[]> errors)
        : base(errors.Values.SelectMany(messages => messages).FirstOrDefault() ?? "Session launch validation failed.")
    {
        Errors = new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
