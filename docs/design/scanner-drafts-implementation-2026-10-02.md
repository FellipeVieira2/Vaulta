# Rascunhos privados do scanner — implementação 02/10/2026

Escopo: Task 1 de `docs/superpowers/plans/2026-10-02-marketplace-scanner.md`. Alterações locais anteriores de Home/catalog/scanner preservadas; sem commit, deploy ou migração de banco compartilhado.

## Contratos e ciclo

`ListingDraftModels.cs` contém contratos aditivos. Rotas autenticadas em `/api/v1/me/seller/listing-drafts`: POST criar, GET `/{id}`, PUT `/{id}`, POST `/{id}/photos`, DELETE `/{id}/photos/{assetId}?version=...`, POST `/{id}/publish`, DELETE `/{id}?version=...`. Operações devolvem `ListingDraftDto`; criação usa 201 e demais 200. Ownership usa o usuário autenticado, sem `SellerUserId` recebido do cliente.

`Listing` ganha estado `draft`. Preço ausente é transportado como null, e usa zero somente internamente enquanto privado; nunca é calculado a partir de cotação. Criação de rascunho não publica evento de anúncio, não reserva unidade e não requer ativação de vendedor. Publicação exige perfil de vendedor ativo, unidade ativa do vendedor, identidade/variante correspondente ao catálogo, condição canônica declarada e igual à unidade, preço manual válido e fotos reais FRONT/BACK.

`ListingPublicationService` também revalida vendedor, impressão/variante e disponibilidade/propriedade de cada foto na recuperação, inclusive quando chamado pelo worker. Foto excluída ou tornada indisponível após a revisão cancela a intenção de forma durável antes de liberar a reserva correspondente. Falhas de liberação ficam recuperáveis pelo worker de cancelados. A validação nova só vale para anúncios originados de rascunho, preservando a publicação legada.

Fotos precisam de asset pronto, privado, image/*, de propriedade do vendedor e purpose `collection-item` existente. URL de catálogo não é aceita como asset. Semântica não promete provar por visão computacional que o conteúdo do upload é uma foto da carta; valida provenance/ownership/storage/prontidão e tipo de upload.

Rascunho, publishing e rascunho cancelado não aparecem na listagem nem no detalhe público, e não são compráveis. Rotas antigas de criação de anúncio continuam compatíveis.

## Recuperação e concorrência

Migration incremental `ListingDrafts` acrescenta metadados opcionais e índice único `(seller_user_id, client_draft_key)`. A fingerprint de criação é persistida separadamente do conteúdo editável: retry da requisição original recupera o mesmo ID e o estado atual, inclusive após edição/publicação/cancelamento; reutilização com payload diferente retorna conflito. A constraint protege retries concorrentes, e o store lê a ocorrência vencedora após conflito de unicidade.

Edição e fotos exigem a versão atual. Publicação persiste `PublicationKey` e `PublicationVersion` da revisão, congela o conteúdo em publishing e reutiliza `ListingPublicationService` e `ListingCollectionWorker`. Retry usa **a mesma chave e a mesma versão original**, mesmo quando o DTO já possui versão posterior. Mesma chave com versão diferente retorna conflito. A unidade é reservada somente depois do commit da intenção; o índice único existente de unidades active/publishing impede duas vendas ativas.

Um teste PostgreSQL reproduziu a corrida de duas retomadas do mesmo anúncio em publishing: a concorrência da reserva podia cancelar a intenção válida. O store agora oferece um advisory lock de sessão por ListingId, adquirido pelo serviço antes da recuperação, com releitura do anúncio após adquirir o lock. O lock cobre reserva e ativação, mas não introduz transação entre contextos: a intenção já está persistida. A segunda retomada encontra active e devolve o mesmo ID. O lock é liberado no finally e na queda da conexão.

Ports novos do store: `FindDraftByKey` consulta exclusivamente a chave do vendedor autenticado e inclui fotos; `CreateDraft` persiste o rascunho e recupera o registro vencedor da constraint de chave durável; `LockPublication` devolve um lease assíncrono de sessão que deve cobrir reserva/ativação; `RefreshListing` recarrega campos e versão do aggregate rastreado após adquirir o lease. Fotos de um anúncio em publishing estão congeladas; `FindListing` já carregou sua coleção antes do serviço. `IMarketplaceCollection.GetItemIdentity` lê a identidade da entrada pela query pública do módulo Collection, sem compartilhar EF/IQueryable entre módulos.

## Verificação

- 5 novos testes de domínio observados RED com métodos ainda sem implementação; GREEN 5/5 após implementar as invariantes.
- 11 novos testes de recuperação de aplicação: RED em 9 casos que publicavam indevidamente; GREEN após a validação no serviço compartilhado. Os demais dois cobrem recuperação válida/replay e preservação da criação legada.
- Suite completa `Vaulta.Commerce.UnitTests`: 78/78 aprovados.
- Build Marketplace Infrastructure: zero warnings e erros.
- Migration gerada e `has-pending-model-changes`: nenhum descompasso de modelo/snapshot.
- 11/11 testes HTTP/PostgreSQL aprovados em PostgreSQL 17.11 portátil isolado (versão confirmada por `SELECT version()`): privacidade, ownership, original retry após edição, retries concorrentes, declaração/fotos, compra de rascunho, duas publicações concorrentes, cancellation, interrupção após reserva, foto excluída e retomadas simultâneas do mesmo anúncio.
- Primeiras tentativas de integração falharam na inicialização do fixture porque o Docker local estava indisponível, inclusive com execução elevada. O log do backend identifica falha de inicialização do Inference manager/socket `dockerInference` e parada dos engines. Isso não foi contado como RED comportamental nem como validação de PostgreSQL. O executor principal forneceu um PostgreSQL portátil isolado, usado via `VAULTA_TEST_POSTGRES`.

Suite completa `Vaulta.Identity.IntegrationTests`: **138/138 aprovados**, exit code 0, PostgreSQL 17.11 em banco novo e isolado, execução elevada/sequencial `-m:1`. Isso inclui os 11 testes novos, os fluxos legados e migrations reais. Evidência local: `artifacts/scanner-draft-full-integration-final-fresh.log`.

Diagnósticos intermediários preservados: primeira execução abortou por recursos do sandbox após 37 aprovados e quatro falhas; migrações que ainda exigiam Docker foram adaptadas pelo executor principal ao PostgreSQL isolado. Uma execução terminou com 137 aprovados e falha em `ProductionApiTests.ProductionStartsWithoutStaticAwsKeysAndKeepsHealthLocalAndSwaggerDisabled`, porque a conexão externa de teste omitia Password (trust local); o campo fictício resolveu a configuração sem enfraquecer a validação de produção. Reutilizar o banco dessa rodada produziu 17 falhas em `CollectionApiTests`, cuja fixture consulta `SingleAsync` por nomes fixos repetidos. A rodada final usou um novo banco e passou todos os 138 casos; não houve alteração de regras comerciais para esconder esses diagnósticos.
