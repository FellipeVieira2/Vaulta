# ADR 001 — Identidade canônica e ingestão do Catalog

- Status: Aceita para a primeira implementação do Catalog
- Contexto: Vaulta precisa consumir dados externos Pokémon sem tornar um provider a identidade ou o proprietário do domínio. As mesmas entidades serão referenciadas no futuro por Collection, Scanner, Pricing e Marketplace.

## Decisões

### Modelo canônico

- O schema `catalog` é o dono de `Game`, `Series`, `Set`, `Card`, `Printing`, `Variant`, `CatalogExternalId` e `CatalogSyncRun`.
- `Game` possui `Series`, `Set` e `Card`. `Set` pertence a um `Game` e pode referenciar zero ou uma `Series`. `Printing` associa exatamente uma `Card` e um `Set`; `Variant` pertence a uma `Printing`.
- `Card` identifica o conceito canônico de carta; `Printing` identifica uma publicação em um Set, idioma e collector number; Variant descreve acabamento/edição dentro da impressão. Nenhum desses conceitos representa a cópia física do usuário.
- Todos os IDs de domínio são `Guid` gerados pela Vaulta. IDs de provider são dados externos mapeados, nunca PKs ou substitutos da identidade interna.
- `Set.Code` representa um código oficial/canônico quando disponível, não uma cópia automática do ID interno do provider. Quando uma fonte não fornecer código confiável, a resolução usa external ID e não inventa um identificador de domínio a partir do nome.

### Proveniência e entity resolution

- DTOs e IDs externos permanecem na borda de ingestão. Domain não referencia TCGdex nem tipos HTTP/provider.
- `CatalogExternalId` possui provider code, entity type, entity ID interno, external ID, datas de primeira/última sincronização e, quando útil, hash do conteúdo normalizado. Unicidade: `(provider, entity_type, external_id)`; índice adicional por `(entity_type, entity_id)`.
- Resolução prioriza external ID já conhecido. Fallback usa códigos oficiais confiáveis com Game como escopo. Para Printing, usar Set, collector number normalizado, idioma e dimensões de edição necessárias; Variant continua entidade própria.
- Nome normalizado é ferramenta de busca, nunca chave suficiente para fundir Card, Set ou Series. Se não houver evidência inequívoca, criar identidade independente/registrar conflito; não aplicar fuzzy merge silencioso.
- Provider records que não ofereçam identidade estável de Card podem inicialmente produzir Cards separados vinculados aos respectivos Printings. A limitação deve ser reportada no resultado de sync; futura consolidação exige chave autoritativa ou operação explícita, não heurística por nome.
- Sincronizações são idempotentes por external mapping e comparação de conteúdo. Atualizar `UpdatedAt` somente quando dados normalizados relevantes mudarem. Ausência numa resposta do provider não apaga uma entidade: atualizar `LastSeenAt`/disponibilidade conforme a semântica da fonte.

### Normalização

- Preservar nome, idioma, rarity e variant de apresentação/raw conforme recebidos; armazenar separadamente valores normalizados.
- `NormalizedName`: Unicode normalizado, caixa invariável, diacríticos dobrados para pesquisa, pontuação convertida em separadores de token e whitespace colapsado. Não alterar `Name` exibido.
- `CollectorNumber` é string. Normalizar caixa e whitespace sem converter para número, retirar zeros à esquerda ou apagar `/`, letras e sufixos.
- Idioma é armazenado como código BCP 47 canônico quando reconhecido (por exemplo `pt-BR`, `en`, `ja`); aliases são mapeados explicitamente no adapter e o valor raw é preservado quando necessário.
- Rarity e Variant são códigos extensíveis, não enums exaustivos. Guardar valor normalizado e valor raw do provider para valores desconhecidos e revisão futura.
- Normalizadores são funções determinísticas com testes próprios e independentes do adapter HTTP.

### Pesquisa

- Queries usam projeções/DTOs com paginação e limite máximo. Ranking privilegia collector number exato, nome exato, prefixo e similaridade nessa ordem apropriada aos filtros.
- PostgreSQL continua sendo o mecanismo de busca inicial. Índices B-tree cobrem filtros/joins; trigram/GIN só é habilitado por migration e validado com Testcontainers/consultas reais. Não adicionar mecanismo distribuído de busca nesta entrega.

### Provider inicial e sync

- TCGdex é o primeiro adapter de leitura. IDs de provider são ligados por `CatalogExternalId`; a API pública não expõe uma operação de sync. Credenciais não são embutidas e a URL base/idioma do provider são configurações.
- A implementação atual semeia Pokémon como jogo inicial, sincroniza sets e cards por set, registra `CatalogSyncRun` e usa lock consultivo do PostgreSQL por provider/escopo. Cada set é persistido em batch separado; falhas não removem entidades previamente sincronizadas.
- O adapter recebe imagem como URL externa, mas não baixa nem espelha artwork. Armazenamento/licença de imagens exige avaliação dos termos do provider e caso de produto separado.
- Search atual é filtro por nome normalizado com paginação limitada; ranking avançado, impressão exata por número como prioridade e similaridade/trigram ficam para evolução medida.

## Consequências

- A troca de provider não muda PKs, contratos canônicos nem entidades de domínio; adapters e mapeamentos externos é que evoluem.
- Dados sem chave de identidade confiável não são automaticamente consolidados. Isso favorece integridade sobre reduzir contagem de registros; qualidade/limitações aparecem nas estatísticas do SyncRun.
- Artwork é referência/proveniência de Catalog, não foto da unidade física. Cache de imagens depende de termos/licença do provider e fica fora desta decisão de identidade.
