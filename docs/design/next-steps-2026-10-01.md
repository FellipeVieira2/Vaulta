# Vaulta — próximos passos preparados

Data: 01/10/2026.

## Avanço nesta etapa

- Especificada Home com contrato aditivo, busca de catálogo, filtros, batching e critérios de aceite.
- Especificado scanner → unidade → preparação/revisão → anúncio, preservando idempotência existente.
- Confirmada decisão humana: frete cotado/cobrado no checkout e pago pelo comprador.
- Criado plano de implementação independente da Home; scanner e frete separados por dependências.

## Evidência nova

Catalog.SearchPrintingIds atual busca nome da carta, não set/número. Collection já usa chave idempotente no importador. Marketplace publica na criação antes de anexar fotos; o fluxo com revisão precisa de preparação não pública. Shipping atual registra remessa, não cotação de checkout.

## Arquivos

- ../superpowers/specs/2026-10-01-home-marketplace-design.md
- ../superpowers/specs/2026-10-01-scanner-to-listing-design.md
- ../superpowers/specs/2026-10-01-checkout-shipping-design.md
- ../superpowers/plans/2026-10-01-home-marketplace.md

As especificações foram publicadas no Notion sob Vaulta, páginas 21–23. Código de produção não alterado nesta etapa; nenhum teste foi declarado executado.

