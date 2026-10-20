"""Instruction-verified native team-classifier callers for the pinned build."""
import bisect
import hashlib
import json
from pathlib import Path
import re
import struct
import capstone
import argparse

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--metadata', type=Path, default=ROOT / 'dump_2026-09-22_build25470389')
parser.add_argument('--output', type=Path, default=ROOT / 'artifacts/opponent-effects')
args = parser.parse_args()
META = args.metadata
binary = Path(r'C:\Steam\steamapps\common\Tape to Tape\GameAssembly.dll').read_bytes()
fingerprints = json.loads((META / 'source-hashes.json').read_text(encoding='utf-8-sig'))
assert hashlib.sha256(binary).hexdigest() == next(x['Hash'].lower() for x in fingerprints if x['Path'].endswith('GameAssembly.dll'))
script = json.loads((META / 'script.json').read_text())
names = {}
for method in script['ScriptMethod']:
    names.setdefault(method['Address'], []).append(method['Name'])
starts = sorted(names)
classifiers = set(re.findall(r'"(Is\w+)"', (ROOT / 'src/CampaignCore/OpponentOptions.cs').read_text()))
targets = {a: ns for a, ns in names.items() if any(n.split('$$')[0] in ('Data.TeamData', 'Tape2Tape.Team') and n.split('$$')[-1] in classifiers for n in ns)}
pe = struct.unpack_from('<I', binary, 0x3c)[0]
count = struct.unpack_from('<H', binary, pe + 6)[0]
optional_size = struct.unpack_from('<H', binary, pe + 20)[0]
decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
verified, rows = {}, []
for index in range(count):
    offset = pe + 24 + optional_size + 40 * index
    _, rva, size, raw = struct.unpack_from('<IIII', binary, offset + 8)
    if not struct.unpack_from('<I', binary, offset + 36)[0] & 0x20000000:
        continue
    code = binary[raw:raw + size]
    for hit in re.finditer(b'[\xe8\xe9]', code):
        at = hit.start()
        pc = rva + at
        if at + 5 > size:
            continue
        dest = pc + 5 + struct.unpack_from('<i', code, at + 1)[0]
        if dest not in targets:
            continue
        i = bisect.bisect_right(starts, pc) - 1
        start, end = starts[i:i + 2]
        if start not in verified:
            verified[start] = {x.address for x in decoder.disasm(code[start-rva:end-rva], start) if x.mnemonic in ('call', 'jmp')}
        if pc in verified[start]:
            rows.append(dict(classifiers=targets[dest], callers=names[start], method_rva=hex(start), instruction_rva=hex(pc)))
output = args.output
output.mkdir(parents=True, exist_ok=True)
(output / 'native-callers.json').write_text(json.dumps(rows, indent=2))
source = (ROOT / 'src/CampaignCore/OpponentEffects.cs').read_text(encoding='utf-8')
def stable(name):
    return re.sub(r'(?<=\|)\d+_\d+', '#', re.sub(r'(?<=d__)\d+|(?<=DisplayClass)\d+_\d+', '#', name.replace('/', '.')))
routed, unmatched, shared = set(), [], []
route_count = 0
for line in source.splitlines():
    if 'new OpponentEffect(' not in line:
        continue
    key = re.search(r'new OpponentEffect\("([^"]+)', line)[1]
    classifier = re.search(r'classifier: "([^"]+)', line)[1]
    for consumer in re.search(r'consumers: "([^"]+)', line)[1].split(';'):
        owner, method = consumer.rsplit('.', 1)
        native = owner.replace('/', '.') + '$$' + method
        routed.add(stable(native))
        hits = [r for r in rows if stable(native) in map(stable, r['callers']) and any(n.endswith('$$' + classifier) for n in r['classifiers'])]
        if not hits:
            unmatched.append([key, consumer, classifier])
        route_count += 1
for address, owners in names.items():
    if any(stable(n) in routed for n in owners) and len(owners) > 1:
        shared.append([hex(address), owners])
other = sorted({n for r in rows for n in r['callers'] if stable(n) not in routed and not n.startswith(('Tape2Tape.Team$$Is', 'Data.TeamData$$Is'))})
(output / 'route-audit.json').write_text(json.dumps(dict(routes=route_count, unmatched=unmatched, shared_addresses=shared, other_consumers=other), indent=2))
assert not unmatched and not shared, (unmatched, shared)
print(f'{len(rows)} verified call sites; {route_count} configured routes; no unmatched routes or shared patch targets.')
