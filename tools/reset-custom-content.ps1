$ErrorActionPreference = 'Stop'
$modRoot = 'C:\Steam\steamapps\common\Tape to Tape\BepInEx\plugins\Custom Campaigns Mod'
$saveRoot = 'C:\Users\kiran\AppData\LocalLow\Excellent Rectangle\Tape to Tape'
$backupRoot = Join-Path 'C:\Users\kiran\Desktop\tape to tape mods\BACKUPS' ('fresh-start-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
foreach ($proc in @(Get-Process | Where-Object { $_.ProcessName -in @('Tape to Tape','T2T Campaign Studio','T2T Campaign Creator') })) {
    if (-not $proc.CloseMainWindow()) { throw "Could not gracefully close $($proc.ProcessName). No reset performed." }
    if (-not $proc.WaitForExit(15000)) { throw "$($proc.ProcessName) is still open, possibly awaiting an unsaved-changes choice. No reset performed." }
}
New-Item -ItemType Directory -Path $backupRoot | Out-Null
$targets = [System.Collections.Generic.List[object]]::new()
foreach ($rel in @('campaigns','library\Custom Players','library\Custom Teams','library\players','library\teams','play_now_overrides','.studio-drafts','.studio-staging')) {
    $targets.Add(@{ Source=(Join-Path $modRoot $rel); Destination=(Join-Path $backupRoot ('mod\'+$rel)); Recreate=$true })
}
$targets.Add(@{ Source=(Join-Path $saveRoot 'CustomLogos'); Destination=(Join-Path $backupRoot 'save\CustomLogos'); Recreate=$true })
$rosterRoot = Join-Path $saveRoot 'TeamDataModels'
foreach($file in @(Get-ChildItem -LiteralPath $rosterRoot -File -Recurse | Where-Object { $_.Name -match '^Custom(Forward|Goalie|Team)-.*\.json$' })) {
    $relative = $file.FullName.Substring($saveRoot.Length+1)
    $targets.Add(@{ Source=$file.FullName; Destination=(Join-Path $backupRoot ('save\'+$relative)); Recreate=$false })
}
$manifest = [System.Collections.Generic.List[object]]::new()
foreach($target in $targets) {
    if(-not (Test-Path -LiteralPath $target.Source)) { continue }
    $resolved = (Resolve-Path -LiteralPath $target.Source).Path
    if(-not ($resolved.StartsWith($modRoot+'\',[StringComparison]::OrdinalIgnoreCase) -or $resolved.StartsWith($saveRoot+'\',[StringComparison]::OrdinalIgnoreCase))) { throw "Unsafe source: $resolved" }
    $items = @(Get-Item -LiteralPath $resolved)
    if($items[0].PSIsContainer) { $items += @(Get-ChildItem -LiteralPath $resolved -Recurse -Force) }
    if($items | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw "Linked path found: $resolved" }
    foreach($file in @($items | Where-Object { -not $_.PSIsContainer })) {
        $suffix = $file.FullName.Substring($resolved.Length)
        $manifest.Add(@{ Source=$file.FullName; Backup=($target.Destination+$suffix); SHA256=(Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash })
    }
}
Copy-Item -LiteralPath (Join-Path $modRoot 'active.txt') -Destination (Join-Path $backupRoot 'active.txt')
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $backupRoot 'manifest.json')
foreach($target in $targets) {
    if(-not (Test-Path -LiteralPath $target.Source)) { continue }
    New-Item -ItemType Directory -Path (Split-Path $target.Destination -Parent) -Force | Out-Null
    Move-Item -LiteralPath $target.Source -Destination $target.Destination
    if($target.Recreate) { New-Item -ItemType Directory -Path $target.Source | Out-Null }
}
Set-Content -LiteralPath (Join-Path $modRoot 'active.txt') -Value 'Active Campaign = default'
foreach($entry in $manifest) { if((Get-FileHash -LiteralPath $entry.Backup -Algorithm SHA256).Hash -ne $entry.SHA256) { throw "Backup mismatch: $($entry.Source)" } }
$remaining = @(Get-ChildItem -LiteralPath $rosterRoot -File -Recurse | Where-Object { $_.Name -match '^Custom(Forward|Goalie|Team)-.*\.json$' })
if($remaining.Count -ne 0) { throw 'Custom roster files remain' }
foreach($target in $targets | Where-Object { $_.Recreate }) { if(@(Get-ChildItem -LiteralPath $target.Source -Force).Count -ne 0) { throw "Reset folder is not empty: $($target.Source)" } }
@{ Backup=$backupRoot; BackedUpFiles=$manifest.Count; RemainingCustomRosterFiles=$remaining.Count; ActiveCampaign=(Get-Content -LiteralPath (Join-Path $modRoot 'active.txt')); BackupHashesVerified=$true } | ConvertTo-Json
