using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Vaulta.Payments.Application;
using Vaulta.SharedKernel;

namespace Vaulta.Payments.Infrastructure;

public sealed class AsaasPaymentGateway(HttpClient http, IConfiguration configuration, ILogger<AsaasPaymentGateway> logger) : IPaymentGateway
{
    public async Task<CreatePaymentResult> CreatePaymentAsync(GatewayPaymentRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(configuration["Asaas:ApiKey"]))
            throw new InvalidOperationException("Asaas:ApiKey is not configured.");
        if (string.IsNullOrWhiteSpace(request.CustomerAsaasId))
            throw new DomainException("An Asaas customer identifier is required.");
        var payload = new AsaasPaymentPayload
        {
            Customer = request.CustomerAsaasId,
            BillingType = request.BillingType,
            Value = request.Amount,
            DueDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)),
            Description = request.Description,
            ExternalReference = request.ExternalReference,
            Split = request.Splits.Count == 0 ? null : request.Splits.Select(s => new AsaasSplitPayload
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
            logger.LogError("Asaas payment creation failed with HTTP {StatusCode}", (int)response.StatusCode);
            throw new HttpRequestException("The payment provider could not create the charge.", null, response.StatusCode);
        }

        using var document = Read(body);
        return ParsePayment(document.RootElement, request);
    }

    public async Task<CreatePaymentResult?> FindPaymentAsync(GatewayPaymentRequest request, CancellationToken ct)
    {
        using var response = await http.GetAsync($"v3/payments?externalReference={Uri.EscapeDataString(request.ExternalReference)}&customer={Uri.EscapeDataString(request.CustomerAsaasId!)}&limit=100", ct);
        response.EnsureSuccessStatusCode(); using var document = Read(await response.Content.ReadAsStringAsync(ct));
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("hasMore", out var more) || more.ValueKind != JsonValueKind.False
            || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new ConflictException("Consulta de cobranças incompleta.");
        var matches = data.EnumerateArray().Select(item => ParsePayment(item, request)).ToArray();
        if (matches.Length > 1) throw new ConflictException("Mais de uma cobrança corresponde ao pedido.");
        return matches.SingleOrDefault();
    }

    public async Task<GatewayPixPayload> GetPixPayloadAsync(string paymentId, CancellationToken ct)
    {
        using var response = await http.GetAsync($"v3/payments/{Uri.EscapeDataString(paymentId)}/pixQrCode", ct);
        response.EnsureSuccessStatusCode(); using var document = Read(await response.Content.ReadAsStringAsync(ct));
        var expiration = OptionalText(document.RootElement, "expirationDate");
        if (expiration is { Length: > 64 }) throw new DomainException("Validade Pix inválida.");
        return new(Text(document.RootElement, "payload"), expiration);
    }

    private static CreatePaymentResult ParsePayment(JsonElement root, GatewayPaymentRequest expected)
    {
        if (Text(root, "customer") != expected.CustomerAsaasId || Text(root, "externalReference") != expected.ExternalReference
            || Text(root, "billingType") != expected.BillingType || !root.TryGetProperty("value", out var value)
            || value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var amount) || amount != expected.Amount
            || root.TryGetProperty("deleted", out var deleted) && deleted.ValueKind != JsonValueKind.False)
            throw new ConflictException("Cobrança não corresponde ao comprador ou pedido.");
        var invoice = OptionalText(root, "invoiceUrl");
        if (invoice is not null && (!Uri.TryCreate(invoice, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || !(uri.Host == "asaas.com" || uri.Host.EndsWith(".asaas.com", StringComparison.OrdinalIgnoreCase))))
            throw new DomainException("URL de cobrança inválida.");
        return new(Text(root, "id"), invoice, null, OptionalText(root, "bankSlipUrl"), Text(root, "status"));
    }
    private static string Text(JsonElement root, string name) => OptionalText(root, name)
        ?? throw new DomainException("Resposta de cobrança incompleta.");
    private static string? OptionalText(JsonElement root, string name) => root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
        ? value.GetString() : null;
    private static JsonDocument Read(string body)
    {
        try { return JsonDocument.Parse(body); }
        catch (JsonException) { throw new DomainException("Resposta de cobrança inválida."); }
    }

    private sealed class AsaasPaymentPayload
    {
        [JsonPropertyName("customer")] public string Customer { get; set; } = null!;
        [JsonPropertyName("billingType")] public string BillingType { get; set; } = null!;
        [JsonPropertyName("value")] public decimal Value { get; set; }
        [JsonPropertyName("dueDate")] public DateOnly DueDate { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("externalReference")] public string? ExternalReference { get; set; }
        [JsonPropertyName("split"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public AsaasSplitPayload[]? Split { get; set; }
    }

    private sealed class AsaasSplitPayload
    {
        [JsonPropertyName("walletId")] public string WalletId { get; set; } = null!;
        [JsonPropertyName("fixedValue")] public decimal? FixedValue { get; set; }
        [JsonPropertyName("percentualValue")] public decimal? PercentualValue { get; set; }
        [JsonPropertyName("externalReference")] public string? ExternalReference { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
    }

}
