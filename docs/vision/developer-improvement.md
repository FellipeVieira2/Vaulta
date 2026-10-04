# Vision: memória de desenvolvimento e avaliação

## Ativação explícita

`DeveloperAutoPromote` é `false` por padrão. Configure uma API privada de desenvolvimento com a conta que fará as revisões:

```json
{
  "Vision": {
    "History": {
      "Enabled": true,
      "OperationalPolicyVersion": "ops-v1",
      "ImprovementPolicyVersion": "improve-v1",
      "RetentionDays": 7
    },
    "Improvement": {
      "DeveloperAutoPromote": true,
      "DeveloperAccountIds": ["SUBSTITUA-PELO-UUID-DA-CONTA"],
      "RefreshDebounceSeconds": 2
    }
  }
}
```

Use `ASPNETCORE_ENVIRONMENT=Development`. O startup recusa auto promoção em Production/Testing, sem histórico habilitado, sem allowlist ou com debounce fora de 1–5 segundos. Configure também os serviços existentes de banco, Assets, encoder e GPT. Não coloque chaves em arquivos versionados ou no APK. No app, ative **Histórico e melhoria → Contribuir com exemplos revisados**; configurar o servidor não substitui essa autorização individual.

Uma previsão automática, mesmo com 99% de confiança, não vira referência. Um usuário explicitamente confirma/corrige Printing/Variant. A promoção confere consentimento atual, retenção, execução concluída, embedding/manifest, frente de carta, identidade canônica e bytes privados finalizados correspondentes ao SHA realmente executado. O app aguarda o upload em segundo plano antes de registrar a escolha humana. Caso o feedback chegue primeiro, confirmar a captura reexecuta os mesmos checks.

Fotos distintas podem pertencer à mesma impressão. Bytes iguais no mesmo modelo não duplicam referências; rótulos conflitantes entre tentativas exigem verificação. Revisões preservam a previsão e o ranking originais e invalidam a referência anterior. Retirada/exclusão e expiração retiram os exemplos do índice. Os pesos não são treinados ou alterados.

O índice coalesce eventos em 2 segundos, depois faz uma troca de snapshot. Continua com verificação periódica de 30 segundos. O tempo efetivo inclui a leitura do snapshot; não existe uma garantia de reconhecimento instantâneo. As instâncias de API têm sinais locais; outra instância converge pela verificação periódica.

## Diagnóstico

```powershell
dotnet run --project src/Vaulta.Web.Api -- --vision-improvement-status
```

O comando informa encoder/modelo, linhas prontas por origem, índice carregado, capturas, revisões, pendências e flags. Contagens persistidas por origem abrangem modelos existentes; `indexReferences` é o snapshot ativo após filtros de consentimento/retenção/modelo. O endpoint autenticado `GET /api/v1/vision/improvement/status` exige uma conta habilitada no modo developer. Não existe endpoint público de promoção.

Em APK Debug, **Diagnóstico da leitura** mostra/copia JSON sanitizado: ranking original com scores, evidências permitidas, resolução e versões. Não exporta foto, URL, hash de imagem, conta ou número de certificado. Release não contém esse controle. Os diagnósticos não participam da decisão de identificação.

## Benchmark com dados do histórico

```powershell
dotnet run --project src/Vaulta.Web.Api -- --vision-export-manifest phone-v1 artifacts/vision-improvement/phone-v1.json
dotnet run --project src/Vaulta.Web.Api -- --vision-rerun-benchmark phone-v1 100 artifacts/vision-improvement/phone-results.json
```

O operador usa um ambiente de avaliação separado, com acesso autorizado aos Assets privados. A execução CLI termina antes de iniciar o servidor. Não rode comparações numa API que está atendendo usuários.

O manifest congela IDs, SHAs, rótulos e revisões humanas. Capturas excluídas/expiradas invalidam o manifest. O índice exclui os Assets, SHAs idênticos e todas as sessões relacionadas transitivamente. Compara **somente oficial** e **oficial + verificado**, reutilizando embeddings e a mesma leitura de evidência entre os dois índices. Há uma chamada de evidência por imagem distinta, não duas; isso isola o efeito do índice, e não compara variações de respostas do GPT.

O relatório contém Top-1/5/10 de impressões distintas, MRR, acerto final de impressão/variante, estados, legibilidade de nome/número/set/idioma e diagnóstico por amostra. Atribuição a retrieval/evidência/resolver é heurística, não prova causal. Não reserva novas tentativas/runs operacionais nem consulta provedor de preço. Para novas capturas guarda recorte e frame completo; para registros antigos sem frame completo informa `legacyCropOnlySamples`, limitação relevante para o GPT.

## Comparação offline de encoder/preprocessing

```powershell
dotnet run --project src/Vaulta.Web.Api -- --vision-compare-encoders dataset-local.json report-local.json clip/manifest.json dinov2/manifest.json
```

Esse comando funciona sem iniciar módulos da API. Não consulta catálogo, preços, banco ou rede. Lê somente arquivos de um dataset congelado autorizado. Use o schema de [offline-dataset.example.json](offline-dataset.example.json); o exemplo vazio deve gerar `insufficient_real_world_dataset`, sem métricas de acurácia.

Referências são artworks próprios já disponíveis ou fotos humanas verificadas com consentimento. Queries precisam ser fotos reais confirmadas/corrigidas, `kind=verified-phone`, consentimento e `splitGroup` da sessão. Use grupos distintos por artwork/identidade independente; não marque toda a biblioteca oficial como uma única sessão. Imagens relacionadas por SHA/grupo ficam fora do índice, inclusive ligações transitivas. Cada arquivo tem SHA validado e fica dentro da pasta do dataset. Exemplos sintéticos não devem ser rotulados como fotos reais.

As três estratégias usam ImageSharp bicúbico e têm identidades separadas:

| Estratégia | CLIP | DINOv2 |
|---|---|---|
| center_crop | shortest edge 224 → crop 224 | shortest edge 256 → crop 224 |
| letterbox | preservar carta, padding cinza 127, canvas 224 | igual, normalização ImageNet |
| direct_resize | deformar para 224 × 224 | igual, normalização ImageNet |

CLIP: revisão `d15189d7028b43f1d3e65039190477f6af591c2a`, 512D, pooling pooled, SHA `583fd1110a514667812fee7d684952aaf82a99b959760c8d7dca7e0ab9839299`.

DINOv2 Small ONNX: revisão `c2bb04a51fab207c420665f1946016107bffc701`, 384D, pooling CLS, SHA `3afdc8bc63b50558d6e5770f5b799bb82455c2311183a2de43803f343a29d917`. A revisão/SHA foram conferidos no repositório do [export ONNX](https://huggingface.co/Xenova/dinov2-small); o [modelo original](https://huggingface.co/facebook/dinov2-small) informa Apache 2.0. Essa comparação não adiciona DINOv2 ao aplicativo ou à configuração pública.

Os manifestos temporários são apagados ao final; o manifest instalado não muda. Produção aceita exclusivamente os pesos, revisão e normalização CLIP atuais. Experimentos exigem opt-in explícito no runner e não entram em índices de outra identidade. O relatório registra tempo de carga, p50/p95 de embedding+retrieval, dimensão, tamanho dos pesos, memória gerenciada/RSS/pico, hardware não testado e limites de medição. Memória é do processo, inclui host e experimentos anteriores; comparar memória fina exige processos separados. O runner de encoder não executa GPT/resolver, portanto suas métricas de impressão/variante final são `null`.

## Coleta inicial

Comece com **20 impressões × 5 fotos independentes**: luz normal, iluminação fraca, reflexo moderado, sleeve e inclinação. Inclua separadamente negativas/versos/oclusão para avaliar presença e orientação. Confirme impressão, idioma, variante e condição visual do cenário; não aceite o palpite automático como rótulo. Separe sessões de referência e avaliação, mantendo consentimento/retenção. Só depois do baseline medido escolher encoder/preprocessing ou experimentar SigLIP. Artworks transformados e screenshots não comprovam qualidade com câmera.
