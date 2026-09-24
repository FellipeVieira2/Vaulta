using FluentValidation;
using Vaulta.Identity.Application.Commands;
using Vaulta.Identity.Contracts;
using Vaulta.Identity.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Identity.Application;

public sealed class CommandHandlers(IIdentityStore store, IPasswordService passwords, ITokenService tokens, IClock clock)
{
    public async Task<UserSummaryDto> Handle(RegisterUserCommand command, CancellationToken ct)
    {
        await new RegisterValidator().ValidateAndThrowAsync(command, ct);
        var r = command.Request;
        if (await store.EmailExists(IdentityRules.NormalizeEmail(r.Email), ct)) throw new ConflictException("Email already registered.");
        if (await store.UsernameExists(IdentityRules.Username(r.Username).ToUpperInvariant(), ct)) throw new ConflictException("Username already registered.");
        var user = User.Register(r.Email, passwords.Hash(r.Password), r.Username, r.DisplayName, clock.UtcNow);
        store.Add(user); await store.Save(ct);
        return Summary(user);
    }
    public async Task<AuthResponse> Handle(LoginCommand command, CancellationToken ct)
    {
        await new LoginValidator().ValidateAndThrowAsync(command, ct);
        var user = await store.FindByEmail(IdentityRules.NormalizeEmail(command.Request.Email), ct);
        if (user is null) { passwords.VerifyDummy(command.Request.Password); throw new UnauthorizedException(); }
        if (!passwords.Verify(user.PasswordHash, command.Request.Password)) throw new UnauthorizedException();
        if (!user.CanLogin) throw new ForbiddenException("Account is unavailable.");
        user.Login(clock.UtcNow);
        var response = Issue(user, out _);
        await store.Save(ct); return response;
    }
    public async Task<AuthResponse> Handle(RefreshTokenCommand command, CancellationToken ct)
    {
        await new TokenValidator().ValidateAndThrowAsync(command.Token, ct);
        var current = await store.FindToken(tokens.HashToken(command.Token), ct) ?? throw new UnauthorizedException();
        var user = await RequireUser(current.UserId, ct);
        if (current.RevokedAt is not null)
        {
            user.InvalidateSessions(clock.UtcNow);
            await store.RevokeSessions(user.Id, clock.UtcNow, ct);
            await store.Save(ct);
            throw new UnauthorizedException("Refresh token reuse detected. Sign in again.");
        }
        if (current.ExpiresAt <= clock.UtcNow || !user.CanLogin) throw new UnauthorizedException();
        // Updating the aggregate also serializes refresh against password changes/session invalidation.
        user.Login(clock.UtcNow);
        var response = Issue(user, out var replacement);
        current.Revoke(clock.UtcNow, replacement.Id);
        await store.Save(ct); return response;
    }
    public async Task Handle(LogoutCommand command, CancellationToken ct)
    {
        await new TokenValidator().ValidateAndThrowAsync(command.Token, ct);
        var token = await store.FindToken(tokens.HashToken(command.Token), ct);
        if (token is not null && token.UserId == command.UserId)
        {
            token.Revoke(clock.UtcNow); await store.Save(ct);
        }
    }
    public async Task<Guid> Handle(UpdateProfileCommand command, CancellationToken ct)
    {
        var user = await RequireUser(command.UserId, ct); CheckVersion(user, command.ExpectedVersion);
        var p = command.Patch; var old = user.Profile;
        if (p.HasDisplayName && string.IsNullOrWhiteSpace(p.DisplayName)) throw new DomainException("Display name is required.");
        user.UpdateProfile(p.HasDisplayName ? p.DisplayName! : old.DisplayName, p.HasBio ? p.Bio : old.Bio,
            p.HasAvatarUrl ? p.AvatarUrl : old.AvatarUrl, p.HasCountryCode ? p.CountryCode : old.CountryCode,
            p.HasState ? p.State : old.State, p.HasCity ? p.City : old.City, clock.UtcNow);
        await store.Save(ct); return user.Version;
    }
    public async Task<Guid> Handle(UpdatePreferencesCommand command, CancellationToken ct)
    {
        var user = await RequireUser(command.UserId, ct); CheckVersion(user, command.ExpectedVersion);
        var p = command.Patch;
        if ((p.HasCurrency && string.IsNullOrWhiteSpace(p.PreferredCurrency)) || (p.HasLanguage && string.IsNullOrWhiteSpace(p.Language)) ||
            (p.HasTimeZone && string.IsNullOrWhiteSpace(p.TimeZone)) || (p.HasInterests && (p.TcgInterests is null || p.TcgInterests.Length > 32 || p.TcgInterests.Any(string.IsNullOrWhiteSpace))))
            throw new DomainException("Preferences cannot be null or empty; use [] to clear interests.");
        user.UpdatePreferences(p.PreferredCurrency ?? user.Preferences.PreferredCurrency, p.Language ?? user.Preferences.Language,
            p.TimeZone ?? user.Preferences.TimeZone, p.TcgInterests ?? user.TcgInterests.Select(x => x.TcgCode).ToArray(), clock.UtcNow);
        await store.Save(ct); return user.Version;
    }
    public async Task Handle(ChangePasswordCommand command, CancellationToken ct)
    {
        var r = command.Request;
        if (string.IsNullOrEmpty(r.CurrentPassword) || r.CurrentPassword.Length > 128) throw new UnauthorizedException();
        if (string.IsNullOrEmpty(r.NewPassword)) throw new DomainException("New password is required.");
        await new PasswordValidator().ValidateAndThrowAsync(r.NewPassword, ct);
        var user = await RequireUser(command.UserId, ct);
        if (!passwords.Verify(user.PasswordHash, r.CurrentPassword)) throw new UnauthorizedException();
        user.ChangePassword(passwords.Hash(r.NewPassword), clock.UtcNow);
        await store.RevokeSessions(user.Id, clock.UtcNow, ct); await store.Save(ct);
    }
    private async Task<User> RequireUser(Guid id, CancellationToken ct) => await store.FindUser(id, ct) ?? throw new NotFoundException("User not found.");
    private static void CheckVersion(User user, Guid version)
    {
        if (user.Version != version) throw new ConflictException("Profile changed. Fetch /me and retry with its current ETag.");
    }
    private AuthResponse Issue(User user, out RefreshToken entity)
    {
        var refresh = tokens.RefreshToken();
        entity = new RefreshToken(user.Id, refresh.Hash, clock.UtcNow, refresh.ExpiresAt); store.Add(entity);
        var access = tokens.AccessToken(user);
        return new AuthResponse(access.Token, access.ExpiresIn, refresh.Token, Summary(user));
    }
    private static UserSummaryDto Summary(User user) => new(user.Id, user.Profile.Username, user.Profile.DisplayName, user.Profile.AvatarUrl);
}
