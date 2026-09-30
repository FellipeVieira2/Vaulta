using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Vaulta.Identity.Application;
using Vaulta.Identity.Application.Commands;
using Vaulta.Identity.Application.Queries;
using Vaulta.Identity.Contracts;
using Vaulta.SharedKernel;

namespace Vaulta.Web.Api;

public static class IdentityEndpoints
{
    public static void MapIdentityEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/api/v1/auth").WithTags("Authentication").RequireRateLimiting("auth");
        auth.MapPost("/register", async (RegisterRequest request, CommandHandlers handler, CancellationToken ct) =>
        {
            var result = await handler.Handle(new RegisterUserCommand(request), ct);
            return Results.Created($"/api/v1/users/{result.Username}", result);
        }).WithName("Register").Produces<UserSummaryDto>(201).ProducesProblem(400).ProducesProblem(409).ProducesProblem(429);
        auth.MapPost("/login", async (LoginRequest request, CommandHandlers handler, CancellationToken ct) =>
            Results.Ok(await handler.Handle(new LoginCommand(request), ct)))
            .WithName("Login").Produces<AuthResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(403).ProducesProblem(429);
        auth.MapPost("/refresh", async (RefreshRequest request, CommandHandlers handler, CancellationToken ct) =>
            Results.Ok(await handler.Handle(new RefreshTokenCommand(request.RefreshToken), ct)))
            .WithName("Refresh").Produces<AuthResponse>().ProducesProblem(400).ProducesProblem(401).ProducesProblem(409).ProducesProblem(429);
        auth.MapPost("/logout", async (RefreshRequest request, ClaimsPrincipal principal, CommandHandlers handler, CancellationToken ct) =>
        {
            await handler.Handle(new LogoutCommand(UserId(principal), request.RefreshToken), ct); return Results.NoContent();
        }).RequireAuthorization().WithName("Logout").Produces(204).ProducesProblem(400).ProducesProblem(401);
        var me = app.MapGroup("/api/v1/me").WithTags("My profile").RequireAuthorization();
        me.MapGet("", async (HttpContext context, QueryHandlers handler, CancellationToken ct) =>
        {
            var result = await handler.Handle(new GetMyProfileQuery(UserId(context.User)), ct);
            context.Response.Headers.ETag = ETag(result.Version); return Results.Ok(result);
        }).WithName("GetMyProfile").Produces<MyProfileDto>().ProducesProblem(401).ProducesProblem(404);
        me.MapPatch("/profile", async (ProfilePatch request, [FromHeader(Name = "If-Match")] string? version, HttpContext context, CommandHandlers handler, CancellationToken ct) =>
        {
            var next = await handler.Handle(new UpdateProfileCommand(UserId(context.User), ParseVersion(version), request), ct);
            context.Response.Headers.ETag = ETag(next); return Results.NoContent();
        }).WithName("UpdateProfile").WithDescription("Send the ETag from GET /me in If-Match. Omitted properties are preserved; null clears optional fields.")
            .Produces(204).ProducesProblem(400).ProducesProblem(401).ProducesProblem(409);
        me.MapPatch("/preferences", async (PreferencesPatch request, [FromHeader(Name = "If-Match")] string? version, HttpContext context, CommandHandlers handler, CancellationToken ct) =>
        {
            var next = await handler.Handle(new UpdatePreferencesCommand(UserId(context.User), ParseVersion(version), request), ct);
            context.Response.Headers.ETag = ETag(next); return Results.NoContent();
        }).WithName("UpdatePreferences").WithDescription("Send current ETag in If-Match. Supported currencies: BRL/USD/EUR; languages: pt-BR/en-US/es-ES; IANA time zone; TCGs: POKEMON/MAGIC/YUGIOH/ONE_PIECE.")
            .Produces(204).ProducesProblem(400).ProducesProblem(401).ProducesProblem(409);
        me.MapPut("/shipping-address", async (UpdateShippingAddressRequest request, HttpContext context, CommandHandlers handler, CancellationToken ct) =>
        {
            await handler.Handle(new UpdateShippingAddressCommand(UserId(context.User), request.Street, request.City, request.State, request.ZipCode), ct);
            return Results.NoContent();
        }).WithName("UpdateShippingAddress").Produces(204).ProducesProblem(400).ProducesProblem(401);
        me.MapPost("/change-password", async (ChangePasswordRequest request, ClaimsPrincipal principal, CommandHandlers handler, CancellationToken ct) =>
        {
            await handler.Handle(new ChangePasswordCommand(UserId(principal), request), ct); return Results.NoContent();
        }).RequireRateLimiting("auth").WithName("ChangePassword").Produces(204).ProducesProblem(400).ProducesProblem(401).ProducesProblem(409);
        app.MapGet("/api/v1/users/{username}", async (string username, QueryHandlers handler, CancellationToken ct) =>
            Results.Ok(await handler.Handle(new GetPublicProfileQuery(username), ct)))
            .WithTags("Public profiles").WithName("GetPublicProfile").Produces<PublicProfileDto>().ProducesProblem(400).ProducesProblem(404);
    }
    private static Guid UserId(ClaimsPrincipal principal) => Guid.Parse(principal.FindFirstValue("sub")!);
    private static string ETag(Guid version) => $"\"{version}\"";
    private static Guid ParseVersion(string? version) => Guid.TryParse(version?.Trim('"'), out var id)
        ? id : throw new DomainException("Send the current ETag from GET /me in the If-Match header.");
}

public sealed record UpdateShippingAddressRequest(string? Street, string? City, string? State, string? ZipCode);
