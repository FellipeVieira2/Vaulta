using System.Text.Json.Serialization;

namespace Vaulta.Identity.Contracts;

public sealed record RegisterRequest(string Email, string Password, string Username, string DisplayName);
public sealed record LoginRequest(string Email, string Password);
public sealed record RefreshRequest(string RefreshToken);
public sealed record ChangePasswordRequest(string CurrentPassword, string NewPassword);
public sealed record LocationDto(string? CountryCode, string? State, string? City);
public sealed record PreferencesDto(string Currency, string Language, string TimeZone);
public sealed record UserSummaryDto(Guid Id, string Username, string DisplayName, string? AvatarUrl);
public sealed record AuthResponse(string AccessToken, int ExpiresIn, string RefreshToken, UserSummaryDto User);
public sealed record MyProfileDto(Guid Id, string Email, string Username, string DisplayName, string? Bio,
    string? AvatarUrl, LocationDto Location, PreferencesDto Preferences, string[] TcgInterests, Guid Version);
public sealed record PublicProfileDto(string Username, string DisplayName, string? Bio, string? AvatarUrl, LocationDto Location);

// Setters distinguish omitted properties (keep) from explicit null (clear).
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ProfilePatch
{
    private string? _displayName, _bio, _avatarUrl, _countryCode, _state, _city;
    public string? DisplayName { get => _displayName; set { _displayName = value; HasDisplayName = true; } }
    public string? Bio { get => _bio; set { _bio = value; HasBio = true; } }
    public string? AvatarUrl { get => _avatarUrl; set { _avatarUrl = value; HasAvatarUrl = true; } }
    public string? CountryCode { get => _countryCode; set { _countryCode = value; HasCountryCode = true; } }
    public string? State { get => _state; set { _state = value; HasState = true; } }
    public string? City { get => _city; set { _city = value; HasCity = true; } }
    [JsonIgnore] public bool HasDisplayName { get; private set; }
    [JsonIgnore] public bool HasBio { get; private set; }
    [JsonIgnore] public bool HasAvatarUrl { get; private set; }
    [JsonIgnore] public bool HasCountryCode { get; private set; }
    [JsonIgnore] public bool HasState { get; private set; }
    [JsonIgnore] public bool HasCity { get; private set; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class PreferencesPatch
{
    private string? _preferredCurrency, _language, _timeZone;
    private string[]? _tcgInterests;
    public string? PreferredCurrency { get => _preferredCurrency; set { _preferredCurrency = value; HasCurrency = true; } }
    public string? Language { get => _language; set { _language = value; HasLanguage = true; } }
    public string? TimeZone { get => _timeZone; set { _timeZone = value; HasTimeZone = true; } }
    public string[]? TcgInterests { get => _tcgInterests; set { _tcgInterests = value; HasInterests = true; } }
    [JsonIgnore] public bool HasCurrency { get; private set; }
    [JsonIgnore] public bool HasLanguage { get; private set; }
    [JsonIgnore] public bool HasTimeZone { get; private set; }
    [JsonIgnore] public bool HasInterests { get; private set; }
}
