using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Vaulta.Payments.Application;

namespace Vaulta.Payments.Infrastructure;

public sealed class AsaasPaymentGateway(HttpClient http, IConfiguration configuration, ILogger<AsaasPaymentGateway> logger) : IPaymentGateway
{
    private readonly string _apiKey = configuration["Asaas:ApiKey"] ?? throw new InvalidOperationException("Asaas:ApiKey not configured.");
    private readonly string _platformWalletId = configuration["Asaas:PlatformWalletId"] ?? throw new InvalidOperationException("Asaas:PlatformWalletId not configured.");

    public async Task<CreatePaymentResult> CreatePaymentAsync(GatewayPaymentRequest request, CancellationToken cancellationToken)
    {
        var payload = new AsaasPaymentPayload
        {
            Customer = request.CustomerAsaasId ?? "vaulta-placeholder-customer",
            BillingType = request.BillingType,
            Value = request.Amount,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            Description = request.Description,
            ExternalReference = request.ExternalReference,
            Split = request.Splits.Select(s => new AsaasSplitPayload
            {
                WalletId = s.WalletId,
                FixedValue = s.FixedValue,
                PercentualValue = s.PercentualValue,
                ExternalReference = s.ExternalReference,
                Description = s.Description
            }).ToArray()
        };

        var response = await http.PostAsJsonAsync("/v3/payments", payload, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            logger.LogError("Asaas payment creation failed: {StatusCode} {Body}", response.StatusCode, body);
            throw new HttpRequestException($"Asaas returned {(int)response.StatusCode}: {body}");
        }

        var result = JsonSerializer.Deserialize<AsaasPaymentResponse>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Failed to deserialize Asaas response.");

        return new CreatePaymentResult(
            result.Id,
            result.CheckoutUrl,
            result.PixQrCode,
            result.BankSlipUrl,
            result.Status);
    }

    private sealed class AsaasPaymentPayload
    {
        [JsonPropertyName("customer")] public string Customer { get; set; } = null!;
        [JsonPropertyName("billingType")] public string BillingType { get; set; } = null!;
        [JsonPropertyName("value")] public decimal Value { get; set; }
        [JsonPropertyName("dueDate")] public DateOnly DueDate { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("externalReference")] public string? ExternalReference { get; set; }
        [JsonPropertyName("split")] public AsaasSplitPayload[] Split { get; set; } = [];
    }

    private sealed class AsaasSplitPayload
    {
        [JsonPropertyName("walletId")] public string WalletId { get; set; } = null!;
        [JsonPropertyName("fixedValue")] public decimal? FixedValue { get; set; }
        [JsonPropertyName("percentualValue")] public decimal? PercentualValue { get; set; }
        [JsonPropertyName("externalReference")] public string? ExternalReference { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
    }

    private sealed class AsaasPaymentResponse
    {
        public string Id { get; set; } = null!;
        public string Status { get; set; } = null!;
        public string? CheckoutUrl { get; set; }
        public string? PixQrCode { get; set; }
        public string? BankSlipUrl { get; set; }
    }
}