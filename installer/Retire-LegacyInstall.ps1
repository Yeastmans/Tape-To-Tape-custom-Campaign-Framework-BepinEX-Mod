# Runs as the original desktop user AFTER the new install succeeds.
# The pre-Studio installer (2.1.31 and earlier) registered per user, under this
# same AppId, with its uninstaller in the game folder. Running that uninstaller
# deletes the BepInEx loader, so its Apps entry and Creator shortcuts are retired.
# The uninstaller files themselves are removed by Setup (elevated).
param([Parameter(Mandatory=$true)][string]$GameDir, [switch]$WhatIf)
$ErrorActionPreference = 'Stop'
function Same-Path([string]$A, [string]$B) {
    if ([string]::IsNullOrWhiteSpace($A) -or [string]::IsNullOrWhiteSpace($B)) { return $false }
    return [IO.Path]::GetFullPath($A.Trim().Trim('"')).TrimEnd('\') -ieq [IO.Path]::GetFullPath($B).TrimEnd('\')
}
function Retire([string]$What, [scriptblock]$Action) {
    if ($WhatIf) { Write-Output "would $What" } else { & $Action; Write-Output $What }
}
try {
    $GameDir = [IO.Path]::GetFullPath($GameDir).TrimEnd('\')
    $oldCreator = Join-Path $GameDir 'BepInEx\plugins\Custom Campaigns Mod'

    $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{D8C4C4D2-8B7A-4A9C-B2D3-C1F4E5A6D7B8}_is1'
    if (Test-Path -LiteralPath $key) {
        $entry = Get-ItemProperty -LiteralPath $key
        if (Same-Path $entry.InstallLocation $GameDir) {
            Retire "remove Apps entry '$($entry.DisplayName) $($entry.DisplayVersion)'" { Remove-Item -LiteralPath $key -Recurse }
        }
    }

    # Shortcuts are matched by target, so a renamed or moved shortcut is still found
    # and anything pointing elsewhere is left alone. The desktop is not searched
    # recursively: it can hold large user folders.
    $shell = New-Object -ComObject WScript.Shell
    $programs = [Environment]::GetFolderPath('Programs')
    $links = @(Get-ChildItem -LiteralPath $programs -Recurse -Filter *.lnk -ErrorAction SilentlyContinue) +
             @(Get-ChildItem -LiteralPath ([Environment]::GetFolderPath('Desktop')) -Filter *.lnk -ErrorAction SilentlyContinue)
    foreach ($link in $links) {
        $target = $shell.CreateShortcut($link.FullName).TargetPath
        if ($target -and $target.StartsWith($oldCreator + '\', [StringComparison]::OrdinalIgnoreCase)) {
            Retire "remove shortcut $($link.FullName)" { Remove-Item -LiteralPath $link.FullName }
        }
    }
    $group = Join-Path $programs 'T2T Custom Campaign Framework'
    if ((Test-Path -LiteralPath $group) -and -not (Get-ChildItem -LiteralPath $group -Force)) {
        Retire "remove empty Start Menu folder $group" { Remove-Item -LiteralPath $group }
    }
    exit 0
} catch {
    Write-Error ($_.Exception.Message + [Environment]::NewLine + $_.ScriptStackTrace) -ErrorAction Continue
    exit 1
}
