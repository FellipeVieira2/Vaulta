using System.Net.Http.Json;
using Vaulta.App.Core.Http;
using Vaulta.Payments.Contracts;

namespace Vaulta.App.Core.Payments;

public interface IPaymentsClient
{
    Task<PaymentDto> InitiatePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default);
}

public sealed class PaymentsClient(HttpClient httpClient) : IPaymentsClient
{
    public async Task<PaymentDto> InitiatePaymentAsync(CreatePaymentRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("api/v1/payments", request, cancellationToken);
        return await response.ReadApiJsonAsync<PaymentDto>(cancellationToken);
    }
}