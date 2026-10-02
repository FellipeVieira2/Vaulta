# Scanner: evidências opcionais com Amazon Nova

Data: 02/10/2026. Escopo: Task 4 do plano `docs/superpowers/plans/2026-10-02-marketplace-scanner.md`.

## Decisão e comportamento implementado

O OCR/Tesseract continua sendo o provedor padrão. Quando `Scanner:Nova:Enabled=true`, o scanner Pokémon usa o Amazon Bedrock Converse para extrair evidências visíveis e o catálogo local para encontrar impressões. Nenhum deploy, chamada real a um modelo, mudança de permissões IAM ou configuração de credenciais foi executado nesta implementação.

Fluxo implementado:

1. Validar bytes, formato e dimensões da imagem antes de enviar ao Bedrock.
2. Extrair JSON de campos visíveis: jogo, nome, número completo, código/nome de coleção, idioma e acabamento. Cada campo carrega valor e confiança de legibilidade.
3. Validar estritamente esquema, tipos, limites e campos permitidos; atribuir versões de prompt/modelo localmente.
4. Consultar `ICardRecognitionCatalog` e usar exclusivamente o GUID da impressão do catálogo.
5. Retornar candidatos do catálogo. Evidência ausente, inválida, resposta interrompida, falha de serviço/credenciais, timeout ou ausência de candidatos retorna ao provedor OCR existente. Cancelamento do chamador é propagado.

Resultados ambíguos permanecem como candidatos para revisão, com confiança limitada a 0,7; o OCR não substitui silenciosamente essa ambiguidade. Número ou idioma confiáveis que contradizem o catálogo excluem a impressão. Prefixos alfanuméricos e zeros iniciais são preservados na evidência e normalizados para comparação; denominadores conflitantes são rejeitados quando ambos os lados os possuem. Evidência fraca de número ou nome sozinho também fica limitada a 0,7.

O modelo não recebe catálogo/IDs e não emite identidade de impressão. Campos desconhecidos, incluindo identidade inventada pelo modelo, são rejeitados. Preços, condição física e seleção automática de acabamento não fazem parte da extração. Os acabamentos retornados aos clientes são as opções reais cadastradas para a impressão; evidência visual de acabamento não transforma essas opções em confirmação da unidade física.

## Contratos e versões

`CardEvidence` e `ICardEvidenceExtractor` são contratos internos do módulo Catalog/Application. `CardEvidenceMatchResult` contém status, candidatos canônicos e versão do matcher. Os contratos públicos atuais do scanner permanecem compatíveis; as evidências são transitórias nesta implementação e não possuem nova tabela de persistência.

- Esquema JSON: `schemaVersion: 1`; sete campos obrigatórios, cada um com exatamente `value` e `confidence`.
- Prompt: `card-evidence-v1`, incluindo instrução de ignorar comandos impressos na fotografia e de não inferir campos invisíveis.
- Modelo: ID/perfil configurado em `Scanner:Nova:ModelId`. Esse valor identifica a configuração utilizada; a aplicação não afirma resolver a revisão interna de um modelo servido por um perfil.
- Matcher: `catalog-evidence-v1`.

A validação rejeita Markdown, propriedades adicionais ou repetidas, documentos acima de 16.384 caracteres, confiança fora de `[0,1]`, confiança não numérica, campos nulos com confiança positiva, texto de controle, campos excessivos e números/idiomas/acabamentos fora dos formatos permitidos. A confiança do modelo representa legibilidade e nunca substitui a comparação ao catálogo. Os escores do matcher são heurísticos conservadores, ainda sem calibração por dataset real.

## Configuração e limites

Configuração opcional, desativada por padrão:

```json
{
  "Scanner": {
    "Nova": {
      "Enabled": false,
      "Region": "us-east-1",
      "ModelId": "amazon.nova-lite-v1:0",
      "TimeoutSeconds": 15,
      "MaxTokens": 768,
      "RetryCount": 1,
      "RetryDelayMilliseconds": 200,
      "MaxConcurrency": 2
    }
  }
}
```

O deadline cobre identificação da imagem, espera por capacidade, chamada e retries. Os limites de configuração aceitos são: timeout de 1–60 segundos, saída de 128–1.024 tokens, 0–2 retries, atraso base de 0–1.000 ms e concorrência de 1–4 chamadas. As imagens são limitadas a 3.750.000 bytes, 8.000 pixels por dimensão, e JPEG/PNG/GIF/WebP. A biblioteca ImageSharp identifica o arquivo, sem aceitar apenas a declaração de formato fornecida pelo cliente.

Os retries ocorrem apenas em falha de transporte ou HTTP 429/500/503/504, dentro do mesmo deadline. Falhas permanentes, como acesso negado, não são repetidas. O cliente SDK usa `MaxErrorRetry=0`, evitando multiplicar as tentativas controladas pelo extrator. A saída interrompida por `max_tokens` ou `tool_use` não é aceita como evidência.

O pacote `AWSSDK.BedrockRuntime` está fixado em `4.0.101.7`. A autenticação usa a cadeia padrão do SDK e roles IAM da carga de trabalho; a configuração Nova não contém chaves estáticas nem implementa assinatura SigV4 manual. A permissão para inferência deve ser restrita ao modelo/perfil efetivamente escolhido; a implantação e a comprovação de acesso ao modelo ainda precisam ser feitas no ambiente alvo. Um modelo/perfil diferente precisa aceitar imagens pelo Converse na região selecionada.

## Telemetria e privacidade

ActivitySource/Meter: `Vaulta.Scanner`.

- Atividades: `scanner.evidence.extract` e `scanner.evidence.match`, com versão do prompt/modelo/matcher, status e indicação de fallback.
- Métricas: `scanner.evidence.extractions`, `scanner.evidence.duration`, `scanner.evidence.retries`, `scanner.evidence.tokens`, `scanner.evidence.matches` e `scanner.evidence.fallbacks`.
- A aplicação não registra bytes/base64 da foto, campos impressos, JSON retornado, IDs de unidades/usuários ou mensagens de exceção do SDK. O cliente desativa logging de resposta e métricas próprio do SDK.

Exportação dessas métricas/atividades depende do pipeline de observabilidade já usado pela aplicação. Logging de invocações e retenção de imagens no lado AWS não foram alterados nem verificados; precisam ser conferidos no ambiente alvo antes da ativação operacional.

## Verificação e pendências reais

Testes exercitam o parser e matcher reais, a construção do ConverseRequest e o tratamento de respostas por um cliente SDK substituído somente na fronteira de serviço externo. Eles cobrem JSON malformado/confiança inválida, campos inventados, números/idiomas conflitantes, prefixos, ambiguidade, GUID canônico, fallback, truncamento, retries, timeout, cancelamento, configuração limitada e telemetria sem texto impresso/fotos. As imagens de 1×1 usadas no teste de protocolo comprovam serialização/validação de formato, sem representar exemplos de reconhecimento de cartas.

O ciclo RED→GREEN foi observado nos conjuntos de extração/matching, chamada Converse, registro/configuração e telemetria. O teste adicional de concorrência/cancelamento protege a liberação de capacidade. A suíte completa `Vaulta.Identity.UnitTests` passou após a integração com 156 testes. Nenhuma acurácia de reconhecimento, comparação Nova versus OCR, disponibilidade de modelo por conta ou custo real foi medida.

Antes de habilitar em produção, falta um dataset autorizado de fotos reais, com verdade de referência por impressão: nomes repetidos entre coleções, idiomas, promos e prefixos, brilho/holo/reverse, desfoque, reflexos, iluminação, cortes e fundos reais. Medir falsos positivos e seleção da impressão correta, além de latência/fallback/tokens, e calibrar limiares a partir dessas medições. Não há dataset fabricado nem alegação de ganho de acurácia nesta entrega.

## Fontes consultadas em 02/10/2026

- [Amazon Nova: modelos, IDs, modalidades e suporte ao Converse](https://docs.aws.amazon.com/nova/latest/userguide/what-is-nova.html): o Nova Lite `amazon.nova-lite-v1:0` aceita texto/imagem/vídeo e Converse.
- [Converse: envio de imagem](https://docs.aws.amazon.com/bedrock/latest/userguide/conversation-inference.html): bloco `image` e bytes crus ao usar um SDK.
- [AWS SDK .NET v4: ImageSource](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/BedrockRuntime/TImageSource.html): `Bytes` é um `MemoryStream`.
- [AWS SDK .NET v4: resolução de credenciais](https://docs.aws.amazon.com/sdkfornet/v4/apidocs/items/Runtime/NRuntimeCredentials.html): resolução padrão de identidade pelo SDK.
- [Pacote oficial AWSSDK.BedrockRuntime](https://www.nuget.org/packages/AWSSDK.BedrockRuntime/4.0.101.7).

Também foram consultados o skill local `amazon-bedrock` e suas referências `model-invocation.md` e `sdk-converse-api-typescript.md`. As fontes oficiais foram consultadas pelas ferramentas AWS Core; a configuração e o protocolo .NET foram conferidos nas referências XML do pacote restaurado.
