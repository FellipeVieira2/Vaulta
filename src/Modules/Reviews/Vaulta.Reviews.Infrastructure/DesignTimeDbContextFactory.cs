using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Vaulta.Reviews.Infrastructure;

public sealed class ReviewsDesignTimeDbContextFactory : IDesignTimeDbContextFactory<ReviewsDbContext>
{
    public ReviewsDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ReviewsDbContext>();
        optionsBuilder.UseNpgsql("Host=localhost;Database=vaulta_design;Username=vaulta;Password=vaulta_dev");
        return new ReviewsDbContext(optionsBuilder.Options);
    }
}