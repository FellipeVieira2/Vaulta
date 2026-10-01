using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Vaulta.Payments.Application;
using Vaulta.SharedKernel;

namespace Vaulta.Payments.Infrastructure;

public sealed class AsaasPixPayoutGateway(HttpClient http) : IPixPayoutGateway
{
    public async Task<PixHolder> Lookup(string key, string keyType, CancellationToken ct)
    {
        RequireConfiguration();
        using var response = await http.GetAsync($"v3/pix/addressKeys/external?type={Uri.EscapeDataString(keyType)}&key={Uri.EscapeDataString(key)}", ct);
        if (!response.IsSuccessStatusCode) throw new DomainException("Não foi possível consultar a chave Pix. Confira a chave ou tente novamente mais tarde.");
        using var payload = await Read(response, ct);
        var root = payload.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("owner", out var owner))
            throw new DomainException("Resposta sem titular da chave Pix.");
        return new(Text(root, "key"), Text(root, "type"), Text(owner, "name"), Text(owner, "cpfCnpj"));
    }

    public async Task<PixTransfer> Transfer(PixTransferRequest request, CancellationToken ct)
    {
        RequireConfiguration();
        using var response = await http.PostAsJsonAsync("v3/transfers", new
        {
            value = request.Amount, pixAddressKey = request.Key, pixAddressKeyType = request.KeyType,
            operationType = "PIX", description = "Repasse de venda Vaulta", externalReference = request.ExternalReference
        }, ct);
        // No automatic POST retries: externalReference is correlation, not a
        // documented provider idempotency key. Never include response bodies/keys in logs.
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("Pix transfer could not be confirmed.", null, response.StatusCode);
        using var payload = await Read(response, ct);
        return Parse(payload.RootElement);
    }

    public async Task<bool> IsPaymentSettled(string paymentId, Guid orderId, decimal amount, decimal minimumNetAmount, CancellationToken ct)
    {
        RequireConfiguration();
        using var response = await http.GetAsync($"v3/payments/{Uri.EscapeDataString(paymentId)}", ct);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        if (!response.IsSuccessStatusCode) throw new HttpRequestException("Could not verify settled payment.");
        using var payload = await Read(response, ct);
        var root = payload.RootElement;
        return root.ValueKind == JsonValueKind.Object && Text(root, "id") == paymentId
            && Text(root, "status") == "RECEIVED"
            && Guid.TryParse(Text(root, "externalReference"), out var reference) && reference == orderId
            && root.TryGetProperty("value", out var gross) && gross.ValueKind == JsonValueKind.Number
            && gross.TryGetDecimal(out var grossAmount) && grossAmount == amount
            && root.TryGetProperty("netValue", out var net) && net.ValueKind == JsonValueKind.Number
            && net.TryGetDecimal(out var netAmount) && netAmount == minimumNetAmount
            && (!root.TryGetProperty("deleted", out var deleted) || deleted.ValueKind == JsonValueKind.False);
    }

    public async Task<PixTransfer?> FindTransfer(string? transferId, string externalReference, DateTimeOffset submittedAt, CancellationToken ct)
    {
        RequireConfiguration();
        if (transferId is not null)
        {
            using var response = await http.GetAsync($"v3/transfers/{Uri.EscapeDataString(transferId)}", ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return null;
            if (!response.IsSuccessStatusCode) throw new HttpRequestException("Could not reconcile Pix transfer.");
            using var payload = await Read(response, ct);
            return Parse(payload.RootElement);
        }
        var from = submittedAt.UtcDateTime.AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var matches = new List<PixTransfer>();
        for (var offset = 0; offset < 1000; offset += 100)
        {
            using var response = await http.GetAsync($"v3/transfers?dateCreated%5Bge%5D={from}&limit=100&offset={offset}", ct);
            if (!response.IsSuccessStatusCode) throw new HttpRequestException("Could not reconcile Pix transfer.");
            using var payload = await Read(response, ct);
            var root = payload.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array
                || !root.TryGetProperty("hasMore", out var more) || more.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                throw new DomainException("Resposta incompleta da conciliação Pix.");
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("externalReference", out var reference) && reference.ValueKind == JsonValueKind.String
                    && reference.GetString() == externalReference) matches.Add(Parse(item));
            }
            if (matches.Count > 1) throw new ConflictException("More than one transfer matches this payout.");
            if (!more.GetBoolean()) return matches.SingleOrDefault();
        }
        // An incomplete/empty search never authorizes another POST.
        throw new ConflictException("Transfer reconciliation needs additional provider records.");
    }

    private void RequireConfiguration()
    {
        if (!http.DefaultRequestHeaders.Contains("access_token")) throw new DomainException("Integração Pix ainda não configurada.");
    }
    private static async Task<JsonDocument> Read(HttpResponseMessage response, CancellationToken ct)
    {
        try { return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); }
        catch (JsonException) { throw new DomainException("Resposta inválida do provedor Pix."); }
    }
    private static string Text(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value)
            || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
            throw new DomainException("Resposta incompleta do provedor Pix.");
        return value.GetString()!;
    }
    private static PixTransfer Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("value", out var value)
            || value.ValueKind != JsonValueKind.Number || !value.TryGetDecimal(out var amount))
            throw new DomainException("Resposta sem valor de transferência.");
        return new(Text(root, "id"), Text(root, "status"), amount, Text(root, "externalReference"),
            Decimal(root, "netValue"), Decimal(root, "transferFee"));
    }
    private static decimal Decimal(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var amount)
            ? amount : throw new DomainException("Resposta sem valores líquidos da transferência.");
}
