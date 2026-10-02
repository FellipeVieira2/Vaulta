using Vaulta.Catalog.Application;

namespace Vaulta.Catalog.Infrastructure.Recognition;

internal sealed class FallbackCardRecognitionProvider(ICardRecognitionProvider primary, ICardRecognitionProvider fallback) : ICardRecognitionProvider
{
    public string GameCode => primary.GameCode;
    public async Task<IReadOnlyList<CardRecognitionCandidate>> IdentifyAsync(byte[] imageData, CancellationToken ct)
    {
        var candidates = await primary.IdentifyAsync(imageData, ct);
        ct.ThrowIfCancellationRequested();
        return candidates.Count > 0 ? candidates : await fallback.IdentifyAsync(imageData, ct);
    }
}
