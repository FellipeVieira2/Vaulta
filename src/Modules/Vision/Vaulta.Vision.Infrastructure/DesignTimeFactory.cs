using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace Vaulta.Vision.Infrastructure;
public sealed class DesignTimeFactory : IDesignTimeDbContextFactory<VisionDbContext>
{
 public VisionDbContext CreateDbContext(string[] args)=>new(new DbContextOptionsBuilder<VisionDbContext>().UseNpgsql("Host=localhost;Database=vaulta;Username=postgres;Password=design-time-only").Options);
}
