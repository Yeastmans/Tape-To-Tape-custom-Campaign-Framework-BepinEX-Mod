param(
    [Parameter(Mandatory)][int]$GamePid,
    [Parameter(Mandatory)][string]$Source,
    [Parameter(Mandatory)][string]$Destination,
    [Parameter(Mandatory)][string]$StatusFile
)
$ErrorActionPreference = 'Stop'
try {
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) { throw 'Prepared DLL is missing.' }
    while (Get-Process -Id $GamePid -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 2 }
    $installed = $false
    for ($attempt = 0; $attempt -lt 60 -and -not $installed; $attempt++) {
        try {
            Copy-Item -LiteralPath $Source -Destination $Destination -Force -ErrorAction Stop
            $installed = (Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash -eq (Get-FileHash -LiteralPath $Destination -Algorithm SHA256).Hash
        } catch { Start-Sleep -Seconds 2 }
    }
    if (-not $installed) { throw 'The game DLL stayed locked after 60 attempts.' }
    Set-Content -LiteralPath $StatusFile -Value "INSTALLED $(Get-Date -Format o)" -Encoding utf8
} catch {
    Set-Content -LiteralPath $StatusFile -Value "FAILED $(Get-Date -Format o) $($_.Exception.Message)" -Encoding utf8
}
