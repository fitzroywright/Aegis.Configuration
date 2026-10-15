using System.Text.Encodings.Web;
using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;

internal static class ConfigurationIssueEndpoints
{
    private static string E(string? value) => HtmlEncoder.Default.Encode(value ?? "Not published");
    internal static void Map(WebApplication app)
    {
        app.MapGet("/provider-issues", async (string? provider, ConfigurationApplicationInventory inventory, CancellationToken ct) =>
        {
            var rows = await inventory.LoadAsync(ct);
            string issues = string.Join("", rows.Select(row => {
                var status = SecretManagerEvaluator.Evaluate(row.Contract, row.Evaluation);
                if (provider is not null && !status.ProviderName.Equals(provider, StringComparison.OrdinalIgnoreCase)) return "";
                return string.Join("", status.Issues.Select(issue => $"<a class='panel issue-panel issue-detail' href='/applications/{Uri.EscapeDataString(row.Contract.ApplicationId)}/issues?check={Uri.EscapeDataString(issue.Code)}#provider-{E(issue.Code)}'><b>{E(row.Contract.DisplayName)} · {E(issue.Code)}</b><p>{E(issue.Message)}</p></a>"));
            }));
            return Results.Content(ConfigurationShell.Render("Provider Issues", string.IsNullOrEmpty(issues) ? "<p>No current provider issues in this selection.</p>" : issues, "applications"), "text/html");
        });
        app.MapGet("/configuration/health", async (IEnumerable<Common.Diagnostics.IAegisHealthAssessmentSource> sources, CancellationToken ct) =>
        {
            var details = new List<string>();
            foreach (var source in sources)
            {
                try { var health = await source.AssessAsync(ct); details.Add($"<section class='panel issue-detail'><h2>{E(source.GetType().Name)}</h2><p>{E(health.State.ToString())}: {E(health.Summary)}</p><p>Last check: {DateTimeOffset.UtcNow:u}</p><p>Check the contract directory permissions and deployment configuration; inspect Configuration logs for the affected component.</p><a href='/configuration/logs'>Configuration logs</a></section>"); }
                catch (Exception e) when (e is not OperationCanceledException) { details.Add("<section class='panel issue-detail'>Health assessment unavailable. Check Configuration logs and provider connectivity.</section>"); }
            }
            return Results.Content(ConfigurationShell.Render("Configuration Health", "<a href='/configuration/health'>Retest health assessment</a>" + string.Join("", details), "applications"), "text/html");
        });
        app.MapGet("/applications/{applicationId}/issues", async (HttpContext context, string applicationId, string? check, ConfigurationApplicationInventory inventory, LifecycleRegistrationAuthority authority, CancellationToken ct) =>
        {
            var rows = await inventory.LoadAsync(ct);
            var row = rows.FirstOrDefault(x => x.Contract.ApplicationId.Equals(applicationId, StringComparison.OrdinalIgnoreCase));
            string id = Uri.EscapeDataString(applicationId);
            string requirements = row.Contract is null ? "<p>No active configuration contract. Check registration and publication logs.</p>" : string.Join("", row.Evaluation.Requirements
                .Where(x => check is null || x.RequirementId == check)
                .Select(x => $"<section class='panel issue-detail' id='requirement-{E(x.RequirementId)}' tabindex='-1'><h2>{E(x.DisplayName)}</h2><p><b>Requirement:</b> {E(x.RequirementId)} · {E(x.State.ToString())}</p><p>{E(x.Message)}</p><p>{E(x.Purpose)}</p><p>Verification: {E(x.VerificationStatus)}</p><p>Last publication: {E(row.Contract.LastRegisteredAtUtc?.ToString("u"))}</p><p>Corrective guidance: resolve this requirement in the application's deployment configuration or declared secret provider, then republish the contract. Disabled optional capabilities do not need configuration.</p><a href='/applications/{id}/contract'>Exact application contract and configuration keys</a> · <a href='/applications/{id}/secrets#issues'>Provider declaration, access, resolution and credentials</a> · <a href='/runbooks/{id}'>Application runbook</a></section>"));
            if (row.Contract is not null)
            {
                var secrets = SecretManagerEvaluator.Evaluate(row.Contract, row.Evaluation);
                requirements += string.Join("", secrets.Issues.Where(x => check is null || x.Code == check).Select(x =>
                    $"<section class='panel issue-detail' id='provider-{E(x.Code)}' tabindex='-1'><h2>{E(x.Code)}</h2><p>{E(x.Message)}</p><p>Component: Common.Secrets / {E(secrets.ProviderName)}</p><p>Last publication: {E(row.Contract.LastRegisteredAtUtc?.ToString("u"))}</p><p>Declare Secrets.Provider to identify the provider; inspect provider access separately from secret resolution and credential verification. Correct the reported stage and republish.</p><a href='/applications/{id}/secrets#issues'>Open this application's provider configuration guidance</a></section>"));
            }
            string lifecycle;
            try
            {
                var records = (await authority.ListAllAsync(ct)).Where(x => x.ApplicationId.Equals(applicationId, StringComparison.OrdinalIgnoreCase)).ToArray();
                lifecycle = string.Join("", records.Select(r => {
                    var failure = r.Audit.Where(a => a.Action is "ConfigurationContractRejected" or "ConfigurationContractPublished").OrderByDescending(a => a.AtUtc).FirstOrDefault();
                    var journey = LifecycleFlowSnapshotBuilder.Build([r], 10).Journeys.FirstOrDefault();
                    return $"<section class='panel issue-detail' id='registration-{E(r.RegistrationId)}' tabindex='-1'><h2>Registration / contract publication</h2><p>Instance: {E(r.InstanceId)} · Registration: {E(r.RegistrationId)} · State: {E(r.State.ToString())}</p><p>Lifecycle health: {E(journey?.Health)}. Runtime state and diagnostic health are reported independently in Diagnostics.</p><p>Check: {E(failure?.Action ?? journey?.LastAction)}</p><p>Published cause: {E(failure?.Reason ?? journey?.LastReason)}</p><p>Last attempt: {E((failure?.AtUtc ?? r.LastAuthenticatedAtUtc)?.ToString("u"))}</p><p>Corrective guidance: for rejected publication, fix the contract validation message above and republish with the current registration. For stale authentication, inspect the application's registration credential and connectivity. Do not delete its registration to hide a failure.</p><a href='/configuration/logs'>Configuration publication logs</a> · <a href='/applications/{id}/contract'>Published contract</a></section>";
                }));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                string cause = e is Npgsql.PostgresException postgres ? $"Database SQLSTATE {postgres.SqlState}." : $"Failure category: {e.GetType().Name}.";
                for (Exception? inner = e.InnerException; inner is not null; inner = inner.InnerException)
                    if (inner is System.Net.Sockets.SocketException socket) { cause += $" Network error: {socket.SocketErrorCode}."; break; }
                lifecycle = $"<section class='panel issue-detail'><h2>Registration retrieval failed</h2><p>Component: Configuration registration database. Check: registration-inventory.</p><p>{E(cause)}</p><p>Last attempt: {DateTimeOffset.UtcNow:u}</p><p>Check the registration database connection, service availability and protected credential configuration, then re-evaluate.</p><a href='/configuration/logs'>Configuration error logs</a></section>";
            }
            string diagnostic = row.Contract?.Diagnostics is { } d
                ? $"<p>Diagnostic capability: {E(d.AuthenticationScheme)}. <a href='/applications/{id}/details#diagnostics'>Open this application's diagnostic destination</a>.</p>" : "<p>No remote diagnostic retest capability is published. Reloading this page re-evaluates the available configuration.</p>";
            string content = $"<section class='panel issue-detail'><h1>{E(applicationId)}</h1><p>Affected components: configuration requirements, provider declarations and registration / contract publication.</p><p>Observed: {DateTimeOffset.UtcNow:u}</p><a href='/applications/{id}'>Application configuration</a> · <a href='/applications/{id}/issues'>Re-evaluate configuration</a>{diagnostic}</section>{requirements}{lifecycle}";
            return Results.Content(ConfigurationShell.Render("Issue Details", content, "applications"), "text/html");
        });
    }
}
