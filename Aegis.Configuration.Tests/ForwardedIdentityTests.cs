using System.Text;
using Common.Security.Abstractions;
using Common.Security.Hosting;
using Common.Security.Integration;
using Common.Security.Models;
using Common.Security.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

public sealed class ForwardedIdentityTests
{
    const string SigningKey = "test-signing-key-for-forwarded-identities";

    [Fact]
    public void CurrentSignedHandoffTakesPrecedenceAndCannotReplayOrFallBackToCookie()
    {
        string path = Path.Combine(Path.GetTempPath(), "configuration-security-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var accounts = new RelationalAccountStore(() => new SqliteConnection($"Data Source={path};Pooling=False"), "control-plane");
            accounts.EnsureSchema();
            var viewerKey = new ExternalIdentityKey("active-directory", "directory", "viewer-guid");
            var managerKey = new ExternalIdentityKey("active-directory", "directory", "manager-guid");
            var viewer = accounts.Create(new("viewer-account", "viewer", "Viewer", AuthenticationProviderKind.ActiveDirectory,
                AccountPopulation.Staff, SecurityAccountStatus.Active, viewerKey), "test-administrator");
            var manager = accounts.Create(new("manager-account", "manager", "Manager", AuthenticationProviderKind.ActiveDirectory,
                AccountPopulation.Staff, SecurityAccountStatus.Active, managerKey), "test-administrator");
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
            {
                ["SuiteSecurity:SigningKey"] = SigningKey,
                ["SuiteSecurity:AccountRoles:viewer-account:0"] = "Reader",
                ["SuiteSecurity:AccountRoles:manager-account:0"] = "Administrator"
            }).Build();
            using var services = new ServiceCollection().AddSingleton(accounts).BuildServiceProvider();
            var security = new SuiteSecurity(configuration, new TestEnvironment(), services, accounts,
                new("Aegis.Configuration", "Aegis.Configuration.Session", new Dictionary<string,string[]>
                { ["Reader"] = ["Configuration.View"], ["Administrator"] = ["Configuration.View","Configuration.Manage"] }));
            var cookie = security.CreatePrincipal(new("viewer", "Viewer", "Reader", ["Configuration.View"], viewer.Id,
                viewer.SessionVersion, viewerKey, AuthenticationProviderKind.ActiveDirectory, DateTimeOffset.UtcNow));
            string ticket = new SessionTicketProtector(Encoding.UTF8.GetBytes(SigningKey)).Create(manager.Id,
                "Aegis.Configuration", manager.SessionVersion, TimeSpan.FromSeconds(45), "handoff", managerKey);
            DefaultHttpContext Request(string? token)
            {
                var context = new DefaultHttpContext { User = cookie };
                if (token is not null) context.Request.Headers["X-Aegis-Suite-Handoff"] = token;
                return context;
            }
            var current = Request(ticket);
            Assert.Equal("manager", security.Read(current.Request)!.UserName);
            Assert.Contains("Configuration.Manage", security.Read(current.Request)!.Permissions);
            Assert.Null(security.Read(Request(ticket).Request)); // single use, no fallback to viewer cookie
            Assert.Null(security.Read(Request("invalid").Request));
            Assert.Equal("viewer", security.Read(Request(null).Request)!.UserName);
            accounts.SetStatus(viewer.Id, SecurityAccountStatus.Disabled, "test-administrator");
            Assert.Null(security.Read(Request(null).Request));
        }
        finally { File.Delete(path); }
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
