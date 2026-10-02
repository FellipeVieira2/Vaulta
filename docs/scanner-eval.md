# Dataset local autorizado do scanner

Use `artifacts/scanner-eval/manifest.json` e imagens na mesma pasta. `scanner-eval/` é ignorada por Git/Docker também fora de artifacts. Não inclua fotos de usuários, fotos sem licença/autorização ou credenciais no repositório.

Formato do manifesto (exemplo de formato, sem dados reais):

```json
{
  "schemaVersion": 1,
  "cases": [
    {
      "id": "pt-normal-001",
      "authorized": true,
      "imageFile": "images/pt-normal-001.jpg",
      "gameCode": "pokemon",
      "expectedPrintingId": "GUID real do catálogo de teste",
      "expectedCollectorNumber": "026/086",
      "expectedLanguage": "pt-BR",
      "tags": ["normal", "pt-BR", "small-footer"]
    }
  ]
}
```

Referências canônicas precisam vir do catálogo daquele ambiente, conferidas por humano. Para negativos, `expectedPrintingId=null`. Inclua carta normal/holo/reverse, sleeve/reflexo, pouca luz, blur, perspectiva, pt/en, nomes repetidos, promos, números pequenos e artworks semelhantes. Confirme disponibilidade das impressões e idiomas no banco antes da avaliação.

Execução opcional **pode consumir tokens**. Não é chamada por CI. Defina `VAULTA_SCANNER_EVAL_TOKEN` no ambiente com token de uma conta de teste, configure OpenAI somente no servidor e execute:

```powershell
./scripts/Invoke-ScannerEval.ps1 -ManifestPath artifacts/scanner-eval/manifest.json -BaseUrl http://localhost:8080/ -AllowPaidCalls
```

O runner envia uma imagem por caso, não inclui/adiciona unidades à coleção nem consulta preços. Relatório local `results.json`: melhor candidato, acerto por Printing, inclusão automática elegível pelos mesmos limiares do app, falso positivo automático, múltiplos candidatos, no-match e latência HTTP completa. Agrupe os resultados pelos tags para comparar idiomas/condições. `ambiguous` neste relatório significa múltiplos candidatos da API, não o status interno do matcher. `correctPrinting` avalia o melhor candidato e não significa que o app o aceitou automaticamente.

Não mede leitura literal do collector number/idioma do GPT: os campos públicos retornam o catálogo. Essas duas métricas exigem um harness interno autorizado que compare `CardEvidence` à referência, sem expor/logar evidência de usuários em produção. Tokens/tentativas/duração do provider vêm de `Vaulta.Scanner`; custo monetário depende da tabela oficial do modelo no momento do eval. Refinamento e estabilidade precisam de ensaios no dispositivo com sequência de frames; o runner testa somente o endpoint de uma captura.

Repita o mesmo dataset com configuração OpenAI, Nova ou OCR em runs separados, nunca acionando dois modelos para cada imagem em produção. Registre configuração/modelo/prompt/detail e revisão do catálogo junto aos resultados locais. Não foi executado eval pago nem medido ganho de acurácia/custo nesta implementação.
