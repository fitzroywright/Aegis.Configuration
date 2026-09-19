using System.Text;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Application;

public sealed class CommissioningReportRenderer
{
    public string RenderMarkdown(CommissioningSnapshot snapshot, ConfigurationInstructionPack? pack = null)
    {
        StringBuilder report = new();
        report.AppendLine($"# {snapshot.ApplicationId} Configuration Runbook");
        report.AppendLine();
        report.AppendLine($"- Application version: {snapshot.ApplicationVersion}");
        report.AppendLine($"- Generated UTC: {snapshot.EvaluatedAtUtc:O}");
        report.AppendLine($"- Configuration status: {(snapshot.Evaluation.IsComplete ? "COMPLETE" : "INCOMPLETE")}");
        if (pack is not null) report.AppendLine($"- Instruction pack: {pack.PackVersion}");
        report.AppendLine();
        report.AppendLine("## Configuration Requirements");
        report.AppendLine();
        report.AppendLine("| Requirement | Required? | Status | Purpose / Guidance |");
        report.AppendLine("| --- | --- | --- | --- |");
        foreach (RequirementEvaluation requirement in snapshot.Evaluation.Requirements)
        {
            string guidance = requirement.Purpose ?? requirement.Message;
            if (!string.IsNullOrWhiteSpace(requirement.Example) && !requirement.Sensitive)
                guidance += $" Example: {requirement.Example}";
            report.AppendLine($"| {Escape(requirement.DisplayName)} | {(requirement.Required ? "REQUIRED" : "OPTIONAL")} | {DisplayState(requirement.State)} | {Escape(guidance)} |");
        }
        report.AppendLine();
        report.AppendLine("## Administrator Procedure");
        report.AppendLine("1. Supply every setting marked REQUIRED.");
        report.AppendLine("2. Optional settings may be left unset when the related feature is not used.");
        report.AppendLine("3. Secret values are obtained from the declared secret provider and are never printed in this runbook.");
        report.AppendLine("4. Restart or reload the application as its deployment instructions require.");
        report.AppendLine("5. Return to Aegis.Configuration and confirm configuration status is COMPLETE.");
        report.AppendLine();
        report.AppendLine("## Administrator Sign-off");
        report.AppendLine("Administrator: ____________________");
        report.AppendLine("Date: ____________________");
        report.AppendLine("Notes: ____________________");
        report.AppendLine();
        report.AppendLine("> Secret values are intentionally excluded. This runbook describes configuration completeness only.");
        return report.ToString();
    }


    public string RenderOperationalRunbook(
        ApplicationConfigurationContract contract,
        ConfigurationEvaluation evaluation,
        DateTimeOffset generatedUtc)
    {
        StringBuilder report = new();
        report.AppendLine($"# {contract.DisplayName} Operational Runbook");
        report.AppendLine();
        report.AppendLine("> Generated automatically by Aegis.Configuration from the application's authenticated configuration contract. Secret values are never written to this artifact.");
        report.AppendLine();
        report.AppendLine("## Application Identity");
        report.AppendLine();
        report.AppendLine($"- Application ID: {contract.ApplicationId}");
        report.AppendLine($"- Version: {contract.Version}");
        report.AppendLine($"- Instance: {contract.InstanceId ?? "—"}");
        report.AppendLine($"- Site: {contract.SiteId ?? "—"}");
        report.AppendLine($"- Contract registered UTC: {contract.LastRegisteredAtUtc?.ToString("O") ?? "—"}");
        report.AppendLine($"- Runbook generated UTC: {generatedUtc:O}");
        report.AppendLine($"- Configuration status: {(evaluation.IsComplete ? "COMPLETE" : "INCOMPLETE")}");
        if (!string.IsNullOrWhiteSpace(contract.Description))
            report.AppendLine($"- Purpose: {Escape(contract.Description)}");

        if (contract.RuntimeEnvironment is not null)
        {
            RuntimeEnvironmentSnapshot runtime = contract.RuntimeEnvironment;
            report.AppendLine();
            report.AppendLine("## Runtime / Deployment Identity");
            report.AppendLine();
            report.AppendLine($"- Machine: {runtime.MachineName ?? "—"}");
            report.AppendLine($"- Host: {runtime.HostName ?? "—"}");
            report.AppendLine($"- Environment: {runtime.HostingEnvironment ?? "—"}");
            report.AppendLine($"- Operating system: {runtime.OperatingSystem ?? "—"}");
            report.AppendLine($"- Architecture: {runtime.OsArchitecture ?? "—"} / {runtime.ProcessArchitecture ?? "—"}");
            report.AppendLine($"- Framework: {runtime.FrameworkDescription ?? "—"}");
            report.AppendLine($"- Content root: {runtime.ContentRootPath ?? "—"}");
        }

        if (contract.Diagnostics is not null)
        {
            DiagnosticsCapability diagnostics = contract.Diagnostics;
            report.AppendLine();
            report.AppendLine("## Health and Diagnostics");
            report.AppendLine();
            report.AppendLine($"- Health path: {diagnostics.HealthPath}");
            report.AppendLine($"- Remote diagnostics: {(diagnostics.SupportsRemoteDiagnostics ? "Supported" : "Not supported")}");
            report.AppendLine($"- Operational telemetry: {(diagnostics.SupportsOperationalTelemetry ? "Supported" : "Not supported")}");
            report.AppendLine($"- Authentication scheme: {diagnostics.AuthenticationScheme}");
            if (!string.IsNullOrWhiteSpace(diagnostics.SecretName))
                report.AppendLine($"- Diagnostic credential reference: {diagnostics.SecretName}");
            if (!string.IsNullOrWhiteSpace(diagnostics.DiagnosticsRunPath))
                report.AppendLine($"- Diagnostic run path: {diagnostics.DiagnosticsRunPath}");
            if (!string.IsNullOrWhiteSpace(diagnostics.TelemetryPath))
                report.AppendLine($"- Telemetry path: {diagnostics.TelemetryPath}");
        }

        if (contract.Secrets is not null)
        {
            report.AppendLine();
            report.AppendLine("## Secret Provider");
            report.AppendLine();
            report.AppendLine($"- Provider: {contract.Secrets.Provider ?? "—"}");
            report.AppendLine($"- Connection status: {contract.Secrets.ConnectionStatus ?? "—"}");
            report.AppendLine($"- Credential status: {contract.Secrets.CredentialStatus ?? "—"}");
            if (!string.IsNullOrWhiteSpace(contract.Secrets.ManagementUrl))
                report.AppendLine($"- Management URL: {contract.Secrets.ManagementUrl}");
            report.AppendLine("- Secret values: intentionally excluded");
        }

        report.AppendLine();
        report.AppendLine("## Configuration Requirements");
        report.AppendLine();
        report.AppendLine("| Requirement | Required? | Status | Source | Purpose / Guidance |");
        report.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (RequirementEvaluation requirement in evaluation.Requirements)
        {
            string guidance = requirement.Purpose ?? requirement.Message;
            if (!string.IsNullOrWhiteSpace(requirement.Example) && !requirement.Sensitive)
                guidance += $" Example: {requirement.Example}";
            string source = string.IsNullOrWhiteSpace(requirement.EffectiveSource) ? "Unresolved" : requirement.EffectiveSource;
            report.AppendLine($"| {Escape(requirement.DisplayName)} | {(requirement.Required ? "REQUIRED" : "OPTIONAL")} | {DisplayState(requirement.State)} | {Escape(source)} | {Escape(guidance)} |");
        }

        IReadOnlyList<ConfigurationRequirement> secretRequirements =
            contract.Requirements.Where(x => x.Kind == ConfigurationRequirementKind.Secret).ToList();
        if (secretRequirements.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("## Secret References");
            report.AppendLine();
            foreach (ConfigurationRequirement requirement in secretRequirements)
            {
                string reference = requirement.SecretPath ?? requirement.SecretName ?? requirement.ConfigurationKey ?? requirement.Id;
                report.AppendLine($"- {requirement.DisplayName}: {reference}");
            }
            report.AppendLine();
            report.AppendLine("> References only. No secret values are stored in this runbook.");
        }

        report.AppendLine();
        report.AppendLine("## Registration and Recovery");
        report.AppendLine();
        report.AppendLine("1. The application registers through Common.Registration and persists its installation identity locally.");
        report.AppendLine("2. Configuration accepts contracts only from a valid durable application identity or an authorized control-plane identity.");
        report.AppendLine("3. If the application credential is lost but installation identity remains, use credential recovery rather than creating a second logical installation.");
        report.AppendLine("4. If the installation identity changes unexpectedly, treat the event as an identity conflict and resolve it explicitly in Operations.");
        report.AppendLine("5. Revocation invalidates the credential; purge removes retained registration state only after the applicable lifecycle safeguards are satisfied.");

        report.AppendLine();
        report.AppendLine("## Administrator Procedure");
        report.AppendLine();
        report.AppendLine("1. Resolve every REQUIRED item that is not CONFIGURED or DEFAULT.");
        report.AppendLine("2. Confirm the declared secret provider is reachable when secret-backed requirements exist.");
        report.AppendLine("3. Confirm the application health endpoint and any declared diagnostics endpoints.");
        report.AppendLine("4. Restart or reload the application when a changed setting requires it.");
        report.AppendLine("5. Confirm the application republishes its contract and that this runbook receives a newer generation timestamp.");

        report.AppendLine();
        report.AppendLine("## Evidence");
        report.AppendLine();
        report.AppendLine($"- Contract application: {contract.ApplicationId}");
        report.AppendLine($"- Contract version: {contract.Version}");
        report.AppendLine($"- Generated from authenticated Configuration state at {generatedUtc:O}");
        report.AppendLine();
        report.AppendLine("> This file is an automatically generated operational artifact. Manual notes should be kept separately or added through a future supported annotation mechanism.");
        return report.ToString();
    }

    private static string DisplayState(ConfigurationState state) => state switch
    {
        ConfigurationState.Missing => "MISSING",
        ConfigurationState.OptionalNotConfigured => "OPTIONAL / NOT CONFIGURED",
        ConfigurationState.Configured => "CONFIGURED",
        _ => state.ToString().ToUpperInvariant()
    };

    private static string Escape(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
