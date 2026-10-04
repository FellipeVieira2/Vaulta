using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Vaulta.Catalog.Infrastructure.Artwork;
using Xunit;

namespace Vaulta.Identity.UnitTests.Catalog;

public sealed class ArtworkImportOptionsTests
{
    [Fact]
    public void Defaults_Are_Conservative_For_Micro_Instance()
    {
        var options = new ArtworkImportOptions();
        Assert.Equal(3, options.WorkerCount);
        Assert.Equal(200, options.PageSize);
        Assert.Equal(24, options.RevalidateAfterHours);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(17)]
    [InlineData(100)]
    public void Invalid_WorkerCount_Fails_Validation(int invalidCount)
    {
        var services = new ServiceCollection();
        services.AddOptions<ArtworkImportOptions>()
            .Configure(o => o.WorkerCount = invalidCount)
            .Validate(o => o.WorkerCount is >= 1 and <= 16, "WorkerCount must be between 1 and 16.");
        var provider = services.BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() =>
            provider.GetRequiredService<IOptions<ArtworkImportOptions>>().Value);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(16)]
    public void Valid_WorkerCount_Passes_Validation(int validCount)
    {
        var services = new ServiceCollection();
        services.AddOptions<ArtworkImportOptions>()
            .Configure(o => o.WorkerCount = validCount)
            .Validate(o => o.WorkerCount is >= 1 and <= 16, "WorkerCount must be between 1 and 16.");
        var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<ArtworkImportOptions>>().Value;
        Assert.Equal(validCount, options.WorkerCount);
    }
}