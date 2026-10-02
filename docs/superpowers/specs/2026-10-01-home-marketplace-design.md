# Home Marketplace — contrato e critérios de aceite

Data da especificação: 01/10/2026. Status em 02/10/2026: API e cliente implementados e validados; integração visual MAUI é o próximo incremento. Evidência: docs/design/implementation-home-2026-10-02.md.

## Objetivo e recorte

Entregar anúncios ativos com identidade da impressão e reputação numa única resposta paginada. Reusar a rota pública /api/v1/marketplace/listings e preservar clientes atuais. Primeiro incremento: dados de catálogo, filtro textual por carta/set/número, filtro de jogo e ordenação determinística. A integração visual MAUI é um incremento posterior.

A busca por nome público do vendedor depende de uma fonte de perfil público ainda ausente no contrato inspecionado. Até esse incremento, o placeholder funcional será “Buscar carta ou set”; filtro por sellerUserId permanece. Não expor email, endereço nem tratar Guid como nome público.

## Evidência atual

MarketplaceQueries já agrega reputação em lote. ListingDto contém IDs, condição, preço, fotos e reputação, mas não CardName/SetName/Language/ArtworkUrl. IMarketplaceCatalog só oferece consultas individuais. ICatalogCollectionReader já possui GetPrintings, GetVariants e SearchPrintingIds na árvore local, inclusive modificações preexistentes do usuário: preservar e revisar essas alterações antes de reutilizá-las.

As fotos têm URLs privadas temporárias geradas por asset; não apresentar isso como um problema resolvido pelo batching de catálogo.

## Contrato proposto

Parâmetros novos opcionais: query e game. Permanecem sellerUserId, printingId, variantId, page, pageSize e sort. query é trimada; vazia equivale a ausência. game é um código de catálogo, inicialmente pokemon para o foco comercial.

ListingDto recebe ao final Printing: ListingPrintingDto? = null. ListingPrintingDto contém CardName, SetName, CollectorNumber, Language, GameCode, ArtworkUrl nullable e VariantCode nullable. Não alterar IDs nem os demais campos existentes. Não incluir cotação externa em cada card da Home: PriceBrl continua preço pedido, Currency permanece a moeda do anúncio.

Sem metadados: Printing null e UI “Dados da carta indisponíveis”. Sem artwork: placeholder identificado como tal. Sem avaliações: SellerTotalReviews = 0 implica “Sem avaliações”, independentemente de média zero. Fotos reais da unidade têm prioridade quando disponíveis; artwork do catálogo deve ser identificado como referência.

Consulta de catálogo por IDs distintos da página, uma chamada para impressões e uma para variantes quando houver IDs. Reputação segue uma chamada por página. Busca de catálogo ocorre antes de Count/Skip/Take; texto sem correspondência devolve página vazia, nunca todos os anúncios.

Ordenações: newest por CreatedAt desc e Id desc; price_asc por PriceBrl asc e Id asc; price_desc por PriceBrl desc e Id desc. Paginação mantém normalização atual (default 20, máximo 100). Estabilidade significa desempate consistente para o mesmo conjunto, não snapshot entre requisições com anúncios novos.

## Aceite

- Clientes sem novos parâmetros continuam recebendo os campos atuais.
- Carta/set/número são resolvidos pelo catálogo, sem consultar DbContext alheio.
- Somente anúncios ativos de vendedores ativos; aplicar interseção com os filtros atuais.
- Uma página com várias cartas não gera uma consulta de catálogo por anúncio.
- Dois anúncios com mesmo preço/data têm ordem reprodutível.
- Filtro de jogo é aplicado mesmo com query vazia.
- Resultado sem correspondência é totalCount 0; sellerUserId não depende de nome público.
- Falta de artwork/reputação não fabrica imagem, nota ou vendas.
- Cancelamento é propagado aos ports e ao cliente.
- Testes HTTP em PostgreSQL real cobrem filtros/ordem e contratos aditivos; testes com ports instrumentados cobrem batching.

## Fora deste incremento

Busca por nome público de vendedor, aliases de jogos, cursor/snapshot de navegação, implementação MAUI, frete, cotações por card, ofertas e Nova. Referência visual: Figma 4:2.
