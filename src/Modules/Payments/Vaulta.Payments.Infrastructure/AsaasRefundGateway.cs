using System.Net.Http.Json;
using System.Text.Json;
using Vaulta.Payments.Application;
using Vaulta.SharedKernel;

namespace Vaulta.Payments.Infrastructure;

public sealed class AsaasRefundGateway(HttpClient http) : IRefundGateway
{
    public async Task<ProviderRefund> Get(string paymentId, Guid orderId, decimal grossAmount, CancellationToken ct)
    {
        RequireCredentials();
        using var response = await http.GetAsync($"v3/payments/{Uri.EscapeDataString(paymentId)}", ct);
        response.EnsureSuccessStatusCode();
        using var document = await Read(response, ct);
        return Parse(document.RootElement, paymentId, orderId, grossAmount);
    }

    public async Task<ProviderRefund> Request(string paymentId, Guid orderId, decimal grossAmount,
        string billingType, string reason, CancellationToken ct)
    {
        RequireCredentials();
        if (billingType is not ("PIX" or "CREDIT_CARD" or "BOLETO"))
            throw new DomainException("Meio de pagamento não permite este reembolso.");
        var endpoint = billingType == "BOLETO" ? "bankSlip/refund" : "refund";
        using var response = await http.PostAsJsonAsync($"v3/payments/{Uri.EscapeDataString(paymentId)}/{endpoint}",
            billingType == "BOLETO" ? (object)new { } : new { value = grossAmount, description = reason }, ct);
        response.EnsureSuccessStatusCode();
        using var document = await Read(response, ct);
        if (billingType != "BOLETO") return Parse(document.RootElement, paymentId, orderId, grossAmount);
        var url = Text(document.RootElement, "requestUrl");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https"
            || !(uri.Host == "asaas.com" || uri.Host.EndsWith(".asaas.com", StringComparison.OrdinalIgnoreCase)))
            throw new DomainException("Resposta inválida do reembolso de boleto.");
        return new(false, 0, grossAmount, url);
    }

    internal static ProviderRefund Parse(JsonElement root, string paymentId, Guid orderId, decimal amount)
    {
        if (Text(root, "id") != paymentId || !Guid.TryParse(Text(root, "externalReference"), out var reference)
            || reference != orderId || Number(root, "value") != amount
            || (root.TryGetProperty("deleted", out var deleted) && deleted.ValueKind != JsonValueKind.False))
            throw new ConflictException("Cobrança do provedor não corresponde ao pedido.");
        var completed = 0m;
        var pending = 0m;
        if (root.TryGetProperty("refunds", out var refunds) && refunds.ValueKind != JsonValueKind.Null)
        {
            if (refunds.ValueKind != JsonValueKind.Array) throw new DomainException("Estornos inválidos.");
            foreach (var refund in refunds.EnumerateArray())
            {
                var value = Number(refund, "value");
                if (value <= 0 || value != Math.Round(value, 2)) throw new DomainException("Valor de estorno inválido.");
                switch (Text(refund, "status"))
                {
                    case "DONE": completed += value; break;
                    case "PENDING": pending += value; break;
                    case "CANCELLED": break;
                    default: throw new DomainException("Estado de estorno desconhecido.");
                }
            }
        }
        if (completed + pending > amount) throw new ConflictException("Estornos excedem a cobrança.");
        var status = Text(root, "status");
        var awaitingSettlement = status == "CONFIRMED" && Text(root, "billingType") != "CREDIT_CARD";
        return new(status == "RECEIVED" || status == "CONFIRMED" && !awaitingSettlement, completed, pending,
            AwaitingSettlement: awaitingSettlement);
    }

    private void RequireCredentials()
    {
        if (!http.DefaultRequestHeaders.Contains("access_token")) throw new DomainException("Reembolso ainda não configurado.");
    }
    private static async Task<JsonDocument> Read(HttpResponseMessage response, CancellationToken ct)
    {
        try { return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); }
        catch (JsonException) { throw new DomainException("Resposta inválida do provedor de reembolso."); }
    }
    private static string Text(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? "" : throw new DomainException("Resposta de reembolso incompleta.");
    private static decimal Number(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var amount)
            ? amount : throw new DomainException("Resposta sem valor de reembolso.");
}
