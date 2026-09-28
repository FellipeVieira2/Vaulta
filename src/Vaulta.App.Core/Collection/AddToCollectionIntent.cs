namespace Vaulta.App.Core.Collection;

/// <summary>
/// Represents one voluntary user intent to add items to the collection (a single tap of
/// "Adicionar à coleção"). The Idempotency-Key it carries must be reused for any automatic
/// retry of that same request, and a new instance must be created for the next intent.
/// </summary>
public sealed class AddToCollectionIntent
{
    public AddToCollectionIntent() => IdempotencyKey = Guid.NewGuid().ToString();

    public string IdempotencyKey { get; }
}
