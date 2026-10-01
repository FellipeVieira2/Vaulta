using System.Net.Http.Json;
using Vaulta.App.Core.Http;
using Vaulta.Orders.Contracts;

namespace Vaulta.App.Core.Orders;

public interface IOrdersClient
{
    Task<OrderDto> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default);
    Task<OrderDto?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task<OrderPageDto> GetUserOrdersAsync(string? status = null, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
    Task ConfirmPaymentAsync(Guid orderId, string paymentId, CancellationToken cancellationToken = default);
    Task MarkShippedAsync(Guid orderId, string trackingCode, CancellationToken cancellationToken = default);
    Task ConfirmReceiptAsync(Guid orderId, CancellationToken cancellationToken = default);
    Task CancelOrderAsync(Guid orderId, string reason, CancellationToken cancellationToken = default);
    Task<Vaulta.Payments.Contracts.OrderRefundDto?> GetRefundAsync(Guid orderId, CancellationToken cancellationToken = default);
}

public sealed class OrdersClient(HttpClient httpClient) : IOrdersClient
{
    public async Task<OrderDto> CreateOrderAsync(CreateOrderRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("api/v1/orders", request, cancellationToken);
        return await response.ReadApiJsonAsync<OrderDto>(cancellationToken);
    }

    public async Task<OrderDto?> GetOrderAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"api/v1/orders/{orderId}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        return await response.ReadApiJsonAsync<OrderDto>(cancellationToken);
    }

    public async Task<OrderPageDto> GetUserOrdersAsync(string? status = null, int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var url = $"api/v1/orders?page={page}&pageSize={pageSize}";
        if (!string.IsNullOrWhiteSpace(status))
            url += $"&status={Uri.EscapeDataString(status)}";
        using var response = await httpClient.GetAsync(url, cancellationToken);
        return await response.ReadApiJsonAsync<OrderPageDto>(cancellationToken);
    }

    public async Task ConfirmPaymentAsync(Guid orderId, string paymentId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync($"api/v1/orders/{orderId}/confirm-payment",
            new ConfirmPaymentRequest(paymentId), cancellationToken);
        await response.EnsureApiSuccessAsync(cancellationToken);
    }

    public async Task MarkShippedAsync(Guid orderId, string trackingCode, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync($"api/v1/orders/{orderId}/ship",
            new MarkShippedRequest(trackingCode), cancellationToken);
        await response.EnsureApiSuccessAsync(cancellationToken);
    }

    public async Task CancelOrderAsync(Guid orderId, string reason, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync($"api/v1/orders/{orderId}/cancel",
            new CancelOrderRequest(reason), cancellationToken);
        await response.EnsureApiSuccessAsync(cancellationToken);
    }

    public async Task ConfirmReceiptAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsync($"api/v1/orders/{orderId}/deliver", null, cancellationToken);
        await response.EnsureApiSuccessAsync(cancellationToken);
    }

    public async Task<Vaulta.Payments.Contracts.OrderRefundDto?> GetRefundAsync(Guid orderId, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"api/v1/orders/{orderId}/refund", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
        return await response.ReadApiJsonAsync<Vaulta.Payments.Contracts.OrderRefundDto>(cancellationToken);
    }
}
