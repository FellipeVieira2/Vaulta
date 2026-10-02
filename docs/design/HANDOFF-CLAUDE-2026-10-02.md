# Prompt de continuidade — Vaulta

Você vai continuar a implementação da Vaulta a partir do estado local deixado pelo Codex em 02/10/2026. O usuário pediu que o Codex pausasse e preparasse este handoff. Leia as fontes, confira o código atual e avance nas pendências; não recomece o projeto nem declare todo o prompt mestre concluído.

## 1. Projeto e fonte principal

Workspace Windows: `C:\Users\Fellipe.Souza\source\repos\Vaulta`.

Prompt mestre completo: `C:\Users\Fellipe.Souza\.codex\attachments\8b867064-17d9-4286-9b88-44275fc02cf0\Texto colado.txt`.

Se estiver em outra máquina, peça ao usuário esse arquivo e o estado local do repositório. O GitHub sozinho não contém necessariamente o trabalho descrito aqui.

Stack: .NET 10, aplicativo MAUI Android, App.Core testável, API ASP.NET Core, monólito modular, EF Core e PostgreSQL. Módulos: Identity, Catalog, Collection, Marketplace, Assets, Orders, Payments, Shipping, Reviews e Wallets.

Branch local: `codex/home-marketplace`. HEAD conferido: `3901315e9a74a929f2906cb839eee2f27a6cb080`. Há muitas alterações e arquivos novos sem commit; não houve push, PR ou deploy nesta execução. Preserve também as alterações anteriores do usuário em Catalog, scanner, TcgDex e testes. Não use reset, clean ou stash para apagar esse estado. Leia eventuais AGENTS.md e instruções locais antes de editar. Confira o remoto antes de propor sincronização.

Repositório de referência: https://github.com/FellipeVieira2/Vaulta.

## 2. Leia estes documentos primeiro

Leia o conteúdo dos arquivos, não apenas seus nomes. Estes são os caminhos completos, em ordem de leitura:

1. `C:/Users/Fellipe.Souza/source/repos/Vaulta/docs/design/master-prompt-coverage-2026-10-02.md`: mapa de P0/P1 e trabalho restante.
2. `C:/Users/Fellipe.Souza/source/repos/Vaulta/docs/superpowers/plans/2026-10-02-marketplace-scanner.md`: tarefas 1–7; alguns checkboxes estão desatualizados.
3. `C:/Users/Fellipe.Souza/source/repos/Vaulta/docs/superpowers/specs/2026-10-01-home-marketplace-design.md`: requisitos da Home.
4. `C:/Users/Fellipe.Souza/source/repos/Vaulta/docs/superpowers/specs/2026-10-01-scanner-to-listing-design.md`: scanner → unidade → anúncio.
5. `C:/Users/Fellipe.Souza/source/repos/Vaulta/docs/superpowers/specs/2026-10-01-checkout-shipping-design.md`: política e dependências de checkout/frete.
6. `C:/Users/Fellipe.Souza/source/repos/Vaulta/docs/design/implementation-marketplace-scanner-2026-10-02.md`: relatório intermediário; contagens posteriores estão neste handoff.
7. `C:/Users/Fellipe.Souza/source/repos/Vaulta/docs/design/scanner-drafts-implementation-2026-10-02.md`: contratos, persistência, concorrência, migration e testes.
8. `C:/Users/Fellipe.Souza/source/repos/Vaulta/docs/design/native-home-implementation-2026-10-02.md`: Home, estados e navegação.
9. `C:/Users/Fellipe.Souza/source/repos/Vaulta/docs/design/native-listing-detail-implementation-2026-10-02.md`: detalhe, comparação, seller e limites.
10. `C:/Users/Fellipe.Souza/source/repos/Vaulta/docs/design/native-login-2026-10-02.md`: login, Figma, fontes e autenticação; a correção de commit interrompida está descrita no item 5 deste handoff.
11. `C:/Users/Fellipe.Souza/source/repos/Vaulta/docs/design/scanner-nova-2026-10-02.md`: Nova, configuração, limites e fontes AWS.
12. `C:/Users/Fellipe.Souza/source/repos/Vaulta/docs/design/price-history-integrity-2026-10-02.md`: ausência de histórico real e retirada das simulações.
13. `C:/Users/Fellipe.Souza/source/repos/Vaulta/docs/design/discovery-2026-10-01/README.md` e `product-context.md`: descoberta, referências e contexto do produto. A mesma pasta contém screenshots, assets e contextos Figma.

### Referências técnicas por área

Todos os caminhos desta lista partem de `C:/Users/Fellipe.Souza/source/repos/Vaulta/`:

- Arquitetura: `docs/architecture.md`; identidade canônica: `docs/adr/001-canonical-catalog-identity.md`; entrada versus unidade física: `docs/adr/002-collection-entry-and-collectible-item.md`.
- Scanner existente e testes no aparelho: `docs/scanner.md`, `docs/scanner-sessions.md`, `docs/scanner-phone-test.md`.
- Ciclo unidade/anúncio: `docs/collection-listing-lifecycle.md`.
- Preços e catálogo: `docs/market-pricing.md`, `docs/daily-market-prices.md`, `docs/catalog-validation-2026-09-26.md`.
- Avaliações e repasses: `docs/seller-reviews.md`, `docs/seller-payouts.md`.
- AWS: `docs/aws-production.md`, `docs/aws-production-validation.md`.
- Design e verificação MAUI: `docs/design/design-system.md`, `docs/design/maui-ui-validation.md`.
- Auditoria e contexto anterior: `docs/business-gap-audit.md`, `docs/design/next-steps-2026-10-01.md`, `docs/design/implementation-home-2026-10-02.md`, `docs/superpowers/plans/2026-10-01-home-marketplace.md`.

### Se o Claude não estiver no mesmo workspace

Envie este prompt junto com `C:/Users/Fellipe.Souza/source/repos/Vaulta/artifacts/claude-handoff-2026-10-02.zip`. O pacote contém todos os `.md` de `docs/` mantendo a estrutura de pastas e o prompt mestre em `MASTER-PROMPT-VAULTA.txt`. Após extrair, os caminhos relativos continuam válidos dentro do pacote; os caminhos absolutos Windows são somente a localização original.

O pacote de referências não contém o código modificado, screenshots ou assets. Para implementar em outra máquina, forneça também o estado atual do workspace, incluindo arquivos novos sem commit. Para reprodução visual, disponibilize a pasta `docs/design/discovery-2026-10-01/` completa ou o acesso ao Figma. Não assuma que abrir o GitHub recupera alterações que ainda estão apenas locais. Não transfira credenciais, arquivos de secrets ou configurações privadas junto.

Documentos são registros em momentos diferentes. Quando houver divergência, confira código, testes e logs; não escolha automaticamente a contagem mais favorável.

## 3. Design e plugins: onde obter as informações

O usuário pediu Product Design, Figma, MagicPath, tldraw, GitHub, AWS Core, Superpowers e Notion. Use as integrações disponíveis no seu ambiente. Se não tiver acesso, use as referências locais e informe a limitação; não invente conteúdo remoto.

Figma principal: https://www.figma.com/design/Ey1vEJNXx1oaSZbODMamWY.

- Página: `0:1`; foundations: `3:22`.
- Home: `4:2`.
- Detalhe do anúncio: `4:85`.
- Scanner: `4:130`; estados: `12:170`, `12:192`, `12:214`, `12:244`.
- Preparar anúncio: `24:137`; revisar anúncio: `24:168`.
- Novo login: `29:153` — https://www.figma.com/design/Ey1vEJNXx1oaSZbODMamWY?node-id=29-153.

Obtenha contexto e screenshot do frame antes de implementar. Reutilize componentes e tokens: fundo preto, superfícies escuras, roxo e Inter; respeite a densidade compacta, sem transformar telas em landing pages. Fontes Inter Regular/Medium/SemiBold/Bold e OFL já estão em `src/Vaulta.App/Resources/Fonts`, com aliases registrados em MauiProgram. Não use URLs temporárias Figma como assets definitivos do app.

tldraw/jornadas: https://www.tldraw.com/f/tKrHPaOm1vyg9K9zwAUzA.

MagicPath: projeto privado “Vaulta — Exploração Home Marketplace”, ID `456589685570015232`. Alternativas: grid `456589876406665216`, feed `456590087627636736`, discovery `456590091880636416`. Uploads anteriores foram autorizados pelo usuário e concluídos. Esses protótipos não comprovam integração nativa.

Notion, raiz Vaulta: https://app.notion.com/p/3ed6ef6d792d81f08428c0c3681f4e34.

- Status 19: `3ed6ef6d-792d-81a4-8b00-e034d365506c`.
- Home 21: `3ed6ef6d-792d-81fe-a3f3-df7eda15d478`.
- Scanner 22: `3ed6ef6d-792d-81d2-8c5e-dc6150af1c37`.
- Checkout 23: `3ed6ef6d-792d-816d-8f6b-fff2e06dae8f`.
- Frete 11: `3ed6ef6d-792d-8194-8f44-fedfc47a3fd9`.

O status no Notion ainda contém resultados intermediários. Leia antes de atualizar, preserve histórico e diferencie proposta visual, implementação local e produção.

## 4. O que já foi implementado

- BrowseListings com busca por carta/set/número, filtros combinados, paginação e metadados/reputação em lote.
- Home nativa, detalhe com fotos reais e comparação por printing/variante, anúncios públicos do seller e entrada no checkout existente.
- Rascunhos privados: criar/ler/editar/anexar/remover fotos/publicar/cancelar, ownership, versionamento, fingerprint original e chave durável.
- Migration `20261002050920_ListingDrafts`, designer e snapshot. Publicação persiste intenção antes da reserva; advisory lock por anúncio e releitura corrigem retomadas simultâneas.
- ScannerOccurrenceImporter, ScannerSaleFlow e persistência por ScanId: cópias físicas distintas, retry estável, custo de aquisição preservado, preparação/revisão e preço manual.
- Fotos próprias frente/verso são exigidas para publicar; artwork é referência. O gate valida ownership/finalidade/prontidão do asset, não certifica visualmente o conteúdo da fotografia.
- Venda publicada ou já vendida recupera o recibo; vendida oferece acesso às vendas.
- Ao voltar ao scanner, restaurar ações da última ocorrência até escolher Próxima evita recapturar a mesma carta.
- Estado privado é limpo depois de troca de conta, inclusive quando a página estava oculta.
- Reconhecimento automático usa número confirmado, confiança mínima e distância entre candidatos. Foi adicionada no máximo uma nova captura da mesma cena após 250 ms antes de pedir escolha; divergência de identidade permanece manual. Acabamento ambíguo não é escolhido automaticamente. Isso pode adicionar uma requisição de reconhecimento por ocorrência incerta.
- ScannerOperationContext captura sessão, owner, geração e token; camada MAUI também congela câmera. Captura, detalhes, leitura/gravação, busca, importação e aplicação de resultado verificam o contexto após awaits.
- Nova/Bedrock: evidências tipadas, JSON estrito, catálogo canônico, limites/retries/concurrency/telemetria e fallback OCR. Desativado por padrão.
- Histórico fabricado foi retirado do provedor de produção; ausência de fonte retorna vazio. Método morto de gráfico ilustrativo também foi removido.
- Novo login nativo com e-mail/senha, mostrar/ocultar senha, validação, carregamento/erro, cadastro existente e navegação de startup/logout/onboarding. Recuperação ainda mostra indisponibilidade real; não envia e-mail. Não existe login social integrado.

## 5. PRIMEIRA PRIORIDADE: concluir a correção de autenticação interrompida

Não trate o login como finalizado antes de resolver este P1 da revisão independente:

LoginFlow cancela a espera via WaitAsync, mas AuthenticationService pode continuar gravando tokens. SecureStorage.SetAsync não cancela uma gravação que já começou. Se o usuário sair, ocorrer timeout ou outro login/logout enquanto essa gravação está pendente, uma tentativa antiga pode terminar depois e sobrescrever a conta atual.

Fontes:

- `src/Vaulta.App.Core/Identity/LoginFlow.cs`.
- `src/Vaulta.App/Services/Authentication/AuthenticationService.cs`.
- `src/Vaulta.App/Services/Authentication/SecureTokenStore.cs` e `ITokenStore.cs`.
- `src/Vaulta.App/State/SessionState.cs`.
- `tests/Vaulta.App.Core.UnitTests/Identity/LoginFlowTests.cs`.

Trabalho em andamento no momento da pausa:

- `src/Vaulta.App.Core/Identity/AuthenticationCommitCoordinator.cs` EXISTE, mas os métodos ainda estão com `throw new NotImplementedException()`; é um stub de TDD interrompido.
- `tests/Vaulta.App.Core.UnitTests/Identity/AuthenticationCommitCoordinatorTests.cs`: sete regressões de gravação atrasada/geração já foram escritas.
- A integração desse coordenador em AuthenticationService NÃO foi feita. LoginAsync ainda grava tokens e atribui User sem esse gate.

Complete o coordenador e a integração. Serialize mutações de tokens; invalide gerações ao começar nova mudança/logout; verifique geração/cancelamento antes e depois de persistir; limpe gravação obsoleta ainda sob exclusão antes de admitir uma nova gravação. Proteja também restore/refresh contra publicação de estado antigo. Teste A cancelado → B, A → logout, storage que ignora cancellation, timeout, refresh e restauração. Não apague testes nem enfraqueça asserts para passar.

Uma tentativa dos novos testes foi bloqueada pelo Windows Application Control ao carregar a DLL, código `0x800711C7`. Isso é problema de ambiente, não evidência de aprovação. Inspecione o resultado real e execute novamente após concluir o código.

Depois, faça revisão independente das duas fronteiras: autenticação e contexto do scanner. Os ajustes do scanner já foram implementados e têm cinco testes de contexto, mas a árvore final inteira ainda precisa de revisão/build após o último incremento.

## 6. Evidências e como validar

Últimos resultados aprovados antes do stub de autenticação interrompido:

- Identity.IntegrationTests: **138/138**, PostgreSQL real 17.11, banco fresco isolado.
- Commerce.UnitTests: **78/78**.
- Identity.UnitTests: **156/156**.
- ArchitectureTests: **6/6**.
- App.Core.UnitTests: **139/139**, incluindo login, reconhecimento adicional e contexto do scanner. Os sete testes novos do coordenador vieram depois; portanto essa contagem não é aprovação da árvore final atual.
- Migration/modelo: nenhum descompasso pendente na última checagem.
- Um build Android terminou com **0 avisos e 0 erros**, em 6m17s. Alterações foram feitas durante/depois desse build; recompilar a árvore final, não assumir que o APK contém todas elas.

Logs em `artifacts/` são locais/ignorados:

- `scanner-draft-full-integration-final-fresh.log`: aprovação final 138/138.
- `android-marketplace-login-build.log`: build aprovado.
- Outros logs preservam falhas intermediárias de ambiente/configuração; não confundi-los com o resultado final.

Comandos sugeridos após corrigir o código, usando PowerShell e execução sequencial:

```powershell
dotnet test tests/Vaulta.App.Core.UnitTests/Vaulta.App.Core.UnitTests.csproj --no-restore --verbosity quiet -m:1
dotnet test tests/Vaulta.Commerce.UnitTests/Vaulta.Commerce.UnitTests.csproj --no-restore --verbosity quiet -m:1
dotnet test tests/Vaulta.Identity.UnitTests/Vaulta.Identity.UnitTests.csproj --no-restore --verbosity quiet -m:1
dotnet test tests/Vaulta.ArchitectureTests/Vaulta.ArchitectureTests.csproj --no-restore --verbosity quiet -m:1
dotnet build src/Vaulta.App/Vaulta.App.csproj --no-restore --verbosity quiet -m:1 -p:RuntimeIdentifier=android-arm64
```

Docker Desktop local não inicia corretamente: logs indicaram falha no Inference manager/socket dockerInference. Não apagar dados Docker para contornar isso. Foi instalado PostgreSQL portátil SOMENTE para testes em `artifacts/test-postgres/pgsql`, dados em `artifacts/test-postgres/data`, loopback `127.0.0.1:55439`. Verifique se o helper está rodando antes de usar; pode ter parado.

ApiFixture aceita `VAULTA_TEST_POSTGRES`. Use banco NOVO gerado para cada execução completa; reutilizar banco provocou duplicação de fixtures Catalog e falsos negativos em CollectionApiTests. A configuração Production exige Password não vazio mesmo no servidor de teste com trust. Exemplo, depois de criar um banco exclusivo de teste:

```powershell
$env:VAULTA_TEST_POSTGRES = 'Host=127.0.0.1;Port=55439;Database=NOME_DO_BANCO_NOVO_DE_TESTE;Username=postgres;Password=vaulta-test-only'
dotnet test tests/Vaulta.Identity.IntegrationTests/Vaulta.Identity.IntegrationTests.csproj --no-restore --verbosity quiet -m:1
```

A senha acima é um valor fictício exclusivo do teste isolado; não é credencial de produção. `TestPostgresDatabase.cs` cria/remove apenas bancos exclusivos de migração; mantenha essa proteção. Execuções paralelas no sandbox sofreram falhas de memória/stack overflow. Execução sequencial fora dessas restrições funcionou.

Emulador Android não iniciou por falta de espaço no disco C; não houve validação visual no aparelho. O espaço ficou próximo de 1 GB durante o trabalho. Não apague arquivos do usuário para liberar espaço. APK existente: `src/Vaulta.App/bin/Debug/net10.0-android/android-arm64/com.vaulta.app-Signed.apk`.

## 7. Tudo que ainda falta: continuar P0 antes de P1

Confira o prompt mestre e o mapa de cobertura; esta lista distingue implementação parcial de jornada realmente pronta.

### P0 e qualidade do incremento atual

1. Concluir autenticação segura, executar novos testes, revisão independente e build final.
2. Validar Home, busca, detalhe/comparação, seller, login e scanner/venda em aparelho: layout, teclado, textos ampliados, alvos de toque, leitor de tela, contraste, reduced motion, offline, erro/retry e imagens ausentes/expiradas.
3. Avaliar scanner com dataset de fotos reais: reflexos, blur, rodapé/número, ângulos, idiomas, edições e acabamentos. Medir precisão, falsas confirmações, latência e custo. Refinamento automático foi codificado; validação em câmera real permanece pendente.
4. Verificar acesso ao modelo/região AWS, avaliar Nova em fotos reais e calibrar gates antes de habilitar. Use a documentação oficial e fontes do relatório scanner-nova; não colocar chaves AWS no aplicativo.
5. Completar apresentação pública do seller: Listing traz SellerUserId, mas endpoint público de perfil consulta por username. Não inventar nome/bio/logo; resolver contrato e autorização, depois ligar a UI.
6. Checkout: cotação real de frete, prazo, total exibido/persistido antes de pagar, validade/revalidação da cotação, etiqueta, tracking, repasse, falha/estorno e testes. Handoff do detalhe para checkout existe; essa política ainda não está implementada integralmente.
7. Pedido e minhas vendas: refinar telas nativas dedicadas, estados e jornada de recebimento/remessa/repasse, alinhadas ao frete cotado.
8. Coleção e detalhe da unidade: redesign nativo compacto, filtros/estados e venda rápida de unidade já existente, reaproveitando rascunhos sem criar uma cópia da carta nem sobrescrever uma sessão real do scanner.
9. Revisar configuração/observabilidade/operação: métricas e alertas reais, custos/budgets, segredos no backend, autorização, upload e recuperação. Nenhum deploy AWS foi realizado.

### Decisão de frete já tomada e dependência externa

O usuário aprovou **cotar e cobrar no checkout, pago pelo comprador**. Provedor ainda não escolhido. Ele disse que pensa em Correios e perguntou se precisa de chave. Não assumir que Correios já está contratado ou configurado.

Para integração direta, consultar acesso contratado às APIs CWS de preço/prazo e token/subdelegação. Fonte oficial usada: https://www.correios.com.br/atendimento/developers/manuais/manual-api-preco-1. Confirmar condições atuais antes de integrar. Configurar credenciais no backend/secret manager; não pedir segredo em chat nem inventar preços/prazos. Avance nas partes independentes enquanto provedor, contrato, origem e pacote não forem definidos.

### P1

10. Portfolio: refinar apresentação e proveniência da valoração real, estados de ausência/falha e atualização.
11. Price History: fonte real no backend, proveniência/data/câmbio/intervalo e testes. Provider atual retorna vazio; não restaurar gráficos simulados ou chaves de provedor no mobile.
12. Offers: especificar e implementar negociação, persistência, autorização, estados/transições e telas.
13. Favorites: implementar persistência por conta e botões/telas funcionais; favorito no detalhe está indisponível.
14. Notifications: eventos reais, preferências, persistência/entrega, leitura e telas.
15. Store: apresentação profissional, catálogo/promoções reais e diferenciação sem esconder relevância P2P.
16. My Videos: preservar gravação opcional existente, validar permissões/áudio/exportação, armazenamento e gerenciamento em aparelho.
17. Profile e Settings: redesign dedicado, dados reais, edição e preferências, logout seguro e controles realmente funcionais.
18. Recuperação de senha: backend/endpoints reais, segurança, entrega, UX e testes; hoje existe só mensagem de indisponibilidade. Login social só se vier a fazer parte do escopo definido.

## 8. Regras para continuar

O usuário já autorizou avançar no prompt e no scanner sem pedir nova confirmação de cada etapa; pediu também redesign de login com plugins. Tome decisões rotineiras com os requisitos existentes, registre escolhas e continue trabalho independente de credenciais. Pergunte apenas quando faltar uma decisão que realmente bloqueie a implementação, como provedor/contratação de frete.

Não criar dados comerciais fictícios nem tratar protótipos como funcionalidades. Condição é declarada pelo vendedor; referência de mercado não é preço pedido nem custo de aquisição. Rascunho privado não entra na Home nem pode ser comprado. Retry não duplica unidade/anúncio. Sessão e conta de origem precisam ser verificadas em todos os limites assíncronos. Fotos de catálogo não substituem fotos reais da unidade.

Use testes significativos para ownership, concorrência, recuperação e operações assíncronas; mantenha contratos legados e limites dos módulos. Faça revisão antes de declarar conclusão. Atualize os documentos e o Notion com evidências reais. Não realize cobrança real ou deploy de produção como efeito colateral de validação.

Comece pela prioridade de autenticação do item 5, confira o git diff completo e os testes interrompidos, e depois avance nas pendências do item 7.
