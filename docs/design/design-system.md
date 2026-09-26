# Design system Vaulta — MAUI

Os arquivos exportados do Figma em `docs/design/tokens/` são a fonte de verdade. O token semântico disponível é `semantic.tokens.json`, com `com.figma.modeName` igual a `Dark`; o caminho `semantic-dark.tokens.json` não existe neste repositório. Não há uma paleta Light nesta fundação.

## Camadas e cores

`primitives.tokens.json` é mapeado para resources `Primitive*` em `src/Vaulta.App/Resources/Styles/Colors.xaml`. `semantic.tokens.json` referencia essas primitives; a camada semântica MAUI usa nomes de intenção para as telas, sem copiar hexadecimais para XAML de página.

| Token semântico Figma | Primitive Figma | Resource MAUI |
|---|---|---|
| `background/primary` | `neutral/950` | `BackgroundPrimary` |
| `background/secondary` | `neutral/900` | `BackgroundSecondary` |
| `background/tertiary` | `neutral/850` | `BackgroundTertiary` |
| `surface/default` | `neutral/900` | `SurfaceDefault` |
| `surface/elevated` | `neutral/800` | `SurfaceElevated` |
| `surface/overlay` | `neutral/800` | `SurfaceOverlay` |
| `text/primary` | `neutral/white` | `TextPrimary` |
| `text/secondary` | `neutral/400` | `TextSecondary` |
| `text/tertiary` | `neutral/500` | `TextTertiary` |
| `text/disabled` | `neutral/600` | `TextDisabled` |
| `text/inverse` | `neutral/950` | `TextInverse` |
| `border/default` | `neutral/700` | `BorderDefault` |
| `border/subtle` | `neutral/800` | `BorderSubtle` |
| `border/strong` | `neutral/500` | `BorderStrong` |
| `brand/primary` | `brand/400` | `BrandPrimary` |
| `brand/secondary` | `brand/500` | `BrandSecondary` |
| `brand/muted` | `brand/800` | `BrandMuted` |
| `status/success` | `success/400` | `StatusSuccess` |
| `status/warning` | `warning/400` | `StatusWarning` |
| `status/error` | `error/400` | `StatusError` |
| `interactive/default` | `brand/400` | `InteractiveDefault` |
| `interactive/hover` | `brand/300` | `InteractiveHover` |
| `interactive/pressed` | `brand/600` | `InteractivePressed` |
| `interactive/disabled` | `neutral/700` | `InteractiveDisabled` |

As primitives de cores exportadas (neutros, brand, success, warning e error) também ficam disponíveis como `PrimitiveNeutral*`, `PrimitiveBrand*`, `PrimitiveSuccess*`, `PrimitiveWarning*` e `PrimitiveError*`.

## Espaçamento e radius

`spacing.tokens.json` é mapeado diretamente para `Spacing4`, `Spacing8`, `Spacing12`, `Spacing16`, `Spacing20`, `Spacing24`, `Spacing32`, `Spacing40` e `Spacing48`, em `Spacing.xaml`.

`radius.tokens.json` é mapeado para os recursos MAUI `CornerRadius` em `Dimensions.xaml`:

| Figma | MAUI | Valor |
|---|---|---:|
| `none` | `RadiusNone` | 0 |
| `xs` | `RadiusXs` | 4 |
| `sm` | `RadiusSm` | 8 |
| `md` | `RadiusMd` | 12 |
| `lg` | `RadiusLg` | 16 |
| `xl` | `RadiusXl` | 20 |
| `2xl` | `Radius2Xl` | 24 |
| `full` | `RadiusFull` | 999 |

## Tipografia

`typography.tokens.json` define Inter e a escala aplicada em `Typography.xaml`. Como o MAUI trata `LineHeight` como multiplicador, cada valor absoluto do Figma foi convertido por `line-height / font-size`.

| Estilo | Figma (tamanho/linha) | Peso Figma | `LineHeight` MAUI |
|---|---:|---:|---:|
| Display | 36/44 | 700 | 1.2222 |
| H1 | 28/36 | 600 | 1.2857 |
| H2 | 24/32 | 600 | 1.3333 |
| H3 | 20/28 | 600 | 1.4000 |
| Title | 17/24 | 600 | 1.4118 |
| Body | 15/22 | 400 | 1.4667 |
| Body Small | 13/20 | 400 | 1.5385 |
| Label | 13/18 | 600 | 1.3846 |
| Caption | 11/16 | 500 | 1.4545 |

Aliases previstos no bootstrap do MAUI: `InterRegular`, `InterMedium`, `InterSemiBold` e `InterBold`, correspondendo a `Inter-Regular.ttf`, `Inter-Medium.ttf`, `Inter-SemiBold.ttf` e `Inter-Bold.ttf`. Os aliases e fontes MAUI são ativados somente se o arquivo correspondente existir em `Resources/Fonts`.

**Pendência:** não há arquivos Inter no repositório. Nenhuma fonte foi baixada. Até que os assets licenciados sejam adicionados, os controles usam as fontes nativas do sistema; pesos 500/600 não podem ser reproduzidos com exatidão apenas por `FontAttributes`.

## Componentes e galeria

`Components.xaml` contém apenas styles básicos globais com tokens semânticos. Não define aparência final de componentes Vaulta; isso fica para as referências Figma de Components.

`DesignSystemGalleryPage` demonstra cores, escala tipográfica, espaçamentos e raios. Ela é registrada e navegável somente em Debug pelo botão **Foundations gallery** da página inicial; não é uma rota de produção.
