"""Export base squad assets and linked gameplay settings without launching the game."""
import argparse
import hashlib
import json
import re
import struct
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--game', type=Path, default=Path(r'C:\Steam\steamapps\common\Tape to Tape'))
    p.add_argument('--metadata', type=Path, default=ROOT / 'dump_2026-09-22_build25470389')
    p.add_argument('--output', type=Path, default=ROOT / 'artifacts/squad-rules')
    p.add_argument('--include-match-data', action='store_true', help='Also audit native match settings, restrictions and match blueprints')
    p.add_argument('--include-teams', action='store_true', help='Also audit all native TeamData assets')
    args = p.parse_args()
    def sha(path):
        with path.open('rb') as stream:
            return hashlib.file_digest(stream, 'sha256').hexdigest()
    fingerprints = json.loads((args.metadata / 'source-hashes.json').read_text(encoding='utf-8-sig'))
    expected = {Path(v['Path']).name: v['Hash'].lower() for v in fingerprints}
    for path in (args.game / 'GameAssembly.dll', args.game / 'Tape to Tape_Data/il2cpp_data/Metadata/global-metadata.dat'):
        if expected.get(path.name) != sha(path):
            raise RuntimeError('Game changed: regenerate metadata and source-hashes.json before extraction')
    sys.path.insert(0, str(ROOT / 'artifacts/unity-asset-tools'))
    import UnityPy
    from UnityPy.classes.PPtr import PPtr
    from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

    bundle = args.game / 'Tape to Tape_Data/data.unity3d'
    env = UnityPy.load(str(bundle))
    generator = TypeTreeGenerator(next(iter(env.objects)).assets_file.unity_version)
    generator.load_local_dll_folder(str(args.metadata / 'DummyDll'))
    objects, errors = {}, []

    def decode(obj):
        key = f'{obj.assets_file.name}:{obj.path_id}'
        if key in objects:
            return objects[key]
        header = obj.read(check_read=False)
        script = header.m_Script.read() if obj.type.name == 'MonoBehaviour' else None
        if script:
            name = '.'.join(filter(None, [script.m_Namespace, script.m_ClassName]))
            tree = generator.get_nodes_up(script.m_AssemblyName, name)
            data = obj.read_typetree(tree)
        else:
            name, data = obj.type.name, obj.read_typetree()
        record = dict(key=key, type=name, name=data.get('m_Name', ''), data={})
        objects[key] = record

        def resolve(value, field=''):
            if isinstance(value, dict):
                if set(value) == {'m_FileID', 'm_PathID'}:
                    if not value['m_PathID']:
                        return None
                    if field in ('m_Script', 'm_GameObject'):
                        return value
                    try:
                        target = PPtr(**value, assetsfile=obj.assets_file).deref()
                        h = target.read(check_read=False)
                        ref = dict(ref=f'{target.assets_file.name}:{target.path_id}', name=getattr(h, 'm_Name', ''), type=target.type.name)
                        # Export every referenced gameplay ScriptableObject. Do not traverse
                        # visual prefab hierarchies, textures or scripts.
                        if target.type.name == 'MonoBehaviour':
                            linked = decode(target)
                            ref['type'] = linked['type']
                        return ref
                    except Exception as ex:
                        errors.append(dict(object=key, field=field, pointer=value, error=str(ex)))
                        return dict(unresolved=value)
                return {k: resolve(v, k) for k, v in value.items()}
            if isinstance(value, list):
                return [resolve(v, field) for v in value]
            return value
        record['data'] = resolve(data)
        return record

    squads, english, extra_roots = [], {}, []
    for obj in env.objects:
        if obj.type.name != 'MonoBehaviour':
            continue
        header = obj.read(check_read=False)
        if header.m_Script and header.m_Script.read().m_Name == 'RunSquadScriptableObject':
            squads.append(decode(obj))
        script_name = header.m_Script.read().m_Name if header.m_Script else ''
        if ((args.include_teams and script_name == 'TeamData') or
            (args.include_match_data and script_name in ('InGameData', 'GameRestrictionData', 'TimerRuleStateData',
             'EndOfGameConditionData', 'ChallengeBlueprint', 'EliteBlueprint', 'BossBlueprint'))):
            extra_roots.append(decode(obj)['key'])
        if header.m_Script and header.m_Script.read().m_Name == 'LanguageSourceAsset':
            # Read only named squad terms from the serialized TermData records.
            # After the aligned term string: term type, language count, English.
            raw = obj.get_raw_data()
            for hit in re.finditer(rb'squads/[a-z]+/(?:desc|name|unlock)', raw):
                start, end = hit.start(), hit.end()
                if start < 4 or struct.unpack_from('<i', raw, start - 4)[0] != end - start:
                    continue
                aligned = (end + 3) & ~3
                term_type, languages, size = struct.unpack_from('<iii', raw, aligned)
                if term_type != 0 or not 1 <= languages <= 100 or not 0 <= size <= 10000:
                    raise ValueError('Unexpected localization structure')
                english[hit.group().decode()] = raw[aligned+12:aligned+12+size].decode('utf-8')
    if not squads:
        raise RuntimeError('No base squads decoded')
    args.output.mkdir(parents=True, exist_ok=True)
    result = dict(schema=1, bundle=str(bundle), bundle_sha256=sha(bundle),
                  game_sha256=sha(args.game / 'GameAssembly.dll'),
                  metadata_sha256=sha(args.game / 'Tape to Tape_Data/il2cpp_data/Metadata/global-metadata.dat'),
                  decoder_metadata=str(args.metadata), squads=[s['key'] for s in squads], extra_roots=extra_roots, objects=objects, english=english, errors=errors)
    (args.output / 'catalog.json').write_text(json.dumps(result, indent=2), encoding='utf-8')
    print(f'{len(squads)} squads; {len(objects)} linked objects; {len(errors)} unresolved references', flush=True)
    print(f'{len(english)} localized squad terms extracted')


if __name__ == '__main__':
    main()
