# Home Marketplace — implementação concluída

Data: 02/10/2026. Plano: docs/superpowers/plans/2026-10-01-home-marketplace.md.
Branch: codex/home-marketplace. Alterações locais, sem commit, push, PR, merge ou implantação.

## Resultado

Concluídas as duas tarefas do plano: consulta pública de anúncios enriquecida e compatibilidade do cliente App.Core.

A rota GET /api/v1/marketplace/listings aceita query (carta, set ou número) e game, além dos filtros anteriores. Aplica a interseção dos filtros antes de contar/paginar. Texto sem correspondência devolve zero anúncios. A ordenação usa Id como desempate consistente.

ListingDto recebe Printing opcional com nome da carta, set, número, idioma, jogo, artwork e variante. O preço continua sendo o valor pedido. Impressões/variantes são consultadas em lote por IDs distintos da página; reputação já segue em lote. Metadados ausentes preservam o anúncio com Printing null.

O cliente acrescenta BrowseListingsAsync e preserva ListActiveListingsAsync, seus defaults e leitores de JSON sem Printing. Cancelamento e codificação de texto/Unicode foram testados.

Exemplo:
```text
GET /api/v1/marketplace/listings?query=223%2F197&game=pokemon&page=1&pageSize=20&sort=price_asc
```

## Verificação

| Suíte | Resultado |
|---|---|
| Vaulta.Identity.IntegrationTests | 127/127 |
| Vaulta.Identity.UnitTests | 112/112 |
| Vaulta.App.Core.UnitTests | 63/63 |
| Vaulta.ArchitectureTests | 6/6 |
| Total | 308 testes, zero falhas ou testes ignorados |

Os testes novos do backend foram observados falhando antes da implementação: 8 falhas e 3 passes; cobrem metadados, ausência de match, jogo, interseção, ordem, ausência de catálogo e batching. Os testes do cliente falharam inicialmente pela ausência de método/contrato. Todos passaram após a implementação.

A suíte completa revelou problemas preexistentes na configuração dos testes de migração: falta de segredo JWT temporário e contagem fixa de migrações antiga. Corrigidos somente no host/teste: segredo aleatório, workers desabilitados e comparação do conjunto exato de migrações de quatro módulos. Nenhuma migration ou configuração de produção foi alterada. Um teste de scanner recebeu correção equivalente de Assert.Single para satisfazer o analisador xUnit, mantendo a asserção.

Revisão independente de código: nenhum problema crítico ou importante. Melhoria não bloqueante registrada para manutenção: ampliar o teste de batching com uma segunda impressão/variante distinta; o código já reúne todos os IDs distintos. Escala de produção não foi medida.

## Preservação e decisões

Alterações de scanner/catalog preexistentes foram preservadas. CatalogQueries recebeu somente ampliação da busca de coleção usada pelo port de Marketplace e normalização do filtro de jogo. O baseline do cliente tinha 58 testes aprovados.

Foi criada branch no checkout atual para preservar os ports e demais mudanças locais do usuário; não houve stash, reset ou cópia incompleta para outro checkout. As chamadas de assets por foto seguem o mecanismo anterior e não foram transformadas em batching de mídia.

## Limites e próximo incremento

Este plano entrega API e cliente, sem ligar o novo grid/busca à tela MAUI. Busca por nome público de vendedor, frete, publicação com rascunho/fotos, preços externos e Nova permanecem nos subprojetos definidos nas especificações.

O passo seguinte é integrar a Home nativa ao contrato validado, com estados e componentes do Figma. Scanner para venda depende de preparar/revisar antes de publicar. Frete já tem decisão humana (cotado/cobrado no checkout, comprador paga); provedor e repasse ainda precisam de definição.

