using Microsoft.Extensions.Options;
using Vaulta.Catalog.Infrastructure.Recognition;
using Vaulta.SharedKernel;
using Xunit;

namespace Vaulta.Identity.UnitTests;

public sealed class TesseractOcrTests
{
    [Fact]
    public void Tsv_PreservesMultiwordNameAndSeparateCollectorNumberLine()
    {
        var tsv = "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n"
            + "5\t1\t1\t1\t1\t1\t10\t10\t40\t20\t95.5\tDark\n"
            + "5\t1\t1\t1\t1\t2\t50\t10\t80\t20\t96.2\tCharizard\n"
            + "5\t1\t2\t1\t1\t1\t10\t200\t60\t20\t86.7\t004/102\n";
        var result = TesseractOcrService.ParseTsv(tsv);
        Assert.Equal("Dark Charizard\n004/102", result.RawText);
        Assert.Equal(3, result.Regions.Count);
    }

    [Fact]
    public async Task InvalidImage_IsAValidationErrorInsteadOfNoMatch()
    {
        var service = new TesseractOcrService(Options.Create(new OcrOptions()));
        await Assert.ThrowsAsync<DomainException>(() => service.ExtractTextAsync([1, 2, 3], CancellationToken.None));
    }
}
