# Vision improvement and scanner reveal implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Build verified development memory, objective diagnostics/benchmarks and a short scanner value reveal.

**Architecture:** Extend the existing Vision history/promotion path and process-local index. Keep presentation independent of stored session state. Offline experiments never change production encoder configuration.

**Tech stack:** .NET 10, EF Core/PostgreSQL, ONNX Runtime, ImageSharp, MAUI, Figma.

**Spec:** docs/superpowers/specs/2026-10-04-vision-improvement-and-reveal-design.md

## Global constraints

- Defaults safe; only allowlisted developers in explicitly configured Development may auto-promote.
- No online training, lowered acceptance thresholds, production model switch or invented benchmark numbers.
- Original prediction, original retrieval and feedback revisions remain separate.
- Consent, deletion, retention and held-out group exclusions remain enforced.

## Review focus

- Feedback precedes upload: capture confirmation retries eligible promotion without predicting a label.
- Same bytes across attempts: do not create duplicate or conflicting canonical references.
- Burst corrections or withdrawals: one bounded refresh and no stale sensitive reference after eventual refresh.
- Price exists for another variant or raw graded card: never animate or sum it.
- Leave/account change while animating: no timers, totals or actions affect another session.

### Task 1: Development promotion and refresh

Files: VisionHistoryService, VisionImprovementOptions, VisualIndexRefreshSignal/Worker, DependencyInjection, VisionHistoryModels; integration/unit tests.

- [x] Add failing allowlist/configuration/promotion/capture-late/revision/SHA-dedup tests.
- [x] Validate with real database and private assets; implement gated promotion using existing PromoteAsync.
- [x] Add bounded refresh signal, burst coalescing and last-success diagnostics; signal committed reference invalidation too.
- [x] Run relevant suites; document Development opt-in configuration.

### Task 2: Diagnostics and benchmark

Files: VisionScannerModels, VisionScannerService, VisionHistoryService, VisionDatasetCommands, VisionBenchmarkMetrics; tests.

- [x] Test raw retrieval ordering independently of displayed candidates and Top-1/5/10/MRR denominators.
- [x] Persist original ranked matches and compute sanitized per-scan diagnostic attribution after human feedback.
- [x] Add CLI status; extend frozen benchmark with final accuracy, status/read-rate metrics and official-only comparison.
- [x] Test held-out SHA/session exclusions and no operational runs added by benchmark.

### Task 3: Offline encoder/preprocessing comparison

Files: encoder manifest/preprocessing, evaluation CLI/harness/scripts; tests and documentation.

- [x] Test distinct preprocessing identities and reject experimental manifests in production mode.
- [x] Support isolated current/letterbox/direct-resize benchmark plus pinned CLIP/DINOv2, runtime/size/latency/memory reports.
- [x] Run real-phone baseline when valid images exist; otherwise emit insufficient dataset and no fabricated metrics.

### Task 4: Scanner feedback and reveal

Files: ScannerRevealPresentation/Motion, ScannerSessionPage.Scene/Continuous/History/Diagnostics; App.Core tests.

- [x] Test available/missing/pending/graded/duplicate presentation states.
- [x] Replace particles/static value with identity→value→total transitions; use previousTotal; cancellation/reduced motion.
- [x] Wire explicit review feedback after capture archive, development memory notices and debug-only sanitized export.
- [x] Run App.Core suite and Android ARM64 build.

### Task 5: Figma and final validation

- [x] Reuse discovered Scanner components/tokens/artwork; add four editable states and motion spec/prototype.
- [x] Verify screenshot/structure and link the existing file to the report.
- [x] Run final suites and independent review; fix material findings.
- [x] Record current production configuration, exact dataset counts, measured metrics or explicit unavailable values and next collection procedure.

Execution follows the user's existing authorization to implement autonomously without repeated confirmation. This task adds code and reviewable design; public development auto-promotion remains disabled.

## Pending evidence, outside implementation completion

- [ ] Collect 100 consented and human-verified physical-card phone images across 20 printings and independent reference/evaluation sessions.
- [ ] Run real-phone baseline and official-only versus verified comparison; report actual accuracy and failure distribution.
- [ ] Validate capture and reveal on a physical Android device.

The empty dataset CLI and six real ONNX runtime combinations were verified. They do not replace real-camera accuracy measurement. Production deployment and Developer allowlist activation were not performed.
