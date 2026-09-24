param([string]$BaseUrl = 'http://localhost:8080')
$ErrorActionPreference = 'Stop'
function Send-Request([string]$Method, [string]$Path, $Body = $null, [hashtable]$Headers = @{}) {
    $parameters = @{ Uri = "$BaseUrl$Path"; Method = $Method; Headers = $Headers; UseBasicParsing = $true }
    if ($null -ne $Body) { $parameters.ContentType = 'application/json'; $parameters.Body = ConvertTo-Json $Body -Depth 5 }
    $response = Invoke-WebRequest @parameters
    Write-Host "$Method $Path => $($response.StatusCode)"
    return $response
}
$null = Send-Request GET '/health'
$null = Send-Request GET '/health/ready'
$null = Send-Request GET '/swagger/v1/swagger.json'
$suffix = [Guid]::NewGuid().ToString('N').Substring(0, 16)
$email = "smoke-$suffix@example.com"
$username = "smoke_$suffix"
$password = 'Smoke-' + [Guid]::NewGuid().ToString('N') + '-A1!'
$nextPassword = 'New-' + [Guid]::NewGuid().ToString('N') + '-B2!'
$null = Send-Request POST '/api/v1/auth/register' @{ email = $email; password = $password; username = $username; displayName = 'Smoke Collector' }
$login = (Send-Request POST '/api/v1/auth/login' @{ email = $email; password = $password }).Content | ConvertFrom-Json
$headers = @{ Authorization = "Bearer $($login.accessToken)" }
$me = Send-Request GET '/api/v1/me' -Headers $headers
$headers['If-Match'] = $me.Headers['ETag']
$profile = Send-Request PATCH '/api/v1/me/profile' @{ bio = 'Smoke verified'; countryCode = 'BR'; state = 'SP'; city = 'Araras' } $headers
$headers['If-Match'] = $profile.Headers['ETag']
$null = Send-Request PATCH '/api/v1/me/preferences' @{ preferredCurrency = 'BRL'; language = 'pt-BR'; timeZone = 'America/Sao_Paulo'; tcgInterests = @('POKEMON', 'MAGIC') } $headers
$public = (Send-Request GET "/api/v1/users/$username").Content | ConvertFrom-Json
if ($public.PSObject.Properties.Name -contains 'email' -or $public.PSObject.Properties.Name -contains 'passwordHash') { throw 'Public profile leaked private fields.' }
$null = Send-Request POST '/api/v1/me/change-password' @{ currentPassword = $password; newPassword = $nextPassword } $headers
$login = (Send-Request POST '/api/v1/auth/login' @{ email = $email; password = $nextPassword }).Content | ConvertFrom-Json
$rotated = (Send-Request POST '/api/v1/auth/refresh' @{ refreshToken = $login.refreshToken }).Content | ConvertFrom-Json
$headers = @{ Authorization = "Bearer $($rotated.accessToken)" }
$null = Send-Request POST '/api/v1/auth/logout' @{ refreshToken = $rotated.refreshToken } $headers
Write-Host "PASS: complete Identity flow. Persisted test user: $username"
