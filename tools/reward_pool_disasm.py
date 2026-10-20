"""Disassembly helper for the reward-pool investigation (evidence for extract_reward_pools.py).

usage: python tools/reward_pool_disasm.py <regex-on-method-name> [...]   -> prints annotated disassembly
       python tools/reward_pool_disasm.py --callers <regex>               -> lists direct callers (E8 rel32)
Names come from Il2CppDumper script.json of artifacts/reward-pools/dump_build25541412 (installed build 25541412; cached as a pickle).
"""
import bisect, json, pickle, re, sys
from pathlib import Path
import capstone, pefile
ROOT = Path(__file__).resolve().parents[1]
DUMP = ROOT / 'artifacts/reward-pools/dump_build25541412'
DLL = Path(r'C:\Steam\steamapps\common\Tape to Tape\GameAssembly.dll')
CACHE = ROOT / 'artifacts/reward-pools/_script_index.pkl'

def index():
    if CACHE.exists(): return pickle.loads(CACHE.read_bytes())
    s = json.loads((DUMP / 'script.json').read_text(encoding='utf-8'))
    meth = {}
    for m in s['ScriptMethod']: meth.setdefault(m['Address'], []).append(m['Name'])
    meta = {m['Address']: m['Name'] for m in s['ScriptMetadata']}
    for m in s['ScriptMetadataMethod']: meta[m['Address']] = m['Name']
    strs = {m['Address']: m['Value'] for m in s['ScriptString']}
    idx = {'meth': meth, 'meta': meta, 'strs': strs, 'starts': sorted(a for a in meth if a)}
    CACHE.write_bytes(pickle.dumps(idx)); return idx

class Bin:
    def __init__(self):
        self.pe = pefile.PE(str(DLL), fast_load=True); self.data = self.pe.__data__
        self.text = next(s for s in self.pe.sections if s.Name.startswith(b'il2cpp'))  # all IL2CPP methods live here
    def off(self, rva):
        for sec in self.pe.sections:
            if sec.VirtualAddress <= rva < sec.VirtualAddress + sec.Misc_VirtualSize:
                return rva - sec.VirtualAddress + sec.PointerToRawData
    def bytes(self, rva, n):
        o = self.off(rva); return bytes(self.data[o:o + n])

def disasm(idx, b, start):
    starts = idx['starts']; end = starts[bisect.bisect_right(starts, start)]
    md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64); md.detail = False
    out = []
    for ins in md.disasm(b.bytes(start, end - start), start):
        note = ''
        if ins.mnemonic in ('call', 'jmp') and ins.op_str.startswith('0x'):
            t = int(ins.op_str, 16); note = ' ; ' + '|'.join(idx['meth'].get(t, [])[:2])
        m = re.search(r'\[rip ([+-]) (0x[0-9a-f]+)\]', ins.op_str)
        if m:
            t = ins.address + ins.size + (int(m.group(2), 16) * (1 if m.group(1) == '+' else -1))
            n = idx['meta'].get(t) or (repr(idx['strs'][t]) if t in idx['strs'] else None)
            if n: note += f' ; {n}'
        out.append(f'{ins.address:x}: {ins.mnemonic} {ins.op_str}{note}')
    return out

def callers(idx, b, targets):
    import numpy as np
    t = b.text; o = t.PointerToRawData; raw = np.frombuffer(bytes(b.data[o:o + t.SizeOfRawData]), dtype=np.uint8)
    pos = np.nonzero(raw[:-5] == 0xE8)[0]
    rel = raw[pos + 1].astype(np.int64) | (raw[pos + 2].astype(np.int64) << 8) | (raw[pos + 3].astype(np.int64) << 16) | (raw[pos + 4].astype(np.int64) << 24)
    rel = np.where(rel >= 1 << 31, rel - (1 << 32), rel)
    dest = pos + 5 + rel + t.VirtualAddress
    res = {}
    for tgt in targets:
        for p in pos[dest == tgt]:
            rva = int(p) + t.VirtualAddress; s = idx['starts'][bisect.bisect_right(idx['starts'], rva) - 1]
            res.setdefault(tgt, []).append((rva, s, idx['meth'].get(s, ['?'])))
    return res

def refs(idx, b, target):
    import numpy as np
    t = b.text; o = t.PointerToRawData; raw = bytes(b.data[o:o + t.SizeOfRawData]); hits = set()
    for k in range(4):
        n = (len(raw) - k) // 4; v = np.frombuffer(raw[k:k + 4 * n], dtype='<i4').astype(np.int64)
        pos = np.arange(n, dtype=np.int64) * 4 + k
        for q in pos[pos + 4 + v + t.VirtualAddress == target]:
            rva = int(q) + t.VirtualAddress; hits.add(idx['starts'][bisect.bisect_right(idx['starts'], rva) - 1])
    return sorted(hits)

if __name__ == '__main__':
    idx = index(); b = Bin(); args = sys.argv[1:]
    if args and args[0] == '--refs':
        pat = re.compile(args[1])
        for a, n in idx['meta'].items():
            if pat.search(n):
                print(f'== {a:x} {n}')
                for f in refs(idx, b, a): print(f'   {f:x} {idx["meth"][f][:2]}')
    elif args and args[0] == '--callers':
        pat = re.compile(args[1]); tg = [a for a, ns in idx['meth'].items() if a and any(pat.search(n) for n in ns)]
        for tgt, lst in callers(idx, b, tg).items():
            print(f'== {tgt:x} {idx["meth"][tgt][:2]}')
            for rva, s, ns in lst: print(f'   {rva:x} in {s:x} {ns[:2]}')
    else:
        for a in args:
            pat = re.compile(a)
            for addr, ns in sorted(idx['meth'].items()):
                if addr and any(pat.search(n) for n in ns):
                    print(f'\n===== {addr:x} {ns[:3]}'); print('\n'.join(disasm(idx, b, addr)))
