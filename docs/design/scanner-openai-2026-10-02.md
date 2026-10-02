# Scanner OpenAI — implementação de 02/10/2026

Este documento registra a etapa inicial. A [evolução posterior](scanner-fast-identification-2026-10-02.md) substitui o refinamento automático por uma foto, atualiza o prompt para v2 com HP/PS, acrescenta descoberta local/TCGdex e atualização diária. As afirmações abaixo de “sem deploy/inferência paga” se referem à etapa inicial.

Branch local: `codex/scanner-openai`. Especificação: integrar GPT como extração visual no scanner existente. Sem deploy, inferência paga, nova infraestrutura ou migration.

## Implementado e arquitetura final

`ScannerSessionPage` mantém câmera, estabilidade, captura contínua, sessão e confirmação das variantes. `ScannerSceneGate` consome a cena para não repetir requests em cada tick. `ScannerRefinementBudget` explicita no máximo um refinamento; identificação inicial segura não refina. Não há streaming de frames ou cache de identidade por hash.

```text
Câmera estável → frame → POST /api/v1/scanner/identify
→ ScannerService → EvidenceCardRecognitionProvider
→ OpenAiCardEvidenceExtractor → Responses/visão → CardEvidence
→ CardEvidenceCatalogMatcher → catálogo PostgreSQL → candidatos com PrintingId canônico

OpenAI ausente/timeout/inválido/sem candidato → OCR/Tesseract → catálogo local
Ambiguidade com candidatos → revisão, sem sobrescrever silenciosamente com OCR

PrintingId confirmado → GET /api/v1/scanner/printings/{id}
→ snapshot PostgreSQL válido → detalhes/preços existentes
→ ausente/expirado → advisory lock + recheck → TCGdex/PTAX → grava snapshot
```

Arquivos principais: `Recognition/OpenAiCardEvidenceExtractor.cs`, `CardEvidenceOpenAiProtocol.cs`, `OpenAiScannerOptions.cs`, `ScannerRecognitionOptions.cs`, `EvidenceCardRecognitionProvider.cs`, `ScannerTelemetry.cs`, `DependencyInjection.cs`, parser/matcher existentes, `App.Core/Catalog/ScannerRefinementBudget.cs` e duas partes existentes da página MAUI. `NovaCardRecognitionProvider` tornou-se wrapper da mesma implementação de matching. Contratos HTTP, Game/Series/Set/Card/Printing/Variant, coleção e marketplace foram preservados.

## OpenAI, schema e decisão de integração

Modelo **configurável** `gpt-6-luna`, atualizado em 02/10/2026 a pedido do usuário para a opção mais barata da família GPT-6. A [documentação oficial do modelo](https://developers.openai.com/api/docs/models/gpt-6-luna) confirma entrada de imagem, Responses e Structured Outputs. A [tabela oficial](https://developers.openai.com/api/docs/pricing) lista Standard/short context a US$ 0,10 por milhão de tokens de entrada e US$ 0,50 de saída, menor preço entre os GPT-6 listados. Nenhum dataset comprovou melhoria de precisão em cartas ou custo real por captura. A escolha inicial anterior era `gpt-4.1-mini`.

Luna recebe `reasoning.effort=none`, suportado oficialmente, para não consumir o orçamento pequeno de saída com raciocínio médio (padrão do modelo). A tarefa permanece extração visual, com imagem em `high`, saída de até 768 tokens e timeout de 15 segundos. Overrides de modelos antigos não recebem o parâmetro de reasoning. Não houve inferência paga nem deploy na troca.

Verificação da troca: 208 testes Identity/Catalog passaram, incluindo payload Luna sem raciocínio e override GPT-4.1 sem parâmetro incompatível. API Release compilou com zero avisos/erros. Logs locais: `artifacts/openai-luna-unit.log` e `artifacts/openai-luna-api-build.log`. Isso valida protocolo/configuração com transporte simulado; não mede qualidade real do modelo.

Responses API com `input_image`, data URL JPEG, `store=false`, saída pequena e `text.format={type:json_schema,strict:true}`. Nenhuma ferramenta, pesquisa web, catálogo ou ID é enviado ao modelo. O schema fechado exige `schemaVersion=1` e sete campos com `value:string|null` e `confidence:number`: gameCode/name/collectorNumber/setCode/setName/language/variant. Todos são required; additionalProperties=false em todos os objetos. O parser compartilhado verifica novamente campos exatos, duplicatas, tamanho/tipos, confidence finita `[0,1]`, null com zero e formatos. Refusal, truncamento/incomplete, funções, mensagens extras e envelopes inválidos não viram evidência.

Prompt explícito `card-evidence-openai-v1`: somente visão, precisão antes de completude, nenhuma estimativa de preço/ID/condição, ignorar instruções impressas, não inferir set por memória ou holo por reflexo. Confiança representa legibilidade; o matcher calcula resolução e limita ambiguidades. Número preserva zeros/prefixos/sufixos/denominador; a normalização só acontece ao comparar.

Integração com cliente HTTP nomeado encapsulado em Infrastructure, sem novo pacote OpenAI. O [SDK .NET oficial](https://github.com/openai/openai-dotnet) é compatível com .NET Standard 2.0/.NET 10; exemplos oficiais de Responses/Structured Outputs consultados ainda usam supressão `OPENAI001`. Escolheu-se o protocolo documentado para evitar acoplamento à superfície experimental, mantendo schema e parsing reais. Uso de HttpClientFactory, pooled connections e redirects desabilitados; loggers HTTP removidos do cliente de inferência. Fontes: [visão](https://developers.openai.com/api/docs/guides/images-vision), [Structured Outputs](https://developers.openai.com/api/docs/guides/structured-outputs), [SDKs](https://developers.openai.com/api/docs/libraries).

## Configuração necessária

Estado operacional em 02/10/2026: usuário criou `/vaulta/production/openai-api-key` no SSM Parameter Store, `us-east-1`, tipo `SecureString`, KMS `alias/aws/ssm`. EC2 `i-0e57271cbb9d8a6b2` resolveu o parâmetro localmente com sua role e gravou somente `OPENAI_API_KEY` em `/opt/vaulta/.env.production`, preservando as demais variáveis e owner, com permissão `0600`. O valor não entrou nos retornos de ferramentas ou no repositório. É uma cópia pontual: rotações posteriores do parâmetro exigem nova sincronização. Não houve reinício, deploy ou chamada à OpenAI; o compose/imagem publicados ainda precisam receber a implementação nova.

Development **também chama GPT**, conforme orientação do usuário. Development/Production: provider openai, fallback ocr, OpenAI.Enabled=true, Nova.Enabled=false. Base/Testing: OCR, inferência desativada. Sem chave ou com OpenAI desativado não há requisição externa; retorna ao fallback local. Um ambiente local pode continuar trabalhando sem segredo real.

Definir no **servidor**, nunca no MAUI:

```text
OPENAI_API_KEY=<secret injetado no ambiente>
Scanner__Recognition__Provider=openai
Scanner__Recognition__FallbackProvider=ocr
Scanner__OpenAI__Enabled=true
Scanner__OpenAI__Model=gpt-6-luna
```

Alternativa segura de configuração da chave: `Scanner__OpenAI__ApiKey`; sem valor default. No Compose, `.env`/`.env.production` ignorados fornecem `OPENAI_API_KEY` e `SCANNER_RECOGNITION_PROVIDER`; ambos são encaminhados explicitamente. Nenhum arquivo real de segredo foi alterado. Não imprimir compose config/env ou incluir chave em chat/logs. Definir secret pelo mecanismo já existente no host.

| Opção OpenAI | Default | Limite validado no startup |
|---|---:|---|
| TimeoutSeconds | 15 | 1–60 |
| MaxConcurrency | 4 | 1–4 |
| RetryCount | 1 | 0–2 |
| RetryDelayMilliseconds | 200 | 0–1000 |
| MaxOutputTokens | 768 | 128–1024 |
| ImageDetail | high | low/auto/high |
| MaxImageEdge | 2048 | 1024–3072 |
| JpegQuality | 90 | 80–95 |

`high` foi escolhido inicialmente para pequenos textos de rodapé; nenhuma comparação high/auto/low foi medida. Modelo/detail/resize/qualidade substituíveis por configuração. Deadline cobre preparo, HTTP e retries. Singleton compartilha a capacidade entre requests; saturação retorna ao fallback imediatamente, sem fila de fotos. Cancelamento do request cancela o transporte e é propagado, sem iniciar fallback. Apenas transporte/429/5xx recebem retry finito; erro permanente ou resposta inválida não recebem tentativa de reparo. Retry-After acima de 1 segundo retorna ao fallback; nunca refaz imediatamente violando esse backoff. Um scan bem-sucedido normalmente faz uma inferência; falhas transitórias/refinamento podem elevar o total, limitado por configuração (default: no máximo duas extrações × duas tentativas = quatro HTTPs por cena).

Para OCR explícito: host `Scanner__Recognition__Provider=ocr`; Compose `SCANNER_RECOGNITION_PROVIDER=ocr`. Fallback igual ao principal é eliminado, sem duplicação. Providers aceitos openai/ocr/nova. Seleção inválida falha na validação. Quando não existe seleção explícita, `Nova:Enabled=true` preserva compatibilidade legada; configurações versionadas agora possuem seleção explícita. Nova é instanciado somente se escolhido como principal/fallback. Não existe cadeia GPT→Nova→OCR automática.

## Imagem, segurança e privacidade

Entrada até 15 MiB; identificação real de JPEG/PNG/WebP por ImageSharp, não pelo Content-Type. Dimensões 100–8000 por lado, até 40 megapixels; frames animados não são aceitos. Auto-orientação, redução somente acima da aresta configurada, JPEG com qualidade conservadora e saída máxima 3.750.000 bytes. EXIF/XMP/IPTC/metadados são removidos; resposta HTTP é limitada a 65.536 bytes e evidência JSON a 16.384 caracteres. Não se persiste foto para GPT. OCR existente usa arquivo temporário e o remove em finally.

Sem crop automático: ainda não existe uma região de carta confiável. Corte presumido poderia apagar rodapé. Preserva-se o enquadramento completo; validação de legibilidade após compressão exige fotos reais. Nenhuma foto de usuário foi enviada externamente nesta execução. `store=false` evita armazenar o objeto Responses; não é uma afirmação de Zero Data Retention. Retenção/controles da conta OpenAI devem ser conferidos antes da ativação operacional.

## Matcher, acabamento e limites canônicos

IDs só vêm de `ICardRecognitionCatalog`. Nome/número/set/idioma conflitantes excluem candidato; múltiplas impressões ficam limitadas a 0,7. Jogo/idioma desconhecido também limita a 0,7, mesmo com nome/número fortes. O modelo não escolhe VariantId e as opções são as variantes ativas cadastradas. Uma única variante segue comportamento anterior; múltiplas pedem confirmação ou preferência explícita do usuário. Condição continua UNKNOWN até declaração humana.

Catálogo/ingestão/fluxo mobile atuais continuam Pokémon. Outros TCGs necessitam dados canônicos e registro de providers por jogo; não se adicionou suporte fictício. O contrato transitório não foi inflado com artwork/ano/rarity sem uso no matcher. Denominador é preservado e comparado quando ambos os lados possuem; catálogo TCGdex normalmente guarda só o numerador. Total do set não foi inventado nem persistido por nova migration. Isso e códigos de set ainda exigem enriquecimento autoritativo do catálogo/eval; nenhuma equivalência foi fabricada.

## Pricing, JustTCG e Nova

Auditoria de referências JustTCG em `src`: somente `Vaulta.App.Core/Catalog/JustTcgPriceHistoryProvider.cs`, adapter legado que retorna listas vazias. Não havia chamada JustTCG no caminho de `/identify`; nenhuma remoção artificial foi feita. `EstimatedMarketValueBrl` permanece null na identificação OpenAI/OCR. TCGdex participa da ingestão/detalhe, nunca da tentativa de descobrir identidade da imagem.

`DailyCardMarketSnapshot`, PostgreSQL, `MarketPriceDay` (05:00 America/Sao_Paulo), lock transacional e segunda leitura foram preservados. Snapshot válido evita TCGdex/PTAX, inclusive entre requests/hosts. Concorrência sem snapshot é testada pelo fluxo real de preços; falhas não congelam preço vazio/antigo como atualizado. Nenhuma migration.

Nova continua opcional com seu adapter Bedrock, limites e autenticação IAM existentes. Seleção explícita pode usá-lo como principal ou único fallback; testes/produção padrão nunca rodam Nova junto com GPT para comparar resultados. Comparação ocorre em runs separados de dataset autorizado.

## Telemetria e performance

ActivitySource/Meter `Vaulta.Scanner`, agora registrados em OpenTelemetry da API. Métricas comuns com provider: extrações, duração, HTTP calls, retries, tokens input/output/model, matches/status e fallbacks. OCR também emite resultado/duração; correção feita após revisão independente com teste RED→GREEN. Não se loga imagem/base64, campos impressos, resposta completa, credencial ou mensagens de exceção do provider.

Spans: preparo de imagem, `scanner.openai.request` por HTTP, matcher, detalhe, leitura do cache/hit e atualização externa. Cliente mobile distingue roundtrip de identificação e detalhe. O roundtrip inclui upload + servidor; não equivale a uma medida isolada dos bytes enviados. Estabilidade é controlada pelo gate (900 ms mínimos); tempo real de câmera/estabilidade/upload isolado e UX precisa de profiling no dispositivo. Exportação/collector, dashboards e alertas de uso ainda dependem do ambiente.

## Testes e resultados executados

Sem OpenAI/Bedrock pagos; transporte externo fake, parser/schema/matcher/fluxos internos reais. PostgreSQL 17 descartável no loopback, banco novo por run. CI não requer chave: job backend força OCR/inferência desativada; casos OpenAI substituem a fronteira HTTP. Dataset real não é usado em CI.

| Verificação | Resultado | Log local ignorado |
|---|---|---|
| Identity/Catalog unitários completos | 207/207 | artifacts/openai-unit-review-final.log |
| App.Core completo | 149/149 | artifacts/openai-app-core-final.log |
| Commerce completo | 78/78 | artifacts/openai-commerce.log |
| Arquitetura | 6/6 | artifacts/openai-architecture.log |
| Integração HTTP/PostgreSQL completa | 139/139 | artifacts/openai-integration-full.log |
| Integração scanner/pricing após revisão | 6/6 | artifacts/openai-integration-review-final.log |
| API Release | 0 warnings/errors | artifacts/openai-api-build.log |
| MAUI Android ARM64 | 0 warnings/errors | artifacts/openai-android-build.log |

Comandos reais: `dotnet test <cada projeto> --no-restore --verbosity quiet -m:1`; integração com `VAULTA_TEST_POSTGRES` apontando para banco descartável novo; API `dotnet build src/Vaulta.Web.Api/Vaulta.Web.Api.csproj --no-restore -c Release --verbosity quiet -m:1`; Android `dotnet build src/Vaulta.App/Vaulta.App.csproj --verbosity quiet -m:1 -p:RuntimeIdentifier=android-arm64 -p:RuntimeIdentifiers=android-arm64 -p:AndroidPackageFormats=apk -p:RestoreIgnoreFailedSources=true`.

Cobertura inclui schema/IDs/propriedades adicionais/JSON/confidence/null, zeros/prefixos/sufixos/idioma, incomplete/refusal, limites/resizing, deadline/cancellation, 429/5xx/permanentes/retry finito/backoff, capacidade, telemetria segura, fallback e DI, revisão conservadora, uma cena sem loop, HTTP imagem→ID local, duas capturas sem cache de identidade, 10 detalhes usando snapshot sem provider e fallback OCR real. Suíte de snapshots cobre refresh concorrente, virada diária, novo host, falhas/cancelamento e avaliação da coleção. As imagens sintéticas comprovam protocolo, não precisão visual.

Falhas iniciais: tipos ainda ausentes na fase RED; sandbox não permitiu NuGet.Config e um assembly inicial ficou bloqueado pelo controle de aplicativos Windows, resolvido por rebuild local autorizado; imagens PNG estáticas têm coleção de frame metadata vazia, corrigido após reproduzir a causa. Não foram ocultados testes, reduzidas assertions ou feitas chamadas reais para contornar falhas.

Revisão independente não encontrou problema de identidade/segredo/cancelamento; apontou a observabilidade OCR, corrigida e testada. Não valida acurácia de câmera nem serviço remoto.

## Deploy e pendências reais

Nenhum deploy/push/alteração de credencial de produção. Para ativar: configurar chave server-side no mecanismo de secrets existente, confirmar acesso/modelo e políticas de retenção, executar eval autorizado, revisar precisão/latência/uso, publicar API pelo processo controlado já existente e verificar scanner autenticado. Não há migration adicional desta troca de provider. O APK compilado não atualiza a API pública.

Pendentes: dataset autorizado e verdade por Printing, calibração de falsos positivos/idioma/números pequenos/finishes, comparação model/detail e tokens/custo reais, validação física Android de câmera/montinhos/reflexos/refinamento, profiling separado da estabilidade/upload, coleta operacional/alertas, confirmação de modelo por conta e publicação controlada. [Estrutura e runner de eval](../scanner-eval.md); nenhuma imagem real foi adicionada ao Git.
