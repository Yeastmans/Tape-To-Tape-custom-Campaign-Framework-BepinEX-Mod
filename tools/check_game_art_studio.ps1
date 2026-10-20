# Studio regression checks with a developer-local art cache and isolated save folders.
param(
    [string]$CacheFolder = (Join-Path $PSScriptRoot '../artifacts/game-art-cache')
)
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
Set-Location -LiteralPath $root
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$env:T2T_GAME_ART_CACHE = [IO.Path]::GetFullPath($CacheFolder)
$output = Join-Path $root ('artifacts/game-art-studio-checks/' + [DateTime]::Now.ToString('yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $output -Force | Out-Null
$env:T2T_GAME_SAVE_PATH = Join-Path $output 'fake-game-save'
$dotnet = 'C:\Program Files\dotnet\dotnet.exe'
$results = @()
function Run-Check([string]$Name, [string[]]$Arguments) {
    $log = Join-Path $output ($Name + '.log')
    & $dotnet @Arguments *> $log
    $ok = $LASTEXITCODE -eq 0
    $script:results += [PSCustomObject]@{ Check = $Name; Passed = $ok; Log = $log }
    if ($ok) { Write-Output "PASS $Name" } else { Write-Output "FAIL $Name"; Get-Content -LiteralPath $log -Tail 7 }
}
# ImportAuditChecks audits a supplied content collection rather than a fixture-based editor suite.
foreach ($project in Get-ChildItem -LiteralPath (Join-Path $root 'tests') -Filter '*.csproj' -Recurse | Where-Object { $_.Directory.Name -notin @('obj', 'bin') } | Sort-Object FullName) {
    $name = $project.BaseName
    if ($name -in @('GameArtChecks', 'GameArtPackageChecks', 'CampaignStudioChecks', 'StudioLearningChecks', 'ImportAuditChecks')) { continue }
    if ((Get-Content -LiteralPath $project.FullName -Raw) -notmatch 'CampaignCreator') { continue }
    $arguments = @('run', '--project', $project.FullName, '-c', 'Release', '-p:NuGetAudit=false')
    if ($name -in @('MapTabsChecks', 'StudioThemeChecks', 'GoalAudioImportChecks', 'CampaignSquadChecks')) {
        $arguments += @('--', (Join-Path $output $name))
    }
    Run-Check $name $arguments
}
$studio = Join-Path $root 'tests/CampaignStudioChecks'
Run-Check 'CampaignStudioChecks' @('run', '--project', $studio, '-c', 'Release', '-p:NuGetAudit=false', '--', (Join-Path $output 'baseline-content'), (Join-Path $output 'baseline'))
foreach ($mode in @('head-previews', 'head-selection', 'head-editor', 'custom-head', 'flat-equipment', 'appearance-parity', 'logo-picker', 'player-logo', 'import-library', 'import-values', 'team-import', 'free-agents', 'squad-appearance', 'node-settings', 'no-game-art')) {
    Run-Check $mode @('run', '--project', $studio, '-c', 'Release', '--no-build', '--', ('--' + $mode), (Join-Path $output $mode))
}
$results | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'results.json') -Encoding UTF8
Write-Output ("{0} passed, {1} failed. Logs: {2}" -f @($results | Where-Object Passed).Count, @($results | Where-Object { !$_.Passed }).Count, $output)
if ($results | Where-Object { !$_.Passed }) { exit 1 }
