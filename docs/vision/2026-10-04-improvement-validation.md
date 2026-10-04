# Vaulta Vision — implementação e validação em 04/10/2026

Implementação disponível na branch `codex/vision-developer-improvement`. Nenhum deploy, alteração de flags públicas, mudança de pesos ou troca do encoder de produção foi realizado nesta execução. O código e o APK permitem revisar a entrega; a precisão com câmera ainda depende da coleta abaixo.

## Causa atual das falhas

A auditoria encontrou ranking com várias referências da mesma impressão ocupando o Top-K, histórico sem promoção automática de revisões humanas no desenvolvimento e ausência de fotos reais verificadas para avaliar a câmera. O ranking agora retorna impressões distintas, preservando a melhor referência de cada uma; o ranking original é armazenado separadamente dos candidatos apresentados pelo resolver.

Isso identifica problemas concretos, mas não demonstra qual deles explica a maioria das falhas com celular. Sem amostras verificadas, a fonte principal de falha entre retrieval, evidência e resolver permanece **não medida**. A atribuição produzida pelo benchmark é heurística e é identificada como tal.

## Configuração e dados observados

Leitura da configuração e do banco de produção via AWS SSM, sem mutação:

| Item | Resultado observado |
|---|---|
| Ambiente | Production |
| History enabled | true |
| Política operacional | ops-v1 |
| Política de melhoria configurada | improve-v1 |
| Consentimento individual | Exigido; a política configurada não autoriza todos os usuários |
| Developer Auto Promotion em produção | Parâmetro ausente; novo default false |
| Auto Promotion implementado | Sim, apenas Development + allowlist + consentimento + revisão humana verificada |
| Fotos privadas prontas e ativas | 0 |
| Amostras humanas revisadas e ativas | 0 |
| Referências verified_capture | 0 |
| Referências oficiais prontas | 344 |
| Referências oficiais pendentes | 46 |

Essas contagens são um snapshot das referências do banco, não o total de cartas do catálogo nem uma medida de cobertura de idiomas. Histórico habilitado e política de melhoria configurada são diferentes de participação autorizada do usuário.

## Memória e atualização do índice

Pesos não são treinados online. Confirmações/correções humanas podem adicionar embeddings de fotos reais ao índice, após conferir propriedade, retenção, consentimento vigente, bytes imutáveis e SHA da captura executada, manifesto, frente de carta e identidade canônica. Uma previsão automática nunca vira rótulo confirmado. O feedback original, a previsão, o ranking e suas revisões ficam separados.

Fotos distintas da mesma impressão são aceitas. O mesmo SHA/modelo não duplica a referência; identidades conflitantes exigem verificação. Upload finalizado depois do feedback repete a verificação. Uma falha na promoção mantém o feedback confirmado e não deixa entidades de uma transação revertida serem gravadas por uma operação posterior.

Eventos locais de promoção, revisão e exclusão solicitam refresh coalescido, com debounce padrão de 2 segundos e troca de snapshot. A verificação periódica de 30 segundos continua como fallback e convergência entre instâncias. A duração efetiva também inclui a carga do índice. Não há rebuild a cada scan nem promessa de indexação instantânea.

## Modelo e preprocessing

Produção permanece com CLIP quantizado, 512 dimensões, normalização L2, pooling pooled e ImageSharp bicúbico: shortest edge 224 seguido de center crop 224. Pesos/revisão/normalização são validados. O APK contém esse modelo atual.

O runner offline compara CLIP e DINOv2 Small, cada um com center crop, letterbox e direct resize. Cada experimento tem identidade de preprocessing distinta e não altera manifestos instalados ou índices públicos. DINOv2 é apenas uma opção de avaliação, não foi incluído no aplicativo. SigLIP permanece para uma avaliação posterior de compatibilidade, caso o baseline justifique.

## Baseline real

| Métrica | Resultado |
|---|---|
| Quantidade de fotos reais verificadas disponíveis | 0 |
| Top-1 / Top-5 / Top-10 | Não medidos |
| MRR | Não medido |
| Acurácia final de impressão / variante | Não medida |
| Principal fonte de falha | Não medida |
| Recomendação | Manter CLIP em produção; medir CLIP × DINOv2 e preprocessing com fotos reais antes de escolher |

A CLI offline foi executada com o dataset vazio e retornou `insufficient_real_world_dataset`, zero amostras e nenhum relatório de acurácia. Seis combinações encoder/preprocessing executaram os pesos ONNX reais com vetores finitos e normalizados em um smoke test sintético. Esse teste valida execução e compatibilidade; não mede precisão, velocidade da câmera nem desempenho em Android físico.

O benchmark do histórico compara somente oficial versus oficial + verificado, com a mesma evidência e embedding por imagem. Exclui Assets, SHAs e todas as sessões relacionadas transitivamente. Não cria runs operacionais nem consulta preço. A revisão independente encontrou uma exclusão transitiva incompleta no runner offline; teste reproduziu a falha e a correção foi validada e revisada.

## Scanner e reveal

A ocorrência é salva uma única vez antes da apresentação. A animação revela identidade, conta o preço, faz um pop sutil, atualiza o total e desaparece em 1.200ms. Capturas ficam bloqueadas durante a sequência; sair da página ou trocar conta/sessão cancela a apresentação. Movimento reduzido apresenta os valores finais imediatamente e usa fade simples.

Ausência de preço, variante pendente e preço indisponível de carta certificada são estados separados. Não são somados como zero fictício nem recebem cotação raw incompatível. Fonte/data/condição exibidas vêm da cotação real. Carta repetida exige confirmação antes de adicionar outra ocorrência. Não há confete nem áudio estridente.

O APK Debug permite exportar diagnóstico sanitizado e sem imagem, URL privada, hash, conta ou número de certificado. O controle não aparece em Release. Novas capturas autorizadas arquivam tanto recorte quanto frame completo para preservar evidências úteis ao GPT.

## Evidência de validação

Resultados finais da suíte são registrados nos logs locais em `artifacts/vision-improvement`. Builds Release da API e Debug Android ARM64 passaram com zero avisos e zero erros. Integração passou com **199/199** em uma base descartável nova; a repetição anterior em uma base já usada acusou colisões de fixtures, e esse resultado não foi tratado como aprovação. A fixture de três testes antigos de cotação recebeu um relógio fixo: sua data congelada havia vencido durante a execução. A regra de validade de preços em produção foi preservada.

| Suíte final | Aprovados | Falhas / ignorados |
|---|---:|---:|
| Identity unitários | 383 | 0 / 0 |
| App.Core | 218 | 0 / 0 |
| Integração PostgreSQL/Assets | 199 | 0 / 0 |
| Encoding | 8 | 0 / 0 |
| Arquitetura | 6 | 0 / 0 |
| Commerce | 78 | 0 / 0 |
| Total | **892** | **0 / 0** |

Adicionalmente, seis smoke tests de execução ONNX passaram. Testes não equivalem a um benchmark de reconhecimento com fotos reais. A revisão independente não encontrou P1/P2 remanescente na correção transitiva após o ajuste. `git diff --check` passou; APK, modelos e logs de execução permanecem em `artifacts`, fora do versionamento.

APK: `artifacts/Vaulta-vision-improvement-arm64.apk`, 136.034.121 bytes. SHA-256: `64CFE24EF8C4ADF255C77B34F3590E90CDF1AF56484E7958AAA8A85F0EE19E00`. Endpoint configurado: `https://api.vaultatcg.com.br/`. Instalar o APK não publica a nova implementação do backend nem habilita memória de desenvolvimento nessa API.

Figma: quatro estados editáveis e conectados no arquivo existente, com componentes, tokens e artwork reutilizados. Links e limites do protótipo em [scanner-value-reveal-figma.md](scanner-value-reveal-figma.md).

## Próxima validação necessária

Configure uma API privada Development e a allowlist da conta que participará, conforme [developer-improvement.md](developer-improvement.md). Habilite a contribuição individual no app. Colete 20 impressões × 5 condições de foto: luz normal, pouca luz, reflexo moderado, sleeve e inclinação; inclua negativas/versos em conjunto separado. Confirme impressão, idioma e variante manualmente. Referências e queries devem ser de sessões independentes.

Congele o manifest e execute o benchmark oficial versus ampliado e a comparação offline. Só esses resultados podem justificar trocar encoder ou preprocessing. Ainda falta essa coleta e o teste em um Android físico; nenhuma melhoria percentual foi declarada.
