param(
    [string]$ManifestPath = 'artifacts/scanner-eval/manifest.json',
    [Uri]$BaseUrl = 'http://localhost:8080/',
    [switch]$AllowPaidCalls
)
$ErrorActionPreference = 'Stop'
if (!$AllowPaidCalls) { throw 'Este eval pode consumir tokens. Execute explicitamente com -AllowPaidCalls em um ambiente autorizado.' }
if ([string]::IsNullOrWhiteSpace($env:VAULTA_SCANNER_EVAL_TOKEN)) { throw 'Defina VAULTA_SCANNER_EVAL_TOKEN com um token de uma conta de teste.' }
if (!$BaseUrl.IsAbsoluteUri -or $BaseUrl.Scheme -notin @('http', 'https')) { throw 'Informe uma URL válida para a API de teste.' }
$manifestFile = (Resolve-Path -LiteralPath $ManifestPath).Path
$datasetRoot = [IO.Path]::GetDirectoryName($manifestFile)
$manifest = Get-Content -LiteralPath $manifestFile -Raw | ConvertFrom-Json
if ($manifest.schemaVersion -ne 1 -or !$manifest.cases) { throw 'Manifesto de dataset inválido.' }

# Validate every path before making any potentially paid request.
$cases = foreach ($case in $manifest.cases) {
    $filePath = [IO.Path]::GetFullPath((Join-Path $datasetRoot $case.imageFile))
    if (!$filePath.StartsWith($datasetRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'A imagem deve estar dentro da pasta do dataset.' }
    if (!(Test-Path -LiteralPath $filePath -PathType Leaf)) { throw 'Uma imagem indicada no manifesto não existe.' }
    if ($case.expectedPrintingId -and ![Guid]::TryParse($case.expectedPrintingId, [ref]([Guid]::Empty))) { throw 'PrintingId de referência inválido.' }
    if (!$case.id -or !$case.authorized) { throw 'Cada caso precisa de id e autorização explícita da imagem.' }
    [pscustomobject]@{ Definition = $case; Path = $filePath }
}
Add-Type -AssemblyName System.Net.Http
$client = [Net.Http.HttpClient]::new()
$client.Timeout = [TimeSpan]::FromSeconds(90)
$client.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $env:VAULTA_SCANNER_EVAL_TOKEN)
try {
    $results = foreach ($entry in $cases) {
        $case = $entry.Definition
        $content = [Net.Http.MultipartFormDataContent]::new()
        try {
            $bytes = [IO.File]::ReadAllBytes($entry.Path)
            if ($bytes.Length -gt 15 * 1024 * 1024) { throw 'Imagem maior que o limite do scanner.' }
            $image = [Net.Http.ByteArrayContent]::new($bytes)
            $extension = [IO.Path]::GetExtension($entry.Path).ToLowerInvariant()
            $mime = switch ($extension) { '.jpg' { 'image/jpeg' } '.jpeg' { 'image/jpeg' } '.png' { 'image/png' } '.webp' { 'image/webp' } default { throw 'Use JPEG, PNG ou WebP.' } }
            $image.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::new($mime)
            $content.Add($image, 'image', 'card' + $extension)
            $uri = [Uri]::new($BaseUrl, 'api/v1/scanner/identify?gameCode=' + [Uri]::EscapeDataString($case.gameCode))
            $timer = [Diagnostics.Stopwatch]::StartNew()
            $response = $client.PostAsync($uri, $content).GetAwaiter().GetResult()
            try {
                if (!$response.IsSuccessStatusCode) { throw "Eval interrompido: HTTP $([int]$response.StatusCode)." }
                $result = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult() | ConvertFrom-Json
            } finally { $response.Dispose() }
            $timer.Stop()
            $candidates = @($result.candidates | Sort-Object confidenceScore -Descending)
            $best = $candidates | Select-Object -First 1
            $automatic = $best -and $best.hasCollectorNumberMatch -and $best.confidenceScore -ge .93 -and $best.confidenceScore -le 1 -and
                ($candidates.Count -eq 1 -or $best.confidenceScore - $candidates[1].confidenceScore -ge .12)
            [pscustomobject]@{ id = $case.id; tags = $case.tags; milliseconds = $timer.ElapsedMilliseconds;
                noMatch = $candidates.Count -eq 0; ambiguous = $candidates.Count -gt 1; automatic = [bool]$automatic;
                expectedPrintingId = $case.expectedPrintingId; selectedPrintingId = $best.printingId;
                correctPrinting = $best -and $case.expectedPrintingId -eq $best.printingId;
                falsePositive = $automatic -and $case.expectedPrintingId -ne $best.printingId }
        } finally { $content.Dispose() }
    }
    # Results are local and ignored, contain no image bytes or auth credentials.
    $report = [ordered]@{ schemaVersion = 1; executedAt = [DateTimeOffset]::UtcNow; cases = @($results);
        total = @($results).Count; correctPrinting = @($results | Where-Object correctPrinting).Count;
        falsePositive = @($results | Where-Object falsePositive).Count; ambiguous = @($results | Where-Object ambiguous).Count;
        noMatch = @($results | Where-Object noMatch).Count }
    $resultPath = Join-Path $datasetRoot 'results.json'
    $report | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $resultPath -Encoding utf8
    Write-Output "Eval concluído: $($report.total) casos; $($report.correctPrinting) corretos; $($report.falsePositive) falsos positivos. Resultado local: $resultPath"
} finally { $client.Dispose() }
