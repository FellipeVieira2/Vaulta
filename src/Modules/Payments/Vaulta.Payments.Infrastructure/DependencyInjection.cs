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
        services.AddScoped<IBuyerCustomerStore, BuyerCustomerStore>();
        services.AddScoped<IBuyerCustomerIdentity, BuyerCustomerIdentity>();
        services.AddScoped<BuyerCustomerService>();
        services.AddScoped<IBuyerCustomerAccess>(provider => provider.GetRequiredService<BuyerCustomerService>());
        services.AddHttpClient<IBuyerCustomerGateway, AsaasBuyerCustomerGateway>(client =>
        {
            client.BaseAddress = new Uri(configuration["Asaas:BaseUrl"] ?? "https://api-sandbox.asaas.com/");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Vaulta-Customers/1.0");
            var apiKey = configuration["Asaas:ApiKey"];
            if (!string.IsNullOrWhiteSpace(apiKey)) client.DefaultRequestHeaders.Add("access_token", apiKey);
        }).RemoveAllLoggers();
        services.AddScoped<PaymentCommandHandlers>();
        services.AddScoped<IPayoutStore, PayoutStore>();
        services.AddScoped<IPayoutIdentity, PayoutIdentity>();
        services.AddScoped<SellerPayoutService>();
        services.AddScoped<IRefundStore, RefundStore>();
        services.AddScoped<PaymentRefundService>();
        if (configuration.GetValue("Refunds:WorkerEnabled", true)) services.AddHostedService<PaymentRefundWorker>();
        if (configuration.GetValue("Refunds:RequestsEnabled", false)
            && (string.IsNullOrWhiteSpace(configuration["Asaas:ApiKey"]) || string.IsNullOrWhiteSpace(configuration["Asaas:WebhookToken"])))
            throw new InvalidOperationException("Refund requests require provider credentials and an authenticated webhook.");
        services.AddHttpClient<IRefundGateway, AsaasRefundGateway>(client =>
        {
            client.BaseAddress = new Uri(configuration["Asaas:BaseUrl"] ?? "https://api-sandbox.asaas.com/");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Vaulta-Refunds/1.0");
            var apiKey = configuration["Asaas:ApiKey"];
            if (!string.IsNullOrWhiteSpace(apiKey)) client.DefaultRequestHeaders.Add("access_token", apiKey);
        }).RemoveAllLoggers();
        if (configuration.GetValue("Payouts:WorkerEnabled", true)) services.AddHostedService<SellerPayoutWorker>();
        if (configuration.GetValue("Payouts:TransfersEnabled", false)
            && (string.IsNullOrWhiteSpace(configuration["Asaas:ApiKey"]) || string.IsNullOrWhiteSpace(configuration["Asaas:WebhookToken"])))
            throw new InvalidOperationException("Pix payouts require provider credentials and an authenticated webhook.");

        services.AddHttpClient<IPixPayoutGateway, AsaasPixPayoutGateway>(client =>
        {
            client.BaseAddress = new Uri(configuration["Asaas:BaseUrl"] ?? "https://api-sandbox.asaas.com/");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Vaulta-Payouts/1.0");
            var apiKey = configuration["Asaas:ApiKey"];
            if (!string.IsNullOrWhiteSpace(apiKey)) client.DefaultRequestHeaders.Add("access_token", apiKey);
        }).RemoveAllLoggers(); // Pix lookup URLs contain private keys/documents.

        services.AddHttpClient<IPaymentGateway, AsaasPaymentGateway>(client =>
        {
            var baseUrl = configuration["Asaas:BaseUrl"] ?? "https://api-sandbox.asaas.com/";
            client.BaseAddress = new Uri(baseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Vaulta-Payments/1.0");
            var apiKey = configuration["Asaas:ApiKey"];
            if (!string.IsNullOrWhiteSpace(apiKey)) client.DefaultRequestHeaders.Add("access_token", apiKey);
        });

        return services;
    }
}
