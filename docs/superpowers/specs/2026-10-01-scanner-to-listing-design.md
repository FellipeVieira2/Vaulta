# Scanner → unidade física → anúncio — especificação P0

Data: 01/10/2026. Status: proposta especificada; reutiliza capacidades existentes sem alegar novo fluxo implementado.

## Resultado esperado

Leitura segura mostra identidade e referência de mercado automaticamente. Vender leva ao preenchimento de condição, preço e fotos da unidade. Adicionar cria exatamente uma cópia por ocorrência física. Próxima carta reabre a leitura; não é confirmação da identidade segura.

## Base existente e lacuna

ScannerSessionImporter já importa com chave scanner-session-{SessionId:N}-{ScanId:N}, congela payload antes do envio e guarda ImportedItemId. Cotação não vira preço de aquisição. Collection.AddCollectibleItems retorna IDs e já aceita IdempotencyKey.

CreateListing requer CollectibleItemId e valida propriedade. ListingPublicationService reserva a unidade e recupera publicação. Não substituir essa infraestrutura por uma segunda transação ad hoc.

A importação atual é da sessão inteira; a ação Vender de uma carta exige persistir o vínculo daquela ocorrência e recuperar a publicação após resposta perdida. Criar anúncio e adicionar fotos são chamadas distintas: o anúncio pode ficar público antes de receber fotos. O novo fluxo precisa de publicação após revisão e fotos, reutilizando recuperação existente. Isso é uma mudança de ciclo de publicação, não uma mera alteração visual.

## Estados e regras

1. Aguardando / detectando: câmera contínua, sem cotação falsa.
2. Seguro: PrintingId do catálogo e variante resolvida; apresentar Vender / Adicionar / Próxima carta.
3. Ambíguo: candidato real, conferir número/acabamento ou nova leitura; não criar unidade silenciosamente.
4. Sem cotação: identidade continua utilizável. Vender pede preço manual; adicionar permanece disponível.
5. Sem permissão/offline/erro: explicar recuperação e permitir busca; desabilitar publicação até comunicação com servidor.
6. Preparando venda: salvar rascunho da ocorrência, selecionar condição e preço, obter fotos reais frente/verso; referência pública não conta como foto da unidade.
7. Revisão: mostrar carta, variante, idioma, condição, preço e fotos; Publicar é ação explícita.
8. Publicando: uma operação lógica por rascunho; botão bloqueado, progresso e recuperação.
9. Publicado: ListingId persistido, retorno ao anúncio ou próxima carta.

Adição sem condição usa UNKNOWN, conforme CollectionRules. Venda exige condição declarada, sem UNKNOWN no fluxo proposto. Valores canônicos: MINT, NEAR_MINT, LIGHTLY_PLAYED, MODERATELY_PLAYED, HEAVILY_PLAYED, DAMAGED. Não assumir NM por reconhecimento.

Uma carta física gera ScanId persistente. Repetir request não gera cópia; retirar e reapresentar outra cópia gera outro ScanId. Nunca deduplicar apenas PrintingId: isso eliminaria cópias legítimas. Troca de conta interrompe operação; conta nova não retoma rascunho privado da anterior.

## Contratos de fronteira

Adição continua usando AddCollectibleItemsRequest com Quantity=1, AcquisitionPrice=null e chave estável por ocorrência. Guardar CreatedItems.Single().Id antes de criar anúncio.

A evolução da publicação deve introduzir rascunho que ainda não entra na consulta pública e uma operação Publish com chave idempotente por rascunho. Repetição com payload diferente deve resultar em conflito, nunca um segundo anúncio. O comando atual de criação permanece compatível até migração explícita; seu comportamento legado não deve ser alterado silenciosamente.

Não definir novo endpoint como já existente. Antes do plano de implementação desse subprojeto, fechar desenho de persistência de rascunho/receipt e transição para publishing→active, incluindo fotos/retomada. Preferir ports e módulo Marketplace existente.

## Aceite e testes necessários

- Mesmo ScanId repetido após perda de resposta retorna a mesma unidade.
- Segundo ScanId da mesma impressão cria outra unidade.
- Cotação ausente não vira zero nem bloqueia adição.
- Cotação não é preço de aquisição ou preço pedido automático.
- Usuário diferente não cria anúncio da unidade.
- Rascunho sem frente/verso não publica; imagem de catálogo não satisfaz o requisito.
- Anúncio em preparação não aparece na Home.
- Falha após reserva permite recuperação sem duplicar nem deixar unidade presa.
- Variante precisa pertencer à impressão; condição deve coincidir com unidade.
- Reabrir o app retoma a ocorrência com IDs já confirmados.

## Próximo incremento independente

Home pode avançar antes desse fluxo. Para scanner, começar pelo rascunho e teste de recuperação; depois tela Venda rápida / Revisão com componentes existentes. OCR atual permanece baseline. Bedrock exige avaliação separada.

