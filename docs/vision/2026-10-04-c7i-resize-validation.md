# Upgrade de capacidade da carga — 2026-10-04

O usuário autorizou aplicar a recomendação de trocar a EC2 para `c7i.large` e aumentar os recursos do worker incremental. A API ficou indisponível durante a parada e reinicialização do host; voltou saudável pela URL pública após a troca.

## Configuração aplicada

- Mesma EC2 `i-0e57271cbb9d8a6b2`, mesma região/AZ `us-east-1/us-east-1a`, mesma arquitetura x86_64 e volume EBS `vol-0c4d214e93ad383bc`.
- Tipo anterior `t3a.micro`, 2 vCPUs/1 GiB; tipo atual `c7i.large`, 2 vCPUs/4 GiB. O Elastic IP `34.197.51.51` foi preservado.
- Worker: 0,25 → 1 CPU, 512 → 1024 MiB de RAM, 1024 → 1536 MiB de RAM+swap. Limites aplicados antes de iniciar o novo container e conferidos com Docker inspect.
- Artwork: 0,5 → 0,75 CPU, mantendo 768 MiB de RAM, 1280 MiB de RAM+swap e três workers. O container ativo foi atualizado sem parar o download. O controlador foi atualizado para preservar esse limite em novos jobs; sua retomada adotou o mesmo container em execução.
- Código de configuração: commit `1311ab0`. Os 20 testes de operação em Linux passaram após atualizar os valores esperados de recursos.
- Ajuste de artwork: commit `67bb76c`; os 20 testes de operação passaram novamente com a configuração final.
- Não houve mudança da imagem da API pública, manifesto do encoder, banco, modelo ou limiares do scanner. O worker continua com a imagem `catalog-follow-20261004-01`; a API usa `b443dd5-master-20261003`.
- Preço consultado na API oficial AWS Pricing: US$ 0,08925/h, aproximadamente US$ 2,14/dia apenas pela EC2 Linux On-Demand em us-east-1, sem EBS, IP, S3, transferência ou impostos.

## Segurança da retomada

Antes da parada, backup PostgreSQL consistente e comprimido salvo no bucket privado: `backups/postgres/20261004T130705Z.sql.gz`, 96.035.074 bytes, SSE AES256. O arquivo comprimido foi validado antes do upload, e sua existência/tamanho/criptografia foram confirmados por HeadObject.

O controlador de catálogo foi parado e temporariamente desabilitado para impedir que interpretasse o container de imagens interrompido como uma falha definitiva antes da retomada. Depois que PostgreSQL e API voltaram, o mesmo container de artwork foi iniciado, e o controlador foi reabilitado. Sua passagem reinicia o cursor, reutilizando imagens que já estão prontas. Embeddings persistidos também são reutilizados.

Cópias da configuração anterior e do estado do catálogo ficam em `/opt/vaulta/.deploy/resize-20261004/`. Foram mantidos o IP, o volume, os secrets, o tag da API e os dados já salvos.

## Verificação inicial

- Antes da parada: 2.259 embeddings prontos e 23.403 impressões com artwork pronto, às 13:10:26 UTC.
- EC2 iniciou novamente às 13:12:29 UTC. DescribeInstances confirmou `running/c7i.large`, e o agente SSM voltou Online.
- Às 13:14:34 UTC: 2.857 embeddings e 23.435 impressões com artwork pronto. Os dois serviços de carga estavam ativos; o worker estava em execução sem OOM.
- Readiness respondeu `Healthy` no endereço local e em `https://api.vaultatcg.com.br/health/ready`.
- Memória disponível observada: 2.595 MiB; swap em uso: 11 MiB. Antes da troca, disponível: 112 MiB; swap em uso: 1.007 MiB, com espera por I/O de aproximadamente 25–28% em uma amostra curta.

A medição de throughput começa em `/opt/vaulta/.deploy/resize-20261004/benchmark-start.json`, às 13:14:34 UTC. A janela é verificada antes de estimar um prazo novo; o salto inicial não substitui essa medição.

Na amostra inicial de 13:19:35 UTC, havia 4.459 embeddings e 23.631 impressões com artwork pronto. Isso representa 1.602 embeddings adicionais em aproximadamente cinco minutos, cerca de 319/minuto. O worker consumia 328 MiB; API 351 MiB; PostgreSQL 180 MiB. A carga de artwork ainda atravessava a parte já baixada do catálogo, com 11.856 arquivos reutilizados, então sua taxa de itens processados não deve ser tratada como taxa de novos downloads.

O resultado da janela de 15 minutos fica em `/opt/vaulta/.deploy/resize-20261004/benchmark-result.json`. A capacidade de gerar embeddings e o tempo restante dos downloads são estimados separadamente.

### Resultado da janela completa

Janela: 13:14:34 a 13:29:34 UTC (10:14 a 10:29 de Brasília), 15,004 minutos.

| Medida | Resultado |
|---|---|
| Embeddings prontos no início/fim | 2.857 → 7.671 |
| Embeddings gerados na janela | 4.814 |
| Taxa medida | 320,84 embeddings/minuto |
| Taxa anterior medida em 30 minutos | 590/30 = 19,67 embeddings/minuto |
| Ganho observado | 16,31 vezes |
| Impressões com artwork pronto | 23.435 → 24.376 |
| Memória disponível no final | 2.191 MiB |
| Swap em uso no final | 18 MiB |

Os dois serviços estavam ativos; ambos os containers estavam em execução sem OOM. Os três lotes finais registraram 100 gerados, zero reutilizados e zero falhas por lote. A API respondeu `Healthy`. Os checks de sistema e instância EC2 estavam `ok/passed`.

A carga de artwork manteve três workers e reutilizou 23.159 arquivos nessa passagem. O contador acumulado de 35,4 itens/s inclui esses skips e os registros sem imagem; não é taxa de novos downloads. A janela adicionou 941 artworks prontos, mas inclui a travessia do trecho já baixado e uma alteração do limite de CPU do artwork no meio da medição.

O catálogo tem 107.806 impressões com URL de imagem, de 140.971 registros. No ritmo observado, gerar os embeddings das 100.135 restantes com URL demandaria aproximadamente 5,2 horas **de processamento**, caso todas as imagens estivessem disponíveis. Isso não é um prazo de conclusão total: o worker continua dependente da chegada dos downloads e das imagens efetivamente disponíveis na fonte.

No fim da janela havia 13 registros em falha de artwork e 7.378 classificados como sem imagem; esses números pertencem à passagem em andamento. A importação francesa permanece parcial, como antes do resize. O upgrade não resolve indisponibilidade ou contrato inválido dos provedores.

## Reversão e continuidade

A c7i.large permanece ativa para continuar a carga. Não foi programado um downgrade automático. Antes de voltar à máquina menor, reduzir os limites dos workers e verificar o tamanho do índice da API e a memória disponível; os valores antigos ficam nas cópias `.before` acima. Um resize exige nova parada e retomada dos serviços. Manter a mesma imagem e o mesmo modelo permite reaproveitar as referências do banco.
