param([string]$BuildFolder = 'artifacts\CampaignStudio-appearance-translation', [string]$StatusName = 'editor-install-status.txt')
$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$statusFile = Join-Path (Join-Path $workspace 'artifacts\appearance-audit') $StatusName
try {
    if (Get-Process -Name 'T2T Campaign Studio' -ErrorAction SilentlyContinue) { throw 'Studio is open. Close it before applying this update.' }
    $targetRoot = 'C:\Program Files\T2T Campaign Studio'
    $sourceRoot = Join-Path $workspace $BuildFolder
    $names = @('T2T Campaign Studio.exe','T2T Campaign Studio.dll','T2T Campaign Studio.deps.json','T2T Campaign Studio.runtimeconfig.json','CampaignCore.dll')
    foreach ($name in $names) {
        $sourceFile = Join-Path $sourceRoot $name
        if (-not (Test-Path -LiteralPath $sourceFile -PathType Leaf)) { throw "Missing build: $sourceFile" }
    }
    foreach ($name in $names) {
        $sourceFile = Join-Path $sourceRoot $name
        $targetFile = Join-Path $targetRoot $name
        Copy-Item -LiteralPath $sourceFile -Destination $targetFile -Force
        if ((Get-FileHash -LiteralPath $sourceFile).Hash -ne (Get-FileHash -LiteralPath $targetFile).Hash) { throw "Hash verification failed: $name" }
    }
    [IO.File]::WriteAllText($statusFile, "INSTALLED: five application files copied and hash-verified. User content unchanged.")
} catch {
    [IO.File]::WriteAllText($statusFile, "FAILED: " + $_.Exception.Message)
    exit 1
}
