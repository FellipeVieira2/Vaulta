# Guia de Publicação — Vaulta na Google Play Store

## Pré-requisitos
- [ ] Conta de desenvolvedor Google Play (https://play.google.com/console)
- [ ] Keystore de produção gerado e armazenado com segurança
- [ ] Variável `VAULTA_KEYSTORE_PASS` configurada no CI/CD (GitHub Secrets)
- [ ] Arquivo `vaulta-release.keystore` em base64 no GitHub Secrets (`VAULTA_KEYSTORE_BASE64`)

## Passo 1: Gerar AAB Assinado
O workflow `.github/workflows/mobile-build.yml` gera automaticamente o AAB assinado em cada push na branch `master`. O artefato `vaulta-aab` fica disponível por 30 dias.

Para gerar localmente:
```bash
dotnet publish src/Vaulta.App/Vaulta.App.csproj \
  -c Release \
  -f net10.0-android \
  -p:AndroidPackageFormat=aab \
  -p:AndroidKeyStore=true \
  -p:AndroidSigningKeyStore=vaulta-release.keystore \
  -p:AndroidSigningKeyAlias=vaulta \
  -p:AndroidSigningKeyPass=$VAULTA_KEYSTORE_PASS \
  -p:AndroidSigningStorePass=$VAULTA_KEYSTORE_PASS
```

## Passo 2: Criar App na Play Console
1. Acesse https://play.google.com/console
2. Clique em "Criar app"
3. Nome: **Vaulta - Sua Coleção TCG**
4. Idioma padrão: Português (Brasil)
5. Tipo: Aplicativo
6. Categoria: Estilo de vida / Colecionáveis

## Passo 3: Preencher Ficha da Loja
Use o conteúdo de `playstore-listing.txt`:
- **Título:** Vaulta - Sua Coleção TCG
- **Descrição curta:** Organize, acompanhe o valor e gerencie sua coleção de cartas TCG em um só lugar.
- **Descrição completa:** (copiar de playstore-listing.txt)
- **Ícone de alta resolução:** Exportar `appicon.svg` como PNG 512x512
- **Feature graphic:** Criar banner 1024x500 com identidade visual Vaulta
- **Screenshots:** Mínimo 2, recomendado 4-8 para phone (16:9 ou 9:16)
  - Usar mockups de `docs/design/screens/` como referência
  - Capturar telas reais do app em device/emulator API 36

## Passo 4: Classificação de Conteúdo (IARC)
1. Vá em "Classificação de conteúdo" na Play Console
2. Responda ao questionário IARC
3. Categoria esperada: **Livre** (sem conteúdo ofensivo, sem compras in-app obrigatórias)

## Passo 5: Segurança dos Dados (Data Safety)
Use `data-safety.md` como referência para preencher:
- **Coleta de dados:** Sim (nome, email, fotos, dados de coleção)
- **Compartilhamento:** Sim (provedores de hospedagem AWS, outros usuários no marketplace)
- **Práticas de segurança:** HTTPS, hash de senhas, SecureStorage
- **Exclusão:** Sim, via configurações do app

## Passo 6: Política de Privacidade
1. Hospede `privacy-policy.md` em URL pública (ex: https://vaultatcg.com.br/privacy ou GitHub Pages)
2. Insira a URL na Play Console em "Política de privacidade"
3. Obrigatório para apps que coletam dados pessoais

## Passo 7: Upload e Revisão
1. Faça upload do AAB assinado em "Produção" ou "Teste interno"
2. Revise todas as seções preenchidas
3. Envie para revisão (prazo: 1-7 dias úteis)

## Checklist Final
- [ ] AAB assinado gerado com sucesso
- [ ] Ficha da loja completa (título, descrição, ícone, screenshots)
- [ ] Classificação IARC obtida
- [ ] Data Safety preenchido
- [ ] Política de privacidade publicada e linkada
- [ ] Testado em device físico Android API 36
- [ ] Login/signup funcionais contra produção
- [ ] Permissão de câmera solicitada apenas no scanner
- [ ] Network security config bloqueia cleartext em produção