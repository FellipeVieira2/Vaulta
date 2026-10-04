# Otimização do Pipeline de Artwork do Catálogo Vaulta

**Data:** 2026-10-04
**Status:** Draft para revisão
**Autor:** Claude Opus 5.5 + Fellipe Vieira De Souza

## 1. Contexto e Objetivo

O pipeline atual de ingestão de artwork (`CatalogArtifactImporter`) processa Printings sequencialmente: download → decode → resize → encode WebP → upload S3 → persistência EF. Esse fluxo não escala para dezenas ou centenas de milhares de imagens, especialmente em produção numa EC2 `t3a.micro` (2 vCPU, 1 GB RAM) que compartilha recursos com PostgreSQL.

O objetivo desta tarefa é tornar o download/processamento/upload significativamente mais rápido, preservando idempotência, ETag/hash dedup, segurança de thread do EF Core e separando responsabilidades operacionais (metadata ≠ artwork ≠ vision).

### Restrições Críticas
- **DbContext não é thread-safe**: cada worker concorrente precisa de seu próprio scope/DbContext
- **Recursos limitados**: t3a.micro com API + PostgreSQL no mesmo host
- **Idempotência obrigatória**: segunda execução deve skippar o que já está pronto
- **ETag/Last-Modified**: conditional requests devem continuar funcionando
- **Content-addressed storage**: hash igual = mesmo objeto S3, sem duplicação
- **Advisory lock global**: preservar proteção contra imports concorrentes, mas não serializar workers internos

## 2. Decisões de Design Aprovadas

### 2.1 Concorrência Limitada com Channel Bounded

**Arquitetura:** Producer-Consumer com `Channel<PrintingId>` bounded.

```
Producer (paginação DB)
    ↓
Channel<PrintingId> (capacity: WorkerCount * 2)
    ↓
N Workers (cada um com IServiceScope próprio)
    ↓
Contadores thread-safe (Interlocked)
```

**Configuração nova:**
```csharp
public sealed class ArtworkImportOptions
{
    public int WorkerCount { get; set; } = 3; // Default conservador para t3a.micro
    public int PageSize { get; set; } = 200;
    public int RevalidateAfterHours { get; set; } = 24;
}
```

**Validação:** `WorkerCount` entre 1–16. Produção documentada começa com 3; ajuste baseado em métricas reais.

### 2.2 Isolamento de DbContext por Worker

Cada worker cria seu próprio `IServiceScope` e resolve:
- `CatalogDbContext` próprio
- `AssetsDbContext` próprio (via `ISystemAssetService` scoped)
- `CatalogArtworkDownloader` (HttpClient já é singleton/thread-safe)

**Nunca compartilhar DbContext entre workers.**

### 2.3 Separação de Responsabilidades Operacionais

**Antes:**
- `--catalog-sync` executava metadata sync + artwork import + vision preparation implicitamente
- Bootstrap Python rodava artwork/embeddings após cada idioma

**Depois:**
- `--catalog-sync tcgdex <scope>` → SOMENTE metadata (TCGdex → PostgreSQL)
- `--catalog-assets-import all` → SOMENTE artwork (download/process/S3/persistência)
- `--vision-index-build <manifest>` → SOMENTE embeddings/index

**Comando de conveniência opcional:** `--catalog-full-bootstrap` executa as 3 fases explicitamente, mas não esconde preparação completa dentro de sync.

### 2.4 Pricing Dentro do Worker (Post-Upload)

Pricing permanece dentro do worker de artwork, mas executado DEPOIS do upload S3 bem-sucedido. Isso evita:
- Consultas N+1 bloqueando throughput de assets
- Download atrasado por pricing lento

Se pricing falhar, artwork ainda é considerado "ready" (não perde upload válido). Pricing pode ser reprocessado depois sem refazer download.

### 2.5 Race Condition em Hash Duplicado

Dois workers podem produzir o mesmo hash simultaneamente. Solução:
- `SystemAssetService.StoreArtworkAsync` já usa upsert por ObjectKey
- Adicionar retry curto (3 tentativas, 100ms backoff) em caso de violação de unique constraint
- Transaction isolation level ReadCommitted é suficiente (unique constraint protege)

### 2.6 Progress Logging Periódico

Não logar por carta. Logar a cada 100 itens OU 30 segundos (o que vier primeiro):

```
Catalog artwork import
Processed: 4,000 / 32,500
Ready: 3,820 | Skipped: 120 | Missing: 42 | Failed: 18
Workers: 3 | Throughput: 7.8 cards/s | Elapsed: 00:08:32
```

### 2.7 Métricas Finais do Job

Ao final, imprimir relatório estruturado:
- Total candidates
- Downloaded / Not modified (304) / Fresh skipped / Missing / Failed
- S3 objects created / Bytes uploaded
- Elapsed / Average cards/sec
- Breakdown opcional: download duration / image processing / S3 / DB

### 2.8 Bootstrap Python Refatorado

Script `scripts/catalog-import-languages.py` ganha flag `--phase`:
- `--phase metadata` → só sync TCGdex (sequencial por idioma)
- `--phase artwork` → só `--catalog-assets-import all` (uma vez)
- `--phase vision` → só `--vision-index-build` (uma vez)
- Sem flag → executa todas as fases na ordem (comportamento atual, mas explícito)

**Default MVP brasileiro:** `pt-br,en,ja` apenas Pokémon. Demais idiomas configuráveis.

### 2.9 ModelManifestPath Validation

Corrigir check em `VisionCommands.cs` e `CatalogCommands.cs`:
```csharp
// Antes (bug):
if(app.Services.GetRequiredService<VisionOptions>().ModelManifestPath is not null)

// Depois (correto):
if(!string.IsNullOrWhiteSpace(app.Services.GetRequiredService<VisionOptions>().ModelManifestPath))
```

String vazia de environment variable não deve disparar Vision.

### 2.10 VisualReferenceBuilder Batched Persistence

Atualmente faz `SaveChangesAsync` por Printing. Avaliar batch pequeno (10–20 referências) para reduzir round trips, mantendo isolamento de falhas. Encoder concurrency já é limitada (`Vision__EncoderConcurrency=1` em produção); I/O S3 pode ser concorrente.

## 3. Arquivos Afetados

### Modificações
- `src/Modules/Catalog/Vaulta.Catalog.Infrastructure/Artwork/CatalogArtifactImporter.cs` → Refatorar para producer-consumer com Channel
- `src/Modules/Catalog/Vaulta.Catalog.Infrastructure/DependencyInjection.cs` → Registrar `ArtworkImportOptions`, validar WorkerCount
- `src/Vaulta.Web.Api/CatalogCommands.cs` → Separar artwork de vision, corrigir ModelManifestPath check
- `src/Vaulta.Web.Api/VisionCommands.cs` → Corrigir ModelManifestPath check
- `src/Modules/Vision/Vaulta.Vision.Application/CatalogVisionPreparation.cs` → Remover chamada implícita a artwork.ImportAsync
- `src/Modules/Vision/Vaulta.Vision.Infrastructure/VisualReferenceBuilder.cs` → Batch persistence opcional
- `scripts/catalog-import-languages.py` → Adicionar `--phase`, default pt-br/en/ja
- `docker-compose.production.yml` → Adicionar `Catalog__Artwork__WorkerCount=3`

### Novos Arquivos
- `src/Modules/Catalog/Vaulta.Catalog.Infrastructure/Artwork/ArtworkImportOptions.cs` → Configuração
- Testes de concorrência, idempotência, ETag, race condition, cancelamento, resume

## 4. Testes Obrigatórios

1. **Concorrência segura:** N workers → nenhuma exceção de DbContext concorrente
2. **Idempotência:** Primeira execução → upload; segunda → skip
3. **ETag/304:** Resposta 304 → nenhum upload, nenhum novo AssetId
4. **Race condition de hash:** Duas Printings com imagem idêntica → um objeto físico, ambas resolvem asset válido
5. **Partial failure:** 1 imagem falha, 99 funcionam → job continua
6. **Cancelamento:** Ctrl+C → workers finalizam gracefully, sem tasks órfãs
7. **Resume:** Job interrompido → execução seguinte continua de onde parou
8. **Empty model path:** `Vision__ModelManifestPath=""` não dispara Vision
9. **Progress logging:** Output periódico aparece, não spam por carta
10. **Métricas finais:** Relatório completo ao final do job

## 5. Critérios de Aceite

A tarefa só está concluída quando:
- [ ] Metadata sync não dispara mais preparação completa implicitamente
- [ ] Artwork import suporta concorrência limitada e segura (WorkerCount configurável)
- [ ] Nenhum DbContext é compartilhado entre workers
- [ ] ETag/hash/idempotência continuam funcionando
- [ ] Retries continuam limitados
- [ ] Segunda execução não refaz downloads
- [ ] Upload S3 ocorre corretamente
- [ ] Falha individual não derruba todo o job
- [ ] Cancelamento funciona
- [ ] Relatório de progresso funciona
- [ ] Testes passam (unitários + integração + smoke)
- [ ] Smoke local passa (Base Set duas vezes, segunda majoritariamente skipped)
- [ ] Primeira carga Pokémon consegue ser executada/resumida com comandos documentados
- [ ] Bootstrap Python suporta `--phase` e default pt-br/en/ja

## 6. Fora do Escopo

- Microserviços, Kafka, Kubernetes, ECS, SQS
- Trocar S3, PostgreSQL, ImageSharp, modelo Vision
- Alterar scanner UX ou arquitetura Card/Printing/Variant
- Remover idempotência, advisory lock, hashes/ETags
- Paralelizar EF com mesmo DbContext
- Aumentar infraestrutura AWS automaticamente
- Multipart upload para imagens pequenas
- Telemetry/OpenTelemetry complexo (apenas logs/métricas simples)

## 7. Riscos e Mitigações

| Risco | Mitigação |
|-------|-----------|
| OOM em t3a.micro com 3 workers | WorkerCount configurável; começar com 3, medir, ajustar |
| Race condition em upsert de asset | Unique constraint + retry curto |
| Pricing atrasa artwork | Pricing post-upload, falha não bloqueia ready |
| DbContext compartilhado acidentalmente | IServiceScope por worker, testes de concorrência |
| Bootstrap roda vision prematuramente | Separação explícita de fases, validação de artwork completeness |

## 8. Próximos Passos

Após aprovação deste spec:
1. Invocar skill `writing-plans` para criar plano de implementação detalhado
2. Implementar seguindo TDD
3. Validar com smoke local (Base Set)
4. Build/push ECR
5. Deploy em produção
6. Executar primeira carga Pokémon (pt-br → en → ja → artwork → vision)
7. Validar métricas e ajustar WorkerCount se necessário