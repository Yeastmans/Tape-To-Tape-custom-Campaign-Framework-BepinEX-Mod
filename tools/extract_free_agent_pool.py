"""Extract the native CampaignState.freeAgents pool from installed game assets."""
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
def decode(obj):
    script = obj.read(check_read=False).m_Script.read()
    return obj.read_typetree(generator.get_nodes_up(script.m_AssemblyName, '.'.join(filter(None, [script.m_Namespace, script.m_ClassName]))))
pools = []
for obj in env.objects:
    if obj.type.name != 'MonoBehaviour':
        continue
    header = obj.read(check_read=False)
    if not header.m_Script or header.m_Script.read().m_ClassName != 'CampaignState':
        continue
    data = decode(obj)
    players = []
    for ref in data['freeAgents']:
        player = decode(PPtr(**ref, assetsfile=obj.assets_file).deref())
        players.append({'name': (player['firstName'] + ' ' + player['lastName']).strip(), 'asset': player['m_Name']})
    pools.append({'asset': data['m_Name'], 'players': players})
if len(pools) != 1 or not pools[0]['players']:
    raise RuntimeError(f'Expected one populated CampaignState pool: {pools}')
report = dict(bundle_sha256=sha(bundle), source='State.CampaignState.freeAgents', pools=pools)
output = ROOT / 'artifacts/free-agent-pool'
output.mkdir(parents=True, exist_ok=True)
(output / 'catalog.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
names = sorted({p['name'] for p in pools[0]['players']})
(ROOT / 'assets/free-agents.txt').write_text('\n'.join(names) + '\n', encoding='utf-8')
print(json.dumps(report, indent=2))
