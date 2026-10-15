using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;
using Aegis.Configuration.Infrastructure;

// Registration is the inventory authority; contracts supply configuration details, never membership.
internal sealed class ConfigurationApplicationInventory(
    DirectoryContractStore store,
    ConfigurationEvaluator evaluator,
    LifecycleRegistrationAuthority authority,
    ILogger<ConfigurationApplicationInventory> logger)
{
    public async Task<List<(ApplicationConfigurationContract Contract, ConfigurationEvaluation Evaluation)>> LoadAsync(CancellationToken ct)
    {
        var contracts = new List<ApplicationConfigurationContract>();
        string? contractError = null;
        try { contracts.AddRange(await store.LoadAllAsync(ct)); }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Contract inventory could not be read; retaining registration inventory.");
            contractError = "Contract store unavailable. Check the contract directory permissions and Configuration logs.";
        }
        var publishedIds = contracts.Select(x => x.ApplicationId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<LifecycleRegistrationRecord> registrations = [];
        string? registrationError = null;
        try { registrations = await authority.ListAsync(ct); }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogError(e, "Registration inventory unavailable; retaining published applications.");
            registrationError = "Registration authority unavailable. Check the registration database connection and Configuration logs.";
        }
        IReadOnlyList<RetiredConfigurationContractInfo> retired = [];
        try { retired = await store.ListRetiredAsync(cancellationToken: ct); }
        catch (Exception e) when (e is not OperationCanceledException) { logger.LogError(e, "Contract history unavailable."); }
        foreach (var registration in registrations.Where(x => x.State == LifecycleRegistrationState.Registered))
        {
            if (contracts.Any(c => c.ApplicationId.Equals(registration.ApplicationId, StringComparison.OrdinalIgnoreCase))) continue;
            bool suspended = retired.Any(x => x.ApplicationId.Equals(registration.ApplicationId, StringComparison.OrdinalIgnoreCase));
            string? name = registration.Metadata?["displayName"]?.ToString();
            contracts.Add(new(registration.ApplicationId, name ?? registration.ApplicationId, "Unknown",
                suspended ? [] : [new("contract-unavailable", "Configuration contract unavailable", ConfigurationRequirementKind.Configuration,
                    true, (contractError ?? "Registered application has no active published contract. Inspect contract publication and registration logs; republish using its current registration."), ConfigurationState: "Unknown")],
                InstanceId: registration.InstanceId));
        }
        var rows = new List<(ApplicationConfigurationContract Contract, ConfigurationEvaluation Evaluation)>();
        foreach (var contract in contracts.OrderBy(x => x.DisplayName))
        {
            ConfigurationEvaluation evaluation;
            try { evaluation = await evaluator.EvaluateAsync(contract, ct); }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Configuration evaluation failed for {ApplicationId}", contract.ApplicationId);
                evaluation = new(contract.ApplicationId, contract.Version, [new("evaluation-failed", "Configuration evaluation", true,
                    ConfigurationState.Unknown, "Evaluation failed. Inspect Configuration logs for this application and retry.")]);
            }
            var records = registrations.Where(x => x.ApplicationId.Equals(contract.ApplicationId, StringComparison.OrdinalIgnoreCase)).ToArray();
            IReadOnlyList<LifecycleFlowJourneyView> journeys = [];
            string? detailError = null;
            try { journeys = LifecycleFlowSnapshotBuilder.Build(records, 500).Journeys; }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger.LogError(e, "Registration details could not be read for {ApplicationId}", contract.ApplicationId);
                detailError = "Registration details unavailable. Inspect this application's lifecycle metadata and Configuration logs.";
            }
            evaluation = evaluation with {
                RegistrationState = records.Length > 0 ? string.Join(", ", records.Select(x => x.InstanceId + ": " + x.State)) : registrationError ?? "No current registration observed",
                LifecycleHealth = journeys.Count > 0 ? string.Join(", ", journeys.Select(x => x.InstanceId + ": " + x.Health)) : "Unknown",
                EvaluationSuspended = !publishedIds.Contains(contract.ApplicationId) && retired.Any(x => x.ApplicationId.Equals(contract.ApplicationId, StringComparison.OrdinalIgnoreCase)),
                InventoryError = detailError ?? registrationError
            };
            rows.Add((contract, evaluation));
        }
        return rows;
    }
}
