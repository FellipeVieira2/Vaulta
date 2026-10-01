using Microsoft.Extensions.Configuration;
using Vaulta.Identity.Application;
using Vaulta.Payments.Application;

namespace Vaulta.Payments.Infrastructure;

internal sealed class PayoutIdentity(IIdentityStore users, IConfiguration configuration) : IPayoutIdentity
{
    public async Task<bool> IsAvailable(Guid sellerId, CancellationToken ct) =>
        (await users.FindUser(sellerId, ct))?.CanLogin == true;
    public bool CanReview(Guid userId) => (configuration.GetSection("Payouts:IdentityReviewerUserIds").Get<string[]>() ?? [])
        .Any(value => Guid.TryParse(value, out var configuredId) && configuredId != Guid.Empty && configuredId == userId);
}
