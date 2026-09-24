using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Vaulta.Identity.Application;
using Vaulta.Identity.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Identity.Infrastructure;

public sealed class SystemClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UtcNow; }
public sealed class JwtOptions
{
    public string Secret { get; set; } = "";
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 30;
}
internal sealed class PasswordService : IPasswordService
{
    private readonly PasswordHasher<object> _hasher = new(Options.Create(new PasswordHasherOptions { IterationCount = 210_000 }));
    private readonly object _subject = new();
    private readonly string _dummy;
    public PasswordService() => _dummy = Hash(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    public string Hash(string password) => _hasher.HashPassword(_subject, password);
    public bool Verify(string hash, string password) => _hasher.VerifyHashedPassword(_subject, hash, password) != PasswordVerificationResult.Failed;
    public void VerifyDummy(string password) => Verify(_dummy, password);
}
internal sealed class TokenService(IOptions<JwtOptions> options, IClock clock) : ITokenService
{
    public (string Token, int ExpiresIn) AccessToken(User user)
    {
        var o = options.Value; var now = clock.UtcNow;
        var token = new JwtSecurityToken(o.Issuer, o.Audience,
            [new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()), new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
             new Claim("sst", user.SecurityStamp.ToString())], now.UtcDateTime, now.AddMinutes(o.AccessTokenMinutes).UtcDateTime,
            new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(o.Secret)), SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), o.AccessTokenMinutes * 60);
    }
    public (string Token, string Hash, DateTimeOffset ExpiresAt) RefreshToken()
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
        return (token, HashToken(token), clock.UtcNow.AddDays(options.Value.RefreshTokenDays));
    }
    public string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
