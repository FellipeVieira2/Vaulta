# Vaulta — contexto de produto, design e engenharia

Descoberta de 01/10/2026. Conteúdo também publicado nas 21 páginas do Notion sob Vaulta.

## 00 — Visão do produto

Marketplace de cartas colecionáveis com scanner como atalho para negociar e organizar acervo. A Home começa em anúncios; catálogo e coleção apoiam a transação. Pokémon é o catálogo inspecionado nesta execução; demais jogos são expansão proposta.

Primeira execução: descoberta, jornadas, três telas e fundações. Não inclui implantação ou implementação integral.

## 01 — Princípios de produto e UX

Marketplace primeiro; densidade compacta; decisão guiada por preço, condição, idioma e reputação. Reconhecimento automático, sem botão Identificar ou confirmação no caso seguro. Correção humana apenas na ambiguidade. Sem cotação é um estado explícito, nunca R$ 0.

Usar linguagem de colecionador. Gravação é opcional e começa mediante ação explícita.

## 02 — Marketplace

O contrato atual de anúncios permite filtrar vendedor, impressão e variante e ordenar por data ou preço. Busca textual e combinação completa de filtros da proposta ainda precisam de contrato.

Home: grid de duas colunas, artwork, preço pedido, condição, idioma e reputação do vendedor. Preferir alternativa A do MagicPath; usar comparação da alternativa B no detalhe. Preços e pessoas dos protótipos são ilustrativos.

Aceitação futura: paginação estável, estados vazio/erro/carregamento e resposta enriquecida sem chamadas por card.

## 03 — Scanner

Atual: OCR Tesseract e busca aproximada; ContinuousRecognition usa critérios de número, score e margem e exige variante definida. Isso não comprova Bedrock implementado.

Alvo: câmera contínua → identificação segura → cotação → Vender / Adicionar / Próxima carta. Número ilegível oferece nova leitura ou catálogo; não inventar variantes. Repetição da mesma impressão precisa distinguir nova cópia física.

Vender requer unidade pertencente ao usuário; criar vínculo de acervo antes de publicar, com operação idempotente. Condição deve ser informada pelo proprietário. Microfone somente ao iniciar gravação.

Figma inclui sucesso, espera, detecção, ambiguidade e sem cotação.

## 04 — Catálogo

Card e Printing são identidades diferentes; preservar PrintingId, set, número, variante e idioma. TCGdex fornece referências e artwork do catálogo. Artwork não comprova condição nem substitui fotos da unidade anunciada.

Exemplo verificado: Charizard ex sv03-223, 223/197, Obsidian Flames; corrigida associação antiga com 151/165.

## 05 — Coleção

Coleção organiza unidades físicas e seus estados; não deve se confundir com anúncios ou cotações. Scanner pode adicionar uma cópia com identidade confirmada e condição ainda não declarada.

Histórico do valor do acervo exige consulta de séries e estratégia para itens sem preço. Não apresentar gráfico de patrimônio como já implementado.

## 06 — Precificação

Código local em 3901315 persiste snapshots diários BRL no primeiro acesso do dia de mercado, com fronteira às 05:00 America/Sao_Paulo. Não é coleta agendada diária universal.

Separar preço pedido, referência de mercado, data da cotação, fornecedor e moeda. Cotação ausente é null; falha de atualização não deve rotular informação antiga como atual. Séries e gráficos ainda precisam de consultas.

Documentação registra rotas novas em produção retornando 404; deployment não foi realizado nesta execução.

## 07 — Pedidos e checkout

Estados atuais: pending, paid, shipped, delivered, cancelled, refund_pending, refunded. Não introduzir Completed ou AwaitingShipment como contratos existentes.

Compra reserva a unidade; entrega depende de confirmação do comprador. Reembolso antes do envio depende de confirmação do provedor. Após envio, atendimento e política ainda precisam de definição.

Frete não está incluído no total cobrado atual. Expiração e liberação da reserva precisam de decisão operacional, apesar do TTL existente.

## 08 — Ofertas

Não foi encontrada uma jornada operacional completa de ofertas. Fazer oferta é expansão P1 proposta e foi removido do CTA principal da tela refinada.

Antes de desenhar/implementar: regras de mínimo, validade, contraproposta, reserva, aceite e concorrência com compra direta.

## 09 — Vendedores

Reputação disponível agrega avaliações recebidas como vendedor: média e quantidade. Sem avaliações deve ser exibido explicitamente; não inferir nota ou quantidade de vendas.

Comparar vendedores somente para mesma impressão, variante e idioma, mantendo diferenças de condição e preço. Frete depende de cotação ainda indisponível.

## 10 — Pagamentos

Regra local: taxa de plataforma de 8% somente do vendedor, acrescida das taxas efetivas de cobrança/Pix do Asaas. Repasse direto a Pix verificado após confirmação de recebimento e liquidação.

Carteira/saque estão depreciados com 410; não propor saldo disponível como jornada vigente. Fluxo financeiro operacional depende de configuração e validação do provedor; não foi ativado nesta execução.

## 11 — Frete e entrega

Política de frete ainda bloqueia checkout completo: quem cotará, quem pagará, quando cobrar e como apresentar prazo. Tela mostra dependência explicitamente sem valor inventado.

Definir rastreio, comprovação, perda, devolução e atendimento antes de vender a jornada como pronta.

## 12 — Design system

Fonte visual: Figma existente, dark premium, Inter, azul/roxo. Criadas fundações com aliases de cores, spacing, raio e alvo de toque; estilos compactos e componentes com propriedades.

Reuso: botões, busca, card, navegação, resultado do scanner; estados loading/empty/partial/error/offline, condição, reputação e referência de preço disponíveis na biblioteca.

Há divergência com MAUI: BrandPrimary ciano no código. Alinhar tokens em mudança futura explícita. Verificação visual em 390×844; acessibilidade nativa e escalonamento de fonte ainda precisam de teste.

## 13 — Arquitetura técnica

Preservar .NET 10 MAUI, PostgreSQL/EF Core e monólito modular. Módulos inspecionados: Identity, Catalog, Collection, Assets, Marketplace, Orders, Payments, Wallets, Shipping, Reviews.

Não criar frontend React de produção: React nesta execução serve somente aos protótipos isolados MagicPath. Integrações de reconhecimento devem permanecer atrás de ports existentes.

## 14 — AWS e IA

Amazon Nova 2 Lite documenta entradas texto/imagem/vídeo com resposta textual; Converse oferece interface de conversação. São capacidades documentadas, não integração atual do Vaulta.

Proposta: extrair evidências visuais e resolver identidade pelo catálogo, nunca aceitar PrintingId inventado pelo modelo. Embeddings são hipótese posterior.

Antes de implementação: benchmark com números/idiomas/acabamentos, escolha de região/modelo, latência, custo, limites, retenção e autenticação servidor. Credenciais AWS nunca no app.

Fontes: https://docs.aws.amazon.com/ai/responsible-ai/nova-2-lite/overview.html e https://docs.aws.amazon.com/nova/latest/nova2-userguide/using-converse-api.html

## 15 — Segurança e privacidade

Fotos da unidade e frames da câmera devem ter finalidade, retenção e exclusão definidas. Gravação opcional; permissão de microfone contextual. Não enviar segredos ao app ou protótipos.

Validar propriedade e autorização no servidor para acervo/anúncio/pedido. Separar imagens públicas de catálogo de mídia privada. Apenas assets públicos e código de protótipo foram enviados ao MagicPath com autorização explícita.

## 16 — Analytics

Plano proposto, sem telemetria criada: busca → detalhe → início de compra → pagamento → entrega; scanner → identificado → ação vender/adicionar; leitura ambígua/sem cotação/erro.

Medir latência até resultado, precisão por set/idioma e abandono. Evitar fotos, OCR bruto e dados pessoais nos eventos. Definir consentimento, retenção e fonte de verdade antes de instrumentar.

## 17 — Decisões

Decisões desta execução: A como base da Home; comparação compacta no detalhe; scanner automático e cotação imediata; gravação opt-in; artwork público identificado; estados sem preço; visual Figma preservado; arquitetura modular mantida.

Adiados: outras telas P0, oferta P1, Nova/embeddings, política de frete e implementação. Não houve deploy, commit ou PR. Mudanças de código preexistentes do usuário foram preservadas.

## 18 — Roadmap

P0 seguinte: contrato da Home enriquecido e paginado; scanner seguro com identidade, preços e vínculo físico; venda com condição/fotos; resolver frete e reservas; depois checkout e pedidos.

P1: ofertas e negociação, séries do acervo e expansão de jogos. Nova depende de avaliação comparativa com baseline OCR. Evitar ativar serviços antes das decisões e testes necessários.

## 19 — Status de implementação

Inspeção local: HEAD 3901315; alterações preexistentes não alteradas. GitHub consultado também mostrou referência 90da93b; não inferir sincronização ou deploy.

Entregues nesta execução: protótipos MagicPath com build concluído, jornadas tldraw, refinamento das três telas e componentes Figma, documentação Notion e relatório local. Telas representam intenção visual; funcionalidades novas não foram conectadas ao MAUI.

## 20 — Gaps e inconsistências

Prioridades: frete/checkout, reserva expirada, vínculo de unidade scanner→venda, contrato enriquecido da Home, deploy de preço, consulta histórica, alinhamento de tokens, estados e acessibilidade, avaliação Nova, governança de mídia.

Cada lacuna deve ter comportamento atual e esperado, evidência, dependência e marco. Responsáveis permanecem sem atribuição quando não definidos pelo usuário.
