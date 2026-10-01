param([string]$BaseUrl = 'http://localhost:8080', [string]$CardName = 'Pikachu', [string]$CollectorNumber = '58')
$ErrorActionPreference = 'Stop'
function Assert-Scanner([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
}
$suffix = [Guid]::NewGuid().ToString('N').Substring(0, 12)
$email = "scanner-$suffix@example.test"
$password = "Scanner!$([Guid]::NewGuid().ToString('N'))"
$account = @{ email = $email; password = $password; username = "scan$suffix"; displayName = 'Scanner validation' }
Invoke-RestMethod "$BaseUrl/api/v1/auth/register" -Method Post -ContentType 'application/json' -Body ($account | ConvertTo-Json) | Out-Null
$auth = Invoke-RestMethod "$BaseUrl/api/v1/auth/login" -Method Post -ContentType 'application/json' -Body (@{email=$email;password=$password} | ConvertTo-Json)
$headers = @{ Authorization = "Bearer $($auth.accessToken)" }
$imagePath = Join-Path ([IO.Path]::GetTempPath()) "vaulta-scanner-$suffix.webp"
try {
    Invoke-WebRequest "https://assets.tcgdex.net/en/base/base1/$CollectorNumber/high.webp" -OutFile $imagePath
    Add-Type -AssemblyName System.Net.Http
    $client = [Net.Http.HttpClient]::new()
    $client.DefaultRequestHeaders.Authorization = [Net.Http.Headers.AuthenticationHeaderValue]::new('Bearer', $auth.accessToken)
    $anonymous = [Net.Http.HttpClient]::new()
    try {
        $denied = $anonymous.GetAsync("$BaseUrl/api/v1/scanner/printings/$([Guid]::NewGuid())").GetAwaiter().GetResult()
        Assert-Scanner ([int]$denied.StatusCode -eq 401) 'Scanner must require authentication'
        $denied.Dispose()
    } finally { $anonymous.Dispose() }
    $invalidForm = [Net.Http.MultipartFormDataContent]::new()
    try {
        $invalidImage = [Net.Http.ByteArrayContent]::new([byte[]](1,2,3,4))
        $invalidImage.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::new('image/png')
        $invalidForm.Add($invalidImage, 'image', 'invalid.png')
        $invalidResponse = $client.PostAsync("$BaseUrl/api/v1/scanner/identify?gameCode=pokemon", $invalidForm).GetAwaiter().GetResult()
        Assert-Scanner ([int]$invalidResponse.StatusCode -eq 400) 'Invalid image must return a useful client error'
        $invalidResponse.Dispose()
    } finally { $invalidForm.Dispose() }
    $form = [Net.Http.MultipartFormDataContent]::new()
    $image = [Net.Http.ByteArrayContent]::new([IO.File]::ReadAllBytes($imagePath))
    $image.Headers.ContentType = [Net.Http.Headers.MediaTypeHeaderValue]::new('image/webp')
    $form.Add($image, 'image', 'card.webp')
    $response = $client.PostAsync("$BaseUrl/api/v1/scanner/identify?gameCode=pokemon", $form).GetAwaiter().GetResult()
    $body = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    Assert-Scanner $response.IsSuccessStatusCode "Identification failed ($([int]$response.StatusCode)): $body"
    $scan = $body | ConvertFrom-Json
    $candidate = $scan.candidates | Where-Object { $_.name -eq $CardName -and $_.collectorNumber -eq $CollectorNumber } | Select-Object -First 1
    Assert-Scanner ($null -ne $candidate) "$CardName $CollectorNumber was not recognized: $body"
    $details = Invoke-RestMethod "$BaseUrl/api/v1/scanner/printings/$($candidate.printingId)" -Headers $headers
    $manual = Invoke-RestMethod "$BaseUrl/api/v1/scanner/search?query=$([Uri]::EscapeDataString($CardName))&gameCode=pokemon" -Headers $headers
    Assert-Scanner ($null -ne ($manual.candidates | Where-Object { $_.printingId -eq $candidate.printingId })) 'Manual search must resolve to the same collectible printing'
    Assert-Scanner (@($manual.candidates | Where-Object { $_.estimatedMarketValueBrl -ne $null -and $_.currency -ne 'BRL' }).Count -eq 0) 'Manual search must not expose a foreign amount as BRL'
    Assert-Scanner ($details.printing.artworkUrl -like 'https://assets.tcgdex.net/*') 'Missing provider artwork'
    Assert-Scanner (@($details.information.PSObject.Properties).Count -gt 0) 'Missing card information'
    Assert-Scanner ($details.marketQuotes.Count -gt 0) 'Missing BRL quote'
    $quote = $details.marketQuotes[0]
    Assert-Scanner ($quote.marketValueBrl -gt 0 -and $quote.exchangeRate -gt 0) 'Invalid converted quote'
    $addHeaders = @{Authorization=$headers.Authorization; 'Idempotency-Key'=[Guid]::NewGuid().ToString('N')}
    $request = @{printingId=$candidate.printingId;variantId=$quote.variantId;quantity=1;condition='NM';acquisitionPrice=$null;acquisitionDate=$null;notes='Scanner smoke test'} | ConvertTo-Json
    $first = Invoke-RestMethod "$BaseUrl/api/v1/me/collection/items" -Headers $addHeaders -Method Post -ContentType 'application/json' -Body $request
    $retry = Invoke-RestMethod "$BaseUrl/api/v1/me/collection/items" -Headers $addHeaders -Method Post -ContentType 'application/json' -Body $request
    Assert-Scanner ($first.createdItems[0].id -eq $retry.createdItems[0].id) 'Retry duplicated collection item'
    $collection = Invoke-RestMethod "$BaseUrl/api/v1/me/collection" -Headers $headers
    Assert-Scanner ($collection.items[0].quantity -eq 1) 'Collection quantity mismatch'
    [pscustomobject]@{ Card=$candidate.name; Number=$candidate.collectorNumber; Confidence=$candidate.confidenceScore; Artwork=$details.printing.artworkUrl; MarketBrl=$quote.marketValueBrl; Source=$quote.source; Comparisons=$quote.comparisons.Count; CollectionQuantity=$collection.items[0].quantity; Idempotency='Passed' }
} finally {
    if ($form) { $form.Dispose() }
    if ($client) { $client.Dispose() }
    Remove-Item -LiteralPath $imagePath -ErrorAction SilentlyContinue
}
