using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Vaulta.Collection.Infrastructure;

public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<CollectionDbContext>
{
    public CollectionDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("ConnectionStrings__Vaulta")
            ?? "Host=localhost;Database=vaulta;Username=vaulta";
        var options = new DbContextOptionsBuilder<CollectionDbContext>().UseNpgsql(connection).Options;
        return new CollectionDbContext(options);
    }
}
