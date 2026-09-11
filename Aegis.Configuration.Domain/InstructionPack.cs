namespace Aegis.Configuration.Domain;

public sealed record ConfigurationInstructionPack(
    string ApplicationId,
    string ApplicationVersion,
    string PackVersion,
    DateTimeOffset PublishedAtUtc,
    IReadOnlyList<ConfigurationInstruction> Instructions);

public sealed record ConfigurationInstruction(
    string Id,
    string Title,
    string Summary,
    IReadOnlyList<string> Steps,
    string? DocumentationUrl = null);

public sealed record CommissioningSnapshot(
    string ApplicationId,
    string ApplicationVersion,
    DateTimeOffset EvaluatedAtUtc,
    ConfigurationEvaluation Evaluation);
