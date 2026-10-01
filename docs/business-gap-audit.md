# Acompanhamento das lacunas — 2026-10-01

Este documento acompanha todos os itens do levantamento de 30/09/2026. **O objetivo completo permanece em andamento.** `Validado` indica comportamento exercitado por testes; `Código` indica correção presente, ainda sem validação suficiente; `Parcial` indica que parte do problema continua; `Pendente` indica ausência do fluxo; `Decisão` indica regra comercial aguardando definição. Nenhum destes estados confirma publicação da API ou instalação do app no dispositivo.

## 16.1 Segurança e dinheiro

| Item | Estado | Evidência e próximo passo |
|---|---|---|
| 1. Declarar pagamento/entrega sem autorização | Validado | Confirmação manual de pagamento responde 403. HTTP real comprovou que vendedor não confirma recebimento e rastreio entregue não altera o pedido; comprador pode confirmar, inclusive repetir sem duplicar efeitos. |
| 2. Webhook sem autenticação | Validado | `PaymentEndpoints` valida `Asaas:WebhookToken` com comparação de tempo constante antes de interpretar o JSON. `CommerceApiTests` confirmou HTTP 401 sem token e 400 para payload autenticado incompleto. |
| 3. ID externo/pedido pago não persistidos | Validado | `PaymentTransaction.SetCheckoutInfo` salva AsaasPaymentId; handler do webhook salva Orders antes de Payments. `PaymentRetryTests` cobre confirmação, netValue aninhado e repetição do webhook. Reconciliação entre contexts ainda necessária. |
| 4. Crédito e saque sem execução | Parcial | Regra substituída pelo usuário: sem saldo/saque, Pix direto após recebimento. Consumers de carteira/crédito retirados; saque antigo responde 410 e preserva saldo histórico. Pipeline de repasse por pedido, liquidação, destino verificado, claim atômico e conciliação implementado e testado com provedor simulado. Falta validação operacional Asaas, configuração dos revisores e publicação; nenhuma transferência real feita. |
| 5. Taxa dos dois lados | Validado | Usuário confirmou 8% somente do vendedor e tarifas efetivas do Asaas também descontadas do vendedor. Testes cobrem venda R$ 100, comissão R$ 8, tarifa de cobrança R$ 2 e tarifa Pix R$ 1: vendedor recebe R$ 89. App mostra deduções separadas em BRL. |
| 6. Configuração/ID de split | Validado | Cobranças novas não usam split; ID interno de carteira não é enviado ao provedor. Gateway omite o campo split. Pagamentos históricos com split são bloqueados para repasse direto, evitando pagar novamente. |
| 7. Cancelamento de pago sem reembolso | Parcial | Regra confirmada: comprador/vendedor antes do envio, depois atendimento. Pedido fica refund_pending com envio/repasse bloqueados; operação integral única, conciliação após falha e transição refunded só com devolução confirmada. Testes HTTP/PG cobrem permissões, claim concorrente, evento repetido, recompra e proteção de venda posterior. Integração operacional Asaas e atendimento após envio continuam pendentes. |

## 16.2 Fluxo principal

| Item | Estado | Evidência e próximo passo |
|---|---|---|
| 1. Status ACTIVE/active | Validado | `CreateListing` compara com `OrdinalIgnoreCase`. Teste de armazenamento criou anúncio via HTTP com item real ACTIVE. |
| 2. Ports Marketplace sem registro | Validado | Catalog, Collection e Assets registrados em `AddMarketplaceModule`; teste HTTP exerceu a composição real. |
| 3. LISTING_PHOTO recusado | Validado | App já envia `collection-item` no commit `09137cf`. Servidor mantém as duas finalidades válidas; teste rejeitou LISTING_PHOTO e enviou collection-item ao MinIO real. Quatro testes do AssetClient passaram. Falta instalar versão atualizada no dispositivo. |
| 4. Fotos com URL vazia | Validado | `IMarketplaceAssets.GetUrl` recebe ownerId, e comandos/queries passam SellerUserId. Detalhe e vitrine retornaram URLs assinadas e downloads idênticos aos bytes enviados. Também corrigidos protocolo HTTP/HTTPS e endpoint público de assinatura, sem reescrever host depois de assinar. |
| 5. Scanner sem Bearer | Validado | ScannerClient tipado usa `AuthorizingHttpMessageHandler`; testes de cliente verificam autenticação e query. Falta validação atual no dispositivo físico. |
| 6. Dados eng/por ausentes | Validado | Imagem instala Tesseract e dados eng/por. OCR real reconheceu Pikachu 58, Charizard 4 e Professor Oak 88; testes de OCR Linux passaram. |
| 7. Limite nginx/foto original | Parcial | Exemplo nginx aceita 16 MB em scanner/identify e API limita imagem a 15 MiB. Captura reduz dimensões. Falta aplicar/validar a configuração no ingress real. |
| 8. Filtro de jogo perdido | Validado | ScannerClient envia gameCode na query e normaliza Pokémon para pokemon; testes de cliente e smoke real verificaram o fluxo. |

## 16.3 Regras incompletas

| Item | Estado | Evidência e próximo passo |
|---|---|---|
| 1. Reserva consumida imediatamente | Decisão | Continua consumida ao criar pedido; aguarda definição do prazo e comportamento ao expirar. Implementar worker/concorrência após a decisão. |
| 2. Sold permanente/um pedido por anúncio | Parcial | Cancelamento e reembolso confirmado antes do envio liberam o anúncio correspondente. Worker recupera gravações entre contexts em lotes e índice único filtrado permite recompra preservando histórico. Testes HTTP/PG comprovam nova compra e que repetição da liberação não desfaz a venda posterior. Expiração de reservas continua pendente. |
| 3. Pending sem expiração/cobrança duplicada | Parcial | Cadastro privado vincula cliente Asaas ao comprador; ID alheio é recusado. Registro único é reservado antes do POST. Resposta incerta é recuperada por consulta de cliente/referência/valor/modalidade, sem novo POST; busca vazia/ambígua exige conciliação. Pix e validade são persistidos/atualizados sem criar outra cobrança. Testes unitários e HTTP/PG exercitam esses casos. Expiração de pedidos e ferramenta operacional para operações ambíguas continuam pendentes. |
| 4. Entrega declarada pelo vendedor | Validado | Usuário escolheu confirmação do comprador. Shipping não altera recebimento do pedido. Teste HTTP cobre rastreio entregue pelo vendedor, rejeição de recebimento pelo vendedor e confirmação/repetição pelo comprador. App oferece confirmação apenas ao comprador de pedido enviado. Não há confirmação automática. |
| 5. Item não bloqueado/transferido | Validado | Anúncio valida dono/printing/variante/condição e vincula a unidade; alterações/remoção/fotos privadas ficam bloqueadas. Publicação e cancelamento têm recuperação durável. Recebimento do comprador cria uma única posse nova em transação, preservando histórico privado do vendedor. HTTP/PG comprovou replay, privacidade, recuperação e disputa de compradores; migração preserva dados antigos e recusa propriedade ambígua. App mostra bloqueio e compilou; dispositivo/publicação pendentes. Veja [Lifecycle](collection-listing-lifecycle.md). |
| 6. Suspensão/exclusão/refund sem caminho | Parcial | Vitrine e consulta de anúncio comprável filtram SellerProfile ativo. Reembolso integral antes do envio está implementado e testado com Asaas simulado; configuração operacional pendente. Faltam caminhos administrativos/conta e suspensão propagada de Identity. |
| 7. Vendedor não verificado | Parcial | Destino escolhido: chave Pix verificada. Consulta Asaas registra titular/documento e mantém chave pendente até revisão independente de identidade por operador autorizado no servidor, com evidência e versão. Alteração invalida revisão; documento mascarado/checkbox do vendedor não verifica identidade. Onboarding/KYC completo, coleta de evidências e regras de origem continuam pendentes; não exige subconta Asaas. |
| 8. Nota zero/papéis misturados | Validado | Papel da pessoa avaliada é definido pelo pedido no servidor. Média/quantidade de SELLER são agregadas no PostgreSQL; perfil, vitrine e detalhe usam a mesma projeção. Migration classifica avaliações antigas por participantes/pedido entregue e preserva UNKNOWN fora da média. Testes HTTP/PG e migration comprovaram separação, permissões, repetição e notas sem avaliações. App deixou de exibir preço/reputação fictícios em anúncios reais; validação física ainda necessária. |
| 9. Unique retorna 500 | Parcial | ApiExceptionHandler traduz PostgreSQL 23505 e concorrência para 409 sem detalhe SQL. Testes HTTP de avaliações e pedidos sincronizam consultas antes dos INSERTs e disputam índices reais: um 201, um 409 e um único registro. Vínculos concorrentes de assets ainda precisam cenário específico. |
| 10. Endereço incompleto/inconsistente | Parcial | Número, complemento, bairro e destinatário adicionados a perfil/pedido; CEP validado por formato nos três módulos. Migrations incrementais completadas. Perfil e pedido usam cidade 200; origem do vendedor ainda requer alinhamento. Obrigatoriedade aguarda decisão. |
| 11. Frete informativo/transportadora livre | Decisão | Escolher cálculo/informado/à parte e integração ou lista de transportadoras. Ainda não integra o total cobrado. |
| 12. Saque incompleto | Substituído | Usuário retirou saldo e saque do produto. App mostra repasses por pedido e Pix; rota de saque antiga responde 410 sem débito. Repasse usa obrigação única por pedido, claim durável antes do único POST, eventos autenticados e busca de conciliação; timeout/busca vazia não autoriza reenvio. Operação financeira real ainda não habilitada. |
| 13. Paginação sem máximo | Validado | Helper Pagination limita a 100 e valida overflow do offset em Marketplace, Orders e Wallets. Quatro testes de limites passaram e HTTP da vitrine normalizou pageSize=10000 para 100. |
| 14. Exclusão/limpeza de Assets | Pendente | Falta exclusão autorizada com vínculos e limpeza de uploads abandonados; não confundir remoção do vínculo com remoção do objeto. |
| 15. Contas antigas sem carteira | Substituído | Usuário decidiu não criar carteiras, incluindo contas antigas. Consumer de registro foi retirado e não há backfill. Dados históricos preservados; teste real comprovou vendedor novo recebendo registro de repasse sem carteira. |

## 16.4 Documentação e código

| Item | Estado | Evidência e próximo passo |
|---|---|---|
| 1. README três módulos/rotas faltantes | Ajustado | README lista os dez módulos, App/Core e rotas de endereço, avaliações e comércio, com limitações explícitas. |
| 2. Copilot sem app/módulos | Ajustado | Instruções atualizadas para MAUI, dez schemas e consumers existentes; regras de arquitetura preservadas. |
| 3. ADR 002 condição/paginação | Ajustado | Documenta sete códigos fechados e a limitação real da ordenação global por nome em memória. Otimização desse caminho continua pendente. |
| 4. ADR 001 fallback/provider/idioma | Parcial | Resolver externo filtra provider e revisão registra implementação atual. Fallback canônico e política de aliases regionais continuam pendentes; `pt` não deve receber região por inferência. |
| 5. JWT opção não usada | Ajustado | appsettings explicita AccessTokenMinutes=15 e RefreshTokenDays=30; ExpirationMinutes removido. |
| 6. Seed/segredos versionados | Parcial | Seed exige Admin:SeedPassword, valida e não imprime senha; JWT/DB/API key removidos de appsettings. Não remove valores do histórico Git nem troca credenciais existentes. Rotação operacional ainda necessária. |
| 7. Privacidade sem funcionalidades | Pendente | Exclusão, exportação e recuperação por e-mail não existem; documentos de loja e ViaCEP precisam revisão alinhada à entrega. Não anunciar essas funções como prontas. |
| 8. SHA-256 condicional no README | Ajustado | README registra download/hash sempre, comparação quando checksum fornecido e URLs de leitura privadas temporárias. |

## 16.5 Decisões comerciais

| Decisão | Estado |
|---|---|
| Taxa de 8%: vendedor, comprador ou ambos | Confirmado: somente vendedor |
| Liberação dos valores | Confirmado: sem saldo/saque; repasse Pix direto após recebimento confirmado pelo comprador e liquidação |
| Reserva de 15 minutos e liberação ao expirar | Pergunta enviada; aguarda resposta |
| Permissões de cancelamento por estado e momento do reembolso | Confirmado: antes do envio, comprador/vendedor com reembolso; depois do envio, atendimento. Aguardar confirmação integral do provedor. |
| Confirmação de recebimento e eventual prazo automático | Confirmado: comprador; nenhum prazo automático definido |
| Dados obrigatórios/verificação do vendedor | Chave Pix verificada confirmada; detalhes de KYC/origem ainda em aberto |
| Composição e cobrança do frete | Em aberto |
| Campos obrigatórios do endereço e validação de CEP | Em aberto |

## Verificação e publicação

- API Release compilada; Commerce unitários: 7 aprovados em Linux. AssetClient: 4 aprovados no host. Testes de OCR, reconhecimento e conversão BRL anteriores: 13 aprovados em Linux; cliente do scanner: 3 aprovados.
- Testes HTTP usam PostgreSQL e MinIO temporários, sem volumes da aplicação: 4 aprovados, incluindo upload/confirm/anúncio/download. A primeira execução revelou migrations faltantes de endereço; elas foram adicionadas, sem ocultar o alerta de modelo pendente. Após isso todas as migrations dos dez módulos foram aplicadas com sucesso no banco de testes.
- Storage/paginação: 15 testes aprovados em Linux, incluindo endpoint acessível ao cliente, HTTP local, HTTPS e credenciais temporárias AWS. AssetClient: 4 aprovados com captura da finalidade enviada. MinIO de testes usa a mesma imagem fixa do Compose; a referência anterior do registry não estava disponível.
- Build Android arm64 de validação concluído sem avisos ou erros. Compose validado sem iniciar serviços; 7 testes dos scripts de operação aprovados com processos simulados. Scripts shell passam a exigir LF no Git, evitando falha de `pipefail` ao usar o checkout Windows em Linux.
- Migration Orders recusa explicitamente endereços legados com estado maior que 100 caracteres, sem truncar. Revisar esses registros antes de aplicá-la a um banco existente. Rollback das novas colunas remove seus dados; não executar automaticamente.
- CI já percorre `tests/*/*.csproj`, incluindo Commerce e App.Core. Não houve deploy, cobrança real, transferência, envio de e-mail ou instalação de app de produção nesta validação.
- Ainda faltam verificação em dispositivo físico e publicação das mudanças. Resultados de scanner já obtidos não provam que a versão atualmente instalada está atualizada.
- Coleção/anúncio: 58 testes Commerce e 7 cenários adicionais HTTP/PG passaram, incluindo migração normal/ambígua, disputa entre compradores e fotos reais em MinIO. A rodada anterior aprovou também os 11 cenários de checkout, reembolso e repasse; uma falha transitória de leitura MinIO e o teste sem Idempotency-Key foram corrigidos/reexecutados, sem mascarar falhas com retries no teste. Android compilou sem avisos/erros. Ainda falta recuperação entre criação de pedido e marcação do anúncio vendido, além da regra de expiração já pendente.
- Comércio/checkout: 57 testes Commerce, 7 de clientes de pagamento/repasse e 17 testes distintos HTTP/PostgreSQL passaram. Incluem cliente próprio, resposta perdida após criação da cobrança, Pix/validade, reembolso/recompra e avaliações separadas por papel com backfill preservando UNKNOWN. Três testes de avaliações passaram novamente com disputa determinística do índice. Android atual compilou sem avisos/erros. Provedor simulado e bancos temporários; nenhuma operação financeira real ou prova automatizada de KYC. Detalhes em [Repasse direto](seller-payouts.md) e [Avaliações](seller-reviews.md).
- Scanner por sessão/vídeo: leitura contínua implementada com mensagem discreta, estabilidade/mudança local da prévia, confirmação de ambiguidades e proteção contra repetição. Treze testes de sessão/leitura/vídeo e doze de reconhecimento/preço passaram em Linux. Gravação opcional, iniciada somente pelo botão; o microfone é solicitado nesse momento. O MP4 foi exportado no emulador Android 35 com áudio; frames de revelação comum/dourada e total final em BRL foram inspecionados após corrigir câmera, inclusão Java e atraso do encoder. Evidências em `artifacts/scanner/native-final` e `artifacts/scanner/session-continuous-tests.log`. Montinhos/reflexos reais, voz e compartilhamento ainda precisam de celular. APK normal arm64 gerado a pedido do usuário: `artifacts/apk/Vaulta-scanner-2026-10-01.apk`, assinatura verificada e compilação sem avisos/erros; novas rotas ainda aguardam deploy da API confirmado pelo usuário.
- Avaliação do acervo: quatro testes de cálculo e um teste HTTP/PostgreSQL passaram, incluindo todas as unidades além da primeira página, variantes, total parcial, itens anunciados/vendidos/removidos e acesso entre contas. Home/portfolio/detalhe usam a avaliação real em BRL. A atualização diária após 05h agora salva preços/conversões no PostgreSQL, reutiliza entre processos/reinícios e mantém os registros anteriores. Nove testes unitários e cinco HTTP/PostgreSQL passaram; migration gerada e sincronizada. Histórico temporal do acervo e publicação dessa mudança continuam pendentes. Evidências em `artifacts/scanner/daily-market-tests-final.log` e `docs/daily-market-prices.md`.
- Retorno do teste físico: usuário relatou falha de identificação e excesso de variações. Consultas públicas em 01/10/2026 encontraram apenas uma impressão de Pikachu/Charizard/Mewtwo/Bulbasaur, todas Base Set em inglês, e nenhuma de Miraidon. A rota de detalhes agora responde 401 sem login, mas a cobertura do catálogo ainda impede reconhecer expansões ausentes. Investigação e correções de acurácia em andamento; não tratar o scanner como aprovado em aparelho.
