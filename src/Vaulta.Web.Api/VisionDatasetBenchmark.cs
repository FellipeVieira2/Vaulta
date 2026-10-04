using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Vaulta.Assets.Application;
using Vaulta.Catalog.Application;
using Vaulta.Vision.Application;
using Vaulta.Vision.Contracts;
using Vaulta.Vision.Domain;
using Vaulta.Vision.Infrastructure;

namespace Vaulta.Web.Api;

internal static class VisionDatasetBenchmark
{
    public static async Task RunAsync(IServiceProvider services, VisionBenchmarkManifestDto manifest, int limit, string path)
    {
        var db = services.GetRequiredService<VisionDbContext>();
        var history = services.GetRequiredService<VisionHistoryService>();
        var expected = manifest.Samples.Take(limit).Select(s => new VisionBenchmarkLabel(s.SampleId.ToString(), s.PrintingId, s.VariantId, s.Orientation, s.Presence)).ToArray();
        var excluded = await history.EvaluationExcludedAssetsAsync(manifest, default);
        object? official = null, expanded = null;
        var legacyCropOnly = new HashSet<Guid>();
        if (expected.Length > 0)
        {
            var builder = services.GetRequiredService<IVisualReferenceBuilder>();
            var index = services.GetRequiredService<IVisualReferenceIndex>();
            var catalog = services.GetRequiredService<IVisionCatalog>();
            var evidence = new FrozenEvidenceReader(services.GetRequiredService<IVisionEvidenceReader>());
            var encoder = new FrozenEncoder(services.GetRequiredService<IImageEncoder>());
            var scanner = new VisionScannerService(encoder, index, catalog, evidence,
                new NoBenchmarkQuotes(), services.GetRequiredService<VisionPrintingResolver>());
            var assets = services.GetRequiredService<IPrivateAssetService>();

            async Task<object> Evaluate(bool officialOnly)
            {
                if (officialOnly) await builder.LoadOfficialForEvaluationAsync(excluded, default);
                else await builder.LoadForEvaluationAsync(excluded, default);
                var predictions = new List<VisionBenchmarkLabel>();
                var observations = new List<VisionRetrievalObservation>();
                var diagnostics = new List<object>();
                foreach (var sample in manifest.Samples.Take(limit))
                {
                    var capture = await db.ScanCaptures.AsNoTracking().SingleAsync(x => x.Id == sample.CaptureId);
                    var attempt = await db.ScanAttempts.AsNoTracking().SingleAsync(x => x.Id == capture.AttemptId);
                    var crop = await assets.ReadOwnedAsync(attempt.OwnerId, capture.AssetId, default) ?? throw new InvalidDataException("Benchmark image unavailable.");
                    if (!Sha(crop).Equals(sample.Sha256, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Frozen image changed.");
                    var run = await (from f in db.Feedback.AsNoTracking() join r in db.ScanRuns.AsNoTracking() on f.RunId equals r.Id where f.Id == sample.FeedbackRevision select r).SingleAsync();
                    var trace = run.ManifestJson is null ? null : JsonSerializer.Deserialize<VisionScanTraceDto>(run.ManifestJson, new JsonSerializerOptions(JsonSerializerDefaults.Web));
                    var inputs = new List<VisionCaptureInput> { new(crop, "card-crop") };
                    if (trace?.EvidenceImageSha256 is {} evidenceSha && evidenceSha != sample.Sha256)
                    {
                        var frame = await db.ScanCaptures.AsNoTracking().FirstOrDefaultAsync(x => x.AttemptId == attempt.Id && x.Status == "ready" && x.Sha256 == evidenceSha);
                        if (frame is not null)
                        {
                            var full = await assets.ReadOwnedAsync(attempt.OwnerId, frame.AssetId, default) ?? throw new InvalidDataException("Evidence frame unavailable.");
                            if (!Sha(full).Equals(evidenceSha, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Evidence frame changed.");
                            inputs.Add(new(full, "full-frame"));
                        }
                        else legacyCropOnly.Add(sample.SampleId);
                    }
                    var result = await scanner.IdentifyAsync(new(inputs), default);
                    var canonical = sample.PrintingId is {} p ? (await catalog.GetPrintingsAsync([p], default)).SingleOrDefault()?.Printing : null;
                    observations.Add(VisionScanDiagnostics.Observation(sample.SampleId.ToString(), sample.PrintingId, result, canonical));
                    diagnostics.Add(new { sample.SampleId, diagnostic = VisionScanDiagnostics.Export(result), correctPrintingRank = VisionRetrievalMetrics.Rank(sample.PrintingId, result.Retrieval?.Select(x => x.PrintingId) ?? []) });
                    string? Field(string name) => result.Evidence.TryGetValue(name, out var f) && f.Confidence is >= .8 and <= 1 ? f.Value : null;
                    predictions.Add(new(sample.SampleId.ToString(), result.PrintingId, result.VariantId, Field("cardSide"), Field("isCard") switch { "true" => "card-present", "false" => "no-card", _ => null }));
                }
                return new { index = index.Status, predictions, metrics = VisionBenchmarkMetrics.Evaluate(expected, predictions), retrieval = VisionRetrievalMetrics.Evaluate(observations), diagnostics };
            }
            official = await Evaluate(true);
            expanded = await Evaluate(false);
        }
        var report = new
        {
            manifest.DatasetVersion, measurement = expected.Length == 0 ? "insufficient_real_world_dataset" : "real_verified_capture",
            samples = expected.Length, excludedAssets = excluded.Count, officialOnly = official, officialAndVerified = expanded,
            evidenceReusedBetweenIndexVariants = true, legacyCropOnlySamples = legacyCropOnly.Count,
            failureAttributionMethod = "heuristic-not-causal-proof", operationalRunsCreated = 0, priceProviderCalls = 0
        };
        await File.WriteAllTextAsync(Path.GetFullPath(path), JsonSerializer.Serialize(report, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
        Console.WriteLine(JsonSerializer.Serialize(new { manifest.DatasetVersion, samples = expected.Length }));
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private sealed class NoBenchmarkQuotes : IScannerCardDetailsReader
    {public Task<Vaulta.Catalog.Contracts.ScannerCardDetailsDto?> GetAsync(Guid printingId,CancellationToken ct)=>Task.FromResult<Vaulta.Catalog.Contracts.ScannerCardDetailsDto?>(null);}
    private sealed class FrozenEvidenceReader(IVisionEvidenceReader reader) : IVisionEvidenceReader
    {
        private readonly Dictionary<string, VisionEvidenceReading> _readings = [];
        public async Task<VisionEvidenceReading> ReadAsync(byte[] image, IReadOnlyList<VisionCatalogPrinting> candidates, CancellationToken ct)
        {
            var key = Sha(image);
            if (!_readings.TryGetValue(key, out var value)) _readings.Add(key, value = await reader.ReadAsync(image, candidates, ct));
            return value;
        }
    }
    private sealed class FrozenEncoder(IImageEncoder encoder) : IImageEncoder
    {
        private readonly Dictionary<string, ImageEmbedding> _embeddings = [];
        public EncoderIdentity Identity => encoder.Identity;
        public async Task<ImageEmbedding> EncodeAsync(Stream image, CancellationToken ct)
        {
            using var copy = new MemoryStream(); await image.CopyToAsync(copy, ct);
            var key = Sha(copy.ToArray());
            if (!_embeddings.TryGetValue(key, out var value)) { copy.Position = 0; _embeddings.Add(key, value = await encoder.EncodeAsync(copy, ct)); }
            return value;
        }
    }
}
