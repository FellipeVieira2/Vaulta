# Carga incremental de imagens e embeddings — 2026-10-04

Este documento registra a ativação inicial do worker. A máquina e os limites foram aumentados depois, com autorização do usuário; estado atual e medição estão em `2026-10-04-c7i-resize-validation.md`.

A geração de embeddings deixou de esperar o término de toda a carga de imagens. Um worker separado procura imagens oficiais já prontas, gera referências em lotes de 100 e persiste cada resultado. Ele continua consultando o banco quando não encontra trabalho; novas imagens entram nos próximos lotes.

## Implementação e validação

- Código: commit `5ce6aa8`, branch `codex/vision-developer-improvement`.
- 384 testes unitários, 203 testes de integração com PostgreSQL e MinIO isolados e 20 testes de operação passaram. Revisão independente concluída sem pendências P1/P2.
- O worker usa o mesmo manifesto CLIP e o mesmo bloqueio de escrita do indexador completo. Não troca pesos nem limiares do scanner.
- Referências prontas do mesmo asset/modelo são reutilizadas. Falhas têm intervalo de cinco minutos antes da próxima tentativa; o backlog inclui essas falhas.
- A lista de sets do TCGdex é deduplicada pelo ID antes de criar checkpoints. A origem retornava `CSV1C` duas vezes em chinês simplificado.
- Metadados incompletos têm até três tentativas. Importações parciais retomam seu checkpoint; idiomas concluídos são preservados.
- Após o download atual terminar, uma passagem adicional deduplicada inclui imagens de metadados recuperados que ficaram atrás do cursor anterior.

## Deploy

- Conta AWS `142767402064`, região `us-east-1`, EC2 `i-0e57271cbb9d8a6b2`.
- Imagem exclusiva dos jobs: `vaulta-api:catalog-follow-20261004-01`.
- Docker image ID no servidor: `sha256:2ab16782670c6f62251beb8fa2d695419035f4db0f59f69b4aa6538267a38139`.
- SHA256 do pacote transferido: `a9626ab57621715e08a5bd1cf9023702074bf398751f02fe1e6f04293506bfe4`; checksum confirmado antes de carregar a imagem.
- Serviço `vaulta-vision-follow.service`, container `vaulta-vision-follow`, reinício automático e retomada pelo banco. Limites aplicados antes do início: 0,25 CPU, 512 MiB de RAM, 1024 MiB de RAM+swap.
- `vaulta-catalog-bootstrap.service` usa `--image-tag catalog-follow-20261004-01 --external-vision`. O job de imagens já existente continuou ativo durante a atualização do controlador.
- A imagem da API pública permanece `b443dd5-master-20261003`; `.deploy/current-tag` não foi alterado. O worker executa somente o comando de indexação, sem iniciar o servidor HTTP ou seus outros serviços de background.
- Não há migração de banco nesta entrega. Não é necessário gerar outro APK para ativar esta carga.

## Evidência de funcionamento

Às 11:32 UTC, havia 20.203 impressões com artwork pronto e 356 embeddings oficiais prontos. Às 11:35 UTC, os números eram 20.290 e 397. Antes de iniciar o worker, os embeddings estavam em 344 desde 02:46 UTC.

Todas as referências prontas observadas usam a versão `775a7bb13f9b5a99e87b168965fe113e80dde65773e662e61306ab3ddbe6fe87`. A API respondeu `Healthy` também pela URL pública. O worker estava em execução, sem OOM, usando aproximadamente 198 MiB na primeira leitura.

A contagem de impressões com artwork pronto não é necessariamente a contagem de arquivos únicos no S3: impressões podem compartilhar um asset. O catálogo completo e a cobertura de embeddings continuam em processamento. Esta verificação comprova avanço da carga, não precisão do scanner em fotos reais.

Às 11:36 UTC, a listagem paginada do S3 confirmou 20.332 imagens originais e 20.338 miniaturas no prefixo `catalog-artwork/`. Às 11:36:48 UTC, o banco já tinha 425 embeddings oficiais prontos. As contagens são leituras de momentos próximos, não um snapshot transacional único.

Às 11:39:27 UTC, o banco tinha 475 embeddings oficiais prontos e 20.428 impressões com artwork pronto. O primeiro lote completo registrou `generated=100`, `reused=0`, `failed=0`; o worker continuava em execução sem OOM. A configuração da API confirmou o manifesto `/models/clip-base/manifest.json`, compatível com as referências novas.

## Operação e retomada

O banco é o checkpoint dos embeddings; não é necessário gerar novamente referências já prontas. O estado do bootstrap fica em `/opt/vaulta/.deploy/catalog-bootstrap/status.json`. Backups do controlador e do estado anterior ficam em `/opt/vaulta/.deploy/catalog-follow/catalog-follow-20261004-01/`.

A imagem foi carregada diretamente no Docker do EC2; a tag do worker ainda não foi publicada no ECR. O pacote privado permanece em `s3://vaulta-assets-142767402064-us-east-1/deployments/catalog-follow/catalog-follow-20261004-01.tar.gz` para recuperação, com o checksum registrado acima.

Para verificar, consultar os serviços e o log do container `vaulta-vision-follow`. Cada lote informa `generated`, `reused`, `failed` e `remainingReady`. O índice da API usa a atualização periódica existente de 30 segundos.

Para atualizar o worker novamente, parar primeiro `vaulta-vision-follow.service`, carregar a nova imagem imutável, atualizar `.deploy/vision-worker-tag` e iniciar o serviço. Um container ativo com outra configuração faz o controlador falhar explicitamente e preserva esse container. Para pausar somente a geração, parar esse serviço; a carga de imagens continua separadamente.

Pendências de provedores precisam ser verificadas no estado de cada idioma. Em 11:35 UTC, francês estava na terceira tentativa e chinês simplificado aguardava sua vez. Português de Portugal foi registrado como `no_data` porque o endpoint retornou uma lista vazia; isso não significa cobertura desse idioma.

A retomada francesa encontrou o checkpoint `pl3` com `permanent:invalid_contract`; por isso houve uma terceira tentativa completa de metadados, ainda em execução às 11:39 UTC. Esse erro não bloqueia imagens ou embeddings. A integração não declara a cobertura francesa completa nem fabrica os registros rejeitados.
