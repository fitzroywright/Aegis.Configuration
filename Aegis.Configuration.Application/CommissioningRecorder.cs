using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Application;

public sealed class CommissioningRecorder(ICommissioningHistoryStore historyStore)
{
    public async ValueTask<CommissioningHistoryEntry> RecordAsync(CommissioningSnapshot snapshot, CancellationToken cancellationToken = default)
    {
        CommissioningHistoryEntry entry = new(Guid.NewGuid(), DateTimeOffset.UtcNow, snapshot.ApplicationId, snapshot.ApplicationVersion, snapshot.Evaluation.IsOperational, snapshot.Evaluation.Requirements);
        await historyStore.AppendAsync(entry, cancellationToken);
        return entry;
    }
}
