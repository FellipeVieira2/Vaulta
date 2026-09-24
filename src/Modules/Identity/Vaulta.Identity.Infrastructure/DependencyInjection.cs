using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Vaulta.Identity.Application;
using Vaulta.Identity.Application.Queries;
using Vaulta.Identity.Domain;
using Vaulta.SharedKernel;

namespace Vaulta.Identity.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddDbContext<IdentityDbContext>(o => o.UseNpgsql(configuration.GetConnectionString("Vaulta")));
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPasswordService, PasswordService>();
        services.AddScoped<ITokenService, TokenService>();
        services.AddScoped<IdentityStore>();
        services.AddScoped<IIdentityStore>(s => s.GetRequiredService<IdentityStore>());
        services.AddScoped<IProfileReader>(s => s.GetRequiredService<IdentityStore>());
        services.AddScoped<CommandHandlers>(); services.AddScoped<QueryHandlers>();
        services.AddScoped<IEventBus, InMemoryEventBus>(); services.AddScoped<OutboxProcessor>();
        if (configuration.GetValue("Outbox:Enabled", true)) services.AddHostedService<OutboxDispatcher>();
        services.AddOptions<JwtOptions>().Bind(configuration.GetSection("Jwt"))
            .Validate(o => Encoding.UTF8.GetByteCount(o.Secret) >= 32 && !o.Secret.StartsWith("change-me", StringComparison.OrdinalIgnoreCase), "Configure a random JWT signing secret with at least 32 bytes.")
            .Validate(o => !string.IsNullOrWhiteSpace(o.Issuer) && !string.IsNullOrWhiteSpace(o.Audience), "JWT issuer and audience are required.")
            .Validate(o => o.AccessTokenMinutes is >= 1 and <= 60 && o.RefreshTokenDays is >= 1 and <= 90, "Invalid token lifetime.")
            .ValidateOnStart();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<IOptions<JwtOptions>>((o, configuredJwt) =>
        {
            var jwt = configuredJwt.Value;
            o.MapInboundClaims = false;
            o.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidateAudience = true, ValidateLifetime = true, ValidateIssuerSigningKey = true,
                ValidIssuer = jwt.Issuer, ValidAudience = jwt.Audience, ClockSkew = TimeSpan.FromSeconds(15),
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
                ValidAlgorithms = [SecurityAlgorithms.HmacSha256], NameClaimType = "sub"
            };
            o.Events = new JwtBearerEvents
            {
                OnTokenValidated = async context =>
                {
                    if (!Guid.TryParse(context.Principal?.FindFirst("sub")?.Value, out var id) ||
                        !Guid.TryParse(context.Principal?.FindFirst("sst")?.Value, out var stamp)) { context.Fail("Invalid session."); return; }
                    var db = context.HttpContext.RequestServices.GetRequiredService<IdentityDbContext>();
                    if (!await db.Users.AsNoTracking().AnyAsync(x => x.Id == id && x.SecurityStamp == stamp &&
                        (x.Status == UserStatus.Active || x.Status == UserStatus.PendingVerification), context.HttpContext.RequestAborted))
                        context.Fail("Session is unavailable.");
                }
            };
        });
        services.AddAuthorization(); return services;
    }
}
