namespace Aegis.Configuration.Application;

public sealed record ConfigurationValueChange(string RequirementId, bool PresenceChanged, bool FingerprintChanged, string Description);
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
                changes.Add(new(now.RequirementId, true, now.ValueFingerprint is not null, "New configuration requirement detected."));
                continue;
            }
            bool presence = before.ValuePresent != now.ValuePresent;
            bool fingerprint = !string.Equals(before.ValueFingerprint, now.ValueFingerprint, StringComparison.Ordinal);
            if (presence || fingerprint)
            {
                string description = now.Kind == Domain.ConfigurationRequirementKind.Secret
                    ? "Secret availability changed; secret value was not captured."
                    : "Configuration value changed; only its fingerprint was compared.";
                changes.Add(new(now.RequirementId, presence, fingerprint, description));
            }
        }
        return new(current.ApplicationId, DateTimeOffset.UtcNow, changes);
    }
}
