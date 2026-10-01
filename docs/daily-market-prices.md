# Atualização diária de preços

O scanner e a avaliação da coleção compartilham o mesmo leitor de preços. A primeira consulta de uma impressão de carta salva informações, valores por variante já convertidos em BRL, comparações e origem/data do câmbio no PostgreSQL. As próximas consultas reutilizam esse registro, inclusive após reiniciar a API.

O dia de mercado começa às **05:00 em America/Sao_Paulo**. Uma atualização no dia 1 às 10h vale até as 5h do dia 2; uma consulta no dia 2 às 7h atualiza. Consultas antes das 5h pertencem ao dia de mercado anterior. Não há consulta agendada de cartas que ninguém acessou, nem preenchimento de dias sem consulta.

Uma trava transacional por impressão coordena consultas simultâneas de diferentes processos. Depois de esperar, o leitor verifica novamente o registro salvo; a consulta ao provedor não se repete quando outra requisição já concluiu a atualização. Cancelamento libera a trava e desfaz a gravação.

A tabela `catalog.daily_card_market_snapshots` mantém um registro por impressão/dia, com JSONB e FK para a impressão. Preços anteriores e respectivas conversões permanecem disponíveis para futura leitura histórica. Isso ainda **não entrega um gráfico do histórico do acervo**: quantidade, entrada/saída e custo também precisariam de histórico próprio.

Uma carta válida sem cotação é registrada como sem preço, nunca como zero. Falha HTTP/JSON, identidade divergente ou câmbio ausente para uma variante com cotação não congela uma resposta vazia pelo resto do dia. Uma nova consulta pode tentar novamente. Um preço do dia anterior não é apresentado como atualizado após uma falha. A resposta inclui `fetchedAt` e `nextRefreshAt` quando foi persistida.

Migration: `20261001220738_DailyCardMarketSnapshots`. Aplicar no deploy controlado da API; nenhuma alteração foi aplicada ao banco de produção por este trabalho. O APK existente aceita os campos opcionais novos e não precisa ser recompilado apenas por esta mudança.

Validação: seis casos de horário e três testes de conversão/informação passaram; quatro testes HTTP/PostgreSQL exercitaram concorrência, novo host, virada às 5h, histórico, erros, falta de preço, cancelamento e migration sincronizada. O teste de avaliação da coleção também passou. Resultado final: `artifacts/scanner/daily-market-tests-final.log`. Uma seleção inicial incluiu dois testes antigos que tentam criar seus próprios containers e falharam por ausência de acesso ao Docker dentro do runner; os cinco testes pertinentes passaram nessa execução e novamente na seleção final sem esse requisito.
