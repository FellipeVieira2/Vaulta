using Vaulta.Identity.Contracts;
using Vaulta.Identity.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Identity.Application.Queries;

public sealed record GetMyProfileQuery(Guid UserId);
public sealed record GetPublicProfileQuery(string Username);
public sealed class QueryHandlers(IProfileReader reader)
{
    public async Task<MyProfileDto> Handle(GetMyProfileQuery query, CancellationToken ct) =>
        await reader.GetMine(query.UserId, ct) ?? throw new NotFoundException("User not found.");
    public async Task<PublicProfileDto> Handle(GetPublicProfileQuery query, CancellationToken ct) =>
        await reader.GetPublic(IdentityRules.Username(query.Username).ToUpperInvariant(), ct) ?? throw new NotFoundException("Profile not found.");
}
