namespace Vaulta.Collection.Infrastructure;

/// <summary>
/// EF entity backing the idempotency ledger for Collection mutating endpoints. This is an
/// infrastructure/persistence concern (HTTP retry safety), not a domain aggregate, so it lives here
/// instead of Vaulta.Collection.Domain.
/// </summary>
public sealed class CollectionIdempotencyKey
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Operation { get; set; } = null!;
    public string IdempotencyKey { get; set; } = null!;
    public string RequestHash { get; set; } = null!;
    public int ResponseStatus { get; set; }
    public string ResponsePayload { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}
