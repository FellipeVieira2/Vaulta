using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vaulta.Reviews.Application;

namespace Vaulta.Reviews.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddReviewsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<ReviewsDbContext>(options => options.UseNpgsql(configuration.GetConnectionString("Vaulta")));
        services.AddScoped<IReviewStore, ReviewStore>();
        services.AddScoped<IReviewQueries, ReviewQueries>();
        services.AddScoped<IReviewOrders, ReviewOrdersAdapter>();
        services.AddScoped<ReviewCommandHandlers>();
        return services;
    }
}