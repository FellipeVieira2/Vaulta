using System.Security.Cryptography;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Xunit;

namespace Vaulta.Identity.IntegrationTests;

public sealed class ApiFixture : IAsyncLifetime
{
    // Optional isolated database supplied by the test runner; avoids mounting the
    // Docker socket into a container merely to run HTTP/database tests.
    private readonly string? _externalPostgres = Environment.GetEnvironmentVariable("VAULTA_TEST_POSTGRES");
    private readonly PostgreSqlContainer? _postgres = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VAULTA_TEST_POSTGRES"))
        ? new PostgreSqlBuilder("postgres:17-alpine").Build() : null;
    private readonly string _jwtSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    public WebApplicationFactory<Program> Factory { get; private set; } = null!;
    public string WebhookToken { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    public async Task InitializeAsync()
    {
        if (_postgres is not null) await _postgres.StartAsync();
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Vaulta"] = _postgres?.GetConnectionString() ?? _externalPostgres!,
                ["Jwt:Secret"] = _jwtSecret,
                ["Jwt:Issuer"] = "vaulta-test", ["Jwt:Audience"] = "vaulta-test-client",
                ["Asaas:WebhookToken"] = WebhookToken,
                ["Payouts:WorkerEnabled"] = "false",
                ["Refunds:WorkerEnabled"] = "false",
                ["Orders:ReleaseWorkerEnabled"] = "false",
                ["Orders:CollectionWorkerEnabled"] = "false",
                ["Marketplace:CollectionWorkerEnabled"] = "false",
                ["Catalog:MarketPrices:Refresh:Enabled"] = "false",
                ["Scanner:OpenAI:Enabled"] = "false",
                ["Scanner:OpenAI:ApiKey"] = "",
                ["Scanner:JustTCG:ApiKey"] = "",
                ["OPENAI_API_KEY"] = "", ["JUSTTCG_API_KEY"] = "",
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
    public async Task DisposeAsync() { if (Factory is not null) await Factory.DisposeAsync(); if (_postgres is not null) await _postgres.DisposeAsync(); }
}
[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;
