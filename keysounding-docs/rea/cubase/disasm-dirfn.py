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

vt = 0x1446dd1d8 + 0x1ee4440
print("vtable", hex(vt))
off = va_off(vt)
for i in range(0, 0x60, 8):
    q = struct.unpack_from("<Q", data, off+i)[0]
    print(hex(i), hex(q))
fn = struct.unpack_from("<Q", data, off+0x40)[0]
print("slot40", hex(fn))
md = Cs(CS_ARCH_X86, CS_MODE_64)
print("---- function")
n = 0
for ins in md.disasm(data[va_off(fn):va_off(fn)+0x200], fn):
    print(f"{ins.address:012x}  {ins.mnemonic} {ins.op_str}")
    n += 1
    if ins.address > fn and ins.mnemonic == "int3":
        break
    if n > 80:
        break
