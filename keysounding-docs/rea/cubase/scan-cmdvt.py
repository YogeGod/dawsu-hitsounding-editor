import struct
from capstone import CS_ARCH_X86, CS_MODE_64, Cs
PATH = r"C:\Program Files\Steinberg\Cubase 15\Cubase15.exe"
IMAGE = 0x140000000
data = open(PATH, "rb").read()
pe = struct.unpack_from("<I", data, 0x3C)[0]
nsec = struct.unpack_from("<H", data, pe + 6)[0]
opt = struct.unpack_from("<H", data, pe + 20)[0]
sec_off = pe + 24 + opt
sections = []
for i in range(nsec):
    off = sec_off + i * 40
    vsize, va, rawsize, raw = struct.unpack_from("<IIII", data, off + 8)
    sections.append((va, vsize, raw, rawsize))

def va_off(va):
    rva = va - IMAGE
    for sva, vsize, raw, rawsize in sections:
        if sva <= rva < sva + max(vsize, rawsize):
            return raw + (rva - sva)
    raise SystemExit(hex(va))

vt = 0x1446aa8af + 0x1bfbf81
print("primary", hex(vt))
off = va_off(vt)
fns = []
for i in range(28):
    slot = struct.unpack_from("<Q", data, off + i*8)[0]
    print(f"+{i*8:02x} {slot:012x}")
    if 0x140000000 <= slot < 0x147000000:
        fns.append(slot)

md = Cs(CS_ARCH_X86, CS_MODE_64)
print("--- interesting ---")
for fn in fns:
    text = []
    hits = False
    for ins in md.disasm(data[va_off(fn):va_off(fn)+0x180], fn):
        text.append(f"{ins.address:012x}  {ins.mnemonic} {ins.op_str}")
        if ins.mnemonic in ("add", "sub", "inc", "dec") and ("0x" in ins.op_str or ins.op_str.endswith("1")):
            hits = True
        if ins.address > fn and ins.mnemonic == "int3":
            break
        if len(text) > 40:
            break
    if hits:
        print(f"## {fn:012x}")
        print("\n".join(text[:25]))
        print()
