# Incremental official-artwork embeddings

User authorized resolving the stalled pipeline and running the ingestion. Existing artwork job remains active. Current host: 917MiB RAM + 2GiB swap, artwork limited to 0.5 CPU. Keep public API, encoder weights, thresholds and consent flags unchanged.

- [x] Add regression tests: ready-only bounded batches, later images, idempotency, reused assets, backoff, duplicate provider set IDs and bounded metadata retries.
- [x] Add incremental builder sharing the existing model advisory lock; persistent vectors serve as checkpoint. No pending row per image that is not downloaded.
- [x] Add operator-only follow command and durable dedicated worker with 0.25 CPU / 512MiB RAM / 1024MiB combined memory+swap. API reloads persisted vectors on existing timer.
- [x] Correct duplicate Chinese set briefs before creating checkpoints; retry only incomplete metadata scopes, maximum three attempts.
- [ ] Validate on isolated PostgreSQL, build immutable worker image, deploy only worker/control scripts and verify images and vectors both increasing.
- [ ] Record deployed worker identity, live counts and remaining provider failures. Do not declare whole catalogue complete.
