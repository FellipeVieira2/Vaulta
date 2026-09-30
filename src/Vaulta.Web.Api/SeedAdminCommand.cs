using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Vaulta.Identity.Application;
using Vaulta.Identity.Domain;
using Vaulta.Identity.Infrastructure;
using Vaulta.SharedKernel;

namespace Vaulta.Web.Api;

internal static class SeedAdminCommand
{
    private const string AdminEmail = "admin@vaultatcg.com.br";
    private const string AdminUsername = "admin";
    private const string AdminDisplayName = "Vaulta Admin";
    private const string DefaultPassword = "Vaulta@2026!Admin";

    public static async Task<bool> TryExecute(WebApplication app, string[] args)
    {
        if (!args.Contains("--seed-admin")) return false;

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var passwords = scope.ServiceProvider.GetRequiredService<IPasswordService>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var logger = app.Logger;

        try
        {
            var normalizedEmail = IdentityRules.NormalizeEmail(AdminEmail);
            var normalizedUsername = AdminUsername.ToUpperInvariant();

            var exists = await db.Users.AsNoTracking()
                .AnyAsync(x => x.NormalizedEmail == normalizedEmail || x.Profile.NormalizedUsername == normalizedUsername);

            if (exists)
            {
                logger.LogInformation("Admin user already exists ({Email} / {Username}). Skipping seed.", AdminEmail, AdminUsername);
                Console.WriteLine($"Admin already exists: {AdminEmail}");
                return true;
            }

            var hash = passwords.Hash(DefaultPassword);
            var now = clock.UtcNow;
            var user = User.Register(AdminEmail, hash, AdminUsername, AdminDisplayName, now);

            // Mark as active and email verified so the admin can log in immediately without verification flow.
            typeof(User).GetProperty(nameof(User.Status))!.SetValue(user, UserStatus.Active);
            typeof(User).GetProperty(nameof(User.EmailVerifiedAt))!.SetValue(user, now);

            db.Users.Add(user);
            await db.SaveChangesAsync();

            logger.LogInformation("Admin user seeded successfully ({Email}, UserId={UserId})", AdminEmail, user.Id);
            Console.WriteLine($"Admin seeded successfully.");
            Console.WriteLine($"  Email:    {AdminEmail}");
            Console.WriteLine($"  Username: {AdminUsername}");
            Console.WriteLine($"  Password: {DefaultPassword}");
            Console.WriteLine($"  UserId:   {user.Id}");
            Console.WriteLine();
            Console.WriteLine("⚠️  Change this password after first login.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to seed admin user");
            Console.Error.WriteLine($"Seed failed: {ex.Message}");
            Environment.ExitCode = 1;
        }

        return true;
    }
}