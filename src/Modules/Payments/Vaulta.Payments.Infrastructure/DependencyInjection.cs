using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Vaulta.Payments.Application;

namespace Vaulta.Payments.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddPaymentsModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<PaymentsDbContext>(options => options.UseNpgsql(configuration.GetConnectionString("Vaulta")));
        services.AddScoped<IPaymentStore, PaymentStore>();
        services.AddScoped<IPaymentGateway, AsaasPaymentGateway>();
        services.AddScoped<PaymentCommandHandlers>();

        services.AddHttpClient<AsaasPaymentGateway>(client =>
        {
            var baseUrl = configuration["Asaas:BaseUrl"] ?? "https://sandbox.asaas.com/api";
            client.BaseAddress = new Uri(baseUrl);
            client.DefaultRequestHeaders.Add("access_token", configuration["Asaas:ApiKey"]);
        });

        return services;
    }
}