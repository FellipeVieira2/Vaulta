# Vaulta — primeira execução: auditoria e entrega

Data: 01/10/2026 (America/Sao_Paulo). Escopo: descoberta e design das três telas existentes; nenhuma alteração no código de produção, implantação, commit ou PR.

## Entregas

- [Figma: telas e design system](https://www.figma.com/design/Ey1vEJNXx1oaSZbODMamWY?node-id=0-1)
- [Notion: contexto Vaulta, 21 páginas](https://app.notion.com/p/3ed6ef6d792d81f08428c0c3681f4e34)
- [Notion: 10 lacunas priorizadas](https://app.notion.com/p/2d7bc704a942405ca248f5d5d12018a2)
- [tldraw: IA, scanner, venda, compra e arquitetura](https://www.tldraw.com/f/tKrHPaOm1vyg9K9zwAUzA)
- MagicPath: projeto privado “Vaulta — Exploração Home Marketplace”, ID 456589685570015232. A Grid de anúncios, B Feed comparativo, C Descoberta e comparação; builds concluídos e previews nativos inspecionados. Upload autorizado explicitamente pelo usuário.
- Inventário de IDs, tokens, estilos e páginas: artifact-ledger.json. Especificação de produto local: product-context.md.

## O que foi analisado

README; docs/architecture.md; docs/business-gap-audit.md; docs/design-system.md; docs/daily-market-prices.md; consultas e contratos de Catalog, Scanner e Marketplace; regras de Orders e pagamentos; Colors.xaml; scanner contínuo/OCR; arquivo Figma existente e documentação oficial AWS/Nova via AWS Core. Repositório local HEAD 3901315; consulta GitHub também retornou referência 90da93b. Não inferir atualização de produção pelo estado local.

A aplicação é MAUI .NET 10 com monólito modular, EF Core e PostgreSQL. A base atual usa Tesseract e busca aproximada; Nova/Bedrock e embeddings permanecem propostas. Alterações preexistentes do usuário foram preservadas.

## Auditoria das três telas

| Tela | Problema observado | Refinamento |
|---|---|---|
| Home | Placeholders, identidade de carta inconsistente, navegação recortada, ausência de reuso efetivo | Grid compacto com quatro artworks, preço pedido, condição, idioma e reputação; rodapé fixo; instâncias do card, busca, ícones e navegação |
| Detalhe | Arte sem proveniência clara, referência confundida com preço, ações e histórico sem contrato suficiente | Identidade 223/197 correta; preço pedido separado de referência; reputação do vendedor; comparação de ofertas equivalentes; dependência de frete/histórico explícita |
| Scanner | Interface técnica centrada em confiança OCR e composição com controles fora do quadro | Resultado automático, cotação imediata, Vender / Adicionar / Próxima carta e gravação opcional; cinco estados desenhados |

Mantidos os IDs dos três frames existentes: 4:2, 4:85 e 4:130. Componentes existentes foram evoluídos. Os arquivos *-before.png preservam evidência anterior; *-after.png registram a revisão final.

## Decisões

Home A como base, usando comparação compacta da alternativa B no detalhe. C explora descoberta, mas suas três colunas reduzem legibilidade no celular. Essa seleção é decisão de design desta execução, ainda sem teste com usuários.

Visual dark, Inter e roxo da fonte Figma preservados. MAUI usa ciano em BrandPrimary: a migração precisa ser explícita. Componentes usam aliases de cores, espaçamento, raio e alvo de toque; seis estilos compactos. Biblioteca inclui botões, busca, card, navegação, ícones, resultado do scanner, condição, reputação, referência e notices loading/empty/partial/error/offline. Reuso conferido: 14 instâncias na Home, 4 no detalhe e 6 no scanner de sucesso.

Artwork TCGdex é referência do catálogo; não representa foto nem condição da unidade à venda. Os protótipos identificam preços e vendedores como ilustrativos. Charizard ex é sv03-223 / 223/197 / Obsidian Flames. Não foi criada uma variante Reverse Holo fictícia para esse exemplo; ambiguidade oferece conferir impressão ou refazer leitura.

Scanner normal não exige Identificar nem confirmação. Identidade segura não equivale à propriedade física: vender requer unidade do usuário, vínculo idempotente e condição declarada. Gravação inicia por escolha explícita; microfone deve ser contextual.

Preço ausente deve permanecer null, com Sem cotação. Snapshots locais são criados no primeiro acesso do dia de mercado, fronteira 05:00 America/Sao_Paulo; não constituem coleta agendada universal. Histórico de mercado/acervo depende de consultas adicionais.

Pedidos preservam pending, paid, shipped, delivered, cancelled, refund_pending e refunded. A taxa local é 8% do vendedor mais taxas efetivas do provedor; repasse direto Pix após recebimento e liquidação. Não restaurar carteira/saque depreciados.

## Gaps, dependências e prioridade

| Prioridade | Lacuna | Dependência / próximo resultado |
|---|---|---|
| P0 | Frete e total do checkout | Política, cotação, prazo e integração; total claro antes do pagamento |
| P0 | Expiração/liberação de reserva | Orders + provedor + idempotência e concorrência |
| P0 | Scanner → unidade → anúncio | Collection / Marketplace e autorização de propriedade |
| P0 | Home enriquecida e busca | Contrato paginado com impressão, artwork, reputação e índices |
| P0 | Novas rotas de cotação | Publicação e validação no ambiente alvo; documentos registram 404 |
| P0 | Divergência Figma / MAUI | Migração de tokens e verificação de contraste |
| P0 | Privacidade e acessibilidade | Política de mídia; leitor de tela, fonte ampliada e dispositivos |
| P1 | Séries de preço/acervo | Queries históricas e tratamento de ausência |
| P1 | Nova/embeddings | Benchmark de precisão, latência, custo e segurança antes da adoção |
| P1 | Ofertas | Regras e estados antes de implementar Fazer oferta |

Responsáveis não foram inventados. Os itens permanecem abertos na base Notion.

## Verificação e limites

Três builds MagicPath concluídos com assets normalizados; previews A/B/C inspecionados. No Figma, screenshots finais das três telas examinados após corrigir a persistência dos assets. Inspeção estrutural confirmou sete frames 390×844, rodapés dentro do quadro e nenhum filho direto ultrapassando o conteúdo fixo. Home: quatro imagens; detalhe: uma; scanner: duas. Originais e revisão estão neste diretório.

Não houve teste ponta a ponta de compra, pagamentos ou câmera real, nem teste de leitor de tela/fonte ampliada. Os estados são design, não prova de comportamento implementado. Não foram executados testes de produção porque seu código não foi alterado. O código React é exploração isolada no MagicPath; não substitui a aplicação MAUI.

## Próxima execução recomendada

1. Resolver frete e reserva para tornar o checkout especificável.
2. Definir contratos e critérios de aceite da Home e scanner → unidade → venda.
3. Alinhar tokens e validar o scanner nativo com dados reais.
4. Avançar para as demais telas P0 sobre essas decisões; só então implementar incrementos e testes adequados.


