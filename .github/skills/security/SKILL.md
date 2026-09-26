---
name: security
description: Descreva o que essa habilidade faz e quando usá-la. Inclua palavras-chave que ajudem os agentes a identificar as tarefas relevantes.
---

---
name: security-review
description: >
  Executa revisão obrigatória de segurança em APIs, backend, frontend,
  autenticação, uploads, banco de dados e infraestrutura antes de considerar
  uma implementação pronta para produção.
---

# Security Review

Você é responsável por realizar uma revisão de segurança do código antes
de considerar uma feature concluída.

A análise deve considerar OWASP, princípio do menor privilégio, defesa em
profundidade e "never trust user input".

Não aprove código apenas porque ele funciona.

## 1. Secrets e variáveis de ambiente

Verifique se existem:

- senhas no código;
- connection strings;
- API keys;
- JWT secrets;
- tokens;
- credentials AWS/Azure/GCP;
- chaves privadas;
- secrets em appsettings.json;
- secrets em arquivos versionados;
- secrets enviados para frontend/mobile;
- secrets registrados em logs.

Secrets devem vir de mecanismos seguros de configuração.

Nunca colocar segredo real em:

- repositório;
- código fonte;
- frontend;
- aplicativo MAUI;
- logs;
- mensagens de erro.

Se um segredo já tiver sido commitado, remover do código NÃO é suficiente:
recomendar rotação/revogação da credencial.

---

## 2. Validação de entrada

Toda entrada externa deve ser considerada não confiável.

Validar no backend:

- body;
- query string;
- route parameters;
- headers relevantes;
- uploads;
- webhooks;
- dados provenientes de APIs externas.

Validar:

- tipo;
- formato;
- tamanho;
- limites;
- valores permitidos;
- enums;
- ranges;
- quantidade de itens.

Validação de frontend é apenas UX e nunca substitui validação backend.

---

## 3. Injection

Procurar vulnerabilidades de:

- SQL Injection;
- Command Injection;
- LDAP Injection;
- NoSQL Injection;
- template injection;
- path traversal.

Não concatenar input do usuário em SQL.

Preferir:

- EF Core;
- LINQ;
- queries parametrizadas;
- prepared statements.

Para SQL manual, exigir parâmetros.

---

## 4. Autenticação

Verificar:

- endpoints que deveriam exigir autenticação;
- tokens expirados;
- validação de issuer;
- validação de audience;
- assinatura;
- lifetime;
- refresh tokens;
- revogação;
- MFA quando aplicável.

Não aceitar JWT apenas porque ele consegue ser decodificado.

---

## 5. Autorização

Autenticação != autorização.

Cada operação sensível deve verificar se o usuário possui permissão
para executar aquela ação.

Procurar especialmente:

- acesso administrativo;
- alteração de dados;
- exclusão;
- dados financeiros;
- informações privadas;
- recursos pertencentes a outro usuário.

---

## 6. IDOR / BOLA

Sempre revisar endpoints como:

GET /resource/{id}
PUT /resource/{id}
DELETE /resource/{id}

Nunca assumir que conhecer o ID significa possuir autorização.

Validar ownership ou autorização apropriada.

Exemplo perigoso:

GetCardCollection(collectionId)

O usuário autenticado pode tentar collectionId pertencente a outro usuário.

---

## 7. Senhas

Nunca armazenar senha em texto puro.

Usar algoritmo adequado de password hashing.

Preferir mecanismos consolidados do framework, como ASP.NET Core Identity.

Nunca:

- criptografar senha de forma reversível;
- registrar senha;
- retornar senha;
- enviar hash ao cliente.

---

## 8. Brute Force

Endpoints sensíveis devem possuir proteção adequada:

- login;
- recuperação de senha;
- MFA;
- cadastro;
- códigos de verificação;
- APIs públicas caras;
- scanner;
- operações financeiras.

Avaliar:

- rate limiting;
- lockout;
- throttling;
- proteção contra enumeração de usuários.

---

## 9. Uploads

Nunca confiar em:

- extensão;
- Content-Type informado pelo cliente;
- nome original do arquivo.

Validar:

- tamanho;
- extensão permitida;
- assinatura/magic bytes quando necessário;
- quantidade;
- destino;
- nome gerado pelo servidor.

Impedir path traversal.

Não permitir execução de arquivos enviados.

Para imagens, considerar reprocessamento seguro.

---

## 10. CSRF

Para autenticação baseada em cookies, verificar proteção CSRF.

Analisar:

- POST;
- PUT;
- PATCH;
- DELETE.

Avaliar:

- SameSite;
- anti-forgery token;
- origem da requisição.

APIs usando Authorization Bearer possuem modelo diferente de risco e
não devem receber correções CSRF cegamente.

---

## 11. Information Disclosure

Não retornar ao cliente:

- stack traces;
- SQL;
- connection strings;
- caminhos internos;
- nomes internos desnecessários;
- secrets;
- tokens;
- detalhes de infraestrutura;
- exception.ToString().

Produção deve retornar erros controlados.

Detalhes técnicos ficam em observabilidade/logs protegidos.

---

## 12. Dependências vulneráveis

Verificar:

- NuGet;
- npm;
- SDKs;
- Docker images;
- GitHub Actions;
- bibliotecas abandonadas.

Executar ferramentas disponíveis de análise de vulnerabilidade.

Para .NET considerar:

dotnet list package --vulnerable --include-transitive

Não atualizar major versions automaticamente sem analisar breaking changes.

---

## 13. Tokens

Tokens devem possuir:

- expiração adequada;
- assinatura segura;
- validação correta;
- finalidade limitada.

Refresh tokens devem receber proteção adicional.

Nunca armazenar tokens sensíveis desnecessariamente em logs.

Para mobile, não armazenar tokens sensíveis em armazenamento inseguro
quando houver mecanismo seguro da plataforma disponível.

---

## 14. Dados sensíveis

Nunca expor dados sensíveis em:

- URL;
- logs;
- analytics;
- exceptions;
- telemetry;
- frontend;
- responses desnecessárias.

Aplicar data minimization:

retornar somente os campos necessários para aquela operação.

---

## 15. Rate Limiting

Avaliar rate limit principalmente para:

- login;
- registro;
- busca;
- scanner;
- upload;
- geração de relatórios;
- integrações externas;
- endpoints públicos;
- endpoints computacionalmente caros.

Considerar limites por:

- usuário;
- API key;
- IP;
- endpoint.

Não usar apenas IP quando usuários autenticados puderem ser identificados.

---

## 16. SSRF

Toda URL fornecida externamente deve ser considerada perigosa.

Evitar que o servidor consiga acessar arbitrariamente:

- localhost;
- 127.0.0.1;
- ::1;
- redes privadas;
- metadata services;
- serviços internos.

Quando possível, usar allowlist de hosts.

Cuidado especial com:

- importação por URL;
- imagens remotas;
- webhooks;
- callbacks;
- proxies.

---

## 17. Cookies

Quando cookies forem utilizados, avaliar:

- Secure;
- HttpOnly;
- SameSite;
- domínio;
- path;
- expiração.

Cookies de autenticação nunca devem ser acessíveis por JavaScript sem
necessidade explícita.

---

## 18. CORS

Nunca usar política permissiva sem justificativa.

Revisar especialmente:

AllowAnyOrigin()

e combinações envolvendo credentials.

Produção deve possuir origens explicitamente controladas quando necessário.

---

## 19. Mass Assignment / Overposting

Não vincular entidades persistidas diretamente ao body quando isso permitir
alteração de campos internos.

Evitar:

Update(entityFromRequest)

quando o cliente puder controlar propriedades como:

- UserId;
- OwnerId;
- Role;
- IsAdmin;
- Status interno;
- Price;
- CreatedAt;
- Approved;
- Balance.

Usar DTOs específicos e mapear explicitamente campos permitidos.

---

# Verificações adicionais obrigatórias

## Broken Access Control

Revisar qualquer mudança envolvendo permissões.

Perguntar:

"Um usuário comum consegue executar isso manipulando diretamente a API?"

---

## XSS

Para conteúdo exibido em WebView ou aplicação web:

- não confiar em HTML externo;
- escapar conteúdo;
- sanitizar HTML quando HTML for realmente permitido;
- evitar execução dinâmica de scripts.

---

## Path Traversal

Nunca construir caminhos diretamente com input externo.

Procurar entradas contendo:

../
..\
caminhos absolutos

---

## Logs

Logs não podem conter:

- senha;
- Authorization header;
- JWT completo;
- refresh token;
- API key;
- dados pessoais desnecessários.

Logs devem possuir informação suficiente para investigação sem vazar
credenciais.

---

## Race Conditions

Para operações críticas, verificar concorrência.

Especialmente:

- pagamentos;
- marketplace;
- estoque;
- compra/venda;
- saldo;
- criação idempotente;
- sincronizações.

Nunca confiar apenas em:

if (!exists)
    insert();

quando múltiplas requisições podem ocorrer simultaneamente.

Usar constraints, transações, locks ou mecanismos idempotentes adequados.

---

## Idempotência

Operações financeiras, webhooks e integrações devem avaliar idempotência.

A mesma requisição recebida duas vezes não deve gerar duas:

- cobranças;
- vendas;
- transferências;
- movimentações;
- notificações críticas.

---

## Segurança de integrações externas

Para APIs externas:

- timeout;
- cancellation token;
- retry controlado;
- circuit breaker quando adequado;
- limites de resposta;
- validação do payload;
- autenticação segura.

Nunca confiar que uma API externa sempre retornará dados válidos.

---

# Processo obrigatório

Antes de concluir uma implementação:

1. identificar a superfície de ataque alterada;
2. executar os checks relevantes desta skill;
3. procurar vulnerabilidades no código modificado;
4. verificar configurações relacionadas;
5. verificar dependências quando aplicável;
6. verificar testes de segurança existentes;
7. criar testes para vulnerabilidades relevantes;
8. corrigir problemas críticos/altos antes de considerar a tarefa pronta.

Não realizar mudanças grandes e silenciosas apenas para "melhorar segurança".

Se uma correção alterar arquitetura, contrato público ou comportamento
esperado, explicar primeiro o impacto.

---

# Classificação

Classifique cada descoberta como:

CRITICAL
HIGH
MEDIUM
LOW
INFO

Para cada vulnerabilidade encontrada informar:

- severidade;
- arquivo;
- linha ou região;
- vulnerabilidade;
- cenário de exploração;
- impacto;
- correção recomendada;
- teste necessário.

Formato:

[HIGH] IDOR em CollectionEndpoints

Arquivo:
CollectionEndpoints.cs

Problema:
O endpoint recebe collectionId e retorna a coleção sem verificar ownership.

Exploração:
Usuário autenticado altera collectionId e acessa coleção pertencente
a outro usuário.

Correção:
Filtrar Collection por Id + UserId obtido da identidade autenticada.

Teste:
Usuário A tenta consultar Collection de usuário B e recebe 403/404.

---

# Security Gate

Uma tarefa NÃO deve ser considerada pronta quando houver vulnerabilidade
CRITICAL ou HIGH conhecida introduzida ou mantida pelo código alterado.

Ao finalizar, produzir:

SECURITY REVIEW

Critical: X
High: X
Medium: X
Low: X

Status:
PASS
ou
BLOCKED

Principais descobertas:
...

Testes executados:
...

Riscos residuais:
...