"""Read the installed Unity bundle and export complete Spine skin inventories.

Uses the workspace UnityPy and Spine runtime dependencies; never launches or
modifies the game. See tools/EQUIPMENT_CATALOG.md for usage and scope.
"""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game-data', type=Path, default=Path(r'C:\Steam\steamapps\common\Tape to Tape\Tape to Tape_Data'))
    parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/equipment-catalog')
    parser.add_argument('--unitypy', type=Path, default=ROOT / 'artifacts/unity-asset-tools')
    parser.add_argument('--node', type=Path, default=Path.home() / '.cache/codex-runtimes/codex-primary-runtime/dependencies/node/bin/node.exe')
    parser.add_argument('--observed', type=Path, help='Existing _game_skins.txt to compare (defaults to installed mod dump)')
    args = parser.parse_args()
    sys.path.insert(0, str(args.unitypy.resolve()))
    import UnityPy

    bundle = args.game_data.resolve() / 'data.unity3d'
    output = args.output.resolve()
    source = output / 'source'
    source.mkdir(parents=True, exist_ok=True)
    with bundle.open('rb') as stream:
        fingerprint = hashlib.file_digest(stream, 'sha256').hexdigest()
    env = UnityPy.load(str(bundle))
    entries = []
    # Scan the entire bundle. Do not rely on fixed Unity path IDs, loaded player
    # presets, unlocked items, or the menu's currently instantiated characters.
    for obj in env.objects:
        if obj.type.name != 'TextAsset':
            continue
        data = obj.read()
        suffix = Path(data.m_Name).suffix.lower()
        if suffix not in ('.skel', '.atlas'):
            continue
        raw = data.m_Script
        if isinstance(raw, str):
            raw = raw.encode('utf-8', errors='surrogateescape')
        filename = f'{obj.assets_file.name}-{obj.path_id}{suffix}'
        (source / filename).write_bytes(raw)
        entries.append(dict(file=obj.assets_file.name, id=obj.path_id,
                            name=data.m_Name, local=filename,
                            sha256=hashlib.sha256(raw).hexdigest()))
    if not any(e['local'].endswith('.skel') for e in entries):
        raise RuntimeError('No Spine skeletons found; no catalog generated.')
    observed = args.observed or args.game_data.parent / 'BepInEx/plugins/Custom Campaigns Mod/_game_skins.txt'
    manifest = dict(schema=1, bundle=str(bundle), sha256=fingerprint,
                    observed=str(observed.resolve()) if observed.exists() else None,
                    assets=sorted(entries, key=lambda e: (e['file'], e['id'])))
    (source / 'index.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
    subprocess.run([str(args.node), str(ROOT / 'tools/read_equipment_skins.mjs'), str(output)], check=True)


if __name__ == '__main__':
    main()
