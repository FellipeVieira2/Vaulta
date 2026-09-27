# Entrega e validação — preparação AWS

Base analisada: `2705a1ac6958f7a2e85d110f2ddf8041d249b2a8`, branch `master`, repositório `FellipeVieira2/Vaulta`.
Branch de trabalho local: `feat/aws-production`.

**Implementação preparada; aceite completo de produção pendente.** Nenhuma mudança ou chamada de operação foi feita na AWS. O push Git não tinha credencial disponível. O plugin GitHub retornou `403 Resource not accessible by integration` ao criar branch e PR, portanto nenhum código/PR foi publicado e o workflow novo não foi executado no GitHub.

## 1. Alterações

- S3 usa BasicAWSCredentials com o par de chaves local ou o construtor SDK sem credenciais para a default chain. Par incompleto falha; nenhum segredo real foi adicionado.
- Validação antecipada de Production antes de migrations/sync, mantendo a validação JWT existente, Outbox e rate limit.
- Forwarded headers de um proxy explícito, sem confiar em toda a rede; health local HTTP e HTTPS nos endpoints de negócio.
- Compose Production separado: PostgreSQL 17 em volume, banco sem portas públicas, API somente loopback, limites de memória/conexões e rotação de logs.
- Deploy idempotente por tag com IAM Role, pull, banco healthy, migration isolada e troca da API somente depois do sucesso.
- Backup privado pg_dump/gzip/S3, timer systemd opcional, restore e rollback documentados.
- Testes e CI para validação offline de credenciais, comportamento de proxy, configuração, falhas de deploy/backup, fluxo de Assets com MinIO real e smoke Docker Production.
- Smoke Catalog corrigido para enviar o Idempotency-Key exigido pelo contrato atual.

O Modular Monolith, .NET 10, migrations, contratos de Identity/Catalog/Collection/Assets e Dockerfile foram preservados. `docker-compose.yml` não foi alterado. O script local agora gera também a senha aleatória do MinIO; `.env.example` contém campos secretos vazios.

## 2. Arquivos criados

- `.env.production.example`
- `docker-compose.production.yml`
- `docs/aws-production.md`
- `docs/aws-production-validation.md`
- `deploy/nginx/vaulta.conf.example`
- `deploy/systemd/vaulta-postgres-backup.service`
- `deploy/systemd/vaulta-postgres-backup.timer`
- `scripts/deploy-production.sh`
- `scripts/backup-postgres.sh`
- `scripts/production-compose.sh`
- `scripts/lib/production.sh`
- `scripts/validate-production-config.py`
- `scripts/smoke-production-container.sh`
- `scripts/smoke-assets.py`
- `src/Modules/Assets/Vaulta.Assets.Infrastructure/AssemblyInfo.cs`
- `src/Modules/Assets/Vaulta.Assets.Infrastructure/S3ClientFactory.cs`
- `src/Vaulta.Web.Api/ProductionConfiguration.cs`
- `src/Vaulta.Web.Api/ReverseProxyConfiguration.cs`
- `src/Vaulta.Web.Api/appsettings.Production.json`
- `tests/Vaulta.Identity.UnitTests/S3ClientFactoryTests.cs`
- `tests/Vaulta.Identity.IntegrationTests/ProductionConfigurationTests.cs`
- `tests/Vaulta.Identity.IntegrationTests/ReverseProxyTests.cs`
- `tests/Vaulta.Identity.IntegrationTests/ProductionApiTests.cs`
- `tests/Vaulta.Identity.IntegrationTests/AssetStorageFlowTests.cs`
- `tests/operations/test_production_scripts.py`

## 3. Arquivos modificados

- `.dockerignore`, `.gitignore`, `.env.example`, `README.md`
- `.github/workflows/catalog-validation.yml`
- `scripts/Initialize-LocalEnvironment.ps1`, `scripts/smoke-catalog.py`
- `src/Modules/Assets/Vaulta.Assets.Infrastructure/DependencyInjection.cs`
- `src/Modules/Assets/Vaulta.Assets.Infrastructure/S3ObjectStorage.cs`
- `src/Vaulta.Web.Api/Program.cs`
- `tests/Vaulta.Identity.UnitTests/Vaulta.Identity.UnitTests.csproj`

## 4–6. Variáveis, secrets e infraestrutura

O quadro completo de variáveis está em [aws-production.md](aws-production.md#configuração-e-secrets).

Secretas: `POSTGRES_PASSWORD`, `JWT_SECRET`. A connection string efetiva também é secreta porque contém a senha.
AWS: bucket `vaulta-assets-142767402064-us-east-1`, região `us-east-1`, ECR `142767402064.dkr.ecr.us-east-1.amazonaws.com/vaulta-api`. Nenhum valor de credencial AWS deve ser configurado no container.
Não secretas: database/username, issuer/audience, subnet/gateway, origins CORS, rate limit e tag de imagem.
Fixadas pelo compose: Production, porta HTTP 8080, migrations=false, ForcePathStyle=false, Outbox=true e IMDSv1 desativado.

## 7–9. Testes e resultados reais

| Validação | Resultado |
|---|---|
| Build backend + projeto de integração, SDK 10.0.401, single-process MSBuild | PASS, 0 warnings / 0 errors |
| Unit tests (incluindo seleção S3 e presign temporário) | PASS 85/85 |
| Configuração Production + TestServer de proxy/rate limit | PASS 32/32 |
| Architecture tests | PASS 6/6 |
| Scripts operacionais com processos simulados | PASS 7/7 |
| Sintaxe Bash | PASS |
| Compose local e Production: renderização/configuração | PASS, CLI standalone Compose v5.5.1, sem Docker Engine |
| NuGet audit, incluindo transitivas da API | Nenhuma vulnerabilidade reportada pela fonte consultada |
| git diff --check e revisão de secrets | PASS; nenhum segredo real adicionado identificado |
| Suíte inteira de integração local | 32 passaram; 52 falharam na inicialização porque não havia Docker acessível |
| Docker build solicitado | Não executado com sucesso: `docker: command not found` |
| PostgreSQL/MinIO Testcontainers, smoke Docker Production, persistência/restore reais | PENDENTES de Docker; testes implementados, não validados aqui |
| GitHub Actions | NÃO EXECUTADO: publicação bloqueada por 403 |
| ECR/EC2/S3/IMDSv2, TLS e exposição externa reais | NÃO EXECUTADOS, fora do escopo de alterações AWS |

A configuração inicial do ambiente também impedia o paralelismo de MSBuild (named pipes); `-m:1 -nodeReuse:false -p:UseSharedCompilation=false` permitiu build e execução dos testes sem containers. Os testes não foram removidos, pulados ou substituídos por EF InMemory.

Novos testes cobrem par parcial de keys, configuração MinIO versus AWS, seleção da default chain sem acessar AWS, presign com token temporário, campos obrigatórios, configurações inseguras, spoof de proxy, limite por IP original, health HTTP, Swagger Production, upload/confirm/attach/leitura privada e falhas operacionais. O teste MinIO real e o ProductionApiTest dependem de Docker e estão entre os pendentes acima.

## 10–15. Comandos de operação

As instruções exatas, pré-requisitos e precauções estão em [aws-production.md](aws-production.md):

- Build/push: seção **Build e push ECR** — testes, build linux/amd64, tag SHA, login federado e push.
- Primeiro deploy: **Primeiro deploy na EC2** — gerar `.env.production` protegido e `./scripts/deploy-production.sh <SHA>`.
- Deploy seguinte: `./scripts/backup-postgres.sh` e `./scripts/deploy-production.sh <NOVA_SHA>`.
- Rollback: `./scripts/deploy-production.sh <SHA_ANTERIOR_COMPATIVEL>`; não desfaz migrations.
- Backup: `./scripts/backup-postgres.sh`; timer diário opcional incluído.
- Restore: **Backup diário e restore** — download, gzip test, criação de banco separado e importação com `ON_ERROR_STOP`; recuperação sem apagar o banco original.

## 16. Pendências

1. Aplicar/publicar o patch com uma credencial GitHub que possa escrever no repositório e abrir PR. A integração atual não conseguiu.
2. Executar workflow completo em Docker antes do merge/deploy, incluindo os testes novos de MinIO e Production.
3. Confirmar role IAM, hop limit IMDSv2, políticas privadas S3, Security Group e permissões de backup no ambiente real.
4. Definir domínio/certificado, instalar Nginx preparado e habilitar timer após revisar caminhos/usuário.
5. Executar a aceitação real AWS, incluindo `scripts/smoke-assets.py`, backup, recuperação e rollback.

## 17. Riscos e revisão

- Um host/disco, ~1 GB RAM e ~2 GB swap: não há alta disponibilidade nem garantia de capacidade sob carga; limites iniciais precisam de medição.
- Volume preserva reinícios, não perda do EBS; backup diário implica até ~24h de dados e recuperação manual. Retenção S3 precisa de política operacional para controlar custo.
- Migrations são sequenciais por módulo; falha pode deixar parte do schema atualizado. Compatibilidade da versão anterior é obrigatória mesmo quando o container antigo foi preservado.
- Troca da API tem downtime curto; falha de readiness exige rollback explícito. A tag de último sucesso permanece registrada.
- Scripts incluem credenciais de banco/JWT no ambiente do container: operadores com acesso Docker podem lê-las; acesso ao host/Docker deve ser restrito.
- Não foi comprovada a segurança da infraestrutura remota, porque nenhuma chamada AWS foi executada.

SECURITY REVIEW: nenhuma descoberta Critical/High identificada no diff revisado; status de liberação **BLOCKED por validação operacional pendente**, não por um achado de segredo. Persistem os riscos operacionais acima. Code Smell Validation: nenhuma refatoração arquitetural ou mudança de domínio; responsabilidades separadas em configuração, factory e scripts; build sem warnings. Isso não equivale a uma auditoria completa de todo o sistema nem a aceite de produção.
