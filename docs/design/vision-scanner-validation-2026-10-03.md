# Validação Vaulta Vision — 03/10/2026

## Evidência observada antes do deploy

- Backend: 346/346 unitários; aplicativo: 208/208; PostgreSQL/MinIO: 177/177 integrações; arquitetura: 6/6. Total: 737 testes aprovados.
- Compilação API Release e Android ARM64 Debug: zero avisos/erros. APK com assinatura v2/v3 verificada, 135.989.065 bytes; SHA256 `2453459da33918169c021f7e7aa42a5375b9e05fe6678c01cfc237a1687af5fc`.
- Pesos reais CLIP: 89.117.001 bytes, SHA256 `583fd1110a514667812fee7d684952aaf82a99b959760c8d7dca7e0ab9839299`. Pesos, manifesto e biblioteca ONNX nativa ARM64 verificados dentro do APK.
- Catálogo de validação: 102 impressões Base Set em inglês, 102 artworks/miniaturas internos e 102 embeddings reais de 512 dimensões. O sistema realizou a ingestão e a geração, não downloads manuais por carta.
- Recuperação real: 16/16 artworks oficiais recuperaram sua própria impressão. Isto não mede precisão em fotos de celular.
- Smoke local: Alakazam 1/102, holo unlimited, R$115,63 do snapshot local, 407 ms. Evidência textual explicitamente simulada; encoder/índice/catalog/quote reais. Não é prova de chamada GPT real.
- Classificação conservadora de orientação: as 16 imagens oficiais retornaram `unknown`. Os protótipos CLIP são experimentais, sem calibração em fotos físicas; `unknown` continua capturável. O contorno geométrico é localização preliminar, não um detector semântico treinado.

## Comportamento implementado

A primeira imagem utilizável pode disparar a captura sem um segundo fixo de espera. Uma operação por vez, sem fila de frames. A foto selecionada fica ligada à execução, permitindo retirar a carta enquanto a animação de identificação aparece. O frame completo acompanha a leitura do GPT para preservar rótulos de certificação; o recorte é usado pela análise local.

Identidade e acabamento exigem certeza de 0,80 para aceitação automática. Ausência de cotação mantém a carta na sessão com total parcial. PSA/CGC/BGS só são extraídos quando visíveis; não recebem o preço de uma carta sem certificação. Não há estimativa de preço inventada pelo GPT.

Outra ocorrência da mesma impressão pede: “Essa carta já foi adicionada. Deseja adicionar novamente?”. Aceitar adiciona outra unidade e soma o valor; cancelar não altera quantidade/total. Replay do mesmo ScanId não duplica unidades.

Histórico privado é opcional. A política operacional e contribuição à melhoria são distintas. Previsões são imutáveis; feedback humano e revisão explícita antecedem promoção de referências. Embeddings e exemplos ampliam a memória; pesos do encoder não são retreinados. Retenção padrão de sete dias, exclusão lógica imediata e remoção física repetida até expirar uploads assinados.

## Limites ainda a verificar

Câmera/inferência no aparelho real, negativos/verso/reflexos, precisão de acabamento, latência em rede móvel e cobertura integral do catálogo. APK compilado e assinado não comprova esses casos. Deploy e smoke GPT reais serão registrados em relatório separado; em 03/10 o usuário autorizou publicá-los e iniciar a importação remota.
