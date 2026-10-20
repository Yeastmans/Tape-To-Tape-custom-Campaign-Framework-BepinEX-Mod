"""Verify a squad manifest against matching IL2CPP metadata and native instructions.

This is an offline integrity check, not an in-game behavior test.
"""
import argparse
import bisect
import collections
import hashlib
import json
import re
from pathlib import Path

import capstone
import pefile


def verify(args):
    data = args.game.read_bytes()
    digest = hashlib.sha256(data).hexdigest()
    fingerprints = json.loads((args.dump / 'source-hashes.json').read_text(encoding='utf-8-sig'))
    assert any(Path(v['Path']).name == args.game.name and v['Hash'].lower() == digest for v in fingerprints), 'Dump belongs to another binary'
    lines = args.manifest.read_text(encoding='utf-8-sig').splitlines()
    assert lines[0].split('sha256=')[1].strip().lower() == digest, 'Manifest belongs to another binary'
    script = json.loads((args.dump / 'script.json').read_text(encoding='utf-8-sig'))
    methods = collections.defaultdict(list)
    for m in script['ScriptMethod']:
        if m['Address']:
            methods[m['Address']].append(m['Name'])
    starts = sorted(methods)
    literals = {m['Address']: m['Value'] for m in script['ScriptString']}
    pe = pefile.PE(data=data, fast_load=True)
    decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
    decoded = {}

    def body(rva):
        start = starts[bisect.bisect_right(starts, rva) - 1]
        if start not in decoded:
            end = starts[bisect.bisect_right(starts, start)]
            decoded[start] = list(decoder.disasm(pe.get_data(start, end - start), start))
        return start, decoded[start]

    rows = [line.split('\t') for line in lines if line and not line.startswith('#')]
    assert len({int(r[3], 16) for r in rows}) == len(rows), 'Duplicate patch addresses'
    expected_targets = {'query': 'Tape2Tape.CampaignUtil$$IsPlayingSquad', 'eq': 'System.String$$op_Equality', 'neq': 'System.String$$op_Inequality'}
    for feature, query, kind, address, target, original, name in rows:
        address, target = int(address, 16), int(target, 16)
        code = bytes.fromhex(original)
        assert len(code) == 5 and code[0] in (0xe8, 0xe9), name
        assert pe.get_data(address, len(code)) == code, name + ': original bytes changed'
        assert address + 5 + int.from_bytes(code[1:], 'little', signed=True) == target, name + ': target differs'
        assert expected_targets[kind] in methods[target], name + ': wrong target symbol'
        start, instructions = body(address)
        assert name == ' | '.join(methods[start][:3]), name + ': wrong containing method'
        indexes = [i for i, ins in enumerate(instructions) if ins.address == address]
        assert len(indexes) == 1, name + ': not an instruction boundary'
        pos = indexes[0]
        assert instructions[pos].size == 5 and instructions[pos].mnemonic in ('call', 'jmp'), name
        found = None
        for ins in reversed(instructions[max(0, pos - 10):pos]):
            match = re.fullmatch(r'rdx, qword ptr \[rip ([+-]) (0x[0-9a-f]+)\]', ins.op_str)
            if ins.mnemonic == 'mov' and match:
                ref = ins.address + ins.size + int(match[2], 16) * (1 if match[1] == '+' else -1)
                if ref in literals:
                    found = literals[ref]
                    break
        assert found == query, name + ': wrong squad ID argument'

    flow = args.flow_site
    start, instructions = body(flow)
    assert any('TitleScreen.<<NewRun>g__StartNewRun' in name and name.endswith('$$MoveNext') for name in methods[start]), 'Wrong flow method'
    assert pe.get_data(flow, 7).hex() == '410fb6c60a4758', 'Starting flow bytes changed'
    pos = next(i for i, ins in enumerate(instructions) if ins.address == flow)
    assert [(i.mnemonic, i.op_str) for i in instructions[pos:pos + 2]] == [('movzx', 'eax, r14b'), ('or', 'al, byte ptr [rdi + 0x58]')], 'Starting flow ABI changed'
    assert instructions[pos + 2].address == flow + 7 and instructions[pos + 2].mnemonic == 'je', 'Wrong flow resume'
    assert [(i.mnemonic, i.op_str) for i in instructions[pos - 2:pos]] == [('xor', 'r14b, r14b'), ('mov', 'byte ptr [rdi + 0x59], 1')], 'Starting flag setup changed'
    if args.previous:
        previous = [line.split('\t') for line in args.previous.read_text().splitlines() if line and not line.startswith('#')]
        key = lambda r: (r[0], r[1], r[2], r[6])
        assert collections.Counter(map(key, previous)) == collections.Counter(map(key, rows)), 'Gameplay comparison coverage changed; review before installing'
    result = dict(game_sha256=digest, comparison_sites=len(rows), native_behaviors=len({r[0] for r in rows}), containing_methods=len(decoded), flow_site=hex(flow), flow_resume=hex(flow + 7), status='Passed', scope='Offline native integrity; human gameplay retest required')
    args.output.write_text(json.dumps(result, indent=2) + '\n')
    print(json.dumps(result))


if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--game', type=Path, required=True)
    p.add_argument('--dump', type=Path, required=True)
    p.add_argument('--manifest', type=Path, required=True)
    p.add_argument('--flow-site', type=lambda v: int(v, 0), required=True)
    p.add_argument('--previous', type=Path)
    p.add_argument('--output', type=Path, required=True)
    verify(p.parse_args())
