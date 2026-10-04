# Scanner: reveal do valor

Figma atualizado no arquivo existente, seção [Scanner / Value reveal — 2026-10-04](https://www.figma.com/design/Ey1vEJNXx1oaSZbODMamWY/Vaulta--Marketplace-Product-Design?node-id=50-166).

| Estado | Frame |
|---|---|
| Identifying | [50:167](https://www.figma.com/design/Ey1vEJNXx1oaSZbODMamWY/Vaulta--Marketplace-Product-Design?node-id=50-167) |
| CardIdentified | [50:197](https://www.figma.com/design/Ey1vEJNXx1oaSZbODMamWY/Vaulta--Marketplace-Product-Design?node-id=50-197) |
| ValueReveal | [50:227](https://www.figma.com/design/Ey1vEJNXx1oaSZbODMamWY/Vaulta--Marketplace-Product-Design?node-id=50-227) |
| ValueRevealed | [50:257](https://www.figma.com/design/Ey1vEJNXx1oaSZbODMamWY/Vaulta--Marketplace-Product-Design?node-id=50-257) |

Frames 390 × 844, texto editável, quatro instâncias por frame; componentes ScannerResult/Compact 12:91, Price/MarketReference 12:103 e botão secundário 3:27. Reutiliza artwork do arquivo e variáveis de cor/espaçamento. Sem gravação ou tela de resultado persistente. A figura mostra valores e fonte explicitamente ilustrativos. No app, fonte/data/condição vêm da cotação real.

Descoberta: sem Code Connect para esses componentes; telas/instâncias locais inspecionadas, componentes e variáveis compatíveis encontrados, nenhuma busca adicional necessária. Bibliotecas disponíveis consultadas antes de criar os estados. Estilo local `Scanner / Reveal value` Inter Bold 38/46 criado para o tamanho utilizado no app. Validação estrutural: quatro estados, conexões automáticas e nenhuma raiz fora dos limites. Screenshot inspecionado; corrigido posicionamento de overlay para preço/fonte não serem cortados.

Protótipo: Identifying → CardIdentified → ValueReveal → ValueRevealed, Smart Animate; clique no estado final para repetir. O protótipo ilustra a sequência/posição/transição dos estados; os valores intermediários são texto. Count-up numérico a 25fps, price-pop, cancelamento e acessibilidade são implementados no MAUI.

Motion do código: carta 220ms (fade/up8px/scale .97→1), valor 460ms, pop 180ms (1→1.05→1), total 220ms e saída 120ms: **1.200ms**. Movimento reduzido mostra os valores finais imediatamente e usa apenas fade, sem count-up/pop/deslocamento. Haptic leve quando suportado; sem áudio estridente ou confete.

Preço disponível é diferente de ausência de preço. Variante pendente/certificação indisponível não usa preço raw nem zero falso. Revelar é apresentação: persiste uma ocorrência antes de começar, não soma novamente, bloqueia novas capturas durante a sequência e cancela ao sair/trocar conta/sessão.
