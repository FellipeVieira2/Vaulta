# Home Marketplace Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Disponibilizar metadados e busca de catálogo na página pública de anúncios, preservando o contrato legado.
**Architecture:** Marketplace consulta Catalog pelos ports existentes, em lote; reputação continua em lote. O cliente App.Core consome o contrato aditivo. Sem acessar DbContext de outro módulo ou executar reconhecimento/precificação por anúncio.
**Tech Stack:** .NET 10, ASP.NET Core, EF Core/PostgreSQL, xUnit, Testcontainers, MAUI App.Core.
**Spec:** docs/superpowers/specs/2026-10-01-home-marketplace-design.md

## Global Constraints

- Preservar monólito modular, .NET 10 MAUI e PostgreSQL/EF Core.
- PriceBrl continua preço pedido; cotação não integra este incremento.
- Novos campos/parâmetros são opcionais; defaults de page=1, pageSize=20, máximo 100.
- Busca por carta/set/número, não por nome público de vendedor.
- Preservar alterações preexistentes de CatalogPorts.cs e CatalogQueries.cs; revisar diff antes de editar e usar patches localizados.
- Nenhuma implantação, alteração de pagamento, commit de arquivos alheios ou implementação MAUI neste incremento.

## Review Focus

- Texto sem correspondência deve devolver zero anúncios, não desativar filtro.
- Empate em preço/data deve ter desempate por Id consistente.
- Metadados incompletos devem manter anúncio com Printing null, sem dados inventados.
- Filtro de jogo com texto vazio deve continuar aplicando game.
- Legacy HTTP/cliente e passagem do CancellationToken devem permanecer compatíveis.

## Task 1: Contrato público, enriquecimento e filtros

**Files — modify:**
- src/Modules/Marketplace/Vaulta.Marketplace.Contracts/Models.cs
- src/Modules/Marketplace/Vaulta.Marketplace.Application/Queries.cs
- src/Modules/Marketplace/Vaulta.Marketplace.Application/MarketplacePorts.cs
- src/Modules/Marketplace/Vaulta.Marketplace.Infrastructure/ContextAdapters.cs
- src/Modules/Marketplace/Vaulta.Marketplace.Infrastructure/MarketplaceQueries.cs
- src/Modules/Catalog/Vaulta.Catalog.Infrastructure/CatalogQueries.cs
- src/Vaulta.Web.Api/MarketplaceEndpoints.cs

**Files — create:**
- tests/Vaulta.Identity.IntegrationTests/MarketplaceBrowseTests.cs

**Interfaces:**
- Create ListingPrintingDto(string CardName, string SetName, string CollectorNumber, string Language, string GameCode, string? ArtworkUrl, string? VariantCode).
- Append ListingPrintingDto? Printing = null to ListingDto, preserving all prior positional fields.
- Create BrowseListingsQuery(Guid? SellerUserId, Guid? PrintingId, Guid? VariantId, string? Query, string? GameCode, int Page, int PageSize, string Sort).
- Add IMarketplaceQueries.BrowseListings(BrowseListingsQuery query, CancellationToken cancellationToken) -> Task<ListingPageDto>.
- Existing ListActiveListings signature stays and delegates with Query/GameCode null.
- Add IMarketplaceCatalog.GetPrintings(IReadOnlyCollection<Guid> ids, CancellationToken ct) -> Task<IReadOnlyList<CollectionPrintingDetails>>.
- Add IMarketplaceCatalog.GetVariants(IReadOnlyCollection<Guid> ids, CancellationToken ct) -> Task<IReadOnlyList<CollectionVariantDetails>>.
- Add IMarketplaceCatalog.SearchPrintingIds(string? query, string? gameCode, CancellationToken ct) -> Task<IReadOnlyList<Guid>>; adapter delegates Catalog.SearchPrintingIds(query, gameCode, null, ct).
- ICatalogCollectionReader signatures already exist locally; do not recreate them.

- [x] Inspect current diff for the two Catalog files; identify user edits to preserve.
- [x] Add failing integration tests using ApiFixture and seeded active seller/listings, following CollectionListingFlowTests setup. Required test names/assertions:
  - BrowseReturnsPrintingMetadataAndLegacyFields: assert CardName/CollectorNumber/Language/ArtworkUrl; preserve Id/PriceBrl/Photos/reputation.
  - BrowseNoMatchReturnsEmpty: assert Items empty and TotalCount 0 for an unmatched query.
  - BrowseFiltersGameWithoutQuery: seed two games; assert only requested game with whitespace query.
  - BrowseSearchesCardSetAndNumber: query card name, set name and collector number independently; assert same expected printing.
  - BrowseAppliesExistingFilters: intersect query/game with sellerUserId, printingId and variantId, not OR.
  - BrowseOrdersTiesDeterministically: duplicate price and CreatedAt; assert Id ascending for price_asc and descending for newest/price_desc.
  - BrowseRetainsListingWithoutMetadata: inject counting IMarketplaceCatalog returning no metadata; assert Printing null and listing still present.
  - BrowseBatchesDistinctIds: counting decorator asserts one GetPrintings call, at most one GetVariants, distinct IDs; no per-item GetPrinting calls.
  - BrowseExcludesSuspendedSellersAndInactiveListings: assert both absent.
- [x] Run tests to confirm failure: dotnet test tests/Vaulta.Identity.IntegrationTests --filter FullyQualifiedName~MarketplaceBrowseTests. Missing contract/failing assertions must explain the failure; environment failure is not a red test.
- [x] Implement DTO/ports/adapters and BrowseListings. Apply Catalog search before count/pagination only when query/game nonempty; no filters means skip the search. If search returns no IDs, return empty normalized page. Enrich distinct page IDs after materialization; map absent metadata to null.
- [x] Expand Catalog search predicate to normalized card name OR normalized set name OR collector number. Retain AND game/set filters and existing callers. Parameterize EF predicates; do not build SQL with user text. Use existing CatalogNormalizer and inspect domain names before choosing the set-name normalization expression.
- [x] Add ThenBy(Id) matching each order direction. Do not claim offset pagination is immutable when new listings arrive.
- [x] Extend endpoint with optional query/game; keep route/name/defaults and delegate new method.
- [x] Run targeted tests plus existing CollectionListingFlowTests; verify current catalog search tests remain compatible. Run architecture tests for module boundaries.

## Task 2: App.Core client compatibility

**Files — modify:**
- src/Vaulta.App.Core/Marketplace/MarketplaceClient.cs

**Files — create:**
- tests/Vaulta.App.Core.UnitTests/Marketplace/MarketplaceClientTests.cs

**Interfaces:**
- Existing ListActiveListingsAsync signature remains unchanged.
- Add IMarketplaceClient.BrowseListingsAsync(BrowseListingsQueryDto query, CancellationToken cancellationToken = default) -> Task<ListingPageDto>.
- Create BrowseListingsQueryDto in Marketplace.Contracts/Models.cs with same filter fields as BrowseListingsQuery; avoids App.Core referencing Application.
- ListActiveListingsAsync forwards equivalent defaults with Query/GameCode null.

- [x] Write failing tests with existing FakeHttpMessageHandler:
  - BrowseEncodesQueryAndAllFilters: assert Unicode/ampersand query encoded and query/game/seller/printing/variant/page/pageSize/sort present.
  - LegacyListKeepsDefaults: assert original invocation still page=1, pageSize=20, sort=newest.
  - BrowseDeserializesOptionalPrinting: read response with new Printing and old response without it; assert metadata or null.
  - BrowsePropagatesCancellation: canceled request is not converted to empty success.
- [x] Run dotnet test tests/Vaulta.App.Core.UnitTests --filter FullyQualifiedName~MarketplaceClientTests to observe relevant failure.
- [x] Implement query DTO and client method, using Uri.EscapeDataString for string filters and ReadApiJsonAsync for errors; preserve current caller signatures.
- [x] Run dotnet test tests/Vaulta.App.Core.UnitTests and the targeted backend tests once. Report pass/failure with environment limitations.
- [x] Review compatibility, batching and search scope against the spec; record actual results. Stage/commit only explicitly authorized files if a commit is requested later.

## Handoff

Execute in this session using the native workflow once implementation begins; no automatic deployment. This plan covers the backend/client increment only. Scanner publication and shipping each need a separate implementation plan after their remaining domain/integration decisions. These specs do not imply operational availability.


## Execution result — 02/10/2026

Both tasks complete. Full integration 127/127; domain 112/112; App.Core 63/63; architecture 6/6. Independent review found no blocking findings. See docs/design/implementation-home-2026-10-02.md for evidence, preexisting test fixture fixes and a deferred minor test improvement. No commit, push or deployment; MAUI UI remains outside this plan.
