using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Vaulta.Wallets.Application;
using Vaulta.Wallets.Application.Commands;
using Vaulta.Wallets.Contracts;

namespace Vaulta.Web.Api;

public static class WalletEndpoints
{
    public static void MapWalletEndpoints(this WebApplication app)
    {
        var wallets = app.MapGroup("/api/v1/wallets").WithTags("Wallets").RequireAuthorization();

        wallets.MapGet("", async (ClaimsPrincipal principal, IWalletStore store, CancellationToken ct) =>
        {
            var userId = Guid.Parse(principal.FindFirstValue("sub")!);
            var wallet = await store.FindByUserId(userId, ct);
            return wallet is null ? Results.NotFound() : Results.Ok(new WalletDto(wallet.Id, wallet.UserId, wallet.Balance, wallet.Currency, wallet.UpdatedAt));
        }).WithName("GetMyWallet").Produces<WalletDto>().ProducesProblem(401).ProducesProblem(404);

        wallets.MapGet("/transactions", async (int? page, int? pageSize, ClaimsPrincipal principal,
            IWalletLedgerQueries queries, CancellationToken ct) =>
        {
            var userId = Guid.Parse(principal.FindFirstValue("sub")!);
            return Results.Ok(await queries.GetTransactions(userId, page ?? 1, pageSize ?? 20, ct));
        }).WithName("GetWalletTransactions").Produces<WalletPageDto>().ProducesProblem(401);

        wallets.MapPost("/withdraw", () => Results.Problem(statusCode: 410,
            title: "Withdrawals are no longer supported. Seller payouts use Pix after buyer-confirmed receipt."))
            .WithName("RequestWithdrawal").ProducesProblem(401).ProducesProblem(410);
    }
}
