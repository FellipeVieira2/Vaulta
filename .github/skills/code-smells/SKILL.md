---
name: code-smells
description: Descreva o que essa habilidade faz e quando usá-la. Inclua palavras-chave que ajudem os agentes a identificar as tarefas relevantes.
---

---
name: code-smells
description: Analisa código C#/.NET em busca de code smells, problemas de manutenibilidade, complexidade, acoplamento, arquitetura, performance, segurança e violações de boas práticas. Use esta habilidade ao criar, alterar, revisar ou refatorar código, especialmente em APIs .NET, CQRS, Entity Framework Core, handlers, services, repositories, controllers, domain models e testes.
---

# Code Smells Validator

Esta habilidade deve ser utilizada para revisar código C#/.NET procurando problemas de qualidade antes de considerar uma implementação concluída.

A análise não deve se limitar a estilo ou formatação. Deve identificar problemas que possam afetar:

- manutenção;
- legibilidade;
- testabilidade;
- escalabilidade;
- performance;
- segurança;
- arquitetura;
- confiabilidade;
- evolução futura do sistema.

Sempre que código for criado ou modificado, execute esta validação antes de finalizar a tarefa.

---

# Objetivo

Encontrar e, quando seguro, corrigir code smells presentes no código.

A análise deve considerar principalmente:

1. complexidade;
2. duplicação;
3. responsabilidades;
4. acoplamento;
5. coesão;
6. tratamento de erros;
7. async/await;
8. Dependency Injection;
9. CQRS;
10. Entity Framework Core;
11. APIs HTTP;
12. domínio;
13. segurança;
14. performance;
15. testabilidade;
16. observabilidade.

Não altere comportamento funcional apenas para satisfazer uma regra de estilo.

---

# Fluxo obrigatório

Ao finalizar uma implementação:

1. identifique os arquivos alterados;
2. analise os arquivos alterados;
3. analise dependências diretamente relacionadas;
4. procure code smells;
5. classifique os problemas encontrados;
6. corrija automaticamente problemas seguros;
7. execute build;
8. execute testes;
9. execute analisadores disponíveis no projeto;
10. informe os problemas encontrados e as correções realizadas.

Evite refatorações grandes que não sejam necessárias para a tarefa atual.

---

# Classificação

Classifique os problemas encontrados como:

## Critical

Problemas que podem gerar:

- corrupção de dados;
- vulnerabilidade;
- inconsistência transacional;
- race condition;
- deadlock;
- falha grave de domínio;
- exposição de dados sensíveis;
- comportamento incorreto em produção.

Devem ser corrigidos antes de considerar a tarefa concluída.

## High

Problemas importantes de arquitetura ou manutenção.

Exemplos:

- God Class;
- método extremamente complexo;
- acesso incorreto ao banco;
- N+1 queries;
- dependências fortemente acopladas;
- ausência de CancellationToken em fluxos relevantes;
- regras de negócio dentro de controllers;
- chamadas síncronas bloqueando código assíncrono.

Devem preferencialmente ser corrigidos.

## Medium

Problemas que aumentam dívida técnica.

Exemplos:

- métodos grandes;
- duplicação;
- nomes pouco claros;
- excesso de parâmetros;
- abstrações desnecessárias;
- validação duplicada.

Corrigir quando a mudança for segura e localizada.

## Low

Melhorias de legibilidade ou estilo.

Não faça grandes mudanças exclusivamente por problemas Low.

---

# Regras gerais de código

Verifique os seguintes smells.

## Métodos grandes

Métodos muito grandes normalmente indicam múltiplas responsabilidades.

Considere refatoração quando houver:

- muitas condições;
- múltiplos níveis de indentação;
- diferentes responsabilidades;
- mais de uma operação de negócio relevante;
- dificuldade para nomear o método de forma objetiva.

Prefira extrair métodos com nomes que revelem intenção.

---

## Complexidade excessiva

Identifique:

- vários `if`;
- vários `switch`;
- loops aninhados;
- condições booleanas grandes;
- múltiplos retornos difíceis de acompanhar.

Evite:

```csharp
if (user != null &&
    user.IsActive &&
    user.Profile != null &&
    user.Profile.IsVerified &&
    !user.IsBlocked)
```

Considere encapsular regras:

```csharp
if (user.CanPerformOperation())
```

---

# Nesting excessivo

Evite estruturas profundamente aninhadas.

Prefira guard clauses.

Evite:

```csharp
if (user != null)
{
    if (user.IsActive)
    {
        if (user.HasPermission)
        {
            Process(user);
        }
    }
}
```

Prefira:

```csharp
if (user is null)
    return;

if (!user.IsActive)
    return;

if (!user.HasPermission)
    return;

Process(user);
```

---

# Duplicação

Procure lógica repetida em:

- handlers;
- services;
- controllers;
- validators;
- repositories;
- testes;
- mapeamentos.

Não crie abstrações prematuramente.

Extraia código somente quando existir uma abstração semanticamente válida.

Não aplique DRY ao ponto de criar dependências artificiais.

---

# Primitive Obsession

Identifique situações em que conceitos importantes do domínio estão representados apenas por:

- string;
- int;
- decimal;
- Guid.

Por exemplo:

```csharp
string email;
decimal money;
string document;
```

Quando fizer sentido para o domínio, considere Value Objects como:

```csharp
Email
Money
Document
```

Não crie Value Objects para dados triviais sem comportamento ou regras.

---

# Magic Numbers e Magic Strings

Evite:

```csharp
if (status == "ACTIVE")
```

Prefira:

```csharp
if (status == UserStatus.Active)
```

Valores relevantes ao domínio devem possuir significado explícito.

---

# Boolean Blindness

Evite métodos como:

```csharp
CreateUser(true, false, true);
```

Prefira parâmetros nomeados, enums, objetos de configuração ou tipos específicos.

---

# Muitos parâmetros

Métodos com muitos parâmetros podem indicar responsabilidade excessiva.

Analise principalmente métodos com mais de 5 parâmetros.

Considere:

- command;
- request object;
- options;
- value object.

Não agrupe parâmetros não relacionados apenas para reduzir quantidade.

---

# Dependency Injection

Procure:

- `new` de dependências externas dentro de services;
- Service Locator;
- acesso direto ao `IServiceProvider`;
- dependências estáticas mutáveis;
- construtores com dependências excessivas.

Evite:

```csharp
var service = serviceProvider.GetRequiredService<MyService>();
```

quando a dependência puder ser declarada diretamente:

```csharp
public MyHandler(IMyService service)
```

Muitas dependências em um construtor podem indicar que a classe possui responsabilidades demais.

---

# Lifetime de dependências

Valide incompatibilidades como:

- Singleton dependendo de Scoped;
- estado mutável em Singleton;
- DbContext sendo reutilizado incorretamente;
- serviços Scoped sendo armazenados globalmente.

---

# Async/Await

Procure:

```csharp
.Result
.Wait()
.GetAwaiter().GetResult()
```

em código assíncrono.

Esses padrões podem provocar bloqueios e deadlocks.

Prefira fluxo assíncrono completo:

```csharp
await operation;
```

---

# Async sem necessidade

Evite:

```csharp
public async Task<int> GetValue()
{
    return await Task.FromResult(10);
}
```

Prefira:

```csharp
public Task<int> GetValue()
{
    return Task.FromResult(10);
}
```

ou simplesmente código síncrono quando apropriado.

---

# CancellationToken

Operações de:

- banco;
- HTTP;
- mensageria;
- I/O;
- processamento potencialmente demorado

devem considerar `CancellationToken`.

Exemplo:

```csharp
await dbContext.Users
    .ToListAsync(cancellationToken);
```

Propague o token entre as camadas.

Não utilize `CancellationToken.None` sem necessidade.

---

# Fire and Forget

Procure:

```csharp
_ = SomeAsyncOperation();
```

ou chamadas async não aguardadas.

Só aceite fire-and-forget quando existir infraestrutura apropriada para processamento em background.

Para processamento confiável prefira:

- queue;
- background worker;
- outbox;
- message broker.

---

# Exception Handling

Evite:

```csharp
catch (Exception)
{
}
```

Nunca silencie exceções sem justificativa.

Evite:

```csharp
catch (Exception ex)
{
    throw ex;
}
```

Prefira:

```csharp
throw;
```

quando a exceção precisar ser propagada.

---

# Exceptions como fluxo de negócio

Não utilize exceptions para situações esperadas de negócio.

Exemplo:

```csharp
try
{
    var user = GetUser();
}
catch (UserNotFoundException)
{
}
```

Considere Result Pattern, erros de domínio ou retornos explícitos quando ausência for situação esperada.

---

# API Controllers

Controllers devem ser pequenos.

Devem principalmente:

1. receber request;
2. delegar operação;
3. retornar response.

Evite colocar nos controllers:

- regra de negócio;
- SQL;
- acesso direto complexo ao DbContext;
- transformação extensa;
- integração externa;
- lógica de autorização manual repetida.

---

# CQRS

Commands devem representar intenção.

Exemplos:

```text
CreateUserCommand
UpdateProfileCommand
DeleteCardCommand
CreateOrderCommand
```

Evite comandos genéricos como:

```text
UserCommand
ExecuteCommand
ProcessCommand
```

---

# Command Handlers

Handlers devem possuir uma responsabilidade clara.

Verifique handlers que:

- fazem múltiplas operações independentes;
- possuem lógica extremamente extensa;
- chamam muitos serviços;
- possuem muitos branches.

Não crie handlers apenas como pass-through para outra classe sem necessidade.

---

# Queries

Queries não devem alterar estado.

Nunca permita que uma Query:

- atualize entidade;
- publique evento de domínio mutável;
- altere status;
- persista informações.

Queries devem ser side-effect free sempre que possível.

---

# Commands

Commands representam alteração de estado.

Não retorne entidades inteiras desnecessariamente.

Prefira retornar:

- ID criado;
- DTO;
- resultado da operação.

---

# Domínio

Entidades devem proteger suas invariantes.

Evite:

```csharp
order.Status = OrderStatus.Paid;
```

quando existir uma regra de negócio associada.

Prefira:

```csharp
order.MarkAsPaid();
```

quando o comportamento fizer parte do domínio.

---

# Anemic Domain Model

Identifique entidades que possuem apenas:

- propriedades;
- getters;
- setters;

enquanto toda regra de negócio está em services.

Não transforme todo projeto obrigatoriamente em DDD rico.

Aplique encapsulamento principalmente onde existirem invariantes importantes.

---

# Entity Framework Core

Analise consultas procurando problemas de performance.

---

# N+1 Query

Identifique loops que consultem banco repetidamente.

Evite:

```csharp
foreach (var order in orders)
{
    var user = await dbContext.Users
        .FirstAsync(x => x.Id == order.UserId);
}
```

Considere:

- Include;
- projection;
- join;
- consulta em lote.

---

# Include excessivo

Evite carregar grafos inteiros quando apenas alguns campos são necessários.

Para queries prefira projection:

```csharp
.Select(x => new UserDto
{
    Id = x.Id,
    Name = x.Name
})
```

---

# AsNoTracking

Consultas somente leitura devem considerar:

```csharp
.AsNoTracking()
```

principalmente em listagens e consultas CQRS.

Não utilize quando tracking for necessário.

---

# Materialização precoce

Evite:

```csharp
var users = await query.ToListAsync();

return users
    .Where(...)
    .Select(...);
```

quando filtros puderem ser traduzidos para SQL.

Prefira aplicar operações antes de `ToListAsync`.

---

# SaveChanges

Evite múltiplos `SaveChangesAsync()` dentro da mesma operação de negócio sem necessidade.

Analise consistência transacional.

Exemplo suspeito:

```csharp
await db.SaveChangesAsync();

DoSomething();

await db.SaveChangesAsync();
```

---

# Transações

Quando múltiplas modificações precisarem ser atômicas, verifique se existe garantia transacional.

Não crie transações manualmente quando uma única chamada ao `SaveChanges` já fornecer atomicidade suficiente.

---

# DateTime

Evite utilizar horário local indiscriminadamente.

Para dados persistidos ou distribuídos prefira normalmente:

```csharp
DateTimeOffset.UtcNow
```

ou abstração de relógio:

```csharp
TimeProvider
```

Especialmente quando o código precisar ser testável.

---

# Guid

Evite gerar IDs em locais aleatórios do código quando isso prejudicar testes ou domínio.

Considere encapsular criação quando existir semântica de negócio.

---

# HttpClient

Evite:

```csharp
new HttpClient();
```

repetidamente.

Prefira `IHttpClientFactory`.

Analise:

- timeout;
- cancellation token;
- retries;
- resiliência;
- tratamento de status HTTP.

---

# Logging

Evite concatenação:

```csharp
_logger.LogInformation("User " + user.Id + " created");
```

Prefira structured logging:

```csharp
_logger.LogInformation(
    "User {UserId} created",
    user.Id);
```

---

# Dados sensíveis

Nunca registrar:

- senha;
- token JWT;
- refresh token;
- authorization header;
- API key;
- secrets;
- dados completos de cartão;
- informações sensíveis desnecessárias.

---

# Logs excessivos

Evite registrar informações em loops de alto volume sem necessidade.

Analise possibilidade de gerar:

- custo excessivo;
- volume de logs;
- degradação de performance.

---

# Segurança

Procure especialmente:

## SQL Injection

Nunca construir SQL com concatenação de dados recebidos.

## Mass Assignment

Não mapear automaticamente requests externos para entidades quando isso permitir alteração de campos protegidos.

## Sensitive Data Exposure

Não retornar:

- PasswordHash;
- tokens internos;
- secrets;
- propriedades internas;
- dados privados desnecessários.

Utilize DTOs.

## Authorization

Verifique endpoints que alterem ou exponham recursos pertencentes a usuários.

Não confie apenas no ID recebido no request.

Valide ownership quando necessário.

---

# Nullable Reference Types

Não utilize `!` indiscriminadamente apenas para eliminar warning.

Exemplo suspeito:

```csharp
user.Profile!.Name
```

Valide se o valor realmente é garantido pelo domínio.

---

# LINQ

Evite múltiplas enumerações desnecessárias:

```csharp
if (items.Any())
{
    foreach (var item in items)
```

principalmente quando `items` representar query remota.

Considere materialização quando necessário.

---

# First vs Single

Utilize conscientemente:

```csharp
First
FirstOrDefault
Single
SingleOrDefault
```

`Single` deve representar uma invariante de unicidade.

Não utilize apenas por preferência estética.

---

# Any vs Count

Evite:

```csharp
collection.Count() > 0
```

quando o objetivo for apenas saber se existem elementos.

Prefira:

```csharp
collection.Any()
```

quando apropriado.

---

# Strings

Evite normalizações repetidas como:

```csharp
value.ToLower() == other.ToLower()
```

Prefira comparação apropriada:

```csharp
string.Equals(
    value,
    other,
    StringComparison.OrdinalIgnoreCase);
```

quando semanticamente correto.

---

# Performance

Procure:

- alocações desnecessárias;
- consultas repetidas;
- serialização repetida;
- loops O(n²);
- chamadas de rede dentro de loops;
- chamadas ao banco dentro de loops;
- processamento duplicado;
- carregamento excessivo de dados.

Não faça micro-otimizações sem evidência.

Priorize problemas claros.

---

# Testabilidade

Identifique dependências difíceis de testar.

Exemplos:

```csharp
DateTime.UtcNow
Guid.NewGuid()
new HttpClient()
File.ReadAllText()
Environment.GetEnvironmentVariable()
```

Quando forem relevantes à regra de negócio, considere abstrações apropriadas.

Não crie interfaces sem necessidade apenas para aumentar o número de mocks.

---

# Testes

Ao encontrar uma regra importante sem cobertura, considere adicionar teste.

Priorize:

- regras de domínio;
- validações;
- autorização;
- fluxo financeiro;
- idempotência;
- estados;
- erros;
- edge cases.

Não escreva testes apenas para aumentar coverage.

---

# Arquitetura

Identifique violações de dependência.

Em arquiteturas em camadas, Domain não deve depender de:

- Infrastructure;
- Entity Framework;
- ASP.NET;
- implementações externas.

Application não deve depender desnecessariamente de Infrastructure.

Infrastructure pode implementar contratos definidos em camadas internas.

---

# Circular Dependencies

Não permitir dependências circulares entre projetos ou módulos.

---

# God Classes

Considere smell quando uma classe:

- possui muitas dependências;
- possui muitos métodos;
- possui muitas responsabilidades;
- conhece várias partes diferentes do sistema.

Antes de dividir, identifique responsabilidades reais.

Não crie dezenas de classes pequenas sem significado.

---

# Interfaces

Não crie interfaces automaticamente para todas as classes.

Interfaces devem existir quando houver:

- abstração relevante;
- múltiplas implementações;
- integração externa;
- boundary arquitetural;
- necessidade legítima de substituição.

Evite:

```text
UserService
IUserService
```

quando a interface não fornece nenhum benefício arquitetural.

---

# Repository Pattern

Se Entity Framework Core já estiver sendo utilizado como ORM, não crie repositories genéricos apenas para esconder o DbContext.

Evite:

```text
IGenericRepository<T>
Repository<T>
```

quando apenas reproduzirem:

```text
Add
Update
Delete
GetById
GetAll
```

Utilize repositories quando existir uma abstração de domínio ou consulta relevante.

---

# Mapper

Evite mapeamento complexo espalhado pelo sistema.

Entretanto, não introduza automaticamente AutoMapper ou outra biblioteca apenas para evitar algumas linhas explícitas.

Para mapeamentos importantes, prefira código claro e rastreável.

---

# Comentários

Comentários não devem explicar código confuso que poderia ser escrito de maneira clara.

Evite:

```csharp
// Incrementa contador
counter++;
```

Comentários são apropriados para explicar:

- decisão arquitetural;
- trade-off;
- workaround;
- regra externa;
- comportamento não óbvio;
- motivo de determinada solução.

---

# TODO

Procure:

```text
TODO
FIXME
HACK
TEMP
```

Avalie se representam dívida relevante.

Não remova TODOs apenas para passar na validação.

---

# Código morto

Remova quando seguro:

- métodos não utilizados;
- imports não utilizados;
- variáveis não utilizadas;
- branches impossíveis;
- código comentado antigo.

Não remova APIs públicas sem verificar consumidores.

---

# Ferramentas de validação

Quando disponíveis, execute:

```bash
dotnet build
```

Execute os testes:

```bash
dotnet test
```

Considere:

```bash
dotnet format --verify-no-changes
```

quando o projeto possuir configuração compatível.

Se existirem analyzers configurados no projeto, considere seus warnings como parte da análise.

Exemplos:

- .NET Analyzers;
- Roslyn Analyzers;
- StyleCop;
- SonarAnalyzer;
- Meziantou.Analyzer;
- Roslynator.

Não introduza uma nova dependência apenas para realizar a validação sem necessidade.

---

# Warnings

Não considere um build válido quando novos warnings relevantes forem introduzidos.

Dê atenção especial a warnings envolvendo:

- nullable;
- async;
- disposal;
- segurança;
- serialization;
- Entity Framework;
- APIs obsoletas.

---

# IDisposable

Analise corretamente recursos descartáveis.

Procure:

- streams;
- database connections;
- HttpResponseMessage;
- CancellationTokenSource;
- recursos unmanaged.

Utilize `using` ou ownership apropriado.

Não faça Dispose de dependências cuja vida útil pertence ao container de DI.

---

# API

Verifique endpoints procurando:

- retorno incorreto de status;
- exposição de exceptions;
- ausência de validação;
- inconsistência de DTO;
- breaking changes;
- ausência de paginação em coleções potencialmente grandes.

---

# Paginação

Evite endpoints como:

```text
GET /users
```

retornando todos os usuários quando a quantidade puder crescer indefinidamente.

Considere:

```text
page
pageSize
cursor
```

dependendo do caso.

---

# Idempotência

Para operações críticas, principalmente:

- pagamentos;
- pedidos;
- webhooks;
- eventos;
- integrações;

analise risco de processamento duplicado.

Quando necessário implemente mecanismo de idempotência.

---

# Mensageria

Quando houver eventos ou message broker, verifique:

- idempotência;
- retry;
- poison messages;
- duplicate delivery;
- observabilidade;
- atomicidade entre banco e publicação.

Quando banco e evento precisarem permanecer consistentes, considere Outbox Pattern.

---

# Outbox

Verifique:

- evento persistido na mesma transação da alteração;
- processamento idempotente;
- status de processamento;
- retry;
- falha parcial;
- concorrência entre workers.

Não marque mensagem como processada antes da operação realmente concluir.

---

# Event Handlers

Eventos devem representar algo que já aconteceu.

Exemplo:

```text
UserCreated
OrderPaid
CardListed
PaymentApproved
```

Commands representam intenção.

Exemplo:

```text
CreateUser
PayOrder
ListCard
```

Não misture esses conceitos.

---

# Resultado esperado

Ao finalizar a validação, produza um resumo parecido com:

```text
Code Smell Validation

Critical: 0
High: 1
Medium: 2
Low: 3

Corrigidos:
- removido acesso síncrono .Result;
- adicionada propagação de CancellationToken;
- eliminada consulta N+1 no OrderQuery;
- extraída regra de negócio do controller.

Mantidos:
- método X possui complexidade moderada, porém refatoração alteraria uma área fora do escopo atual.

Validação:
- dotnet build: PASS
- dotnet test: PASS
- 33/33 testes passaram
```

---

# Regra de alteração automática

Pode corrigir automaticamente:

- duplicações simples;
- nomes ruins em código privado;
- guard clauses;
- async/await incorreto;
- LINQ claramente ineficiente;
- CancellationToken não propagado;
- nullable checks;
- structured logging;
- código morto privado;
- complexidade facilmente extraível;
- queries claramente ineficientes;
- pequenos problemas arquiteturais locais.

Tenha cuidado ao modificar:

- APIs públicas;
- contratos;
- schema de banco;
- migrations;
- eventos;
- nomes serializados;
- endpoints;
- autorização;
- regras financeiras;
- comportamento de domínio.

Nesses casos preserve compatibilidade sempre que possível.

---

# Regra fundamental

Não faça refatoração por refatoração.

Uma correção de code smell deve melhorar pelo menos um dos seguintes atributos:

- clareza;
- segurança;
- manutenção;
- testabilidade;
- performance;
- confiabilidade;
- arquitetura.

Se a mudança apenas deixar o código diferente sem benefício claro, não faça.

---

# Definition of Done

Uma implementação somente deve ser considerada concluída quando:

- compila;
- testes passam;
- não introduz warnings relevantes;
- não possui code smells Critical conhecidos;
- não introduz regressão arquitetural evidente;
- não possui vulnerabilidade evidente;
- erros são tratados adequadamente;
- async é utilizado corretamente;
- CancellationToken é propagado quando aplicável;
- banco é acessado de maneira eficiente;
- logs não expõem dados sensíveis;
- código novo possui responsabilidade clara.

A análise deve priorizar qualidade real do sistema em vez de obedecer cegamente métricas ou regras de estilo.