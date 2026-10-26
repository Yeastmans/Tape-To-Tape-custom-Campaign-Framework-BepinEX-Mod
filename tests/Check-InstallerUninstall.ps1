[CmdletBinding()]
param(
    [string]$IsccPath = (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 6\ISCC.exe'),
    [ValidateSet('fresh','upgrade','missing-files','junction','installed-junction','loader-junction-no-guard','running-game','late-game')]
    [string[]]$Scenarios = @('fresh','upgrade','missing-files','junction','installed-junction','loader-junction-no-guard','running-game','late-game')
)
$ErrorActionPreference = 'Stop'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$fixture = Join-Path $repository ('artifacts\uninstall-check-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($fixture) | Out-Null
$source = Get-Content -Raw -LiteralPath (Join-Path $repository 'installer\T2T_CampaignFramework.iss')

function Section([string]$Name) {
    [regex]::Match($source, '(?ms)^\[' + [regex]::Escape($Name) + '\]\s*\r?\n(.*?)(?=^\[|\z)').Groups[1].Value
}
function Assert-FixturePath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    if (!$full.StartsWith($fixture + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Not a fixture path: $full" }
    return $full
}
function Write-Fixture([string]$Path, [string]$Text) {
    $full = Assert-FixturePath $Path
    [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($full)) | Out-Null
    [IO.File]::WriteAllText($full, $Text)
}
function Run-Installer([string]$Executable, [string[]]$Arguments) {
    $null = Assert-FixturePath $Executable
    $process = Start-Process -FilePath $Executable -ArgumentList $Arguments -WindowStyle Hidden -Wait -PassThru
    return $process.ExitCode
}
function Check([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw "FAIL: $Message (fixture: $fixture)" }
    Write-Output "PASS: $Message"
}

# Compile the production file/deletion sections, not a reimplementation of cleanup.
# Fixture-only setup disables elevation, registry/shortcuts and all launch/migration
# hooks. No real installation, profile or game directory is referenced.
$payload = Join-Path $fixture 'payload'
$bundle = Join-Path $fixture 'bepinex'
foreach ($name in @('T2T Campaign Studio.exe','CampaignCore.dll','mod-runtime\CustomCampaignFramework.dll','mod-runtime\MapSeams\seam.png')) {
    Write-Fixture (Join-Path $payload $name) "fixture $name"
}
$loaderFiles = @('winhttp.dll','doorstop_config.ini','.doorstop_version','BepInEx_LICENSE.txt','changelog.txt')
foreach ($name in @('BepInEx\core\BepInEx.Core.dll','dotnet\host\hostfxr.dll') + $loaderFiles) {
    Write-Fixture (Join-Path $bundle $name) "fixture $name"
}
foreach ($name in @('Migrate-UserContent.ps1','Retire-LegacyInstall.ps1')) {
    Copy-Item -LiteralPath (Join-Path $repository "installer\$name") -Destination (Join-Path $fixture $name)
}
$files = (Section 'Files').Replace('Source: "bepinex\*"', 'Source: "' + $bundle + '\*"')
$deletions = Section 'UninstallDelete'
$messages = Section 'Messages'
$guard = ''
foreach ($name in @('GameIsRunning','InitializeUninstall','CurUninstallStepChanged')) {
    $guard += [regex]::Match($source, '(?ms)^(?:function|procedure) ' + $name + '\([^\r\n]*\).*?^end;').Value + "`r`n"
}
$appId = 'CampaignStudio-Uninstall-Test-' + [Guid]::NewGuid().ToString('N')
foreach ($variant in @('legacy','current')) {
    $variantFiles = $files
    $variantDeletions = $deletions
    if ($variant -eq 'legacy') {
        $variantDeletions = ''
        $variantFiles = [regex]::Replace($files, '(?m)^(Source: "' + [regex]::Escape($bundle) + '\\\*"[^\r\n]*?)(?: uninsneveruninstall)?$', '$1 uninsneveruninstall')
    }
    $script = @"
#define StudioDir "$payload"
[Setup]
AppId=$appId
AppName=Campaign Studio uninstall fixture
AppVersion=1.0
DefaultDirName=$fixture\unused default
PrivilegesRequired=lowest
CreateUninstallRegKey=no
UninstallFilesDir={app}
CloseApplications=no
OutputDir=$fixture
OutputBaseFilename=$variant
Compression=none
WizardStyle=modern
[Files]
$variantFiles
[UninstallDelete]
$variantDeletions
[Messages]
$messages
[Code]
function GameDirectory(Param: String): String;
begin
  Result := ExpandConstant('{param:GAMEDIR}');
end;
$guard
// Fixture-only injection: the game starts after InitializeUninstall succeeds.
procedure InitializeUninstallProgressForm();
var
  GameExe: String;
  ExitCode: Integer;
  Attempts: Integer;
begin
  GameExe := ExpandConstant('{param:LATEGAMEEXE}');
  if GameExe = '' then Exit;
  if not Exec(GameExe, '-n 5 127.0.0.1', '', SW_HIDE, ewNoWait, ExitCode) then
    RaiseException('Could not start the late-game fixture.');
  for Attempts := 1 to 40 do begin
    if GameIsRunning() then Exit;
    Sleep(50);
  end;
  RaiseException('The late-game fixture never became visible.');
end;
"@
    $scriptPath = Join-Path $fixture "$variant.iss"
    Write-Fixture $scriptPath $script
    & $IsccPath /Q $scriptPath 2>&1 | Out-File -LiteralPath (Join-Path $fixture "$variant-compile.log")
    if ($LASTEXITCODE -ne 0) { throw "Fixture compilation failed: $fixture\$variant-compile.log" }
}

foreach ($scenario in $Scenarios) {
    $case = Join-Path $fixture $scenario
    $game = Join-Path $case 'Game with spaces'
    $app = Join-Path $case 'Studio with spaces'
    $saved = Join-Path $case 'LocalAppData\T2T Campaign Studio\campaigns\Saved\campaign.txt'
    $outside = Join-Path $case 'outside\keep.txt'
    $protected = @((Join-Path $game 'Tape to Tape.exe'), (Join-Path $game 'GameAssembly.dll'),
        (Join-Path $game 'Tape to Tape_Data\keep.dat'), (Join-Path $game 'unrelated.txt'), $saved, $outside,
        (Join-Path $case 'LocalAppData\T2T Campaign Studio\library\heads\custom.png'))
    foreach ($path in $protected) { Write-Fixture $path "preserve $path" }
    if ($scenario -in @('running-game','late-game')) {
        Copy-Item -LiteralPath (Join-Path $env:SystemRoot 'System32\ping.exe') -Destination (Join-Path $game 'Tape to Tape.exe') -Force
    }
    $hashes = @{}
    foreach ($path in $protected) { $hashes[$path] = (Get-FileHash -LiteralPath $path).Hash }
    $arguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="' + $app + '"'),('/GAMEDIR="' + $game + '"'))
    if ($scenario -eq 'upgrade') {
        Check ((Run-Installer (Join-Path $fixture 'legacy.exe') ($arguments + ('/LOG="' + $case + '\legacy-install.log"'))) -eq 0) 'Legacy fixture installed'
    }
    Check ((Run-Installer (Join-Path $fixture 'current.exe') ($arguments + ('/LOG="' + $case + '\install.log"'))) -eq 0) "$scenario fixture installed"
    foreach ($name in @('BepInEx\interop\generated.dll','BepInEx\cache\cache.dat','BepInEx\config\custom.cfg','BepInEx\plugins\OtherMod.dll','BepInEx\LogOutput.log')) {
        Write-Fixture (Join-Path $game $name) 'generated or additional runtime file'
    }
    if ($scenario -eq 'missing-files') {
        # Only literal, freshly resolved files inside this fixture may be deleted.
        foreach ($name in $loaderFiles) { Remove-Item -LiteralPath (Assert-FixturePath (Join-Path $game $name)) -Force }
    }
    if ($scenario -eq 'junction') {
        New-Item -ItemType Junction -Path (Assert-FixturePath (Join-Path $game 'BepInEx\linked-content')) -Target (Split-Path $outside -Parent) | Out-Null
    }
    if ($scenario -in @('installed-junction','loader-junction-no-guard')) {
        $linkedNames = @('BepInEx\core','BepInEx\plugins','dotnet\host')
        if ($scenario -eq 'loader-junction-no-guard') { $linkedNames = @('BepInEx\core','dotnet\host') }
        foreach ($name in $linkedNames) {
            $original = Assert-FixturePath (Join-Path $game $name)
            $target = Assert-FixturePath (Join-Path $case ('outside-' + $name.Replace('\','-')))
            Move-Item -LiteralPath $original -Destination $target
            foreach ($file in Get-ChildItem -LiteralPath $target -File -Recurse) {
                $protected += $file.FullName
                $hashes[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName).Hash
            }
            New-Item -ItemType Junction -Path $original -Target $target | Out-Null
        }
    }
    $uninstaller = Join-Path $app 'unins000.exe'
    $uninstallArguments = @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/LOG="' + $case + '\uninstall.log"'))
    if ($scenario -eq 'loader-junction-no-guard') { $uninstallArguments += '/NOREDIRECTIONGUARD' }
    if ($scenario -eq 'late-game') {
        $gameExe = Join-Path $game 'Tape to Tape.exe'
        try {
            Check ((Run-Installer $uninstaller ($uninstallArguments + ('/LATEGAMEEXE="' + $gameExe + '"'))) -ne 0) 'Game launched after initialization blocks uninstall before deletion'
            Check (Test-Path -LiteralPath (Join-Path $game 'BepInEx\core\BepInEx.Core.dll')) 'Late-game refusal preserves the runtime'
            Check (Test-Path -LiteralPath (Join-Path $app 'T2T Campaign Studio.exe')) 'Late-game refusal preserves Studio'
        } finally {
            foreach ($process in @(Get-Process -Name 'Tape to Tape' -ErrorAction SilentlyContinue)) {
                if ($process.Path -eq $gameExe) { Stop-Process -Id $process.Id -Force; $process.WaitForExit() }
                $process.Dispose()
            }
        }
    }
    if ($scenario -eq 'running-game') {
        $running = Start-Process -FilePath (Join-Path $game 'Tape to Tape.exe') -ArgumentList @('-t','127.0.0.1') -WindowStyle Hidden -PassThru
        try {
            Check ((Run-Installer $uninstaller $uninstallArguments) -ne 0) 'Running game blocks the native uninstaller'
            Check (Test-Path -LiteralPath (Join-Path $game 'BepInEx\core\BepInEx.Core.dll')) 'Blocked uninstall preserves the runtime'
            Check (Test-Path -LiteralPath (Join-Path $app 'T2T Campaign Studio.exe')) 'Blocked uninstall preserves Studio'
        } finally {
            if (!$running.HasExited) { Stop-Process -Id $running.Id -Force; $running.WaitForExit() }
            $running.Dispose()
        }
    }
    # Deliberately supply no game-path argument to uninstall: it must use the
    # destination recorded at install time, including spaces and legacy upgrades.
    Check ((Run-Installer $uninstaller $uninstallArguments) -eq 0) "$scenario uninstall succeeded"
    foreach ($name in @('BepInEx','dotnet') + $loaderFiles) {
        Check (!(Test-Path -LiteralPath (Join-Path $game $name))) "$scenario removed $name"
    }
    Check (!(Test-Path -LiteralPath (Join-Path $app 'T2T Campaign Studio.exe'))) "$scenario removed Studio"
    foreach ($path in $protected) {
        Check ((Test-Path -LiteralPath $path) -and (Get-FileHash -LiteralPath $path).Hash -eq $hashes[$path]) "$scenario preserved $($path.Substring($case.Length + 1))"
    }
}
Write-Output "PASS: native uninstall cleanup, upgrade, missing files, junction, running-game guard, and preservation. Evidence: $fixture"
