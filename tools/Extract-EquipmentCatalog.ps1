param(
    [string]$GameData = 'C:\Steam\steamapps\common\Tape to Tape\Tape to Tape_Data',
    [string]$Output = (Join-Path $PSScriptRoot '..\artifacts\equipment-catalog'),
    [string]$Python = "$env:USERPROFILE\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe",
    [string]$Node = "$env:USERPROFILE\.cache\codex-runtimes\codex-primary-runtime\dependencies\node\bin\node.exe"
)
$ErrorActionPreference = 'Stop'
& $Python (Join-Path $PSScriptRoot 'extract_equipment_catalog.py') --game-data $GameData --output $Output --node $Node
if ($LASTEXITCODE -ne 0) { throw "Equipment extraction failed (exit $LASTEXITCODE)." }
