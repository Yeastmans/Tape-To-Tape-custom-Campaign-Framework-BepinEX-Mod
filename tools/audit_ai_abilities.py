"""Record native hockey AI calls and metadata references for the pinned game build."""
import bisect
import hashlib
import json
import re
import struct
from pathlib import Path
import capstone

ROOT = Path(__file__).resolve().parents[1]
META = ROOT / 'dump_2026-09-22_build25470389'
binary = Path(r'C:\Steam\steamapps\common\Tape to Tape\GameAssembly.dll').read_bytes()
fingerprints = json.loads((META / 'source-hashes.json').read_text(encoding='utf-8-sig'))
assert hashlib.sha256(binary).hexdigest() == next(x['Hash'].lower() for x in fingerprints if x['Path'].endswith('GameAssembly.dll'))
script = json.loads((META / 'script.json').read_text())
names = {}
for m in script['ScriptMethod']:
    names.setdefault(m['Address'], []).append(m['Name'])
starts = sorted(names)
metadata = {m['Address']: m['Name'] for k in ('ScriptMetadata', 'ScriptMetadataMethod') for m in script.get(k, [])}
decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
decoder.detail = True
results = {}
for start in starts:
    owners = names[start]
    if not any(n.startswith(('AI.', 'Skaters.', 'Rogue.Powerups.Controllers.Abilities.', 'Movements.')) for n in owners):
        continue
    if not 0x3600000 <= start < 0x4500000:
        continue
    end = starts[bisect.bisect_right(starts, start)]
    refs = set()
    for ins in decoder.disasm(binary[start-0x1600:end-0x1600], start):
        if ins.mnemonic in ('call', 'jmp') and ins.operands[0].type == capstone.x86.X86_OP_IMM:
            refs.update(names.get(ins.operands[0].imm, []))
        for operand in ins.operands:
            if operand.type == capstone.x86.X86_OP_MEM and operand.mem.base == capstone.x86.X86_REG_RIP:
                name = metadata.get(ins.address + ins.size + operand.mem.disp)
                if name:
                    refs.add(name)
    relevant = sorted(refs)
    if relevant:
        results['; '.join(owners)] = relevant
out = ROOT / 'artifacts/ai-abilities'
out.mkdir(parents=True, exist_ok=True)
(out / 'native-references.json').write_text(json.dumps(results, indent=2))
print(f'Recorded {len(results)} native methods in {out}')
