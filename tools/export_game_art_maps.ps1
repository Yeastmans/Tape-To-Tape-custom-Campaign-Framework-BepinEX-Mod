# Regenerate the geometry/name tables shipped by GameArt after a game update.
# The Spine runtime under artifacts/spine-runtime is development-only; no pixels or runtime are packaged.
param(
    [string]$GameFolder = 'C:\Steam\steamapps\common\Tape to Tape',
    [string]$OutputFolder = '',
    [string]$NodeExe = 'C:\Users\kiran\AppData\Local\Programs\nodejs\node.exe'
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputFolder) { $OutputFolder = Join-Path $projectRoot 'assets/game-art-maps' }
$OutputFolder = [IO.Path]::GetFullPath($OutputFolder)
$dumpOutput = Join-Path $projectRoot 'artifacts/game-art-map-sources'
$sources = Join-Path $dumpOutput 'spine-sources'
New-Item -ItemType Directory -Path $OutputFolder -Force | Out-Null
Push-Location $projectRoot
try {
    & 'C:\Program Files\dotnet\dotnet.exe' run --project tests/GameArtChecks -c Release -p:NuGetAudit=false -- $GameFolder $dumpOutput --spine-sources
    if ($LASTEXITCODE) { throw 'Spine source extraction failed' }
    & $NodeExe tools/export_head_poses.mjs $sources (Join-Path $OutputFolder 'head-poses.json')
    if ($LASTEXITCODE) { throw 'Head pose map export failed' }
    & $NodeExe tools/export_flat_equipment.mjs $sources (Join-Path $OutputFolder 'flat-regions.json')
    if ($LASTEXITCODE) { throw 'Equipment map export failed' }
    $index = Get-Content -LiteralPath (Join-Path $sources 'index.json') -Raw | ConvertFrom-Json
    $nodeMap = Get-Content -LiteralPath (Join-Path $projectRoot 'assets/game-art-maps/node-meshes.json') -Raw | ConvertFrom-Json
    $requiredRegions = @($nodeMap.meshes.region | Sort-Object -Unique)
    $atlas = @($index.assets | Where-Object {
        if ($_.name -ne 'skeleton.atlas') { return $false }
        $regionLines = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        Get-Content -LiteralPath (Join-Path $sources $_.local) | ForEach-Object { [void]$regionLines.Add($_.Trim()) }
        return @($requiredRegions | Where-Object { -not $regionLines.Contains($_) }).Count -eq 0
    })
    if ($atlas.Count -ne 1) { throw 'Expected one map-node atlas containing the complete attachment region catalog' }
    $skeleton = @($index.assets | Where-Object { $_.file -eq $atlas[0].file -and $_.name -eq 'skeleton.skel' })
    if ($skeleton.Count -ne 1) { throw 'Expected one map-node skeleton beside the selected atlas' }
    & $NodeExe tools/export_node_meshes.mjs (Join-Path $sources $atlas[0].local) (Join-Path $sources $skeleton[0].local) (Join-Path $OutputFolder 'node-meshes.json')
    if ($LASTEXITCODE) { throw 'Map node mesh export failed' }
    & $NodeExe tools/export_mathieu_head_assets.mjs $sources (Join-Path $OutputFolder 'mathieu-head-assets.json')
    if ($LASTEXITCODE) { throw 'Head template map export failed' }
    & $NodeExe tools/export_head_templates.mjs $sources (Join-Path $OutputFolder 'head-templates.json')
    if ($LASTEXITCODE) { throw 'Native head template catalog map export failed' }
} finally { Pop-Location }
Write-Host "Generated five numbers-only maps in $OutputFolder"
