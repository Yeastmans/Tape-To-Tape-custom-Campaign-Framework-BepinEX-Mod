"""Decode every TeamData / ForwardData / GoaltenderData shipped in the game bundle.

Reads the installed game files only (no runtime, no campaign). Writes
artifacts/native-rosters/native.json: teams with roster references resolved to
player records. Requires an Il2CppDumper output matching the installed build.
"""
import hashlib, json, sys
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'artifacts/unity-asset-tools'))
import UnityPy
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

game = Path(r'C:\Steam\steamapps\common\Tape to Tape')
metadata = ROOT / (sys.argv[1] if len(sys.argv) > 1 else 'dump_2026-09-24_build25494871')
bundle = game / 'Tape to Tape_Data/data.unity3d'
expected = {Path(v['Path']).name: v['Hash'].lower() for v in json.loads((metadata / 'source-hashes.json').read_text(encoding='utf-8-sig'))}
def sha(path):
    with path.open('rb') as f: return hashlib.file_digest(f, 'sha256').hexdigest()
for path in (game / 'GameAssembly.dll', game / 'Tape to Tape_Data/il2cpp_data/Metadata/global-metadata.dat'):
    assert sha(path) == expected[path.name], 'Decoder metadata does not match the installed game build'
env = UnityPy.load(str(bundle))
generator = TypeTreeGenerator(next(iter(env.objects)).assets_file.unity_version)
generator.load_local_dll_folder(str(metadata / 'DummyDll'))
KINDS = ('TeamData', 'ForwardData', 'GoaltenderData')
records, index = {}, {}
def target(owner, v):
    """Resolve a PPtr to 'assetsfile:pathid' using the owner file's external table."""
    if not v or not v.get('m_PathID'): return None
    fid = v['m_FileID']
    if fid == 0: name = owner.name
    elif 0 < fid <= len(owner.externals): name = Path(owner.externals[fid - 1].path).name
    else: name = f'ext{fid}'  # script references and other non-file ids
    return f'{name}:{v["m_PathID"]}'
def plain(v, owner):
    if isinstance(v, dict):
        if set(v) == {'m_FileID', 'm_PathID'}:
            ref = target(owner, v)
            return {'ref': ref} if ref else None
        return {k: plain(x, owner) for k, x in v.items()}
    if isinstance(v, list): return [plain(x, owner) for x in v]
    return v
def script_of(obj):
    header = obj.read(check_read=False)
    return header.m_Script.read() if header.m_Script else None
def decode(obj, script=None):
    script = script or script_of(obj)
    if script is None: return None, None
    tree = generator.get_nodes_up(script.m_AssemblyName, '.'.join(filter(None, [script.m_Namespace, script.m_ClassName])))
    try: return script.m_ClassName, obj.read_typetree(tree)
    except Exception as e: return script.m_ClassName, {'_error': str(e)}
for obj in env.objects:
    if obj.type.name != 'MonoBehaviour': continue
    index[f'{obj.assets_file.name}:{obj.path_id}'] = obj
    script = script_of(obj)
    if script is None or script.m_ClassName not in KINDS: continue
    cls, data = decode(obj, script)
    assert '_error' not in data, (cls, obj.path_id, data)
    data = plain(data, obj.assets_file); data['_class'] = cls
    records[f'{obj.assets_file.name}:{obj.path_id}'] = data
# Talents, abilities, bench powers and relics are separate assets: keep their ids.
wanted = set()
for r in records.values():
    for f in ('powerups', 'relics'):
        wanted.update(x['ref'] for x in r.get(f) or [] if x)
    for f in ('ability', 'benchPower'):
        if r.get(f): wanted.add(r[f]['ref'])
refs = {}
for k in sorted(wanted):
    obj = index.get(k)
    if obj is None: refs[k] = None; continue
    cls, data = decode(obj)
    refs[k] = {'_class': cls, 'id': (data or {}).get('id'), 'm_Name': (data or {}).get('m_Name')}
out = ROOT / 'artifacts/native-rosters/native.json'
out.write_text(json.dumps({'bundle_sha256': sha(bundle), 'decoder': metadata.name, 'records': records, 'refs': refs}, indent=1, ensure_ascii=False), encoding='utf-8')
from collections import Counter
print(Counter(r['_class'] for r in records.values()), '->', out)
