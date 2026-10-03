# Vaulta Vision Dataset Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Persistir capturas reais autorizadas, resultados originais do scanner, versões e feedback por tarefa, preparando benchmark e evolução do scanner sem substituir seu motor neste incremento.

**Architecture:** Adicionar Vision ao monólito, com schema próprio e portas para Catalog/Assets. Integrar o scanner por uma coordenação na API e upload privado independente; o reconhecimento continua funcionando quando a participação no dataset estiver desabilitada ou falhar. Fotografias e feedback não alteram pesos de modelos automaticamente.

**Tech Stack:** .NET 10, C#, EF Core 10.0.6, Npgsql 10.0.0, PostgreSQL, Assets S3/MinIO existentes, MAUI Android, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-03-vaulta-vision-dataset-design.md`.

## Global Constraints

- Dataset desativado por padrão; sem coleta silenciosa e sem retenção infinita implícita.
- Não treinar modelos, gerar embeddings fictícios, escolher encoder por preferência ou considerar autoaceitação como ground truth.
- Preservar contratos existentes sem participação no dataset, IDs, migrations, catálogo, coleção e regras de venda.
- Assets guarda arquivos; Vision guarda histórico. Fotos de usuário não substituem artwork oficial.
- Sem novos microserviços, SQS, plataforma MLOps, pgvector ou FKs/DbContexts entre módulos.
- Não modificar o segundo de estabilidade nem afirmar detecção local de frente/verso implementada nesta entrega.
- Um resultado válido continua disponível quando persistência secundária falha; falha de armazenamento permanece observável.

## Review Focus

1. Retry concorrente do mesmo scan: não produzir dois Runs finais nem confundir imagens diferentes com a mesma execução; tarefa 3/4.
2. Falha/crash entre reconhecimento e upload: não promover exemplos sem arquivo confirmado e sem correspondência ao hash de entrada; tarefa 3/4/6.
3. Confirmação somente de Printing: não rotular automaticamente Variant, orientação ou bounding box; tarefa 1/5.
4. Fonte de feedback falsificada pelo cliente: impedir promoção administrativa/benchmark pelo endpoint de usuário; tarefa 5.
5. Dataset ou upload indisponível: manter scan comum, limitar memória e não perder resultado nem prometer salvamento; tarefa 4/6.

## Escopo e sequência maior

Este plano implementa o incremento estrutural de Vision explicitamente solicitado no complemento. Não representa a conclusão do novo scanner.

As entregas seguintes são separadas: catálogo/assets oficiais internos; benchmark inicial e seleção real de encoder/detector; detecção local frente/verso/crop; embeddings e resolução local; memória incremental de referências verificadas. Cada uma exige implementação e medições próprias. Preparar o dataset não bloqueia a sincronização do catálogo.

## Task 1: Domínio, contratos e regras de rótulos

**Files:**
- Create: quatro projetos `src/Modules/Vision/Vaulta.Vision.{Domain,Contracts,Application,Infrastructure}/Vaulta.Vision.{Domain,Contracts,Application,Infrastructure}.csproj`.
- Create: `Vaulta.Vision.Domain/ScanAttempt.cs`, `ScanCapture.cs`, `ScanRun.cs`, `ScanCandidate.cs`, `ScanFeedback.cs`, `VisionDatasetSample.cs`, `VisionRules.cs` dentro de `src/Modules/Vision/`.
- Create: `src/Modules/Vision/Vaulta.Vision.Contracts/VisionModels.cs`.
- Create: `tests/Vaulta.Identity.UnitTests/VisionRulesTests.cs`.
- Modify: `Vaulta.slnx`, `tests/Vaulta.Identity.UnitTests/Vaulta.Identity.UnitTests.csproj`.

**Interfaces:**
- Produces DTOs: `CreateScanAttemptRequest(string OperationalPolicyVersion, string? ImprovementPolicyVersion, Guid? SessionCorrelationId)`, `CreateScanAttemptResponse(Guid AttemptId, DateTimeOffset RetentionUntil)`.
- Produces `CreateVisionCaptureRequest(string ContentType, long ContentLength, string Sha256, int Sequence, string Role)` and `VisionCaptureUploadResponse(Guid CaptureId, Guid AssetId, string UploadUrl, DateTimeOffset ExpiresAt)`.
- Produces `VisionFeedbackRequest(Guid RunId, string Source, Guid? ConfirmedPrintingId, Guid? ConfirmedVariantId, string? Orientation, bool? CardPresent, string? Notes)`; Source aceita apenas UserConfirmation/UserCorrection no endpoint público.
- Produces `VisionTraceDto(Guid AttemptId, Guid? RunId, string PersistenceStatus)`, `VisionAttemptDetailsDto` com capturas, Runs, candidatos e feedback; nenhum owner/token/URL assinada no manifesto de benchmark.
- Produces `VisionBenchmarkManifestDto` e `VisionBenchmarkSampleDto` contendo DatasetVersion, sample/capture IDs, SHA256, revisão de rótulos, Printing/Variant esperadas e rótulos de orientação/presença opcionais. Exportação inicial é metadata, não distribuição de fotos privadas.
- `VisionRules.ValidateConfidence(double?)` rejeita NaN/Infinity/valores fora de 0..1; scores vetoriais têm semântica própria e não usam essa validação probabilística.

- [ ] Escrever testes que rejeitam confiança inválida e preservam campos desconhecidos nulos.
- [ ] Escrever testes que permitem corrigir somente acabamento, deixam orientação/presença desconhecidas e não tornam a Printing incorreta por consequência.
- [ ] Executar testes antes das regras; registrar falha esperada.
- [ ] Implementar domínio independente de provider, limites (8 capturas por tentativa, 10 candidatos por Run, 1.000 caracteres de notas) e rótulos por tarefa; novas execuções têm IDs próprios.
- [ ] Executar `dotnet test tests/Vaulta.Identity.UnitTests --filter FullyQualifiedName~VisionRulesTests` e revisar somente arquivos desta tarefa antes do commit.

## Task 2: Persistência, configuração e Assets privados

**Files:**
- Create: `src/Modules/Vision/Vaulta.Vision.Infrastructure/VisionDbContext.cs`, `EntityConfigurations.cs`, `DesignTimeDbContextFactory.cs`, `DependencyInjection.cs`, `VisionOptions.cs`, `VisionStore.cs`, `VisionAssetsAdapter.cs`.
- Create: `src/Modules/Vision/Vaulta.Vision.Infrastructure/Persistence/Migrations/*_InitialVision*` e snapshot gerados pelo EF.
- Create: `src/Modules/Vision/Vaulta.Vision.Application/VisionPorts.cs`.
- Modify: `src/Modules/Assets/Vaulta.Assets.Domain/AssetRules.cs`, `src/Vaulta.Web.Api/AssetEndpoints.cs`, `Program.cs`, `DatabaseMigrations.cs`, `Vaulta.Web.Api.csproj`.
- Test: `tests/Vaulta.Identity.IntegrationTests/VisionStorageTests.cs`, `tests/Vaulta.Identity.UnitTests/AssetRulesTests.cs`.

**Interfaces:**
- `IVisionAssets.CreateUploadAsync(Guid ownerId, CreateVisionCaptureRequest request, CancellationToken ct)` retorna o upload de Assets; `GetAccessAsync(Guid ownerId, Guid assetId, CancellationToken ct)` retorna estado, propósito e hash declarado/confirmado; `ConfirmAsync` confirma por storage real.
- Adicionar `GetOwnedAssetDetails` a `IAssetService` para metadata mínima, sem expor DbContext; implementar no AssetService existente.
- `IVisionCatalog.GetPrintingAsync(Guid printingId, CancellationToken ct)` retorna Printing e suas Variants através de `ICatalogSearch` em um adapter de Infrastructure.
- `AddVisionModule(IServiceCollection, IConfiguration)` registra opções e DbContext. `Vision:Enabled=false`; quando habilitado exigir `OperationalPolicyVersion`, `OperationalRetentionDays` entre 1 e 30 e `ImprovementPolicyVersion` configurados. Participação em melhoria permanece separada do armazenamento operacional.
- `IVisionStore` oferece operações de transação e consulta limitada: criar tentativa/upload pendente, reservar Run, completar Run, anexar captura confirmada, inserir feedback e consultar tentativa por proprietário. EF/SQL fica em Infrastructure.

- [ ] Escrever testes de owner incorreto, purpose incorreto e upload pendente rejeitado como captura confirmada.
- [ ] Permitir `vision-scan` como purpose privado no serviço Assets; rejeitar esse purpose na rota genérica `/assets/uploads`, direcionando ao endpoint Vision que valida política/retention.
- [ ] Criar schema `vision` com FKs internas e índices únicos `(AttemptId, Sequence)`, `(AttemptId, ExecutionKey)` e `(RunId, Rank)`; chaves de feedback também idempotentes. UUIDs de Catalog/Assets são referências validadas por portas.
- [ ] Gerar migration aditiva usando a ferramenta EF 10.0.6 já fixada em `.config/dotnet-tools.json`; adicionar Vision por último ao executor sequencial de migrations.
- [ ] Aplicar em PostgreSQL isolado e executar segunda aplicação sem mudanças; testar constraints reais e configuração default desabilitada.
- [ ] Validar upload/confirm/read privado em MinIO e os testes existentes de assinatura S3; não usar conta AWS de produção nessa validação.

## Task 3: Tentativas, execução idempotente e captura correspondente

**Files:**
- Create: `src/Modules/Vision/Vaulta.Vision.Application/VisionAttemptService.cs`, `VisionRunService.cs`, `VisionCaptureService.cs`.
- Create: `src/Modules/Vision/Vaulta.Vision.Infrastructure/VisionCatalogAdapter.cs`.
- Test: `tests/Vaulta.Identity.IntegrationTests/VisionAttemptTests.cs`.

**Interfaces:**
- `VisionAttemptService.CreateAsync(Guid ownerId, CreateScanAttemptRequest request, CancellationToken ct)` e `GetAsync(Guid ownerId, Guid attemptId, CancellationToken ct)` retornam DTOs da tarefa 1.
- `VisionRunService.BeginAsync(Guid ownerId, Guid attemptId, string executionKey, string inputSha256, CancellationToken ct)` retorna Run reservado ou replay já concluído; mesma chave/hash diferente dá 409. Execução em progresso também dá 409; reconhecimento comum sem tentativa não depende desse mecanismo.
- `VisionRunService.CompleteAsync(Guid runId, VisionPredictionSnapshot prediction, VisionPipelineManifest manifest, CancellationToken ct)` completa uma vez; reexecução explícita usa outra ExecutionKey e preserva o original.
- `VisionCaptureService.CreateUploadAsync(Guid ownerId, Guid attemptId, CreateVisionCaptureRequest request, CancellationToken ct)` e `ConfirmAsync(Guid ownerId, Guid attemptId, Guid captureId, CancellationToken ct)`.
- Manifesto contém componentes efetivamente executados, modelo/revisão disponível, prompt hash e pré-processamento. Componentes não implementados são nulos, nunca SigLIP/finish classifier fictícios.

- [ ] Testar duas reservas concorrentes da mesma chave, hashes diferentes e retries após conclusão; testar segunda tentativa legítima com bytes idênticos.
- [ ] Testar tentativa expirada, user desconhecido e limites de capturas/candidatos.
- [ ] Registrar hash SHA256 calculado pela API para a imagem realmente identificada. Para vincular uma captura ao Run, exigir upload confirmado e SHA256 correspondente. Vários frames futuros têm hashes explícitos por captura; não fingir que o endpoint atual processou todos eles.
- [ ] Manter upload pendente e resultado concluído como estados diferentes; não permitir promoção se não existir arquivo correspondente confirmado.
- [ ] Executar testes em PostgreSQL, incluindo rollback de transações curtas; não manter uma transação aberta durante a chamada ao provider.

## Task 4: Integração com scanner e rastreabilidade real

**Files:**
- Create: `src/Vaulta.Web.Api/VisionEndpoints.cs`, `ScannerVisionCoordinator.cs`.
- Create: `src/Modules/Catalog/Vaulta.Catalog.Application/RecognitionExecutionTrace.cs`.
- Modify: `src/Vaulta.Web.Api/ScannerEndpoints.cs`, `Program.cs`, `src/Modules/Catalog/Vaulta.Catalog.Contracts/ScannerModels.cs`.
- Modify: providers em `src/Modules/Catalog/Vaulta.Catalog.Infrastructure/Recognition/` somente para registrar componentes realmente chamados, sem dados de segredo.
- Test: `tests/Vaulta.Identity.IntegrationTests/VisionScannerTests.cs`.

**Interfaces:**
- Rotas autenticadas `/api/v1/vision/attempts` POST, `/{attemptId}` GET, `/{attemptId}/captures/uploads` POST e `/{attemptId}/captures/{captureId}/confirm` POST; leitura de captura usa URL privada de curta duração somente para proprietário.
- Identificação aceita query `attemptId` e header `X-Scan-Execution-ID`, opcionais juntos. Identificação sem esses campos mantém o contrato atual. Acrescentar `VisionTraceDto?` ao resultado por DTO de API que preserve todos os campos atuais sem introduzir dependência circular entre Contracts de Catalog/Vision.
- `ScannerVisionCoordinator.IdentifyAsync(Guid ownerId, byte[] image, string? gameCode, Guid? attemptId, string? executionKey, CancellationToken ct)` chama ScannerService e registra somente snapshot necessário à identificação, sem preços/raw requests/segredos.
- `RecognitionExecutionTrace` é scoped e acumula componentes reais chamados; providers registram modelo/prompt/revisão/preprocessamento utilizados. OCR/fallback devem ser rastreados quando executados.

- [ ] Testar o endpoint antigo sem Vision e confirmar mesma identificação/preço e nenhuma imagem armazenada.
- [ ] Testar tentativa de outro usuário, replay idempotente e mistura de imagens; acesso indevido falha antes de executar provider.
- [ ] Testar falha de gravação de Run após um reconhecimento válido: retornar resultado com `PersistenceStatus=failed`, telemetria sem PII e nenhuma promessa de salvamento. Cancelamento real do request continua sendo propagado.
- [ ] Não adicionar `Task.Run`, upload/download de imagem ou cópia S3 ao trecho de persistência do resultado. Aplicar timeout curto configurável a metadata secundária e limpar estado de tracking após falha.
- [ ] Testar fallback real com dois componentes no manifesto e todos os campos de embeddings/modelos ausentes nulos.

## Task 5: Feedback, orientação e preparação de benchmark

**Files:**
- Create: `src/Modules/Vision/Vaulta.Vision.Application/VisionFeedbackService.cs`, `VisionDatasetService.cs`, `VisionBenchmarkManifestService.cs`.
- Modify: `src/Vaulta.Web.Api/VisionEndpoints.cs`.
- Create: `src/Vaulta.Web.Api/VisionCommands.cs` para operações de revisão/exportação do operador.
- Test: `tests/Vaulta.Identity.IntegrationTests/VisionFeedbackTests.cs`, `VisionBenchmarkTests.cs`.

**Interfaces:**
- `VisionFeedbackService.RecordAsync(Guid ownerId, Guid attemptId, string idempotencyKey, VisionFeedbackRequest request, CancellationToken ct)`.
- Rota `POST /api/v1/vision/attempts/{attemptId}/feedback`, somente fontes UserConfirmation/UserCorrection; campos de identidade, orientação e presença são rotulados separadamente.
- `VisionDatasetService.PromoteAsync(Guid attemptId, Guid feedbackId, CancellationToken ct)` é operação de operador, sem rota administrativa pública. Exige permissão aplicável, captures correspondentes prontas e classificação adequada do rótulo.
- CLI `--vision-review <attemptId> <feedbackId>` cria revisão confiável preservando evento anterior; `--vision-export-manifest <datasetVersion> <outputPath>` exporta metadata determinística de amostras autorizadas/revisadas, sem ownerId, notas, URLs assinadas ou imagens privadas.
- `VisionBenchmarkManifestService.ExportAsync(string datasetVersion, CancellationToken ct)` produz contrato da tarefa 1. DatasetVersion congelada não muda silenciosamente quando chega novo feedback.

- [ ] Testar VariantId de outra Printing, source AdminReview enviado pelo usuário e confirmação sem consentimento: nenhuma promoção válida.
- [ ] Testar `front/back/unknown` e `card-present/no-card/multiple-cards/uncertain` com Printing opcional; esses rótulos são feedback, não detecção automática já implementada.
- [ ] Testar preservação da predição original e feedback anterior, separando Printing correta de Variant errada.
- [ ] Testar exportação com rótulo de Printing conhecido e Finish desconhecido; excluir de métricas de Finish sem inventar label.
- [ ] Testar versões congeladas e exportação sem PII; comando exige banco e arquivos explícitos, não envia dados externos.
- [ ] Documentar que não há admin role apropriado hoje; não inferir privilégio pelo nome/e-mail da conta seed.

## Task 6: Participação opt-in no app sem atrasar o scan

**Files:**
- Create: `src/Vaulta.App.Core/Vision/VisionClient.cs`, `VisionParticipation.cs`, `VisionCaptureArchive.cs`.
- Modify: `src/Vaulta.App.Core/Catalog/ScannerClient.cs`, `src/Vaulta.App/Views/ScannerSessionPage.Scene.cs`, `ScannerSessionPage.Continuous.cs` e registro DI real encontrado no app.
- Test: `tests/Vaulta.App.Core.UnitTests/Vision/VisionParticipationTests.cs`, `VisionCaptureArchiveTests.cs`, `VisionClientTests.cs`.

**Interfaces:**
- `IVisionClient` cria tentativa, prepara upload, confirma captura, consulta tentativa e envia feedback via rotas das tarefas 4/5.
- `VisionParticipation` mantém versão da política aceita e opção separada de melhoria; default off, mudança de versão desativa participação até nova manifestação explícita.
- `VisionCaptureArchive.ArchiveAsync(byte[] image, Guid attemptId, CancellationToken ct)` processa uma imagem por vez, com limite de uma pendente, SHA256 e confirmação do upload; chamado por loop de vida controlado do app, nunca fire-and-forget ilimitado.
- ScannerClient conserva `IScannerClient.ScanCardAsync` existente e acrescenta overload interno com AttemptId/ExecutionKey, sem quebrar fakes de testes existentes.

- [ ] Testar participação desabilitada: não criar tentativa, não enviar imagem adicional e não alterar UX/total atual.
- [ ] Expor opção voluntária em opções da sessão, com finalidade e retenção fornecidas pela API, separando permissão operacional de contribuição para melhoria. Não exigir confirmação de toda carta.
- [ ] Quando habilitado, reservar tentativa com metadata curta; reconhecimento segue imediatamente e upload adicional ocorre pelo loop controlado. Se reserva falhar, executar scan comum. Associar feedback ao resultado correspondente, nunca à próxima carta.
- [ ] Testar falha de upload, app saindo da tela, retry, policy version diferente e buffer cheio; cancelamento descarta trabalho secundário sem perder resultado da sessão.
- [ ] Preservar animação, soma, processamento de uma carta por vez e rearmamento atuais. Interface não deve sugerir detecção local de verso enquanto ela não existe.

## Task 7: Operação, validação e entrega honesta

**Files:**
- Create: `docs/vision-dataset.md`, `docs/design/vision-dataset-validation-2026-10-03.md`.
- Modify: este plano e o status da especificação conforme avanço real.

- [ ] Compilar API Release e executar unitários afetados, App.Core e arquitetura. Executar integrações novas contra PostgreSQL/MinIO isolados, sem depender de API paga.
- [ ] Aplicar migrations duas vezes e conferir que entidades existentes permanecem; rodar regressão dos endpoints scanner/assets e regras de coleção relevantes.
- [ ] Verificar upload pronto, propriedade, eventos imutáveis, constraints concorrentes e falhas de dataset sem perder identificação. Não duplicar testes já aprovados sem novo motivo.
- [ ] Construir Android para confirmar compatibilidade da integração do app; validar permissões/scan no aparelho quando disponível. Não declarar teste físico executado se não houve aparelho.
- [ ] Documentar configuração, opt-in, retenção e remoção operacional, comandos reais, limitações e contagens observadas. Coleta ampla continua desabilitada até operação de exclusão/lifecycle disponível.
- [ ] Fazer revisão do diff final por agente independente somente após o usuário selecionar método de execução que autorize essa revisão, usando skill correspondente.
- [ ] Entregar sem afirmar embeddings, reconhecimento local de verso, aprendizagem online ou treinamento funcionando. Não fazer deploy AWS nem publicar APK automaticamente como parte deste plano.

## Self-review e estado

Plano revisado contra a especificação: persistência mínima, multi-frame estrutural, feedback por tarefa, orientação rotulada, versionamento, Assets, políticas e benchmark metadata cobertos. Detector, índice de embeddings, distribuição de memória, coleta ampla/lifecycle e treinamento são próximos incrementos explicitamente separados.

Status: plano proposto para revisão; nenhuma tarefa de produto executada ainda. Arquivos de planos/validação já existentes no workspace serão preservados. Método de execução ainda não escolhido pelo usuário.
