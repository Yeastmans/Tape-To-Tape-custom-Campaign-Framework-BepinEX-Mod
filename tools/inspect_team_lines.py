import bisect, json, hashlib
from pathlib import Path
import capstone
root=Path(__file__).resolve().parents[1]
meta=root/'dump_2026-09-22_build25470389'
binary=Path(r'C:\Steam\steamapps\common\Tape to Tape\GameAssembly.dll').read_bytes()
assert hashlib.sha256(binary).hexdigest()==next(x['Hash'].lower() for x in json.loads((meta/'source-hashes.json').read_text(encoding='utf-8-sig')) if x['Path'].endswith('GameAssembly.dll'))
s=json.loads((meta/'script.json').read_text())
names={}
for m in s['ScriptMethod']: names.setdefault(m['Address'],[]).append(m['Name'])
starts=sorted(names)
md=capstone.Cs(capstone.CS_ARCH_X86,capstone.CS_MODE_64)
out=[]
for start in starts:
    if not any(('SwitchLineAfterGoal' in n and n.endswith('MoveNext')) or (n.startswith('Tape2Tape.Team$$') and n.split('$$')[-1] in ('OnReplayEnd','SwitchLineIfNeeded','ToggleSelectedLine','ApplyLineForFaceoff','OnFixedUpdate','Update','FixedUpdate','ChangeLine')) or n.startswith('Data.TeamData$$') and any(k in n for k in ('Line','Forward')) for n in names[start]): continue
    out.append('\n'+ ';'.join(names[start]))
    end=starts[bisect.bisect_right(starts,start)]
    for ins in md.disasm(binary[start-0x1600:end-0x1600],start):
        call=''
        if ins.mnemonic in ('call','jmp') and ins.op_str.startswith('0x'): call=' ; '+str(names.get(int(ins.op_str,16),[]))
        out.append(f'{ins.address:x} {ins.mnemonic} {ins.op_str}{call}')
folder=root/'artifacts/tycoon-lines';folder.mkdir(parents=True,exist_ok=True)
(folder/'native-lines.txt').write_text('\n'.join(out))
print(folder/'native-lines.txt')
