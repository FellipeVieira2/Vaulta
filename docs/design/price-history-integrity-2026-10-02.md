# Histórico de preços: origem verificada ou indisponibilidade

Data: 02/10/2026. Correção independente do prompt master: gráficos de mercado/carteira não podem apresentar simulações como dados reais.

## Problema corrigido

O provedor JustTCG antigo gerava a série de demonstração para carteira, carta e filtros quando não havia mapeamento canônico. A ausência de chave também levava ao provedor simulado na configuração de produção da aplicação. Isso desenhava valores e tendências sem histórico de origem.

## Estado implementado

`UnavailablePriceHistoryProvider` fornece listas vazias para carteira, impressão e filtro. O contrato `IPriceHistoryProvider` define explicitamente que resultado vazio significa histórico indisponível.

`JustTcgPriceHistoryProvider` mantém o construtor e os métodos existentes para compatibilidade, mas não utiliza mais o fallback simulado. Como não há contrato de histórico servido pelo backend com impressão/variante, moeda e origem verificadas, seus métodos também retornam listas vazias. O método legado de consulta de variante não faz chamada direta ao provedor e respeita cancelamento; o construtor não anexa chave proprietária a cabeçalhos de `HttpClient`.

`SimulatedPriceHistoryProvider` permanece somente para demonstrações/testes explícitos. O registro de produção em `MauiProgram` foi atualizado pelo executor principal para usar exclusivamente `UnavailablePriceHistoryProvider` até a implementação de uma origem real. A verificação visual dos estados vazios dos gráficos faz parte da integração de interface conduzida pelo executor principal.

Preços atuais já recebidos do catálogo/backend são um dado diferente do histórico temporal; esta correção não fabrica séries históricas a partir deles.

## Verificação

Os nove testes de `PriceHistoryAvailabilityTests` falharam no código anterior e passaram após a correção. Foram cobertas carteira/carta/filtro, com e sem chave, consulta legada sem origem confiável, ausência de envio de chave e cancelamento do chamador. A suíte completa `Vaulta.App.Core.UnitTests` passou com 111 testes no momento da integração desta alteração.

Nenhuma chave real, chamada JustTCG, cobrança ou dado financeiro externo foi utilizado. A ausência de séries reais é reportada como indisponibilidade, sem estatística de mercado inventada.

## Pendente para dados reais

Ainda falta um contrato de histórico no backend com fonte e data das observações, mapeamento canônico da impressão/variante, moeda original e conversão BRL rastreável, política de atualização/cache e agregação da carteira/filtros. A interface só deve desenhar séries a partir desses dados verificados. Chaves proprietárias devem permanecer no servidor.
