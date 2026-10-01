# ADR 002 — Separação entre CollectionEntry e CollectibleItem

- Status: Aceita
- Contexto: representar uma coleção como `PrintingId + Quantity` perde os atributos e a história de cada cópia física. Unidades equivalentes podem divergir em condição, custo, data de aquisição, observações e fotos, e poderão ter lifecycles diferentes em funcionalidades futuras.

## Decisões

### Modelo e identidade

- `CollectionEntry` é o agrupamento lógico de um usuário para `(PrintingId, VariantId?)`. Não armazena quantidade, condição, preço, data de aquisição nem fotos.
- `CollectibleItem` é uma unidade física, com lifecycle próprio. É sempre uma unidade (não tem `Quantity`) e carrega estado, condição física normalizada, preço/data de aquisição opcionais, observações e timestamps.
- Idioma pertence a `Printing` no Catalog atual; Collection não duplica esse atributo.
- `UserId` aparece no item para filtrar e autorizar eficientemente. Uma FK composta para `(CollectionEntryId, UserId)` mantém consistência com o dono do Entry.
- `quantity` no comando de adição é apenas conveniência para criar N linhas `CollectibleItem`, limitada a 100. A quantidade de leitura é contagem de unidades `ACTIVE` no PostgreSQL.
- A unicidade de Entry é garantida por índices únicos parciais separados para variante nula e não nula, em conjunto com tratamento de concorrência no comando.

### Aggregates e lifecycle

- `CollectionEntry` e `CollectibleItem` são Aggregate Roots separados. Alterar uma unidade não carrega todos os itens do agrupamento. A consistência local entre item e dono do Entry é reforçada no banco, e as regras cross-context são verificadas por ports públicos.
- Remover unidade é soft delete (`REMOVED`, `RemovedAt`); itens removidos ficam fora das queries normais. Entries sem unidades ativas são mantidos para possível reuso, mas não aparecem na coleção normal.
- Condição descreve o estado físico do item, sem grading/certificação profissional. A implementação valida uma lista fechada de sete códigos: MINT, NEAR_MINT, LIGHTLY_PLAYED, MODERATELY_PLAYED, HEAVILY_PLAYED, DAMAGED e UNKNOWN. Aliases conhecidos são normalizados; texto desconhecido é recusado. Novos códigos exigem revisão de regras e contratos.
- `Money` do SharedKernel representa aquisição opcional com decimal e currency; não há cálculo de portfolio nesta etapa.

### Fotos e fronteiras

- `CollectibleItemAsset` referencia um Asset existente e registra tipo (FRONT/BACK/DETAIL/OTHER), ordem e imagem principal.
- Collection nunca grava URL permanente nem object key do storage. Upload, ownership/status e emissão de URL temporária privada pertencem ao bounded context Assets.
- Assets anexados precisam pertencer ao usuário, estar READY, ser imagem compatível e permanecer privados. Artwork de Catalog não é foto da cópia física.
- Printing/Variant são verificados por contrato público do Catalog; Collection não consulta tabelas ou DbContext de Catalog/Assets diretamente.

### Eventos

- Domain Events registram adição em lote, atualização/remoção de unidade e alteração de vínculo de asset; o evento de lote carrega IDs necessários sem replicar dados de usuário ou grandes payloads.
- As mensagens são persistidas na tabela Outbox existente na mesma transação local. Os nomes versionados são contratos de integração potenciais, mas não há consumers fictícios nesta etapa.

## Consequências

- Duas ou mais unidades iguais reutilizam o mesmo Entry e permanecem individualmente editáveis/removíveis.
- Marketplace, Trade, Condition Check, Grading e Portfolio poderão referenciar `CollectibleItemId`; esses contextos não são criados por este ADR.
- A grade agrega no banco. Ordenações recent/quantity e páginas de itens são paginadas no banco. `sort=name` ainda carrega as entradas filtradas, busca os nomes no Catalog e ordena globalmente em memória antes de paginar; é uma limitação de escala que requer uma projeção de leitura adequada, não um comportamento garantido de paginação SQL.
- Adicionar uma nova condição, status ou tipo de foto exige extensão explícita dos códigos validados; não autoriza implementar grading profissional.
