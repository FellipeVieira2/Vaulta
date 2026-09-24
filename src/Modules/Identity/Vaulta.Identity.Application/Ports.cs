using Vaulta.Identity.Contracts;
using Vaulta.Identity.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Identity.Application;

// Specific aggregate persistence port; IQueryable never crosses this boundary.
public interface IIdentityStore
{
    Task<User?> FindUser(Guid id, CancellationToken ct);
    Task<User?> FindByEmail(string normalizedEmail, CancellationToken ct);
    Task<bool> EmailExists(string normalizedEmail, CancellationToken ct);
    Task<bool> UsernameExists(string normalizedUsername, CancellationToken ct);
    void Add(User user);
    void Add(RefreshToken token);
    Task<RefreshToken?> FindToken(string hash, CancellationToken ct);
    Task RevokeSessions(Guid userId, DateTimeOffset now, CancellationToken ct);
    Task Save(CancellationToken ct);
}
public interface IProfileReader
{
    Task<MyProfileDto?> GetMine(Guid id, CancellationToken ct);
    Task<PublicProfileDto?> GetPublic(string normalizedUsername, CancellationToken ct);
}
public interface IPasswordService
{
    string Hash(string password);
    bool Verify(string hash, string password);
    void VerifyDummy(string password);
}
public interface ITokenService
{
    (string Token, int ExpiresIn) AccessToken(User user);
    (string Token, string Hash, DateTimeOffset ExpiresAt) RefreshToken();
    string HashToken(string token);
}
public sealed record EventEnvelope(Guid Id, string Type, string Payload, DateTimeOffset OccurredAt);
public interface IEventBus { Task Publish(EventEnvelope message, CancellationToken ct); }
public interface IEventConsumer { Task Handle(EventEnvelope message, CancellationToken ct); }
// Future durable consumers use (message.Id, consumer name) as their inbox unique key.
public sealed class NotFoundException(string message) : Exception(message);
public sealed class ConflictException(string message) : Exception(message);
public sealed class UnauthorizedException(string message = "Invalid credentials or session.") : Exception(message);
public sealed class ForbiddenException(string message) : Exception(message);
