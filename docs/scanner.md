# Scanner de cartas

## Provedor de extração — 02/10/2026

Development e Production estão configurados para `openai` com fallback `ocr`. A configuração base/Testing usa OCR. Defina **somente no servidor** `OPENAI_API_KEY` (ou `Scanner__OpenAI__ApiKey` via configuração segura); sem chave, OpenAI não faz requisição e o OCR continua disponível. O Compose encaminha essa variável explicitamente. Para testar só OCR, configure `Scanner__Recognition__Provider=ocr` no host ou `SCANNER_RECOGNITION_PROVIDER=ocr` no Compose. Nenhuma chave pertence ao MAUI.

Fluxo: carta presente e estável por um segundo → uma foto → `/identify` → extração visual estruturada GPT → catálogo local → descoberta TCGdex se não houver correspondência → importação/resolução canônica. A carta não precisa existir previamente na Vaulta. O GPT não fornece ID, preço ou condição. Candidatos ambíguos permanecem para revisão e acabamentos vêm das variantes reais do catálogo. Sem evidência confiável de jogo/idioma, o matcher limita a confiança e impede inclusão automática.

Modelo configurável `gpt-6-luna`, Responses API sem tools/web search e schema estrito de oito campos visíveis, incluindo HP/PS. Para Luna, `reasoning.effort=none` mantém a extração dentro do orçamento de saída; modelos antigos configurados por override não recebem esse parâmetro. Prompt `card-evidence-openai-v2`, schemaVersion 2. Uma foto por cena, sem refinamento automático; cada extração admite um retry transitório por padrão. Falta de saldo retorna `credits_exhausted` sem repetir a chamada. A cena consumida não se repete em cada tick. Não existe cache de identidade por semelhança de imagem.

No Android, a captura automática exige presença geométrica local antes da estabilidade: quatro bordas coerentes de um retângulo vertical centralizado, proporção de carta e detalhe interno. Fundo vazio/contorno insuficiente permanece no celular; a verificação final ocorre antes da foto. `ScannerCardPresence` opera em preview grayscale pequeno (maior lado 160 px), sem rede/dependência de ML, a cada 250 ms. É um filtro geométrico, não identificação semântica de TCG: objetos semelhantes podem passar, e pouco contraste, oclusão ou inclinação podem bloquear uma carta real. `Opções → Capturar novamente` permite captura manual deliberada. [Implementação e limites](design/scanner-presence-2026-10-02.md).

Após disparar a captura, um pequeno spinner aparece na própria câmera: “Tirando a foto…” e depois “Foto capturada · identificando…”. A pessoa pode remover a carta assim que a foto foi capturada. `_busy`/`_continuousInFlight` impedem novas capturas até concluir identificação, consulta de detalhes e resultado/revisão. O indicador sai em `finally` inclusive em erro/cancelamento, e é encerrado ao parar a câmera. A resposta pertence à foto salva; não depende da carta continuar no enquadramento. A observação local continua durante a espera para detectar remoção e permitir outra cópia depois do resultado.

O resolver cruza nome, idioma, número completo e HP quando legível. Um número sem denominador no banco não confirma uma evidência como `026/086`. A descoberta exige nome/número/idioma/jogo confiáveis, consulta no máximo cinco detalhes e tem prazo total de oito segundos. Código físico de coleção não verificável exige revisão e não é comparado a IDs do provedor. Importações parciais preservam outras cartas e os nomes canônicos das expansões; nomes traduzidos viram aliases da mesma expansão. Sem impressão resolvida, `visualIdentification` preserva a leitura provisória para busca manual; não cria IDs. `serviceIssue` distingue falha do serviço de imagem ilegível.

`GET /printings/{id}` continua separado: snapshot PostgreSQL válido primeiro, atualização TCGdex/PTAX somente quando necessária, protegida por advisory lock. Identificação não consulta preço ou JustTCG. Nova permanece disponível por seleção explícita; não participa do fallback padrão de GPT. [Implementação, segurança, configuração e validações](design/scanner-openai-2026-10-02.md). [Dataset local autorizado](scanner-eval.md).

O scanner está disponível no início e na coleção, inclusive quando a coleção já tem cartas. É necessário entrar na conta. A captura usa a câmera do Android; a API autenticada identifica candidatos no catálogo local, e o usuário confirma a edição antes de adicionar.

O detalhe recupera a imagem e as informações da TCGdex. Preços por variante são convertidos para reais pela PTAX de venda do Banco Central. Veja [valores de mercado](market-pricing.md) para a definição das comparações por período. O custo de aquisição é um campo opcional separado, informado pelo usuário em reais.

## Preparação do servidor

O Dockerfile instala Tesseract e os idiomas inglês/português. Ao executar fora do contêiner, instale o executável e configure `Scanner:Ocr:ExecutablePath` e, se necessário, `Scanner:Ocr:DataPath`. O limite é 15 MB por imagem, com JPEG, PNG ou WebP; a identificação também limita a resolução de entrada e o tempo de OCR.

As migrações incluem a extensão PostgreSQL `pg_trgm`, usada para tolerar erros de leitura. A identificação automática atualmente suporta Pokémon. Edições ausentes podem ser descobertas sob demanda na TCGdex a partir de evidência suficiente; importar expansões antecipadamente continua útil. Exemplo para uma expansão:

```powershell
docker compose exec -T vaulta-api dotnet Vaulta.Web.Api.dll --catalog-sync tcgdex base1
```

A TCGdex e o serviço público PTAX precisam estar acessíveis por HTTPS. Preço ou câmbio indisponível é mostrado como indisponível, sem valores simulados.

## Validação

Correção atual, testes e limites: [identificação com uma captura e descoberta de catálogo](design/scanner-fast-identification-2026-10-02.md). Média de sete dias, quando fornecida pela Cardmarket, é exibida como referência internacional convertida em reais. A produção também atualiza diariamente impressões já consultadas; veja [atualização diária](daily-market-prices.md).

`scripts/Smoke-Scanner.ps1` testa uma imagem real de Pikachu do provedor, identifica a edição, consulta informações e preços em BRL e adiciona à coleção com repetição idempotente. Execute apenas contra um ambiente de teste: o script cria uma conta e um item de coleção.

```powershell
./scripts/Smoke-Scanner.ps1 -BaseUrl http://localhost:8080
```

Para compilar uma versão Debug apontando para a API local no emulador Android, passe `-p:VaultaDevelopmentApiBaseUrl=http://10.0.2.2:8080/`. Esse ajuste é incluído apenas em builds Debug; a configuração padrão de produção é preservada.

A precisão com fotos do mundo real ainda exige testes de câmera: iluminação, reflexos, perspectiva, idioma e cartas com layouts diferentes. OCR não determina condição física nem acabamento holográfico; esses dados devem ser confirmados pelo usuário.

### Validação local em 01/10/2026

O fluxo completo da API passou com as imagens reais do provedor de Pikachu 58, Charizard 4 e Professor Oak 88, do Base Set: identificação, busca manual da mesma edição, imagem e informações, preço em BRL, três comparações por média e inclusão idempotente na coleção. As cotações são consultadas durante o teste e não estão fixadas no código.

O aplicativo Android compilou sem avisos ou erros. O acesso ao scanner foi observado na tela inicial e a tela de captura foi conferida no emulador. A abertura da câmera, a tela de resultado e a confirmação de inclusão ainda precisam de conferência visual: as tentativas seguintes foram interrompidas por falhas e lentidão do emulador. Isso não valida a precisão com fotos de cartas físicas.

Os testes automatizados do scanner passaram: 13 de reconhecimento/OCR/preços e 3 do cliente. A correção final do OCR foi compilada e testada no contêiner local; ainda é necessário concluir a reconstrução integral da imagem da API. Uma tentativa capturou uma alteração simultânea no módulo de pagamentos, e a repetição posterior não foi executada por indisponibilidade da revisão automática de permissões.
