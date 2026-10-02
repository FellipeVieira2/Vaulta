# Scanner: leitura, enquadramento e soma — 02/10/2026

Base analisada: `efc0992` (`melhorias scanner`). Branch: `codex/scanner-result-and-detection-fixes`.

## Causas reproduzidas

- A estabilidade tinha sido reduzida a 550 ms, mas a tolerância caiu de 15 para 7. Pequenas oscilações alternadas de exposição reiniciavam a espera indefinidamente. O teste de captura a cada 250 ms reproduziu a regressão. Restaurados um segundo e tolerância 15; captura continua única, com bloqueio durante a identificação da foto já salva.
- O prompt v3 pedia detalhes desconhecidos com confiança baixa, mas o parser só aceitava `null` com confiança zero. Acabamento/condição/variante desconhecidos podiam descartar também nome e número legíveis. Os três casos foram reproduzidos. Campos opcionais nulos agora são normalizados para desconhecido; estruturas inválidas, campos extras e valores não permitidos continuam rejeitados.
- O guia podia ocupar 96% da largura, enquanto o detector exige margem nas quatro bordas, e era desenhado em coordenadas diferentes da câmera. Agora usa as mesmas coordenadas da prévia completa, com margem. Testes alinham uma carta ao guia em três proporções verticais e na horizontal. A revisão encontrou a área insuficiente na horizontal; o teste falhou antes do ajuste para 84% da altura nessa orientação.
- A soma matemática estava correta. O fluxo não adicionava quando havia várias variantes sem acabamento confirmado. A confirmação ainda dizia “Cotação indisponível” antes de uma seleção, mesmo com cotações existentes. Agora cada acabamento mostra seu próprio valor e um único botão confirma e soma. Acabamento confirmado pelo GPT continua automático; não se escolhe uma variante silenciosamente quando há conflito ou incerteza.

## Extração GPT v4

Continua `gpt-6-luna`, uma chamada por foto, `reasoning=none`, schema estrito, sem ferramentas, preço, identificadores internos ou consulta de certificados. Limite de saída 1280 tokens para evitar truncamento dos campos adicionais.

Campos: jogo, nome, número completo, código/nome visível do set, idioma, variante, HP/PS, acabamento, condição visual tentativa, presença de certificação, empresa, nota impressa, número do certificado, raridade, ano, tipo e estágio. Selo PSA/CGC/BGS é leitura visual; a resposta não declara autenticidade ou verificação. Nota profissional não é uma nota criada pelo modelo. Condição não legível e detalhes desconhecidos não impedem a leitura da identidade.

O app exibe e salva a leitura na ocorrência da sessão. Dados do selo são preservados também em notas da unidade importada, com indicação de leitura não verificada. Na sessão, cotações de cartas comuns não são usadas como preços de cartas certificadas. **Preço de certificação e verificação junto à certificadora ainda não têm integração.** A avaliação agregada da coleção existente ainda usa referências da impressão/variante e não calcula preço por certificadora/nota; não confundir essa referência com preço PSA.

O GPT pode ler Pokémon, Yu-Gi-Oh!, One Piece e outros jogos, independentemente da cobertura do catálogo. Uma imagem continua gerando uma extração; o resolver consulta o jogo lido. Outro TCG não passa pelo fallback OCR exclusivo de Pokémon. O app não força Pokémon no envio/busca. Descoberta externa automática e preços continuam implementados para Pokémon/TCGdex; outros jogos sem edição local retornam a leitura com valor pendente, sem inventar IDs.

## Referência visual

[PokéScope](https://pokescope.app/) foi usado como referência de organização: câmera predominante, enquadramento amplo, controles discretos e resultado com carta/valor. A Vaulta conserva marca, cores, total/custo da sessão, coleção, venda e vídeo da abertura. Cabeçalho `SCANNER TCG`, captura manual central, busca/finalização laterais e última carta com artwork, edição/acabamento e valor. Não é uma cópia visual integral do app da referência. A página pública foi inspecionada no navegador; ainda falta validar o layout/câmera no aparelho físico.

## Validação

- Backend: 230 testes unitários aprovados.
- App.Core: 169 testes aprovados, incluindo guia vertical/horizontal, estabilidade, soma por variante, idempotência e proteção de cotação para selo visível.
- Integração: quatro testes de scanner aprovados com HTTP/autenticação/PostgreSQL isolado, incluindo resposta v4 com selo e compatibilidade com v1.
- A revisão independente encontrou o problema de orientação horizontal, corrigido com teste RED/GREEN.

Builds e diagnóstico de produção são registrados após a validação final. Fotos físicas, reflexos, identificação de acabamento em condições reais e uma carta certificada real ainda precisam de teste no aparelho; fixtures de JSON não comprovam acurácia da visão nesses casos.
