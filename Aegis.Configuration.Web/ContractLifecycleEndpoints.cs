using System.Text.Encodings.Web;
using Aegis.Configuration.Infrastructure;

internal sealed record ContractLifecycleRequest(string RecordId, string Fingerprint, string Reason);
internal static class ContractLifecycleEndpoints
{
    private static string E(string? text) => HtmlEncoder.Default.Encode(text ?? "—");
    internal static void Map(WebApplication app)
    {
        app.MapGet("/contracts/history", async (HttpContext context, DirectoryContractStore store, CancellationToken ct) =>
        {
            if (!Has(context, "Configuration.View")) return Results.Forbid();
            var retired = await store.ListRetiredAsync(cancellationToken: ct);
            var audit = await store.HistoryAsync(ct);
            string rows = string.Join("", retired.Select(x => $"<tr><td>{E(x.ApplicationId)}<br>{E(x.DisplayName)}</td><td>{E(x.Version)}</td><td>{E(x.InstanceId)}</td><td>{E(x.LastRegisteredAtUtc?.ToString("u"))}</td><td>{E(x.Reason)}</td><td>{E(x.Actor)}<br>{E(x.RetiredAtUtc.ToString("u"))}</td><td><a href='/contracts/manage/{Uri.EscapeDataString(x.ApplicationId)}'>Manage revision</a></td></tr>"));
            string events = string.Join("", audit.Select(x => $"<tr><td>{E(x.AtUtc.ToString("u"))}</td><td>{E(x.ApplicationId)}</td><td>{E(x.Action)}</td><td>{E(x.Version)}<br>{E(x.LastPublishedAtUtc?.ToString("u"))}</td><td>{E(x.Actor)}</td><td>{E(x.Reason)}</td><td>{E(x.RecordId)}</td></tr>"));
            return Results.Content(ConfigurationShell.Render("Contract History", $"<section class='panel'><h2>Retired contracts</h2><p>Retired contracts are excluded from active evaluation. Republishing activates the current contract and preserves these historical revisions.</p><table><thead><tr><th>Owner / Identity</th><th>Version</th><th>Instance</th><th>Last publication</th><th>Reason</th><th>Retired by / at</th><th>Actions</th></tr></thead><tbody>{rows}</tbody></table></section><section class='panel'><h2>Lifecycle audit</h2><table><thead><tr><th>Time UTC</th><th>Owner</th><th>Action</th><th>Version / Last publication</th><th>Administrator</th><th>Reason</th><th>Record</th></tr></thead><tbody>{events}</tbody></table></section>", "contracts"), "text/html");
        });
        app.MapGet("/contracts/manage/{applicationId}", async (HttpContext context, string applicationId, DirectoryContractStore store, CancellationToken ct) =>
        {
            if (!Has(context, "Configuration.Manage")) return Results.Forbid();
            var records = await store.PreviewAsync(applicationId, ct);
            string rows = string.Join("", records.Select(x => $"<tr><td>{E(x.RecordId)}</td><td>{E(x.Version)}</td><td>{(x.Retired ? "Retired" : "Active")}</td><td><button data-action='{(x.Retired ? "restore" : "retire")}' data-record='{E(x.RecordId)}' data-fingerprint='{E(x.Fingerprint)}'>{(x.Retired ? "Restore" : "Retire")}</button> <button class='danger' data-action='delete' data-record='{E(x.RecordId)}' data-fingerprint='{E(x.Fingerprint)}'>Permanently delete this revision</button></td></tr>"));
            string content = $"<section class='panel'><h2>{E(applicationId)}</h2><p>Preview: each action affects exactly the contract revision identified below. Registration, settings, secrets, databases, diagnostic and acceptance evidence are preserved. Deletion is permanent. A current publisher may publish again.</p><table><thead><tr><th>Record identity</th><th>Version</th><th>State</th><th>Administrator actions</th></tr></thead><tbody>{rows}</tbody></table><p><label>Reason <input id='reason' maxlength='1000' required></label></p><div id='lifecycleResult' role='status'></div><a href='/contracts/history'>Historical view and audit</a></section>";
            content += """
<script>
document.querySelectorAll('[data-action]').forEach(button=>button.addEventListener('click',async()=>{
 const reason=document.getElementById('reason').value.trim(),result=document.getElementById('lifecycleResult');
 if(!reason){result.textContent='Enter a reason before proceeding.';return}
 if(!confirm(button.textContent+'?\nAffected record: '+button.dataset.record+'\nReason: '+reason))return;
 button.disabled=true;
 try{const r=await fetch('/api/contracts/manage/'+encodeURIComponent(location.pathname.split('/').pop())+'/'+button.dataset.action,{method:'POST',headers:{'Content-Type':'application/json','X-Contract-Action':'administrator'},body:JSON.stringify({recordId:button.dataset.record,fingerprint:button.dataset.fingerprint,reason})});if(!r.ok)throw new Error(await r.text());location.reload()}
 catch(e){result.textContent=e.message;button.disabled=false}
}));
</script>
""";
            return Results.Content(ConfigurationShell.Render("Manage Contract", content, "contracts"), "text/html");
        });
        app.MapPost("/api/contracts/manage/{applicationId}/{action}", async (HttpContext context, string applicationId, string action, ContractLifecycleRequest request, DirectoryContractStore store, CancellationToken ct) =>
        {
            if (!Has(context, "Configuration.Manage")) return Results.Forbid();
            // JSON plus a non-simple header prevents cross-origin form submission. Reject cross-site fetches too.
            if (context.Request.Headers["X-Contract-Action"] != "administrator" || context.Request.Headers["Sec-Fetch-Site"] == "cross-site") return Results.BadRequest("Same-origin administrator action required.");
            string actor = (context.Items["SuiteIdentity"] as SuiteIdentity)?.UserName ?? context.User.Identity?.Name ?? "";
            try
            {
                bool changed = await store.ManageAsync(applicationId, request.RecordId, request.Fingerprint, action, actor, request.Reason, ct);
                return changed ? Results.Ok(new { changed, applicationId, action }) : Results.Conflict("Contract changed since preview. Reload before acting.");
            }
            catch (InvalidDataException e) { return Results.BadRequest(e.Message); }
        });
    }
    private static bool Has(HttpContext context, string permission) => (context.Items["SuiteIdentity"] as SuiteIdentity)?.Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase) == true;
}
