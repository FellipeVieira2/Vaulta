# Teste do scanner no celular

Atualização de 02/10: instalar `artifacts/Vaulta-scanner-result-fixes-2026-10-02-arm64.apk`. Guia e estabilidade corrigidos, prompt GPT v4 com leitura do selo, resultado por acabamento com confirmação/soma em um botão. Protocolo e validação em [correções do resultado](design/scanner-result-fixes-2026-10-02.md). As instruções antigas de preset de acabamento abaixo pertencem ao APK anterior; o novo fluxo usa a leitura visual ou confirma o acabamento no resultado.

Verificar no aparelho: câmera vazia não dispara captura automática; carta no guia por cerca de um segundo dispara uma foto; após “Foto capturada” pode retirar a carta; não inicia outra identificação durante o carregamento; cada acabamento mostra o próprio preço e soma uma única vez; segunda cópia física pode contar novamente após retirar e reenquadrar. Para selo visível, conferir empresa/nota/número e valor pendente de certificação. Testar também rotação horizontal e retomada após abrir opções de custo.

APK gerado em 01/10/2026: `artifacts/apk/Vaulta-scanner-2026-10-01.apk` (56.968.499 bytes), `com.vaulta.app`, ARM64, compilação Debug assinada para teste. O teste automático do emulador não está incluído. Compilação sem avisos/erros: `artifacts/scanner/scanner-phone-apk-build.log`. SHA-256: `DE576420AC8E19E201FFECDD2E6669A57FC8314498D980D6F2D4C8B77BF90EA4`.

O APK de teste usa `https://api.vaultatcg.com.br/`. Publique a API atualizada antes de testar informações, cotações e avaliação do estoque. As novas rotas ainda retornavam 404 em 01/10/2026, antes do deploy informado pelo usuário.

1. Entre na conta e abra uma sessão do scanner. Quantidade e custo dos pacotes são opcionais. Para um montinho com o mesmo acabamento, escolha Normal, Holo ou Reverse; caso contrário, mantenha a confirmação de acabamento.
2. Primeiro use uma sessão **sem tocar em Gravar**. A câmera deve identificar normalmente, sem pedir microfone, iniciar contador de gravação ou criar um clipe.
3. Mostre uma carta e mantenha-a enquanto aparece “Mantenha a carta…”. Depois de “Pode retirar a carta”, retire-a e mostre a próxima. Confira nome, edição, variante, soma em reais e som da revelação.
4. Deixe a mesma carta parada: não deve somar novamente. Outra cópia da última carta pede confirmação para evitar duplicação por movimento da mão/câmera. Uma edição ou variante incerta também pede confirmação. Você pode pular, pausar a leitura ou identificar manualmente.
5. Inicie **Gravar com voz e efeitos** somente se quiser um vídeo. Agora permita o microfone, fale durante a sessão, identifique algumas cartas e pare a gravação. A voz, os sons, as revelações e os valores devem aparecer no MP4 exportado verticalmente. Teste também com fones.
6. Revise/finalize e exporte/compartilhe o vídeo. Confira o total no canto superior direito, a última revelação, a sincronia e a legibilidade ao abrir no aplicativo de destino. Compartilhar abre o seletor do Android; não publica sozinho.
7. Adicione as cartas revisadas ao estoque e volte à coleção. Confira quantidades e avaliação real, incluindo cartas sem preço marcadas como total parcial. O preço de mercado não deve aparecer como custo de aquisição da unidade.

A referência atual é internacional, convertida pela PTAX para reais e sem ajuste automático pela condição física. O valor menos o custo dos pacotes é uma diferença estimada, não lucro de uma venda realizada.

Também precisam de conferência no aparelho: reflexos, mão passando rapidamente sobre o montinho, câmera inclinada, fonte ampliada, permissões negadas, conexão lenta, saída/retorno à tela e gravações longas. A validação do emulador usa cartas/valores sintéticos e não substitui esses testes.
