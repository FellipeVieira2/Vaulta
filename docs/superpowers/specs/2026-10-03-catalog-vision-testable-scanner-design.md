# Vaulta: novo scanner com catálogo próprio, pronto para teste

Status: proposta consolidada após esclarecimento do usuário em 2026-10-03.

## Resultado contratado

A entrega não termina com tabelas/contratos ou dataset preparado. Deve existir um aplicativo Android testável que capture uma carta automaticamente, reconheça usando embeddings reais e o catálogo próprio, resolva a impressão/variante quando houver evidência e mostre cotação e total da sessão.

O app ainda não está em uso. Não manter os fluxos antigos de reconhecimento, pesquisa web durante o scan ou compatibilidade de DTOs/clientes como obrigação. API e app serão atualizados juntos. Preservar IDs/dados de domínio úteis, segredos e módulos de coleção/marketplace; não fazer reset destrutivo do banco para simplificar trabalho.

Esta especificação amplia o escopo da proposta de dataset, que permanece como desenho do componente de histórico. O plano exclusivamente estrutural de Vision não representa mais a entrega solicitada.

## Responsabilidades e fluxo

Importação manual: TCGdex -> provider -> catálogo canônico PostgreSQL + artwork interno Assets/S3 -> encoder real -> referências vetoriais versionadas.

Uso: preview local -> presença/localização e orientação -> captura útil/crop -> embedding -> Top-K no índice próprio -> OCR/evidência visual -> Printing/Variant -> cotação local -> feedback visual breve e total. Dataset recebe histórico e correções autorizadas desse fluxo.

Não acessar TCGdex, JustTCG ou pesquisa web para servir detalhes, catálogo ou cotação em requests comuns. Atualização externa acontece fora do scan. GPT pode analisar evidência nos casos em que ajuda a distinguir candidatos/acabamento, sem atuar como catálogo ou inventar preço/UUID.

Reconhecimento sem cotação continua sendo reconhecimento: registrar a carta, mostrar “Sem cotação” brevemente e manter o total parcial. Preço ausente nunca vira zero apresentado como valor de mercado nem bloqueia a próxima carta.

## Catálogo e imagens internas

Reutilizar Game/Series/Set/Card/Printing/Variant, IDs internos, referências externas, ICatalogProvider/ICatalogSync e CLI existentes. Pokémon é a cobertura inicial efetiva; arquitetura e detector não ficam acoplados ao Pokémon. Registrar claramente a cobertura de outros jogos, sem prometer que seus catálogos já existem.

Expandir ingestão para séries, metadata útil à resolução, idiomas e variantes detalhadas efetivamente existentes. Não mesclar Cards pelo nome. Usar idioma real da fonte; não trocar artwork inglês por português apenas alterando language.

Adicionar ingestão server-to-server por streaming a Assets, propósito de artwork de catálogo, hash original, dimensões, metadata da origem e renditions. Reusar S3/MinIO. Imagens oficiais não são imagens de usuário. Não ampliar resolução artificialmente.

Importação inicial e reprocessamento de assets são comandos finitos com concorrência/retries limitados, cancelamento, registro de progresso, erros parciais e retomada. Falta de artwork exclui a referência visual daquele registro, sem apagar a Printing ou interromper todo o sync.

Catálogo e detalhes passam a retornar acesso ao asset interno; variante/metadata de preço fica desacoplada de HTTP no runtime. A atualização de preço preserva moeda, origem e data; não converter com câmbio inventado. Cotação deve corresponder à impressão/variante, separada de preço pedido por vendedor.

## Encoder e índice reais

Selecionar encoder pela avaliação comparativa de no mínimo dois candidatos reais disponíveis, adequados ao hardware do servidor/celular e com licença/origem identificadas. SigLIP/DINOv2/CLIP são famílias candidatas, não decisões já tomadas. Registrar revisão e SHA256 dos pesos, preprocessing, dimensão e runtime.

Não usar hash perceptual como se fosse embedding aprendido nem produzir vetores artificiais. Normalizar vetores conforme o modelo; official artwork e captura devem usar o mesmo encoder/preprocessamento compatível. Vetores de versões diferentes não se misturam.

Persistir VisualReference separada de Printing: AssetId, PrintingId, origem, hash da imagem, encoder/revisão, preprocessamento, dimensão, embedding e timestamp. Incluir geração/rebuild CLI incremental e publicação atômica do índice. Alterações de artwork invalidam referências anteriores de modo rastreável.

Geração não fica como tarefa manual esquecida após importar cartas. Com o manifesto de encoder configurado, o pipeline de importação deve gerar embeddings reais para cada artwork interno pronto, registrar falhas/pendências e publicar a nova versão do índice. O comando de rebuild continua disponível para reprocessamento, troca de modelo e retomada. Não executar inferência dentro de uma transação de banco longa.

Cada captura processada pelo novo scanner também produz embedding real com a mesma versão compatível e pode persistir esse vetor junto ao Run autorizado. Captura sem rótulo confirmado não vira referência canônica nem amostra de treinamento supervisionado por consequência. Exemplos suficientemente verificados podem enriquecer o índice incrementalmente, preservando procedência e limites por Printing.

Geração/indexação de embeddings não equivale a ajustar os pesos do encoder. O usuário pediu geração desde o início e perguntou qual estratégia é melhor. A recomendação desta primeira entrega mantém a restrição dos prompts originais: encoder pré-treinado, embeddings reais e memória incremental de exemplos verificados, sem treinamento dos pesos agora. Uma solicitação futura de treino real acrescenta incremento próprio com dados rotulados, avaliação separada e publicação/rollback de versões; não executar treinamento silencioso a cada predição.

Para o primeiro catálogo, índice exato por cosseno em memória, alimentado de PostgreSQL e com limites de memória, é uma opção simples. Comparar custo observado antes de introduzir ANN/pgvector; a implementação deve fazer busca vetorial real e registrar tamanho/latência. Retornar candidatos e scores, não IDs externos livres.

Validar inicialmente com fixtures rotuladas e fotografias reais disponíveis. Testes com transformações das imagens oficiais são smoke tests do pipeline, não substituem avaliação com câmera real. Sem benchmark físico disponível, entregar o scanner operacional com ferramenta de captura/rotulagem e informar que a precisão ainda aguarda medição em aparelho; não alegar porcentagens inventadas.

## Detecção, frente/verso e câmera

Substituir o gate rígido de um segundo e o requisito de retângulo perfeitamente centralizado. Procurar região de carta, escolher frame com informação suficiente e disparar quando estiver útil. Não enviar requests para cada preview e não acumular frames antigos.

Implementar presença/localização e frente/verso como componentes distintos, preferencialmente no Android para responder sem round-trip. Escolher mecanismo/modelo de orientação e detecção por prova no dispositivo alvo, com negativos (mesa/mão/objetos retangulares), sleeves, perspectiva e cartas encapsuladas. Zero-shot ou referências visuais são alternativas a avaliar, sem tratá-las como detector treinado especificamente para TCG.

Somente verso conhecido: manter câmera ativa e orientar “Vire a carta”. Não identificar uma Printing pelo verso comum. `unknown` continua possível em sleeves opacas, jogos desconhecidos e cartas com duas faces; não bloquear permanentemente por uma classificação incerta.

Depois da captura, processar uma carta por vez; usuário pode retirar a carta enquanto a imagem salva é resolvida. Mostrar animação discreta na mesma tela, valor breve e total, sem painel persistente da última carta. Rearmar após saída/troca; duas cópias físicas idênticas apresentadas separadamente contam como ocorrências diferentes.

Confirmação humana somente quando a identificação/acabamento não tiver evidência suficiente para a política de aceitação de 0,80. Score de similaridade não é probabilidade: manter score e confiança separados, explicar origem da confiança e não mapear cosseno diretamente para essa política. Reprints compartilhando artwork exigem texto/layout/metadata adicionais.

Reconhecer etiqueta/empresa/nota de slab é extração visual, não prova de autenticidade do certificado. Não classificar condição física automaticamente a partir da identificação.

## Histórico e memória incremental

Implementar ScanAttempt/Captures/Runs/Candidates/Feedback e manifesto do pipeline conforme a especificação de dataset. Resultados originais e reexecuções ficam separados; Printing e Finish possuem rótulos/confianças independentes. Não exigir confirmação de toda carta para usar o scanner.

Capturas autorizadas, correspondentes ao Run e suficientemente verificadas podem acrescentar referências reais ao índice. Isso é memória incremental do reconhecimento, não treinamento online dos pesos. Publicar versão incremental e medir regressões, sem promover automaticamente alta confiança a ground truth.

Rótulos de presença e frente/verso podem existir sem Printing conhecida. Registrar cada confirmação na tarefa correspondente. Um feedback de identidade não confirma automaticamente bounding box, verso ou acabamento.

O armazenamento operacional e uso para melhoria têm políticas separadas. Capturas privadas, retenção finita e participação explícita; exportações sem dados pessoais/URLs assinadas. Coleta ampla somente após disponibilizar exclusão e retenção operacional. Não replicar fotos privadas no celular de outros usuários para “ensinar” o app.

## Entrega e aceitação

Entregar código, migrations, comandos/configuração documentados, catálogo/artwork interno importado, pesos reais com versão identificada, referências e índice gerados, API integrada e APK instalável apontando para API acessível ao aparelho.

Publicar contagens observadas de jogos/sets/Printings/Variants/assets/referências, quantidade sem artwork/cotação, memória e latência medidas. Incluir ferramenta de benchmark/manifesto e casos negativos/reprints/idiomas/acabamento.

Verificação obrigatória: sync repetido idempotente, geração vetorial repetida incremental, providers de catálogo/preço bloqueados durante scan, reconhecimento de carta indexada com ID canônico, ausência de cotação sem bloquear sessão, câmera sem carta, verso, troca de carta e cópias idênticas.

Validar API/EF/PostgreSQL/MinIO, build Release Android e instalação/scan físico quando houver dispositivo. Compilar APK não equivale a teste em aparelho. Não afirmar todo catálogo indexado se foi só subconjunto, nem scanner implementado se existe apenas endpoint não integrado.

O ambiente de teste pode usar API na rede local ou AWS. Preparar build e configuração de endpoint concreto; deploy AWS depende de credenciais válidas e terá relatório separado. Não usar segredos em documentos/APK/repositorio.

## Referências técnicas consultadas

- TCGdex Assets: https://tcgdex.dev/assets (high 600x825; derivados respeitam original).
- ONNX Runtime C#: https://onnxruntime.ai/docs/get-started/with-csharp.html.
- ONNX Runtime Mobile: https://onnxruntime.ai/docs/tutorials/mobile/ (API Java/C/C++ no Android; não presumir que adicionar pacote managed de servidor habilita Android).
- SigLIP2: https://github.com/huggingface/transformers/blob/main/docs/source/en/model_doc/siglip2.md.

## Estado

Implementação integrada e validada localmente em 03/10/2026; deploy e validação física são rastreados no plano e relatório observado. Ela incorpora os esclarecimentos: a entrega é o scanner funcionando com nosso catálogo, e o fluxo antigo pode ser substituído porque não há usuários ativos.
