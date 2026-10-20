"""Find instruction-verified references to base squad ID literals in GameAssembly."""
import bisect
import hashlib
import json
from pathlib import Path
import re
import struct
import capstone

ROOT = Path(__file__).resolve().parents[1]
META = ROOT / 'dump_2026-09-22_build25470389'
GAME = Path(r'C:\Steam\steamapps\common\Tape to Tape\GameAssembly.dll')
OUTPUT = ROOT / 'artifacts/squad-rules'
binary = GAME.read_bytes()
pe = struct.unpack_from('<I', binary, 0x3c)[0]
count = struct.unpack_from('<H', binary, pe + 6)[0]
optional_size = struct.unpack_from('<H', binary, pe + 20)[0]
sections = []
for i in range(count):
    offset = pe + 24 + optional_size + i * 40
    size, rva, raw_size, raw_offset = struct.unpack_from('<IIII', binary, offset + 8)
    flags = struct.unpack_from('<I', binary, offset + 36)[0]
    sections.append((rva, raw_offset, raw_size, flags))
catalog = json.loads((OUTPUT / 'catalog.json').read_text())
if hashlib.sha256(binary).hexdigest() != catalog['game_sha256']:
    raise RuntimeError('GameAssembly changed since the catalog export')
fingerprints = json.loads((META / 'source-hashes.json').read_text(encoding='utf-8-sig'))
if not any(Path(v['Path']).name == 'GameAssembly.dll' and v['Hash'].lower() == catalog['game_sha256'] for v in fingerprints):
    raise RuntimeError('Code metadata does not match the extracted catalog')
ids = {catalog['objects'][k]['data']['id'] for k in catalog['squads']}
literals = {int(x['address'], 16): x['value'] for x in json.loads((META / 'stringliteral.json').read_text()) if x['value'] in ids}
methods = json.loads((META / 'script.json').read_text())['ScriptMethod']
by_address = {}
for method in methods:
    by_address.setdefault(method['Address'], []).append(method['Name'])
starts = sorted(by_address)
decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
verified = {}
results = {id: [] for id in sorted(ids)}
for rva, raw_offset, raw_size, flags in sections:
    if not flags & 0x20000000:
        continue
    code = binary[raw_offset:raw_offset + raw_size]
    # RIP-relative MOV/LEA with any common REX prefix. Verify boundaries by
    # decoding from the containing method start, never just a byte-pattern hit.
    for match in re.finditer(rb'[\x48-\x4f][\x8b\x8d][\x05\x0d\x15\x1d\x25\x2d\x35\x3d].{4}', code, re.S):
        pc = rva + match.start()
        target = pc + 7 + struct.unpack_from('<i', code, match.start() + 3)[0]
        if target not in literals:
            continue
        index = bisect.bisect_right(starts, pc) - 1
        if index < 0:
            continue
        start = starts[index]
        end = starts[index + 1] if index + 1 < len(starts) else pc + 16
        if not rva <= start < rva + raw_size:
            continue
        if start not in verified:
            verified[start] = {i.address: f'{i.mnemonic} {i.op_str}' for i in decoder.disasm(code[start-rva:end-rva], start)}
        if pc not in verified[start]:
            continue
        results[literals[target]].append(dict(methods=by_address[start], method_rva=hex(start), instruction_rva=hex(pc), instruction=verified[start][pc]))
(OUTPUT / 'code-references.json').write_text(json.dumps(results, indent=2))
for id, refs in results.items():
    print(id + ': ' + '; '.join(sorted({m for r in refs for m in r['methods']})))
