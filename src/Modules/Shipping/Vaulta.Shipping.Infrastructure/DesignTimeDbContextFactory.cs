using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Vaulta.Shipping.Infrastructure;

public sealed class ShippingDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ShippingDbContext>
{
    public ShippingDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ShippingDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=vaulta_design;Username=vaulta;Password=vaulta_dev");
        return new ShippingDbContext(optionsBuilder.Options);
    }
}