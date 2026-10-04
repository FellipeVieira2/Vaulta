# Publicação e carga multilíngue — master

Base revisada: `acc0812`. A entrega corrige o contrato de relatório da fila, considera todas as fases no resultado final e desativa a construção automática do índice na etapa de imagens da fila. A consulta de catálogo para preparar preços agora é resolvida no mesmo escopo isolado do worker.

A fila cobre os 18 códigos de idioma oferecidos pelo adaptador TCGdex. O provedor atual cobre Pokémon; esta execução não equivale à carga de Yu-Gi-Oh! ou One Piece. Endpoints sem dados e imagens ausentes ficam explicitamente registrados. Arquivos prontos são reutilizados por 24 horas; embeddings são incrementais por modelo e artwork.

APK de testes: `artifacts/Vaulta-master-scanner-arm64.apk`, ARM64, Debug, API `https://api.vaultatcg.com.br/`. SHA256 `e90df51cffa91f0bcafbd450198a76bf8ab7f9327b41c6113128494a705baf86`. Compilação sem erros/avisos, assinatura v2/v3 verificada e modelo ONNX de 89.117.001 bytes incluído. Validação física no celular continua pendente.

## Verificação desta execução

376 testes unitários de backend aprovados; 16 testes de operação em Linux aprovados. A primeira rodada de integração reproduziu quatro falhas nas dependências de teste depois da mudança para escopos por worker; a substituição do armazenamento simulado foi adaptada aos novos escopos. A rodada completa seguinte aprovou 188 testes, incluindo regressão com oito workers, 24 imagens e cotações persistidas. A rodada final aprovou 189 testes de integração. Outra regressão reproduziu o timeout do provedor abortando a carga; a correção registra a falha somente naquela carta e preserva o cancelamento solicitado pelo operador.

Antes da atualização, a produção tinha 23.736 impressões em inglês e 123 em português; 3.203 impressões com artworks internos, mas somente 221 referências prontas no índice. A carga antiga ainda estava em execução, gerando imagens antes de reconstruir o índice.

O resultado final da publicação, os testes restantes e o estado da fila serão acrescentados após a verificação real.

Validação final: 376 unitários do backend, 189 integrações PostgreSQL/MinIO, 211 testes do aplicativo e seis de arquitetura: 782/782 aprovados. Mais 16/16 testes de operação em Linux. API Release publicada em diretório isolado. Nenhum aparelho Android conectado nesta execução.

## Publicação observada

API publicada pelo AWS Core na instância existente, tag `b443dd5-master-20261003`, imagem `sha256:263d522b6fc7c1cb43435ad0a4e4ec99b0ceca4bbfb01a4101b95dc39f1bc2e8`. Health local e público retornaram Healthy, e o container ativo foi comparado à imagem validada. Migrações aplicadas duas vezes. Backup privado: `backups/postgres/20261004T022701Z.sql.gz` (horário UTC).

A imagem de runtime foi construída a partir da compilação isolada e arquivada no S3 privado em `operations/vision-20261003/b443dd5-master-20261003-image.tar.gz`; esta execução não fez push para o ECR. A versão anterior permanece disponível para reversão. Não houve alteração de IAM nem aumento da instância. Os contêineres de testes foram parados após a validação.

APK também salvo no bucket privado: `operations/vision-20261003/vaulta-master-scanner-arm64.apk`.

## Fila automática

Serviço `vaulta-catalog-bootstrap.service` instalado, habilitado e observado ativo. A fila cobre `en,pt,pt-br,ja,es,es-mx,fr,de,it,ko,zh-tw,zh-cn,id,th,nl,pl,ru,pt-pt`. O relatório do run inglês `66ed9e95-1a09-4879-9313-e7514aeae38d` foi verificado no banco com status completed antes de ser reaproveitado no checkpoint; as imagens restantes continuam na fase global.

Estado persistido em `/opt/vaulta/.deploy/catalog-bootstrap/status.json`. Logs por contêiner de idioma e nas fases `vaulta-artwork-all-*` e `vaulta-vision-build-*`. A carga continua no sistema, após o término desta conversa. Estar ativo não significa que todas as imagens ou os embeddings já estejam disponíveis. Falhas e ausências do provedor permanecem visíveis; a fila pode terminar com pendências.

## Smoke real da API

O sistema sincronizou o set me04 em português (122 impressões) e preparou 244 impressões do mesmo set entre inglês e português: 106 imagens novas, 138 reutilizadas, zero ausências/falhas e 244 com cotação local. A API pública recebeu o artwork interno de Golisopod 026/086 em português, com autenticação legítima de conta temporária de teste.

Resultado observado: status identified, PrintingId esperado `bbb6a60e-d0b8-4dbd-9503-4d649c20000e`, confiança de Printing 0,99. Chamada real ao `gpt-6-luna`, prompt `card-evidence-openai-v8`, resolvedor `vision-evidence-v2`, embedding de 512 dimensões e índice `06f39eaee2815c91bcbc982c5048b1e1bf62aae4519e27823361b87096b31c1c`. Tempo HTTP 9,91 segundos, sem serviceIssue. O modelo retornou reverse com confiança 0,91 e a API retornou R$1,36 do snapshot TCGplayer convertido pela PTAX. A API de detalhes também retornou a cotação normal, R$0,12, separada da reverse.

Esta imagem oficial comprova transporte/API/GPT/Printing/cotação integrada; não comprova que o acabamento físico é reverse, nem precisão em fotografias de celular. Exibição e soma foram verificadas nos testes do aplicativo; não foram observadas em aparelho nesta execução.

A conta temporária do smoke e seu evento de cadastro foram removidos após o teste. Um job finito adicional (`vaulta-vision-warm-start-b443dd5`) foi iniciado para indexar o set já preparado, reutilizando os arquivos; o índice completo continuará na fase global da fila.

Último checkpoint observado antes da entrega: metadados completos de en, pt, pt-br, ja e es; es-mx em execução. Impressões ativas: en 23.736, pt 13.907, pt-BR 1.124, ja 12.781, es 15.510. Imagens internas vinculadas: 3.358 en e 122 pt; os outros idiomas ainda aguardam a fase global. Índice: 284 referências ready e cinco pending, com preparação incremental adicional em andamento. Estes números são um checkpoint, não a cobertura final. Health público permanecia Healthy.
