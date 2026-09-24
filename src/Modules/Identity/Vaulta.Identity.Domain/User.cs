using Vaulta.SharedKernel;

namespace Vaulta.Identity.Domain;

public enum UserStatus { PendingVerification, Active, Suspended, Deleted }
public sealed record UserRegisteredDomainEvent(Guid Id, Guid UserId, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record UserProfileUpdatedDomainEvent(Guid Id, Guid UserId, DateTimeOffset OccurredAt) : IDomainEvent;
public sealed record UserPasswordChangedDomainEvent(Guid Id, Guid UserId, DateTimeOffset OccurredAt) : IDomainEvent;

public sealed class User : AggregateRoot
{
    private User() { }
    public Guid Id { get; private set; }
    public string Email { get; private set; } = null!;
    public string NormalizedEmail { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public UserStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? LastLoginAt { get; private set; }
    public DateTimeOffset? EmailVerifiedAt { get; private set; }
    public Guid Version { get; private set; }
    public Guid SecurityStamp { get; private set; }
    public UserProfile Profile { get; private set; } = null!;
    public UserPreferences Preferences { get; private set; } = null!;
    private readonly List<UserTcgInterest> _tcgInterests = [];
    public IReadOnlyCollection<UserTcgInterest> TcgInterests => _tcgInterests.AsReadOnly();
    public bool CanLogin => Status is UserStatus.Active or UserStatus.PendingVerification;

    public static User Register(string email, string hash, string username, string displayName, DateTimeOffset now)
    {
        var user = new User
        {
            Id = Guid.NewGuid(), Email = IdentityRules.Email(email), NormalizedEmail = IdentityRules.NormalizeEmail(email),
            PasswordHash = IdentityRules.Required(hash, 512, "passwordHash"), Status = UserStatus.PendingVerification,
            CreatedAt = now, UpdatedAt = now, Version = Guid.NewGuid(), SecurityStamp = Guid.NewGuid()
        };
        user.Profile = new UserProfile(user.Id, username, displayName, now);
        user.Preferences = new UserPreferences(user.Id);
        user.Raise(new UserRegisteredDomainEvent(Guid.NewGuid(), user.Id, now));
        return user;
    }
    private void Touch(DateTimeOffset now) { UpdatedAt = now; Version = Guid.NewGuid(); }
    public void Login(DateTimeOffset now)
    {
        if (!CanLogin) throw new DomainException("User cannot log in.");
        LastLoginAt = now; Touch(now);
    }
    public void UpdateProfile(string name, string? bio, string? avatar, string? country, string? state, string? city, DateTimeOffset now)
    {
        Profile.Update(name, bio, avatar, country, state, city, now); Touch(now);
        Raise(new UserProfileUpdatedDomainEvent(Guid.NewGuid(), Id, now));
    }
    public void UpdatePreferences(string currency, string language, string zone, IEnumerable<string> interests, DateTimeOffset now)
    {
        var codes = interests.Select(x => x.ToUpperInvariant()).Distinct().ToArray();
        if (codes.Any(x => !IdentityRules.TcgCodes.Contains(x))) throw new DomainException("Unsupported TCG code.");
        Preferences.Update(currency, language, zone);
        _tcgInterests.RemoveAll(x => !codes.Contains(x.TcgCode));
        foreach (var code in codes.Where(x => !_tcgInterests.Any(t => t.TcgCode == x)))
            _tcgInterests.Add(new UserTcgInterest(Id, code, now));
        Touch(now);
    }
    public void ChangePassword(string hash, DateTimeOffset now)
    {
        PasswordHash = IdentityRules.Required(hash, 512, "passwordHash");
        SecurityStamp = Guid.NewGuid(); Touch(now);
        Raise(new UserPasswordChangedDomainEvent(Guid.NewGuid(), Id, now));
    }
    public void InvalidateSessions(DateTimeOffset now) { SecurityStamp = Guid.NewGuid(); Touch(now); }
}

public sealed class UserProfile
{
    private UserProfile() { }
    internal UserProfile(Guid id, string username, string displayName, DateTimeOffset now)
    {
        UserId = id; Username = IdentityRules.Username(username); NormalizedUsername = Username.ToUpperInvariant();
        DisplayName = IdentityRules.Required(displayName, 100, "displayName"); CreatedAt = UpdatedAt = now;
    }
    public Guid UserId { get; private set; }
    public string Username { get; private set; } = null!;
    public string NormalizedUsername { get; private set; } = null!;
    public string DisplayName { get; private set; } = null!;
    public string? Bio { get; private set; }
    public string? AvatarUrl { get; private set; }
    public string? CountryCode { get; private set; }
    public string? State { get; private set; }
    public string? City { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    internal void Update(string name, string? bio, string? avatar, string? country, string? state, string? city, DateTimeOffset now)
    {
        DisplayName = IdentityRules.Required(name, 100, "displayName"); Bio = IdentityRules.Optional(bio, 500, "bio");
        AvatarUrl = IdentityRules.Avatar(avatar); CountryCode = IdentityRules.Country(country);
        State = IdentityRules.Optional(state, 100, "state"); City = IdentityRules.Optional(city, 100, "city"); UpdatedAt = now;
    }
}

public sealed class UserPreferences
{
    private UserPreferences() { }
    internal UserPreferences(Guid id) => UserId = id;
    public Guid UserId { get; private set; }
    public string PreferredCurrency { get; private set; } = IdentityRules.DefaultCurrency;
    public string Language { get; private set; } = IdentityRules.DefaultLanguage;
    public string TimeZone { get; private set; } = IdentityRules.DefaultTimeZone;
    internal void Update(string currency, string language, string zone)
    {
        currency = currency.ToUpperInvariant();
        if (!IdentityRules.Currencies.Contains(currency) || !IdentityRules.Languages.Contains(language))
            throw new DomainException("Unsupported currency or language.");
        if (zone.Length > 100 || !TimeZoneInfo.TryFindSystemTimeZoneById(zone, out _) || !zone.Contains('/'))
            throw new DomainException("Use a valid IANA time zone.");
        PreferredCurrency = currency; Language = language; TimeZone = zone;
    }
}
public sealed class UserTcgInterest
{
    private UserTcgInterest() { }
    internal UserTcgInterest(Guid userId, string code, DateTimeOffset now) { UserId = userId; TcgCode = code; CreatedAt = now; }
    public Guid UserId { get; private set; }
    public string TcgCode { get; private set; } = null!;
    public DateTimeOffset CreatedAt { get; private set; }
}
