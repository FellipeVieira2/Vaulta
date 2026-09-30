using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Vaulta.Payments.Infrastructure;

public sealed class PaymentsDesignTimeDbContextFactory : IDesignTimeDbContextFactory<PaymentsDbContext>
{
    public PaymentsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<PaymentsDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=vaulta_design;Username=vaulta;Password=vaulta_dev");
        return new PaymentsDbContext(optionsBuilder.Options);
    }
}