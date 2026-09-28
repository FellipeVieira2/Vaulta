$ErrorActionPreference = 'SilentlyContinue'
$candidates = @(
    "$env:ProgramFiles\Amazon\AWSCLIV2\aws.exe",
    "${env:ProgramFiles(x86)}\Amazon\AWSCLIV2\aws.exe",
    "$env:LOCALAPPDATA\Programs\AWSCLIV2\aws.exe",
    "$env:USERPROFILE\AppData\Local\Programs\AWSCLIV2\aws.exe",
    "$env:USERPROFILE\scoop\shims\aws.exe"
)
foreach ($c in $candidates) {
    if (Test-Path $c) { Write-Output "FOUND:$c"; break }
}
# Fallback: where.exe via PowerShell
if (-not $found) {
    $w = where.exe aws 2>$null
    if ($w) { Write-Output "FOUND:$($w | Select-Object -First 1)" }
}