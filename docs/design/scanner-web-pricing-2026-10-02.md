# Scanner: preço pesquisado pelo GPT

## Pedido e diagnóstico

O usuário quer identificar pela foto e obter preço mesmo quando a edição não existe no catálogo local ou não é encontrada no JustTCG. O preço pesquisado deve aparecer brevemente e somar no total, mantendo os 80% para aceitação automática, sem inventar uma cotação.

A AWS executa `50c7e44-automatic-scan-20261002`, com readiness público HTTP 200. A captura recebida mostra Sliggoo `062/066`; consultas públicas ao TCGdex não encontram esse número. Sliggoo `067/086` existe, mas não é correto trocar a edição só porque nome/PS coincidem. O JustTCG existente é um adaptador de histórico sem consulta real; o scanner usa TCGdex para informações e preços. A chamada de visão atual não possui `web_search` e proíbe preços no prompt.

## Fluxo autorizado

Foto → extração visual → catálogo/cache quando existir → preço já conhecido da variante exata → pesquisa de mercado pelo GPT quando não houver identidade/cotação suficiente → fontes e média calculada pelo backend → soma na sessão → cache durável e atualização diária.

O catálogo continua útil para IDs canônicos e anúncios, mas deixa de bloquear a leitura e a estimativa da sessão. Uma ocorrência sem PrintingId é explicitamente pendente, sem UUID fictício. Pode mostrar preço pesquisado com fontes; coleção/venda só recebem a ocorrência após resolver a identidade canônica. Uma leitura confiável sem preço também conta como carta, com total parcial.

Pesquisa de preço é uma etapa própria usando `gpt-6-luna` e `web_search` na Responses API. Recebe a foto salva quando necessário e pode reler o número; uma correção precisa estar sustentada pela imagem e pelas referências pesquisadas, nunca apenas pelo nome. Não fornece IDs internos. O resultado mantém jogo, nome, número completo, idioma, set, acabamento e certificação. Abaixo de 80% pede revisão. Reflexo/PSA não autoriza preço de carta raw.

Preço exige URLs realmente presentes nas fontes/citações da ferramenta, valores e moedas válidos, identidade compatível e data. Não se usa memória do GPT como preço. O backend calcula a média e aplica câmbio confiável para BRL. A pesquisa tem prazo, concorrência, tamanho de imagem/resposta e quantidade de ferramentas limitados. Fotos não são persistidas no cache.

O cache inclui jogo, nome, número/total, idioma, acabamento e certificadora/nota. Nunca cruza normal/reverse ou graus. No retorno de cache, preserva a etiqueta desta captura; não reutiliza número de certificado de outro usuário. Resultados positivos ficam válidos até o próximo dia de mercado, às 05h de Brasília. Falhas não provocam repetição ilimitada. A atualização diária pesquisa somente identidades já consultadas, sem nova chamada de visão ou fotos.

JustTCG será consultado primeiro quando houver uma correspondência de impressão/variante; ausência, erro, limite ou cotação indisponível seguem para GPT com pesquisa web. A credencial foi cadastrada como SecureString em `/vaulta/production/justtcg-api-key`, sem gravar seu valor no APK ou Git. Preço de idioma/região diferente nunca deve aparecer como se fosse o mercado brasileiro daquela impressão.

O usuário confirmou que a carta da captura é número 067, e não 062. Não exigir número legível para reconhecer uma carta: combinar nome, PS, tipo, estágio, ataques, símbolo/set, ano e artwork. O prompt deve manter campos ilegíveis desconhecidos e reler o rodapé pela foto ampliada. Pesquisa pode resolver número/impressão a partir do conjunto de evidências e fontes; conflitos relevantes continuam exigindo confirmação abaixo de 80%.

## Contratos

`CardScanResultDto.MarketEstimate` traz `ScannerMarketEstimateDto`: AmountBrl, Source, CheckedAt, Confidence, Identification, Sources, IsEstimate e NextRefreshAt. Sources usam `ScannerPriceSourceDto`: Url, Title, Amount, Currency, Basis, PriceUpdatedAt.

`ScannerMarketResearchResultDto` separa Identification, Estimate opcional e Issue. Uma pesquisa pode corrigir a identificação com fontes e a foto sem encontrar preço; essa correção ainda pode resolver a impressão e obter uma cotação existente no TCGdex. Nunca fabricar cotação de zero para representar ausência.

`IScannerWebMarketResearchProvider.ResearchAsync(CardVisualIdentificationDto, byte[]? image, CancellationToken)` retorna esse resultado e faz somente pesquisa e validação das fontes. `IScannerMarketResearch.ResearchAsync(...)` acrescenta cache durável e controle entre processos. ScannerService coordena o retorno e tenta resolver novamente uma leitura corrigida, preservando pendência quando necessário.

## Verificação

TDD com fontes inexistentes/falsas, erro de moeda/câmbio, números conflitantes, certificados, limite de 80%, cache por variante e etiqueta, resultado sem PrintingId, soma e bloqueio de importação/venda indevida. Teste HTTP/PostgreSQL isolado, build API, APK assinado, revisão independente e verificação do modelo/tool em produção. Foto de carta física deve ser validada separadamente; o screenshot recebido não contém a foto original da carta.
