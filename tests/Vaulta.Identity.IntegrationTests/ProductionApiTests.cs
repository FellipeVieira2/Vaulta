using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Vaulta.Identity.IntegrationTests;

[Collection("api")]
public sealed class ProductionApiTests(ApiFixture fixture)
{
    [Fact]
    public async Task ProductionStartsWithoutStaticAwsKeysAndKeepsHealthLocalAndSwaggerDisabled()
    {
        await using var factory = fixture.Factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Assets:S3:ServiceUrl"] = null, ["Assets:S3:AccessKey"] = null, ["Assets:S3:SecretKey"] = null,
                ["Assets:S3:Bucket"] = "vaulta-assets-test", ["Assets:S3:Region"] = "us-east-1",
                ["Assets:S3:ForcePathStyle"] = "false", ["Database:ApplyMigrations"] = "false",
                ["Outbox:Enabled"] = "true"
            }));
        });
        using var http = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/health/ready")).StatusCode);
        Assert.Equal(HttpStatusCode.TemporaryRedirect, (await http.GetAsync("/api/v1/me")).StatusCode);
        using var https = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.NotFound, (await https.GetAsync("/swagger/v1/swagger.json")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await https.GetAsync("/api/v1/me")).StatusCode);
    }
}
