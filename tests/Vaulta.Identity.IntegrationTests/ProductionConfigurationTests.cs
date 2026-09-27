using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Vaulta.Web.Api;
using Xunit;

namespace Vaulta.Identity.IntegrationTests;

public sealed class ProductionConfigurationTests
{
    internal static Dictionary<string, string?> Valid() => new()
    {
        ["ConnectionStrings:Vaulta"] = "Host=postgres;Database=vaulta;Username=vaulta;Password=disposable-test-value",
        ["Jwt:Secret"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
        ["Jwt:Issuer"] = "vaulta", ["Jwt:Audience"] = "vaulta-app",
        ["Assets:S3:Bucket"] = "vaulta-assets-test", ["Assets:S3:Region"] = "us-east-1",
        ["Assets:S3:ForcePathStyle"] = "false"
    };

    [Fact]
    public void RoleBasedProductionConfigurationIsValid() =>
        ProductionConfiguration.Validate(new ConfigurationBuilder().AddInMemoryCollection(Valid()).Build());

    [Theory]
    [InlineData("ConnectionStrings:Vaulta")]
    [InlineData("Jwt:Secret")]
    [InlineData("Jwt:Issuer")]
    [InlineData("Jwt:Audience")]
    [InlineData("Assets:S3:Bucket")]
    [InlineData("Assets:S3:Region")]
    public void MissingConfigurationFailsByKeyWithoutValues(string key)
    {
        var values = Valid();
        values[key] = " ";
        var error = Assert.Throws<InvalidOperationException>(() =>
            ProductionConfiguration.Validate(new ConfigurationBuilder().AddInMemoryCollection(values).Build()));
        Assert.Contains(key, error.Message);
        if (key != "Jwt:Secret") Assert.DoesNotContain(values["Jwt:Secret"]!, error.Message);
        Assert.DoesNotContain("disposable-test-value", error.Message);
    }

    [Theory]
    [InlineData("Jwt:Secret", "short")]
    [InlineData("Jwt:Secret", "development-secret-with-more-than-32-bytes")]
    [InlineData("Jwt:Secret", "change-me-with-more-than-thirty-two-bytes")]
    [InlineData("Assets:S3:ServiceUrl", "http://minio:9000")]
    [InlineData("Assets:S3:AccessKey", "not-a-real-key")]
    [InlineData("Assets:S3:SecretKey", "not-a-real-secret")]
    [InlineData("AWS_ACCESS_KEY_ID", "not-a-real-key")]
    [InlineData("AWS_SECRET_ACCESS_KEY", "not-a-real-secret")]
    [InlineData("AWS_SESSION_TOKEN", "not-a-real-token")]
    [InlineData("Assets:S3:ForcePathStyle", "true")]
    [InlineData("Database:ApplyMigrations", "true")]
    [InlineData("Outbox:Enabled", "false")]
    [InlineData("RateLimit:AuthPermitLimit", "0")]
    [InlineData("Cors:Origins:0", "*")]
    [InlineData("Cors:Origins:0", "http://example.com")]
    [InlineData("Cors:Origins:0", "https://example.com/path")]
    [InlineData("ConnectionStrings:Vaulta", "invalid=disposable-test-value")]
    public void UnsafeConfigurationFailsWithoutEchoingValues(string key, string value)
    {
        var values = Valid();
        values[key] = value;
        var error = Assert.Throws<InvalidOperationException>(() =>
            ProductionConfiguration.Validate(new ConfigurationBuilder().AddInMemoryCollection(values).Build()));
        Assert.Contains(key.Split(':')[0], error.Message);
        Assert.Null(error.InnerException);
        Assert.DoesNotContain("disposable-test-value", error.Message);
    }

    [Fact]
    public void EmptyCorsAndExplicitHttpsOriginsAreAccepted()
    {
        var values = Valid();
        values["Cors:Origins:0"] = "";
        values["Cors:Origins:1"] = "https://app.example.com";
        ProductionConfiguration.Validate(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
    }
}
