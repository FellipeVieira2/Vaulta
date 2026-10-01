using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Vaulta.SharedKernel;

namespace Vaulta.Identity.Domain;

public static partial class IdentityRules
{
    public const string DefaultCurrency = "BRL";
    public const string DefaultLanguage = "pt-BR";
    public const string DefaultTimeZone = "America/Sao_Paulo";
    public static readonly IReadOnlySet<string> Currencies = new HashSet<string> { "BRL", "USD", "EUR" };
    public static readonly IReadOnlySet<string> Languages = new HashSet<string> { "pt-BR", "en-US", "es-ES" };
    public static readonly IReadOnlySet<string> TcgCodes = new HashSet<string> { "POKEMON", "MAGIC", "YUGIOH", "ONE_PIECE" };
    private static readonly HashSet<string> Countries = CultureInfo.GetCultures(CultureTypes.SpecificCultures)
        .Select(x => new RegionInfo(x.Name).TwoLetterISORegionName).ToHashSet(StringComparer.Ordinal);
    [GeneratedRegex("^[a-zA-Z0-9_]{3,30}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernamePattern();
    public static string Email(string value)
    {
        var email = value.Trim();
        if (email.Length > 254 || !MailAddress.TryCreate(email, out var parsed) || parsed.Address != email)
            throw new DomainException("Invalid email.");
        return email;
    }
    public static string NormalizeEmail(string value) => Email(value).ToUpperInvariant();
    public static string Username(string value)
    {
        var username = value.Trim();
        if (!UsernamePattern().IsMatch(username)) throw new DomainException("Username must contain 3–30 ASCII letters, digits or underscores.");
        return username;
    }
    public static string Required(string value, int max, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > max) throw new DomainException($"Invalid {field}.");
        return value.Trim();
    }
    public static string? Optional(string? value, int max, string field)
    {
        if (value?.Length > max) throw new DomainException($"{field} is too long.");
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
    public static string? Country(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var code = value.Trim().ToUpperInvariant();
        if (!Countries.Contains(code)) throw new DomainException("Invalid ISO country code.");
        return code;
    }
    public static string? Avatar(string? value)
    {
        value = Optional(value, 2048, "avatarUrl");
        if (value is not null && (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https"))
            throw new DomainException("Avatar URL must use HTTPS.");
        return value;
    }

    [GeneratedRegex(@"^\d{5}-?\d{3}$", RegexOptions.CultureInvariant)]
    private static partial Regex ZipCodePattern();

    public static string? ZipCode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        if (trimmed.Length > 10) throw new DomainException("shippingZipCode is too long.");
        if (!ZipCodePattern().IsMatch(trimmed))
            throw new DomainException("Invalid zip code format. Expected 00000-000 or 00000000.");
        return trimmed;
    }
}
