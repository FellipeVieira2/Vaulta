# Checkout — frete pago pelo comprador

Data: 01/10/2026. Decisão confirmada pelo usuário: “Cotar e cobrar no checkout, pago pelo comprador”.

## Decisão e limites

Exibir carta + frete = total antes de criar cobrança. Nenhuma taxa de plataforma é transferida ao comprador. A regra atual de 8% do vendedor permanece; a base dessa taxa com frete ainda precisa ser decidida e testada, sem deduzir que todo o total seja venda da carta.

A política de quem paga/onde cobra está definida. Provedor, serviços, etiqueta, repasse do frete, seguro e devoluções ainda não estão definidos. Shipping atual registra remessa/custo após pedido; não oferece cotação de checkout. OrderSnapshotDto não possui campo separado para frete.

## Jornada

Endereço de entrega → cotação → serviço/prazo/preço → revisão do total → reservar unidade → cobrança. Cotação não reserva estoque. Sem cotação válida, o checkout informa indisponibilidade e permite tentar novamente; não cobra só a carta.

Mudar CEP, endereço, serviço, origem ou dados do pacote invalida a cotação selecionada. O servidor resolve origem do vendedor e dados do pacote; não confia em valor de frete enviado pelo cliente.

## Contrato proposto, independente do provedor

ShippingQuoteRequest referencia ListingId, endereço de destino e dados necessários do pacote resolvidos pelo servidor. QuoteId identifica resultado persistido com Carrier, ServiceCode, ShippingAmountBrl, Currency=BRL, estimativa de prazo e ExpiresAt.

CreateOrder recebe QuoteId escolhido, não um decimal de frete calculado no app. Servidor confere vínculo com anúncio/endereço, validade e preço atual antes de reservar. Pedido congela ItemPriceBrl, ShippingAmountBrl, TotalAmountBrl e serviço/endereço utilizados. TotalAmountBrl = ItemPriceBrl + ShippingAmountBrl.

Alteração de total exige nova revisão pelo comprador. Separar custo do frete, tarifa de pagamento e comissão no cálculo do repasse; não ativar cobrança até conciliação e reembolso completos.

## Reserva e pagamento

Preservar TTL atual de 15 minutos nesta especificação, sem criar um novo prazo arbitrário. Worker e tratamento de resposta tardia devem coordenar reserva, pedido e confirmação do provedor. Pedido cancelado/expirado não pode virar pago nem vender unidade novamente já entregue a outro comprador.

Pagamento confirmado tardiamente exige conciliação e tratamento financeiro definido com o provedor; não marcar refunded antes da confirmação. Estados existentes de pedido permanecem. Não há alteração de operação financeira nesta etapa.

## Aceite

- Carta R$ 100,00 + frete R$ 20,00 exibe e cobra R$ 120,00 (exemplo de teste).
- QuoteId de outro anúncio/comprador/endereço é rejeitado.
- Cotação expirada, falta de origem/pacote ou falha do provedor impedem cobrança.
- Request duplicado não gera dois pedidos/cobranças.
- Total e breakdown permanecem iguais no checkout, cobrança e pedido persistido.
- Concorrência de compradores vende a unidade apenas uma vez.
- Corrida de expiração/pagamento e estorno tem testes de integração.
- Nenhuma implantação financeira antes de validar provedor, tarifa, repasse e reembolso.

## Decisões ainda necessárias

Escolher provedor e serviços; definir embalagem/peso/dimensões; etiqueta e destino da parcela de frete; base de comissão; política de seguro, devolução e confirmação tardia. Essas decisões não bloqueiam Home nem preparação de venda.

