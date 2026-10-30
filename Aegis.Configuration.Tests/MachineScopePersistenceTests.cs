using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

public sealed class MachineScopePersistenceTests
{
    [Fact]
    public async Task RegistrationScopeMigrationIsIdempotentAndPersistsRestrictedScopes()
    {
        var cancellation=TestContext.Current.CancellationToken;
        string connection=Environment.GetEnvironmentVariable("CONFIGURATION_TEST_POSTGRES") ?? throw new InvalidOperationException("CONFIGURATION_TEST_POSTGRES must point to a disposable PostgreSQL database.");
        string schema="security_registration_"+Guid.NewGuid().ToString("N");
        await using var admin=new NpgsqlConnection(connection);await admin.OpenAsync(cancellation);
        await using(var create=new NpgsqlCommand($"CREATE SCHEMA {schema}",admin))await create.ExecuteNonQueryAsync(cancellation);
        try
        {
            var scoped=new NpgsqlConnectionStringBuilder(connection){SearchPath=schema}.ConnectionString;
            var config=new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?> {
                ["AegisConfiguration:RegistrationConnectionString"]=scoped,["ControlPlane:Internal:Operations"]="https://operations.example.test" }).Build();
            using var telemetry=new RegistrationLifecycleTelemetryPublisher(config,new NoHttp(),NullLogger<RegistrationLifecycleTelemetryPublisher>.Instance);
            var store=new PostgresLifecycleRegistrationStore(config,telemetry);var now=DateTimeOffset.UtcNow;
            var record=new LifecycleRegistrationRecord("original-registration","original-app","original-instance","original-installation",LifecycleRegistrationState.Registered,
                "claim-hash",now.AddMinutes(5),"credential-hash",now,now,now,now,null,null,null,[]) {CredentialScopes=["registration.authenticate"]};
            await store.SaveAsync(record,cancellation);
            var restored=await new PostgresLifecycleRegistrationStore(config,telemetry).LoadAsync(record.RegistrationId,cancellation);
            Assert.NotNull(restored);Assert.Equal(record.InstallationId,restored.InstallationId);Assert.Equal(record.CredentialHash,restored.CredentialHash);
            Assert.Equal(new[]{"registration.authenticate"},restored.CredentialScopes);
            await store.SaveAsync(restored,cancellation);
            Assert.Single(await store.LoadAllAsync(cancellation));
        }
        finally{await using var drop=new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE",admin);await drop.ExecuteNonQueryAsync(cancellation);}
    }
    private sealed class NoHttp:IHttpClientFactory {public HttpClient CreateClient(string name)=>throw new InvalidOperationException("Persistence tests must not make HTTP requests.");}
}
