# Valores e variações de mercado

Nesta etapa do produto, preços de mercado, históricos, totais e variações monetárias apresentados no app devem estar em reais (BRL), com formatação pt-BR. Variações percentuais podem acompanhar a variação em reais; devem ser calculadas a partir da mesma série em BRL.

Preços de fontes em USD ou EUR precisam ser convertidos usando uma cotação válida antes da apresentação. A integração deve preservar moeda e valor originais, fonte e data do preço, além da cotação e sua data. Uma referência internacional convertida não equivale a uma cotação do mercado brasileiro.

Sem preço ou conversão válida, apresentar valor indisponível. Nunca apenas trocar o símbolo da moeda. Séries simuladas devem continuar identificadas como demonstrativas e não representam variação real de mercado.

O detalhe do scanner consulta os dados e a imagem da TCGdex e preços por variante da Cardmarket/TCGplayer. Valores em EUR/USD são convertidos pela PTAX de venda do Banco Central. A tela informa fonte, data e cotação; sem conversão válida, não apresenta preço.

Quando a Cardmarket fornece `avg7` positivo para a variante, o scanner mostra “Média de 7 dias” convertida pela mesma PTAX e identificada como referência internacional em reais. Esse campo é opcional (`averageMarketValueBrl` / `averagePeriodDays`); fontes sem média não recebem uma média estimada pelo GPT.

As comparações do scanner são com médias de 1, 7 e 30 dias fornecidas pela Cardmarket, convertidas pela mesma cotação atual. Não são uma série diária nem a diferença exata contra o preço de dias anteriores. Variantes sem essas médias não recebem comparações inventadas. O histórico sem origem real está indisponível; o adapter legado JustTCG retorna listas vazias e não consulta o provedor. Veja [integridade do histórico](design/price-history-integrity-2026-10-02.md).

A extração visual OpenAI não consulta preços. A identificação retorna candidatos canônicos e o detalhe separado usa [snapshots diários PostgreSQL](daily-market-prices.md). JustTCG não participa do caminho síncrono do scanner. A cotação diária e sua proteção concorrente foram preservadas na integração OpenAI.

## Custo de consulta — verificado em 2026-10-01

O [Starter da JustTCG](https://justtcg.com/pricing) custa US$ 19/mês mais impostos, inclui 10.000 requisições/mês (1.000/dia, 50/minuto) e permite 100 cartas por requisição. Um lote de 100 cartas conta como uma requisição. Consultar 20.000 cartas diariamente em lotes demanda aproximadamente 6.000 chamadas em 30 dias. A licença paga permite cache no servidor enquanto a assinatura está ativa.

A [busca web da API OpenAI](https://developers.openai.com/api/docs/pricing) custa US$ 10 por mil chamadas de ferramenta, além dos tokens do modelo/conteúdo. Dez mil buscas custariam US$ 100 só na ferramenta. GPT pode auxiliar identificação e explicação, mas o modelo sem fonte de mercado atual não deve fornecer um preço como cotação real. A recomendação discutida é usar dados de mercado com lotes/cache e IA para identificação/informação; não foi comprada assinatura nem substituído o provider neste trabalho.
