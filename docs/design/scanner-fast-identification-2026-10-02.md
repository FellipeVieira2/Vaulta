# Scanner: uma captura, descoberta de catálogo e preço diário

## Comportamento

No Android, presença local e estabilidade por um segundo disparam uma foto. A câmera mostra um indicador pequeno enquanto identifica, sem trocar de tela. Depois de “Foto capturada”, a pessoa pode mover o celular/remover a carta. O resultado pertence ao arquivo capturado. Novas capturas ficam bloqueadas até concluir o resultado; observações locais continuam para detectar a remoção e permitir outra cópia. Não há segunda foto automática.

## Identificação

`OpenAiCardEvidenceExtractor` usa `gpt-6-luna`, Responses API, `reasoning.effort=none`, imagem high, saída estrita v2 com jogo/nome/número/código/nome de set/idioma/variante/HP. Uma extração por foto, prazo de 15 segundos, quatro chamadas simultâneas, um retry transitório. Sem web search, preço, condição ou IDs internos. Falta de saldo é distinguida de falha de leitura e não provoca retry.

O catálogo local é consultado primeiro. Sem correspondência, `TcgDexScannerCatalogEnricher` exige evidência confiável e consulta o catálogo público do idioma: no máximo cinco detalhes e prazo total de oito segundos. Cruza nome, numerador, denominador oficial e HP legível. Não confunde identificador do provedor com código impresso. Código de set não verificável preserva candidatos com confiança máxima de 0,7, exigindo revisão da edição e impedindo inclusão automática.

`CatalogSyncService` importa apenas resultados compatíveis, usando IDs canônicos existentes quando disponíveis e a trava da sincronização normal. Não desativa outras impressões nem troca o nome canônico de uma expansão em importação parcial. Nomes traduzidos viram aliases associados à mesma expansão na tabela existente de identificadores externos. A resolução é repetida após importar; a resposta visual nunca inventa CardId/PrintingId/VariantId.

Uma leitura sem edição resolvida retorna `visualIdentification` para busca manual. Uma falha de serviço retorna `serviceIssue`; o aplicativo não apresenta toda indisponibilidade como “não consegui ler”. Acabamento incerto exige escolha entre variantes reais. O suporte canônico atual é Pokémon; outros jogos ainda exigem integração de catálogo própria.

## Preço

Identificação não consulta preço. Depois de resolver uma impressão, o detalhe consulta snapshot PostgreSQL válido; apenas ausência/expiração chama TCGdex/PTAX. A média de sete dias da Cardmarket, quando disponível para a variante, aparece separadamente como referência internacional convertida em BRL. Não é estimativa do GPT nem média de vendas brasileiras.

Produção habilita worker diário para impressões ativas anteriormente consultadas, com virada do dia às 05:00 de São Paulo e verificação a cada cinco minutos. Limites: duas atualizações simultâneas, 20.000 impressões e 120 minutos por execução. Falhas podem ser tentadas novamente sob demanda. Ver [cache diário](../daily-market-prices.md). Sem nova migration.

## Diagnóstico real e verificação

O servidor tinha apenas 102 impressões do Base Set em inglês. Uma leitura de Golisopod em português não podia resolver nesse acervo. A API OpenAI também retornou HTTP 429, `credit_balance_exhausted` / `insufficient_quota`, em teste com artwork público. Isso comprovou falta de saldo, não baixa qualidade da foto.

Após o usuário adicionar US$ 10, a chamada real respondeu HTTP 200 em 3,09 segundos: Golisopod, 140, 026/086. O primeiro teste retornou idioma por extenso (`Portuguese`) e código visual `CR`; o parser esperava ISO e o catálogo não verifica códigos físicos. Foram adicionados enum de idiomas ao schema, instrução explícita e candidatos para revisão quando o código não é verificável, com quatro regressões RED/GREEN. Fotos físicas do usuário não foram usadas nesta validação; testes com artwork não comprovam desempenho da câmera, reflexos ou holografia.

Repetição com schema de idioma corrigido: HTTP 200 em 3,17 segundos; `Golisopod`, `026/086`, `pt-BR`, HP `140`, código/acabamento desconhecidos com confiança zero. Essa saída satisfaz os tipos/formato do parser. Não é uma medida de latência do fluxo completo nem teste com foto física.

Todas as suítes passaram: Identity.Unit 222, App.Core 159, Commerce 78, Architecture 6 e Identity.Integration 141: **606 testes, zero falhas**. Logs: `artifacts/scanner-fast-<projeto>.log`. A repetição encontrou colisão de bucket no MinIO externo de teste; o fixture agora usa bucket único por execução e a suíte completa passou novamente. Integrações exercitam PostgreSQL e MinIO em rede Docker isolada, sem acesso do runner ao socket do daemon. A política de execução do Windows bloqueou assemblies locais; os testes foram executados no SDK Linux.

Build Android ARM64 Debug: zero avisos/erros, log `artifacts/scanner-fast-android-build.log`. Artefato destinado ao teste: `artifacts/Vaulta-scanner-fast-identification-2026-10-02-arm64.apk`. Instalação/câmera física e latência em condições reais continuam pendentes. Revisão independente final não encontrou P1/P2 reproduzível.

## Implantação e diagnóstico do serviço em produção

Imagem implantada pelo script existente: `4b5323e-scanner-20261002`, digest `sha256:b1fdced8bfd97745d659ea7174ebb0ce2a4cd553eda5be4591e11bbaa54ea3b0`. SSM deploy `dd2f26a4-7d03-4923-bcb2-8e00b5d1c6bf`, sucesso. Não houve nova migration; o PostgreSQL existente foi preservado.

Diagnóstico de operador usando `ScannerService` e `IScannerCardDetailsReader` dentro do contêiner implantado, com chave/configuração presentes somente no runtime e artwork público TCGdex: identificação em 5,99 segundos. Resolveu `Golisopod`, `026/86`, `Caos Ascendente`, idioma `pt`, leitura visual `pt-BR`, HP 140, confiança do candidato 0,95; variantes canônicas normal/reverse. A impressão ausente foi importada, sem criar usuário de teste ou adicionar carta a uma coleção. O acabamento não foi afirmado pela visão.

O detalhe consultou dados reais e persistiu snapshot com `fetchedAt=2026-10-02T19:49:11.7466854Z` e `nextRefreshAt=2026-10-03T08:00:00Z`. Referências internacionais convertidas por PTAX: normal/Cardmarket R$ 0,12, média de sete dias R$ 0,12; reverse/TCGplayer R$ 1,25, sem média inventada. São dados daquele momento, não uma cotação de vendas brasileiras nem preço recomendado.

Comando SSM do diagnóstico final: `e30178ca-5bd9-4098-8650-0011f3ad640a`, saída 0. A primeira tentativa não executou a inferência porque o arquivo temporário de diagnóstico não era legível pelo usuário do contêiner; a permissão do arquivo de código foi corrigida e ele foi removido após executar. O diagnóstico percorreu os serviços reais, não o endpoint HTTP/autenticação; esses contratos são cobertos pelos testes de integração.

APK SHA-256: `F6D63E0DC794DBB4F4F72A7A49FF8EC6A16E484CF65CEFEF4DDB45C0608CC9BC`. Assinaturas v2/v3 verificadas. É um build Debug para teste Android ARM64.
