# Marketplace nativo e scanner para venda — plano de implementação

> **For agentic workers:** Use superpowers:executing-plans para executar e verificar cada entrega. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ligar a Home ao marketplace real e permitir carta física → reconhecimento automático → unidade → rascunho privado → fotos/revisão → publicação recuperável.

**Architecture:** Evoluir o monólito modular e os ports existentes. Reutilizar ScannerSession, importação idempotente, Listing e recuperação de publicação. Não criar HTTP ou regras comerciais nas Views.

**Tech Stack:** .NET 10, MAUI, App.Core, PostgreSQL, EF Core, Testcontainers.

**Spec:** docs/superpowers/specs/2026-10-01-scanner-to-listing-design.md; docs/superpowers/specs/2026-10-01-home-marketplace-design.md; prompt mestre anexado.

## Global Constraints

- Reconhecimento seguro automático; ambiguidade real permite escolha. Sem botão Identificar no caminho normal.
- Condição declarada e fotos reais frente/verso antes de publicar. Cotação não vira aquisição ou preço pedido automaticamente.
- Contratos legados preservados. Rascunhos privados não entram na Home nem podem ser comprados.
- Conta que iniciou a sessão controla sua retomada. ScanId distingue cópias físicas; retry usa chave estável.
- Manter alterações locais anteriores. Sem deploy, migração de banco compartilhado ou operação financeira real nesta implementação.
- Autorização humana de 02/10: seguir todo o prompt sem novas confirmações de etapas; esta autorização substitui os handoffs de aprovação das skills.

## Review Focus

- Resposta perdida após criar unidade/rascunho/publicar: mesma ocorrência e mesmos IDs no retry.
- Mudança de conta durante chamada: nenhum rascunho privado retomado por outra conta.
- Dois rascunhos da mesma unidade e concorrência de publicação: apenas um anúncio ativo, reserva consistente.
- Fotos de catálogo, assets incompletos/alheios, condição UNKNOWN e variante inválida: publicação rejeitada.
- Home vazia/offline e mudanças rápidas de busca/filtro: estado recuperável, resultados antigos não substituem recentes.

## Task 1: Rascunho e publicação no Marketplace

**Files:** módulo Marketplace Domain/Application/Infrastructure/Contracts, MarketplaceEndpoints, testes Commerce/Integration.
**Interfaces:** Produz contratos aditivos para criar/ler/editar rascunho privado, anexar fotos, publicar e recuperar por chave idempotente. Consome ports Collection/Assets existentes.

- [ ] Definir contratos e persistência incremental, com versão/ownership e recuperação durável.
- [ ] Escrever testes de privacidade, validação, idempotência e concorrência; observar falha.
- [ ] Implementar rascunho → publishing → active sem alterar criação legada.
- [ ] Executar testes e conferir migration/snapshot, erros e recuperação.

## Task 2: Home nativa

**Files:** Views/ViewModels marketplace novos, componentes reutilizáveis, Resources e registro/navegação MAUI; controller testável em App.Core e testes.
**Interfaces:** Consome BrowseListingsAsync e ListingDto.Printing já implementados. Não modifica os contratos do scanner.

- [ ] Consultar Figma Home 4:2 e componentes/tokens existentes.
- [ ] Testar carregamento, paginação, filtros e cancelamento sem resultados obsoletos.
- [ ] Implementar grid virtualizado, busca/filtros, anúncio e navegação Vender.
- [ ] Compilar Android e verificar limites/estados/acessibilidade disponíveis no ambiente.

## Task 3: Ocorrência do scanner e venda nativa

**Files:** App.Core/Catalog e Marketplace, ScannerSessionPage, telas/ViewModels de preparação/revisão e DI.
**Interfaces:** Consome ScannerSession e contratos do Task 1; produz ação por ScanId persistente e publicação recuperável.

- [ ] Testar importação de uma ocorrência, retries, cópias distintas, condição e mudança de conta.
- [ ] Implementar persistência por ocorrência com IDs de unidade/rascunho/anúncio.
- [ ] Ligar Vender/Adicionar/Próxima carta ao resultado automático; preço manual e fotos reais, revisão e publicação explícita.
- [ ] Testar cliente/recuperação e compilar Android, preservando gravação opcional.

## Task 4: Evolução do scanner e cobertura do prompt

**Files:** documentação de cobertura, scanner/provider, testes e configuração conforme evidência.

- [ ] Conferir arquitetura OCR/matcher atual e integração Bedrock/Nova com documentação AWS atual.
- [ ] Implementar capacidades independentes que não exijam credenciais/provedor externo ou decisão financeira não definida.
- [ ] Mapear P0/P1, checkout/frete, observabilidade e segurança por estado real; não apresentar simulações como implementação.
- [ ] Executar revisão independente, suites relevantes e atualizar Notion, relatório e pendências concretas.

## Decisões de execução

Ruling: tarefas 1 e 2 são subsistemas independentes e podem ser delegadas conforme superpowers:dispatching-parallel-agents, com arquivos separados; o executor principal cuida da integração do scanner e da verificação final.

Ruling: permanecer na branch codex/home-marketplace atual preserva código de scanner/catalog já modificado pelo usuário; não mover/resetar alterações para criar isolamento incompleto.

Ruling: frete pago pelo comprador está aprovado, mas cotação real exige provedor/origem/pacote/repasse. Preparar contratos seguros quando possível; não inventar cotação ou ativar cobrança incompleta.

## Task 5: Detalhe nativo e comparação de vendedores

**Files:** App.Core/Marketplace controllers novos e testes, Views/ViewModels nativos de detalhe/anúncios do vendedor, registro/navegação MAUI aditivos.
**Interfaces:** Consome GetListingAsync, BrowseListingsAsync por PrintingId/VariantId/SellerUserId e ICatalogClient.GetPrintingAsync para anúncios legados. Reusa a entrada do checkout existente sem realizar cobrança nesta implementação.

- [ ] Consultar Figma detalhe 4:85 com contexto de alta fidelidade e screenshot.
- [ ] Testar carregamento, anúncio ausente, cancelamento obsoleto, falhas de comparação e metadados ausentes.
- [ ] Implementar fotos reais frente/verso, referência de catálogo identificada, preço/condição/reputação e comparações compatíveis.
- [ ] Manter frete, oferta, referência/histórico e nome público ausentes explicitamente limitados pelos contratos reais.
- [ ] Compilar Android integrado e documentar os testes, limites e navegação para checkout.

## Task 6: Login nativo solicitado em 02/10

**Files:** LoginPage/LoginViewModel, componentes de autenticação, App.Core/Identity, navegação e fontes existentes.
**Interfaces:** Preservar IAuthenticationService e endpoints reais de login/cadastro. Não inventar autenticação social ou envio de recuperação de senha.

- [ ] Usar Product Design e Figma para criar referência editável com tokens e componentes da Vaulta.
- [ ] Implementar formulário com labels, senha visível/oculta, validação, carregamento e erro recuperável.
- [ ] Ligar startup, logout, onboarding e cadastro à nova tela, mantendo restauração de sessão.
- [ ] Testar autenticação/concorrência apropriadas, compilar Android e registrar limites de validação visual.

Ruling: o usuário pediu explicitamente o redesign de login e continuidade sem novas confirmações; reutilizar a direção visual existente da Vaulta, documentar a referência no Figma e seguir para implementação.

## Task 7: Validação automática adicional do scanner

**Files:** política testável App.Core/Catalog e captura contínua existente.

- [ ] Testar gates de número, confiança, diferença entre candidatos e divergência entre duas leituras.
- [ ] Antes de pedir escolha, tentar no máximo uma nova captura da mesma cena estável; cancelar se a carta/conta mudar.
- [ ] Nunca resolver acabamento ambíguo automaticamente; preservar escolha real e impedir repetição ilimitada.
- [ ] Registrar custo máximo adicional de uma requisição por ocorrência incerta; validar no build e no aparelho quando disponível.
