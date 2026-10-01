# Validação nativa do vídeo

Este teste utiliza a câmera do emulador, captura uma foto enquanto grava e exporta
um MP4 com áudio, duas revelações e valores **sintéticos** em BRL. Não autentica,
não acessa provedores e não altera coleções. O teste é excluído das compilações
normais e de todas as compilações Release.

Compile para o emulador:

```powershell
dotnet build src/Vaulta.App/Vaulta.App.csproj -c Debug -f net10.0-android `
  -p:RuntimeIdentifier=android-x64 -p:RuntimeIdentifiers=android-x64 `
  -p:ApplicationId=com.vaulta.scannervalidation -p:VaultaVideoValidation=true
```

Instale o APK assinado apenas no emulador. Conceda as permissões `CAMERA` e
`RECORD_AUDIO` ao pacote de teste. Inicie a atividade principal com o extra
booleano `vaulta-video-validation=true`. A atividade principal pode ser obtida
com `adb shell cmd package resolve-activity --brief com.vaulta.scannervalidation`.

O diretório privado `files/video-validation` contém o resultado, a filmagem
original, o MP4 final, a linha do tempo e três frames decodificados. Consulte
`result.txt` com `adb shell run-as com.vaulta.scannervalidation cat ...`.
Confira orientação e proporção 9:16, áudio, os totais 0 → 25 → 275 e as duas
revelações. O arquivo original deve continuar disponível.

Este teste confirma a integração nativa somente quando executado com sucesso.
Áudio de voz inteligível, reflexos das cartas, desempenho de gravações longas,
interrupções e compartilhamento para redes sociais também exigem um aparelho
real. Nunca trate a simples compilação como evidência de um vídeo exportado.
