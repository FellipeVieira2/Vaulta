# Scanner automático — 02/10/2026

Base: `207385a`. Implementação: `50c7e44`. Branch: `codex/scanner-automatic-flow`.

## Comportamento

Abrir o scanner já inicia a câmera e a detecção local. Basta colocar a carta no guia por cerca de um segundo. A câmera vazia não dispara o GPT. A foto é capturada uma vez; a pessoa pode retirar a carta enquanto a API identifica a imagem salva. Não há outra captura durante a identificação ou a revelação do valor.

Depois de identificar e salvar a ocorrência, apenas o valor aparece por cerca de um segundo, com animação discreta. O total permanece acumulado. Não há painel da última carta, ações de venda/coleção ou botão “Próxima carta” bloqueando a sequência. A câmera fica pronta para a próxima automaticamente. Gravação e botão central de captura foram retirados da tela ao vivo; captura manual permanece nas opções como alternativa. Clipes antigos continuam disponíveis na revisão.

O detalhe das cartas, certificação, remoção, coleção e venda fica na revisão final da sessão. Retomar a sessão abre a câmera diretamente, sem reabrir as ações da última carta.

## Confiança e acabamento

`ScannerConfidencePolicy.AutoAcceptThreshold = 0.8`, compartilhado pela API, matcher e aplicativo. O limite inclui exatamente 80%. Nome/número/jogo/idioma ainda precisam resolver uma impressão canônica válida; impressões concorrentes ou evidências incompatíveis são rebaixadas e requerem revisão.

O GPT identifica normal, holo e reverse. A API mantém classificações com confiança de pelo menos 80%. Abaixo disso, o usuário confirma o acabamento; nem uma variante única no catálogo autoriza deduzir a classificação que o modelo não conseguiu ler. Uma classificação confiável ausente do catálogo é preservada sem escolher outra variante ou atribuir seu preço.

Prompt `card-evidence-openai-v5`, contrato estruturado `schemaVersion 4`, modelo `gpt-6-luna`. Uma extração por foto, sem busca, preços, IDs internos ou autenticação de certificados pelo modelo. O prompt orienta a examinar ilustração, corpo, bordas e padrões físicos, distinguir reflexos de sleeve/slab e classificar normal quando há evidência de superfície não metálica. Não permite elevar confiança artificialmente para evitar confirmação.

`Finish` no resultado visual representa normal/holo/reverse aceito. `SurfaceTreatment` preserva textura/full art separadamente; layout não substitui evidência de foil. Empresa, nota e certificado são lidos da etiqueta e mostrados na revisão final como leitura não verificada. Preço de carta comum continua excluído da avaliação de uma certificação.

## Detecção e validação

Duas amostras consecutivas sem carta (250 ms entre elas) rearmam outra cópia idêntica, mesmo com assinatura visual semelhante ao fundo. Uma única falha de detecção não rearma a carta parada. O bloqueio durante a identificação continua impedindo chamadas simultâneas.

- Testes inicialmente reproduziram a rejeição de 80%/85%, a falha de rearme de cópia idêntica e a escolha indevida de variante única.
- 174 testes App.Core e 235 Identity unitários passaram.
- Quatro testes HTTP/PostgreSQL do scanner passaram em banco isolado novo. Um teste anterior encontrou corretamente ambiguidade causada por dados repetidos de uma execução prévia; os asserts não foram afrouxados.
- Revisão independente final sem problemas acionáveis remanescentes.

Os testes não substituem validação de câmera, reflexos, detecção e animação no aparelho físico. Cobertura de descoberta externa/preço segue Pokémon/TCGdex; outros jogos sem impressão local e certificações sem integração de pricing continuam com valor pendente, sem preço inventado.

## Artefatos e produção verificados

- APK: `artifacts/Vaulta-scanner-automatic-2026-10-02-arm64.apk`, Android ARM64, Debug para teste, `com.vaulta.app`, 57.917.147 bytes. Assinatura v2/v3 validada. Build sem avisos ou erros.
- SHA-256: `AEFE2B5AF2514AC1933877AC909CE0DBAA9160B464B0B209B035700D39946DF8`.
- API publicada na AWS: `142767402064.dkr.ecr.us-east-1.amazonaws.com/vaulta-api:50c7e44-automatic-scan-20261002`.
- Digest da imagem: `sha256:69ef568bb3faa6f389745b0aef34d174cee5db3b069f77b698904e97a3c36099`.
- Deploy SSM `fd2bcc02-7db0-4bac-b19b-500795a97b8c`: Success, saída 0. Contêiner iniciou em `2026-10-02T23:09:56.426236599Z`; verificação independente confirmou essa imagem em execução e HTTP 200/Healthy em `/health` e `/health/ready` públicos.
- Diagnóstico real SSM `6ba6d2e3-4500-49c6-ab83-119646cb984a`: Success, saída 0, sem stderr. Uma imagem pública do TCGdex foi processada pelo serviço publicado: Golisopod, 140 PS, `026/086`, português, impressão canônica Caos Ascendente com confiança 0,95 em 6,929 s. O GPT retornou normal; a cotação existente dessa variante era R$ 0,12. A reverse tinha R$ 1,25 e não foi usada como preço da normal. Esses valores são referências internacionais já armazenadas, não estimativas do GPT.

Esse diagnóstico usa artwork de catálogo, não uma foto de carta física ou uma leitura de câmera. Não valida reflexos, acabamento real, calibração da confiança ou tempo de detecção no aparelho. O teste HTTP com autenticação e banco isolado e os testes unitários cobrem os contratos; a experiência de câmera deve ser conferida seguindo [teste no celular](../scanner-phone-test.md).

O código foi commitado localmente. Não há push ou PR novo confirmado nesta execução.
