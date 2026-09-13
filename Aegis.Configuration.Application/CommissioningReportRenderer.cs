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
        report.AppendLine($"- Configuration status: {(snapshot.Evaluation.IsOperational ? "COMPLETE" : "INCOMPLETE")}");
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
        report.AppendLine("6. Use Aegis.Diagnostics separately to verify runtime health and dependency connectivity.");
        report.AppendLine();
        report.AppendLine("## Administrator Sign-off");
        report.AppendLine("Administrator: ____________________");
        report.AppendLine("Date: ____________________");
        report.AppendLine("Notes: ____________________");
        report.AppendLine();
        report.AppendLine("> Aegis.Configuration reports configuration completeness only. Runtime health belongs to Aegis.Diagnostics. Secret values are intentionally excluded.");
        return report.ToString();
    }

    private static string DisplayState(ConfigurationState state) => state switch
    {
        ConfigurationState.Missing => "MISSING",
        ConfigurationState.NotRequired => "NOT CONFIGURED",
        ConfigurationState.Invalid => "INCOMPLETE",
        ConfigurationState.ConfiguredUnverified or ConfigurationState.Verified => "CONFIGURED",
        _ => state.ToString().ToUpperInvariant()
    };

    private static string Escape(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
