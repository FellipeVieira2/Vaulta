# Vaulta

Backend brasileiro para colecionadores de TCGs em .NET 10, ASP.NET Core, EF Core e PostgreSQL. Os bounded contexts implementados nesta entrega são **Identity / Users / Profile**, **Catalog** e uma fundação de **Assets** para upload S3 compatível.

## Arquitetura e estrutura

Monólito modular com DDD e CQRS seletivo: comandos usam o aggregate `User`; queries projetam DTOs diretamente no PostgreSQL, sem tracking. Um banco, um processo de API, sem MediatR e sem generic repository. O projeto original **Vaulta.Web.Api** foi preservado e movido para `src`.

```text
src/
  Vaulta.Web.Api/                       HTTP, JWT, erros, OpenAPI, composição
  Vaulta.SharedKernel/                  AggregateRoot, eventos, IClock, Money
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
tests/
  Vaulta.Identity.UnitTests/
  Vaulta.Identity.IntegrationTests/
  Vaulta.ArchitectureTests/
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

Alternativamente, copie `.env.example` para `.env` e substitua `POSTGRES_PASSWORD` e `JWT_SECRET`. Para gerar uma chave no Linux/macOS, use `openssl rand -base64 48`. O placeholder `change-me...` de JWT é recusado no startup. `.env` não é versionado nem incluído na imagem. O script preserva um `.env` existente.

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
| POST | `/api/v1/me/change-password` | Bearer | 204, invalida todas as sessões |
| GET | `/api/v1/users/{username}` | Não | DTO público |
| GET | `/api/v1/catalog/search?q=...&game=pokemon&page=1&pageSize=20` | Não | Printings paginadas por nome |
| GET | `/api/v1/catalog/printings/{id}` | Não | Detalhe de printing e variants |
| POST | `/api/v1/assets/uploads` | Bearer | Emite URL S3 pré-assinada para imagem |
| POST | `/api/v1/assets/{assetId}/confirm` | Bearer | Confirma objeto enviado e valida tamanho/tipo/checksum |

Assets aceita `image/jpeg`, `image/png` e `image/webp` até 15 MB. Propósitos iniciais: `collection-item` (privado) e `profile-avatar` (metadado público; o bucket continua privado). O cliente envia PUT para a URL pré-assinada com o `Content-Type` declarado e depois chama confirm. A confirmação calcula SHA-256 lendo o objeto quando um checksum foi informado. A URL de upload expira em 10 minutos. A API não fornece URL pública de leitura nesta entrega.

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

Schemas: `identity` mantém users, profiles, tokens e a única tabela `outbox_messages`; `catalog` contém games/series/sets/cards/printings/variants, external IDs e sync runs; `assets` contém metadados e lifecycle de uploads. Todos usam UUID e datas UTC. Catalog mapeia Outbox para a tabela física Identity excluindo-a de suas próprias migrations; o dispatcher atual continua único.

```powershell
docker compose exec postgres sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "SELECT id, type, processed_at, retry_count, error FROM identity.outbox_messages ORDER BY occurred_at DESC LIMIT 20;"'
```

Outbox persiste na mesma transação dos dados. O dispatcher roda a cada 2 segundos, com lotes de 20 e `FOR UPDATE SKIP LOCKED`. Falhas ficam registradas e são tentadas no máximo 10 vezes. Mensagens processadas permanecem no banco. Após corrigir um consumer, reprocessamento operacional pode zerar `retry_count` e `error` da mensagem afetada. A entrega é pelo menos uma vez; consumers futuros devem ser idempotentes e usar Inbox com chave `(message_id, consumer)`. O bus atual executa consumers em memória e registra ID/tipo; não há broker externo nesta entrega.

Logs JSON incluem CorrelationId e não incluem bodies, tokens ou senhas. OpenTelemetry coleta instrumentação ASP.NET Core para traces/métricas; exportação para um collector fica para a configuração do ambiente. CORS está fechado por padrão; configure `Cors__Origins__0` etc. Auth tem limite básico por IP (20/minuto).

Compose é para desenvolvimento e publica portas somente no loopback. Produção exige TLS, segredo aleatório gerenciado, `ASPNETCORE_ENVIRONMENT=Production` e configuração de ingress. HSTS e redirecionamento HTTPS estão ativos fora de Development/Testing. Se TLS terminar em proxy, configure forwarded headers **somente de proxies confiáveis** antes do redirecionamento; não confie indiscriminadamente em headers enviados pelo cliente. Swagger fica habilitado somente em Development.

## Aplicativo mobile — Vaulta.App

O app MAUI está em `src/Vaulta.App`. O target padrão é Android (`net10.0-android`); iOS é habilitado explicitamente em um Mac com `BuildIos=true`. O projeto usa MVVM com CommunityToolkit.Mvvm e consome apenas o assembly de contratos HTTP Identity, não a infraestrutura do servidor.

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

Antes de gerar uma versão Release, configure `Api:BaseUrl` em `src/Vaulta.App/Configuration/appsettings.Production.json` para o endpoint HTTPS do ambiente. O valor está vazio intencionalmente; o client tipado recusa uma URL ausente/inválida quando for resolvido.

Tokens de sessão usam `SecureStorage`; não use Preferences para credenciais. A renovação é exposta pela interface de autenticação e serializada para evitar refresh simultâneo. O retry automático de requests após 401 ainda não foi implementado.

### Galeria do design system

Em Debug, abra o app e toque em **Foundations gallery** na página inicial. A galeria não é registrada em Release nem participa da navegação de produção. O mapeamento completo Figma→MAUI está em [`docs/design/design-system.md`](docs/design/design-system.md). Os arquivos oficiais de fonte Inter ainda precisam ser adicionados a `src/Vaulta.App/Resources/Fonts`; não são baixados pelo build.
