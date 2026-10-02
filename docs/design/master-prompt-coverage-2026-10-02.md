# Vaulta — cobertura do prompt mestre

Estado de implementação local em 02/10/2026. Este registro não confirma funcionalidades em produção nem substitui validação visual em aparelho.

| Área | Evidência atual | Trabalho restante |
| --- | --- | --- |
| Home, busca e resultados | API com busca por carta/set/número, filtros e metadados; Home nativa com estados e paginação | Validar layout, acessibilidade e navegação no aparelho |
| Detalhe e comparação de sellers | Controller testado e telas nativas em integração; comparação por printing/variante; fotos reais separadas da referência de catálogo | Compilação integrada e validação visual; nome público do seller limitado pelo contrato atual |
| Scanner idle/detecting/identified/ambiguous | Sessão existente evoluída com resultado compacto e ações por ocorrência; fluxo seguro mantém escolha em ambiguidade | Dataset de fotos reais, precisão por iluminação/ângulo, validação contínua no aparelho e segundo passe automático de casos ambíguos |
| Scanner → vender, criar/revisar anúncio | Importação idempotente por ScanId; rascunho privado, condição/preço manuais, fotos reais frente/verso, revisão e publicação recuperável | QA no aparelho e avaliação de upload em rede instável |
| Reconhecimento Nova | Extração validada, matching canônico, limites de chamada, fallback OCR e telemetria | Ativar somente após verificar acesso/modelo AWS e medir precisão/custo com fotos reais; desativado por padrão |
| Perfil seller | Listagem nativa dos anúncios do seller e reputação disponível | Contrato de apresentação pública, bio/logo e estado profissional completo |
| Checkout e frete | Checkout existente e política aprovada: comprador paga, cotar/cobrar antes do pagamento | Provedor, origem/pacote, expiração da cotação, total persistido, etiqueta, repasse e testes; Correios apenas em consideração |
| Pedido e minhas vendas | Fluxos existentes de pedidos, recebimento, repasse e remessa | Redesign nativo dedicado e alinhamento integral com frete cotado |
| Coleção e detalhe da unidade | Telas/contratos existentes; scanner pode adicionar ocorrência com identidade própria | Redesign de densidade, venda rápida de unidade existente e avaliação visual |
| Portfolio e Price History | Valoração existente; provedor indisponível retorna vazio; histórico fabricado retirado da configuração de produção | Fonte real de histórico no backend, proveniência, intervalos e testes de dados reais |
| Offers, Favorites, Notifications, Store | Sem evidência de implementação completa nesta execução | Implementar contratos, persistência, autorização e telas próprias; não declarar como concluídos |
| My Videos | Gravação opcional e armazenamento existentes preservados | Validar no aparelho, estados de armazenamento e gerenciamento de vídeos |
| Profile e Settings | Fluxos existentes de conta, preferências e logout | Redesign dedicado e verificação dos controles oferecidos |
| Login | Redesign adicional solicitado pelo usuário; referência Figma e integração nativa em andamento | Verificar formulário, retomada de sessão, navegação, teclado e erros; recuperação e social dependem de endpoints reais |
| Observabilidade, segurança e custos | Gates de ownership, assets, idempotência e concorrência testados; Nova com limites e métricas | Infraestrutura/alertas reais, orçamento e operação AWS; nenhum deploy realizado |

## Validação

Registrar os resultados finais no relatório do incremento. Testes automáticos não confirmam fidelidade visual ou reconhecimento de cartas fotografadas no mundo real. O ambiente atual não iniciou o emulador por falta de espaço em disco; a compilação Android anterior foi aprovada, e a nova compilação integrada ainda será executada.

## Decisões humanas

- Continuar a implementação do prompt sem pedir nova aprovação de cada etapa.
- Cotar e cobrar frete no checkout, pago pelo comprador.
- Provedor ainda indefinido; Correios está em consideração. Credenciais devem ser configuradas no backend, sem compartilhamento no chat.
- Refazer login usando os plugins e a identidade visual existente da Vaulta.
