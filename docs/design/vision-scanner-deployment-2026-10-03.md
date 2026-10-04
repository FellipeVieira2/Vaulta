# Publicação e carga multilíngue — master

Base revisada: `acc0812`. A entrega corrige o contrato de relatório da fila, considera todas as fases no resultado final e desativa a construção automática do índice na etapa de imagens da fila. A consulta de catálogo para preparar preços agora é resolvida no mesmo escopo isolado do worker.

A fila cobre os 18 códigos de idioma oferecidos pelo adaptador TCGdex. O provedor atual cobre Pokémon; esta execução não equivale à carga de Yu-Gi-Oh! ou One Piece. Endpoints sem dados e imagens ausentes ficam explicitamente registrados. Arquivos prontos são reutilizados por 24 horas; embeddings são incrementais por modelo e artwork.

APK de testes: `artifacts/Vaulta-master-scanner-arm64.apk`, ARM64, Debug, API `https://api.vaultatcg.com.br/`. SHA256 `e90df51cffa91f0bcafbd450198a76bf8ab7f9327b41c6113128494a705baf86`. Compilação sem erros/avisos, assinatura v2/v3 verificada e modelo ONNX de 89.117.001 bytes incluído. Validação física no celular continua pendente.

## Verificação desta execução

376 testes unitários de backend aprovados; 16 testes de operação em Linux aprovados. A primeira rodada de integração reproduziu quatro falhas nas dependências de teste depois da mudança para escopos por worker; a substituição do armazenamento simulado foi adaptada aos novos escopos. A rodada completa seguinte aprovou 188 testes, incluindo regressão com oito workers, 24 imagens e cotações persistidas. A rodada final aprovou 189 testes de integração. Outra regressão reproduziu o timeout do provedor abortando a carga; a correção registra a falha somente naquela carta e preserva o cancelamento solicitado pelo operador.

Antes da atualização, a produção tinha 23.736 impressões em inglês e 123 em português; 3.203 impressões com artworks internos, mas somente 221 referências prontas no índice. A carga antiga ainda estava em execução, gerando imagens antes de reconstruir o índice.

O resultado final da publicação, os testes restantes e o estado da fila serão acrescentados após a verificação real.

Validação final: 376 unitários do backend, 189 integrações PostgreSQL/MinIO, 211 testes do aplicativo e seis de arquitetura: 782/782 aprovados. Mais 16/16 testes de operação em Linux. API Release publicada em diretório isolado. Nenhum aparelho Android conectado nesta execução.
