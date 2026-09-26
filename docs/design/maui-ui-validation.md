# Validação do refinamento MAUI

## Verificado neste ambiente

- Compilação do código compartilhado, incluindo XAML: zero erros e zero avisos.
- Verificação isolada do ViewModel com autenticação fake: validação da senha, estado ocupado, bloqueio de envio duplicado, preservação dos valores após falha e mensagem de erro sem detalhes internos passaram. Essa verificação não exercita controles nativos.
- `git diff --check`: sem erros de whitespace.
- Metadados reais TCGdex de `sv03-223`: Charizard ex; imagem externa `https://assets.tcgdex.net/en/sv/sv03/223/high.webp` respondeu HTTP 200.
- O build Android foi tentado, mas parou em XA5300: SDK Android ausente e JDK incompleto. A instalação das dependências também falhou ao acessar o feed do instalador.

Comando usado para validar exclusivamente a camada compartilhada, sem alterar os targets do projeto:

```sh
dotnet build src/Vaulta.App/Vaulta.App.csproj \
  -p:TargetFrameworks=net10.0 -p:RuntimeIdentifiers= -p:OutputType=Library \
  -m:1 -nr:false -p:UseSharedCompilation=false
```

Esse comando não gera APK nem valida renderização, APIs específicas de plataforma ou desempenho de animação. Não houve execução em emulador/dispositivo e não foram produzidas capturas de runtime. As imagens em `screens/` continuam sendo referências de design.

## Conferir antes de aprovar visualmente

1. Android e iOS: primeira abertura, duas etapas do onboarding, pular, cadastro, login, reiniciar e logout. Confirmar que logout offline também sai da área autenticada.
2. Larguras de 320, 390 e 768 dp, orientação horizontal e fonte ampliada: texto sem corte, rodapé alcançável, rolagem e teclado sem esconder ações.
3. Login inválido e timeout: mensagem legível, mesmos campos preenchidos, sem reconstrução do formulário. Durante envio, indicador ativo e novo envio bloqueado.
4. Mostrar/ocultar senha e requisitos de cadastro; quantidade/custo preservados ao mudar a condição.
5. Abas com nomes e ícones; TalkBack/VoiceOver identificando campos e botões; foco e volta consistentes.
6. Movimento normal/reduzido: entrada e toque sem salto, sem botão preso em escala reduzida ao sair ou cancelar toque.
7. Grade com uma/duas colunas, artwork online e fallback offline. Confirmar legibilidade das legendas e preços demonstrativos.

## Limites funcionais preservados

Os exemplos de catálogo, coleção, scanner, pricing e mercado não são dados reais do usuário. Login Google e recuperação de senha continuam sem integração. Os botões de prévia não equivalem à seleção de uma Printing real. O fluxo MAUI com `PrintingId`/`VariantId` ainda exige integração do cliente com a API, fora deste refinamento visual.
