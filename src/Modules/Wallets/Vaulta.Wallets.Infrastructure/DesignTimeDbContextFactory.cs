using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Vaulta.Wallets.Infrastructure;

public sealed class WalletsDesignTimeDbContextFactory : IDesignTimeDbContextFactory<WalletsDbContext>
{
    public WalletsDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<WalletsDbContext>()
            .UseNpgsql("Host=localhost;Database=vaulta_design;Username=vaulta;Password=vaulta_dev")
            .Options;
        return new WalletsDbContext(options);
    }
}
