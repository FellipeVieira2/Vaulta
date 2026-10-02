# Vaulta — marketplace, scanner e login

Incremento local de 02/10/2026, em validação. Branch `codex/home-marketplace`; sem commit, push ou deploy.

## Comportamento implementado

Home nativa com busca/paginação, detalhe e comparação de anúncios, anúncios por vendedor e navegação para o checkout existente. Dados e imagens vêm dos contratos reais; artwork é identificado como referência. Nome público, frete cotado e histórico de preços continuam limitados pelas fontes disponíveis.

Scanner com importação por ocorrência, ações Vender/Adicionar/Próxima, rascunho privado recuperável, condição e preço manuais, fotos reais frente/verso e revisão antes de publicar. Retry preserva identidade da ocorrência e versão original de publicação. Publicações simultâneas do mesmo anúncio foram reproduzidas em PostgreSQL e serializadas por anúncio. Referência de mercado não vira custo de aquisição ou preço pedido automaticamente.

Nova fornece evidências validadas contra o catálogo, com limites de tempo/tokens/retries e fallback OCR. Permanece desativado por padrão; não houve chamada real à AWS nem validação de precisão com dataset de fotos reais. Histórico fabricado foi removido do provedor de produção; dados indisponíveis resultam em estado vazio.

Login nativo adicional solicitado pelo usuário, em implementação com referência Figma e autenticação existente. Resultado e validação serão registrados ao concluir esse incremento.

## Revisão independente

Três problemas concretos identificados e corrigidos:

- Limpar estado privado do scanner/venda ao retornar depois de uma troca de conta ocorrida com a página oculta.
- Tratar anúncio vendido como publicação concluída, preservando o recibo e oferecendo acesso às vendas.
- Retomar as ações da última ocorrência ao voltar ao scanner, impedindo recaptura automática da mesma carta até escolher Próxima.

Também corrigido: adicionar/remover fotos preserva preço, condição e descrição ainda digitados.

## Evidência de validação até este registro

- App.Core: 113/113 aprovados, incluindo RED/GREEN da recuperação de anúncio vendido.
- Identity unitários: 156/156 aprovados.
- Commerce: 78/78 aprovados.
- Arquitetura: 6/6 aprovados.
- Rascunhos HTTP/PostgreSQL: 11/11 aprovados, inclusive corrida reproduzida e corrigida.
- Regressão de integração completa: última tentativa 137 aprovados e uma falha de configuração do banco de teste em modo de produção. Teste isolado aprovado após fornecer senha de teste; reexecução completa em andamento.
- Android: compilação anterior aprovada, zero warnings/erros; compilação incluindo detalhe e login ainda pendente.

Docker local indisponível. Usado PostgreSQL 17.11 portátil, somente loopback, com dados sintéticos isolados. Fixtures de migração usam banco exclusivo criado/removido pelo próprio helper quando a conexão externa de teste é fornecida; o padrão Docker foi preservado. Falhas de runtime/recursos do sandbox foram contornadas executando verificações sequenciais; não foram contadas como falhas de comportamento do produto.

O emulador não iniciou por falta de espaço em disco. Compilação e testes não confirmam visual, teclado, acessibilidade ou câmera em aparelho.

## Referências

- Figma: `Ey1vEJNXx1oaSZbODMamWY`; Home `4:2`, detalhe `4:85`, scanner `4:130`, preparação `24:137`, revisão `24:168`.
- Plano: `../superpowers/plans/2026-10-02-marketplace-scanner.md`.
- Cobertura e trabalho restante: `master-prompt-coverage-2026-10-02.md`.
- Backend: `scanner-drafts-implementation-2026-10-02.md`.
- Nova: `scanner-nova-2026-10-02.md`.
- Integridade de preços: `price-history-integrity-2026-10-02.md`.

Frete pago pelo comprador e cotado/cobrado no checkout está aprovado. Provedor ainda indefinido; usuário considera Correios. Não foram inventadas cotações nem realizadas operações financeiras reais.
