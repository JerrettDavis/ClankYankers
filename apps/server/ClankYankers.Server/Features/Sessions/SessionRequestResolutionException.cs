namespace ClankYankers.Server.Features.Sessions;

public sealed class SessionRequestResolutionException : InvalidOperationException
{
    public SessionRequestResolutionException(IDictionary<string, string[]> errors)
        : base(errors.Values.SelectMany(messages => messages).FirstOrDefault() ?? "Session request resolution failed.")
    {
        Errors = new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
