# Vision improvement and scanner reveal

Source of truth: the two user prompts supplied on 2026-10-04, sections 1–90. Extend the existing modular monolith; keep the production encoder, preprocessing and 0.80 acceptance threshold unchanged. No online weight updates and no confidence-based ground truth.

## Verified starting state

Local master 2035fc6 includes remote master acc0812 plus the previous deployment fixes. ONNX CLIP weights are fixed, 512D, ImageSharp shortest-edge resize and 224px center crop. History and improvement consent are separate. Feedback preserves predictions; operator promotion adds `verified_capture` references. The worker reloads every 30 seconds. The displayed candidate list is reordered by the resolver and truncated to five: it cannot represent the original retrieval Top-10. The current reveal is a static value plus particles and updates the total before the animation.

The running AWS API explicitly reports Production, History enabled, ops-v1/improve-v1. Reviewed sample count is zero. Do not change that environment while implementing development mode.

## Development improvement

Explicit `Vision:Improvement:DeveloperAutoPromote`, default false. Enabled mode requires Development environment, history enabled, and a nonempty allowlist of developer account UUIDs. Ordinary users remain on manual review even in Development. The existing promotion checks still validate current improvement consent, active attempt, stored matching immutable bytes, completed run, executed encoder, front/card-present labels and canonical Printing/Variant. Capture completion may arrive after feedback: retry promotion then, under the same checks. Never promote automatically accepted scans.

References are deduplicated by SHA + model + printing; conflicting labels across attempts fail closed. Revision, withdrawal and retention invalidate references. Signal an in-process bounded channel after committed changes; batch events with a configurable 2-second window and preserve 30-second refresh fallback. No rebuild per scan.

## Diagnostics and evaluation

Preserve original ranked retrieval separately from resolver candidates. CLI status reports encoder, index version/count/last refresh, origins, active private captures, pending feedback, reviews and configuration flags. Debug-only app diagnostics observe and export sanitized results without image bytes, image hashes, owner IDs, certification numbers or signed artwork URLs.

Benchmark reports distinct printing Top-1/5/10, MRR, final printing/variant accuracy, resolver status distribution, evidence read rates and heuristic failure attribution (retrieval/evidence/resolver/unknown). Evaluate standalone without reserving new operational scan runs. Keep held-out assets, identical SHAs and their session groups excluded. Compare official-only and official + verified references.

Preprocessing/model experiments are offline and explicitly versioned. Current C# preprocessing is the baseline; letterbox and direct-resize get distinct identities. CLIP and pinned DINOv2 use isolated indexes and checksum-verified weights. SigLIP is optional only after license and runtime compatibility verification. No production switch before real-phone metrics. Zero samples means no accuracy claims and a clear collection requirement of at least 20 printings × 5 conditions.

## Presentation

Persist/add each occurrence once before presentation. A resolved card or explicit human selection gets one integrated reveal: identity/artwork (220ms), price count-up (460ms), subtle pop (180ms), total count-up (220ms), fade (120ms). Motion uses existing reduced-motion detection and cancels on page/account/session changes. No new capture while reveal runs; no capture queue. Missing, variant-pending and graded-unavailable quotes never animate a raw or zero value. A real available zero quote is distinct from missing data. Brief +value near the total; no confetti or loud sound. Inter, dark Vaulta surface, purple accent and white price.

Figma adds four editable states of the same scanner experience and a motion specification using existing tokens/artwork. No duplicated full library.

## Acceptance

Security and revision/retention tests; raw retrieval ordering, metrics and held-out leakage tests; UI state/pricing/duplicate tests; backend/App.Core suites and Android build; Figma structural and visual validation. Real-world benchmark must be run only on actual verified phone images. Report incomplete measurement honestly if dataset is empty; never substitute synthetic artworks for it.
