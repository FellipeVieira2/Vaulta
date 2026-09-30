using System.Net.Http.Json;
using Vaulta.App.Core.Http;

namespace Vaulta.App.Core.Address;

public sealed record ViaCepResponse(
    string? Cep,
    string? Logradouro,
    string? Complemento,
    string? Bairro,
    string? Localidade,
    string? Uf,
    bool Erro);

public interface IViaCepClient
{
    Task<ViaCepResponse?> GetByCepAsync(string cep, CancellationToken cancellationToken = default);
}

public sealed class ViaCepClient(HttpClient httpClient) : IViaCepClient
{
    public async Task<ViaCepResponse?> GetByCepAsync(string cep, CancellationToken cancellationToken = default)
    {
        var cleanCep = new string(cep.Where(char.IsDigit).ToArray());
        if (cleanCep.Length != 8) return null;

        using var response = await httpClient.GetAsync($"{cleanCep}/json/", cancellationToken);
        if (!response.IsSuccessStatusCode) return null;

        var result = await response.Content.ReadFromJsonAsync<ViaCepResponse>(cancellationToken);
        return result is { Erro: false } ? result : null;
    }
}