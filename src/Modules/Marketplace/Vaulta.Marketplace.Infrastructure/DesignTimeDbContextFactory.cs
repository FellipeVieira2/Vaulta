using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Vaulta.Marketplace.Infrastructure;

public sealed class MarketplaceDesignTimeDbContextFactory : IDesignTimeDbContextFactory<MarketplaceDbContext>
{
    public MarketplaceDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<MarketplaceDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=vaulta_design;Username=vaulta;Password=vaulta_dev");
        return new MarketplaceDbContext(optionsBuilder.Options);
    }
}