using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Vaulta.Assets.Infrastructure;

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<AssetsDbContext>
{
    public AssetsDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Vaulta")
            ?? "Host=localhost;Database=vaulta;Username=vaulta";
        var options = new DbContextOptionsBuilder<AssetsDbContext>().UseNpgsql(connection).Options;
        return new AssetsDbContext(options);
    }
}
