# Scanner por sessão

Requisito do produto confirmado em 01/10/2026: a abertura de pacotes deve ser visualmente atraente para Instagram/TikTok e funcionar como fluxo confiável de identificação, avaliação e entrada no estoque. A sessão é o diferencial do app, não apenas uma tela para tirar fotos.

## Experiência acordada

- Câmera em destaque; referência de enquadramento da carta e valores no canto superior direito.
- Cada carta identificada apresenta edição e variante para confirmação. A soma produz um som e uma revelação; cartas valiosas recebem um efeito especial.
- Quantidade de pacotes opcional. Custo opcional por pacote ou pelo conjunto: cinco pacotes de R$ 20 equivalem a R$ 100. Mostrar o valor das cartas crescendo a partir de zero em relação ao custo informado.
- Valores e diferenças em reais. A diferença é uma estimativa baseada nas referências de mercado, e não lucro de uma venda realizada. Cartas sem cotação tornam o total parcial.
- Cópias físicas repetidas podem ser adicionadas. Repetir uma requisição da mesma identificação não duplica a carta.
- Finalizar, revisar/remover cartas e perguntar se o usuário deseja adicionar todas ao estoque. Importação retomável, inclusive quando o servidor confirma e a resposta se perde.
- **Gravação opcional:** iniciar somente quando o usuário tocar em “Gravar com voz e efeitos”. A sessão inteira funciona sem gravar. O microfone é solicitado apenas ao iniciar a gravação. O usuário pode parar e começar outro clipe na mesma sessão; nenhuma sessão inicia uma gravação automaticamente.
- Quando escolhida, gravar vídeo vertical com voz, sons do app, revelações e valores incorporados à imagem, pronto para compartilhar. A voz foi explicitamente confirmada pelo usuário.
- As cartas adicionadas passam a compor a avaliação da coleção. “Carteira” neste contexto é o valor do acervo; permanece a decisão de não criar saldo financeiro ou saques.

## Implementação atual e evidência

`Vaulta.App.Core/Catalog/ScannerSession.cs` calcula o custo e a avaliação, distingue falta de preço de valor zero, congela os dados durante a importação e mantém uma identificação por cópia física. Cada preço guarda fonte/data e, quando disponível, valor original/câmbio. Não grava preço de mercado como custo de aquisição da carta: o custo informado pertence à sessão.

`FileScannerSessionStore` grava sessões por conta e faz substituição do arquivo por meio de um temporário. `ScannerSessionImporter` persiste o início da importação antes do primeiro pedido e utiliza a mesma chave de idempotência por carta nas retomadas. Uma nova sessão é criada somente depois de preencher e confirmar o início; o histórico salvo não é apagado.

Treze testes portáveis de sessão, vídeo e leitura contínua passaram: custo, cópias repetidas, fases, recuperação da importação, armazenamento por conta, posição dos sons, compensação do início assíncrono do encoder, recuperação após arquivo bloqueado/cópia incompleta e mudança/estabilidade da cena. Evidência atual: `artifacts/scanner/session-continuous-tests.log`. Esses testes não confirmam câmera ou MP4 nativo.

`ScannerSessionPage` está registrada no Shell e na navegação do scanner. O código usa CameraView, confirma edição/variante/condição, limita a foto a 15 MB, prefere a câmera traseira e oferece busca por nome. Guarda o progresso antes de atualizar a tela. A troca de conta cancela operações e retira dados da sessão da interface.

O visual usa câmera ao fundo, painel superior de valores, barra em relação ao custo, identificação da última carta, soma animada, partículas e revelação dourada a partir de R$ 100. Os controles ficam desabilitados enquanto uma ação está em andamento. O usuário pode desligar o som e editar quantidade/custo durante a sessão. Animações respeitam a preferência do sistema de movimento reduzido.

`ScannerSessionPage.Continuous.cs` inicia leitura contínua independente da gravação. Amostra localmente a região central da prévia a cada 600 ms e espera estabilidade por pelo menos 900 ms; não envia todos os frames ao servidor. Depois da consulta, mantém a cena consumida até detectar uma mudança. Mostra “Mantenha a carta…” durante a consulta e “Pode retirar a carta” após adicionar. A soma automática exige nome/número fortes, margem em relação ao segundo candidato e variante única ou acabamento previamente escolhido pelo usuário. Apenas nome não autoriza a soma automática. Edições/variantes incertas e outra cópia da última carta exigem confirmação; o usuário pode pular, pausar ou identificar manualmente. Os limiares de movimento ainda precisam de ajuste com montinhos reais no celular.

O Android compilou com CameraView 6.1, MAUI Controls 10.0.60 e Media3 1.11 sem avisos ou erros. A execução Android 35 encontrou problemas que a compilação não detectou: início da câmera ainda pendente, retorno de finalização incompatível, exportador Java ausente do pacote e atraso do encoder deslocando a última revelação. A câmera agora aguarda o fluxo nativo `STREAMING`, a finalização aceita o retorno real do toolkit, o Java é incluído explicitamente e a linha do tempo de sons/valores é alinhada à duração final da mídia. A última compilação x64 passou sem avisos/erros (`artifacts/scanner/session-continuous-video-build.log`).

**A exportação nativa foi executada e os frames foram inspecionados.** Evidências em `artifacts/scanner/native-final`: filme original preservado de 156.264 bytes; exportação de 1.815.922 bytes com áudio e 6.028 ms, dimensões codificadas 1920×1080 e rotação 90° (exibição 1080×1920). A captura simultânea da foto produziu 24.166 bytes, e a amostragem local da prévia produziu 288 bytes. Os frames atuais `frame-2590918.png` e `frame-4404427.png` mostram respectivamente R$ 25/carta comum e R$ 275/grande achado de R$ 250, com custo de R$ 100 e diferença estimada de R$ 175. São valores sintéticos; não provam identificação de cartas reais nem qualidade da voz. Arquivos de frames de testes anteriores também podem existir na pasta; usar os tempos correspondentes à linha do tempo desta execução. Os 13 testes de sessão/leitura/vídeo e 12 de reconhecimento/conversão BRL passaram (`artifacts/scanner/session-continuous-tests.log`).

`ScannerSessionPage.Video.cs` grava somente após a ação explícita do usuário. Usa o vídeo/áudio da câmera e persiste uma linha do tempo monotônica relativa ao início da mídia. Finaliza o MP4 antes de desmontar a câmera ao sair. Os arquivos são separados por conta/sessão/clipe, sem apagar o original quando a exportação falha. A recuperação utiliza exclusivamente o arquivo de captura associado àquela gravação; substitui a cópia incompleta somente após concluir outra cópia temporária.

`AndroidSessionVideoExporter` mistura o áudio original com os sons sintetizados na posição das revelações, incluindo uso de fones. O exportador Java monta vídeo vertical 1080×1920 com totais em BRL no canto superior direito, custo/diferença estimada, revelação e partículas. A validação nativa exige proporção 9:16 e uma trilha de áudio. O compartilhamento abre o seletor do Android após a exportação; não publica automaticamente. Há limite de 30 minutos por clipe e controles de espaço livre.

`CollectionValuationService` e `/api/v1/me/collection/valuation` calculam o acervo real por variante e quantidade de todas as unidades ativas, incluindo anunciadas e excluindo vendidas/removidas. Mostram cobertura parcial e diferenças de mercado em reais. Quatro testes de cálculo passaram (`artifacts/scanner/session-valuation-tests.log`). O teste HTTP/PostgreSQL também passou (`artifacts/scanner/session-recovery-and-valuation-linux-tests.log`), incluindo mais de 50 unidades, cópias anunciadas, exclusão de vendidas/removidas e isolamento entre contas/entradas. Home/portfolio/detalhe usam essa avaliação em vez dos antigos números fixos. Comparações com médias do período não são um histórico do saldo do acervo.

As referências atuais são por variante; não são ajustadas automaticamente pela condição da unidade nem equivalem a uma cotação específica do mercado brasileiro. Essa limitação aparece na confirmação e na revisão. Uma edição ou variante ambígua exige confirmação; não somar automaticamente um candidato incerto.

## Pendências para considerar pronto

1. Validar a interface Android, inclusive permissões negadas, orientação, telas pequenas, fonte ampliada, câmera real e comportamento ao voltar/trocar de conta. A compilação já passou; isso não equivale a validação visual/funcional.
2. Testar o MP4 no aparelho real, incluindo compartilhamento. A exportação vertical com áudio e as duas revelações já foram executadas e inspecionadas no emulador; voz inteligível e sincronia acústica ainda precisam de teste físico.
3. Verificar sincronia e preservação da voz no aparelho, além do uso de fones e possível captação acústica dos sons do alto-falante.
4. Validar identificação contínua com montinhos reais, reflexos, consultas lentas/sem rede, movimentos rápidos e cópias físicas iguais. O modo contínuo está implementado; a detecção visual da troca e os limiares de estabilidade precisam de teste no aparelho. Casos incertos mantêm a confirmação de edição/variante.
5. Validar a atualização da avaliação no app ao voltar do scanner. O teste HTTP/PostgreSQL já passou. Histórico temporal do acervo continua pendente; não usar gráficos demonstrativos como prova de valorização real.
6. Testar a exportação longa no aparelho: sincronização voz/efeitos, espaço insuficiente, interrupção, consumo de memória/bateria, compartilhamento e preservação das sessões.

A API pública ainda retornou 404 nas novas rotas `/api/v1/scanner/printings/{id}` e `/api/v1/me/collection/valuation` em 01/10/2026. O usuário confirmou que ainda não fez o deploy. O APK usa a URL pública configurada; testar cotações e estoque após publicar a API. A atualização diária após 05h continua pendente no goal; o cache atual dura dez minutos. Instruções do aparelho em `docs/scanner-phone-test.md`.

APK normal para o celular gerado: `artifacts/apk/Vaulta-scanner-2026-10-01.apk`, ARM64 e `com.vaulta.app`, sem o teste automático do emulador. Compilação sem avisos/erros em `artifacts/scanner/scanner-phone-apk-build.log`; não houve instalação em aparelho físico ou deploy da API.

A restrição anterior de aprovação por limite de uso não impediu as novas compilações. As primeiras tentativas do emulador encontraram pouco espaço/memória e inicialização lenta; a execução Android 35 citada acima foi concluída. Um teste de banco com acesso ao socket Docker foi recusado pela revisão automática; o teste HTTP foi adaptado e passou em PostgreSQL isolado, sem conceder esse acesso ao processo.
