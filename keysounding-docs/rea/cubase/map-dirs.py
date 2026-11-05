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

lea = 0x1446e4def
disp = struct.unpack_from("<i", data, va_off(lea)+3)[0]
base = lea + 7 + disp
print("rdx base", hex(base & 0xffffffffffffffff))
table = (base + 0x046e5880) & 0xffffffffffffffff
print("table", hex(table))
names = {7:"Left",8:"Right",9:"Up",10:"Down"}
for edx in range(1, 15):
    rel = struct.unpack_from("<i", data, va_off(table)+(edx-1)*4)[0]
    target = (base + rel) & 0xffffffffffffffff
    print(f"dir {edx:2d} {names.get(edx,''):6} -> {hex(target)}")
md = Cs(CS_ARCH_X86, CS_MODE_64)
seen = []
for edx in (7,8,9,10):
    rel = struct.unpack_from("<i", data, va_off(table)+(edx-1)*4)[0]
    target = (base + rel) & 0xffffffffffffffff
    if target in seen:
        continue
    seen.append(target)
    print("====", names[edx], hex(target))
    n = 0
    for ins in md.disasm(data[va_off(target):va_off(target)+0x40], target):
        print(f"{ins.address:012x}  {ins.mnemonic} {ins.op_str}")
        n += 1
        if ins.mnemonic == "jmp" and "0x1446e4f31" in ins.op_str:
            break
        if n > 12:
            break
