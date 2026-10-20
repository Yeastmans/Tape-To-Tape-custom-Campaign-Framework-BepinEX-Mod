"""Read-only native appearance audit for the installed, fingerprinted game."""
import bisect
import hashlib
import json
from pathlib import Path
import capstone

ROOT = Path(__file__).resolve().parents[1]
META = ROOT / 'dump_2026-09-22_build25470389'
binary = Path(r'C:\Steam\steamapps\common\Tape to Tape\GameAssembly.dll').read_bytes()
expected = json.loads((META / 'source-hashes.json').read_text(encoding='utf-8-sig'))
assert hashlib.sha256(binary).hexdigest() == next(x['Hash'].lower() for x in expected if x['Path'].endswith('GameAssembly.dll'))
script = json.loads((META / 'script.json').read_text())
names = {}
for method in script['ScriptMethod']:
    names.setdefault(method['Address'], []).append(method['Name'])
starts = sorted(names)
references = {m['Address']: m['Name'] for key in ('ScriptMetadata', 'ScriptMetadataMethod') for m in script.get(key, [])}
references.update({m['Address']: repr(m['Value']) for m in script['ScriptString']})
decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
decoder.detail = True
out = ROOT / 'artifacts/appearance-audit'
out.mkdir(parents=True, exist_ok=True)
wanted = ('Data.ForwardData$$UsesCustom', 'Data.ForwardData$$GetCustomizationFlags',
          'Data.GoaltenderData$$UsesCustom', 'Data.GoaltenderData$$GetCustomizationFlags',
          'Skaters.SkinHelper$$GetSkins', 'Skaters.SkinHelper$$GetHeadSkin',
          'Skaters.T2TSkeletonExtensions$$SetupSkeletonSlots',
          'Skaters.T2TSkeletonExtensions$$SetSlotCustomizable')
for start in starts:
    owners = names[start]
    if not any(name.startswith(wanted) for name in owners):
        continue
    end = starts[bisect.bisect_right(starts, start)]
    lines = ['; ' + '; '.join(owners)]
    for ins in decoder.disasm(binary[start-0x1600:end-0x1600], start):
        refs = []
        if ins.mnemonic in ('call', 'jmp') and ins.operands[0].type == capstone.x86.X86_OP_IMM:
            refs.extend(names.get(ins.operands[0].imm, []))
        for operand in ins.operands:
            if operand.type == capstone.x86.X86_OP_MEM and operand.mem.base == capstone.x86.X86_REG_RIP:
                ref = references.get(ins.address + ins.size + operand.mem.disp)
                if ref: refs.append(ref)
        lines.append(f'{ins.address:08x} {ins.mnemonic:8} {ins.op_str:42} ' + '; '.join(refs))
    (out / (owners[0].replace('$$', '-') + '.asm')).write_text('\n'.join(lines))
print(f'Wrote fingerprint-verified native disassembly to {out}')
