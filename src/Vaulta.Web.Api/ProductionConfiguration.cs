using System.Text;
using Npgsql;

namespace Vaulta.Web.Api;

public static class ProductionConfiguration
{
    public static void Validate(IConfiguration configuration)
    {
        string[] required = ["ConnectionStrings:Vaulta", "Jwt:Secret", "Jwt:Issuer", "Jwt:Audience",
            "Assets:S3:Bucket", "Assets:S3:Region"];
        foreach (var key in required)
            if (string.IsNullOrWhiteSpace(configuration[key]))
                throw new InvalidOperationException($"Production requires {key}.");

        var secret = configuration["Jwt:Secret"]!;
        if (Encoding.UTF8.GetByteCount(secret) < 32 ||
            secret.StartsWith("change-me", StringComparison.OrdinalIgnoreCase) ||
            secret.StartsWith("development-secret", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Jwt:Secret must be random and at least 32 bytes in Production.");

        // Do not include parser exceptions: connection strings can contain secrets.
        try
        {
            var connection = new NpgsqlConnectionStringBuilder(configuration.GetConnectionString("Vaulta"));
            if (string.IsNullOrWhiteSpace(connection.Host) || string.IsNullOrWhiteSpace(connection.Database) ||
                string.IsNullOrWhiteSpace(connection.Username) || string.IsNullOrWhiteSpace(connection.Password))
                throw new ArgumentException();
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException("ConnectionStrings:Vaulta must specify Host, Database, Username and Password.");
        }

        string[] forbidden = ["Assets:S3:ServiceUrl", "Assets:S3:AccessKey", "Assets:S3:SecretKey",
            "AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY", "AWS_SESSION_TOKEN"];
        foreach (var key in forbidden)
            if (!string.IsNullOrWhiteSpace(configuration[key]))
                throw new InvalidOperationException($"Production AWS must not configure {key}; use the instance IAM role.");

        if (configuration.GetValue("Assets:S3:ForcePathStyle", true))
            throw new InvalidOperationException("Assets:S3:ForcePathStyle must be false in Production AWS.");
        if (configuration.GetValue<bool>("Database:ApplyMigrations"))
            throw new InvalidOperationException("Database:ApplyMigrations must be false in Production; run --migrate separately.");
        if (!configuration.GetValue("Outbox:Enabled", true))
            throw new InvalidOperationException("Outbox:Enabled must remain true in Production.");
        if (configuration.GetValue("RateLimit:AuthPermitLimit", 20) < 1)
            throw new InvalidOperationException("RateLimit:AuthPermitLimit must be positive.");

        foreach (var origin in configuration.GetSection("Cors:Origins").Get<string[]>() ?? [])
        {
            if (string.IsNullOrWhiteSpace(origin)) continue;
            if (origin.Contains('*') || !Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
                uri.Scheme != Uri.UriSchemeHttps || uri.AbsolutePath != "/" ||
                uri.UserInfo.Length != 0 || uri.Query.Length != 0 || uri.Fragment.Length != 0)
                throw new InvalidOperationException("Cors:Origins must contain explicit HTTPS origins in Production.");
        }
    }
}
