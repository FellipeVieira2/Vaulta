# Task 2 report — scanner session and review

Implemented against Task 1 contracts at base f667c35. Owned App.Core scanner/session/import/valuation/sale-flow files, ScannerSessionPage partials and App.Core tests only. No contracts, API, providers, cache or DI changes.

Trusted visual readings at >= 0.8 can create a physical occurrence with PrintingId=Guid.Empty and unknown collector number. Lower confidence requires explicit confirmation without changing the visual confidence. Research value requires compatible name/number/game/language/set/finish/company/grade, finite confidence, positive BRL value and HTTPS sources. Existing exact-variant provider quotes win; research fills compatible missing quotes including graded cards. Captured certificate serial remains unchanged. Research source URLs, checked date, estimate marker and refresh date persist in SessionMarketValue. Trusted unpriced readings count, briefly reveal Sem cotação and continue; the live counter identifies a partial total. Review shows pending identity, estimate/date and clickable source links.

All inventory and listing mutation paths reject pending identity. Both importers reject all-pending work before store freeze or HTTP. Mixed bulk imports only resolved physical occurrences, finish Imported, retain pending cards and retry idempotently. ScannerSaleFlow.Publish now rejects a restored pending identity before any collection HTTP, condition update, freeze or publication. Original owner/cancellation guards and duplicate-copy semantics remain. The one-second camera gate and existing no-recording/no-last-card controls were unchanged.

## Verification commands and outputs

Linux Docker runner vaulta-web-provider-tests was used with existing SDK dependencies. Windows unit-test processes were never launched; no Docker socket mount was used. Commands ran through authorized require_escalated Docker access:

```powershell
docker cp tests/Vaulta.App.Core.UnitTests vaulta-web-provider-tests:/src/tests/
docker exec vaulta-web-provider-tests dotnet test /src/tests/Vaulta.App.Core.UnitTests --filter FullyQualifiedName~ScannerPendingTests
```

Initial RED: Failed 2, Passed 0 — Add rejected PrintingId=Guid.Empty in TrustedReadingWithoutNumberCountsWithoutFabricatingPrintingOrPrice and MixedImportCompletesAfterResolvedCopiesAndRetainsPending. ResearchValue was first compiled as a null stub; subsequent behavioral RED: Failed 1, Passed 2, Assert.NotNull failed in ResearchEstimateSumsWithSourcesAndRejectsOtherFinish. Listing-state RED: Failed 1, Passed 8 — PendingCardsCannotAcquireListingStateEvenIfRestoredWithInventoryIds did not throw before guards were added.

```powershell
docker cp src/Vaulta.App.Core vaulta-web-provider-tests:/src/src/
docker cp tests/Vaulta.App.Core.UnitTests vaulta-web-provider-tests:/src/tests/
docker exec vaulta-web-provider-tests dotnet test /src/tests/Vaulta.App.Core.UnitTests --filter FullyQualifiedName~PendingRestoredPrinting
```

Sale guard RED: Failed 1, Passed 0 — expected zero inventory updates, actual 1. GREEN now asserts the collection call count stays at the single resolved Prepare call performed before restoring a pending ID, zero updates/publications and unchanged store.

```powershell
docker exec vaulta-web-provider-tests dotnet test /src/tests/Vaulta.App.Core.UnitTests --filter FullyQualifiedName~ExistingMatchingQuoteWins
```

Exact-quote/missing-research-value RED: Assert.IsType failed because SessionValue stub was null. During self-review, an added canonical-language mismatch assertion was RED: expected null, actual researched 500 BRL. Fixed language compatibility with normalized language prefix (pt-BR/pt), keeping different languages separate.

```powershell
docker cp src/Vaulta.App.Core vaulta-web-provider-tests:/src/src/
docker cp tests/Vaulta.App.Core.UnitTests vaulta-web-provider-tests:/src/tests/
docker exec vaulta-web-provider-tests dotnet test /src/tests/Vaulta.App.Core.UnitTests
git diff --check
```

Final full Core GREEN on 2026-10-03: Passed! Failed 0, Passed 189, Skipped 0, Total 189, Duration 483 ms, net10.0. Builds and restore exited 0 without compilation warnings. New coverage includes 0.8 inclusive/.7999/NaN/>1, manual .79 evidence retention, unnumbered pending readings, researched sum/source restart persistence, raw-versus-graded/company/grade/serial compatibility, catalog print/language/variant precedence, pending pre-write rejection and mixed import retries. Existing owner/cancellation/recovery/duplicate-copy/resolved-sale tests remained green. git diff --check clean after removing two extra EOF blank lines; Git emits only local LF-to-CRLF conversion notices.

## Limits and concerns

MAUI Android build and physical-camera/source-link validation remain controller-owned. No deployment performed. Research-to-catalog comparison is deliberately conservative when visible set/collector strings differ; backend corrected identity should supply consistent strings. Pending cards remain visible but cannot be added to inventory or sold until canonical resolution exists. An automatic approval review briefly refused a Docker verification command because usage was exhausted (review could not execute, not an unsafe-action verdict); after the controller resumed with reset quota, the same authorized verification succeeded. No approval bypass was attempted.
