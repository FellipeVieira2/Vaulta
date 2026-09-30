using System.Text.Json;
using Vaulta.Identity.Application;
using Vaulta.SharedKernel;
using Vaulta.Wallets.Application;
using Vaulta.Wallets.Domain;

namespace Vaulta.Wallets.Infrastructure;

public sealed class UserRegisteredWalletConsumer(IWalletStore store, IClock clock) : IEventConsumer
{
    public async Task Handle(EventEnvelope message, CancellationToken ct)
    {
        if (message.Type != "identity.user-registered.v1") return;

        using var doc = JsonDocument.Parse(message.Payload);
        if (!doc.RootElement.TryGetProperty("UserId", out var userIdProp) ||
            !Guid.TryParse(userIdProp.GetString(), out var userId))
            return;

        var existing = await store.FindByUserId(userId, ct);
        if (existing is not null) return;

        var wallet = Wallet.Create(userId, clock.UtcNow);
        store.Add(wallet);
        await store.Save(ct);
    }
}