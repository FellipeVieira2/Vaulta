# Testar o scanner Vaulta Vision

O app aponta para `https://api.vaultatcg.com.br/`. A API e o APK precisam usar esta mesma entrega: o DTO de identificação mudou.

APK local: `artifacts/Vaulta-master-scanner-arm64.apk` (Debug ARM64, assinatura de teste). Pesos são incluídos no pacote; não existe chave de API dentro do app.

1. Entrar na conta, abrir Scanner e mostrar a frente de uma carta presente no catálogo importado.
2. Após a captura e a animação, retirar a carta. O valor, quando houver cotação da variante, aparece brevemente e entra no total.
3. Mostrar outra carta. Remover e mostrar uma cópia da anterior: confirmar o aviso de repetição e verificar quantidade/total. Cancelar deve manter ambos.
4. Uma carta sem preço deve aumentar a quantidade e indicar avaliação parcial. Certificadas não usam preço de carta sem certificação.
5. Conferir verso, objeto retangular, reflexos, pequena movimentação e transições. A classificação local é experimental: registrar falsos positivos/negativos sem tratá-los como benchmark aprovado.
6. Nas opções, habilitar histórico privado somente se desejado. A contribuição para melhoria é separada; exemplos confirmados ainda aguardam revisão de operador.

Operação de catálogo: instalar modelo com `--vision-model-install /models/clip-base/manifest.json` em volume gravável de preparação; serviço usa volume somente leitura. Rodar `--catalog-sync tcgdex <setId|all|resume:runId>` sincroniza somente metadados. A fila `scripts/catalog-import-languages.py` encadeia os idiomas suportados, `--catalog-assets-import all` e `--vision-index-build /models/clip-base/manifest.json`. A fase de imagens na fila desabilita a geração de embeddings para executar o índice uma única vez na fase seguinte. Os downloads usam três workers por padrão, com contextos de banco isolados; imagens prontas são reutilizadas por 24 horas. O deploy não faz uma importação ilimitada ao iniciar cada réplica. O operador dispara o job dedicado, com checkpoint e relatório de erro. `--vision-index-status` informa quantidade e versão realmente disponíveis.

Detalhes e evidências: [validação](design/vision-scanner-validation-2026-10-03.md), [modelo](design/vision-model-evaluation.md), [índice](design/vision-index-validation-2026-10-03.md), [plano](superpowers/plans/2026-10-03-catalog-vision-testable-scanner.md).
