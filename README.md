# Vaulta

Fundação do backend brasileiro para colecionadores de TCGs. Esta entrega implementa somente **Identity / Users / Profile**, em .NET 10, ASP.NET Core, EF Core e PostgreSQL.

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

As portas podem ser alteradas por `API_PORT` e `POSTGRES_PORT`. O Compose aguarda o healthcheck do banco; a API aplica migrations reais antes de começar a atender. O volume `vaulta-postgres` mantém os dados entre reinicializações.

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
dotnet ef migrations add NomeDaMudanca --project src/Modules/Identity/Vaulta.Identity.Infrastructure --output-dir Persistence/Migrations
dotnet ef migrations has-pending-model-changes --project src/Modules/Identity/Vaulta.Identity.Infrastructure
```

Também há modo exclusivo de migration: `dotnet run --project src/Vaulta.Web.Api -- --migrate` (requer configuração válida). Em produção, execute a migration como etapa de deploy antes da API, com credencial específica de DDL.

## Testes e smoke test

```powershell
dotnet test Vaulta.slnx
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Smoke-Test.ps1
```

Integração usa PostgreSQL 17 real, criado e removido pelo Testcontainers, com portas aleatórias e migrations. Docker deve estar funcionando. Os testes não usam EF InMemory e não acessam o volume do Compose. O smoke test cria um usuário de teste único no banco de desenvolvimento e testa todo o fluxo HTTP sem imprimir senhas ou tokens.

## Banco, eventos e operação

Schema `identity`: `users`, `user_profiles`, `user_preferences`, `user_tcg_interests`, `refresh_tokens`, `outbox_messages`. IDs UUID, datas TIMESTAMPTZ/UTC, payload JSONB, unicidade de email/username normalizados e PK composta para interesses. `version` e `security_stamp` são campos adicionais do usuário para concorrência e revogação de sessões.

```powershell
docker compose exec postgres sh -c 'psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c "SELECT id, type, processed_at, retry_count, error FROM identity.outbox_messages ORDER BY occurred_at DESC LIMIT 20;"'
```

Outbox persiste na mesma transação dos dados. O dispatcher roda a cada 2 segundos, com lotes de 20 e `FOR UPDATE SKIP LOCKED`. Falhas ficam registradas e são tentadas no máximo 10 vezes. Mensagens processadas permanecem no banco. Após corrigir um consumer, reprocessamento operacional pode zerar `retry_count` e `error` da mensagem afetada. A entrega é pelo menos uma vez; consumers futuros devem ser idempotentes e usar Inbox com chave `(message_id, consumer)`. O bus atual executa consumers em memória e registra ID/tipo; não há broker externo nesta entrega.

Logs JSON incluem CorrelationId e não incluem bodies, tokens ou senhas. OpenTelemetry coleta instrumentação ASP.NET Core para traces/métricas; exportação para um collector fica para a configuração do ambiente. CORS está fechado por padrão; configure `Cors__Origins__0` etc. Auth tem limite básico por IP (20/minuto).

Compose é para desenvolvimento e publica portas somente no loopback. Produção exige TLS, segredo aleatório gerenciado, `ASPNETCORE_ENVIRONMENT=Production` e configuração de ingress. HSTS e redirecionamento HTTPS estão ativos fora de Development/Testing. Se TLS terminar em proxy, configure forwarded headers **somente de proxies confiáveis** antes do redirecionamento; não confie indiscriminadamente em headers enviados pelo cliente. Swagger fica habilitado somente em Development.
