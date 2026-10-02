using Amazon.BedrockRuntime;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vaulta.Catalog.Application;
using Vaulta.Catalog.Infrastructure;
using Vaulta.Catalog.Infrastructure.Recognition;
using Xunit;

namespace Vaulta.Identity.UnitTests;

public sealed class NovaScannerRegistrationTests
{
    [Fact]
    public void Module_OcrIsDefaultAndDoesNotRegisterAwsInferenceClient()
    {
        var services = Services(new Dictionary<string, string?>());
        Assert.DoesNotContain(services, x => x.ServiceType == typeof(IAmazonBedrockRuntime));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<PokemonTcgRecognitionProvider>(Assert.Single(scope.ServiceProvider.GetServices<ICardRecognitionProvider>()));
    }

    [Fact]
    public void Module_EnabledNovaUsesEvidenceProviderWithExistingOcrFallback()
    {
        var services = Services(new Dictionary<string, string?> { ["Scanner:Nova:Enabled"] = "true" });
        services.AddSingleton<ICardEvidenceExtractor, NoEvidence>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<NovaCardRecognitionProvider>(Assert.Single(scope.ServiceProvider.GetServices<ICardRecognitionProvider>()));
    }

    [Theory]
    [InlineData("MaxTokens", "0")]
    [InlineData("TimeoutSeconds", "121")]
    [InlineData("RetryCount", "9")]
    [InlineData("MaxConcurrency", "20")]
    public void Module_EnabledNovaRejectsUnboundedConfiguration(string setting, string value)
    {
        var services = Services(new Dictionary<string, string?> { ["Scanner:Nova:Enabled"] = "true", [$"Scanner:Nova:{setting}"] = value });
        using var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<NovaScannerOptions>>().Value);
    }

    private static IServiceCollection Services(Dictionary<string, string?> values)
    {
        values["ConnectionStrings:Vaulta"] = "Host=localhost;Database=vaulta;Username=test;Password=test";
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return new ServiceCollection().AddLogging().AddCatalogModule(configuration);
    }

    private sealed class NoEvidence : ICardEvidenceExtractor
    {
        public Task<CardEvidence?> ExtractAsync(byte[] imageData, CancellationToken cancellationToken) => Task.FromResult<CardEvidence?>(null);
    }
}
