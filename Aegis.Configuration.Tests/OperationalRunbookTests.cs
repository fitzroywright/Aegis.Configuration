using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Tests;

public sealed class OperationalRunbookTests
{
    [Fact]
    public void RenderOperationalRunbook_IncludesIdentityAndExcludesSecretValues()
    {
        ApplicationConfigurationContract contract = new(
            "Aegis.Hello",
            "Aegis Hello",
            "1.0.0",
            [
                new ConfigurationRequirement(
                    "secret",
                    "Machine Credential",
                    ConfigurationRequirementKind.Secret,
                    true,
                    "Authenticates machine calls.",
                    SecretName: "diagnostics/machine/Aegis.Hello",
                    Sensitive: true)
            ],
            SiteId: "JM",
            InstanceId: "Production",
            Description: "Reference registration application.",
            LastRegisteredAtUtc: DateTimeOffset.UtcNow,
            RuntimeEnvironment: new RuntimeEnvironmentSnapshot(
                MachineName: "kratos",
                HostName: "kratos",
                HostingEnvironment: "Production"),
            Diagnostics: new DiagnosticsCapability(
                HealthPath: "/health",
                SecretName: "diagnostics/machine/Aegis.Hello"),
            Secrets: new SecretProviderDeclaration(
                Provider: "OpenBao",
                ManagementUrl: "https://openbao.ffpja.org",
                ConnectionStatus: "Healthy",
                CredentialStatus: "Configured"));

        ConfigurationEvaluation evaluation = new(
            contract.ApplicationId,
            contract.Version,
            [
                new RequirementEvaluation(
                    "secret",
                    "Machine Credential",
                    true,
                    ConfigurationState.Configured,
                    "Configured",
                    Purpose: "Authenticates machine calls.",
                    Sensitive: true,
                    EffectiveSource: "Common.Secrets")
            ]);

        string runbook = new CommissioningReportRenderer().RenderOperationalRunbook(
            contract,
            evaluation,
            DateTimeOffset.UtcNow);

        Assert.Contains("# Aegis Hello Operational Runbook", runbook);
        Assert.Contains("Instance: Production", runbook);
        Assert.Contains("Machine: kratos", runbook);
        Assert.Contains("diagnostics/machine/Aegis.Hello", runbook);
        Assert.Contains("Secret values: intentionally excluded", runbook);
        Assert.DoesNotContain("super-secret-value", runbook);
    }

    [Fact]
    public async Task RunbookArtifactStore_PersistsAndReadsGeneratedArtifact()
    {
        string directory = Path.Combine(Path.GetTempPath(), "aegis-runbook-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new RunbookArtifactStore(directory);
            RunbookArtifact artifact = await store.SaveAsync(
                "Aegis.Hello",
                "# test runbook",
                CancellationToken.None);

            Assert.Equal("Aegis.Hello.runbook.md", artifact.FileName);
            Assert.True(File.Exists(Path.Combine(directory, artifact.FileName)));
            Assert.Equal(
                "# test runbook",
                await store.ReadAsync("Aegis.Hello", CancellationToken.None));

            IReadOnlyList<RunbookArtifact> artifacts = await store.ListAsync(CancellationToken.None);
            Assert.Single(artifacts);
            Assert.Equal("Aegis.Hello", artifacts[0].ApplicationId);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }
}
