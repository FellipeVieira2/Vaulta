using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vaulta.Identity.Application;
using Vaulta.Identity.Contracts;
using Vaulta.Identity.Domain;
using Vaulta.Identity.Infrastructure;
using Vaulta.SharedKernel;
using Xunit;

namespace Vaulta.Identity.IntegrationTests;

[Collection("api")]
public sealed class IdentityApiTests(ApiFixture fixture)
{
    private const string Password = "Secure-Test-Password1!";
    private HttpClient Client() => fixture.Factory.CreateClient();
    private static RegisterRequest Registration() => new($"{Guid.NewGuid():N}@example.com", Password, "u" + Guid.NewGuid().ToString("N")[..20], "Collector");
    private async Task<(HttpClient Client, RegisterRequest Registration, AuthResponse Auth)> Account()
    {
        var client = Client(); var registration = Registration();
        Assert.Equal(HttpStatusCode.Created, (await client.PostAsJsonAsync("/api/v1/auth/register", registration)).StatusCode);
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(registration.Email, registration.Password));
        response.EnsureSuccessStatusCode(); var auth = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return (client, registration, auth);
    }
    [Fact] public async Task RegisterLoginMeAndOutboxArePersisted()
    {
        var (client, registration, auth) = await Account(); using var owned = client;
        Assert.NotEmpty(auth.AccessToken); Assert.Equal(900, auth.ExpiresIn);
        var response = await client.GetAsync("/api/v1/me"); response.EnsureSuccessStatusCode();
        var me = (await response.Content.ReadFromJsonAsync<MyProfileDto>())!;
        Assert.Equal(registration.Email, me.Email); Assert.Equal("BRL", me.Preferences.Currency); Assert.NotNull(response.Headers.ETag);
        await using var scope = fixture.Factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var user = await db.Users.SingleAsync(x => x.Id == me.Id);
        Assert.NotEqual(Password, user.PasswordHash); Assert.NotNull(user.LastLoginAt);
        var token = await db.RefreshTokens.SingleAsync(x => x.UserId == me.Id);
        Assert.NotEqual(auth.RefreshToken, token.TokenHash);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(auth.RefreshToken))), token.TokenHash);
        var registrationEvents = await db.OutboxMessages.Where(x => x.Type == "identity.user-registered.v1").ToListAsync();
        Assert.Contains(registrationEvents, x => x.Payload.Contains(me.Id.ToString(), StringComparison.Ordinal));
    }
    [Fact] public async Task DuplicateEmailAndUsernameAreCaseInsensitive()
    {
        var (client, registration, _) = await Account(); using var owned = client;
        var email = await client.PostAsJsonAsync("/api/v1/auth/register", Registration() with { Email = registration.Email.ToUpperInvariant() });
        var username = await client.PostAsJsonAsync("/api/v1/auth/register", Registration() with { Username = registration.Username.ToUpperInvariant() });
        Assert.Equal(HttpStatusCode.Conflict, email.StatusCode); Assert.Equal(HttpStatusCode.Conflict, username.StatusCode);
        Assert.Equal("application/problem+json", email.Content.Headers.ContentType?.MediaType);
    }
    [Fact] public async Task ProfilePatchPreservesOmittedFieldsClearsNullAndRejectsStaleVersion()
    {
        var (client, registration, _) = await Account(); using var owned = client;
        var me = await client.GetAsync("/api/v1/me"); var original = me.Headers.ETag!.ToString();
        var updated = await Patch(client, "/api/v1/me/profile", new { bio = "Collector bio", countryCode = "BR", city = "Araras" }, original);
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Patch(client, "/api/v1/me/profile", new { bio = "stale" }, original)).StatusCode);
        var publicResponse = await client.GetAsync($"/api/v1/users/{registration.Username.ToUpperInvariant()}");
        publicResponse.EnsureSuccessStatusCode(); var json = await publicResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal("Collector bio", doc.RootElement.GetProperty("bio").GetString());
        Assert.Equal(5, doc.RootElement.EnumerateObject().Count());
        Assert.False(doc.RootElement.TryGetProperty("email", out _)); Assert.False(doc.RootElement.TryGetProperty("id", out _));
        Assert.Equal(HttpStatusCode.NoContent, (await Patch(client, "/api/v1/me/profile", new { bio = (string?)null }, updated.Headers.ETag!.ToString())).StatusCode);
        var profile = await client.GetFromJsonAsync<MyProfileDto>("/api/v1/me");
        Assert.Null(profile!.Bio); Assert.Equal("Araras", profile.Location.City); Assert.Equal("Collector", profile.DisplayName);
        Assert.Equal(HttpStatusCode.BadRequest, (await Patch(client, "/api/v1/me/profile", new { email = "illegal@example.com" }, $"\"{profile.Version}\"")).StatusCode);
    }
    [Fact] public async Task PreferencesAreValidatedAndPersisted()
    {
        var (client, _, _) = await Account(); using var owned = client;
        var me = await client.GetAsync("/api/v1/me"); var version = me.Headers.ETag!.ToString();
        Assert.Equal(HttpStatusCode.BadRequest, (await Patch(client, "/api/v1/me/preferences", new { tcgInterests = new[] { "INVALID" } }, version)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Patch(client, "/api/v1/me/preferences", new { preferredCurrency = "USD", language = "en-US", timeZone = "America/New_York", tcgInterests = new[] { "POKEMON", "MAGIC" } }, version)).StatusCode);
        var profile = (await client.GetFromJsonAsync<MyProfileDto>("/api/v1/me"))!;
        Assert.Equal("USD", profile.Preferences.Currency); Assert.Equal(2, profile.TcgInterests.Length);
        Assert.Equal(HttpStatusCode.NoContent, (await Patch(client, "/api/v1/me/preferences", new { tcgInterests = Array.Empty<string>() }, $"\"{profile.Version}\"")).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<MyProfileDto>("/api/v1/me"))!.TcgInterests);
    }
    [Fact] public async Task RefreshRotatesAndReuseInvalidatesAllSessions()
    {
        var (client, _, auth) = await Account(); using var owned = client;
        var response = await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(auth.RefreshToken)); response.EnsureSuccessStatusCode();
        var rotated = (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
        Assert.NotEqual(auth.RefreshToken, rotated.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(auth.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(rotated.RefreshToken))).StatusCode);
    }
    [Fact] public async Task LogoutRevokesSession()
    {
        var (client, _, auth) = await Account(); using var owned = client;
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/auth/logout", new RefreshRequest(auth.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(auth.RefreshToken))).StatusCode);
    }
    [Fact] public async Task PasswordChangeRevokesAccessAndRefreshAndRequiresNewPassword()
    {
        var (client, registration, auth) = await Account(); using var owned = client;
        const string next = "Another-Secure-Password2!";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/me/change-password", new ChangePasswordRequest("wrong", next))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/v1/me/change-password", new ChangePasswordRequest(Password, next))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(registration.Email, Password))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new RefreshRequest(auth.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/v1/auth/login", new LoginRequest(registration.Email, next))).StatusCode);
    }
    [Fact] public async Task AnonymousAndInvalidInputReturnProblemDetails()
    {
        using var client = Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/me")).StatusCode);
        var response = await client.PostAsJsonAsync("/api/v1/auth/register", Registration() with { Password = "weak" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("correlationId", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/v1/users/missing_user")).StatusCode);
    }
    [Fact] public async Task DatabaseRejectsConcurrentAggregateWrites()
    {
        var (client, _, auth) = await Account(); using var owned = client;
        await using var first = fixture.Factory.Services.CreateAsyncScope(); await using var second = fixture.Factory.Services.CreateAsyncScope();
        var a = first.ServiceProvider.GetRequiredService<IdentityDbContext>(); var b = second.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var u1 = await a.Users.Include(x => x.Profile).SingleAsync(x => x.Id == auth.User.Id);
        var u2 = await b.Users.Include(x => x.Profile).SingleAsync(x => x.Id == auth.User.Id);
        u1.UpdateProfile("First", null, null, null, null, null, DateTimeOffset.UtcNow);
        u2.UpdateProfile("Second", null, null, null, null, null, DateTimeOffset.UtcNow);
        await a.SaveChangesAsync(); await Assert.ThrowsAsync<ConflictException>(() => b.SaveChangesAsync());
        await using var check = fixture.Factory.Services.CreateAsyncScope();
        var db = check.ServiceProvider.GetRequiredService<IdentityDbContext>();
        Assert.Equal("First", (await db.Profiles.SingleAsync(x => x.UserId == auth.User.Id)).DisplayName);
        var profileEvents = await db.OutboxMessages.Where(x => x.Type == "identity.user-profile-updated.v1").ToListAsync();
        Assert.Single(profileEvents, x => x.Payload.Contains(auth.User.Id.ToString(), StringComparison.Ordinal));
    }
    [Fact] public async Task OutboxRetriesFailureThenProcessesWithoutLosingMessage()
    {
        var (client, _, _) = await Account(); using var owned = client;
        await using var scope = fixture.Factory.Services.CreateAsyncScope(); var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        // Existing messages are drained first so the bounded batch definitely includes this isolated probe.
        var normal = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
        while (await normal.ProcessBatch(default) > 0) { }
        var message = new OutboxMessage { Id = Guid.NewGuid(), Type = "test.probe.v1", Payload = "{}", OccurredAt = DateTimeOffset.UtcNow.AddHours(-1) };
        db.OutboxMessages.Add(message); await db.SaveChangesAsync();
        var processor = new OutboxProcessor(db, new FailingBus(), new SystemClock(), NullLogger<OutboxProcessor>.Instance);
        await processor.ProcessBatch(default);
        Assert.Null(message.ProcessedAt); Assert.Equal(1, message.RetryCount); Assert.Equal("InvalidOperationException", message.Error);
        await normal.ProcessBatch(default); Assert.NotNull(message.ProcessedAt); Assert.Null(message.Error);
    }
    private sealed class FailingBus : IEventBus
    {
        public Task Publish(EventEnvelope message, CancellationToken ct) => throw new InvalidOperationException("Simulated failure");
    }
    private static Task<HttpResponseMessage> Patch(HttpClient client, string path, object payload, string version)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, path) { Content = JsonContent.Create(payload) };
        request.Headers.TryAddWithoutValidation("If-Match", version); return client.SendAsync(request);
    }
}
