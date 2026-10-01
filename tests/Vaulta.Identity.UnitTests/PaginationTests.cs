using Vaulta.SharedKernel;
using Xunit;

namespace Vaulta.Identity.UnitTests;

public sealed class PaginationTests
{
    [Theory]
    [InlineData(-1, 0, 1, 1, 0)]
    [InlineData(2, 10000, 2, 100, 100)]
    [InlineData(3, 20, 3, 20, 40)]
    public void NormalizesBoundedWindow(int page, int size, int expectedPage, int expectedSize, int offset)
    {
        Assert.Equal((expectedPage, expectedSize, offset), Pagination.Normalize(page, size));
    }

    [Fact]
    public void RejectsOffsetOverflowInsteadOfProducingNegativeSkip() =>
        Assert.Throws<DomainException>(() => Pagination.Normalize(int.MaxValue, 100));
}
