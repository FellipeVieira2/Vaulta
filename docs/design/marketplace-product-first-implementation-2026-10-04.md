# Marketplace Product First — implementação local

Data: 04/10/2026. Design: Figma `Ey1vEJNXx1oaSZbODMamWY`, Home `4:2`, produto `60:461`, anúncio `4:85`, filtros `60:462`, vendedor `60:463`.

## Comportamento implementado

- A Home apresenta produtos por `PrintingId + VariantId`, com imagem de referência, edição/número/idioma, acabamento, menor preço pedido e quantidade de ofertas. Exibe duas, três ou quatro colunas conforme a largura.
- O produto abre ofertas paginadas daquela identidade exata, ordenadas pelo preço. A oferta abre o anúncio físico, com fotos do vendedor, condição, descrição e acesso ao checkout existente.
- O anúncio oferece navegação para o mercado da carta. A comparação deixou de ser carregada automaticamente nessa tela; as páginas existentes de vendedor continuam usando os anúncios.
- Os filtros usam sets pesquisáveis e paginados, idiomas e acabamentos reais do catálogo, condição, intervalo em reais e presença de fotos. Buscar mais sets conserva os filtros escolhidos, inclusive a opção “Todos”.
- O scanner recebeu somente um acesso explícito às ofertas da carta identificada, com seleção de acabamento quando necessário. A leitura e a gravação opcional existentes foram preservadas; não houve alteração do algoritmo de reconhecimento nesta etapa.

## API

Rotas públicas adicionais, sob `/api/v1/marketplace`:

| Rota | Resultado |
| --- | --- |
| `GET /products` | Produtos agregados antes da paginação; filtros e ordenação |
| `GET /products/{printingId}/variants/{variantKey}` | Identidade e mercado do produto, inclusive sem ofertas |
| `GET /products/{printingId}/variants/{variantKey}/offers` | Anúncios da impressão e variante exatas |
| `GET /product-filters` | Sets paginados, idiomas e acabamentos do catálogo |

`variantKey` é um UUID ou `none`. `none` significa variante nula explicitamente e não mistura acabamentos. A busca original de anúncios mantém sua semântica anterior.

Os totais e mínimos usam somente anúncios e vendedores ativos, em BRL, de impressões ativas e variantes ativas quando declaradas. A agregação e a paginação são feitas no PostgreSQL. Metadados e cotações são lidos em lote para a página; a Home não resolve URLs assinadas das fotos dos vendedores.

As cotações vêm exclusivamente dos snapshots locais do catálogo, com fonte, data, moeda/valor original e conversão para reais. Não há consulta a provedores pagos ao abrir o marketplace. Ausência, payload incompleto ou falta de cotação para aquele acabamento produz “Cotação indisponível”. A aplicação não inventa valores, histórico, vendas ou variações percentuais.

URLs relativas das imagens do catálogo são resolvidas contra a origem da API no cliente. Os controladores protegem contra respostas antigas, cancelam buscas ao sair das telas e preservam ofertas já carregadas quando a próxima página falha.

## Revisão

Revisão independente e somente leitura do diff local identificou variantes desativadas no agrupamento, comparação de idiomas regionais e restauração de filtros limpos durante a busca de sets. As correções foram aplicadas e a segunda revisão não encontrou problemas críticos ou importantes. O botão Limpar também foi desabilitado durante o carregamento das opções para impedir uma resposta pendente de restaurar filtros.

## Validação final

As seis suítes passaram em ambiente isolado, sem falhas ou testes ignorados:

| Suíte | Testes aprovados | Evidência em artifacts/ |
| --- | ---: | --- |
| Integração da API, incluindo os oito casos de produtos | 211 | marketplace-integration-suite.log |
| Núcleo do app | 224 | marketplace-core-suite.log |
| Comércio, pedidos e pagamentos | 78 | marketplace-commerce-suite.log |
| Identidade e regras dos módulos | 384 | marketplace-identity-suite.log |
| Arquitetura | 6 | marketplace-architecture-suite.log |
| Encoder visual | 8 | marketplace-vision-suite.log |
| **Total** | **911** | |

A validação final inclui variantes desativadas antes da paginação, idioma regional `pt-BR`, snapshots incompletos, resolução da imagem de referência no anúncio, rotas `none`, recuperação de ofertas e isolamento de respostas antigas.

- Android Debug ARM64: build completo aprovado, zero erros e zero avisos (`artifacts/marketplace-android-build.log`).
- Compilação da API em Release: execução final em andamento; resultado registrado ao terminar.
- `git diff --check`: aprovado.

O bloqueio inicial por falta de espaço no C: foi resolvido após a limpeza feita pelo usuário. Dependências de compilação e serviços temporários de teste foram restaurados. Os testes rodaram em Linux, sem desativar o Smart App Control do Windows.

## APK para teste

Arquivo: `artifacts/apk/vaulta-marketplace-product-first-debug.apk` (136.075.081 bytes, aproximadamente 130 MiB).

- Pacote: `com.vaulta.app`, versão 1.0, ARM64, target SDK 36.
- Assinatura validada pelo `apksigner`, esquemas v2 e v3, um assinante.
- SHA-256 do APK: `999c11554642ab73b637df27c9dd7efc5e68c7a4d5c0590a027d43f6b8904159`.
- Modelo do scanner presente dentro do APK: 89.117.001 bytes, SHA-256 `583fd1110a514667812fee7d684952aaf82a99b959760c8d7dca7e0ab9839299`, correspondente ao manifesto fixado.
- Metadados da verificação: `artifacts/apk/vaulta-marketplace-product-first-debug.metadata.json`.
- Origem da API configurada: `https://api.vaultatcg.com.br/`.

Comando utilizado a partir da raiz do repositório:

```powershell
dotnet build src/Vaulta.App/Vaulta.App.csproj -f net10.0-android -c Debug -m:1 -p:UseSharedCompilation=false -p:RuntimeIdentifier=android-arm64 -p:RuntimeIdentifiers=android-arm64 -p:AndroidPackageFormats=apk -p:VaultaVisionModelPath=C:/Users/Fellipe.Souza/source/repos/Vaulta/artifacts/vision-improvement/models/clip-base/model.onnx
```

Não havia celular ou emulador conectado ao ADB. Portanto, o comportamento visual e de toque no dispositivo permanece para teste com este APK.

## Dependências para teste e publicação

Esta etapa é local: não houve deploy nem push remoto. O aplicativo atualizado depende da publicação das novas rotas da API. Com uma API anterior, a Home de produtos recebe 404; isso não valida o comportamento do código novo.

Não há histórico de preços, favoritos ou grading funcional nesta entrega. O fluxo visual e de toque no celular ainda precisa de verificação com o APK gerado. A compilação não substitui esse teste.
