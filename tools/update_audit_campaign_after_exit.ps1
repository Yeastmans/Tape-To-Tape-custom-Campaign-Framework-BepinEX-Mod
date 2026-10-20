param(
    [Parameter(Mandatory)][int]$GamePid,
    [Parameter(Mandatory)][string]$Source,
    [Parameter(Mandatory)][string]$Destination,
    [Parameter(Mandatory)][string]$StatusFile
)
$ErrorActionPreference = 'Stop'
try {
    $sourceRoot = [IO.Path]::GetFullPath($Source).TrimEnd('\')
    $destinationRoot = [IO.Path]::GetFullPath($Destination).TrimEnd('\')
    if (-not (Test-Path -LiteralPath (Join-Path $sourceRoot 'campaign.txt'))) { throw 'Audit source campaign is missing.' }
    if (-not (Test-Path -LiteralPath (Join-Path $destinationRoot 'campaign.txt'))) { throw 'Installed audit campaign is missing.' }
    while (Get-Process -Id $GamePid -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 2 }
    foreach ($file in Get-ChildItem -LiteralPath $sourceRoot -Recurse -File) {
        $relative = $file.FullName.Substring($sourceRoot.Length + 1)
        $target = Join-Path $destinationRoot $relative
        $parent = Split-Path -Parent $target
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    }
    Set-Content -LiteralPath $StatusFile -Value "UPDATED $(Get-Date -Format o)" -Encoding utf8
} catch {
    Set-Content -LiteralPath $StatusFile -Value "FAILED $(Get-Date -Format o) $($_.Exception.Message)" -Encoding utf8
}
