# Teste do scanner no celular

Versão atual: `artifacts/Vaulta-scanner-automatic-2026-10-02-arm64.apk`. O app de teste é Android ARM64, Debug assinado, e usa `https://api.vaultatcg.com.br/`. Consulte [fluxo automático e validação](design/scanner-automatic-flow-2026-10-02.md). Não há gravação na tela ao vivo nem seleção obrigatória de acabamento antes de começar.

1. Entre na conta e abra o scanner. Deve abrir a câmera diretamente. Pacotes e custo são opcionais nas opções da sessão.
2. Deixe a câmera vazia: nenhuma captura, chamada de identificação ou soma deve ocorrer. Não deve pedir microfone nem mostrar controles de gravação.
3. Coloque a carta inteira no guia por cerca de um segundo. A foto deve ser automática, sem tocar em botão. Após “Foto capturada”, retire a carta; a animação de identificação continua na mesma tela.
4. Durante a identificação, mostre outras cartas rapidamente: não devem iniciar chamadas concorrentes nem substituir a foto salva. Aguarde a identificação anterior antes de apresentar a próxima.
5. Com identificação/normal/holo/reverse aceitos a partir de 80%, o valor aparece brevemente, some e permanece somado no total. Nenhum painel da última carta ou botão “Próxima carta” deve ficar bloqueando a câmera.
6. Deixe a mesma carta parada após a revelação: não deve somar novamente. Retire por pelo menos duas amostras da prévia (~250 ms entre elas) e coloque outra cópia: ela deve contar como nova ocorrência após estabilizar.
7. Teste carta normal, holo e reverse com corpo/ilustração visíveis. A classificação é do GPT; confiança abaixo de 80% deve pedir confirmação. Full art e textura não podem substituir a leitura de foil. Reflexos intensos podem exigir outra foto pelo menu de opções.
8. Finalize a sessão. Confira cartas, variantes, total e itens sem cotação. Uma etiqueta de PSA/CGC/BGS deve aparecer com empresa, nota e número, como leitura não verificada; não deve receber o preço da carta comum.
9. Adicione as cartas revisadas ao estoque ou abra a venda pela revisão. Uma carta sem cotação não entra como zero no preço; o total permanece parcial. Preço de referência não é custo de aquisição.
10. Retome a sessão, altere custo opcional, gire o aparelho, teste fonte ampliada, conexão lenta e permissões negadas. Retomar deve abrir a câmera sem ações da última carta. A detecção e animação no telefone ainda precisam de conferência física.

Os valores disponíveis são referências internacionais convertidas para reais, sem ajuste automático de condição. Jogos sem catálogo local e certificações sem integração própria de preço ficam pendentes. Gravações antigas podem continuar disponíveis na revisão, mas não se inicia gravação nova no scanner atual.
