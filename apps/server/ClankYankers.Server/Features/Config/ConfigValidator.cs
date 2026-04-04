using ClankYankers.Server.Core.Models;

namespace ClankYankers.Server.Features.Config;

public static class ConfigValidator
{
    public static IDictionary<string, string[]> Validate(AppConfig config)
    {
        var errors = new Dictionary<string, string[]>();

        if (config.Backplanes.Count == 0)
        {
            errors["backplanes"] = ["At least one backplane is required."];
        }

        if (config.Hosts.Count == 0)
        {
            errors["hosts"] = ["At least one host is required."];
        }

        var backplaneIds = config.Backplanes.Select(backplane => backplane.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unknownBackplanes = config.Hosts
            .Where(host => !backplaneIds.Contains(host.BackplaneId))
            .Select(host => host.Id)
            .ToArray();

        if (unknownBackplanes.Length > 0)
        {
            errors["hosts.backplaneId"] = [$"Unknown backplane references: {string.Join(", ", unknownBackplanes)}"];
        }

        var duplicateHostIds = config.Hosts
            .GroupBy(host => host.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicateHostIds.Length > 0)
        {
            errors["hosts.id"] = [$"Duplicate host ids: {string.Join(", ", duplicateHostIds)}"];
        }

        return errors;
    }
}
