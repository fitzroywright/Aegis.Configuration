using System.Security.Cryptography;
using System.Text;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Application;

public sealed record SafeRequirementSnapshot(string RequirementId, ConfigurationRequirementKind Kind, ConfigurationState State, bool ValuePresent, string? ValueFingerprint);
public sealed record SafeConfigurationSnapshot(string ApplicationId, string ApplicationVersion, DateTimeOffset CapturedAtUtc, IReadOnlyList<SafeRequirementSnapshot> Requirements);

public sealed class SafeConfigurationSnapshotFactory(IConfigurationValueResolver resolver)
{
    public async ValueTask<SafeConfigurationSnapshot> CaptureAsync(ApplicationConfigurationContract contract, ConfigurationEvaluation evaluation, CancellationToken cancellationToken = default)
    {
        Dictionary<string, RequirementEvaluation> results = evaluation.Requirements.ToDictionary(x => x.RequirementId, StringComparer.OrdinalIgnoreCase);
        List<SafeRequirementSnapshot> snapshots = [];
        foreach (ConfigurationRequirement requirement in contract.Requirements)
        {
            string? value = await resolver.ResolveAsync(requirement, cancellationToken);
            bool present = !string.IsNullOrWhiteSpace(value);
            bool mayFingerprint = requirement.Kind != ConfigurationRequirementKind.Secret && requirement.FingerprintSafe;
            string? fingerprint = mayFingerprint && present ? Fingerprint(value!) : null;
            ConfigurationState state = results.TryGetValue(requirement.Id, out RequirementEvaluation? result) ? result.State : ConfigurationState.Missing;
            snapshots.Add(new(requirement.Id, requirement.Kind, state, present, fingerprint));
        }
        return new(contract.ApplicationId, contract.Version, DateTimeOffset.UtcNow, snapshots);
    }

    private static string Fingerprint(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}
