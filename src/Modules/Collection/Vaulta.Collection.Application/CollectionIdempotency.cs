using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Vaulta.Collection.Contracts;

namespace Vaulta.Collection.Application;

/// <summary>
/// Names of Collection operations that require an Idempotency-Key. Kept as constants instead of an
/// enum so new operations can be added without a schema/enum migration.
/// </summary>
public static class CollectionOperations
{
    public const string AddCollectibleItems = "collection.items.add";
}

/// <summary>
/// Computes a deterministic hash of a command payload so a retried request can be distinguished from
/// a different logical request that happens to reuse the same Idempotency-Key.
/// </summary>
public static class IdempotencyRequestHasher
{
    public static string Hash(AddCollectibleItemsRequest request)
    {
        var json = JsonSerializer.Serialize(request);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }
}
