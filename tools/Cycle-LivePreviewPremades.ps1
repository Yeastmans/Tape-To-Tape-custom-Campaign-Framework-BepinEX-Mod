param(
    [string]$RoundOne = 'C:\Users\kiran\Desktop\tape to tape mods\BACKUPS\live-preview-premade-round1-20260924-201328.txt',
    [string]$Preview = (Join-Path $env:LOCALAPPDATA 'T2T Campaign Studio\live-preview.txt'),
    [int]$Seconds = 5
)

$ErrorActionPreference = 'Stop'
if (-not (Get-Process 'Tape to Tape' -ErrorAction SilentlyContinue)) { throw 'Tape to Tape is not running.' }
if (-not (Test-Path -LiteralPath $RoundOne)) { throw "Missing round-one preview: $RoundOne" }
if (-not (Test-Path -LiteralPath $Preview)) { throw "Missing live preview: $Preview" }
$roundOneLines = Get-Content -LiteralPath $RoundOne
$roundTwoText = Get-Content -LiteralPath $Preview -Raw
$roundTwo = @{}
foreach ($line in ($roundTwoText -split '\r?\n')) {
    if ($line -match '^\s*([^#][^=]*?)\s*=\s*(.*)$') { $roundTwo[$matches[1].Trim()] = $matches[2].Trim() }
}

$groups = [ordered]@{
    'Skater face' = @('Face')
    'Skater jerseys' = @('Body', 'Body Away')
    'Skater helmets' = @('Helmet', 'Helmet Away')
    'Skater sleeves' = @('Bicep', 'Bicep Away')
    'Skater gloves' = @('Gloves', 'Gloves Away')
    'Skater pants' = @('Pants', 'Pants Away')
    'Skater skates' = @('Skates', 'Skates Away')
    'Skater stick' = @('Stick')
    'Skater glasses' = @('Glasses')
    'Skater number' = @('Number Skin')
    'Goalie jerseys' = @('Skin', 'Skin Away')
    'Goalie pants' = @('Goalie Pants Skin', 'Goalie Pants Away')
    'Goalie helmet' = @('Helmet Skin')
    'Goalie gloves' = @('Glove Skin', 'Glove Away')
    'Goalie blockers' = @('Blocker Skin', 'Blocker Away')
    'Goalie pads' = @('Pads Skin', 'Pads Away')
    'Goalie sticks' = @('Stick Skin', 'Stick Away')
}

try {
    foreach ($group in $groups.GetEnumerator()) {
        if (-not (Get-Process 'Tape to Tape' -ErrorAction SilentlyContinue)) { throw 'Game closed during preview cycle.' }
        $keys = $group.Value
        foreach ($key in $keys) { if (-not $roundTwo.ContainsKey($key)) { throw "Round-two preview lacks $key" } }
        $lines = [System.Collections.Generic.List[string]]::new()
        $lines.Add("# Temporary premade comparison: $($group.Key). Next group in $Seconds seconds.")
        foreach ($line in $roundOneLines) {
            if ($line -notmatch '^\s*([^#][^=]*?)\s*=\s*(.*)$') { continue }
            $key = $matches[1].Trim()
            $value = if ($keys -contains $key) { $roundTwo[$key] } else { $matches[2].Trim() }
            $lines.Add("$key = $value")
        }
        [System.IO.File]::WriteAllLines($Preview, $lines, [System.Text.UTF8Encoding]::new($false))
        Write-Output ("{0:HH:mm:ss}  {1}" -f (Get-Date), $group.Key)
        Start-Sleep -Seconds $Seconds
    }
}
finally {
    [System.IO.File]::WriteAllText($Preview, $roundTwoText, [System.Text.UTF8Encoding]::new($false))
    Write-Output ("{0:HH:mm:ss}  Restored full round-two preview" -f (Get-Date))
}
