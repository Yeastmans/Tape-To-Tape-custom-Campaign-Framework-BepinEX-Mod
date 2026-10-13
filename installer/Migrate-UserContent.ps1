# Runs as the original desktop user BEFORE the elevated installer removes old files.
param([Parameter(Mandatory=$true)][string]$GameDir, [string]$AppDir, [string]$ErrorFile = "",
      [string]$DataRoot = (Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'T2T Campaign Studio'))
$ErrorActionPreference = 'Stop'
function Extended-Path([string]$Path) {
    if ($Path.StartsWith('\\?\')) { return $Path }
    if ($Path.StartsWith('\\')) { return '\\?\UNC\' + $Path.Substring(2) }
    if (![IO.Path]::IsPathRooted($Path)) { $Path = [IO.Path]::GetFullPath($Path) }
    return '\\?\' + $Path
}
function Assert-PlainPath([string]$Path) {
    $part = [IO.Path]::GetFullPath($Path)
    while ($part) {
        if ((Test-Path -LiteralPath $part) -and ((Get-Item -LiteralPath $part -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Linked paths must be migrated manually: $part" }
        $part = Split-Path $part -Parent
    }
}
function Copy-Verified([string]$Source, [string]$Destination) {
    $Source = Extended-Path $Source
    $Destination = Extended-Path $Destination
    if ((Get-Item -LiteralPath $Source -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "Refusing linked content: $Source" }
    Assert-PlainPath $Destination
    if (Test-Path -LiteralPath $Source -PathType Container) {
        [IO.Directory]::CreateDirectory($Destination) | Out-Null
        foreach ($item in Get-ChildItem -LiteralPath $Source -Force) { Copy-Verified $item.FullName ([IO.Path]::Combine($Destination, $item.Name)) }
    } else {
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Destination)) | Out-Null
        [IO.File]::Copy($Source, $Destination, $true)
        if ((Get-FileHash -LiteralPath $Source).Hash -ne (Get-FileHash -LiteralPath $Destination).Hash) { throw "Migration copy verification failed: $Source" }
    }
}
function Merge-Preserved([string]$Source, [string]$Destination) {
    $Source = Extended-Path $Source
    $Destination = Extended-Path $Destination
    Assert-PlainPath $Source
    Assert-PlainPath $Destination
    if (Test-Path -LiteralPath $Source -PathType Container) {
        if (Test-Path -LiteralPath $Destination -PathType Leaf) { return }
        [IO.Directory]::CreateDirectory($Destination) | Out-Null
        foreach ($item in Get-ChildItem -LiteralPath $Source -Force) { Merge-Preserved $item.FullName ([IO.Path]::Combine($Destination, $item.Name)) }
    } elseif (!(Test-Path -LiteralPath $Destination)) { Copy-Verified $Source $Destination }
    # Existing AppData content wins. No archival copies are created.
}
try {
    $GameDir = [IO.Path]::GetFullPath($GameDir)
    if (!(Test-Path -LiteralPath (Join-Path $GameDir 'Tape to Tape.exe'))) { throw 'Invalid game directory.' }
    Assert-PlainPath $GameDir
    foreach ($runtime in @('BepInEx\core','BepInEx\interop','BepInEx\cache','dotnet')) { Assert-PlainPath (Join-Path $GameDir $runtime) }
    Assert-PlainPath $DataRoot
    if ($AppDir) {
        Assert-PlainPath $AppDir
        $application = [IO.Path]::GetFullPath($AppDir).TrimEnd('\') + '\'
        $content = [IO.Path]::GetFullPath($DataRoot).TrimEnd('\') + '\'
        if ($application.StartsWith($content, [StringComparison]::OrdinalIgnoreCase) -or $content.StartsWith($application, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Choose an application folder separate from the AppData content folder.'
        }
    }
    New-Item -ItemType Directory -Path $DataRoot -Force | Out-Null
    $plugins = Join-Path $GameDir 'BepInEx\plugins'
    $sources = @(@{Path=(Join-Path $plugins 'Custom Campaigns Mod'); Name='old-creator'}, @{Path=(Join-Path $plugins 'Campaigns'); Name='legacy-campaigns'})
    if ($AppDir -and (Test-Path -LiteralPath $AppDir)) { $sources += @{Path=$AppDir; Name='previous-studio'} }
    # The old plugin regenerated these library folders on every launch, filing any
    # player in memory (campaign players included) under "Base Game". Studio ships
    # its own base rosters, so they are never carried into AppData.
    $generatedLibrary = @('Base Game Teams','Base Game Players','Custom Teams','Custom Players')
    # Old portable Studio builds kept authored content beside the executable.
    # Only these product-owned saved-content locations belong in AppData; runtime
    # libraries, media tools, generated art and uninstall metadata stay in AppDir.
    $studioDirectories = @('campaigns','library','custom-heads','.head-backups','campaign_backups','.studio-drafts','.studio-staging','play_now_overrides','logos','_team_logos')
    $studioFiles = @('active.txt','game-folder.txt','studio-default-look.cfg')
    foreach ($source in $sources) {
        if (!(Test-Path -LiteralPath $source.Path)) { continue }
        Assert-PlainPath $source.Path
        $saved = $source.Path
        if ($source.Name -eq 'legacy-campaigns') { Merge-Preserved $saved (Join-Path $DataRoot 'campaigns'); continue }
        foreach ($item in Get-ChildItem -LiteralPath $saved -Force) {
            if ($source.Name -eq 'previous-studio') {
                if ($item.PSIsContainer) { if ($item.Name -notin $studioDirectories) { continue } }
                elseif ($item.Name -notin $studioFiles) { continue }
            }
            if ($item.Name -in @('_internal','_build','_dist','__pycache__','head-preview','reward-data','migration-backups','content-folder.txt','defaults.txt','base-rosters')) { continue }
            if (!$item.PSIsContainer -and ($item.Extension -in @('.exe','.dll','.pdb','.py','.pyc','.deps.json','.runtimeconfig.json') -or $item.Name -in @('VERSION.txt','README.md'))) { continue }
            if ($item.PSIsContainer -and $item.Name -eq 'library') {
                foreach ($entry in Get-ChildItem -LiteralPath $item.FullName -Force) {
                    if ($entry.Name -in $generatedLibrary) { continue }
                    Merge-Preserved $entry.FullName (Join-Path (Join-Path $DataRoot 'library') $entry.Name)
                }
                continue
            }
            Merge-Preserved $item.FullName (Join-Path $DataRoot $item.Name)
        }
    }
    $dump = Join-Path $plugins 'T2T_Dumps'
    if (Test-Path -LiteralPath $dump) { Merge-Preserved $dump (Join-Path $DataRoot 'T2T_Dumps') }
    # defaults.txt is not carried over: the game ignores it outside the legacy
    # (Linux creator) layout, and Studio has its own default look for new players.
    $old = Join-Path $plugins 'active.txt'
    if (Test-Path -LiteralPath $old) { Merge-Preserved $old (Join-Path $DataRoot 'active.txt') }
    foreach ($name in @('campaigns','library')) { New-Item -ItemType Directory -Path (Join-Path $DataRoot $name) -Force | Out-Null }
    if (!(Test-Path -LiteralPath (Join-Path $DataRoot 'active.txt'))) { Set-Content -LiteralPath (Join-Path $DataRoot 'active.txt') 'Active Campaign = default' }
    Set-Content -LiteralPath (Join-Path $DataRoot 'game-folder.txt') $GameDir
    exit 0
} catch {
    $detail = $_.Exception.Message + [Environment]::NewLine + $_.ScriptStackTrace
    if ($ErrorFile) { try { [IO.File]::WriteAllText($ErrorFile, $detail) } catch {} }
    Write-Error $detail -ErrorAction Continue
    exit 1
}








