# Unidade da coleção, anúncio e recebimento

Publicar um anúncio valida dono, printing, variante e condição contra a unidade real da coleção. A unidade mantém `ACTIVE` e continua visível para o dono, com `ListedById`; atualização, remoção e mudanças de fotos privadas ficam bloqueadas. O app mostra o aviso e esconde os controles de edição. Preço anunciado e valor de aquisição são dimensões diferentes.

## Publicação e cancelamento

O Marketplace salva primeiro `publishing`, ainda fora da vitrine/compras, reserva a unidade em Collection e então publica `active`. O índice único inclui ambos os estados. Falhas de validação cancelam a publicação; falhas técnicas preservam um registro durável para recuperação. Cancelar o anúncio é gravado antes de liberar Collection, e a liberação compara o ID do anúncio: uma repetição antiga não libera um anúncio posterior.

`ListingCollectionWorker` percorre lotes de 50 a cada 30 segundos, com escopo novo por registro, retomando `publishing` e liberando unidades de anúncios `cancelled`. Pode ser desabilitado por `Marketplace:CollectionWorkerEnabled=false`. O cancelamento/reembolso de **pedido** reativa o anúncio correspondente e mantém a unidade bloqueada, pois ela continua à venda. Pedido antigo não reativa uma venda posterior.

## Recebimento e histórico privado

Somente o comprador confirma recebimento. Rastreio entregue ou confirmação do vendedor não transferem a unidade. Orders grava `delivered`; Collection cria a posse do comprador e o registro de transferência na mesma transação em seu próprio contexto. A cópia do vendedor fica `SOLD`, preservando valor de aquisição, notas e vínculos de fotos privados como histórico. A nova unidade tem ID próprio, a mesma identidade/condição, aquisição pelo preço comprado em BRL e data do recebimento; notas e fotos privadas não são copiadas.

`item_ownership_transfers` usa o pedido como chave e índices únicos para a unidade de origem/destino. Uma trava transacional por comprador/printing/variante serializa as entradas. Repetir a confirmação verifica o registro já existente e encerra a transação, sem criar unidade adicional. Se o comprador posteriormente remover ou revender a carta, uma repetição antiga não a recria nem altera seu anúncio novo.

`DeliveredCollectionWorker` percorre pedidos entregues em lotes de 50 a cada 30 segundos para recuperar uma interrupção entre os commits de Orders e Collection. Pode ser desabilitado por `Orders:CollectionWorkerEnabled=false`. Essa recuperação não executa pagamentos. Reembolso/disputa após o envio ainda exige o fluxo de atendimento, que permanece pendente no levantamento.

## Migrações e limites operacionais

`20261001175706_TrackListingOwnership` adiciona o vínculo e o histórico. Em banco existente, vincula unidades ativas de anúncios ativos/vendidos ao vendedor correspondente e invalida versões antigas da unidade. Preserva notas, valores e fotos. Se houver mais de um anúncio ativo/vendido pendente para a mesma unidade, interrompe a migração com erro: a operação deve resolver a ambiguidade de propriedade antes de tentar novamente. Não escolhe um dono silenciosamente. Em banco novo, Collection migra antes de existir Marketplace.

`20261001175812_RecoverListingPublication` estende o índice único a `publishing`. Os novos estados não transformam a reserva de 15 minutos em prazo efetivo de pagamento. A regra de expiração continua aguardando decisão. Também permanece necessário fechar a recuperação de falhas entre criação do pedido e marcação do anúncio como vendido; os contextos não têm transação distribuída. Transferências com origem histórica ausente/incompatível ficam em erro para correção operacional, sem criar carta fictícia.

Compilação Android não confirma instalação/teste em dispositivo. Migrações e mudanças ainda não foram publicadas no ambiente real.
