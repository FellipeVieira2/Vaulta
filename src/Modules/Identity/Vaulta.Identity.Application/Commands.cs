using Vaulta.Identity.Contracts;

namespace Vaulta.Identity.Application.Commands;

public sealed record RegisterUserCommand(RegisterRequest Request);
public sealed record LoginCommand(LoginRequest Request);
public sealed record RefreshTokenCommand(string Token);
public sealed record LogoutCommand(Guid UserId, string Token);
public sealed record UpdateProfileCommand(Guid UserId, Guid ExpectedVersion, ProfilePatch Patch);
public sealed record UpdatePreferencesCommand(Guid UserId, Guid ExpectedVersion, PreferencesPatch Patch);
public sealed record ChangePasswordCommand(Guid UserId, ChangePasswordRequest Request);
