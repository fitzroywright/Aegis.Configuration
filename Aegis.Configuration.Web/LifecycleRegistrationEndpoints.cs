using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Common.Security;

internal sealed record LifecycleIntroductionRequest(
    string ApplicationId,
    string InstanceId,
    string InstallationId,
    JsonObject? Metadata);

internal sealed record LifecycleClaimRequest(
    string ApplicationId,
    string InstanceId,
    string InstallationId,
    string ClaimToken);

internal sealed record LifecycleRecoveryRequest(
    string ApplicationId,
    string InstanceId,
    string InstallationId,
    string? RegistrationId);

internal sealed record LifecycleDecisionRequest(string? Reason);

internal static class LifecycleRegistrationEndpoints
{
    internal static void MapLifecycleRegistration(this WebApplication app)
    {
        app.MapPost("/api/registration/request", async (
            HttpContext context,
            LifecycleIntroductionRequest request,
            LifecycleRegistrationAuthority authority,
            CancellationToken ct) =>
        {
            if (!Valid(request.ApplicationId, request.InstanceId, request.InstallationId))
                return Results.BadRequest(new { error = "ApplicationId, InstanceId and InstallationId are required." });

            string correlation = Correlation(context);
            LifecycleIntroductionResult result = await authority.IntroduceAsync(
                request.ApplicationId,
                request.InstanceId,
                request.InstallationId,
                request.Metadata,
                "unauthenticated-introduction",
                context.Connection.RemoteIpAddress?.ToString(),
                context.Request.Headers.UserAgent.ToString(),
                correlation,
                ct);

            context.Response.Headers.CacheControl = "no-store";

            if (result.Conflict)
                return Results.Conflict(new
                {
                    result.Record.RegistrationId,
                    state = "IdentityConflict",
                    error = "ApplicationId/InstanceId is already associated with a different InstallationId."
                });

            if (result.Record.State == LifecycleRegistrationState.Registered)
                return Results.Conflict(new
                {
                    result.Record.RegistrationId,
                    state = "Registered",
                    error = "This logical instance is already registered. Authenticate or request credential recovery."
                });

            if (result.Record.State is LifecycleRegistrationState.Revoked or LifecycleRegistrationState.Rejected)
                return Results.StatusCode(StatusCodes.Status403Forbidden);

            // The claim token is returned only when this request created the pending record.
            // Repeated introductions never disclose the original token.
            if (!result.Created || string.IsNullOrWhiteSpace(result.ClaimToken))
                return Results.Conflict(new
                {
                    result.Record.RegistrationId,
                    state = result.Record.State.ToString(),
                    error = "A pending registration already exists. Use the original local claim state."
                });

            return Results.Ok(new
            {
                result.Record.RegistrationId,
                result.ClaimToken,
                state = result.Record.State.ToString()
            });
        }).AllowAnonymous();

        app.MapPost("/api/registration/{registrationId}/claim", async (
            HttpContext context,
            string registrationId,
            LifecycleClaimRequest request,
            LifecycleRegistrationAuthority authority,
            CancellationToken ct) =>
        {
            if (!Valid(request.ApplicationId, request.InstanceId, request.InstallationId) ||
                string.IsNullOrWhiteSpace(request.ClaimToken))
                return Results.BadRequest(new { error = "Complete identity and claim token are required." });

            LifecycleClaimResult result = await authority.ClaimAsync(
                registrationId,
                request.ApplicationId,
                request.InstanceId,
                request.InstallationId,
                request.ClaimToken,
                Correlation(context),
                ct);

            context.Response.Headers.CacheControl = "no-store";

            if (result.Record is null) return Results.NotFound(new { error = "Registration not found." });
            if (result.Conflict) return Results.Conflict(new { state = "IdentityConflict", error = "Installation identity conflict." });
            if (result.Rejected) return Results.Json(new { state = "Rejected", error = "Registration was rejected." }, statusCode: 403);
            if (result.Pending) return Results.Json(new { registrationId, state = result.Record.State.ToString() }, statusCode: 202);
            if (result.InvalidClaim) return Results.Unauthorized();

            return Results.Ok(new
            {
                registrationId,
                credential = result.Credential,
                state = "Registered"
            });
        }).AllowAnonymous();

        app.MapPost("/api/registration/authenticate", async (
            HttpContext context,
            LifecycleRegistrationAuthority authority,
            CancellationToken ct) =>
        {
            string applicationId = context.Request.Headers["X-Aegis-Application-Id"].ToString().Trim();
            string instanceId = context.Request.Headers["X-Aegis-Instance-Id"].ToString().Trim();
            string installationId = context.Request.Headers["X-Aegis-Installation-Id"].ToString().Trim();
            string credential = Bearer(context.Request.Headers.Authorization.ToString());

            if (!Valid(applicationId, instanceId, installationId) || string.IsNullOrWhiteSpace(credential))
                return Results.Unauthorized();

            LifecycleRegistrationRecord? record = await authority.AuthenticateAsync(
                applicationId,
                instanceId,
                installationId,
                credential,
                Correlation(context),
                ct);

            if (record is null) return Results.Unauthorized();
            if (record.State == LifecycleRegistrationState.IdentityConflict)
                return Results.Conflict(new { state = "IdentityConflict", error = "Installation identity conflict." });
            if (record.State == LifecycleRegistrationState.Revoked)
                return Results.Json(new { state = "Revoked", error = "Registration is revoked." }, statusCode: 410);
            if (record.State != LifecycleRegistrationState.Registered)
                return Results.Unauthorized();

            return Results.Ok(new
            {
                record.RegistrationId,
                state = "Registered",
                record.LastAuthenticatedAtUtc
            });
        }).AllowAnonymous();

        app.MapPost("/api/registration/recovery", async (
            HttpContext context,
            LifecycleRecoveryRequest request,
            LifecycleRegistrationAuthority authority,
            CancellationToken ct) =>
        {
            if (!Valid(request.ApplicationId, request.InstanceId, request.InstallationId))
                return Results.BadRequest(new { error = "ApplicationId, InstanceId and InstallationId are required." });

            try
            {
                LifecycleIntroductionResult result = await authority.RequestRecoveryAsync(
                    request.ApplicationId,
                    request.InstanceId,
                    request.InstallationId,
                    "recovery-request",
                    context.Connection.RemoteIpAddress?.ToString(),
                    context.Request.Headers.UserAgent.ToString(),
                    Correlation(context),
                    ct);

                if (result.Conflict)
                    return Results.Conflict(new { state = "IdentityConflict", error = "Installation identity conflict." });
                if (!result.Created || string.IsNullOrWhiteSpace(result.ClaimToken))
                    return Results.Conflict(new
                    {
                        result.Record.RegistrationId,
                        state = result.Record.State.ToString(),
                        error = "Recovery is already pending; use the locally persisted claim state."
                    });

                context.Response.Headers.CacheControl = "no-store";
                return Results.Ok(new
                {
                    result.Record.RegistrationId,
                    result.ClaimToken,
                    state = "RecoveryPending"
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        }).AllowAnonymous();

        app.MapGet("/api/registration/requests", async (
            HttpContext context,
            LifecycleRegistrationAuthority authority,
            CancellationToken ct) =>
        {
            if (!Has(context, "Configuration.Manage")) return Results.Forbid();
            IReadOnlyList<LifecycleRegistrationRecord> records = await authority.ListAsync(ct);
            return Results.Ok(records.Select(x => new
            {
                x.RegistrationId,
                x.ApplicationId,
                x.InstanceId,
                x.InstallationId,
                state = x.State.ToString(),
                x.CreatedAtUtc,
                x.UpdatedAtUtc,
                x.ApprovedAtUtc,
                x.RegisteredAtUtc,
                x.LastAuthenticatedAtUtc,
                x.RevokedAtUtc,
                x.Metadata,
                x.Audit
            }));
        });

        app.MapPost("/api/registration/{registrationId}/approve", async (
            HttpContext context,
            string registrationId,
            LifecycleDecisionRequest request,
            LifecycleRegistrationAuthority authority,
            CancellationToken ct) =>
        {
            if (!Has(context, "Configuration.Manage")) return Results.Forbid();
            LifecycleRegistrationRecord? record = await authority.DecideAsync(
                registrationId, true, Actor(context),
                context.Connection.RemoteIpAddress?.ToString(),
                context.Request.Headers.UserAgent.ToString(),
                request.Reason, Correlation(context), ct);
            return record is null ? Results.NotFound() : Results.Ok(new
            {
                record.RegistrationId,
                state = record.State.ToString(),
                record.ApprovedAtUtc
            });
        });

        app.MapPost("/api/registration/{registrationId}/reject", async (
            HttpContext context,
            string registrationId,
            LifecycleDecisionRequest request,
            LifecycleRegistrationAuthority authority,
            CancellationToken ct) =>
        {
            if (!Has(context, "Configuration.Manage")) return Results.Forbid();
            LifecycleRegistrationRecord? record = await authority.DecideAsync(
                registrationId, false, Actor(context),
                context.Connection.RemoteIpAddress?.ToString(),
                context.Request.Headers.UserAgent.ToString(),
                request.Reason, Correlation(context), ct);
            return record is null ? Results.NotFound() : Results.Ok(new
            {
                record.RegistrationId,
                state = record.State.ToString()
            });
        });

        app.MapPost("/api/registration/{registrationId}/revoke", async (
            HttpContext context,
            string registrationId,
            LifecycleDecisionRequest request,
            LifecycleRegistrationAuthority authority,
            CancellationToken ct) =>
        {
            if (!Has(context, "Configuration.Manage")) return Results.Forbid();
            LifecycleRegistrationRecord? record = await authority.RevokeAsync(
                registrationId, Actor(context),
                context.Connection.RemoteIpAddress?.ToString(),
                context.Request.Headers.UserAgent.ToString(),
                request.Reason, Correlation(context), ct);
            return record is null ? Results.NotFound() : Results.Ok(new
            {
                record.RegistrationId,
                state = record.State.ToString(),
                record.RevokedAtUtc
            });
        });

        app.MapDelete("/api/registration/{registrationId}", async (
            HttpContext context,
            string registrationId,
            LifecycleDecisionRequest request,
            LifecycleRegistrationAuthority authority,
            CancellationToken ct) =>
        {
            if (!Has(context, "Configuration.Manage")) return Results.Forbid();
            try
            {
                LifecycleRegistrationRecord? record = await authority.PurgeAsync(
                    registrationId, Actor(context),
                    context.Connection.RemoteIpAddress?.ToString(),
                    context.Request.Headers.UserAgent.ToString(),
                    request.Reason, Correlation(context), ct);
                return record is null ? Results.NotFound() : Results.Ok(new
                {
                    record.RegistrationId,
                    state = record.State.ToString()
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        app.MapPost("/api/registration/{registrationId}/conflict/retain-existing", async (
            HttpContext context,
            string registrationId,
            LifecycleDecisionRequest request,
            LifecycleRegistrationAuthority authority,
            CancellationToken ct) =>
        {
            if (!Has(context, "Configuration.Manage")) return Results.Forbid();
            try
            {
                LifecycleRegistrationRecord? record = await authority.RetainExistingIdentityAsync(
                    registrationId, Actor(context),
                    context.Connection.RemoteIpAddress?.ToString(),
                    context.Request.Headers.UserAgent.ToString(),
                    request.Reason, Correlation(context), ct);
                return record is null ? Results.NotFound() : Results.Ok(new
                {
                    record.RegistrationId,
                    state = record.State.ToString()
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });
    }

    private static bool Valid(string? app, string? instance, string? installation)
        => !string.IsNullOrWhiteSpace(app) &&
           !string.IsNullOrWhiteSpace(instance) &&
           !string.IsNullOrWhiteSpace(installation);

    private static string Correlation(HttpContext context)
    {
        string value = context.Request.Headers["X-Aegis-Correlation-Id"].ToString().Trim();
        return string.IsNullOrWhiteSpace(value) ? Guid.NewGuid().ToString("N") : value;
    }

    private static string Bearer(string header)
    {
        if (!AuthenticationHeaderValue.TryParse(header, out AuthenticationHeaderValue? parsed) ||
            !string.Equals(parsed.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return parsed.Parameter?.Trim() ?? string.Empty;
    }

    private static bool Has(HttpContext context, string permission)
        => context.Items.TryGetValue("SuiteIdentity", out object? value) &&
           value is SuiteIdentity identity &&
           identity.Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);

    private static string Actor(HttpContext context)
        => context.Items.TryGetValue("SuiteIdentity", out object? value) && value is SuiteIdentity identity
            ? identity.UserName
            : context.User.Identity?.Name ?? "unknown";
}
