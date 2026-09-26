using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Vaulta.Catalog.Infrastructure;

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>()
            .UseNpgsql("Host=localhost;Database=vaulta;Username=postgres;Password=design-time-only")
            .Options;
        return new CatalogDbContext(options);
    }
}
