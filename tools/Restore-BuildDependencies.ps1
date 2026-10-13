# Restore pinned redistributable build inputs. Does not install software or delete files.
[CmdletBinding()]
param(
    [string]$SevenZipPath = '',
    [switch]$VerifyOnly
)
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$repository = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$manifest = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'build-dependencies.json') | ConvertFrom-Json
if ($manifest.schema -ne 1) { throw 'Unsupported build dependency manifest.' }

function Assert-PlainPath([string]$Path) {
    $part = [IO.Path]::GetFullPath($Path)
    while ($part) {
        if ((Test-Path -LiteralPath $part) -and ((Get-Item -LiteralPath $part -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Build dependencies cannot use linked paths: $part"
        }
        $part = Split-Path $part -Parent
    }
}
function Repository-Path([string]$Relative) {
    if ([IO.Path]::IsPathRooted($Relative) -or $Relative -match '(^|[/\\])\.\.([/\\]|$)' -or $Relative.Contains(':')) {
        throw "Invalid dependency path: $Relative"
    }
    $path = [IO.Path]::GetFullPath((Join-Path $repository $Relative))
    if (!$path.StartsWith($repository.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw "Path is outside the repository: $path" }
    Assert-PlainPath $path
    return $path
}
function Require-Hash([string]$Path, [string]$Expected) {
    if ($Expected -notmatch '^[0-9A-Fa-f]{64}$' -or !(Test-Path -LiteralPath $Path -PathType Leaf) -or
        (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash -ne $Expected) {
        throw "Dependency hash mismatch or missing file: $Path. Existing files were not replaced."
    }
}
function Find-SevenZip {
    if ($SevenZipPath) {
        if (!(Test-Path -LiteralPath $SevenZipPath -PathType Leaf)) { throw "7-Zip executable not found: $SevenZipPath" }
        return [IO.Path]::GetFullPath($SevenZipPath)
    }
    $command = Get-Command 7z.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }
    foreach ($base in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if (!$base) { continue }
        $candidate = Join-Path $base '7-Zip/7z.exe'
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    throw 'Install 7-Zip, add 7z.exe to PATH, or pass -SevenZipPath. No dependencies were deleted.'
}

# Inspect all destinations before downloading or writing anything. A different existing
# dependency is an explicit conflict, never permission to overwrite a developer's files.
$pending = @()
foreach ($dependency in $manifest.dependencies) {
    if ($dependency.destination -notin @('installer/bepinex', 'tools/ffmpeg')) { throw 'Unexpected dependency destination.' }
    if ($dependency.url -notmatch '^https://' -or $dependency.sha256 -notmatch '^[0-9A-Fa-f]{64}$') { throw 'Invalid dependency download identity.' }
    $missing = $false
    foreach ($file in $dependency.files) {
        $target = Repository-Path ($dependency.destination + '/' + $file.path)
        # Check archive paths too, before passing them to an extractor.
        $null = Repository-Path ('build/dependencies/extracted/' + $file.archivePath)
        if (Test-Path -LiteralPath $target) { Require-Hash $target $file.sha256 }
        else { $missing = $true }
    }
    if ($missing) { $pending += $dependency }
    else { Write-Output "$($dependency.name) $($dependency.version): all $(@($dependency.files).Count) pinned files match." }
}
if ($VerifyOnly) {
    if ($pending.Count) { throw ('Missing dependencies: ' + (($pending | ForEach-Object name) -join ', ') + '. Run Restore-BuildDependencies.ps1 without -VerifyOnly.') }
    return
}
if (!$pending.Count) { return }

$sevenZip = $null
if (@($pending | Where-Object format -eq '7z').Count) { $sevenZip = Find-SevenZip }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$cache = Repository-Path 'build/dependencies'
[IO.Directory]::CreateDirectory($cache) | Out-Null
foreach ($dependency in $pending) {
    $archive = Repository-Path ('build/dependencies/' + $dependency.archive)
    if (!(Test-Path -LiteralPath $archive)) {
        $download = Repository-Path ('build/dependencies/' + $dependency.archive + '.' + [Guid]::NewGuid().ToString('N') + '.download')
        Write-Output "Downloading $($dependency.name) $($dependency.version)..."
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -UseBasicParsing -Uri $dependency.url -OutFile $download
        Require-Hash $download $dependency.sha256
        # Both paths are checked repository paths. Keep failed downloads for inspection.
        Move-Item -LiteralPath $download -Destination $archive
    }
    Require-Hash $archive $dependency.sha256
    $stagingRelative = 'build/dependencies/extracted-' + [Guid]::NewGuid().ToString('N')
    $staging = Repository-Path $stagingRelative
    [IO.Directory]::CreateDirectory($staging) | Out-Null
    if ($dependency.format -eq 'zip') {
        $zip = [IO.Compression.ZipFile]::OpenRead($archive)
        try {
            foreach ($file in $dependency.files) {
                $entry = $zip.GetEntry($file.archivePath)
                if (!$entry) { throw "Missing archive entry: $($file.archivePath)" }
                $extracted = Repository-Path ($stagingRelative + '/' + $file.archivePath)
                [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($extracted)) | Out-Null
                [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $extracted, $false)
            }
        } finally { $zip.Dispose() }
    } elseif ($dependency.format -eq '7z') {
        $extractArgs = @('x', $archive, ('-o' + $staging), '-y', '--') + @($dependency.files | ForEach-Object archivePath)
        & $sevenZip @extractArgs | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "7-Zip extraction failed: $($dependency.name)" }
    } else { throw "Unsupported archive format: $($dependency.format)" }
    # Validate the complete extracted payload before copying any dependency into source.
    foreach ($file in $dependency.files) {
        Require-Hash (Repository-Path ($stagingRelative + '/' + $file.archivePath)) $file.sha256
    }
    foreach ($file in $dependency.files) {
        $target = Repository-Path ($dependency.destination + '/' + $file.path)
        if (Test-Path -LiteralPath $target) { Require-Hash $target $file.sha256; continue }
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target)) | Out-Null
        [IO.File]::Copy((Repository-Path ($stagingRelative + '/' + $file.archivePath)), $target, $false)
        Require-Hash $target $file.sha256
    }
    Write-Output "$($dependency.name) restored and verified. Archive/extraction retained under build/dependencies."
}
