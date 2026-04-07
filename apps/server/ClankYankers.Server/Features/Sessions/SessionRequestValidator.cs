using ClankYankers.Server.Core.Models;

namespace ClankYankers.Server.Features.Sessions;

public static class SessionRequestValidator
{
    private static readonly HashSet<string> SupportedClaudePermissionModes = new(StringComparer.OrdinalIgnoreCase)
    {
        "default",
        "acceptEdits",
        "plan",
        "auto",
        "dontAsk",
        "bypassPermissions"
    };

    public static IDictionary<string, string[]> Validate(CreateSessionRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.Cols is < 24 or > 240)
        {
            errors["cols"] = ["Columns must be between 24 and 240."];
        }

        if (request.Rows is < 24 or > 240)
        {
            errors["rows"] = ["Rows must be between 24 and 240."];
        }

        if (request.BackplaneId is not null && string.IsNullOrWhiteSpace(request.BackplaneId))
        {
            errors["backplaneId"] = ["Backplane id cannot be blank."];
        }

        if (request.HostId is not null && string.IsNullOrWhiteSpace(request.HostId))
        {
            errors["hostId"] = ["Host id cannot be blank."];
        }

        if (request.ConnectorId is not null && string.IsNullOrWhiteSpace(request.ConnectorId))
        {
            errors["connectorId"] = ["Connector id cannot be blank."];
        }

        if (request.WorkingDirectory is not null && string.IsNullOrWhiteSpace(request.WorkingDirectory))
        {
            errors["workingDirectory"] = ["Workspace folder cannot be blank."];
        }

        if (request.Model is not null && string.IsNullOrWhiteSpace(request.Model))
        {
            errors["model"] = ["Model override cannot be blank."];
        }

        if (request.PermissionMode is not null && string.IsNullOrWhiteSpace(request.PermissionMode))
        {
            errors["permissionMode"] = ["Permission mode cannot be blank."];
        }
        else if (!string.IsNullOrWhiteSpace(request.PermissionMode) &&
                 !SupportedClaudePermissionModes.Contains(request.PermissionMode.Trim()))
        {
            errors["permissionMode"] = [$"Unsupported permission mode '{request.PermissionMode.Trim()}'."];
        }

        if (request.Agent is not null && string.IsNullOrWhiteSpace(request.Agent))
        {
            errors["agent"] = ["Agent selection cannot be blank."];
        }

        if (request.AllowedTools is not null && request.AllowedTools.Any(tool => string.IsNullOrWhiteSpace(tool)))
        {
            errors["allowedTools"] = ["Allowed tools cannot contain blank entries."];
        }

        return errors;
    }

    public static IDictionary<string, string[]> ValidateResolved(BackplaneDefinition backplane, LaunchSpec launchSpec)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(launchSpec.WorkingDirectory))
        {
            return errors;
        }

        var workingDirectory = launchSpec.WorkingDirectory.Trim();
        if (backplane.Kind.Equals("local", StringComparison.OrdinalIgnoreCase))
        {
            if (!Path.IsPathFullyQualified(workingDirectory))
            {
                errors["workingDirectory"] = ["Local workspace folder must be an absolute path."];
            }
            else if (!Directory.Exists(workingDirectory))
            {
                errors["workingDirectory"] = [$"Local workspace folder '{workingDirectory}' does not exist."];
            }

            return errors;
        }

        if (backplane.Kind.Equals("docker", StringComparison.OrdinalIgnoreCase) &&
            !IsAbsoluteContainerPath(workingDirectory))
        {
            errors["workingDirectory"] = ["Docker workspace folder must be an absolute path such as /workspace."];
        }

        return errors;
    }

    private static bool IsAbsoluteContainerPath(string workingDirectory) =>
        workingDirectory.StartsWith("/", StringComparison.Ordinal);
}
