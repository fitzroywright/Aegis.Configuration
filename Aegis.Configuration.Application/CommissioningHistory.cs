using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Application;

public sealed record CommissioningHistoryEntry(
    Guid Id,
    DateTimeOffset RecordedAtUtc,
    string ApplicationId,
    string ApplicationVersion,
    bool IsComplete,
    IReadOnlyList<RequirementEvaluation> Requirements);

public interface ICommissioningHistoryStore
{
    ValueTask AppendAsync(CommissioningHistoryEntry entry, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<CommissioningHistoryEntry>> ReadAsync(string? applicationId = null, CancellationToken cancellationToken = default);
}
