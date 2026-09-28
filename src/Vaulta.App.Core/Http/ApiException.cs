namespace Vaulta.App.Core.Http;

/// <summary>
/// A user-facing API failure. Carries the original HTTP status for diagnostics/retry decisions
/// without ever exposing a raw server message or stack trace to the UI.
/// </summary>
public sealed class ApiException(int? statusCode, string userMessage, string? detail = null)
    : Exception(userMessage)
{
    public int? StatusCode { get; } = statusCode;
    public string? Detail { get; } = detail;
    public bool IsRetryable => StatusCode is null or 429 or >= 500;
    public bool IsUnauthorized => StatusCode == 401;
}
