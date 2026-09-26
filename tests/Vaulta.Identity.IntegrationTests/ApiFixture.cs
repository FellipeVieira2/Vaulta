using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Xunit;

namespace Vaulta.Identity.IntegrationTests;

public sealed class ApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17-alpine").Build();
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Vaulta"] = _postgres.GetConnectionString(),
                ["Jwt:Secret"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
                ["Jwt:Issuer"] = "vaulta-test", ["Jwt:Audience"] = "vaulta-test-client",
                ["Assets:S3:ServiceUrl"] = "http://127.0.0.1:9000",
                ["Assets:S3:AccessKey"] = "test-access-key",
                ["Assets:S3:SecretKey"] = "test-secret-key",
                ["Database:ApplyMigrations"] = "true", ["Outbox:Enabled"] = "false", ["RateLimit:AuthPermitLimit"] = "1000"
            }));
        });
        using var client = Factory.CreateClient();
        var readiness = await client.GetAsync("/health/ready");
        if (!readiness.IsSuccessStatusCode)
            throw new InvalidOperationException($"Readiness probe returned {(int)readiness.StatusCode}: {await readiness.Content.ReadAsStringAsync()}");
    }
    public async Task DisposeAsync() { await Factory.DisposeAsync(); await _postgres.DisposeAsync(); }
}
[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;
