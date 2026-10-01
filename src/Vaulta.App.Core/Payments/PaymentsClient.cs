using System.Net.Http.Json;
using Vaulta.App.Core.Http;
using Vaulta.Payments.Contracts;

namespace Vaulta.App.Core.Payments;

public interface IPaymentsClient
{
    Task<PaymentDto> InitiatePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default);
    Task<BuyerCustomerDto?> GetCustomerAsync(CancellationToken cancellationToken = default);
    Task<BuyerCustomerDto> RegisterCustomerAsync(RegisterBuyerCustomerRequest request, CancellationToken cancellationToken = default);
    Task<PaymentDto?> GetOrderPaymentAsync(Guid orderId, CancellationToken cancellationToken = default);
}

public sealed class PaymentsClient(HttpClient httpClient) : IPaymentsClient
{
    public async Task<PaymentDto> InitiatePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("api/v1/payments", request, cancellationToken);
        return await response.ReadApiJsonAsync<PaymentDto>(cancellationToken);
    }

    public async Task<BuyerCustomerDto?> GetCustomerAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("api/v1/payments/customer", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
        return await response.ReadApiJsonAsync<BuyerCustomerDto>(cancellationToken);
    }
    public async Task<BuyerCustomerDto> RegisterCustomerAsync(RegisterBuyerCustomerRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PutAsJsonAsync("api/v1/payments/customer", request, cancellationToken);
        return await response.ReadApiJsonAsync<BuyerCustomerDto>(cancellationToken);
    }
    public async Task<PaymentDto?> GetOrderPaymentAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"api/v1/payments/orders/{orderId}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
        return await response.ReadApiJsonAsync<PaymentDto>(cancellationToken);
    }
}
