# Catalog/artwork ingestion validation — 2026-10-03

Isolated local PostgreSQL database `vision_delivery` and private MinIO bucket `vaulta-vision-artwork`; no production deployment performed.

- Actual TCGdex `en/base1` synchronization: 102 canonical Printings, started 06:52:57Z, finished 06:53:04Z. Metadata includes explicit detailed variants and their own marketplace mappings/pricing.
- First artwork ingestion exposed 21 HTTP 503 responses and unsupported detailed quote codes. Regressions observed RED, bounded transient retry and per-variant pricing parser implemented, 296/296 unit tests GREEN.
- Retry: 21 artworks imported, 81 unchanged, 0 missing, 0 failed. Database verified 102/102 owned artworks and 102 daily local market snapshots with quote/variant/source/FX provenance. Counts do not mean every variant has a quote.
- PostgreSQL tests: 24/24 relevant catalog/assets/local-quote tests passed, including interrupted PUT/resume, owner isolation, changed source hash, ETag 304, no-ETag comparison, partial sync/resume and canonical UUID stability.
- Request details reader has no HTTP client/provider dependency. It preserves known stale quotes with an explicit pending-update notice and returns an explicit unavailable-price state for an unpriced Printing.
- Catalog queries now return an internal artwork route only when the owned asset exists; external source URLs remain ingestion metadata.
- Pricing format reference: https://tcgdex.dev/es/reference/card and actual `variants_detailed[].pricing` responses. A variant with no own quote never inherits another edition's root price. Legacy boolean variants and an explicit single generated variant remain supported.

This is ingestion validation, not a physical-camera accuracy benchmark. Visual index, scanner/app integration and APK are subsequent tasks.