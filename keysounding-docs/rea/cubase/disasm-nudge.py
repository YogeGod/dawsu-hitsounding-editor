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
    sections.append((va, raw, rawsize, vsize))

def va_off(va):
    rva = va - IMAGE
    for sva, raw, rawsize, vsize in sections:
        if sva <= rva < sva + max(vsize, rawsize):
            return raw + (rva - sva)
    raise SystemExit(hex(va))

fn = 0x1446e4ce0
md = Cs(CS_ARCH_X86, CS_MODE_64)
raw = data[va_off(fn):va_off(fn)+0x400]
n = 0
for ins in md.disasm(raw, fn):
    print(f"{ins.address:012x}  {ins.mnemonic} {ins.op_str}")
    n += 1
    if ins.address > fn and ins.mnemonic in ("ret", "int3"):
        break
    if n > 120:
        break
