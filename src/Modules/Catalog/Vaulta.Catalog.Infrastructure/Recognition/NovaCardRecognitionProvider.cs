using Vaulta.Catalog.Application;

namespace Vaulta.Catalog.Infrastructure.Recognition;

public sealed class NovaCardRecognitionProvider(ICardEvidenceExtractor extractor, CardEvidenceCatalogMatcher matcher, ICardRecognitionProvider fallback) : ICardRecognitionProvider
{
    public string GameCode => fallback.GameCode;
    public Task<IReadOnlyList<CardRecognitionCandidate>> IdentifyAsync(byte[] imageData, CancellationToken cancellationToken) =>
        new EvidenceCardRecognitionProvider(extractor, matcher, fallback, "nova", "ocr", GameCode).IdentifyAsync(imageData, cancellationToken);
}
