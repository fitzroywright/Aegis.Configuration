using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

public sealed class ForwardedIdentityTests
{
    const string SigningKey = "test-signing-key-for-forwarded-identities";

    [Fact]
    public void Signed_operations_identity_takes_precedence_over_stale_configuration_cookie()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var security = new SuiteSecurity(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> {
                ["SuiteSecurity:SigningKey"] = SigningKey }).Build(),
            new TestEnvironment(), services);
        var context = new DefaultHttpContext();
        context.Request.Headers.Cookie = "Aegis.Configuration.Session=" + Ticket("viewer", ["Configuration.View"]);
        context.Request.Headers["X-Aegis-Suite-Handoff"] = Ticket("manager", ["Configuration.View", "Configuration.Manage"]);
        var identity = security.ReadRequest(context.Request);
        Assert.Equal("manager", identity!.UserName);
        Assert.Contains("Configuration.Manage", identity.Permissions);
        context.Request.Headers["X-Aegis-Suite-Handoff"] = Ticket("viewer", ["Configuration.View"]);
        Assert.DoesNotContain("Configuration.Manage", security.ReadRequest(context.Request)!.Permissions);
        context.Request.Headers["X-Aegis-Suite-Handoff"] = "invalid";
        Assert.Null(security.ReadRequest(context.Request));
        context.Request.Headers.Remove("X-Aegis-Suite-Handoff");
        Assert.Equal("viewer", security.ReadRequest(context.Request)!.UserName);
    }

    private static string Ticket(string name, string[] permissions)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new SuiteTicket(
            name, name, "User", permissions, "Aegis.Configuration",
            DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds(), Guid.NewGuid().ToString("N")));
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(SigningKey));
        return WebEncoders.Base64UrlEncode(payload) + "." +
            WebEncoders.Base64UrlEncode(hmac.ComputeHash(payload));
    }

    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
