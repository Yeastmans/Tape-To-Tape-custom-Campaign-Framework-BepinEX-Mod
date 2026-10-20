"""Package self-contained dotnet publishes with Unix executable permissions."""
import hashlib
import io
import plistlib
import tarfile
from pathlib import Path

root = Path(__file__).resolve().parents[1]
output = root / 'artifacts' / 'Installer-ready'
output.mkdir(parents=True, exist_ok=True)
receipts = []
for rid in ('osx-arm64', 'osx-x64', 'linux-x64'):
    source = root / 'artifacts' / f'CampaignStudio-{rid}'
    executable = source / 'T2T Campaign Studio'
    if not executable.is_file():
        raise RuntimeError(f'Missing publish: {source}')
    mac = rid.startswith('osx')
    package = output / f'T2T_Campaign_Studio_{rid}.tar.gz'
    prefix = 'T2T Campaign Studio.app/Contents/MacOS' if mac else 'T2T Campaign Studio'
    readme = '''T2T Campaign Studio - native editor

Includes the latest centered map preview. Self-contained: no .NET install required.
User campaigns and library live in the operating system's LocalApplicationData
folder under "T2T Campaign Studio", separate from the application.

Audio import requires FFmpeg installed on PATH (on Mac, Homebrew's default
locations are also searched). Audio playback preview currently requires Windows.
Native game export paths follow Unity's platform conventions. If your game uses
another save location, set T2T_GAME_SAVE_PATH to the game's save folder containing
TeamDataModels and CustomLogos before starting the editor. This is necessary for
Linux running the Windows game through Proton; select its prefix's AppData/LocalLow/
Excellent Rectangle/Tape to Tape folder. No old content is erased or auto-migrated.

These packages contain the editor, not a native BepInEx/mod installer. Native mod
integration and running the editor on macOS/Linux have not been tested on this
Windows build machine. Build and archive checks are not gameplay verification.
Unity save path reference:
https://docs.unity3d.com/ScriptReference/Application-persistentDataPath.html
'''
    readme += ('''
macOS: extract the archive, then drag T2T Campaign Studio.app to Applications.
Choose osx-arm64 for Apple Silicon or osx-x64 for Intel. This app is unsigned and
not notarized; macOS may require approval in Privacy & Security before opening.
''' if mac else '''
Linux x64: extract the archive and run ./install.sh [destination-folder].
Default destination: ~/.local/opt/t2t-campaign-studio. A desktop menu entry is
created. Alternatively run "./T2T Campaign Studio/T2T Campaign Studio" directly.
Requires a desktop session with X11/XWayland and fontconfig installed.
''')
    with tarfile.open(package, 'w:gz') as archive:
        def add_text(name, data, mode=0o644):
            if isinstance(data, str):
                data = data.encode('utf-8')
            info = tarfile.TarInfo(name)
            info.size, info.mode = len(data), mode
            archive.addfile(info, io.BytesIO(data))
        for path in sorted(source.rglob('*')):
            if not path.is_file():
                continue
            relative = path.relative_to(source)
            if relative.parts[0] == 'audio-tools':
                continue  # Windows-only converter must not enter a native package.
            info = archive.gettarinfo(str(path), f'{prefix}/{relative.as_posix()}')
            info.mode = 0o755 if path == executable or path.suffix in ('.so', '.dylib') else 0o644
            info.uid = info.gid = 0
            info.uname = info.gname = ''
            with path.open('rb') as stream:
                archive.addfile(info, stream)
        add_text('README.txt', readme)
        if mac:
            add_text('T2T Campaign Studio.app/Contents/Info.plist', plistlib.dumps({
                'CFBundleExecutable': 'T2T Campaign Studio',
                'CFBundleName': 'T2T Campaign Studio',
                'CFBundleIdentifier': 'com.t2t.campaignstudio',
                'CFBundlePackageType': 'APPL',
                'CFBundleVersion': '1',
                'NSHighResolutionCapable': True,
            }))
        else:
            add_text('install.sh', '''#!/bin/sh
set -eu
source_dir=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
dest=${1:-"$HOME/.local/opt/t2t-campaign-studio"}
case "$dest" in /*) ;; *) dest="$PWD/$dest" ;; esac
mkdir -p "$dest"
cp -R "$source_dir/T2T Campaign Studio/." "$dest/"
chmod +x "$dest/T2T Campaign Studio"
applications="${XDG_DATA_HOME:-$HOME/.local/share}/applications"
mkdir -p "$applications"
escaped=$(printf '%s' "$dest" | sed 's/\\\\/\\\\\\\\/g; s/"/\\\\"/g; s/`/\\\\`/g; s/\$/\\\\$/g; s/%/%%/g')
printf '[Desktop Entry]\\nType=Application\\nName=T2T Campaign Studio\\nExec="%s/T2T Campaign Studio"\\nTerminal=false\\nCategories=Game;\\n' "$escaped" > "$applications/t2t-campaign-studio.desktop"
printf 'Installed to %s\\n' "$dest"
''', 0o755)
    with tarfile.open(package) as archive:
        member = archive.getmember(f'{prefix}/T2T Campaign Studio')
        assert member.mode & 0o111
        assert archive.extractfile(member).read() == executable.read_bytes()
        assert not any(m.name.endswith('ffmpeg.exe') for m in archive)
    digest = hashlib.file_digest(package.open('rb'), 'sha256').hexdigest()
    receipts.append(f'{digest}  {package.name}')
    print(f'Verified {package.name}: {package.stat().st_size:,} bytes')
(output / 'SHA256-unix.txt').write_text('\n'.join(receipts) + '\n', encoding='utf-8')
