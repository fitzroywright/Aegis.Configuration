using System.Text;
using Aegis.Configuration.Domain;

namespace Aegis.Configuration.Application;

public sealed class CommissioningReportRenderer
{
    public string RenderMarkdown(CommissioningSnapshot snapshot, ConfigurationInstructionPack? pack = null)
    {
        StringBuilder report = new();
        report.AppendLine($"# {snapshot.ApplicationId} Commissioning Report");
        report.AppendLine();
        report.AppendLine($"- Application version: {snapshot.ApplicationVersion}");
        report.AppendLine($"- Evaluated UTC: {snapshot.EvaluatedAtUtc:O}");
        report.AppendLine($"- Overall: {(snapshot.Evaluation.IsOperational ? "VERIFIED / OPERATIONAL" : "COMMISSIONING REQUIRED")}");
        if (pack is not null) report.AppendLine($"- Instruction pack: {pack.PackVersion}");
        report.AppendLine();
        report.AppendLine("| Requirement | State | Result |");
        report.AppendLine("| --- | --- | --- |");
        foreach (RequirementEvaluation requirement in snapshot.Evaluation.Requirements)
        {
            report.AppendLine($"| {Escape(requirement.DisplayName)} | {requirement.State} | {Escape(requirement.Message)} |");
        }
        report.AppendLine();
        report.AppendLine("## Administrator Sign-off");
        report.AppendLine("Administrator: ____________________");
        report.AppendLine("Date: ____________________");
        report.AppendLine("Notes: ____________________");
        report.AppendLine();
        report.AppendLine("> Secret values are intentionally excluded from this report.");
        return report.ToString();
    }

    private static string Escape(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
}
