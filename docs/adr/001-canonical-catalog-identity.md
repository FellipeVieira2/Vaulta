# ADR 001 — Identidade canônica e ingestão do Catalog

- Status: Aceita para a primeira implementação do Catalog
- Contexto: Vaulta precisa consumir dados externos Pokémon sem tornar um provider a identidade ou o proprietário do domínio. As mesmas entidades serão referenciadas no futuro por Collection, Scanner, Pricing e Marketplace.

## Decisões

### Modelo canônico

- O schema `catalog` é o dono de `Game`, `Series`, `Set`, `Card`, `Printing`, `Variant`, `CatalogExternalId` e `CatalogSyncRun`.
- `Game` possui `Series`, `Set` e `Card`. `Set` pertence a um `Game` e pode referenciar zero ou uma `Series`. `Printing` associa exatamente uma `Card` e um `Set`; `Variant` pertence a uma `Printing`.
- `Card` identifica o conceito canônico de carta; `Printing` identifica uma publicação em um Set, idioma e collector number; Variant descreve acabamento/edição dentro da impressão. Nenhum desses conceitos representa a cópia física do usuário.
- Todos os IDs de domínio são `Guid` gerados pela Vaulta. IDs de provider são dados externos mapeados, nunca PKs ou substitutos da identidade interna.
- `Set.Code` representa um código oficial/canônico quando disponível, não uma cópia automática do ID interno do provider. Quando uma fonte não fornecer código confiável, a resolução usa external ID e não inventa um identificador de domínio a partir do nome.

### Proveniência e entity resolution

- DTOs e IDs externos permanecem na borda de ingestão. Domain não referencia TCGdex nem tipos HTTP/provider.
- `CatalogExternalId` possui provider code, entity type, entity ID interno, external ID, datas de primeira/última sincronização e, quando útil, hash do conteúdo normalizado. Unicidade: `(provider, entity_type, external_id)`; índice adicional por `(entity_type, entity_id)`.
- Resolução prioriza external ID já conhecido. Fallback usa códigos oficiais confiáveis com Game como escopo. Para Printing, usar Set, collector number normalizado, idioma e dimensões de edição necessárias; Variant continua entidade própria.
- Nome normalizado é ferramenta de busca, nunca chave suficiente para fundir Card, Set ou Series. Se não houver evidência inequívoca, criar identidade independente/registrar conflito; não aplicar fuzzy merge silencioso.
- Provider records que não ofereçam identidade estável de Card podem inicialmente produzir Cards separados vinculados aos respectivos Printings. A limitação deve ser reportada no resultado de sync; futura consolidação exige chave autoritativa ou operação explícita, não heurística por nome.
- Sincronizações são idempotentes por external mapping e comparação de conteúdo. Atualizar `UpdatedAt` somente quando dados normalizados relevantes mudarem. Ausência numa resposta do provider não apaga uma entidade: atualizar `LastSeenAt`/disponibilidade conforme a semântica da fonte.

### Normalização

- Preservar nome, idioma, rarity e variant de apresentação/raw conforme recebidos; armazenar separadamente valores normalizados.
- `NormalizedName`: Unicode normalizado, caixa invariável, diacríticos dobrados para pesquisa, pontuação convertida em separadores de token e whitespace colapsado. Não alterar `Name` exibido.
- `CollectorNumber` é string. Normalizar caixa e whitespace sem converter para número, retirar zeros à esquerda ou apagar `/`, letras e sufixos.
- Idioma é armazenado como código BCP 47 canônico quando reconhecido (por exemplo `pt-BR`, `en`, `ja`); aliases são mapeados explicitamente no adapter e o valor raw é preservado quando necessário.
- Rarity e Variant são códigos extensíveis, não enums exaustivos. Guardar valor normalizado e valor raw do provider para valores desconhecidos e revisão futura.
- Normalizadores são funções determinísticas com testes próprios e independentes do adapter HTTP.

### Pesquisa

- Queries usam projeções/DTOs com paginação e limite máximo. Ranking privilegia collector number exato, nome exato, prefixo e similaridade nessa ordem apropriada aos filtros.
- PostgreSQL continua sendo o mecanismo de busca inicial. Índices B-tree cobrem filtros/joins; trigram/GIN só é habilitado por migration e validado com Testcontainers/consultas reais. Não adicionar mecanismo distribuído de busca nesta entrega.

### Provider inicial e sync

- TCGdex é o primeiro adapter de leitura. IDs de provider são ligados por `CatalogExternalId`; a API pública não expõe uma operação de sync. Credenciais não são embutidas e a URL base/idioma do provider são configurações.
- A implementação atual semeia Pokémon como jogo inicial, sincroniza sets e cards por set, registra `CatalogSyncRun` e usa lock consultivo do PostgreSQL por provider/escopo. Cada set é persistido em batch separado; falhas não removem entidades previamente sincronizadas.
- O adapter recebe imagem como URL externa, mas não baixa nem espelha artwork. Armazenamento/licença de imagens exige avaliação dos termos do provider e caso de produto separado.
- Search pública é filtro por nome normalizado com paginação limitada. O scanner tem um fluxo separado de reconhecimento e busca aproximada; a revisão de implementação abaixo descreve seus índices e limitações.

## Consequências

- A troca de provider não muda PKs, contratos canônicos nem entidades de domínio; adapters e mapeamentos externos é que evoluem.
- Dados sem chave de identidade confiável não são automaticamente consolidados. Isso favorece integridade sobre reduzir contagem de registros; qualidade/limitações aparecem nas estatísticas do SyncRun.
- Artwork é referência/proveniência de Catalog, não foto da unidade física. Cache de imagens depende de termos/licença do provider e fica fora desta decisão de identidade.

## Revisão — TCGdex completo e Catalog → Collection (2026-09-26)

- CardBrief é apenas identidade/resumo de exibição. O adapter consulta CardDetails por carta para rarity e o objeto booleano
  `variants`, com concorrência limitada/configurável e atualização em cada sync. DTOs de JSON são privados de Infrastructure;
  os modelos de ingestão neutros pertencem a Application. Não se acrescentam dependências HTTP/JSON ao Domain.
- O ID TCGdex continua sendo external ID de Card e Printing independentes. Não há identidade canônica cross-printing
  autoritativa nesse payload. Novos Cards separados contam como unresolved; nomes nunca provocam merge.
  Novos Sets não recebem o ID TCGdex em `Set.Code`; códigos anteriormente armazenados permanecem intactos.
- Treatments verdadeiros tornam-se Variant com Code/Name/RawValue. Preserva-se o formato minúsculo com hífens do modelo existente,
  sem enum fechado. Ausência do objeto obrigatório de variants é erro de contrato, não autorização para apagar disponibilidade.
- A Collection já armazena VariantId (referência lógica entre módulos, sem FK cruzando schemas no modelo atual).
  Portanto, variants não são removidas fisicamente: IsActive=false mantém identidade e leituras históricas;
  reaparecimento reativa o mesmo Guid. A API pública só oferece ativas, e o reader interno conserva acesso às inativas,
  inclusive para representar cópias físicas antigas ainda possuídas por colecionadores.
- Artwork pertence à Printing: ExternalArtworkUrl/ArtworkProvider são uma referência externa explícita.
  Card.ImageAssetKey preserva a semântica de Asset interno; fotos de unidades físicas continuam em Collection/Assets.
  O adapter centraliza `/high.png` e mantém URLs completas suportadas. Nada baixa ou espelha imagens; mudança de estratégia
  futura será feita na projeção/adapter, preservando os contratos do app e avaliando licença/termos antes de qualquer cache.
- Busca v1 passa de array para CatalogSearchPage (Items/Page/PageSize/TotalCount), com SetId e ArtworkUrl nos itens.
  Detalhes incluem CardId/SetId e CatalogVariantDto(Id/Code/Name). É mudança incompatível intencional durante desenvolvimento,
  documentada no README e refletida no OpenAPI. Nenhum DTO TCGdex integra o contrato público.
- Todas as resoluções de CatalogExternalId recebem explicitamente source.Code. O lock PostgreSQL passa a proteger todo o
  provider, evitando a corrida entre `all` e escopos de sets. Cada set é uma transação; o ChangeTracker é limpo após commit
  e antes de salvar um resultado de falha, evitando flush acidental de entidades incompletas.
- Não há roles administrativas robustas. O mecanismo escolhido é CLI no processo da API, acessível apenas a operadores com
  acesso ao host/container e configuração do banco. Não se expõe endpoint de sync nem se adiciona fila/job runner nesta entrega.
  Status permanece persistido e consultável por CLI; morte abrupta do processo pode exigir inspeção de runs `running`.
- Índices atuais de Card/Game/name, Printing/CardId, Printing/SetId/number/language, Variant/PrintingId/code,
  ExternalId/provider/type/id e SyncRun/provider/started atendem joins/identidade/filtros atuais. Nenhum B-tree adicional
  foi criado prometendo acelerar contains. A migration posterior de reconhecimento do scanner adiciona trigram conforme a revisão abaixo.
- Referências verificadas: https://tcgdex.dev/rest/set, https://tcgdex.dev/reference/card e https://tcgdex.dev/assets.

## Revisão — Printing lifecycle e hardening final (pré-integração MAUI)

- `Printing` ganhou `IsActive` (default `true`), espelhando exatamente o padrão já adotado por `Variant`: não há
  remoção física. Ao final de um sync de set concluído com sucesso, Printings retornadas pelo provider são
  ativadas/reativadas; Printings anteriormente conhecidas para aquele `SetId` e ausentes do lote atual são marcadas
  `IsActive = false` dentro da mesma transação. Reaparecimento reativa exatamente o mesmo `PrintingId` (mesmo Guid),
  preservando `CardId`, `SetId`, External IDs e todo o histórico associado.
- A desativação só ocorre após o sync do set inteiro persistir com sucesso (upsert de Set, Cards, Printings e
  Variants concluído e commitado). Timeout, HTTP 429/500, JSON inválido, cancelamento ou falha de persistência
  interrompem a sincronização antes de qualquer `SaveChanges`/commit; nenhuma Printing é desativada nesses casos.
- Catálogo público (`GET /api/v1/catalog/search`, `GET /api/v1/catalog/printings/{id}`) só retorna
  `Printing.IsActive == true`; uma Printing inativa responde `404` no detalhe público e não aparece na busca.
- `ICatalogCollectionReader` (usado por Collection) continua resolvendo Printings ativas e inativas via
  `GetPrinting`/`GetPrintings`, agora expondo `IsActive` no DTO `CollectionPrintingDetails`. Isso preserva a leitura
  de itens de coleção históricos mesmo quando a Printing correspondente deixou de ser oferecida pelo provider.
- Novas inserções em Collection (`POST /api/v1/me/collection/items`) verificam `IsActive` e rejeitam com `409
  Conflict` quando a Printing referenciada está inativa; leituras de entradas/itens já existentes não usam esse
  gate e continuam funcionando normalmente.
- Índice composto `ix_catalog_printings_set_active (SetId, IsActive)` foi adicionado por estar diretamente alinhado
  às consultas de sync (`DeactivateMissingPrintings`) e de busca pública por set/atividade; nenhum índice isolado
  adicional foi criado sem uso concreto.
- Dívida técnica documentada: `CatalogSyncService` ainda fixa `GameCode = "pokemon"` internamente. Antes de um
  segundo TCG/provider, esse valor precisa se tornar configuração por provider/fonte, não mais uma constante.
- Limitação conhecida: se o processo morrer no meio de um sync, o `CatalogSyncRun` correspondente pode permanecer
  com `Status = "running"` indefinidamente. Não há job manager para expirar/corrigir automaticamente esse estado
  nesta entrega; runs muito antigos em `running` devem ser investigados manualmente.

## Revisão de implementação — 2026-10-01

- A resolução atual do sync utiliza `(provider, entity_type, external_id)`. O resolver do reconhecimento Pokémon TCG também filtra explicitamente `provider = pokemontcg`. O fallback canônico por set + collector number + idioma descrito na decisão permanece pendente; não se deve presumir consolidação entre providers sem esse caminho.
- `NormalizeLanguage` atualmente preserva o código recebido após trim e troca de `_` por `-`; `pt` permanece `pt`. Tanto `pt` quanto `pt-BR` são códigos BCP 47, mas expressam escopos distintos. O idioma da printing não é a preferência de interface `pt-BR` do usuário. Uma política de aliases e regionalização explícita permanece pendente; não inferir uma região pela língua.
- A migration `ScannerFuzzyRecognition` instala `pg_trgm` e índices GIN sobre nome normalizado e collector number para o reconhecimento. A busca aproximada do scanner preserva os IDs internos da Printing e não usa similaridade para fundir entidades durante sync.
