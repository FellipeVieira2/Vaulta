# Presença local antes da captura automática — 02/10/2026

Objetivo: não chamar o scanner/GPT só porque a câmera apontada para um fundo vazio ficou estável.

`CameraSceneSampler.ReadObservation` mantém a assinatura da cena e extrai um preview grayscale com proporção preservada, maior lado de 160 pixels. Não armazena nem envia esse preview. `ScannerCardPresence` procura até oito picos de borda por eixo, quatro bordas com contraste coerente, retângulo vertical centralizado com proporção 0,58–0,82, área de 20–85% e detalhe interno. Trabalho e memória são limitados; sem novo modelo, pacote ou chamada externa.

`ScannerSceneGate.Observe` não libera capturas quando `cardPresent=false` e reinicia a estabilidade. Continua observando mudanças para permitir a próxima cópia após remoção visível, inclusive enquanto a identificação anterior está em andamento. Quando a carta aparece, precisa novamente ficar estável por 1.000 ms; o preview é observado a cada 250 ms. Cena consumida continua sem repetição.

A página Android usa presença antes de iniciar a captura e imediatamente antes de fotografar. Depois da fotografia, a identidade fica ligada ao arquivo salvo: a pessoa pode tirar a carta do enquadramento ou mover o celular sem cancelar o upload ou o resultado. Não existe refinamento automático nem segunda foto. A captura manual em `Opções → Capturar novamente` é preservada para cenas que o filtro não detecta. Ticks que apenas observam a cena não liberam o bloqueio de uma captura em andamento.

Pedido adicional do usuário: animação pequena durante identificação, sem mudar de tela. Um ActivityIndicator animado aparece com “Tirando a foto…” e depois “Foto capturada · identificando…” sobre a própria câmera. O bloqueio `_busy`/`_continuousInFlight` serializa captura, identificação, detalhes e resultado. `try/finally` encerra o indicador em sucesso, falha e cancelamento; `StopCamera` também o encerra. O indicador é ocultado antes da animação de carta adicionada; novas capturas continuam bloqueadas até terminar essa operação. Não há preview congelado, nova página ou overlay que oculte a câmera.

É um filtro geométrico conservador, não um classificador TCG. Um objeto com formato e detalhe semelhantes pode passar. Cartas inclinadas, parcialmente cobertas, com pouco contraste ou muito reflexo podem não passar; enquadrar inteira sobre fundo contrastante ou usar manual. O caminho manual pode fazer chamadas sem carta por escolha explícita do usuário. Não existe promessa de custo zero para toda cena sem TCG.

Verificação: RED tipos/assinatura ausentes; GREEN 12 testes novos com previews sintéticos de fundo claro/escuro, degradê, ruído, cartas com contraste positivo/negativo, papel vazio, quadrado, objeto pequeno/cortado, forma oval, contraste insuficiente, dados inválidos, estabilidade sem carta e remoção/reentrada. Suíte App.Core: 161 testes aprovados. Nenhuma chamada externa foi feita nos testes.

Build final Android ARM64 com filtro + spinner: zero avisos e erros (`artifacts/scanner-presence-loading-android-build.log`). Revisão independente de presença e depois do loading: nenhum achado P1/P2. APK: `artifacts/Vaulta-scanner-presence-loading-2026-10-02-arm64.apk`.

Pendente: calibrar com fotos autorizadas e câmera de aparelho físico, medir falsos positivos/negativos e custo/tempo do preview no dispositivo. Testes sintéticos não comprovam desempenho de detecção no mundo real. A implementação automática existente permanece Android; demais plataformas mantêm o fluxo manual atual.

Validação posterior de uma captura: 159 testes App.Core e 606 testes no conjunto completo, sem falhas; build Android ARM64 sem avisos/erros. APK atual e diagnóstico do serviço em [scanner com descoberta](scanner-fast-identification-2026-10-02.md). Os 161 testes acima pertencem à etapa anterior; os testes do refinamento removido não fazem mais parte da suíte.
