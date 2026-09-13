using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Application;

public sealed record SafeRequirementSnapshot(string RequirementId, ConfigurationRequirementKind Kind, ConfigurationState State, bool ValuePresent);
public sealed record SafeConfigurationSnapshot(string ApplicationId, string ApplicationVersion, DateTimeOffset CapturedAtUtc, IReadOnlyList<SafeRequirementSnapshot> Requirements);

public sealed class SafeConfigurationSnapshotFactory(IConfigurationValueResolver resolver)
{
    public async ValueTask<SafeConfigurationSnapshot> CaptureAsync(ApplicationConfigurationContract contract, ConfigurationEvaluation evaluation, CancellationToken cancellationToken = default)
    {
        Dictionary<string, RequirementEvaluation> results = evaluation.Requirements.ToDictionary(x => x.RequirementId, StringComparer.OrdinalIgnoreCase);
        List<SafeRequirementSnapshot> snapshots = [];
        foreach (ConfigurationRequirement requirement in contract.Requirements)
        {
            bool present;
            if (requirement.IsConfigured.HasValue)
            {
                present = requirement.IsConfigured.Value;
            }
            else
            {
                string? value = await resolver.ResolveAsync(requirement, cancellationToken);
                present = !string.IsNullOrWhiteSpace(value);
            }

            ConfigurationState state = results.TryGetValue(requirement.Id, out RequirementEvaluation? result)
                ? result.State
                : requirement.Required ? ConfigurationState.Missing : ConfigurationState.OptionalNotConfigured;
            snapshots.Add(new(requirement.Id, requirement.Kind, state, present));
        }
        return new(contract.ApplicationId, contract.Version, DateTimeOffset.UtcNow, snapshots);
    }
}
