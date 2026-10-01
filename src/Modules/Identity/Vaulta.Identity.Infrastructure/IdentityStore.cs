using Microsoft.EntityFrameworkCore;
using Vaulta.Identity.Application;
using Vaulta.Identity.Contracts;
using Vaulta.Identity.Domain;

namespace Vaulta.Identity.Infrastructure;

internal sealed class IdentityStore(IdentityDbContext db) : IIdentityStore, IProfileReader
{
    private IQueryable<User> Aggregate => db.Users.Include(x => x.Profile).Include(x => x.Preferences).Include(x => x.TcgInterests);
    public Task<User?> FindUser(Guid id, CancellationToken ct) => Aggregate.SingleOrDefaultAsync(x => x.Id == id, ct);
    public Task<User?> FindByEmail(string email, CancellationToken ct) => Aggregate.SingleOrDefaultAsync(x => x.NormalizedEmail == email, ct);
    public Task<bool> EmailExists(string email, CancellationToken ct) => db.Users.AnyAsync(x => x.NormalizedEmail == email, ct);
    public Task<bool> UsernameExists(string username, CancellationToken ct) => db.Profiles.AnyAsync(x => x.NormalizedUsername == username, ct);
    public void Add(User user) => db.Users.Add(user);
    public void Add(RefreshToken token) => db.RefreshTokens.Add(token);
    public Task<RefreshToken?> FindToken(string hash, CancellationToken ct) => db.RefreshTokens.SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
    public async Task RevokeSessions(Guid id, DateTimeOffset now, CancellationToken ct)
    {
        foreach (var token in await db.RefreshTokens.Where(x => x.UserId == id && x.RevokedAt == null).ToListAsync(ct)) token.Revoke(now);
    }
    public Task Save(CancellationToken ct) => db.SaveChangesAsync(ct);
    public Task<MyProfileDto?> GetMine(Guid id, CancellationToken ct) => db.Users.AsNoTracking().Where(x => x.Id == id)
        .Select(x => new MyProfileDto(x.Id, x.Email, x.Profile.Username, x.Profile.DisplayName, x.Profile.Bio, x.Profile.AvatarUrl,
            new LocationDto(x.Profile.CountryCode, x.Profile.State, x.Profile.City),
            new PreferencesDto(x.Preferences.PreferredCurrency, x.Preferences.Language, x.Preferences.TimeZone),
            x.TcgInterests.Select(t => t.TcgCode).ToArray(),
            x.Profile.ShippingStreet != null || x.Profile.ShippingCity != null || x.Profile.ShippingState != null || x.Profile.ShippingZipCode != null
                ? new ShippingAddressDto(x.Profile.ShippingStreet, x.Profile.ShippingNumber, x.Profile.ShippingComplement, x.Profile.ShippingNeighborhood, x.Profile.ShippingCity, x.Profile.ShippingState, x.Profile.ShippingZipCode, x.Profile.ShippingRecipient)
                : null,
            x.Version)).SingleOrDefaultAsync(ct);
    public Task<PublicProfileDto?> GetPublic(string username, CancellationToken ct) => db.Users.AsNoTracking()
        .Where(x => x.Profile.NormalizedUsername == username && (x.Status == UserStatus.Active || x.Status == UserStatus.PendingVerification))
        .Select(x => new PublicProfileDto(x.Profile.Username, x.Profile.DisplayName, x.Profile.Bio, x.Profile.AvatarUrl,
            new LocationDto(x.Profile.CountryCode, x.Profile.State, x.Profile.City))).SingleOrDefaultAsync(ct);
}
