using Aegis.Configuration.Application;
using Aegis.Configuration.Domain;
using Aegis.Configuration.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aegis.Configuration.Tests;

public sealed class ContractLifecycleTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "contract-lifecycle-" + Guid.NewGuid().ToString("N"));
    private CancellationToken Ct => TestContext.Current.CancellationToken;
    private static ApplicationConfigurationContract Contract(string id = "Demo.App", bool configured = false) =>
        new(id, id, "1.0", [new("database", "Database", ConfigurationRequirementKind.Configuration, true, "Database required", ConfigurationKey: "Database:Connection", IsConfigured: configured)]);
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    private async Task Change(DirectoryContractStore store, string action, bool retired = false)
    {
        var record = (await store.PreviewAsync("Demo.App", Ct)).Single(x => x.Retired == retired);
        Assert.True(await store.ManageAsync("Demo.App", record.RecordId, record.Fingerprint, action, "administrator", "Replace obsolete revision", Ct));
    }

    [Fact]
    public async Task RetirementAndRestoreSurviveNewStoreInstanceAndPreserveAudit()
    {
        var store = new DirectoryContractStore(root);
        await store.RegisterAsync(Contract(), Ct);
        await Change(store, "retire");
        var restarted = new DirectoryContractStore(root);
        Assert.Empty(await restarted.LoadAllAsync(Ct));
        var history = Assert.Single(await restarted.ListRetiredAsync(cancellationToken: Ct));
        Assert.Equal("administrator", history.Actor);
        Assert.Equal("Replace obsolete revision", history.Reason);
        Assert.True(history.RetiredAtUtc >= history.LastRegisteredAtUtc);
        await Change(restarted, "restore", retired: true);
        var restored = Assert.Single(await new DirectoryContractStore(root).LoadAllAsync(Ct));
        Assert.False((await new ConfigurationEvaluator().EvaluateAsync(restored, Ct)).IsComplete);
        Assert.Contains(await restarted.HistoryAsync(Ct), x => x.Action == "Restored" && x.Actor == "administrator");
    }

    [Fact]
    public async Task RepublishingReactivatesWithoutOverwritingHistoryAndRestoreRefusesCurrentContract()
    {
        var store = new DirectoryContractStore(root);
        await store.RegisterAsync(Contract(), Ct);
        await Change(store, "retire");
        await store.RegisterAsync(Contract(configured: true) with { Version = "2.0" }, Ct);
        Assert.True((await new ConfigurationEvaluator().EvaluateAsync(Assert.Single(await store.LoadAllAsync(Ct)), Ct)).IsComplete);
        var retired = (await store.PreviewAsync("Demo.App", Ct)).Single(x => x.Retired);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await store.ManageAsync("Demo.App", retired.RecordId, retired.Fingerprint, "restore", "admin", "Restore", Ct));
        Assert.Contains(await store.HistoryAsync(Ct), x => x.Action == "Republished");
        Assert.Equal("2.0", Assert.Single(await store.LoadAllAsync(Ct)).Version);
    }

    [Fact]
    public async Task DeleteTargetsOnlyPreviewedRevisionAndLeavesEvidenceAndOtherApplicationsIntact()
    {
        var store = new DirectoryContractStore(root);
        await store.RegisterAsync(Contract(), Ct);
        await Change(store, "retire");
        await store.RegisterAsync(Contract(configured: true), Ct);
        await store.RegisterAsync(Contract("Demo.App.Child"), Ct);
        string evidence = Path.Combine(root, "evidence.txt");
        await File.WriteAllTextAsync(evidence, "diagnostic and acceptance evidence", Ct);
        await Change(store, "delete", retired: true);
        Assert.Equal(2, (await store.LoadAllAsync(Ct)).Count);
        Assert.Empty(await store.ListRetiredAsync(cancellationToken: Ct));
        Assert.True(File.Exists(evidence));
        Assert.Contains(await store.HistoryAsync(Ct), x => x.Action == "Deleted");
    }

    [Fact]
    public async Task StalePreviewCannotDeleteRepublishedContract()
    {
        var store = new DirectoryContractStore(root);
        await store.RegisterAsync(Contract(), Ct);
        var preview = Assert.Single(await store.PreviewAsync("Demo.App", Ct));
        await store.RegisterAsync(Contract() with { Version = "2.0" }, Ct);
        Assert.False(await store.ManageAsync("Demo.App", preview.RecordId, preview.Fingerprint, "delete", "admin", "Delete old version", Ct));
        Assert.Equal("2.0", Assert.Single(await store.LoadAllAsync(Ct)).Version);
    }

    [Fact]
    public async Task MalformedContractDoesNotRemoveOtherApplicationsAndNeverEchoesPayload()
    {
        var store = new DirectoryContractStore(root);
        await store.RegisterAsync(Contract(), Ct);
        await File.WriteAllTextAsync(Path.Combine(root, "RequestPortal.configuration-contract.json"), "{secret-token", Ct);
        var loaded = await store.LoadAllAsync(Ct);
        Assert.Equal(2, loaded.Count);
        var failed = loaded.Single(x => x.ApplicationId == "RequestPortal");
        Assert.Equal("Unknown", failed.Requirements.Single().ConfigurationState);
        Assert.DoesNotContain("secret-token", failed.Requirements.Single().Purpose);
    }

    [Fact]
    public async Task LegacyAliasMigrationPreservesCanonicalContractAndIsIdempotent()
    {
        var store = new DirectoryContractStore(root);
        await store.RegisterAsync(Contract("Aegis.Cafeteria.Services", true), Ct);
        await File.WriteAllTextAsync(Path.Combine(root, "Cafeteria.Services.configuration-contract.json"), System.Text.Json.JsonSerializer.Serialize(Contract("Cafeteria.Services"), new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web)), Ct);
        Assert.True(await store.RetireLegacyCafeteriaAliasAsync(Ct));
        Assert.False(await new DirectoryContractStore(root).RetireLegacyCafeteriaAliasAsync(Ct));
        Assert.Equal("Aegis.Cafeteria.Services", Assert.Single(await store.LoadAllAsync(Ct)).ApplicationId);
        Assert.Single(await store.ListRetiredAsync(cancellationToken: Ct));
    }

    [Fact]
    public async Task RegisteredApplicationWithoutContractRemainsVisibleAndRecoveryReevaluates()
    {
        var store = new DirectoryContractStore(root);
        await store.RegisterAsync(Contract("Healthy.App", true), Ct);
        var authority = new LifecycleRegistrationAuthority(new ConfigurationBuilder().Build(), new MemoryRegistrations());
        var inventory = new ConfigurationApplicationInventory(store, new ConfigurationEvaluator(), authority, NullLogger<ConfigurationApplicationInventory>.Instance);
        var rows = await inventory.LoadAsync(Ct);
        Assert.Equal(2, rows.Count);
        Assert.False(rows.Single(x => x.Contract.ApplicationId == "RequestPortal").Evaluation.IsComplete);
        Assert.Contains("Registered", rows.Single(x => x.Contract.ApplicationId == "RequestPortal").Evaluation.RegistrationState);
        await store.RegisterAsync(Contract("RequestPortal", true), Ct);
        Assert.True((await inventory.LoadAsync(Ct)).Single(x => x.Contract.ApplicationId == "RequestPortal").Evaluation.IsComplete);
    }

    [Fact]
    public async Task DisabledOptionalSecretDoesNotCreateProviderOrVerificationWarnings()
    {
        var contract = Contract() with { Requirements = [new("whatsapp-token", "WhatsApp token", ConfigurationRequirementKind.Secret, false, "Disabled capability", SecretName: "whatsapp", IsConfigured: true, ConfigurationState: "Disabled", VerificationStatus: "Failed")], Secrets = new(ConnectionStatus: "Failed", CredentialStatus: "Failed") };
        var evaluation = await new ConfigurationEvaluator().EvaluateAsync(contract, Ct);
        Assert.True(evaluation.IsComplete);
        Assert.Empty(SecretManagerEvaluator.Evaluate(contract, evaluation).Issues);
    }

    [Fact]
    public async Task RetirementSuspendsEvaluationButKeepsRegisteredApplicationAndRepublishResumesIt()
    {
        var store = new DirectoryContractStore(root);
        await store.RegisterAsync(Contract("RequestPortal"), Ct);
        var active = Assert.Single(await store.PreviewAsync("RequestPortal", Ct));
        Assert.True(await store.ManageAsync("RequestPortal", active.RecordId, active.Fingerprint, "retire", "admin", "Superseded", Ct));
        var authority = new LifecycleRegistrationAuthority(new ConfigurationBuilder().Build(), new MemoryRegistrations());
        var inventory = new ConfigurationApplicationInventory(store, new ConfigurationEvaluator(), authority, NullLogger<ConfigurationApplicationInventory>.Instance);
        var retired = Assert.Single(await inventory.LoadAsync(Ct));
        Assert.True(retired.Evaluation.EvaluationSuspended);
        Assert.False(retired.Evaluation.NeedsAttention);
        Assert.Contains("Registered", retired.Evaluation.RegistrationState);
        await store.RegisterAsync(Contract("RequestPortal"), Ct);
        var current = Assert.Single(await inventory.LoadAsync(Ct));
        Assert.False(current.Evaluation.EvaluationSuspended);
        Assert.True(current.Evaluation.NeedsAttention);
    }

    [Fact]
    public async Task PurgingParentDoesNotDeleteChildHistoricalContract()
    {
        var store = new DirectoryContractStore(root);
        await store.RegisterAsync(Contract(), Ct);
        await store.RegisterAsync(Contract("Demo.App.Child"), Ct);
        await store.RetireAsync("Demo.App.Child", "obsolete", Ct);
        await store.PurgeAsync("Demo.App", Ct);
        Assert.Equal("Demo.App.Child", Assert.Single(await store.ListRetiredAsync(cancellationToken: Ct)).ApplicationId);
    }

    [Fact]
    public async Task RenderedApplicationsRetainsFailedRowAndOverviewTargetsExactRequirement()
    {
        var store = new DirectoryContractStore(root);
        await store.RegisterAsync(Contract("Healthy.App", true), Ct);
        var authority = new LifecycleRegistrationAuthority(new ConfigurationBuilder().Build(), new MemoryRegistrations());
        var inventory = new ConfigurationApplicationInventory(store, new ConfigurationEvaluator(), authority, NullLogger<ConfigurationApplicationInventory>.Instance);
        var rows = await inventory.LoadAsync(Ct);
        var program = typeof(ConfigurationApplicationInventory).Assembly.GetType("Program")!;
        var methods = program.GetMethods(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        string overview = (string)methods.Single(x => x.Name.Contains("g__RenderOverview|")).Invoke(null, [rows, 0])!;
        string applications = (string)methods.Single(x => x.Name.Contains("g__RenderDashboard|")).Invoke(null, [rows, null, 0, "applications"])!;
        Assert.Contains("/applications/RequestPortal/issues?check=contract-unavailable#requirement-contract-unavailable", overview);
        Assert.Contains("data-application-id='RequestPortal'", applications);
        Assert.Contains("<option value=''>All Status</option>", applications);
        Assert.Contains("data-configuration-state='Not Configured'", applications);
        Assert.Contains("data-application-id='Healthy.App'", applications);
        string? snapshot = Environment.GetEnvironmentVariable("AEGIS_UI_SNAPSHOT_DIR");
        if (snapshot is not null) { Directory.CreateDirectory(snapshot); await File.WriteAllTextAsync(Path.Combine(snapshot, "applications.html"), applications, Ct); await File.WriteAllTextAsync(Path.Combine(snapshot, "overview.html"), overview, Ct); }
    }

    private sealed class MemoryRegistrations : ILifecycleRegistrationStore
    {
        private readonly LifecycleRegistrationRecord record = new("registration-rp", "RequestPortal", "Kratos", "installation-rp", LifecycleRegistrationState.Registered, "", DateTimeOffset.UtcNow, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, null, null, []);
        public Task<IReadOnlyList<LifecycleRegistrationRecord>> LoadAllAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<LifecycleRegistrationRecord>>([record]);
        public Task<LifecycleRegistrationRecord?> LoadAsync(string id, CancellationToken ct) => Task.FromResult<LifecycleRegistrationRecord?>(record);
        public Task<LifecycleRegistrationRecord?> LoadActiveAsync(string app, string instance, CancellationToken ct) => Task.FromResult<LifecycleRegistrationRecord?>(record);
        public Task SaveAsync(LifecycleRegistrationRecord record, CancellationToken ct) => Task.CompletedTask;
    }
}
