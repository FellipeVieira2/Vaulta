using System.Net.Http.Json;
using Vaulta.App.Core.Http;
using Vaulta.Payments.Contracts;

namespace Vaulta.App.Core.Payments;

public interface IPayoutsClient
{
    Task<PixDestinationDto?> GetDestinationAsync(CancellationToken ct = default);
    Task<PixDestinationDto> RegisterDestinationAsync(RegisterPixDestinationRequest request, CancellationToken ct = default);
    Task<SellerPayoutPageDto> GetPayoutsAsync(int page = 1, int pageSize = 20, CancellationToken ct = default);
}

public sealed class PayoutsClient(HttpClient http) : IPayoutsClient
{
    public async Task<PixDestinationDto?> GetDestinationAsync(CancellationToken ct = default)
    {
        using var response = await http.GetAsync("api/v1/payouts/pix", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
        if (response.IsSuccessStatusCode)
        {
            // A seller with no registered key receives JSON null.
            return await response.Content.ReadFromJsonAsync<PixDestinationDto>(ct);
        }
        return await response.ReadApiJsonAsync<PixDestinationDto>(ct);
    }
    public async Task<PixDestinationDto> RegisterDestinationAsync(RegisterPixDestinationRequest request, CancellationToken ct = default)
    {
        using var response = await http.PutAsJsonAsync("api/v1/payouts/pix", request, ct);
        return await response.ReadApiJsonAsync<PixDestinationDto>(ct);
    }
    public async Task<SellerPayoutPageDto> GetPayoutsAsync(int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        using var response = await http.GetAsync($"api/v1/payouts?page={page}&pageSize={pageSize}", ct);
        return await response.ReadApiJsonAsync<SellerPayoutPageDto>(ct);
    }
}
