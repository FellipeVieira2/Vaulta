---
name: ship-code
description: Descreva o que essa habilidade faz e quando usá-la. Inclua palavras-chave que ajudem os agentes a identificar as tarefas relevantes.
---

---
name: ship-code
description: Finaliza uma implementação com revisão de código, validação de segurança e qualidade, execução de build e testes, análise de code smells, criação de commit Git padronizado e push seguro para o repositório remoto. Use quando o usuário pedir para subir, entregar, commitar, publicar ou finalizar uma implementação.
---

# Ship Code

Esta skill representa o processo de entrega de código do projeto.

Ela deve ser utilizada quando o usuário solicitar algo como:

- subir o código;
- enviar as alterações;
- commitar;
- finalizar a implementação;
- entregar a task;
- publicar a branch;
- preparar código para review;
- abrir PR;
- fazer code review antes do commit.

O objetivo é impedir que código seja enviado ao repositório sem passar por uma validação mínima de qualidade.

---

# Regra principal

Nunca executar imediatamente:

```bash
git add .
git commit
git push
```

sem antes revisar e validar as alterações.

O fluxo obrigatório é:

```text
IMPLEMENTAÇÃO
      ↓
INSPEÇÃO DO GIT
      ↓
CODE REVIEW
      ↓
CODE SMELLS
      ↓
BUILD
      ↓
TESTES
      ↓
VALIDAÇÕES
      ↓
CORREÇÕES SEGURAS
      ↓
REVALIDAÇÃO
      ↓
COMMIT
      ↓
PUSH
      ↓
RESUMO DA ENTREGA
```

---

# 1. Entender o estado atual

Antes de qualquer operação Git, execute:

```bash
git status
```

Identifique:

- branch atual;
- arquivos modificados;
- arquivos adicionados;
- arquivos removidos;
- arquivos não rastreados.

Depois analise:

```bash
git diff
```

e:

```bash
git diff --cached
```

quando houver alterações staged.

---

# 2. Verificar branch

Descubra a branch atual:

```bash
git branch --show-current
```

Nunca faça alterações perigosas automaticamente em:

```text
main
master
production
prod
```

Se estiver em uma dessas branches, preserve o histórico e nunca execute force push.

Quando a política do projeto exigir feature branches, considere criar uma branch coerente antes de publicar alterações.

Exemplo:

```text
feature/catalog-import
fix/outbox-processing
refactor/card-search
chore/update-tests
```

Não crie branch nova desnecessariamente se o usuário já estiver trabalhando em uma branch apropriada.

---

# 3. Revisar todas as alterações

Realize um code review do diff antes do commit.

Analise cada arquivo alterado e identifique:

- bugs;
- comportamento inesperado;
- código duplicado;
- complexidade;
- problemas arquiteturais;
- erros de concorrência;
- problemas de async;
- problemas de Entity Framework;
- queries ineficientes;
- falta de validação;
- exposição de dados;
- riscos de segurança;
- ausência de testes;
- breaking changes;
- migrations incorretas;
- código morto;
- comentários temporários;
- logs inadequados.

A revisão deve considerar também o contexto ao redor do código alterado.

Não analisar somente linhas adicionadas isoladamente.

---

# 4. Procurar alterações acidentais

Identifique arquivos que não deveriam fazer parte do commit.

Exemplos:

```text
bin/
obj/
.vs/
*.user
*.suo
.env
.env.local
secrets.json
appsettings.Development.json
*.log
coverage/
TestResults/
```

Verifique se devem estar presentes no `.gitignore`.

Não commitar arquivos de ambiente local sem necessidade.

---

# 5. Segurança antes do commit

Procure explicitamente por possíveis secrets.

Exemplos:

```text
API keys
JWT secrets
database passwords
connection strings com senha
AWS keys
access tokens
refresh tokens
private keys
certificados privados
webhook secrets
```

Procure também padrões como:

```text
password=
secret=
token=
apikey=
api_key=
Bearer
BEGIN PRIVATE KEY
```

Se encontrar um secret real:

1. não commitar;
2. remover do diff;
3. utilizar configuração segura;
4. alertar no resumo.

Se o secret já tiver sido commitado anteriormente, não assumir que removê-lo do arquivo é suficiente.

Recomendar rotação da credencial.

---

# 6. Validar debug code

Procure código temporário como:

```csharp
Console.WriteLine(...)
Debug.WriteLine(...)
throw new Exception("test")
```

ou:

```text
TODO TEMP
HACK
FIXME
TEST
DEBUG
```

Avalie se pertence realmente à implementação.

Não remover TODO legítimo automaticamente.

---

# 7. Code Smells

Executar a análise definida pela skill:

```text
code-smells
```

Priorizar:

- Critical;
- High.

Problemas Critical devem impedir a publicação até serem resolvidos.

Problemas High devem ser corrigidos quando a correção for segura e estiver dentro do escopo.

Medium e Low não devem bloquear automaticamente a entrega.

---

# 8. Revisão arquitetural

Para este projeto, verificar principalmente:

## CQRS

Confirmar:

```text
Command → alteração de estado
Query → leitura
```

Queries não devem modificar estado.

Handlers não devem se tornar God Classes.

---

## Domain

Verificar se regras importantes estão no domínio quando apropriado.

Evitar lógica de negócio espalhada por:

```text
Controllers
Endpoints
Infrastructure
```

---

## Infrastructure

Verificar:

- DbContext;
- migrations;
- providers externos;
- Outbox;
- mensageria;
- persistência.

---

## API

Endpoints devem permanecer pequenos.

Não adicionar regra de negócio diretamente na API.

---

# 9. Entity Framework

Para código alterado envolvendo EF Core, verificar:

- `AsNoTracking` para leitura quando apropriado;
- N+1;
- `Include` excessivo;
- consultas dentro de loops;
- materialização precoce;
- múltiplos SaveChanges desnecessários;
- concorrência;
- transações;
- CancellationToken;
- índices necessários.

---

# 10. Async

Nunca introduzir:

```csharp
.Result
.Wait()
.GetAwaiter().GetResult()
```

sem motivo extremamente específico.

Propagar:

```csharp
CancellationToken
```

quando aplicável.

---

# 11. Outbox e eventos

Caso alterações envolvam eventos:

Verificar:

- persistência atômica;
- idempotência;
- retry;
- duplicidade;
- status da mensagem;
- tratamento de falhas;
- concorrência entre workers.

Não permitir que uma mensagem seja marcada como concluída antes do processamento efetivamente finalizar.

---

# 12. Migrations

Verifique se modelos persistidos foram alterados.

Se houver mudança estrutural que exija migration, confirme que uma migration correspondente existe.

Analise o conteúdo da migration.

Procure alterações perigosas como:

```text
DROP TABLE
DROP COLUMN
ALTER COLUMN destrutivo
remoção de índice importante
```

Não assumir que migration gerada automaticamente é correta.

---

# 13. Build

Execute:

```bash
dotnet build
```

O build precisa terminar com sucesso.

Se falhar:

1. investigue;
2. corrija quando estiver relacionado às alterações;
3. execute novamente.

Não criar commit com build quebrado, salvo se explicitamente solicitado pelo usuário.

---

# 14. Testes

Execute:

```bash
dotnet test
```

Todos os testes relevantes devem passar.

Registrar:

```text
total
passed
failed
skipped
```

Exemplo:

```text
Tests: 48
Passed: 48
Failed: 0
Skipped: 0
```

Se algum teste falhar:

- determine se a falha foi causada pela alteração;
- corrija quando possível;
- execute novamente.

Não esconder testes quebrados.

---

# 15. Testes novos

Para código novo, verifique se existem testes adequados.

Especialmente para:

- regras de negócio;
- validações;
- handlers;
- autorização;
- persistência;
- eventos;
- concorrência;
- idempotência.

Não exigir teste unitário para código trivial sem comportamento.

---

# 16. Testes de integração

Quando a alteração envolver:

- PostgreSQL;
- migrations;
- EF Core;
- endpoints;
- autenticação;
- Outbox;
- integrações internas;

execute os testes de integração existentes.

Quando o projeto possuir ambiente Docker para integração, utilize-o quando necessário.

---

# 17. Docker

Se a alteração afetar infraestrutura ou execução da aplicação, valide:

```bash
docker compose up --build -d
```

Quando aplicável, verificar:

```text
API iniciou
PostgreSQL iniciou
migrations aplicadas
readiness saudável
health saudável
```

Depois executar smoke tests disponíveis.

Não subir containers sem necessidade para alterações puramente locais que já estejam suficientemente cobertas por testes.

---

# 18. Smoke test

Quando a API for iniciada, verificar endpoints básicos quando existirem.

Exemplos:

```text
/health
/health/ready
/swagger
```

E, dependendo da alteração:

```text
registro
login
endpoint alterado
fluxo principal da feature
```

---

# 19. Corrigir problemas encontrados

Pode corrigir automaticamente problemas seguros como:

- null checks;
- CancellationToken;
- async incorreto;
- naming privado;
- duplicação pequena;
- structured logging;
- LINQ ineficiente;
- guard clauses;
- imports;
- warnings;
- testes relacionados à alteração.

Não realizar refatoração massiva antes de um commit sem necessidade.

---

# 20. Revalidar após correções

Sempre que código for alterado durante o review, execute novamente pelo menos:

```bash
dotnet build
dotnet test
```

Não utilizar resultados anteriores como prova de que a versão final funciona.

---

# 21. Revisar diff final

Antes de criar o commit, executar novamente:

```bash
git diff
```

ou:

```bash
git diff HEAD
```

Verificar se o diff final corresponde exatamente ao objetivo da tarefa.

Pergunta interna obrigatória:

```text
Eu consigo explicar por que cada arquivo deste commit foi alterado?
```

Se não, investigar.

---

# 22. Commit atômico

O commit deve representar uma mudança coerente.

Evitar misturar:

```text
feature
+
refactor não relacionado
+
formatação do projeto inteiro
+
configuração aleatória
```

Se houver mudanças independentes, considerar commits separados.

---

# 23. Conventional Commits

Utilizar preferencialmente Conventional Commits.

Formatos permitidos:

```text
feat:
fix:
refactor:
perf:
test:
docs:
chore:
build:
ci:
```

Exemplos:

```text
feat: add pokemon catalog provider
```

```text
feat(catalog): implement card search query
```

```text
fix(outbox): prevent duplicate message processing
```

```text
perf(catalog): optimize card search query
```

```text
test(auth): add integration tests for login flow
```

---

# 24. Escolher mensagem de commit

A mensagem deve explicar a mudança e não a atividade realizada.

Evitar:

```text
update
changes
fix stuff
alteracoes
commit
final
teste
ajustes
```

Preferir:

```text
feat(catalog): add pokemon card synchronization
```

Não mencionar ferramentas ou agentes.

Evitar mensagens como:

```text
implemented by Copilot
AI generated changes
ChatGPT changes
```

---

# 25. Commit body

Para mudanças relevantes, considere corpo explicativo.

Exemplo:

```text
feat(catalog): add pokemon catalog synchronization

- add ICatalogProvider abstraction
- implement Pokemon provider
- normalize card and set data
- persist external provider identifiers
- add incremental synchronization
```

Não criar corpo enorme para alterações triviais.

---

# 26. Stage

Adicionar apenas arquivos pertencentes à mudança.

Preferir staged consciente.

Verificar:

```bash
git status
```

antes do commit.

Evitar `git add .` cegamente quando houver arquivos não relacionados.

---

# 27. Criar commit

Após validações:

```bash
git commit -m "..."
```

Quando houver corpo:

```bash
git commit
```

com mensagem adequada.

---

# 28. Verificar commit

Após criar o commit, verificar:

```bash
git status
```

e:

```bash
git log -1 --stat
```

Confirme:

- commit criado;
- arquivos corretos;
- working tree esperado.

---

# 29. Sincronizar com remoto

Antes do push, quando houver branch remota existente:

```bash
git fetch
```

Avalie se existem alterações remotas relevantes.

Evite sobrescrever trabalho remoto.

Nunca executar automaticamente:

```bash
git push --force
```

ou:

```bash
git push -f
```

---

# 30. Push

Se a branch já possuir upstream:

```bash
git push
```

Caso seja uma branch nova:

```bash
git push -u origin <branch>
```

---

# Regra de force push

Não utilizar force push por padrão.

Se for realmente necessário, somente com autorização explícita.

Mesmo com autorização, preferir:

```bash
git push --force-with-lease
```

em vez de:

```bash
git push --force
```

---

# 31. Pull Requests

Quando o fluxo do projeto utilizar Pull Requests e a ferramenta estiver disponível, depois do push pode preparar ou criar um PR.

O PR deve possuir:

## Título

Curto e objetivo.

Exemplo:

```text
Add Pokémon catalog synchronization
```

## Descrição

Usar estrutura semelhante a:

```markdown
## Summary

- Added catalog provider abstraction
- Added Pokémon provider
- Added incremental synchronization
- Added card normalization

## Validation

- dotnet build ✅
- dotnet test ✅
- 48/48 tests passing

## Notes

No database-breaking changes.
```

---

# 32. PR Review

Antes de criar PR, considerar o diff completo entre a branch e sua base.

Exemplo:

```bash
git diff origin/main...HEAD
```

Isso é diferente de analisar apenas o último commit.

A revisão deve considerar tudo que será incluído no PR.

---

# 33. Nunca alterar histórico sem necessidade

Evitar automaticamente:

```text
git reset --hard
git rebase
git commit --amend
git push --force
```

Essas operações podem destruir ou sobrescrever trabalho.

Somente utilizar quando fizer parte explícita da tarefa e o impacto estiver compreendido.

---

# 34. Não apagar trabalho do usuário

Nunca descartar mudanças locais não relacionadas.

Não executar:

```bash
git checkout -- .
git restore .
git clean -fd
git reset --hard
```

como forma de “limpar” o repositório.

Preservar trabalho existente.

---

# 35. Resumo final

Depois do push, apresentar um resumo.

Formato recomendado:

```text
Entrega concluída

Branch:
feature/catalog

Commit:
abc1234 feat(catalog): add pokemon catalog synchronization

Code review:
Critical: 0
High: 0
Medium: 2
Low: 1

Validação:
Build: PASS
Tests: 48/48 PASS
Integration tests: PASS
Docker: PASS
Smoke test: PASS

Push:
origin/feature/catalog atualizado
```

Não afirmar que algo passou se não tiver sido executado.

---

# 36. Problemas não bloqueantes

Se existirem problemas Medium ou Low não corrigidos, mencioná-los.

Exemplo:

```text
Pendências não bloqueantes:

- CardSearchHandler possui complexidade moderada.
- Mapper possui duplicação pequena.
```

Não esconder dívida técnica encontrada.

---

# 37. Problemas bloqueantes

Interromper publicação quando houver:

- build quebrado;
- testes relevantes quebrados;
- secret no diff;
- vulnerabilidade evidente;
- perda de dados;
- migration destrutiva não intencional;
- code smell Critical;
- conflito Git não resolvido;
- alteração claramente fora do escopo.

Resolver quando possível antes de continuar.

---

# 38. Exceções

O usuário pode explicitamente solicitar algo como:

```text
commita mesmo com os testes quebrados
```

Nesse caso a ação pode continuar se for tecnicamente segura, mas o commit e o resumo devem deixar claro que a validação falhou.

Não ocultar a situação.

Secrets e operações destrutivas continuam exigindo cuidado especial.

---

# 39. Escopo da revisão

Ao revisar uma implementação, priorizar:

```text
1. Correção
2. Segurança
3. Integridade dos dados
4. Concorrência
5. Arquitetura
6. Performance
7. Testabilidade
8. Manutenibilidade
9. Estilo
```

Não gastar mais esforço discutindo formatação do que bugs reais.

---

# 40. Definition of Done

Uma entrega padrão é considerada pronta quando:

```text
[✓] alterações revisadas
[✓] nenhum secret encontrado
[✓] code smells analisados
[✓] build passou
[✓] testes passaram
[✓] migrations verificadas
[✓] diff final revisado
[✓] commit criado
[✓] commit possui mensagem significativa
[✓] push realizado
```

Quando aplicável:

```text
[✓] testes de integração passaram
[✓] Docker validado
[✓] smoke test passou
[✓] PR criado
```

---

# Regra final

A missão desta skill não é simplesmente executar Git.

A missão é transformar:

```text
"terminei de programar"
```

em:

```text
"essa mudança foi revisada, validada, versionada e publicada com segurança"
```

Nunca priorizar velocidade de push sobre integridade do código ou do histórico Git.