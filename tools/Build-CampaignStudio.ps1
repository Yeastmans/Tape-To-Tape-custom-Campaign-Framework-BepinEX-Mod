# Active Windows build entry point. Builds and packages; never installs or publishes.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$GameDirectory,
    [string]$DotnetPath = '',
    [string]$PythonPath = '',
    [string]$IsccPath = '',
    [string]$SevenZipPath = '',
    [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9._-]{0,80}$')][string]$ReleaseTag = 'beta-1.0-r2',
    [ValidatePattern('^[a-zA-Z0-9][a-zA-Z0-9-]{0,63}$')][string]$StageName = 'CampaignStudio'
)
$ErrorActionPreference = 'Stop'
$env:AVALONIA_TELEMETRY_OPTOUT = '1'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$game = [IO.Path]::GetFullPath($GameDirectory)
if ($StageName -in @('installer', 'dependencies')) { throw 'StageName cannot use the reserved installer or dependencies output folders.' }

function Assert-PlainPath([string]$Path) {
    $part = [IO.Path]::GetFullPath($Path)
    while ($part) {
        if ((Test-Path -LiteralPath $part) -and ((Get-Item -LiteralPath $part -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw "Linked build paths are not supported: $part" }
        $part = Split-Path $part -Parent
    }
}
function Find-Tool([string]$Explicit, [string[]]$Names, [string[]]$Candidates) {
    if ($Explicit) {
        if (!(Test-Path -LiteralPath $Explicit -PathType Leaf)) { throw "Tool not found: $Explicit" }
        return [IO.Path]::GetFullPath($Explicit)
    }
    foreach ($name in $Names) {
        $command = Get-Command $name -ErrorAction SilentlyContinue
        if ($command) { return $command.Source }
    }
    foreach ($candidate in $Candidates) { if ($candidate -and (Test-Path -LiteralPath $candidate -PathType Leaf)) { return $candidate } }
    throw ('Install the required tool or pass its executable path: ' + ($Names -join ', '))
}
function Assert-PlainTree([string]$Directory) {
    foreach ($entry in Get-ChildItem -LiteralPath $Directory -Force) {
        if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "A linked entry prevents cleaning the build stage: $($entry.FullName)" }
        if ($entry.PSIsContainer) { Assert-PlainTree $entry.FullName }
    }
}
function Run-Tool([string]$Executable, [string[]]$Arguments, [string]$Step) {
    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Step failed (exit $LASTEXITCODE)." }
}
function Same-Hash([string]$A, [string]$B, [string]$Description) {
    if (!(Test-Path -LiteralPath $A -PathType Leaf) -or !(Test-Path -LiteralPath $B -PathType Leaf) -or
        (Get-FileHash -LiteralPath $A).Hash -ne (Get-FileHash -LiteralPath $B).Hash) { throw "Package mismatch: $Description" }
}
function Check-NoGameArt([string]$Folder) {
    $forbidden = @('head-preview','head-templates','team-logos','base-rosters','base-maps','flat-equipment','node-images','map-preview','game-art','spine-runtime','node_modules','campaigns','library','.studio-drafts','.studio-staging')
    foreach ($file in Get-ChildItem -LiteralPath $Folder -File -Recurse -Force) {
        $relative = $file.FullName.Substring($Folder.TrimEnd('\').Length + 1)
        $parts = $relative -split '[/\\]'
        if (@($parts | Where-Object { $_ -in $forbidden }).Count -or $file.Extension.ToLowerInvariant() -in @('.png','.jpg','.jpeg','.webp','.skel','.atlas','.bundle') -or $file.Name -like '*Spine*') {
            throw "Extracted artwork, runtime or user content must not be bundled: $relative"
        }
    }
}

$dotnet = Find-Tool $DotnetPath @('dotnet.exe') @((Join-Path $env:ProgramFiles 'dotnet/dotnet.exe'))
$python = Find-Tool $PythonPath @('python.exe','py.exe') @()
$innoCandidates = @()
foreach ($base in @(${env:ProgramFiles(x86)}, $env:ProgramFiles, (Join-Path $env:LOCALAPPDATA 'Programs'))) {
    if ($base) { $innoCandidates += Join-Path $base 'Inno Setup 6/ISCC.exe' }
}
$iscc = Find-Tool $IsccPath @('ISCC.exe') $innoCandidates
if (!(Test-Path -LiteralPath (Join-Path $game 'Tape to Tape.exe') -PathType Leaf)) { throw 'Choose the game directory containing Tape to Tape.exe.' }
$interop = Join-Path $game 'BepInEx/interop'
if (!(Test-Path -LiteralPath (Join-Path $interop 'Assembly-CSharp.dll') -PathType Leaf)) {
    throw 'Current generated BepInEx interop is missing. Install the pinned loader into your own game, launch once, then close the game. This script does not install or launch it.'
}
$nativeProfilePath = Join-Path $repository 'src/CustomCampaignFramework/Assets/native-compatibility.json'
$baselinePath = Join-Path $repository 'src/ModCompatibility/Assets/native-baseline.json'
$nativeProfile = Get-Content -Raw -LiteralPath $nativeProfilePath | ConvertFrom-Json
$baseline = Get-Content -Raw -LiteralPath $baselinePath | ConvertFrom-Json
if ($nativeProfile.BaselineId -ne $baseline.Id -or $nativeProfile.GameHash -ne $baseline.Profile.GameHash -or $nativeProfile.MetadataHash -ne $baseline.Profile.MetadataHash) { throw 'Source mod and Studio compatibility baselines differ.' }
if ((Get-FileHash -LiteralPath (Join-Path $game 'GameAssembly.dll')).Hash -ne $nativeProfile.GameHash -or
    (Get-FileHash -LiteralPath (Join-Path $game 'Tape to Tape_Data/il2cpp_data/Metadata/global-metadata.dat')).Hash -ne $nativeProfile.MetadataHash) {
    throw "This source snapshot targets game build $($nativeProfile.Build). Update the source compatibility baseline before building for a different game."
}
# Restore only redistributable tools into the checkout; never touch the installed game.
& (Join-Path $PSScriptRoot 'Restore-BuildDependencies.ps1') -SevenZipPath $SevenZipPath

$buildRoot = [IO.Path]::GetFullPath((Join-Path $repository 'build'))
$stage = [IO.Path]::GetFullPath((Join-Path $buildRoot $StageName))
$marker = Join-Path $buildRoot ('.campaign-studio-build-' + $StageName + '.txt')
if ([IO.Path]::GetDirectoryName($stage) -ne $buildRoot) { throw 'Stage must be a direct child of this checkout build folder.' }
Assert-PlainPath $stage
Assert-PlainPath $marker
if (Test-Path -LiteralPath $stage) {
    if (!(Test-Path -LiteralPath $marker -PathType Leaf)) { throw "Existing stage was not created by this wrapper: $stage. Choose a new -StageName or move it yourself." }
    foreach ($process in Get-Process -Name 'T2T Campaign Studio' -ErrorAction SilentlyContinue) {
        try { $runningPath = $process.Path } catch { throw 'Cannot verify the running Studio process. Close it before rebuilding this stage.' }
        if (!$runningPath -or [IO.Path]::GetDirectoryName($runningPath).Equals($stage, [StringComparison]::OrdinalIgnoreCase)) { throw 'Studio is running from the build stage. Close it or use another -StageName.' }
    }
    Assert-PlainTree $stage
    # The resolved direct-child path, ancestors and descendants were checked above.
    Remove-Item -LiteralPath $stage -Recurse -Force
}
[IO.Directory]::CreateDirectory($buildRoot) | Out-Null
[IO.File]::WriteAllText($marker, 'Owned build output for tools/Build-CampaignStudio.ps1' + [Environment]::NewLine)
$pythonArguments = @()
if ([IO.Path]::GetFileName($python) -ieq 'py.exe') { $pythonArguments += '-3' }
$pythonArguments += Join-Path $PSScriptRoot 'build_upgrade_fields.py'
Run-Tool $python $pythonArguments 'Upgrade field schema generation'
Run-Tool $dotnet @('build', (Join-Path $repository 'src/CustomCampaignFramework'), '-c', 'Release', '-p:NuGetAudit=false', '-p:DebugType=None', '-p:DebugSymbols=false', ('-p:InteropLive=' + $interop), ('-p:InteropDir=' + $interop)) 'Mod build'

$mod = Join-Path $repository 'src/CustomCampaignFramework/bin/Release/net6.0/CustomCampaignFramework.dll'
$releaseMod = Join-Path $repository 'release/CustomCampaignFramework.dll'
$acceptedPath = Join-Path $repository 'src/ModCompatibility/Assets/replaces-sha256.txt'
if (Test-Path -LiteralPath $releaseMod -PathType Leaf) {
    $accepted = @(Get-Content -LiteralPath $acceptedPath) + (Get-FileHash -LiteralPath $releaseMod).Hash
    [IO.File]::WriteAllLines($acceptedPath, [string[]]($accepted | Where-Object { $_ -match '^[A-Fa-f0-9]{64}$' } | Sort-Object -Unique))
}
[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($releaseMod)) | Out-Null
Copy-Item -LiteralPath $mod -Destination $releaseMod -Force
$modHash = (Get-FileHash -LiteralPath $releaseMod).Hash
[IO.File]::WriteAllText((Join-Path $repository 'src/ModCompatibility/Assets/runtime-sha256.txt'), $modHash)
Run-Tool $dotnet @('publish', (Join-Path $repository 'src/CampaignCreator'), '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true', '-p:NuGetAudit=false', '-p:DebugType=None', '-p:DebugSymbols=false', '-o', $stage) 'Studio publish'

# Shipping invariants belong to packaging and do not depend on gameplay test projects.
Check-NoGameArt $stage
Check-NoGameArt (Join-Path $repository 'installer/bepinex')
$packagedMod = Join-Path $stage 'mod-runtime/CustomCampaignFramework.dll'
Same-Hash $releaseMod $packagedMod 'Studio bundled mod'
if ((Get-Content -Raw -LiteralPath (Join-Path $stage 'mod-runtime/runtime-sha256.txt')).Trim() -ne $modHash) { throw 'Bundled runtime checksum does not match the built mod.' }
Same-Hash $baselinePath (Join-Path $stage 'mod-runtime/native-baseline.json') 'Studio compatibility baseline'
foreach ($required in @('T2T Campaign Studio.exe','T2T Campaign Studio.dll','CampaignCore.dll','GameArt.dll','ModCompatibility.dll','THIRD_PARTY_NOTICES-GameArt.txt','THIRD_PARTY_NOTICES-ModCompatibility.txt','reward-data/_reward_catalog.tsv','audio-tools/ffmpeg.exe','audio-tools/LICENSE','audio-tools/README.txt')) {
    if (!(Test-Path -LiteralPath (Join-Path $stage $required) -PathType Leaf)) { throw "Missing package input: $required" }
}
$dependencyManifest = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'build-dependencies.json') | ConvertFrom-Json
$ffmpeg = @($dependencyManifest.dependencies | Where-Object name -eq 'FFmpeg')[0]
foreach ($file in $ffmpeg.files) { Same-Hash (Join-Path $repository ('tools/ffmpeg/' + $file.path)) (Join-Path $stage ('audio-tools/' + $file.path)) ('FFmpeg ' + $file.path) }
[xml]$studioProject = Get-Content -Raw -LiteralPath (Join-Path $repository 'src/CampaignCreator/CampaignCreator.csproj')
$resourceInputs = @($studioProject.SelectNodes('//AvaloniaResource') | ForEach-Object Include)
$allowedInputs = @('../../assets/studio-guide/*.png', '../CustomCampaignFramework/Assets/MapSeams/*.png')
if ($resourceInputs.Count -ne 2 -or @($resourceInputs | Where-Object { $_ -notin $allowedInputs }).Count) { throw 'Review the changed Studio embedded artwork inputs before packaging.' }
$brandIcon = '../../assets/branding/CampaignStudio.ico'
$embeddedArtwork = @($studioProject.SelectNodes('//EmbeddedResource') | Where-Object { $_.Include -match '\.(ico|png|jpe?g|webp|gif|skel|atlas|bundle)$' })
if ($embeddedArtwork.Count -ne 1 -or $embeddedArtwork[0].Include -ne $brandIcon -or
    $embeddedArtwork[0].LogicalName -ne 'CampaignStudioIcon' -or $studioProject.Project.PropertyGroup.ApplicationIcon -ne $brandIcon) {
    throw 'The embedded application artwork must be the approved CampaignStudio.ico brand icon.'
}
if (!(Test-Path -LiteralPath (Join-Path $repository 'assets/branding/CampaignStudio.ico') -PathType Leaf)) { throw 'The Studio brand icon is missing.' }
$seamSource = Join-Path $repository 'src/CustomCampaignFramework/Assets/MapSeams'
$seamOutput = Join-Path $repository 'src/CustomCampaignFramework/bin/Release/net6.0/MapSeams'
$sourceSeams = @(Get-ChildItem -LiteralPath $seamSource -File -Filter '*.png')
$outputSeams = @(Get-ChildItem -LiteralPath $seamOutput -File)
if ($sourceSeams.Count -ne 6 -or $outputSeams.Count -ne $sourceSeams.Count) { throw 'The mod must contain exactly the six approved map seams.' }
foreach ($file in $sourceSeams) { Same-Hash $file.FullName (Join-Path $seamOutput $file.Name) ('Map seam ' + $file.Name) }

$installerOutput = Join-Path $buildRoot 'installer'
Assert-PlainPath $installerOutput
[IO.Directory]::CreateDirectory($installerOutput) | Out-Null
$label = ([regex]::Match((Get-Content -Raw -LiteralPath (Join-Path $repository 'installer/T2T_CampaignFramework.iss')), '#define AppVersion "([^"]+)"')).Groups[1].Value
$buildId = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds()
$identity = [ordered]@{ schema=1; buildId=$buildId; label=$label; platform='win-x64' }
[IO.File]::WriteAllText((Join-Path $stage 'studio-build.json'), ($identity | ConvertTo-Json) + [Environment]::NewLine)
Run-Tool $iscc @(('/DStudioDir=' + $stage), ('/O' + $installerOutput), (Join-Path $repository 'installer/T2T_CampaignFramework.iss')) 'Installer compilation'
$installer = Join-Path $installerOutput 'T2T_Custom_Campaign_Framework_Setup.exe'
if (!(Test-Path -LiteralPath $installer -PathType Leaf)) { throw 'Inno Setup did not produce the expected installer.' }
$receipt = [ordered]@{
    release = $label; buildId = $buildId; releaseTag = $ReleaseTag; builtUtc = [DateTime]::UtcNow.ToString('o'); gameBuild = $nativeProfile.Build
    gameHash = $nativeProfile.GameHash; metadataHash = $nativeProfile.MetadataHash; baselineId = $nativeProfile.BaselineId
    modSha256 = $modHash; studioSha256 = (Get-FileHash -LiteralPath (Join-Path $stage 'T2T Campaign Studio.dll')).Hash
    installerSha256 = (Get-FileHash -LiteralPath $installer).Hash
    stage = $stage; installer = $installer; validation = 'Package structure, input artwork/brand-icon allowlist, seams, dependency and runtime hashes; no gameplay or installer execution.'
}
[IO.File]::WriteAllText((Join-Path $installerOutput 'build-receipt.json'), ($receipt | ConvertTo-Json) + [Environment]::NewLine)
$update = [ordered]@{
    schema=1; buildId=$buildId; label=$label; platform='win-x64'; releaseTag=$ReleaseTag
    installerName=[IO.Path]::GetFileName($installer); installerSize=(Get-Item -LiteralPath $installer).Length
    installerSha256=$receipt.installerSha256; studioSha256=$receipt.studioSha256; modSha256=$modHash
}
[IO.File]::WriteAllText((Join-Path $installerOutput 'campaign-studio-update.json'), ($update | ConvertTo-Json) + [Environment]::NewLine)
Write-Output "Built $label installer: $installer"
Write-Output 'No game files, user content, installed applications or published releases were changed.'
