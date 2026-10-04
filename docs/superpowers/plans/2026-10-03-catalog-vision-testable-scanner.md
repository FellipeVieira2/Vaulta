# Scanner com catálogo próprio — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Entregar API e APK de teste que identifiquem cartas pelo catálogo/índice visual próprios, com histórico real e comportamento automático de câmera.

**Architecture:** Evoluir Catalog e Assets existentes, implementar Vision no monólito e substituir conjuntamente fluxo/API/app antigos de reconhecimento. Importação externa e preço ficam fora dos requests do scanner. Detector/orientação e encoder são escolhidos e versionados por avaliação real antes da integração definitiva.

**Tech Stack:** .NET 10/MAUI Android, PostgreSQL/EF, Assets S3/MinIO; runtime de visão compatível com os pesos avaliados, sem serviço externo obrigatório de catálogo em runtime.

**Spec:** `docs/superpowers/specs/2026-10-03-catalog-vision-testable-scanner-design.md`.

## Global Constraints

- A entrega inclui identificação integrada ao app; preparação de dataset isolada não conclui o trabalho.
- Fluxo antigo pode ser substituído; preservar dados/IDs úteis e módulos fora do scanner.
- Embeddings reais, encoder/revisão/weights hash declarados; nenhuma escolha somente por preferência.
- Sem treinamento agora e sem transformar autoaceitação em ground truth.
- GPT para evidência/fallback, preço pelo serviço local, sem pesquisa web durante scan.
- Captura automática sem tempo fixo de imobilidade; processamento de uma carta por vez.
- Imagens privadas de usuários não substituem artwork oficial nem são distribuídas entre aparelhos.
- Não declarar benchmark físico, deploy, cobertura ou precisão que não foram efetivamente verificados.

## Review Focus

1. Mesmo artwork, Printing distinta por número/idioma/reprint: resolver com evidência adicional ou pedir revisão, tarefa 5.
2. Verso/sleeve/objeto retangular: orientar ou rejeitar sem gastar chamadas de identidade desnecessárias, tarefa 6.
3. Modelo/preprocessing atualizado com índice antigo: impedir comparação silenciosa, tarefa 4.
4. Carta válida sem imagem ou preço: não inventar correspondência/valor nem travar sessão, tarefas 3/5/7.
5. Captura/feedback indevido ou não autorizado: não promover memória errada/privada, tarefa 7.

## Estrutura e interfaces decididas

- `src/Modules/Vision/Vaulta.Vision.Contracts/VisionScannerModels.cs`: captura, regiões/orientação, candidato canônico, scores/confianças separados, resultado de identidade/variante e estado de cotação.
- `Vaulta.Vision.Application/VisionPorts.cs`: `IImageEncoder.EncodeAsync(Stream image, CancellationToken ct) -> ImageEmbedding`, `IVisualReferenceIndex.SearchAsync(ImageEmbedding embedding, int topK, CancellationToken ct) -> IReadOnlyList<VisualMatch>` e `IVisionCatalog.GetPrintingsAsync(IReadOnlyList<Guid> ids, CancellationToken ct)`.
- `ImageEmbedding` inclui vetor e identidade completa de modelo/preprocessing; `VisualMatch` inclui ReferenceId, PrintingId, score e origem. Dimensão vem do encoder validado, não constante escolhida arbitrariamente.
- `VisionScannerService.IdentifyAsync(VisionScanInput input, CancellationToken ct) -> VisionScanResultDto` coordena embedding/candidatos/evidência/catalog/price. Input aceita Captures[]; implementação começa com um frame e aceita a extensão sem confundir várias cartas.
- `Vaulta.Vision.Infrastructure/Recognition/`: runtime/pesos/preprocessamento, OCR e adapter de evidência GPT; `Indexing/`: geração, storage e índice; persistência de histórico conforme plano de dataset.
- `src/Vaulta.App.Core/Vision/`: cliente, estado de sessão e seleção de frames; `src/Vaulta.App/Platforms/Android/Vision/`: runtime de detecção/orientação e câmera. Bindings/pacotes exatos ficam registrados no relatório da avaliação real da tarefa 1, antes de código definitivo dependente deles.
- Não criar interface de provider que assuma as responsabilidades de todo o sync; manter ICatalogProvider/ICatalogSync existentes.

## Task 1: Avaliar runtime, encoder e presença/orientação reais

**Files:** Create `scripts/vision/` (aquisição/exportação/avaliação somente, sem segredos), `docs/design/vision-model-evaluation.md` e manifesto em `artifacts/vision/` ignorado no Git; fixtures/labels em `tests/Fixtures/Vision/`.

- [x] Verificar referências/pesos oficiais de dois encoders adequados (famílias SigLIP, DINOv2 ou CLIP) e compatibilidade real com .NET servidor e Android. Fixar revisões e SHA256; não colocar pesos grandes no Git.
- [ ] Executar extração real em imagens oficiais e fotos reais disponíveis, incluindo negativos/verso; fixtures sintéticas ficam identificadas como sintéticas.
- [x] Medir Top-K/custo de CPU/memória, dimensão, download e custo de inferência; verificar preprocessamento e licença/origem.
- [ ] Avaliar presença/orientação em Android/emulador com runtime suportado. Não classificar contorno geométrico sozinho como detector semântico treinado.
- [x] Escolher a menor solução que cumpra a prova funcional observada, registrar resultados e limitações. Se não houver fotos reais suficientes, não prometer precisão; entregar a instrumentação para avaliá-la no primeiro teste físico.
- [x] Registrar paths, package versions, tensor names/shapes, normalização e parâmetros no manifesto; esses contratos determinam tarefa 4/6.

## Task 2: Catálogo determinístico e metadata de resolução

**Files:** Modify `CatalogEntities.cs`, `ProviderModels.cs`, `TcgDexProvider.cs`, `CatalogSyncService.cs`, configurações/migrations de Catalog; tests `TcgDexProviderTests.cs`, `CatalogCollectionFlowTests.cs`.

- [x] Escrever regressões de sync repetido, idioma/artwork corretos, variantes detalhadas combinando acabamento/edição/stamps e nomes repetidos sem merge indevido.
- [x] Expandir mapping neutral de provider para série/metadata/variantes disponíveis e referências de preço externas. Manter campos ausentes desconhecidos.
- [x] Reusar locking/hash/UUIDs/CLI, acrescentar checkpoint/contagens/erros por item e retry retomável; não recriar banco.
- [x] Criar/aplicar migration aditiva e rodar duas sincronizações do mesmo conjunto. Verificar IDs/contagens estáveis e cancelamento/falhas parciais.

## Task 3: Artwork interno e cotação local

**Files:** Modify `Asset.cs`, `AssetPorts.cs`, `AssetService.cs`, `S3ObjectStorage.cs`, migrations de Assets; create catálogo asset ingestion/renditions; modify `CatalogQueries.cs`, leitor de detalhes e comandos.

- [x] Testar stream válido/404/hash alterado/ETag e segundo sync sem baixar novamente quando há validadores; falta de ETag exige comparação honesta por hash após download.
- [x] Adicionar upload interno por stream, metadata de origem/dimensões e renditions sem upscaling, usando IDs/hash estáveis e estados retomáveis para DB/storage não atômicos.
- [x] Importar imagens para MinIO/S3 e fazer catálogo servir acesso interno. Registrar ausentes/falhas sem apagar Printing.
- [x] Persistir observações de preço com moeda/origem/data/variante e cálculo BRL somente com FX real; evolução do snapshot existente sem duplicar preço pedido de anúncio.
- [x] Fazer detalhes/cotação lerem somente PostgreSQL/Assets. Atualizações externas manuais/job existente fora do request; sem cotação é estado explícito.
- [x] Testar com HTTP externo bloqueado que catálogo/detalhes/quote existentes continuam disponíveis.

## Task 4: Referências vetoriais e busca próprias

**Files:** Create `Vaulta.Vision.Domain/VisualReference.cs`, Infra encoder selecionado/manifest validation, `VisualReferenceBuilder.cs`, `PostgresVisualReferenceStore.cs`, `CosineReferenceIndex.cs`, EF migration e `VisionCommands.cs`.

- [x] Escrever testes de compatibilidade encoder/preprocessamento/dimensão e rejeição de NaN/vetor inválido; teste funcional usa pesos reais, teste matemático unitário usa vetores declaradamente artificiais só para validar cosseno.
- [x] Implementar `IImageEncoder` com saída real verificada na tarefa 1. Reusar session/runtime, limitar concorrência/memória e aceitar cancelamento.
- [x] Implementar CLI `--vision-index-build <modelManifestPath>` e `--vision-index-status`, gerando referências por hash e sem repetir imagens inalteradas.
- [x] Integrar a importação manual de catálogo/assets com geração automática de referências quando o encoder estiver configurado. Persistir pendências/falhas e retomar; sync/index não é declarado completo quando faltam vetores. CLI de rebuild continua disponível, sem inferência em transação longa.
- [x] Persistir vetores e publicar índice exato por cosseno de forma atômica; limite de memória e chunking, versão de índice e metadados legíveis. Não expor listas ilimitadas em requests.
- [x] Verificar referência oficial indexada retorna PrintingId canônico em Top-K; testar troca de modelo sem misturar versões e segundo build incremental.

## Task 5: Resolver com catálogo, OCR/GPT e preço local

**Files:** Create `VisionScannerService.cs`, `VisionPrintingResolver.cs`, adapter OCR/GPT/Catalog/quote e DTOs; modify `ScannerEndpoints.cs`, `Program.cs`, DI e cliente/API contratos.

- [x] Testar mesmos artworks/números pequenos/idiomas, ausência de número, candidato fora do catálogo, acabamento desconhecido e Printing correta sem cotação.
- [x] Implementar crop/preprocessamento segundo a prova e busca Top-K primeiro. Cruzar evidência OCR/texto/layout com candidatos próprios; GPT recebe candidatos/evidência quando necessário, sem preço/web/IDs inventados.
- [x] Separar scores vetoriais de confiança de Printing/Variant; manter revisão para incerteza relevante e política 0,80 sem converter cosseno em probabilidade.
- [x] Substituir composição antiga de scanner e pesquisa web em runtime. Endpoints de identificação/detalhes usam Vision/local pricing; API/app atualizados juntos.
- [x] Bloquear TCGdex/JustTCG/web em integração e confirmar reconhecimento real da imagem indexada; GPT fallback usa stub em testes comuns e chamada real somente teste limitado autorizado.
- [x] Retornar resultado aceito, ambíguo, verso, não-carta ou nova captura, conforme evidência. Sem cotação não impede identidade/sessão.

## Task 6: Câmera Android, frente/verso e captura automática

**Files:** Modify `ScannerSessionPage.Continuous.cs`, `.Scene.cs`, `CameraSceneSampler.cs`, `ScannerSceneGate.cs`; create Android Vision bridge selecionado na tarefa 1 e App.Core state machine/frame selector.

- [x] Escrever testes de estados sem carta, verso, frente útil, blur, remoção/troca, uma operação em voo e cópias idênticas separadas.
- [x] Implementar inferência/localização/orientação local conforme prova; obter regiões/cantos, usar preview reduzido e descartar backlog. Preservar opção unknown e captura manual para casos difíceis.
- [x] Remover gate rígido de 1 segundo. Disparar pelo frame utilizável, persistir foto e liberar posição física enquanto resolve; não esperar usuário manter celular apontado.
- [x] Mostrar “Vire a carta” para verso sustentado, sem identificar Printing pelo verso comum. Desconhecido não vira bloqueio permanente.
- [x] Integrar novo cliente e política de confirmação; manter animação discreta, valor breve, total parcial e próxima carta. Não deixar última carta ocupando a tela.
- [x] Compilar Android ARM64, verificar assinatura e inclusão dos pesos ONNX.
- [ ] Executar harness/emulador; medir custo do preview/inferência e testar em dispositivo disponível sem inventar validação física.

## Task 7: Histórico real, feedback e memória incremental

**Files:** Executar modelo/portas/migrations/feedback/Assets privados do plano `2026-10-03-vaulta-vision-dataset.md`, adaptando Task 4/6 daquele plano ao novo scanner e sem preservar contratos antigos por obrigação.

- [x] Implementar ScanAttempt/Captures/Runs/Candidates e manifesto real, sem precios/raw provider secrets/PII desnecessária; idempotência por execução e hashes de captura.
- [x] Persistir embeddings reais de capturas autorizadas junto ao Run, com modelo/preprocessamento/dimensão. Captura não verificada fica fora do índice canônico e de treino supervisionado, mesmo que tenha embedding calculado.
- [x] Implementar correção por tarefa e política operacional/melhoria separadas, uploads privados e retenção/remoção para teste controlado. Default sem contribuição ao dataset.
- [x] Testar ownership, variante de outra Printing, source administrativo falsificado, promoção sem permissão e falha de upload sem perder resultado.
- [x] Acrescentar referências reais suficientemente verificadas ao índice com versão incremental, sem re-treinar encoder nem usar autoaceitação como label forte.
- [x] Preparar benchmark congelado e comparar reruns; medir Printing/Finish/presença/orientação separadamente e impedir vazamento de frames relacionados entre avaliação/desenvolvimento.

## Task 8: Entrega realmente testável

**Files:** Create `docs/vision-scanner-test.md`, relatório observado e `artifacts/Vaulta-vision-scanner-arm64.apk`; atualizar este plano/spec com estado real.

- [x] Rodar sync/index reais, informar cobertura/contagens/tempo, apontar API de teste acessível ao celular e confirmar health/versão/index ativos. Carga multilíngue completa permanece em andamento.
- [x] Compilar API Release e Android ARM64 Debug, aplicar migrations duas vezes e executar unitários/integrações relevantes. Revisão independente anterior corrigida; ajustes desta entrega validados por regressões.
- [x] Gerar APK assinado e documentar endpoint/modelo/índice correspondentes, sem segredos dentro do app. API publicada e saudável na AWS.
- [x] Executar smoke API público com GPT real, catálogo e cotação locais; regressões de soma/sem cotação e providers bloqueados aprovadas.
- [ ] Observar exibição, soma e próxima carta no aparelho físico.
- [ ] Testar negativos/verso/troca e registrar lacunas físicas do benchmark. Não encerrar apenas com scaffold, contratos, migrations ou embeddings sem integração ao aplicativo.

## Self-review e estado

O plano anterior exclusivamente estrutural continua útil como subplano de dataset, mas não rege o critério final de entrega. Este plano cobre a identificação real via catálogo, índice e app solicitada pelo usuário, sem obrigar manutenção do fluxo antigo.

Estado em 03/10: implementação das tarefas 1–7 integrada, 737 testes aprovados e APK ARM64 compilado/assinado. A execução física de orientação/câmera continua pendente; protótipos conservadores retornaram unknown nos 16 artworks oficiais. Tarefa 8 em andamento: o usuário autorizou deploy AWS, chamada real e importação remota. Evidências detalhadas em docs/design/vision-scanner-validation-2026-10-03.md; não declarar precisão física nem cobertura integral por inferência.

Atualização da entrega master: ver docs/design/vision-scanner-deployment-2026-10-03.md. API b443dd5 publicada; 782 testes .NET e 16 de operação aprovados. Smoke GPT real resolve Printing e retorna cotação local. Fila dos 18 códigos de idioma ativa; câmera física, acabamento e cobertura integral continuam pendentes.
