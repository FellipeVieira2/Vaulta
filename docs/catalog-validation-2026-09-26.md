# Catalog — entrega e validação de 26/09/2026

Status: implementação local entregue, aceite ponta a ponta **pendente de execução com Docker**.
Base analisada: `6f54edb83f8e83cbcefdfd1063c07cf132e0bc5f` (`master`).
Branch local: `fix/catalog-tcgdex-maui`.

## Alterações

1. Corrigido o adapter que confundia CardBrief e CardDetails, esperava variants como array e não incluía o idioma na URL.
2. DTOs JSON privados: TcgDexSetDto, TcgDexSetDetailsDto, TcgDexCardBriefDto e TcgDexCardDetailsDto;
   variants é Dictionary<string,bool>, refletindo o objeto extensível real.
3. Fluxo: listar sets → selecionar escopo → detalhes do set → detalhes de cada carta → normalizar → resolver external IDs → persistir por set.
4. CardDetails é atualizado a cada sync, pois o brief não contém rarity/treatments nem é uma prova de que esses dados não mudaram.
5. Parallel.ForEachAsync limitado a 4 por padrão, configurável entre 1–8; retries finitos para falhas transitórias, timeout por tentativa,
   Retry-After respeitado dentro do orçamento, cancelamento propagado e JSON incompatível classificado como permanente.
6. Variants usam códigos canônicos extensíveis em minúsculas com hífen; preservam Name/RawValue, GUID e histórico com IsActive.
7. Artwork externo fica em Printing.ExternalArtworkUrl/ArtworkProvider; nenhum download/S3/Asset/foto de usuário é criado.
8. Search retorna CatalogSearchPage com artwork; detalhe retorna CardId/SetId/artwork e CatalogVariantDto(Id/Code/Name).
9. Upserts e external mappings recebem source.Code; o provider fake usa `fake`. Nenhum literal `tcgdex` permanece no sync genérico.
10. Migration gerada pelo EF: `20260926045142_ExternalArtworkAndVariantAvailability`; antigas não foram editadas.
11. GET search e printing evoluídos com OpenAPI; operação administrativa por CLI (`--catalog-sync`, `--catalog-sync-run`, `--catalog-sync-runs`).
    Não foi criado endpoint HTTP de sync. A CLI requer acesso operacional ao host/container e configuração do banco.
12. Testes cobrem payloads, campos opcionais, variants booleanas e futuras, image URL, idioma, número original, retries, timeout,
    JSON inválido, limite de concorrência, provider fake, idempotência, atualização, reativação, histórico, advisory lock,
    cancelamento, rollback e Catalog → busca → detalhe → VariantId → Collection (201/uma entry/duas unidades).
13. Backend e projeto de testes de integração compilam: **zero erros e zero warnings**.
14. **50 testes unitários e 6 arquiteturais passaram**. Testes de integração foram tentados, mas o fixture Testcontainers não inicializa:
    Docker não está disponível neste ambiente. Os comportamentos de banco/HTTP integrado não foram comprovados por execução.
15. Smoke real **do adapter** passou: 220 sets lidos, 102 CardDetails de `base1`, Pikachu `base1-58`, número `58`, idioma `en`,
    rarity raw `Common`, artwork `https://assets.tcgdex.net/en/base/base1/58/high.png`, variants `first-edition` e `normal`.
    Isso não é smoke de persistência/API/Collection. O smoke completo está automatizado em `scripts/smoke-catalog.py` e no workflow.
16. Breaking changes: search passa de array para objeto paginado; variants passa de strings para DTOs; modelos internos Provider*
    mudam de Contracts para Application, e o port do provider passa a retornar ProviderSetDetails.
17. Pendências reais: executar integração e Compose/smoke completo num ambiente Docker; executar a solution inteira com workloads MAUI/Android;
    publicar a branch após disponibilizar acesso de escrita ao GitHub. O envio pelo conector foi recusado com HTTP 403
    `Resource not accessible by integration`. Nenhuma branch/PR remota foi criada, e nenhum workflow foi executado remotamente.

## Comandos e resultados

| Verificação | Resultado |
|---|---|
| `dotnet restore Vaulta.slnx` | Bloqueado por NETSDK1147: falta workload maui-android |
| `dotnet build Vaulta.slnx` | Bloqueado pela mesma dependência móvel |
| `dotnet test Vaulta.slnx` | Bloqueado pela mesma dependência móvel |
| Restore do projeto de integração e suas dependências backend | Passou |
| Build do projeto de integração e API/dependências | Passou, 0 warnings/0 erros |
| Testes unitários | 50/50 passaram |
| Testes arquiteturais | 6/6 passaram |
| Testes de integração | Tentativa bloqueada no fixture Docker, sem execução dos fluxos |
| `dotnet ef migrations has-pending-model-changes --project src/Modules/Catalog/Vaulta.Catalog.Infrastructure --no-build` | Sem mudanças pendentes |
| Adapter real TCGdex (`GetSets` + `GetSetDetails("base1")`) | Passou; nenhuma imagem baixada |
| Compose e HTTP Catalog → Collection | Não executados: Docker ausente |
| Publicação GitHub | Bloqueada por permissão do conector; git push também sem credenciais |

SDK temporário de validação: .NET 10.0.100. Neste executor, MSBuild exigiu `-m:1 -nr:false` e restore sem paralelismo;
foram usados esses parâmetros para separar limitação de processos do resultado do código.

## Aplicar o patch

Em um checkout limpo baseado no commit indicado, crie uma branch e aplique o arquivo da entrega:

```sh
git switch -c fix/catalog-tcgdex-maui
git am Vaulta-Catalog-TCGdex.patch
```

Depois execute as validações do README. O workflow incluído pode executar os testes com PostgreSQL Testcontainers
quando a branch for publicada em um repositório com GitHub Actions habilitado. O smoke usa um ambiente descartável e uma conta de teste;
nenhuma credencial é escrita no relatório. Se o processo de sync morrer abruptamente, um run pode permanecer `running` e precisa de inspeção.
