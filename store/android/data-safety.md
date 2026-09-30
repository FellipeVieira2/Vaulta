# Data Safety — Vaulta (Play Store)

## Visão Geral
Este documento descreve os dados coletados, compartilhados e as práticas de segurança do app Vaulta, conforme exigido pela seção "Data Safety" da Google Play Store.

## Coleta de Dados

### Dados Pessoais
| Categoria | Dado | Obrigatório? | Finalidade |
|-----------|------|-------------|------------|
| Identificação | Nome de exibição | Sim | Perfil do usuário e marketplace |
| Identificação | Nome de usuário | Sim | Login e perfil público |
| Mensagens | E-mail | Sim | Autenticação e recuperação de conta |
| Senhas | Senha (hash) | Sim | Autenticação segura |

### Dados Financeiros
| Categoria | Dado | Obrigatório? | Finalidade |
|-----------|------|-------------|------------|
| Compras | Histórico de transações | Não | Carteira digital e saques |
| Compras | Endereço de entrega | Não | Checkout no marketplace |

### Fotos e Vídeos
| Categoria | Dado | Obrigatório? | Finalidade |
|-----------|------|-------------|------------|
| Fotos | Imagens capturadas/enviadas | Não | Scanner de cartas e fotos de anúncios |

### Localização
| Categoria | Dado | Obrigatório? | Finalidade |
|-----------|------|-------------|------------|
| Aproximada | Cidade/Estado (via CEP) | Não | Preenchimento automático de endereço |

### Atividade do App
| Categoria | Dado | Obrigatório? | Finalidade |
|-----------|------|-------------|------------|
| Interações | Telas visitadas | Não | Melhoria da experiência |
| Outros | Dados de coleção | Sim | Funcionalidade principal do app |

## Compartilhamento de Dados

| Tipo de Terceiro | Dados Compartilhados | Finalidade |
|-----------------|---------------------|------------|
| Provedores de Hospedagem (AWS) | Todos os dados acima | Infraestrutura e armazenamento |
| Processadores de Pagamento | Dados financeiros | Processamento de transações |
| Outros Usuários | Nome de exibição, fotos de anúncios | Marketplace |

**Não vendemos dados pessoais.** Nenhum dado é compartilhado para fins de publicidade ou marketing de terceiros.

## Práticas de Segurança
- ✅ Dados em trânsito criptografados (HTTPS/TLS)
- ✅ Dados sensíveis armazenados com hash criptográfico (senhas)
- ✅ Tokens de autenticação armazenados localmente via SecureStorage
- ✅ URLs de acesso a fotos são assinadas e temporárias
- ✅ Acesso ao backend autenticado via JWT
- ✅ Backups regulares com retenção limitada

## Exclusão de Dados
- Usuários podem solicitar exclusão da conta via configurações do app
- Dados pessoais são removidos em até 30 dias após solicitação
- Registros financeiros podem ser retidos conforme exigência legal
- Dados anonimizados podem ser mantidos para análise agregada

## Contato
Dúvidas sobre privacidade e dados: **privacidade@vaultatcg.com.br**