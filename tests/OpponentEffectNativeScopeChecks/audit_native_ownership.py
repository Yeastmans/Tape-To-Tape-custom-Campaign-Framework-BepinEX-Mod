"""Read-only address audit against the reviewed current-build native dump.

Arguments: native-dump/script.json reward-address-map.json [output.json].
The reward map is the existing 33-entry compatibility review artifact.
"""
import json
import pathlib
import re
import sys

root = pathlib.Path(__file__).resolve().parents[2]
source = (root / 'src/CampaignCore/OpponentEffects.cs').read_text(encoding='utf-8-sig')
consumers = sorted({c for text in re.findall(r'consumers: "([^"]+)"', source) for c in text.split(';')})
raw = json.loads((root / 'src/CustomCampaignFramework/Assets/jersey-logo-targets.json').read_text(encoding='utf-8-sig'))
profile = json.loads((root / 'src/CustomCampaignFramework/Assets/native-compatibility.json').read_text(encoding='utf-8-sig'))
native = json.loads(pathlib.Path(sys.argv[1]).read_text(encoding='utf-8-sig'))['ScriptMethod']
rewards = json.loads(pathlib.Path(sys.argv[2]).read_text(encoding='utf-8-sig'))
assert len(rewards) == 33 and len(raw) == 21

def key(name):
    name = name.replace('$$', '.').replace('/', '.').replace('+', '.').replace('<', '_').replace('>', '_')
    return re.sub(r'(?<=d__)\d+|(?<=DisplayClass)\d+_\d+|(?<=\|)\d+_\d+', '#', name)

entries = {}
for method in native:
    entries.setdefault(key(method['Name']), []).append(method)
owners = {int(value, 16): 'reward' for value in rewards.values()}
owners.update({c['Rva']: 'jersey/' + c['Id'] for c in raw})
owners[profile['SurfaceRva']] = 'surface'
rows = []
for consumer in consumers:
    matches = entries.get(key(consumer), [])
    assert matches, 'Unresolved native consumer: ' + consumer
    rows.append({'consumer': consumer, 'native': [{'name': m['Name'], 'rva': hex(m['Address']),
                 'owner': owners.get(m['Address'])} for m in matches]})
collisions = [(row['consumer'], m['owner']) for row in rows for m in row['native'] if m['owner']]
assert sorted(owner for _, owner in collisions) == ['jersey/2-move', 'jersey/7-move'], collisions
result = {'gameHash': profile['GameHash'], 'consumers': len(consumers), 'nativeMethods': sum(len(row['native']) for row in rows),
          'rawAddresses': len(owners), 'collisions': collisions, 'rows': rows}
if len(sys.argv) > 3:
    pathlib.Path(sys.argv[3]).write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
print(json.dumps({k: v for k, v in result.items() if k != 'rows'}, indent=2))
