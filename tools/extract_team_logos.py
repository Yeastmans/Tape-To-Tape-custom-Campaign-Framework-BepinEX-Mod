"""Extract team sprites directly from the installed bundle, without GPU readback."""
import hashlib
import json
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'artifacts/unity-asset-tools'))
import UnityPy
from UnityPy.classes.PPtr import PPtr
from UnityPy.helpers.TypeTreeGenerator import TypeTreeGenerator

game = Path(r'C:\Steam\steamapps\common\Tape to Tape')
metadata = ROOT / 'dump_2026-09-22_build25470389'
def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()
expected = {Path(v['Path']).name: v['Hash'].lower() for v in json.loads((metadata / 'source-hashes.json').read_text(encoding='utf-8-sig'))}
for path in (game / 'GameAssembly.dll', game / 'Tape to Tape_Data/il2cpp_data/Metadata/global-metadata.dat'):
    if expected[path.name] != sha(path):
        raise RuntimeError('Game changed: refresh decoder metadata before extraction')
bundle = game / 'Tape to Tape_Data/data.unity3d'
env = UnityPy.load(str(bundle))
generator = TypeTreeGenerator(next(iter(env.objects)).assets_file.unity_version)
generator.load_local_dll_folder(str(metadata / 'DummyDll'))
out = ROOT / 'assets/team-logos'
out.mkdir(parents=True, exist_ok=True)
entries = {}
for obj in env.objects:
    if obj.type.name != 'MonoBehaviour':
        continue
    header = obj.read(check_read=False)
    if not header.m_Script or header.m_Script.read().m_ClassName != 'TeamData':
        continue
    script = header.m_Script.read()
    data = obj.read_typetree(generator.get_nodes_up(script.m_AssemblyName, '.'.join(filter(None, [script.m_Namespace, script.m_ClassName]))))
    name = data.get('teamName', '')
    ref = data.get('logo')
    if not name or not ref or not ref.get('m_PathID'):
        continue
    sprite = PPtr(**ref, assetsfile=obj.assets_file).read()
    image = sprite.image.convert('RGBA')
    if not image.getbbox() or len(set(image.getdata())) < 2:
        raise RuntimeError('Blank logo: ' + name)
    filename = 'CCF_Team_' + hashlib.sha256(('logo\n' + name.strip().replace('\\', '/').lower()).encode()).hexdigest() + '.png'
    image.save(out / filename)
    entries[name] = dict(file=filename, sprite=sprite.m_Name, sha256=sha(out / filename))
if not entries:
    raise RuntimeError('No team logos extracted')
(out / 'index.json').write_text(json.dumps(dict(bundle_sha256=sha(bundle), teams=entries), indent=2), encoding='utf-8')
print(f'Extracted {len(entries)} nonblank team logos to {out}')
