param()
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$target = Join-Path $root '.env'
if (Test-Path -LiteralPath $target) { Write-Host '.env already exists; no changes made.'; exit 0 }
function New-RandomSecret {
    $bytes = New-Object byte[] 48
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes); return [Convert]::ToBase64String($bytes) } finally { $rng.Dispose() }
}
$content = Get-Content -LiteralPath (Join-Path $root '.env.example') -Raw
$content = $content.Replace('POSTGRES_PASSWORD=', ('POSTGRES_PASSWORD=' + (New-RandomSecret)))
$content = $content.Replace('JWT_SECRET=', ('JWT_SECRET=' + (New-RandomSecret)))
$content = $content.Replace('MINIO_ROOT_PASSWORD=', ('MINIO_ROOT_PASSWORD=' + (New-RandomSecret)))
[IO.File]::WriteAllText($target, $content, (New-Object System.Text.UTF8Encoding($false)))
Write-Host '.env created with random local credentials. Do not commit this file.'
