$ErrorActionPreference = 'Stop'
$gameRoot = 'C:/Steam/steamapps/common/Tape to Tape'
$contentRoot = Join-Path $gameRoot 'BepInEx/plugins/Custom Campaigns Mod'
$activeFile = Join-Path $contentRoot 'active.txt'
$receiptRoot = Join-Path $PSScriptRoot '../artifacts/pool-wiring'
$testName = 'PoolWiringCheck-' + [Guid]::NewGuid().ToString('N')
$testCampaign = Join-Path $contentRoot ('campaigns/' + $testName)
$activeBytes = [IO.File]::ReadAllBytes($activeFile)
[IO.File]::WriteAllBytes((Join-Path $receiptRoot 'active-before.txt'), $activeBytes)
$ownedProcess = $null
try {
    New-Item -ItemType Directory -Path (Join-Path $testCampaign 'player_teams/free_agents') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $testCampaign 'free_agents') -Force | Out-Null
    'Maps = Basic' | Set-Content (Join-Path $testCampaign 'campaign.txt')
    Copy-Item (Join-Path $contentRoot 'library/Base Game Players/Brad Bird.txt') (Join-Path $testCampaign 'player_teams/free_agents/Brad Bird.txt')
    Copy-Item (Join-Path $contentRoot 'library/Base Game Players/Bear Grohl.txt') (Join-Path $testCampaign 'free_agents/Bear Grohl.txt')
    @('Excluded Relics = wiring-relic','Excluded Talents = wiring-skater','Excluded Goalie Talents = wiring-goalie','Excluded Abilities = wiring-ability') | Set-Content (Join-Path $testCampaign 'reward_pools.txt')
    ('Active Campaign = ' + $testName) | Set-Content $activeFile
    Copy-Item (Join-Path $receiptRoot 'CustomCampaignFramework.dll') (Join-Path $gameRoot 'BepInEx/plugins/CustomCampaignFramework.dll')
    $env:T2T_VERIFY_POOL_WIRING = '1'
    $ownedProcess = Start-Process -FilePath (Join-Path $gameRoot 'Tape to Tape.exe') -WorkingDirectory $gameRoot -ArgumentList '-batchmode','-nographics','-noaudio' -WindowStyle Hidden -PassThru
    $ownedProcess.Id | Set-Content (Join-Path $receiptRoot 'game-pid.txt')
    $deadline = [DateTime]::UtcNow.AddSeconds(50)
    do {
        Start-Sleep -Seconds 2
        $log = Get-Content (Join-Path $gameRoot 'BepInEx/LogOutput.log') -Raw -ErrorAction SilentlyContinue
        if ($log -match 'Native weighted relic/talent selection wiring PASS' -or $log -match 'Error loading \[Custom Campaign') { break }
    } while ([DateTime]::UtcNow -lt $deadline -and !$ownedProcess.HasExited)
    Copy-Item (Join-Path $gameRoot 'BepInEx/LogOutput.log') (Join-Path $receiptRoot 'runtime.log')
    $log | Select-String 'Native weighted|Loaded 2 free agents|reward_pools.txt loaded|Error loading|Failed eligible' -AllMatches | ForEach-Object { $_.Matches.Value }
} finally {
    if ($ownedProcess -and !$ownedProcess.HasExited) { Stop-Process -Id $ownedProcess.Id }
    [IO.File]::WriteAllBytes($activeFile, $activeBytes)
    $campaignParent = [IO.Path]::GetFullPath((Join-Path $contentRoot 'campaigns')) + [IO.Path]::DirectorySeparatorChar
    $resolvedTest = [IO.Path]::GetFullPath($testCampaign)
    if ($resolvedTest.StartsWith($campaignParent, [StringComparison]::OrdinalIgnoreCase) -and (Split-Path $resolvedTest -Leaf) -eq $testName) { Remove-Item -LiteralPath $resolvedTest -Recurse -Force }
}
