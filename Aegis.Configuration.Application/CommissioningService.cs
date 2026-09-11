using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Application;

public interface IConfigurationContractStore
{
    ValueTask<IReadOnlyList<ApplicationConfigurationContract>> LoadAllAsync(CancellationToken cancellationToken = default);
}

public sealed class CommissioningService(
    IConfigurationContractStore contractStore,
    ConfigurationEvaluator evaluator)
{
    public async ValueTask<IReadOnlyList<CommissioningSnapshot>> EvaluateAllAsync(
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<ApplicationConfigurationContract> contracts = await contractStore.LoadAllAsync(cancellationToken);
        List<CommissioningSnapshot> snapshots = [];

        foreach (ApplicationConfigurationContract contract in contracts)
        {
            ConfigurationEvaluation evaluation = await evaluator.EvaluateAsync(contract, cancellationToken);
            snapshots.Add(new(contract.ApplicationId, contract.Version, DateTimeOffset.UtcNow, evaluation));
        }

        return snapshots;
    }
}
