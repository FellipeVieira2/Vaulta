# Marketplace Product First Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement task by task. Follow test-driven-development and verification-before-completion. Execution is authorized by the user's “pode seguir” after the Figma handoff; no new approval gate is needed.

**Goal:** Implement the approved Home → exact printing/variant product → offers → physical listing → existing checkout flow.

**Architecture:** Aggregate active listings in PostgreSQL before pagination. Read catalog metadata and stored BRL quotes in batches through application ports; never trigger provider calls from marketplace requests. Keep the existing listing controller for seller pages, and add product browse/detail controllers independent of MAUI.

**Tech Stack:** .NET 10, EF Core/Npgsql, ASP.NET minimal APIs, MAUI, xUnit.

**Spec:** Existing Figma Ey1vEJNXx1oaSZbODMamWY nodes 4:2, 60:461, 4:85, 60:462, 60:463 and the Product First handoff delivered on 04/10/2026.

## Global Constraints
- PrintingId + VariantId is the product identity; explicit `none` means a null variant, never absence of filter.
- All market prices are BRL with provenance. Missing quotes remain missing; no invented history, sales or percentage discounts.
- Canonical art in the product grid, real photos only in offers/listings; no signed seller photos resolved on the Home.
- Active sellers/listings only. Catalog languages and provider variants remain distinct.
- Preserve scanner, optional recording, collection, sell/authentication and checkout.
- Local implementation only; no deployment or remote push in this step.

## Review Focus
- Null variant must not mix with Holo/Reverse in counts or pagination.
- Multiple sellers and copies must produce one product before page boundaries.
- Suspended sellers/cancelled or reserved listings cannot affect minimum/count.
- Slow old searches cannot overwrite new results; failed next pages retain loaded cards.
- Relative catalog artwork URLs must resolve correctly in the app; stale photos remain labelled.

### Task 1: Paged product API and exact offers
**Files:** New Marketplace.Contracts/ProductModels.cs, Application/MarketplaceProductPorts.cs, Infrastructure/MarketplaceProductQueries.cs; Catalog.Application/CatalogMarketplaceReader.cs and Catalog.Infrastructure/CatalogMarketplaceReader.cs; modify module registrations and MarketplaceEndpoints.cs. Tests: Identity.IntegrationTests/MarketplaceProductTests.cs.
**Interfaces:** IMarketplaceProductQueries.BrowseProducts(BrowseProductsQueryDto, ct), GetProduct(Guid printingId, Guid? variantId, ct), GetOffers(Guid printingId, Guid? variantId, page, pageSize, sort, ct). DTO MarketplaceProductDto carries PrintingId, VariantId, metadata, LowestPriceBrl, OfferCount, optional quote.
- [x] Write HTTP tests for aggregation/page boundaries, identity, min/count exclusions, missing/BRL quote and filters.
- [x] Run tests expecting 404 before new routes exist (Linux after Windows loader block).
- [x] Implement grouped SQL pagination, batched local quotes, exact identity endpoints and supported filters.
- [x] Run initial five integration tests with isolated PostgreSQL; inspect SQL translation failures and correct causes.
- [ ] Revalidate the three additional review cases after restoring host disk capacity.

### Task 2: App product browsing and detail state
**Files:** New Core/Marketplace/MarketplaceProductClient.cs, MarketplaceProductBrowseController.cs, MarketplaceProductDetailController.cs, MarketplaceProductPresentation.cs; retain old HomeController for seller listing pages. Tests: App.Core.UnitTests/Marketplace/MarketplaceProductTests.cs.
**Interfaces:** IMarketplaceProductClient.BrowseProductsAsync/GetProductAsync/GetOffersAsync; controllers expose cancellation, generation checks, refresh/retry and paging.
- [x] Write tests for correct product routes, relative artwork, BRL presentation, pagination, exact `none`, old response rejection and recoverable offer failure.
- [x] Run RED for missing behavior, implement controllers/client, run GREEN (224 Core tests).
- [ ] Revalidate the final listing-artwork assertion and normalization added during final verification.

### Task 3: Native MAUI flow
**Files:** Modify Home VM/page and listing detail page/controller; create ProductDetail VM/page, ProductCardView and FiltersPage; register routes/services. Scanner session gets only an explicit marketplace entry.
- [x] Reuse tests from Task 2 to guard state, routing identities and price presentation before wiring views.
- [x] Wire native product grid (artwork AspectFit 171×228 reference size, responsive columns), searchable filters, detail/offer list, physical listing and existing checkout.
- [x] Remove automatic comparison fetching from the app listing page; add “Ver outras ofertas” navigation.
- [ ] Build API and Android; run Core, commerce, identity, architecture and integration suites. Generate an APK for testing if the local Android environment is available.
- [x] Record real validation, remaining limitations and API deployment dependency in docs/design/marketplace-product-first-implementation-2026-10-04.md.

**Current validation:** final Android/API code-only `Compile` targets passed without warnings/errors. Android packaging and broader/final regression runs are blocked by host disk capacity. User was asked to free at least 3 GB on C:. No APK completion, deployment or push is claimed.

## Interface preflight
Task 1 produces MarketplaceProductDto/PageDto, exact variant route semantics and BRL quote provenance consumed by Task 2. Task 2 produces product presentations/controllers consumed by Task 3. Existing seller listing pagination remains on the original HomeController. No conflicting shared interface was found.
