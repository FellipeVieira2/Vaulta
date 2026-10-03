# Vaulta Vision: capturas reais e dataset versionado

Status: proposta para revisão; não implementada.

## Objetivo e relação com o catálogo

Complementar o catálogo próprio com capturas reais, predições rastreáveis e feedback humano. Nesta etapa não treinar modelos, não gerar embeddings fictícios, não escolher encoder por preferência e não trocar o scanner existente.

O catálogo mantém identidades canônicas e referências oficiais. Vision guarda tentativas de reconhecimento e evidências de imagens reais. Assets continua responsável por arquivos, acesso e storage S3/MinIO. Uma captura não substitui o artwork de uma Printing.

O complemento não exige descartar os módulos existentes. A importação de catálogo e artwork continua sendo uma entrega independente e prioritária.

## Contexto verificado no repositório

- O endpoint autenticado `POST /api/v1/scanner/identify` recebe uma imagem e retorna `CardScanResultDto`, sem histórico persistente de tentativas.
- `ScannerService` pertence atualmente a Catalog.Application e usa portas de reconhecimento, catálogo e preço. Esta entrega não muda esses providers nem a política de preço.
- Os DTOs atuais não expõem separadamente todas as confianças e scores de cada estágio. Não preencher campos novos copiando uma confiança genérica.
- Assets já implementa uploads e leituras assinadas em S3/MinIO, mas aceita apenas os propósitos `collection-item` e `profile-avatar`.
- As sessões atuais do aplicativo não equivalem a um histórico de benchmark no servidor.

## Alternativas

1. Recomendação: módulo Vision pequeno dentro do monólito, com armazenamento próprio de metadata e referências a Assets e Catalog por contratos. Mantém o dataset independente de GPT e evita ampliar ainda mais o domínio de Catalog.
2. Guardar toda a informação em Catalog: reduz scaffolding inicial, mas mistura referência oficial, dados operacionais e rótulos de avaliação. Não recomendado.
3. Serviço externo de dataset/MLOps: pode atender necessidades futuras, mas introduz infraestrutura e operação desnecessárias agora.

Vision segue a estrutura Domain/Application/Infrastructure/Contracts existente e não acessa os DbContexts de outros módulos. Referências de IDs entre módulos são validadas por portas; não adicionar FKs cruzadas por conveniência. PostgreSQL existente, sem um novo servidor de banco.

## Modelo mínimo

### ScanAttempt

Uma ocorrência de captura de uma carta. Campos: Id, OwnerId operacional, CreatedAt, SessionCorrelationId opcional, Status e política de captura aplicada. Uma nova tentativa legítima continua sendo outra ocorrência, mesmo quando seus bytes são iguais.

Erros e capturas negativas podem existir sem PrintingId: `not_a_card`, imagem insuficiente, captura interrompida e carta desconhecida também são exemplos úteis. Não inventar identidade para permitir a persistência.

### ScanCapture

Relação de uma tentativa com um Asset confirmado: Id, ScanAttemptId, AssetId, Sequence, CapturedAt, Role e metadata limitada. Role pode distinguir frame inteiro, crop da carta e frame de acabamento. Vários frames podem pertencer à mesma tentativa.

Uma referência de crop registra a captura de origem e a transformação usada. Isso permite reproduzir o pré-processamento sem confundir crop com fotografia original. Nesta entrega não implementar detector, crop automático nem multi-frame no app.

### ScanRun

Resultado imutável de uma execução sobre as capturas: Id, ScanAttemptId, StartedAt, CompletedAt, Status, PipelineVersion, componentes utilizados, versões, identificação extraída, PredictedPrintingId opcional, PredictedVariantId opcional, PrintingConfidence opcional e VariantConfidence opcional.

Cada reexecução futura cria outro Run; não sobrescreve o resultado original. O manifesto deve registrar provider/modelo efetivamente usado, revisão disponível, versão/hash do prompt, pré-processamento, resolver e versão do índice de catálogo. Campos de componentes não utilizados permanecem nulos. Um alias de modelo sem revisão fixa não garante reprodução exata; registrar essa limitação.

Resultados existentes de GPT podem ser registrados sem fingir que passaram por embeddings ou classificadores que ainda não existem.

### ScanCandidate

RunId, PrintingId canônico validado, Rank, VisualScore opcional, OcrScore opcional, CombinedScore opcional e metadata limitada. Rank é único por Run e positivo. Registrar somente scores realmente produzidos, com a semântica declarada pelo pipeline.

Similaridade vetorial não é probabilidade de acerto. Não converter um score de 0,80 em 80% de certeza. Não fabricar scores ausentes para preencher o schema.

### ScanFeedback

Id, ScanAttemptId, ScanRunId, Source, CreatedAt, CorrectedPrintingId opcional, CorrectedVariantId opcional, PrintingCorrect opcional, VariantCorrect opcional e Notes limitada. Feedback é preservado como evento: novas correções não apagam a predição nem as correções anteriores.

Uma correção somente de acabamento não torna a Printing incorreta. VariantId deve pertencer à Printing confirmada. A variante pode continuar desconhecida quando o usuário só consegue confirmar a carta.

UserConfirmation, UserCorrection, AdminReview e BenchmarkLabel são origens de feedback, não garantias equivalentes de qualidade. Não alterar Collection ou Marketplace automaticamente ao registrar feedback.

### VisionDatasetSample

Uma promoção explícita de capturas e rótulos para uso de melhoria: Id, ScanAttemptId, Captures, LabelSourceFeedbackId, LabelRevision, VerificationStatus, HardCaseTags, ImprovementPolicyVersion, PermissionRecordedAt e RetentionUntil opcional.

Estados de revisão: Unverified, UserConfirmed, UserCorrected e ReviewerVerified. Confirmação do próprio usuário não equivale automaticamente a ground truth forte. Autoaceitação do scanner não é feedback humano nem promove o exemplo.

HardCaseTags é uma lista pequena de categorias, sem sistema genérico de taxonomia. Exemplos: glare, blur, same-artwork-reprint, unreadable-number, normal-vs-reverse, occlusion e language-conflict.

## Captura, resposta e persistência

1. Dataset desativado por padrão. Fluxo atual de reconhecimento permanece disponível sem participação no dataset.
2. Para testes e participantes habilitados, um endpoint autenticado cria uma tentativa e URLs de upload usando Assets. Usuário só pode associar Assets próprios com propósito `vision-scan` e upload confirmado.
3. Upload da captura é explícito e independente da promoção para melhoria. Não exigir um upload adicional síncrono dentro de todas as chamadas de identificação.
4. A identificação pode receber AttemptId opcional, validado contra o usuário autenticado. Ausência dele conserva o contrato atual. Requisições repetidas usam chave de idempotência de Run para não duplicar a mesma execução por retry.
5. Registrar resultado e manifesto em uma gravação curta de metadata, sem processamento de imagens, download de catálogo ou cópias S3 nesse trecho. A persistência do dataset não pode descartar um resultado válido do scanner; falha de registro deve ficar explícita na resposta/telemetria, sem declarar um exemplo salvo.
6. Confirmação ou correção chega por endpoint separado, preserva a predição e valida os IDs canônicos. Confirmar toda carta não será obrigatório: manter o fluxo automático existente e permitir revisão voluntária.
7. Promover para dataset somente com política aplicável e permissão registrada. Registrar feedback não implica consentimento para melhoria.

Não usar `Task.Run` ou tarefas não aguardadas dentro de request como mecanismo confiável de persistência. Qualquer trabalho secundário necessário deve ser representado por estado durável e processado por um serviço limitado do monólito. Não criar SQS, microserviço ou plataforma MLOps nesta entrega.

Se o processo cair antes do registro de resultado, a tentativa não é marcada artificialmente como concluída. Uploads sem Run podem expirar pela política operacional. Falhas de Assets não obrigam o scanner comum a parar.

## Storage, permissão e retenção

Reutilizar Assets com propósito privado `vision-scan`, leitura assinada e validação de proprietário. Separar dos propósitos de catálogo e fotos de coleção. Uso administrativo exige autorização administrativa real; não confiar em `FeedbackSource=AdminReview` enviado pelo cliente.

Usar chaves estáveis, por exemplo `vision/captures/{assetId}`. Verified, hard-case e benchmark são estados do banco, não diretórios que obriguem mover/copiar objetos após cada revisão.

Separar permissão de armazenamento operacional da permissão de melhoria: versão da política, finalidade, instante e origem. Ausência de permissão impede promoção permanente. A primeira implementação não habilita captura silenciosa no aplicativo.

RetentionUntil e DeletionRequestedAt permitem expiração e retirada futuras. Capturas operacionais não promovidas usam prazo finito configurável. Em produção, a funcionalidade de captura só é habilitada com uma política de retenção configurada; não adotar retenção infinita implícita.

Uma rotina completa de lifecycle/expiração não está nesta entrega. Não ativar coleta ampla antes de disponibilizar a operação de exclusão e retenção. Promover para benchmark não deve impedir retirar dados quando a política aplicável exigir.

Não colocar UserId, e-mail ou nome nas exportações de benchmark. Metadata é allowlist, não dump livre de requests ou resposta do provider. Não armazenar chaves, tokens, URLs assinadas, localização ou EXIF desnecessário. Exportações usam derivados sanitizados, mantendo rastreabilidade da transformação.

Deduplicação física por hash é opcional e fica adiada. Quando implementada, deve respeitar proprietário, finalidade e política. Uma tentativa não pode excluir um Asset ainda utilizado por outra referência legítima. Não expor a existência de arquivos de outros usuários por hash.

## Benchmark e embeddings

### Detecção local de cartas: requisito do novo scanner

O aplicativo deve localizar uma carta no preview antes de solicitar sua identidade. Este requisito integra a etapa futura de scanner e é independente da entrega inicial de persistência de dataset. Detectar a presença de uma carta, identificar sua Printing e determinar seu acabamento são tarefas distintas, com versões e métricas próprias.

O código atual usa `ScannerCardPresence`, um filtro de bordas, proporção e centralização; `ScannerSceneGate` exige aproximadamente um segundo de estabilidade. Isso não equivale a um detector semântico de cartas. O novo scanner deve evoluir/substituir essa combinação conforme medições, sem exigir que a carta fique perfeitamente parada ou centralizada por um tempo fixo.

Fluxo pretendido: preview local amostrado -> detecção/localização -> seleção de frame utilizável -> crop/correção de perspectiva quando possível -> embedding e candidatos -> OCR/resolver/GPT quando necessário -> preço local -> resultado e próxima carta.

Detecção e tracking devem acontecer preferencialmente no dispositivo, sem chamadas remotas para cada frame. Trabalhar com preview reduzido e filas limitadas, descartando frames antigos quando o processamento fica atrás da câmera. Não acumular requisições de identificação para a mesma carta.

Selecionar uma imagem suficiente assim que estiver disponível. Nitidez, visibilidade e área útil orientam a captura; estabilidade é apenas uma evidência, não uma espera rígida. Bloquear novas identificações enquanto uma captura está sendo resolvida, permitir retirar o celular após capturar e rearmar quando a carta sair ou houver evidência de uma nova ocorrência. Duas cópias físicas idênticas apresentadas separadamente devem poder contar como duas cartas.

Preparar metadata de detecção: detector/revisão, preprocessamento, tamanho do frame, bounding box normalizada, cantos opcionais, score de detecção, métricas de qualidade e motivo de seleção/rejeição. Scores de detecção não são a confiança na identidade da carta.

Fotos positivas do catálogo não bastam para avaliar presença na câmera. Dataset de detecção deve permitir rótulos humanos de `card-present`, `no-card`, `multiple-cards` e `uncertain`, além de bounding boxes/cantos verificados. Incluir mesa, mãos, celular, caixas, sleeves vazias e outros retângulos como negativos, bem como cartas inclinadas, parcialmente cobertas e encapsuladas como positivos quando localizáveis. Esses rótulos não exigem uma Printing conhecida.

O domínio prepara esses registros; não treina um detector nesta entrega. Guardar feedback não altera automaticamente o comportamento do aplicativo. Uma nova versão de detector só será publicada após avaliação comparativa, com regressões e versão rastreáveis.

Escolher a solução local por testes no Android alvo, considerando latência entre entrada e captura, recall de cartas, falsos disparos sem carta, nitidez da captura, memória, consumo de bateria e duplicação de ocorrências. Definir metas quantitativas após medir a linha de base; não prometer um tempo de identificação sem benchmark real.

O componente de localização deve funcionar para TCGs diferentes. Casos com múltiplas cartas ou oclusão insuficiente não iniciam várias identificações concorrentes; a interface fornece orientação curta quando necessário.

### Memória incremental e orientação frente/verso

Requisito adicional: aproveitar cada nova tentativa para acumular experiência reutilizável, incluindo reconhecer que uma carta está de costas. A coleta segue a política de permissão; predições sem confirmação permanecem exemplos não verificados e não mudam decisões futuras automaticamente.

Após detectar a carta, classificar orientação observável como `front`, `back` ou `unknown`, com score próprio e versão do componente. Para múltiplas cartas, essa informação pertence a cada região localizada. Dataset permite rótulos de orientação independentes de identidade, jogo e acabamento, com origem e revisão humanas.

Quando somente o verso estiver visível e houver evidência suficiente, manter preview/tracking e mostrar orientação breve para virar a carta. Não executar pesquisa de Printing/preço nem afirmar qual carta é a partir de um verso compartilhado por muitas cartas. Reconhecer o jogo pelo verso, quando possível, não resolve a Printing. Ao aparecer a frente, selecionar o frame útil e continuar a identificação sem reiniciar toda a sessão.

Cartas com duas faces, versos especiais, sleeves opacas, objetos parcialmente ocultos e jogos desconhecidos podem continuar `unknown`; não bloquear definitivamente reconhecimento por uma classificação incerta de orientação. Preservar a possibilidade de revisão e captura manual.

A aprendizagem é dividida em dois mecanismos:

1. Memória de exemplos: capturas autorizadas e suficientemente verificadas podem ser adicionadas a uma versão incremental do índice de referências reais quando o motor de embeddings existir. Isso pode enriquecer candidatos sem retreinar o encoder. Uma captura confirmada da frente pode ajudar a reconhecer fotos semelhantes daquela impressão; não comprova sozinha seu acabamento, nem ensina um detector a localizar cartas.
2. Evolução dos modelos: exemplos rotulados de presença, frente/verso e acabamento alimentam avaliações e possíveis atualizações futuras de detector/classificadores. Alterar pesos a cada scan não está autorizado nesta entrega. Cada atualização exige versão, benchmark e possibilidade de rollback.

Um exemplo que o scanner aceitou sozinho, mesmo com alta confiança, não entra automaticamente como rótulo correto. Confirmação simples de identidade pelo usuário não confirma orientação, acabamento e localização se esses campos não foram verificados. Separar elegibilidade de cada tarefa para evitar feedback circular.

Modelo, índice e memória local são componentes distintos. Um servidor acrescentar uma amostra ao dataset não atualiza silenciosamente o modelo instalado no celular. Distribuição futura de memória/índices locais utiliza versões, limites de tamanho e atualização controlada; nunca replica capturas privadas de outros usuários para os aparelhos.

Esta entrega prepara rastreabilidade e rótulos. Reconhecimento local de frente/verso, índice adaptativo e publicação de modelos pertencem à implementação seguinte do scanner; não declarar que aprendizado incremental já funciona apenas porque dados são armazenados.

Preparar contrato de exportação de amostras autorizadas, verificadas e com assets disponíveis. Cada versão congelada registra manifest/hash dos arquivos, capturas selecionadas, revisão de rótulos, Printing/Variant esperadas e versão do catálogo. Registrar elegibilidade separada por tarefa.

Uma amostra pode ser adequada para Printing e inconclusiva para Finish. Não contá-la como erro de acabamento quando não existe rótulo confiável desse campo. Capturas negativas entram em avaliação de rejeição/detecção, sem identidade canônica fictícia.

Futuras métricas: Top-1/Top-5 por Printing, acerto por Variant/Finish, latência por estágio, taxa de falha e regressões por amostra. Reruns não exigem novas fotos e não alteram o benchmark congelado.

Separar dados de desenvolvimento e avaliação por carta física/sessão/capturas próximas; impedir que frames correlacionados ou duplicatas vazem entre os grupos. A seleção do encoder será baseada em benchmark com as mesmas imagens e recursos disponíveis, incluindo casos de reprint, idioma e brilho.

Referências oficiais e capturas reais têm papéis diferentes. Embeddings oficiais futuros guardam AssetId, PrintingId, encoder/revisão e pré-processamento. Troca de encoder cria outra versão de índice; vetores de modelos diferentes não são comparados diretamente.

Não implementar nesta entrega modelo de embeddings, pgvector, treinamento, augmentation, classificador de acabamento, seleção automática de frames ou autenticação de certificados PSA. Uma etiqueta lida na imagem não comprova a autenticidade do certificado.

## Incremento recomendado e verificações

Entregar primeiro o modelo/contratos, persistência PostgreSQL, integração privada de Assets, registro opt-in de tentativas, Run imutável e feedback validado. Preparar promoção e exportação sem coletar automaticamente todos os scans. Evitar reescrever o scanner ou condicionar o sync de catálogo a esse módulo.

Migration aditiva e recursos desativados por padrão. Não remover dados de catálogo, usuários, coleção, scanner ou preços existentes. Não adicionar variant obrigatória a tentativas inconclusivas.

Verificar com testes:

- Autorização: usuário não lê, associa, corrige ou promove Assets/tentativas de outro usuário.
- Política: sem permissão de melhoria não existe promoção; feedback e alta confiança não a concedem.
- Identidade: variante pertence à Printing; referências inexistentes não são aceitas.
- Rastreabilidade: predição original não muda após correção ou rerun; componentes ausentes continuam nulos.
- Multi-frame: capturas e candidatos permanecem associados à tentativa/Run corretos.
- Idempotência: retries da mesma execução/feedback não criam eventos duplicados; novas tentativas legítimas continuam distintas.
- Falha: dataset indisponível não transforma reconhecimento válido em falha; nenhum Run é declarado salvo quando a gravação falhou.
- Storage: validar upload/read em MinIO e contrato S3, sem acesso público das capturas.
- Compatibilidade: endpoint e cliente atuais continuam funcionando sem AttemptId e sem dataset habilitado.
- Benchmark: exportação sem dados pessoais, imagens disponíveis, rótulos por tarefa e manifestação de versões.

Na etapa posterior do scanner, medir também tempo de entrada até captura, falsos disparos em cenas sem carta, captura com movimento, cartas inclinadas/encapsuladas e rearmamento de cópias idênticas. As métricas de detector e de identidade são independentes.

Implementação de detector/embeddings/OCR e benchmark executável completo continua uma etapa posterior. As 50–100 fotos sugeridas serão uma primeira medição, não uma garantia de precisão em produção.

## Situação desta proposta

Documento produzido a partir dos dois prompts de arquitetura e da leitura do código atual. Nenhuma entidade, migration, endpoint, coleta de imagem ou modelo novo foi implementado nesta etapa. Não foram executados testes de código porque o produto não foi alterado.
