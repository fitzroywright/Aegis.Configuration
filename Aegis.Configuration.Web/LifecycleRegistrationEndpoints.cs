using Npgsql;
using Microsoft.AspNetCore.RateLimiting;
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
            [Microsoft.AspNetCore.Mvc.FromBody] LifecycleIntroductionRequest request,
            LifecycleRegistrationAuthority authority,
            CancellationToken ct) =>
        {
            if (context.Request.ContentLength is > 65536)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

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
        }).AllowAnonymous().RequireRateLimiting("registration-lifecycle-rate-limit");

        app.MapPost("/api/registration/{registrationId}/claim", async (
            HttpContext context,
            string registrationId,
            [Microsoft.AspNetCore.Mvc.FromBody] LifecycleClaimRequest request,
            LifecycleRegistrationAuthority authority,
            CancellationToken ct) =>
        {
            if (context.Request.ContentLength is > 16384)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

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
        }).AllowAnonymous().RequireRateLimiting("registration-lifecycle-rate-limit");

        app.MapPost("/api/registration/authenticate", async (
            HttpContext context,
            LifecycleRegistrationAuthority authority,
            CancellationToken ct) =>
        {
            string applicationId = context.Request.Headers["X-Aegis-Application-Id"].ToString().Trim();
            string instanceId = context.Request.Headers["X-Aegis-Instance-Id"].ToString().Trim();
            string installationId = context.Request.Headers["X-Aegis-Installation-Id"].ToString().Trim();
            string runtimeSessionId = context.Request.Headers["X-Aegis-Runtime-Session-Id"].ToString().Trim();
            string credential = Bearer(context.Request.Headers.Authorization.ToString());

            if (!Valid(applicationId, instanceId, installationId) ||
                string.IsNullOrWhiteSpace(credential) ||
                string.IsNullOrWhiteSpace(runtimeSessionId))
                return Results.Unauthorized();

            LifecycleRegistrationRecord? record = await authority.AuthenticateAsync(
                applicationId,
                instanceId,
                installationId,
                credential,
                runtimeSessionId,
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
                record.LastAuthenticatedAtUtc,
                record.ActiveRuntimeSessionId,
                record.RuntimeLeaseExpiresAtUtc
            });
        }).AllowAnonymous().RequireRateLimiting("registration-lifecycle-rate-limit");

        app.MapPost("/api/registration/recovery", async (
            HttpContext context,
            [Microsoft.AspNetCore.Mvc.FromBody] LifecycleRecoveryRequest request,
            LifecycleRegistrationAuthority authority,
            CancellationToken ct) =>
        {
            if (context.Request.ContentLength is > 16384)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);

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
        }).AllowAnonymous().RequireRateLimiting("registration-lifecycle-rate-limit");

        app.MapGet("/api/registration/requests", async (
            HttpContext context,
            bool? includePurged,
            LifecycleRegistrationAuthority authority,
            CancellationToken ct) =>
        {
            if (!Has(context, "Configuration.Manage")) return Forbidden();
            IReadOnlyList<LifecycleRegistrationRecord> records = includePurged == true
                ? await authority.ListAllAsync(ct)
                : await authority.ListAsync(ct);
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
                x.ActiveRuntimeSessionId,
                x.RuntimeLeaseExpiresAtUtc,
                x.ConflictRuntimeSessionId,
                x.RevokedAtUtc,
                x.Metadata,
                x.Audit
            }));
        });

        app.MapPost("/api/registration/{registrationId}/approve", async (
            HttpContext context,
            string registrationId,
            [Microsoft.AspNetCore.Mvc.FromBody] LifecycleDecisionRequest request,
            LifecycleRegistrationAuthority authority,
            CancellationToken ct) =>
        {
            if (!Has(context, "Configuration.Manage")) return Forbidden();
            try
            {
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
            }
            catch (PostgresException ex)
            {
                return PersistenceFailure("approve", ex);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        app.MapPost("/api/registration/{registrationId}/reject", async (
            HttpContext context,
            string registrationId,
            [Microsoft.AspNetCore.Mvc.FromBody] LifecycleDecisionRequest request,
            LifecycleRegistrationAuthority authority,
            CancellationToken ct) =>
        {
            if (!Has(context, "Configuration.Manage")) return Forbidden();
            if (string.IsNullOrWhiteSpace(request.Reason))
                return Results.BadRequest(new { error = "A reason is required to reject a registration." });
            try
            {
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
            }
            catch (PostgresException ex)
            {
                return PersistenceFailure("reject", ex);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        app.MapPost("/api/registration/{registrationId}/revoke", async (
            HttpContext context,
            string registrationId,
            [Microsoft.AspNetCore.Mvc.FromBody] LifecycleDecisionRequest request,
            LifecycleRegistrationAuthority authority,
            CancellationToken ct) =>
        {
            if (!Has(context, "Configuration.Manage")) return Forbidden();
            if (string.IsNullOrWhiteSpace(request.Reason))
                return Results.BadRequest(new { error = "A reason is required to revoke a registration." });
            try
            {
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
            }
            catch (PostgresException ex)
            {
                return PersistenceFailure("revoke", ex);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        app.MapDelete("/api/registration/{registrationId}", async (
            HttpContext context,
            string registrationId,
            [Microsoft.AspNetCore.Mvc.FromBody] LifecycleDecisionRequest request,
            LifecycleRegistrationAuthority authority,
            CancellationToken ct) =>
        {
            if (!Has(context, "Configuration.Manage")) return Forbidden();
            if (string.IsNullOrWhiteSpace(request.Reason))
                return Results.BadRequest(new { error = "A reason is required to purge a registration." });
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
            catch (PostgresException ex)
            {
                return PersistenceFailure("purge", ex);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        app.MapPost("/api/registration/{registrationId}/remove-orphan", async (
            HttpContext context,
            string registrationId,
            [Microsoft.AspNetCore.Mvc.FromBody] LifecycleDecisionRequest request,
            LifecycleRegistrationAuthority authority,
            RegistrationReconciliationState reconciliation,
            CancellationToken ct) =>
        {
            if (!Has(context, "Configuration.Manage")) return Forbidden();
            if (string.IsNullOrWhiteSpace(request.Reason))
                return Results.BadRequest(new { error = "A reason is required to remove an orphaned registration." });

            RegistrationReconciliationIssue? issue = reconciliation.Get().Issues
                .FirstOrDefault(x =>
                    string.Equals(x.RegistrationId, registrationId, StringComparison.Ordinal) &&
                    x.Code is "AUTHENTICATION_STALE" or "NEVER_AUTHENTICATED");

            if (issue is null)
                return Results.Conflict(new { error = "Registration is not currently identified as orphaned by reconciliation." });

            try
            {
                LifecycleRegistrationRecord? revoked = await authority.RevokeAsync(
                    registrationId, Actor(context),
                    context.Connection.RemoteIpAddress?.ToString(),
                    context.Request.Headers.UserAgent.ToString(),
                    request.Reason, Correlation(context), ct);

                if (revoked is null) return Results.NotFound();

                LifecycleRegistrationRecord? purged = await authority.PurgeAsync(
                    registrationId, Actor(context),
                    context.Connection.RemoteIpAddress?.ToString(),
                    context.Request.Headers.UserAgent.ToString(),
                    "Orphan cleanup: " + request.Reason, Correlation(context), ct);

                return purged is null
                    ? Results.NotFound()
                    : Results.Ok(new
                    {
                        purged.RegistrationId,
                        state = purged.State.ToString(),
                        removed = true,
                        auditRetained = true
                    });
            }
            catch (PostgresException ex)
            {
                return PersistenceFailure("remove orphaned registration", ex);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

        app.MapPost("/api/registration/{registrationId}/conflict/retain-existing", async (
            HttpContext context,
            string registrationId,
            [Microsoft.AspNetCore.Mvc.FromBody] LifecycleDecisionRequest request,
            LifecycleRegistrationAuthority authority,
            CancellationToken ct) =>
        {
            if (!Has(context, "Configuration.Manage")) return Forbidden();
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
            catch (PostgresException ex)
            {
                return PersistenceFailure("retain existing identity", ex);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });


        app.MapPost("/api/registration/{registrationId}/conflict/replace-with-incoming", async (
            HttpContext context,
            string registrationId,
            [Microsoft.AspNetCore.Mvc.FromBody] LifecycleDecisionRequest request,
            LifecycleRegistrationAuthority authority,
            CancellationToken ct) =>
        {
            if (!Has(context, "Configuration.Manage")) return Forbidden();
            if (string.IsNullOrWhiteSpace(request.Reason))
                return Results.BadRequest(new { error = "A reason is required to replace an existing registration identity." });
            try
            {
                LifecycleRegistrationRecord? record =
                    await authority.ReplaceWithConflictingIdentityAsync(
                        registrationId,
                        Actor(context),
                        context.Connection.RemoteIpAddress?.ToString(),
                        context.Request.Headers.UserAgent.ToString(),
                        request.Reason,
                        Correlation(context),
                        ct);

                return record is null
                    ? Results.NotFound()
                    : Results.Ok(new
                    {
                        record.RegistrationId,
                        state = record.State.ToString(),
                        next = "Incoming installation must retry registration and will create a fresh Pending request."
                    });
            }
            catch (PostgresException ex)
            {
                return PersistenceFailure("replace registration identity", ex);
            }
            catch (InvalidOperationException ex)
            {
                return Results.Conflict(new { error = ex.Message });
            }
        });

    }

    private static IResult Forbidden() =>
        Results.Json(new { error = "Forbidden", detail = "The authenticated SuiteSecurity session does not have the required permission." }, statusCode: StatusCodes.Status403Forbidden);

    private static IResult PersistenceFailure(string action, PostgresException ex) =>
        Results.Problem(
            title: "Registration persistence failed",
            detail: $"Database rejected {action}: {ex.SqlState} {ex.MessageText}",
            statusCode: StatusCodes.Status503ServiceUnavailable);

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
