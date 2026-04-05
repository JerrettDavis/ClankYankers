using ClankYankers.Server.Core.Models;

namespace ClankYankers.Server.Features.Sessions;

public static class SessionRequestValidator
{
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

        return errors;
    }
}
