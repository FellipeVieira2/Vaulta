# Scanner OpenAI — execução

Especificação: pedido anexado em 02/10/2026, integração no scanner existente.
Branch: `codex/scanner-openai`; base `2371b32`.

1. Ler fluxo, configurações, contratos, testes, pricing e documentação oficial.
2. Testar e implementar extrator OpenAI/Responses, schema estrito, imagem limitada, timeout, cancelamento, concorrência e retry.
3. Generalizar seleção de provider/fallback, preservando matcher e compatibilidade Nova legada. Testar registros e OCR.
4. Verificar resolução conservadora e orçamento de refinamento por cena; teste HTTP com fronteira OpenAI fake e PostgreSQL real, snapshot sem consulta externa.
5. Executar suítes pertinentes, builds API/Android, revisão independente e documentar ativação/eval.

## Decisões

- Sem mudanças de IDs, contratos públicos, schema EF ou pricing. Sem deploy/chamada paga.
- HTTP tipado encapsulado: os exemplos oficiais atuais de Responses .NET ainda suprimem `OPENAI001`; usar protocolo Responses documentado evita dependência de superfície experimental. JSON Schema estruturado e parser existente são reais nos testes.
- Modelo inicial `gpt-4.1-mini`, substituído a pedido do usuário por `gpt-6-luna` em 02/10/2026: opção mais barata da família 6 na tabela oficial, com imagem/Structured Outputs. Luna usa `reasoning.effort=none` para preservar o orçamento de 768 tokens; overrides antigos não recebem reasoning. Precisão real depende de eval autorizado. `high` inicial para o rodapé, sem alegar comparação medida.
- Development/Production usam OpenAI principal, conforme orientação adicional do usuário. Sem chave, fallback OCR mantém compatibilidade local; Base/Testing usa OCR. Nova só é instanciado quando selecionado. Preservar compatibilidade de `Nova:Enabled` se não existe seleção explícita.
- Sem crop automático: não existe região de carta confiável. Orientação/downscale conservador preservam imagem completa; fotos reais calibrarão limites.

## Progresso

- Leitura: fluxo atual rastreado. Configurações atuais habilitam Nova, divergindo do documento histórico. Identify não chama pricing/JustTCG. Detalhe usa snapshot PostgreSQL e advisory lock; preservar.
- Pré-flight: extrator produz CardEvidence consumido pelo matcher; seletor registra apenas um provider no ScannerService. Contratos públicos preservados. Scene gate já consome cena e permite uma segunda captura; explicitar orçamento testável.
- Etapas 2–3 concluídas: extrator/protocolo/opções, providers e compatibilidade Nova testados. RED tipos ausentes → GREEN 60 casos focados. Sem chamada paga.
- Etapa 4 concluída: sufixos preservados, jogo/idioma desconhecidos não autorizam auto-add (RED→GREEN); orçamento 1 refinamento (13 casos de gate/auto/budget). HTTP real + matcher/PostgreSQL + transporte fake passaram; preço continua separado.
- Revisão independente concluída: um achado P2 de telemetria OCR, corrigido com RED→GREEN. Nenhum achado adicional de identidade/segredo/cancelamento.
- Etapa 5 concluída: 207 Identity/Catalog unitários, 149 App.Core, 78 Commerce, 6 arquitetura, 139 integração completa; 6 scanner/pricing repetidos após instrumentação final. API Release e Android ARM64 sem warnings/errors. Relatório e dataset local ignorado documentados. Script eval teve sintaxe validada; não houve eval pago.
- Decisão operacional: não fazer deploy/push nem configurar chave real; integração está pronta para ativação server-side e avaliação autorizada. Precisão física, acesso real ao modelo, perfil de upload/estabilidade e alertas são verificações operacionais pendentes, não resultados simulados.
