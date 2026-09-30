using System.Net.Http.Json;
using Vaulta.App.Core.Http;
using Vaulta.Wallets.Contracts;

namespace Vaulta.App.Core.Wallets;

public interface IWalletsClient
{
    Task<WalletDto> GetMyWalletAsync(CancellationToken cancellationToken = default);
    Task<WalletPageDto> GetTransactionsAsync(int page = 1, int pageSize = 20, CancellationToken cancellationToken = default);
    Task<WalletDto> RequestWithdrawalAsync(WithdrawRequest request, CancellationToken cancellationToken = default);
}

public sealed class WalletsClient(HttpClient httpClient) : IWalletsClient
{
    public async Task<WalletDto> GetMyWalletAsync(CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync("api/v1/wallets", cancellationToken);
        return await response.ReadApiJsonAsync<WalletDto>(cancellationToken);
    }

    public async Task<WalletPageDto> GetTransactionsAsync(int page = 1, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync($"api/v1/wallets/transactions?page={page}&pageSize={pageSize}", cancellationToken);
        return await response.ReadApiJsonAsync<WalletPageDto>(cancellationToken);
    }

    public async Task<WalletDto> RequestWithdrawalAsync(WithdrawRequest request, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync("api/v1/wallets/withdraw", request, cancellationToken);
        return await response.ReadApiJsonAsync<WalletDto>(cancellationToken);
    }
}