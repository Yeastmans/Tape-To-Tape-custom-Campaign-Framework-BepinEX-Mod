"""Disassemble ChooseMetaMenu.SetupMetas from GameAssembly.dll and name its call targets via dump.cs RVAs."""
import re
import capstone

GA = r"C:\Steam\steamapps\common\Tape to Tape\GameAssembly.dll"
DUMP = r"C:\Users\kiran\Desktop\tape to tape mods\dump_2026-09-22_build25470389\dump.cs"

FUNC_OFFSET = 0x36DF440      # file offset of SetupMetas (Jul-10 binary)
FUNC_RVA = 0x36E0A40         # RVA of SetupMetas
FUNC_SIZE = 0x36E0FA0 - 0x36E0A40  # bound: next method (SetupRelics)

# Build RVA -> name map from the dump (names come from the preceding signature line)
rva_names = {}
sig = None
with open(DUMP, "r", encoding="utf-8", errors="replace") as f:
    prev_rva = None
    for line in f:
        m = re.search(r"RVA: (0x[0-9A-Fa-f]+) Offset", line)
        if m:
            prev_rva = int(m.group(1), 16)
            continue
        if prev_rva is not None:
            s = line.strip()
            if s and not s.startswith("//") and not s.startswith("["):
                # first code-ish line after an RVA comment = the method signature
                rva_names.setdefault(prev_rva, s[:150])
                prev_rva = None

with open(GA, "rb") as f:
    f.seek(FUNC_OFFSET)
    code = f.read(FUNC_SIZE)

md = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
md.detail = False

calls = []
for insn in md.disasm(code, FUNC_RVA):
    if insn.mnemonic == "call":
        try:
            target = int(insn.op_str, 16)
            name = rva_names.get(target, "?")
            calls.append((insn.address, target, name))
        except ValueError:
            calls.append((insn.address, None, f"indirect: {insn.op_str}"))

print(f"SetupMetas: {len(calls)} call sites")
for addr, target, name in calls:
    t = f"0x{target:X}" if target else "----"
    print(f"  +0x{addr - FUNC_RVA:04X}  {t}  {name}")
