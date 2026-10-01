using System.Net.Http.Json;
using System.Text.Json;
using Vaulta.Payments.Application;
using Vaulta.SharedKernel;

namespace Vaulta.Payments.Infrastructure;

public sealed class AsaasBuyerCustomerGateway(HttpClient http) : IBuyerCustomerGateway
{
    public async Task<ProviderCustomer?> Find(string externalReference, CancellationToken ct)
    {
        RequireCredentials();
        using var response = await http.GetAsync($"v3/customers?externalReference={Uri.EscapeDataString(externalReference)}&limit=100", ct);
        response.EnsureSuccessStatusCode(); using var document = await Read(response, ct);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("hasMore", out var more) || more.ValueKind != JsonValueKind.False
            || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new ConflictException("Consulta de clientes incompleta; conciliação necessária.");
        var matches = data.EnumerateArray().Select(Parse).ToArray();
        if (matches.Length > 1) throw new ConflictException("Mais de um cliente corresponde ao comprador.");
        if (matches.Length == 1 && matches[0].ExternalReference != externalReference)
            throw new ConflictException("Cliente não corresponde à referência do comprador.");
        return matches.SingleOrDefault();
    }
    public async Task<ProviderCustomer> Create(string name, string document, string email, string externalReference, CancellationToken ct)
    {
        RequireCredentials();
        using var response = await http.PostAsJsonAsync("v3/customers", new { name, cpfCnpj = document, email,
            externalReference, notificationDisabled = true }, ct);
        response.EnsureSuccessStatusCode(); using var result = await Read(response, ct);
        return Parse(result.RootElement);
    }
    private static ProviderCustomer Parse(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("deleted", out var deleted) && deleted.ValueKind != JsonValueKind.False
            || !root.TryGetProperty("notificationDisabled", out var notifications) || notifications.ValueKind != JsonValueKind.True)
            throw new ConflictException("Cadastro de cobrança indisponível ou com notificações habilitadas.");
        return new(Text(root, "id"), Text(root, "externalReference"), Text(root, "cpfCnpj"), true);
    }
    private void RequireCredentials()
    {
        if (!http.DefaultRequestHeaders.Contains("access_token")) throw new DomainException("Integração de cobrança ainda não configurada.");
    }
    private static string Text(JsonElement root, string name) => root.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
        ? value.GetString()! : throw new DomainException("Resposta de cliente incompleta.");
    private static async Task<JsonDocument> Read(HttpResponseMessage response, CancellationToken ct)
    {
        try { return JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)); }
        catch (JsonException) { throw new DomainException("Resposta de cliente inválida."); }
    }
}
