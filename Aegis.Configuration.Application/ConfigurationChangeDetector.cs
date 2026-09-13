namespace Aegis.Configuration.Application;

public sealed record ConfigurationValueChange(string RequirementId, bool PresenceChanged, string Description);
public sealed record SafeSnapshotDiff(string ApplicationId, DateTimeOffset ComparedAtUtc, IReadOnlyList<ConfigurationValueChange> Changes);

public sealed class ConfigurationChangeDetector
{
    public SafeSnapshotDiff Compare(SafeConfigurationSnapshot previous, SafeConfigurationSnapshot current)
    {
        Dictionary<string, SafeRequirementSnapshot> old = previous.Requirements.ToDictionary(x => x.RequirementId, StringComparer.OrdinalIgnoreCase);
        List<ConfigurationValueChange> changes = [];
        foreach (SafeRequirementSnapshot now in current.Requirements)
        {
            if (!old.TryGetValue(now.RequirementId, out SafeRequirementSnapshot? before))
            {
                changes.Add(new(now.RequirementId, true, "New configuration requirement detected."));
                continue;
            }

            if (before.ValuePresent != now.ValuePresent)
            {
                changes.Add(new(now.RequirementId, true, now.ValuePresent
                    ? "Configuration is now reported as configured."
                    : "Configuration is no longer reported as configured."));
            }
        }
        return new(current.ApplicationId, DateTimeOffset.UtcNow, changes);
    }
}
