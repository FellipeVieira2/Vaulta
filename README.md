# Vaulta

Plataforma brasileira para colecionadores de TCGs, com app .NET MAUI e API .NET 10, ASP.NET Core, EF Core e PostgreSQL. A solution contém os módulos **Identity, Catalog, Collection, Assets, Marketplace, Orders, Payments, Wallets, Shipping e Reviews**. A existência dos módulos não significa que todos os fluxos comerciais estejam completos; o acompanhamento está em [Lacunas e validação](docs/business-gap-audit.md).

## Arquitetura e estrutura

Monólito modular com DDD e CQRS seletivo: comandos usam aggregates de cada módulo e queries projetam DTOs sem tracking quando apropriado. Um banco, um processo de API, sem MediatR e sem generic repository. A ordenação da Collection por nome ainda carrega as entradas filtradas antes de paginar; veja ADR 002.

Anúncios vinculam e protegem a unidade da coleção; o recebimento confirmado pelo comprador registra uma nova posse sem copiar dados privados do vendedor. Recuperação e limites operacionais estão em [Unidade, anúncio e recebimento](docs/collection-listing-lifecycle.md).

```text
src/
  Vaulta.Web.Api/                       HTTP, JWT, erros, OpenAPI, composição
  Vaulta.SharedKernel/                  AggregateRoot, eventos, IClock, Money
  Vaulta.App/                           App MAUI, telas, sessão e captura de fotos
  Vaulta.App.Core/                      Clientes HTTP e serviços testáveis sem MAUI
  Modules/Identity/
    Vaulta.Identity.Domain/             Aggregate, entidades, regras e eventos
    Vaulta.Identity.Application/        Commands, Queries, Handlers, validators, ports
    Vaulta.Identity.Infrastructure/     EF, migrations, tokens, hashing, Outbox
    Vaulta.Identity.Contracts/          Contratos HTTP e DTOs públicos
  Modules/Catalog/
    Vaulta.Catalog.Domain/               Identidade canônica de jogos, sets, cards e printings
    Vaulta.Catalog.Application/          Ports de ingestão e leitura
    Vaulta.Catalog.Infrastructure/       PostgreSQL, migrations, TCGdex e queries
    Vaulta.Catalog.Contracts/            DTOs de catálogo e provider
  Modules/Assets/
    Vaulta.Assets.Domain/                Regras de upload e metadados
    Vaulta.Assets.Application/           Ports de armazenamento e casos de uso
    Vaulta.Assets.Infrastructure/        PostgreSQL e adapter S3/MinIO
    Vaulta.Assets.Contracts/             Contratos de uploads
  Modules/Collection/                   Entries e unidades físicas com fotos
  Modules/Marketplace/                  Vendedores e anúncios
  Modules/Orders/                       Pedidos e reservas
  Modules/Payments/                     Cobranças e webhook Asaas
  Modules/Wallets/                      Preservação de carteiras/extratos históricos
  Modules/Shipping/                     Remessas e rastreio
  Modules/Reviews/                      Avaliações
tests/
  Vaulta.Identity.UnitTests/
  Vaulta.Identity.IntegrationTests/
  Vaulta.ArchitectureTests/
  Vaulta.App.Core.UnitTests/
  Vaulta.Commerce.UnitTests/
```

```text
Client
  ↓
Vaulta.Web.Api
  ↓
Application
  ↓
Domain
  ↓ (persistência orquestrada por ports da Application)
Infrastructure
  ↓
PostgreSQL

Command → Handler → Aggregate → DbContext
                                  ↓
                            Domain Events
                                  ↓
                               Outbox → Event Dispatcher
```

As setas mostram o fluxo de execução, não dependências de assemblies. Domain depende apenas de SharedKernel; Infrastructure implementa os ports da Application. [Decisões arquiteturais](docs/architecture.md).

## Executar com Docker

Pré-requisitos: Docker Desktop com containers Linux / Docker Engine e Compose v2. Para executar testes e migrations pelo host: SDK .NET 10.

Na raiz do repositório, gere `.env` com credenciais aleatórias locais:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Initialize-LocalEnvironment.ps1
docker compose up --build -d
```

Alternativamente, copie `.env.example` para `.env` e preencha `POSTGRES_PASSWORD`, `JWT_SECRET` e `MINIO_ROOT_PASSWORD`. Para gerar uma chave no Linux/macOS, use `openssl rand -base64 48`. O placeholder `change-me...` de JWT é recusado no startup. `.env` não é versionado nem incluído na imagem. O script preserva um `.env` existente.

- Swagger: http://localhost:8080/swagger
- OpenAPI: http://localhost:8080/swagger/v1/swagger.json
- Liveness: http://localhost:8080/health
- Readiness com PostgreSQL: http://localhost:8080/health/ready
- PostgreSQL local: `localhost:5432`; banco `vaulta`; usuário/senha em `.env`.

As portas podem ser alteradas por `API_PORT`, `POSTGRES_PORT`, `MINIO_API_PORT` e `MINIO_CONSOLE_PORT`. O Compose aguarda PostgreSQL e inicialização do MinIO; a API aplica migrations antes de começar a atender. MinIO usa bucket privado `vaulta-assets` e volume `vaulta-minio`, mantido entre reinicializações.

```powershell
docker compose ps
docker compose logs --tail 50 vaulta-api
docker compose restart
docker compose stop
docker compose down
# APAGA os dados locais permanentemente; somente para reset intencional:
docker compose down -v
```

## Fluxo da API

| Método | Rota | Autenticação | Resultado |
|---|---|---|---|
| POST | `/api/v1/auth/register` | Não | 201 + resumo do usuário |
| POST | `/api/v1/auth/login` | Não | 200 + JWT e refresh token |
| POST | `/api/v1/auth/refresh` | Refresh no body | 200 + tokens rotacionados |
| POST | `/api/v1/auth/logout` | Bearer + refresh no body | 204 |
| GET | `/api/v1/me` | Bearer | DTO privado + ETag |
| PATCH | `/api/v1/me/profile` | Bearer + If-Match | 204 + novo ETag |
| PATCH | `/api/v1/me/preferences` | Bearer + If-Match | 204 + novo ETag |
| PUT | `/api/v1/me/shipping-address` | Bearer | Atualiza endereço de entrega |
| POST | `/api/v1/me/change-password` | Bearer | 204, invalida todas as sessões |
| GET | `/api/v1/users/{username}` | Não | DTO público |
| GET | `/api/v1/catalog/search?q=...&game=pokemon&page=1&pageSize=20` | Não | Printings paginadas por nome |
| GET | `/api/v1/catalog/printings/{id}` | Não | Detalhe de printing e variants |
| POST | `/api/v1/assets/uploads` | Bearer | Emite URL S3 pré-assinada para imagem |
| POST | `/api/v1/assets/{assetId}/confirm` | Bearer | Confirma objeto enviado e valida tamanho/tipo/checksum |
| GET/POST | `/api/v1/me/collection`, `/api/v1/me/collection/items` | Bearer | Consulta entradas / adiciona unidades |
| POST | `/api/v1/scanner/identify?gameCode=pokemon` | Bearer | Identifica foto enviada como multipart |
| GET | `/api/v1/scanner/search`, `/api/v1/scanner/printings/{printingId}` | Bearer | Busca manual / imagem e valores em BRL |
| GET | `/api/v1/marketplace/listings`, `/api/v1/marketplace/listings/{listingId}` | Não | Vitrine / detalhe com URLs temporárias de fotos |
| POST | `/api/v1/me/seller`, `/api/v1/me/seller/listings` | Bearer | Habilita vendedor / cria anúncio |
| POST | `/api/v1/me/seller/listings/{listingId}/photos` | Bearer | Vincula imagem já confirmada do vendedor |
| GET/POST | `/api/v1/orders` | Bearer | Consulta / cria pedido |
| POST | `/api/v1/orders/{orderId}/deliver` | Bearer, comprador | Confirma recebimento |
| POST | `/api/v1/orders/{orderId}/cancel` | Bearer, comprador/vendedor | Antes do envio: 202 para reembolso de pago, 204 para cancelamento sem pagamento; depois do envio: atendimento |
| GET | `/api/v1/orders/{orderId}/refund` | Bearer, participante | Estado do reembolso; URL de preenchimento de boleto somente para comprador |
| POST | `/api/v1/orders/{orderId}/confirm-payment` | Bearer | 403; confirmação manual desabilitada |
| GET/PUT | `/api/v1/payments/customer` | Bearer | Cadastro privado do pagador; cliente Asaas vinculado pelo servidor |
| GET | `/api/v1/payments/orders/{orderId}` | Bearer, comprador | Retoma cobrança e consulta código Pix/validade |
| POST | `/api/v1/payments` | Bearer, comprador | Inicia/retoma a mesma cobrança; atualiza código Pix sem criar outra |
| POST | `/api/v1/webhooks/asaas` | `asaas-access-token` | Processa eventos autenticados do provedor |
| GET | `/api/v1/wallets`, `/api/v1/wallets/transactions` | Bearer | Dados históricos de carteira / extrato |
| POST | `/api/v1/wallets/withdraw` | Bearer | 410; saque descontinuado, sem débito |
| GET | `/api/v1/payouts` | Bearer, vendedor | Repasses por pedido em BRL |
| GET/PUT | `/api/v1/payouts/pix` | Bearer | Consulta / cadastro privado de chave Pix; cadastro não verifica identidade |
| GET/POST | `/api/v1/payouts/pix/reviews/{sellerId}` e `/verify` | Bearer, revisor configurado | Revisão de titularidade com evidência de identidade |
| POST/GET | `/api/v1/shipping`, `/api/v1/shipping/{shipmentId}` | Bearer, participante | Cria / consulta remessa |
| POST | `/api/v1/reviews` | Bearer, participante | Avalia pedido entregue |
| GET | `/api/v1/reviews/seller/{userId}/rating` | Bearer | Nota e quantidade de avaliações recebidas como vendedor |

Assets aceita `image/jpeg`, `image/png` e `image/webp` até 15 MB. Propósitos: `collection-item` (privado, usado também para fotos de anúncios) e `profile-avatar` (metadado público; o bucket continua privado). `LISTING_PHOTO` é recusado. O cliente envia PUT para a URL pré-assinada com o `Content-Type` declarado e depois chama confirm. A confirmação sempre lê o objeto e calcula SHA-256; quando um checksum foi informado, também compara o resultado. A URL de upload expira em 10 minutos. Fotos de itens e anúncios são lidas por URLs assinadas com duração de 5 minutos.

Para storage com endereço interno diferente daquele acessível ao cliente, configure `Assets:S3:PublicServiceUrl`; a API assina a URL usando esse endereço e conserva `ServiceUrl` para validar o objeto internamente. O protocolo da URL assinada acompanha o endpoint configurado; S3 AWS sem endpoint customizado usa HTTPS. No Compose, `ASSETS_PUBLIC_SERVICE_URL` tem padrão `http://localhost:9000` para clientes no host. Para Android Emulator, use `http://10.0.2.2:9000`; ajuste também a porta quando `MINIO_API_PORT` mudar. Um celular físico exige endereço de rede e publicação da porta acessíveis a ele; `localhost` e `minio` não apontam ao servidor nesse dispositivo. Não reescreva o host de uma URL já assinada.

Catalog usa IDs internos estáveis e `CatalogExternalId` para mapear TCGdex. O primeiro provider disponibiliza sincronização por interface de Application, ainda sem endpoint administrativo público; o jogo Pokémon é semeado por migration. Pesquisa e detalhes são públicos. Nomes/rarity/variants preservam valores de exibição e códigos normalizados, conforme ADR [001](docs/adr/001-canonical-catalog-identity.md). Imagens do provider ainda são referências externas; não há download/cache de artwork, pois uso depende de licença/termos.

Cadastro de exemplo (use uma senha própria):

```json
{
  "email": "collector@example.com",
  "password": "Example-Password123!",
  "username": "collector",
  "displayName": "Colecionador"
}
```

No Swagger, faça login e cole apenas o `accessToken` no botão **Authorize**. Consulte `/me`, copie o header `ETag` incluindo as aspas e envie-o no parâmetro `If-Match` dos PATCHes. Também é possível usar `version` do JSON envolvida em aspas. Depois de cada alteração, use o novo ETag. Versão antiga retorna 409; header ausente/inválido retorna 400.

PATCH é parcial: campos omitidos são preservados, `null` limpa os campos opcionais do perfil. `displayName` não pode ser vazio/nulo. Campos desconhecidos, incluindo `email`, `username` e `userId`, são recusados. Perfil público expõe apenas username, displayName, bio, avatarUrl e location; localização é pública quando preenchida.

```json
{ "bio": "Colecionador de Pokémon", "countryCode": "BR", "state": "SP", "city": "Araras" }
```

```json
{ "preferredCurrency": "BRL", "language": "pt-BR", "timeZone": "America/Sao_Paulo", "tcgInterests": ["POKEMON", "MAGIC"] }
```

Preferências suportam BRL/USD/EUR, pt-BR/en-US/es-ES, fusos IANA e POKEMON/MAGIC/YUGIOH/ONE_PIECE. `[]` limpa interesses. Username aceita 3–30 letras ASCII, dígitos ou `_`, case-insensitive. Bio: 500 caracteres; displayName: 100; avatar: URL HTTPS de até 2048 caracteres; país: ISO alpha-2 válido. Senhas: 12–128 caracteres com maiúscula, minúscula, dígito e símbolo.

Após trocar a senha, **faça login novamente**: JWT e refresh tokens anteriores deixam de funcionar. No logout, o refresh informado é revogado; o JWT já emitido pode continuar válido até expirar (15 minutos). Reutilizar refresh revogado invalida todas as sessões da conta. Clientes devem serializar refresh e substituir o token armazenado após cada rotação.

Erros usam ProblemDetails: 400 validação/domínio, 401 credenciais/sessão, 403 conta indisponível, 404 não encontrado, 409 conflito, 429 rate limit e 500 genérico. O header `X-Correlation-ID` também aparece nos erros.

## Desenvolvimento no host e migrations

O `dotnet run` não lê `.env` automaticamente. Defina `ConnectionStrings__Vaulta`, `Jwt__Secret`, `Jwt__Issuer` e `Jwt__Audience` no ambiente ou use user-secrets do projeto. Use os mesmos valores do banco local. `Database__ApplyMigrations=true` é uma conveniência de desenvolvimento; o padrão fora do Compose é não migrar automaticamente.

O comando `--seed-admin` exige `Admin__SeedPassword` configurado de forma segura para uma conta nova. A senha não tem padrão nem é impressa. Uma conta já existente não é alterada pelo seed; credenciais antigas precisam ser trocadas pelo fluxo de mudança de senha. JWT usa `Jwt:AccessTokenMinutes` (15) e `Jwt:RefreshTokenDays` (30).

```powershell
dotnet restore Vaulta.slnx
dotnet build Vaulta.slnx
dotnet tool restore
# ConnectionStrings__Vaulta deve apontar ao banco desejado:
dotnet ef database update --project src/Modules/Identity/Vaulta.Identity.Infrastructure
dotnet run --project src/Vaulta.Web.Api --launch-profile http
```

Swagger no host: http://localhost:5243/swagger. Para novas migrations:

```powershell
dotnet ef migrations add NomeDaMudanca --project src/Modules/Catalog/Vaulta.Catalog.Infrastructure --output-dir Persistence/Migrations
dotnet ef migrations has-pending-model-changes --project src/Modules/Catalog/Vaulta.Catalog.Infrastructure
dotnet ef migrations add NomeDaMudanca --project src/Modules/Assets/Vaulta.Assets.Infrastructure --output-dir Persistence/Migrations
dotnet ef migrations has-pending-model-changes --project src/Modules/Assets/Vaulta.Assets.Infrastructure
```

Também há modo exclusivo de migration: `dotnet run --project src/Vaulta.Web.Api -- --migrate` (requer configuração válida). Em produção, execute a migration como etapa de deploy antes da API, com credencial específica de DDL.

## Testes e smoke test

```powershell
dotnet test Vaulta.slnx
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Smoke-Test.ps1
```

Integração usa PostgreSQL 17 real, criado e removido pelo Testcontainers, com portas aleatórias e migrations. Docker deve estar funcionando. Os testes não usam EF InMemory e não acessam o volume do Compose. O smoke test cria um usuário de teste único no banco de desenvolvimento e testa todo o fluxo HTTP sem imprimir senhas ou tokens.

## Banco, eventos e operação

Schemas: `identity`, `catalog`, `collection`, `assets`, `marketplace`, `orders`, `payments`, `wallets`, `shipping` e `reviews`. Cada módulo mantém suas migrations. `identity.outbox_messages` é compartilhada pelos módulos que emitem eventos e excluída das migrations desses outros módulos. IDs são UUID e datas são UTC. Não são criadas carteiras nem créditos automáticos: o vendedor recebe por Pix verificado após confirmação do comprador e liquidação, descontados os 8% da Vaulta e as tarifas efetivas do Asaas. Cancelamento por comprador/vendedor antes do envio aguarda reembolso integral confirmado; depois do envio, atendimento. Execução financeira permanece desabilitada por padrão, sujeita à configuração e validação operacional. A implantação e a conciliação estão documentadas em [Repasse direto](docs/seller-payouts.md). Dados históricos de Wallets são preservados.

```powershell
docker compose exec postgres sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "SELECT id, type, processed_at, retry_count, error FROM identity.outbox_messages ORDER BY occurred_at DESC LIMIT 20;"'
```

Outbox persiste na mesma transação dos dados. O dispatcher roda a cada 2 segundos, com lotes de 20 e `FOR UPDATE SKIP LOCKED`. Falhas ficam registradas e são tentadas no máximo 10 vezes. Mensagens processadas permanecem no banco. Após corrigir um consumer, reprocessamento operacional pode zerar `retry_count` e `error` da mensagem afetada. A entrega é pelo menos uma vez; consumers futuros devem ser idempotentes e usar Inbox com chave `(message_id, consumer)`. O bus atual executa consumers em memória e registra ID/tipo; não há broker externo nesta entrega.

Logs JSON incluem CorrelationId e não incluem bodies, tokens ou senhas. OpenTelemetry coleta instrumentação ASP.NET Core para traces/métricas; exportação para um collector fica para a configuração do ambiente. CORS está fechado por padrão; configure `Cors__Origins__0` etc. Auth tem limite básico por IP (20/minuto).

Compose é para desenvolvimento e publica portas somente no loopback. Produção exige TLS, segredo aleatório gerenciado, `ASPNETCORE_ENVIRONMENT=Production` e configuração de ingress. HSTS e redirecionamento HTTPS estão ativos fora de Development/Testing. Se TLS terminar em proxy, configure forwarded headers **somente de proxies confiáveis** antes do redirecionamento; não confie indiscriminadamente em headers enviados pelo cliente. Swagger fica habilitado somente em Development.

## Aplicativo mobile — Vaulta.App

O app MAUI está em `src/Vaulta.App`. O target padrão é Android (`net10.0-android`); iOS é habilitado explicitamente em um Mac com `BuildIos=true`. O projeto usa MVVM com CommunityToolkit.Mvvm e consome apenas os assemblies de contratos HTTP (Identity, Catalog, Collection, Assets), não a infraestrutura do servidor.

`src/Vaulta.App.Core` é uma class library .NET puro (sem dependência de MAUI) que concentra a fundação HTTP reutilizável pelo app: `AuthorizingHttpMessageHandler` (Bearer token, retry único em 401 com refresh single-flight), `ApiErrorTranslator`/`ApiException`, e os clients `CatalogClient`, `CollectionClient` e `AssetClient`. Ela é coberta por `tests/Vaulta.App.Core.UnitTests` (client parsing, paginação, idempotency key, mapping de condition, fluxo de refresh/retry) usando `HttpMessageHandler` fake, sem chamadas de rede reais. Auth real (login/registro/refresh/logout com restauração de sessão), busca de Catálogo (com debounce e cancelamento), detalhe de Printing/Variant, adicionar/editar/remover item da Coleção (com `Idempotency-Key` e controle de concorrência otimista) já funcionam fim a fim contra o backend — o Scanner agora tem captura autenticada, reconhecimento por OCR e detalhes com imagem e preços convertidos para BRL (veja docs/scanner.md). Pricing e Portfolio fora do scanner ainda usam dados demonstrativos.

```powershell
dotnet restore Vaulta.slnx
dotnet build Vaulta.slnx
dotnet build src/Vaulta.App/Vaulta.App.csproj --framework net10.0-android
```

Para executar no Android Emulator pelo Visual Studio, defina `Vaulta.App` como projeto de inicialização, selecione um emulador Android e pressione F5. Também é possível usar `dotnet build src/Vaulta.App/Vaulta.App.csproj --framework net10.0-android -t:Run` com um emulador iniciado e disponível no ADB.

Para compilar iOS em Mac com o workload iOS instalado:

```powershell
dotnet build src/Vaulta.App/Vaulta.App.csproj -p:BuildIos=true --framework net10.0-ios
```

### Backend local

A configuração Debug está em `src/Vaulta.App/Configuration/appsettings.Development.json` e usa `http://10.0.2.2:8080/`, endereço do host visto pelo Android Emulator. O app não fixa URL em services. A política Android de cleartext permite apenas o domínio do emulador `10.0.2.2`. Inicie API e PostgreSQL conforme a seção [Executar com Docker](#executar-com-docker). Em uma execução no emulador, esse encaminhamento disponibiliza a API pela porta 8080.

Produção aponta para `https://api.vaulta.com.br/`, já configurado em `src/Vaulta.App/Configuration/appsettings.Production.json`. O client tipado recusa uma `Api:BaseUrl` ausente/inválida (não-absoluta ou sem esquema http/https) quando for resolvido, para qualquer ambiente.

Tokens de sessão usam `SecureStorage`; não use Preferences para credenciais. A renovação é exposta pela interface de autenticação e serializada para evitar refresh simultâneo. Todo request autenticado (Catalog/Collection/Assets) passa por `AuthorizingHttpMessageHandler`: em um 401 ele dispara um refresh forçado (ignorando a janela de validade otimista do token) e repete a requisição original exatamente uma vez; refreshes concorrentes reaproveitam a mesma chamada em andamento; se o refresh falhar, a sessão local é limpa e o usuário volta para a tela de login — nunca há laço de retry.

### Galeria do design system

Em Debug, abra o app e toque em **Foundations gallery** na página inicial. A galeria não é registrada em Release nem participa da navegação de produção. O mapeamento completo Figma→MAUI está em [`docs/design/design-system.md`](docs/design/design-system.md). Os arquivos oficiais de fonte Inter ainda precisam ser adicionados a `src/Vaulta.App/Resources/Fonts`; não são baixados pelo build.

## Catalog: TCGdex e consumo pelo MAUI

O adapter consulta `/{language}/sets`, `sets/{id}` (CardBrief) e `cards/{id}` (CardDetails).
Rarity e variants vêm do detalhe; o resumo não é tratado como carta completa.
Os detalhes são atualizados em cada sync, com no máximo quatro requisições simultâneas por padrão.
Configuração: `Catalog:Providers:TcgDex` em appsettings/ambiente, com `BaseAddress` (raiz `/v2/`),
`Language` (`en` por padrão; português é `pt`), `Timeout` (segundos por tentativa), `MaxConcurrency` (1–8),
`RetryCount` (0–5) e `MaxRetryDelaySeconds`. Há retries finitos para 408/429/5xx, rede e timeout;
JSON incompatível e 404 falham sem retry. Retry-After é respeitado; quando excede o orçamento configurado,
a execução falha como transitória em vez de tentar antes do prazo. Cancelamento do chamador é propagado.

Artwork é `Printing.ExternalArtworkUrl` + `ArtworkProvider`, nunca `Card.ImageAssetKey` nem foto do usuário.
Uma URL base de carta TCGdex vira `/high.png`, conforme o contrato de assets do provider.
Não ocorre download, cache, upload para S3/MinIO ou criação de Asset. A URL pode ser nula quando a fonte não possui imagem.

### Contratos públicos (mudança incompatível de v1 durante desenvolvimento)

`GET /api/v1/catalog/search?q=pikachu&game=pokemon&page=1&pageSize=20` agora retorna um objeto:

```json
{
  "items": [{
    "printingId": "<guid>", "gameCode": "pokemon", "setId": "<guid>",
    "setName": "Base Set", "cardName": "Pikachu", "collectorNumber": "58",
    "language": "en", "rarity": "common",
    "artworkUrl": "https://assets.tcgdex.net/en/base/base1/58/high.png"
  }],
  "page": 1, "pageSize": 20, "totalCount": 1
}
```

O total usa `COUNT` filtrado sem carregar entidades; os itens têm ordenação estável e projeção SQL.
Os índices existentes cobrem joins, identidade e filtros por jogo; B-tree não acelera `contains`,
por isso não se adicionou índice redundante nem trigram sem medição. Busca avançada permanece fora do escopo.

`GET /api/v1/catalog/printings/{id}` retorna `printingId`, `cardId`, `setId`, `gameCode`, `setName`,
`cardName`, `collectorNumber`, `language`, `rarity`, `artworkUrl` e `variants: [{ id, code, name }]`.
Somente variants ativas aparecem para seleção. Códigos mantêm o padrão canônico existente em minúsculas
(`normal`, `reverse`, `holo`, `first-edition`); flags falsas não geram variants. Treatments novos são normalizados
sem enum fechado, preservando `RawValue`. Variants removidas da fonte ficam inativas e conservam seus GUIDs;
se reaparecerem, o mesmo ID é reativado. Collection continua resolvendo referências históricas, inclusive inativas.

### Printing lifecycle (IsActive)

`Printing` também possui `IsActive` (default `true`), com o mesmo comportamento de `Variant`: nunca é deletada
fisicamente. Ao final de um sync de set concluído com sucesso, o serviço compara as Printings retornadas pelo
provider com as já conhecidas para aquele Set:

- Presente no resultado atual → `IsActive = true` (ativa ou reativada, preservando o mesmo `PrintingId`).
- Conhecida anteriormente para o Set mas ausente do resultado atual → `IsActive = false`.

Essa comparação só acontece depois que todo o lote do set foi persistido com sucesso (mesma transação do sync).
Timeout, HTTP 429/500, JSON inválido, cancelamento ou falha de persistência interrompem o processo antes disso;
nenhuma Printing é desativada em um sync que falhou parcial ou totalmente.

Catálogo público × leitura histórica:

- `GET /api/v1/catalog/search` e `GET /api/v1/catalog/printings/{id}` só retornam/expõem Printings com
  `IsActive == true`. Uma Printing inativa responde `404` no detalhe público e não aparece na busca.
- O reader interno usado por Collection (`ICatalogCollectionReader`) continua resolvendo Printings ativas e
  inativas, preservando a leitura de itens de coleção antigos mesmo que a Printing tenha saído do catálogo público.
- `POST /api/v1/me/collection/items` rejeita com `409 Conflict` a adição de **novos** itens referenciando uma
  Printing inativa; a leitura de entradas/itens já existentes não é afetada por essa regra.

O app usa diretamente os GUIDs retornados:

```http
POST /api/v1/me/collection/items
Authorization: Bearer <token>
Content-Type: application/json

{"printingId":"<guid>","variantId":"<guid>","quantity":2,"condition":"NEAR_MINT"}
```

Resultado esperado: HTTP 201, uma CollectionEntry e dois CollectibleItems.
Clientes antigos precisam trocar o array de busca por `response.items` e as strings de variants por objetos.
OpenAPI acompanha os DTOs; o app MAUI consome esses mesmos contratos via `Vaulta.App.Core.Catalog.CatalogClient` (busca com paginação e detalhe de Printing/Variants), sem duplicar DTOs.
Os modelos de ingestão `Provider*` foram movidos de Contracts para Application; somente adapters e consumidores internos usam esses tipos.

### Operação segura do sync

Não há endpoint administrativo HTTP novo nem sistema de roles improvisado. Execute o comando no processo/container
com acesso operacional ao banco e à configuração da API; não é um job durável nem prende uma requisição HTTP.
Em Compose de desenvolvimento, as migrations são aplicadas no startup:

```sh
docker compose up --build -d
docker compose exec -T vaulta-api dotnet Vaulta.Web.Api.dll --catalog-sync tcgdex base1
docker compose exec -T vaulta-api dotnet Vaulta.Web.Api.dll --catalog-sync-runs
docker compose exec -T vaulta-api dotnet Vaulta.Web.Api.dll --catalog-sync-run <run-guid>
python3 scripts/smoke-catalog.py http://127.0.0.1:8080
```

Ou, com a configuração de banco no ambiente local:

```sh
dotnet run --project src/Vaulta.Web.Api -- --catalog-sync tcgdex base1
```

`--catalog-sync tcgdex all` é suportado, mas prefira um set no primeiro smoke. Cada execução registra provider,
scope, contadores, erro e timestamps; logs incluem SyncRunId/Provider/Scope/SetId/ErrorType.
O comando retorna código 1 em falha e 130 em cancelamento por Ctrl+C; o histórico lista as últimas 50 execuções.
Em outro processo, consulte `--catalog-sync-runs` durante uma execução longa. Não existe fila em memória nem tarefa
fire-and-forget: encerrar abruptamente o processo pode deixar uma execução como `running`; inspecione-a antes de repetir.
O advisory lock é liberado pela sessão PostgreSQL e protege o provider inteiro, inclusive sobreposição `all`/set.
Cada set é transacional: dados de sets anteriores permanecem, e um set incompleto é revertido antes de registrar a falha.
`RecordsCreated`/`RecordsUpdated` contam Set/Card/Printing/Variant; `RecordsUnresolved` conta novos Cards sem chave autoritativa
para consolidação entre printings. Nenhum merge por nome é feito. External IDs usam sempre o código do provider selecionado.

Migration: `20260926045142_ExternalArtworkAndVariantAvailability`; adições nullable de artwork e `is_active` com default true.
Não há backfill de imagens inventadas: reexecute o sync para preencher os artworks. Migrations anteriores permanecem intactas.

## Collection: hardening final (já integrado ao MAUI)

`CollectionEntry` (agrupamento por `UserId + PrintingId + VariantId`) e `CollectibleItem` (unidade física
individual) não mudaram de forma nesta rodada; os gaps abaixo foram corrigidos sem reescrever o modelo.

### Conditions canônicas

`CollectionRules.Condition` só persiste um destes sete códigos: `MINT`, `NEAR_MINT`, `LIGHTLY_PLAYED`,
`MODERATELY_PLAYED`, `HEAVILY_PLAYED`, `DAMAGED`, `UNKNOWN`. Texto livre nunca mais vira condição distinta.
Aliases de entrada aceitos (case/hífen/espaço insensíveis) são normalizados para o código canônico antes de
qualquer persistência:

| Alias de entrada | Código canônico |
|---|---|
| `M`, `MINT` | `MINT` |
| `NM`, `NEAR MINT`, `NEAR-MINT`, `NEAR_MINT` | `NEAR_MINT` |
| `LP`, `LIGHTLY PLAYED`, `LIGHTLY-PLAYED` | `LIGHTLY_PLAYED` |
| `MP`, `MODERATELY PLAYED` | `MODERATELY_PLAYED` |
| `HP`, `HEAVILY PLAYED` | `HEAVILY_PLAYED` |
| `DMG`, `DAMAGED` | `DAMAGED` |
| `UNKNOWN`, `UNSPECIFIED` | `UNKNOWN` |

Qualquer outro valor (`PERFECT`, `EXCELENTE`, `SUPER_BONITA`, vazio) é rejeitado com `400` (`DomainException`
→ `ProblemDetails`). A regra vive só em `CollectionRules`; handlers e endpoints não replicam `if condition == ...`.

### `sort=name` agora é uma ordenação global

Antes, `GET /api/v1/me/collection?sort=name` ordenava `CollectionEntry` por `PrintingId`, paginava no
PostgreSQL e só então ordenava por nome a página já reduzida — o resultado era correto apenas dentro de
cada página isolada. Agora, quando `sort=name`, a query materializa as identidades (`EntryId`, `PrintingId`,
`VariantId`) de todas as entries filtradas, resolve `CardName`/`SetName`/`CollectorNumber`/`VariantCode` via
`ICatalogCollectionReader` uma única vez, ordena a coleção inteira com tie-breaker estável
(`CardName, SetName, CollectorNumber, VariantCode, CollectionEntryId`) e só então aplica `Skip`/`Take`. Os
demais sorts (`recent`: `UpdatedAt DESC, EntryId`; `quantity`: `Count DESC, EntryId`) já eram calculados no
banco antes da paginação e não precisaram mudar. Collection não ganhou FK cruzando o schema `catalog`; o
boundary entre módulos continua por porta (`ICollectionCatalog`/`ICatalogCollectionReader`).

### Idempotência de `POST /api/v1/me/collection/items`

O header `Idempotency-Key` é **obrigatório** nesse endpoint; sua ausência responde `400`. A chave é escopada
por `(UserId, Operation, IdempotencyKey)` em uma tabela nova, `collection.idempotency_keys`
(migration `20260926201932_AddCollectionIdempotencyKeys`), com índice único
`(user_id, operation, idempotency_key)`. Fluxo:

- O `pg_advisory_xact_lock` existente (identidade `UserId+PrintingId+VariantId`) é adquirido **antes** da
  checagem de idempotência, então retries verdadeiramente concorrentes com a mesma chave são serializados
  pelo mesmo lock que já protege a criação de `CollectionEntry`/`CollectibleItem`.
- Se a chave já existe com o mesmo hash de request (SHA-256 do payload serializado): nenhuma nova
  `CollectionEntry`/`CollectibleItem`/evento de Outbox é criada; a resposta original (serializada em
  `response_payload`) é devolvida sem reexecutar o domínio.
- Se a chave já existe com um hash de request diferente: `409 Conflict` (nunca aceita silenciosamente um
  payload diferente para a mesma chave).
- Se a chave não existe: o fluxo normal roda e a linha de idempotência é gravada na **mesma transação/mesmo
  `SaveChangesAsync`** que persiste a Entry, os Items e as mensagens de Outbox — não há como a operação ficar
  "meio concluída".
- Uma corrida residual entre duas requisições que colidem na mesma chave antes do lock (rara, dado que o
  lock cobre a identidade de destino) é resolvida pela unique constraint da tabela: a segunda
  `SaveChangesAsync` falha com violação de unicidade e é convertida em `409`, pedindo retry.

Chaves diferentes para o mesmo payload são tratadas como intenções distintas (ex.: dois cliques reais de
"Adicionar" no MAUI) e criam itens adicionais normalmente — idempotência nunca deduplica por conteúdo, só
por chave.

O app MAUI já integra este endpoint; a regra de geração de chave é: gerar
um GUID novo por intenção lógica de "Adicionar à coleção" e reenviar o mesmo GUID em qualquer retry
automático daquela mesma intenção (timeout, perda de resposta, etc.). Uma nova ação do usuário — mesmo que
para o mesmo card — deve gerar um GUID novo. A geração da chave é responsabilidade do cliente; a API nunca
gera uma chave automática em nome do cliente, pois isso anularia a proteção contra retries.

### Printing/Variant inativa: nova inclusão bloqueada, histórico preservado

`CollectionVariantDetails` (porta interna `ICatalogCollectionReader`) agora também expõe `IsActive`, igual
`CollectionPrintingDetails` já expunha. `POST /api/v1/me/collection/items` valida ambos antes de criar
qualquer coisa: `Printing.IsActive == false` **ou** `Variant.IsActive == false` (quando informada) →
`409 Conflict`, sem consultar o lock nem tocar o banco de Collection. Leituras de entries/itens já existentes
(`GET /entries/{id}`, `GET /items/{id}`, listagem) nunca aplicam esse filtro — `ICatalogCollectionReader`
continua resolvendo identidades ativas e inativas, preservando artwork/nome/rarity de cópias físicas
antigas mesmo depois que o Catalog parou de oferecer aquela Printing ou Variant.

### Testes adicionados

- `tests/Vaulta.Identity.UnitTests/CollectionDomainTests.cs`: aliases de condition (`NM`, `Near Mint`,
  `near-mint`, `LP`, `MP`, `HP`, `DMG`, `unspecified`, …) e rejeição de códigos desconhecidos/vazios.
- `tests/Vaulta.Identity.IntegrationTests/CollectionApiTests.cs` (novo): 401 sem Bearer, quantidade cria N
  `CollectibleItem` com IDs/versions distintos, reuso de Entry na mesma identidade, Entries separadas por
  Variant/usuário, Printing/Variant inexistente (`404`), Variant de outra Printing (`400`), update com
  ETag/Version e conflito otimista (`409`), soft delete, summary, filtros (`query`/`condition`/`variantId`),
  paginação inválida (`400`), ownership (`404` para outro usuário), concorrência real com chaves diferentes
  (`pg_advisory_xact_lock`), retry idempotente com mesma chave/mesmo payload, `409` para mesma
  chave/payload diferente, e `sort=name` com 5 cartas em 3 páginas provando ordenação global.
- `tests/Vaulta.Identity.IntegrationTests/CatalogCollectionFlowTests.cs`: novo teste
  `VariantLifecycleProtectsNewCollectionAdditionsWhileKeepingHistoricalItemsReadable`, análogo ao já
  existente para Printing, cobrindo especificamente Variant inativa com Printing ainda ativa.

Validação do backend sem workloads móveis:

```sh
dotnet test tests/Vaulta.Identity.UnitTests/Vaulta.Identity.UnitTests.csproj
dotnet test tests/Vaulta.Identity.IntegrationTests/Vaulta.Identity.IntegrationTests.csproj
dotnet test tests/Vaulta.ArchitectureTests/Vaulta.ArchitectureTests.csproj
dotnet tool restore
dotnet ef migrations has-pending-model-changes --project src/Modules/Catalog/Vaulta.Catalog.Infrastructure
```

Integração usa PostgreSQL 17 real via Testcontainers e requer Docker. A solution inteira também contém MAUI Android,
portanto `dotnet restore/build/test Vaulta.slnx` exige workloads móveis e Android SDK. O workflow `Catalog validation`
valida backend, testes e snapshot; na branch de implementação também executa Compose e smoke real de Base Set.
O smoke cria uma conta descartável no ambiente informado e grava `catalog-smoke-result.json`, sem credenciais.

## AWS Production / MVP

O Compose local com MinIO permanece inalterado. Para EC2 + PostgreSQL Docker + S3 privado, use somente `docker-compose.production.yml`. Consulte [operação AWS](docs/aws-production.md) para configuração, deploy por tag, migration separada, Nginx/TLS, backup/restore e rollback.
