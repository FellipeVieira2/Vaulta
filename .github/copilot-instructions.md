# Instruções do GitHub Copilot — Vaulta

Este arquivo é a referência permanente para agentes que alterem este repositório. Leia-o antes de implementar tarefas e valide as decisões descritas contra o código atual. Preserve implementações corretas; não transforme intenções futuras em escopo automático.

## 1. Produto e princípios

A Vaulta é uma plataforma brasileira para colecionadores e jogadores de Trading Card Games. A ambição é ser um sistema operacional para colecionáveis, não apenas um marketplace: identificar cartas, organizar coleções, acompanhar valores e histórico, descobrir oportunidades, comprar, vender e trocar com confiança.

O ciclo de produto desejado é:

`Scan → Identify → Collect → Value → Track → Discover → Buy → Trade → Sell`

A experiência deve tornar simples um mercado complexo, inclusive para quem não conhece profundamente TCGs. Priorize simplicidade, confiança, velocidade, liquidez, qualidade dos dados, experiência mobile, consistência e segurança. Reduza passos e complexidade sem sacrificar integridade, segurança ou clareza.

Pokémon é o primeiro foco comercial, não uma fronteira do domínio. A arquitetura deve acomodar Pokémon, Magic: The Gathering, Yu-Gi-Oh!, One Piece, Digimon, Lorcana e futuros TCGs sem codificar pressupostos irreversíveis de um único jogo.

Visão futura inclui scanner/Vision, catálogo, coleção, portfolio, preços, wishlist, marketplace P2P e profissional, sellers, pedidos, pagamentos, ledger, logística, condition analysis, reputação, disputas, notificações, analytics e integrações. **Essas capacidades não são escopo por estarem nesta lista.** Crie cada bounded context quando houver uma tarefa de produto que o solicite.

## 2. Estado real do repositório versus arquitetura alvo

### Implementado hoje

- Solution `Vaulta.slnx` em .NET 10; `Directory.Build.props` define `net10.0`, nullable, implicit usings e warnings como errors.
- Backend de monólito modular: uma API ASP.NET Core, um PostgreSQL e dez módulos: Identity, Catalog, Collection, Assets, Marketplace, Orders, Payments, Wallets, Shipping e Reviews.
- API em `src/Vaulta.Web.Api`, com Minimal APIs versionadas em `/api/v1`, ProblemDetails, JWT, Swagger em Development, health checks, rate limiting básico, CORS configurável, headers de segurança, CorrelationId e instrumentação OpenTelemetry ASP.NET Core.
- Módulos divididos em Domain, Application, Infrastructure e Contracts. SharedKernel pequeno. App MAUI em `src/Vaulta.App` e clientes/serviços sem MAUI em `src/Vaulta.App.Core`.
- EF Core/PostgreSQL e migration inicial em `src/Modules/Identity/Vaulta.Identity.Infrastructure/Persistence/Migrations`.
- Outbox transacional com worker e bus em memória. A entrega é at-least-once; não há broker externo nem tabela Inbox nesta versão.
- Testes unitários, integração com PostgreSQL real via Testcontainers e testes arquiteturais.
- Docker Compose de desenvolvimento para API e PostgreSQL, volume persistente, healthcheck, migração no startup e scripts PowerShell para ambiente local e smoke test.

### Ainda não implementado

- Home/portfolio/detalhe têm avaliação real implementada por quantidade/variante de unidades ativas, com cobertura parcial e diferenças em BRL; cálculos e teste HTTP/PostgreSQL passaram, validação no dispositivo ainda pendente. Histórico temporal do acervo ainda não foi entregue. O scanner tem OCR, captura autenticada e consulta de valores convertidos para BRL. Não apresente demonstrações como dados reais de mercado.
- Itens anunciados ficam bloqueados; a confirmação de recebimento pelo comprador transfere a unidade, preservando o histórico privado do vendedor. Publicação e transferência interrompidas têm recuperação idempotente; veja `docs/collection-listing-lifecycle.md`. Reservas com expiração e onboarding/KYC completo ainda estão incompletos. Sem saldo/saque: repasse direto por Pix verificado após recebimento confirmado pelo comprador e liquidação, descontados 8% e tarifas reais do Asaas do vendedor. Cancelamento antes do envio por comprador/vendedor aguarda reembolso integral confirmado; após envio exige atendimento. Claims financeiros são persistidos antes do único POST; respostas incertas só são conciliadas por consulta, sem reenvio automático. Execução real permanece desabilitada por padrão. Acompanhe limitações e evidências em `docs/business-gap-audit.md` e `docs/seller-payouts.md`.
- O scanner por sessão implementa custo opcional, estimativas em BRL por variante, persistência local e importação retomável. Gravação é **opcional e iniciada somente por um toque explícito**, com microfone solicitado nesse momento. A leitura contínua usa estabilidade/mudança da prévia, mensagem discreta e proteção contra repetição; soma automaticamente somente com nome/número fortes e variante definida, confirmando casos incertos e outra cópia da última carta. CameraView/Media3 exportaram um MP4 vertical com áudio no emulador; depois de corrigir o atraso do encoder, os frames das revelações comum/dourada e do total final foram inspecionados. Compilação Android e 25 testes de sessão/leitura/reconhecimento/preço passaram. Voz inteligível, reflexos/montinhos reais, gravações longas e compartilhamento ainda exigem aparelho físico. Novas rotas de preço/avaliação ainda retornam 404 na API pública em 01/10/2026; o usuário confirmou que não fez deploy. Não confundir código local com publicação. Consulte `docs/scanner-sessions.md` e `docs/scanner-phone-test.md` antes de afirmar que o diferencial está pronto.
- A lista de TCGs, moedas e idiomas permitidos no Identity é validação atual, não um catálogo global.
- Domain Events são persistidos no Outbox com nomes versionados; o bus atual apenas executa consumers locais registrados e registra dispatch. Isso não equivale a integração externa já entregue.
- Não há broker externo, Inbox geral, Redis, busca dedicada ou exportação de telemetria configurada. Assets usa object storage S3/MinIO. Wallets preserva dados históricos; não criar novas carteiras/backfill nem registrar crédito automático de novos pagamentos.

### Evolução esperada

Evolua o monólito por bounded contexts dentro do processo e do PostgreSQL existentes. Extraia serviço, banco, broker ou infraestrutura especializada somente quando volume, isolamento operacional, requisitos de disponibilidade/equipe ou medições demonstrarem benefício real. A arquitetura é uma ferramenta para confiabilidade e evolução, não um objetivo independente.

## 3. Estrutura e projetos

Use os caminhos físicos abaixo; os nomes de pastas lógicas em `Vaulta.slnx` não mudam esses caminhos:

- `src/Vaulta.Web.Api` — composition root, endpoints HTTP, middleware, autenticação, OpenAPI e configuração da aplicação.
- `src/Vaulta.SharedKernel` — primitivas compartilhadas pequenas (`AggregateRoot`, `IDomainEvent`, `IClock`, `Money`, `DomainException`). Não o transforme em depósito de tipos de todos os módulos.
- `src/Modules/Identity/Vaulta.Identity.Domain` — aggregate `User`, entidades, regras e Domain Events; referencia SharedKernel, não EF/ASP.NET.
- `src/Modules/Identity/Vaulta.Identity.Application` — Commands, Queries, handlers, validators e ports de persistência/serviços.
- `src/Modules/Identity/Vaulta.Identity.Infrastructure` — EF Core, PostgreSQL, mapeamentos, migration, tokens, hashing, store e Outbox.
- `src/Modules/Identity/Vaulta.Identity.Contracts` — DTOs e contratos de transporte independentes do domínio/EF.
- `tests/Vaulta.Identity.UnitTests` — regras e comportamento do domínio/aplicação.
- `tests/Vaulta.Identity.IntegrationTests` — fluxos HTTP e PostgreSQL via Testcontainers.
- `tests/Vaulta.ArchitectureTests` — regras de dependência entre assemblies.

A solution contém os dez módulos, API, SharedKernel, App, App.Core e projetos de testes, incluindo `Vaulta.Commerce.UnitTests`. Preserve a direção atual de dependências: API → Infrastructure/composição; Infrastructure → Application; Application → Domain e Contracts; Domain → SharedKernel. Contracts e SharedKernel permanecem independentes de Infrastructure/API. App consome contratos e App.Core; não referencia Infrastructure. Não introduza referências circulares entre módulos.

## 4. Arquitetura e padrões do backend

### Modular monolith, DDD e CQRS seletivo

- Mantenha inicialmente um processo de API e PostgreSQL como banco principal. CQRS é separação de intenção/modelos no código, não obrigação de criar dois bancos.
- Commands expressam alterações; Queries expressam leituras. O fluxo esperado é `HTTP → endpoint → Command/Query → handler → domínio/port → Infrastructure`.
- O padrão corrente usa `CommandHandlers` e `QueryHandlers` tipados diretamente, sem MediatR. Preserve-o; não adicione mediator, pipeline genérico ou bus interno sem necessidade concreta.
- Endpoints mapeados em `IdentityEndpoints.cs` devem permanecer finos: binding, autenticação/autorização, chamada ao handler e status/headers HTTP. Regras de negócio pertencem ao domínio/aplicação.
- Commands retornam DTO/resultados, nunca Entities de domínio. Queries devem projetar DTOs/read models, usar `AsNoTracking` quando apropriado e não carregar aggregate completo sem necessidade.
- `IIdentityStore` e `IProfileReader` são ports específicos; EF e `IQueryable` ficam dentro de Infrastructure. Não crie `GenericRepository<T>` para encapsular EF Core.
- Validadores usam FluentValidation; o domínio também protege invariantes, para que regras não dependam apenas da API.

### Domínio e dados

- `User` é o aggregate de consistência para autenticação, perfil, preferências e interesses TCG. Mutação relevante deve passar por métodos do aggregate e regras do domínio, não por setters públicos ou atualizações ad hoc no controller.
- Perfil privado e perfil público têm DTOs diferentes. A resposta pública é allowlist; não exponha email, hash de senha, tokens, status interno ou futuros dados financeiros.
- Email e username preservam valor de apresentação e usam colunas normalizadas para comparação case-insensitive, índices únicos no PostgreSQL e tratamento de concorrência/constraint.
- O username corrente aceita 3–30 caracteres ASCII `[A-Za-z0-9_]`; alterá-lo requer decisão de produto e atualização conjunta de validação, persistência, testes e documentação.
- Status corrente é persistido como string. `PendingVerification` pode autenticar nesta entrega, pois verificação de email ainda não faz parte do fluxo. Não mude silenciosamente essa regra.
- Preferências têm defaults centralizados em `IdentityRules`; TCGs são códigos em string validados pelo domínio para permitir extensão sem enum/migration por cada código. Não espalhe magic strings.
- IDs são `Guid`. Datas persistidas são UTC (`DateTimeOffset`); obtenha horário por `IClock`, não por `DateTime.Now` espalhado.
- Concorrência usa `User.Version` como token otimista e ETag/`If-Match` em alterações do perfil/preferências. Mantenha detecção explícita de conflito; não sobrescreva silenciosamente edições concorrentes.
- `Money` no SharedKernel usa `decimal` e currency. Nunca use `float`/`double` para dinheiro. A existência de `Money` não significa que Identity tenha saldo financeiro.
- No domínio futuro de catálogo, separe identidade/catalogação (`Game → Series → Set → Card → Printing → Variant`) da unidade física possuída pelo usuário (`CollectionItem` ou conceito equivalente). “O que é a carta?” não é “qual cópia esta pessoa possui?”.
- Não represente raw, graded, sealed e lot como uma única entidade genérica com dezenas de condicionais. Modele conceitos que expressem diferenças reais sem antecipar abstrações não usadas.
- Preços devem manter dimensões distintas de printing, variant, idioma, condição, grade, mercado e data. Não compare preço raw com grade PSA 10 como o mesmo ativo.

### Events, Outbox e consistência

- Domain Events descrevem fatos internos do domínio. Integration Events são contratos estáveis entre módulos/processos; não os trate como sinônimos ao adicionar integrações.
- `IdentityDbContext.SaveChanges` persiste alterações do aggregate e mensagens do Outbox na mesma transação. Não publique efeitos externos antes do commit.
- Outbox atual usa JSONB, nomes versionados, processamento em lotes e `FOR UPDATE SKIP LOCKED`. Assume at-least-once; consumidores externos futuros precisam ser idempotentes.
- Hoje o bus é in-memory, não há broker nem Inbox persistente. Ao introduzir consumidor durável ou efeitos externos, desenhe Inbox/idempotência com chave estável (por exemplo, mensagem + consumer) e transação apropriada; não alegue exactly-once.
- O dispatcher marca mensagens processadas quando todos os consumers locais registrados concluem sem erro. `UserRegisteredWalletConsumer` e `PaymentConfirmedWalletConsumer` não são registrados: o produto não oferece carteira. Repasses ficam em Payments e usam worker/claim durável, sem split antecipado. `externalReference` correlaciona uma transferência Asaas; não presumir que seja chave de idempotência. Após timeout/crash, conciliar e nunca reenviar cegamente.
- Ao evoluir envelopes, considere `EventId`, `CorrelationId`, `CausationId`, `OccurredAt` e identificadores de contexto, sem incluir senha, token ou dados pessoais desnecessários. Não adicione campos/protocolo até existir consumidor ou caso de uso.
- Use transação PostgreSQL local para invariantes fortes dentro do bounded context. Entre contextos, prefira integração assíncrona/eventual consistency e compensação quando adequada; não introduza distributed transactions.
- Reservas, estoque, pedidos e pagamentos futuros precisam tratar concorrência e idempotência como invariantes, não como detalhes posteriores.

### PostgreSQL e migrations

- Schemas correntes: `identity`, `catalog`, `collection`, `assets`, `marketplace`, `orders`, `payments`, `wallets`, `shipping` e `reviews`. Outbox fica em `identity`; outros módulos excluem essa tabela de suas próprias migrations.
- EF Core e migrations são a fonte de evolução do schema. Nunca use `EnsureCreated` como alternativa a migrations.
- Para alteração de schema, crie migration incremental, revise SQL/operações/constraints e sincronize snapshot. Nunca edite uma migration já aplicada para corrigir ambientes existentes; use migration nova.
- Defina PKs, FKs, unique constraints, índices, comprimentos e delete behavior explicitamente. Considere consultas reais ao desenhar índices.
- Testes que validam comportamento EF/PostgreSQL usam PostgreSQL real no Testcontainers; não substitua por EF InMemory.
- Compose aplica migrations por configuração de desenvolvimento. Em produção, executar migration como etapa controlada de deploy e com credencial apropriada para DDL; não presumir que migrations automáticas no startup são estratégia de produção.

Comandos úteis da solução atual:

```powershell
dotnet restore Vaulta.slnx
dotnet build Vaulta.slnx
dotnet test Vaulta.slnx
dotnet tool restore
dotnet ef database update --project src/Modules/Identity/Vaulta.Identity.Infrastructure
dotnet ef migrations add NomeDaMudanca --project src/Modules/Identity/Vaulta.Identity.Infrastructure --output-dir Persistence/Migrations
dotnet ef migrations has-pending-model-changes --project src/Modules/Identity/Vaulta.Identity.Infrastructure
```

Os testes de integração requerem Docker. O desenho de testes atual usa PostgreSQL 17 Testcontainers e migrations reais. O Compose usa o volume persistente `vaulta-postgres`; não execute `docker compose down -v` sem solicitação explícita, pois isso apaga os dados locais.

## 5. API e segurança implementadas

- Rotas públicas começam em `/api/v1`; preserve compatibilidade e não renomeie contratos publicados sem necessidade. Mudança incompatível pede versão/estratégia explícita.
- Endpoints ficam nos arquivos `*Endpoints.cs` da API, separados por módulo; o host usa Minimal APIs, não controllers. A confirmação de pagamento vem do webhook autenticado, e o comprador confirma recebimento em `/orders/{id}/deliver`.
- Identity oferece registro, login, refresh rotation, logout, `GET /api/v1/me`, atualização parcial de perfil/preferências, endereço de entrega, troca de senha e leitura de perfil público. Recuperação por e-mail, exportação e exclusão de conta ainda precisam de implementação.
- PATCH de perfil/preferências requer ETag em `If-Match`; omissão preserva campo e `null` limpa apenas opcionais conforme o contrato. Não permita alterar email/username pelo endpoint de perfil.
- Erros são `ProblemDetails`; preserve mapeamento entre validação, domínio, autenticação, autorização, not-found e conflito. Erros 500 são genéricos para o cliente; nunca retorne stack trace.
- Password hashing usa o hasher da plataforma (PBKDF2 configurado em 210.000 iterações); não invente criptografia ou hash próprio.
- JWT corrente usa HS256, issuer/audience/expiration/signature validation e SecurityStamp/status verificado contra o banco. Access token dura 15 minutos; refresh token dura 30 dias, é aleatório, rotacionado e armazenado apenas como SHA-256 no banco.
- Troca de senha e reutilização de refresh revogado invalidam sessões da conta. Logout revoga o refresh correspondente; access token emitido pode continuar válido até expirar. Preserve essa semântica documentada ou altere-a com desenho e testes explícitos.
- Segredos vêm de configuração/user-secrets/ambiente; `.env` é local e ignorado. Nunca introduza secrets reais ou valores sensíveis em appsettings versionado, logs, testes, exemplos ou output de ferramentas.
- Auth tem rate limit local básico por IP (20/minuto por padrão); CORS fechado sem origens configuradas. Não trate isso como rate limiting distribuído nem como proteção completa de produção.
- Logs estruturados e CorrelationId são usados. Não registre senha, token, request body sensível ou payload privado do Outbox. Stack trace não vai ao cliente; logs também devem evitar valores sensíveis.
- Compose é desenvolvimento, expõe portas em loopback. Produção exige TLS, secrets externos, configuração cuidadosa de proxies confiáveis/forwarded headers, migrations de deploy e Swagger desabilitado fora de Development.
- Autenticação JWT é configurada via options validadas no startup. Mantenha as opções ligadas à configuração efetiva do host (inclusive testes/overrides); não capture valores de configuração prematuramente em closures.
- Mantenha Swashbuckle e Microsoft.OpenApi em versões binariamente compatíveis; não force uma versão direta que sobrescreva a dependência exigida pelo Swashbuckle sem validar geração e execução do Swagger.

## 6. Aplicativo móvel

O app existente usa .NET MAUI/MVVM, Android por padrão e iOS mediante build em Mac. Ao evoluí-lo, preserve a separação entre telas, ViewModels e os clientes testáveis de App.Core. Mudanças de backend não ampliam automaticamente o escopo das telas.

No app:

- Separe Views, ViewModels, serviços/clientes HTTP, contratos, navegação e estado conforme a necessidade real do app.
- Code-behind fica limitado a comportamento visual; ViewModels e serviços importantes devem ser testáveis.
- Use clients/services estruturados para HTTP; não espalhe `HttpClient` ou regras de negócio por telas.
- Backend é fonte das regras e do server state. Não replique invariantes de domínio no app; UI state (loading, filtros, modal, formulário) é distinto do server state.
- Tokens devem usar armazenamento seguro da plataforma; nunca registrar credenciais e limpar credenciais locais no logout.
- Não adicione cache/offline complexo até haver requisito, mas não acople a UI de forma que impeça evolução offline para coleção/scanner.
- Priorize mobile-first, acessibilidade, feedback de loading/erro e empty states com próxima ação clara. Scanner idealmente segue `abrir → enquadrar → identificar → confirmar → adicionar`; ofereça fallback manual sem perder contexto.
- Identidade visual desejada é premium, clean, moderna, dark-first e própria da Vaulta. Não copie marcas/elementos protegidos de TCGs nem use artwork sem verificar licença.

## 7. Evolução de produto, experiência e arquitetura

- Bounded contexts esperados ao longo do tempo incluem Identity, Catalog, Collection, Portfolio, Pricing, Marketplace, Orders, Payments, Ledger, Logistics, Trust, Sellers e Notifications; criar apenas os necessários para o escopo atual.
- O marketplace futuro deve impedir dupla venda: reserva/estoque precisa de operação atômica e concorrência explícita. Não confiar em status enviado pelo frontend.
- Pagamentos devem usar PSP apropriado e adapters/provider ports; webhooks precisam de autenticação, idempotência e reconciliação. Não acoplar domínio a um provedor nem presumir que a Vaulta custodiará fundos.
- Um ledger financeiro futuro deve ser auditável e imutável; não modele saldo como `User.Balance += amount`.
- Valor estimado de mercado e valor de liquidação/liquidez são conceitos diferentes. Condition Check de IA é estimativa visual, nunca certificação profissional garantida.
- Separe artwork de catálogo de fotos reais do item/seller. Considere licenças de assets e privacidade/LGPD; não exponha endereço residencial, KYC ou dados de pagamento em perfil público.
- Busca PostgreSQL é suficiente inicialmente. Redis, Elasticsearch/OpenSearch, cache, materialized views, microservices e outros componentes só entram após necessidade comprovada/medida.
- Para portfolio, marketplace, histórico de preços e dashboards, prefira projeções/read models, paginação, índices e consultas sem N+1. Não carregue aggregates apenas para responder leitura.
- Performance importa: evite gargalos óbvios e faça profiling/benchmark antes de otimizações complexas. Prefira evidência de métrica à intuição.
- UI deve priorizar carta e ação principal sem excesso de widgets ou dados. Evite aparência de ERP, dashboard corporativo genérico, app infantil, neon/gradientes excessivos ou clone visual de um TCG.

## 8. Padrão de trabalho para agentes

Antes de editar:

1. Leia as instruções aplicáveis e a issue/solicitação completa; identifique o bounded context e o resultado de produto desejado.
2. Explore a solution, arquivos próximos e implementação análoga. Use os caminhos reais; não presuma que a issue exige criar uma camada/projeto novo.
3. Separe fatos do repositório, requisito explícito e visão futura. Pergunte somente se faltar uma decisão que impeça solução correta; não invente requisito.
4. Faça a menor mudança completa; preserve código saudável e compatibilidade. Evite refactor massivo junto a feature pequena.
5. Considere Domain, Application, Persistence, API contract, autorização, segurança, observabilidade, concorrência, idempotência e testes conforme aplicável — sem produzir artefatos artificiais para cada item.

Ao implementar:

- Regras e invariantes no Domain; orquestração/use cases na Application; dependências externas/EF na Infrastructure; HTTP e composição na API.
- Prefira nomes de intenção e domínio. Evite `Helper`, `Manager`, `Utils` ou `Service` genéricos quando um conceito/ação preciso for possível.
- Evite God Classes/Services, métodos longos, duplicação, Feature Envy, Shotgun Surgery, Primitive Obsession, boolean blindness, excesso de parâmetros, nesting profundo e efeitos colaterais ocultos. Corrija smell relevante no escopo, sem usar isso para justificar refatoração ampla.
- Comentários explicam por que, não repetem código. Decisões arquiteturais não óbvias devem atualizar `docs/architecture.md` ou ADR quando a mudança justificar.
- Não acrescente TODOs essenciais, abstrações sem valor, flags, interfaces de uma implementação, enums extensos ou eventos para substituir uma chamada síncrona que exige consistência imediata.

Definition of Done:

- comportamento solicitado implementado e erros tratados;
- DTOs/contratos e autorização coerentes, sem expor entidade ou dado privado;
- migrations e constraints revisadas quando schema mudou;
- testes relevantes adicionados/atualizados e executados;
- build e testes relevantes passam sem novos warnings (warnings são errors nesta solution);
- documentação atualizada quando uma decisão ou instrução de execução mudou;
- sem secrets, logging sensível, stack trace para cliente ou funcionalidades futuras fora de escopo.

Ao relatar o trabalho, resuma arquivos/decisões alterados, validações executadas e pendências reais. Não afirme que testes, Docker, migration ou smoke test passaram se não foram executados.
