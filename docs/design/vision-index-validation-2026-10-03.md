# Owned visual index validation — 2026-10-03

The backend CLI generated real embeddings for all 102 owned English Base Set artworks in the isolated `vision_delivery` database. Model: quantized CLIP `Xenova/clip-vit-base-patch32`, revision `d15189d7028b43f1d3e65039190477f6af591c2a`, SHA256 `583fd1110a514667812fee7d684952aaf82a99b959760c8d7dca7e0ab9839299`, 512 dimensions, ONNX Runtime 1.23.2, shared ImageSharp preprocessing.

First build: generated 102, unchanged 0, pending 0, failed 0. Second build: generated 0, unchanged 102, pending 0, failed 0. Index version was stable: `d428fafcab33244ec37295db2a56049eddfefa341f06bbd8c3252e07ed48f070`.

A query with the original official Alakazam image (`base1-1.png`) returned canonical Printing `aac2b58c-d66e-4ec1-824f-bcc61851da68` first, cosine approximately 1. PostgreSQL confirmed Alakazam, collector number 1/102. Cosine is a retrieval score, not an identity probability or physical-card finish confirmation.

The system's `--catalog-assets-import all` automatically followed artwork import with the reference build: 102 artwork unchanged, 102 vectors unchanged, no missing/failures, 102 local price snapshots checked. This is backend orchestration; no per-card manual downloads or embeddings. Configured `--catalog-sync` follows the same preparation path. Model installation is a separate finite setup command with pinned-source checksum verification.

Artifacts remain in local private MinIO, not AWS S3 at this checkpoint. AWS account 142767402064 bucket `vaulta-assets-142767402064-us-east-1` had no objects in `catalog-artwork/` when verified. No production deploy or Android/physical scan accuracy is claimed here.

Reproduce with the API CLI and configured DB/storage/model path:

- `--vision-model-install <manifestPath>`: install/check real weights.
- `--vision-index-build <manifestPath>`: resumable generation.
- `--vision-index-status`: load persisted current model index.
- `--vision-index-probe <imagePath>`: bounded operator Top-5 query.
- `--catalog-assets-import all`: artwork plus automatic generation when Vision model path is configured.

Model/index manifests prevent comparison across model, weight, tensor, dimension or preprocessing changes. Pending and failed references remain visible for resume; a preparation report is incomplete while artwork/references are missing or failed. API worker refreshes the local DB index, without external catalog or model downloads in scan requests.
