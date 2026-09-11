using System.Text;

namespace Aegis.Configuration.Application;

public sealed class OperationalBriefRenderer
{
    public string Render(IncidentSummary incident)
    {
        StringBuilder text = new();
        text.AppendLine($"# Operational Incident {incident.IncidentId}");
        text.AppendLine();
        text.AppendLine($"**Severity:** {incident.Severity}");
        text.AppendLine($"**Created UTC:** {incident.CreatedAtUtc:O}");
        text.AppendLine($"**Summary:** {incident.Headline}");
        text.AppendLine();
        if (incident.RootSuspects.Count > 0) text.AppendLine($"**Likely shared dependency:** {string.Join(", ", incident.RootSuspects)}");
        text.AppendLine($"**Affected:** {string.Join(", ", incident.AffectedApplications)}");
        text.AppendLine();
        text.AppendLine("## Recovery order");
        foreach (RemediationStep step in incident.RecoveryPlan) text.AppendLine($"{step.Order}. **{step.Target}** — {step.Action} ({step.Reason})");
        text.AppendLine();
        text.AppendLine("## Evidence");
        foreach (string evidence in incident.Evidence) text.AppendLine($"- {evidence}");
        text.AppendLine();
        text.AppendLine("> Generated from verified configuration state, drift and dependency evidence. No secret values are included.");
        return text.ToString();
    }
}
