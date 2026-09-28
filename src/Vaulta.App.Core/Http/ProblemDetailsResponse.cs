namespace Vaulta.App.Core.Http;

/// <summary>
/// Minimal shape of the ProblemDetails JSON the API returns on errors. Only the fields the
/// client needs to build a user-facing message; never logged or shown verbatim to the user.
/// </summary>
public sealed record ProblemDetailsResponse(string? Title, int? Status, string? Detail, string? CorrelationId);
