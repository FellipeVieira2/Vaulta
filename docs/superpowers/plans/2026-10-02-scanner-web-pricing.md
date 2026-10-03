# Scanner Web Pricing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Obter preço pesquisado com fontes pelo GPT e somar leituras confiáveis sem depender de um cadastro prévio no catálogo.

**Architecture:** Manter extração visual rápida e adicionar pesquisa web somente quando a identidade/cotação não estiver disponível. Cache durável separado guarda referências de mercado; o app suporta ocorrências pendentes sem autorizar importação/venda com IDs inexistentes.

**Tech Stack:** .NET 10, Responses API, EF Core/PostgreSQL, MAUI Android.

**Spec:** `docs/design/scanner-web-pricing-2026-10-02.md`.

## Global Constraints

- Modelo `gpt-6-luna`, autoaceitação em `0.8` inclusive; nenhuma chave no APK/Git.
- Web com fontes verificadas, tamanho/prazo/concorrência limitados, sem valores inventados ou UUID fictício.
- Não cruzar acabamento, idioma, impressão ou certificação. Fotos não são persistidas.
- Valor aparece brevemente, soma e segue para a próxima carta; revisão só quando incerto.

## Review Focus

- Preço de outra impressão por erro no número; exigir relação com a foto e fontes.
- URL inventada/memória sem web; não produzir cotação.
- Cache de certificação retornando etiqueta alheia; preservar a captura atual.
- Ocorrência pendente enviada à coleção/venda; exigir identidade canônica.
- Chamadas repetidas/concorrentes e timeout; respeitar cache e limites.

### Task 1: Pesquisa web

Files: `Catalog.Contracts/ScannerModels.cs`, `Catalog.Application/ScannerPorts.cs`, infraestrutura `MarketResearch/`, `tests/Vaulta.Identity.UnitTests/ScannerWebMarketResearchTests.cs`.

- [ ] Fixar os contratos comuns, incluindo ScannerMarketResearchResultDto para manter identificação corrigida mesmo sem cotação.
- [ ] Escrever testes RED de resposta com/sem web e fontes, confiança, moeda, foto e conflitos.
- [ ] Implementar cliente Responses com `web_search`, fontes, schema estrito e média/câmbio no backend; permitir número ausente e reler número pela imagem, sem aceitar preço de impressão conflitante.
- [ ] Implementar consulta real ao JustTCG com chave somente server-side, correspondência de idioma/impressão/variante e fallback web em ausência/erro. Usar documentação oficial atual em `https://justtcg.com/docs/swagger.json`; não usar o antigo adaptador simulado.
- [ ] Rodar testes do projeto e revisar.

### Task 2: Sessão e tela

Files: `Vaulta.App.Core/Catalog/ScannerSession*`, `ScannerValuation`, `Vaulta.App/Views/ScannerSessionPage*`, testes App.Core.

- [ ] Testes RED de soma de estimativa pesquisada e leitura pendente sem ID fictício.
- [ ] Manter fontes e impedir importação/venda pendente; preservar itens resolvidos e importações idempotentes.
- [ ] Exibir/somar estimativa automaticamente >=80%, confirmar abaixo; contar leitura sem cotação e seguir.
- [ ] Mostrar pendência/fontes na revisão final, sem painel persistente após sucesso.
- [ ] Rodar testes App.Core e revisar.

### Task 3: Cache, API e atualização diária

Files: `CatalogDbContext`, nova entidade/configuração/migration de snapshots pesquisados, `ScannerService`, DI, worker diário, testes unitários e integração.

- [ ] Testes RED de variante/idioma/certificado, cache positivo/negativo e fluxo HTTP sem impressão local.
- [ ] Implementar cache/lock e política de expiração; integrar pesquisa somente em miss e re-resolver leitura corrigida. Ajustar prompt visual para combinar outros sinais quando o rodapé for ilegível e evitar inventar números.
- [ ] Ligar atualização diária de identidades já conhecidas sem visão/fotos.
- [ ] Gerar/verificar migration com banco isolado e rodar suítes completas dos projetos alterados.

### Task 4: Entrega verificada

- [ ] Revisão independente de todo o diff.
- [ ] Builds API/Android, assinatura/hash do APK, commit local e documentação.
- [ ] Publicar imagem imutável no ECR privado e deploy AWS autorizado; verificar imagem e health.
- [ ] Uma pesquisa real com `gpt-6-luna`/web e reutilização de cache, sem expor secrets ou criar usuário de teste em produção.
- [ ] Registrar resultado e limitações; limpar apenas os recursos temporários criados nesta execução.
