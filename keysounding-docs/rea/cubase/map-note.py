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

lea = 0x1446e5089
disp = struct.unpack_from("<i", data, va_off(lea)+3)[0]
base = (lea + 7 + disp) & 0xffffffffffffffff
# mov ecx, [rdx+rax*4+imm] bytes after lea
b = data[va_off(lea)+7:va_off(lea)+14]
print("after lea", b.hex(), "base", hex(base))
imm = struct.unpack_from("<i", data, va_off(0x1446e5090)+3)[0]
table = (base + imm) & 0xffffffffffffffff
print("table", hex(table), "imm", hex(imm & 0xffffffff))
names = {7:"Left",8:"Right",9:"Up",10:"Down"}
md = Cs(CS_ARCH_X86, CS_MODE_64)
seen = {}
for edx in range(1, 19):
    rel = struct.unpack_from("<i", data, va_off(table)+(edx-1)*4)[0]
    target = (base + rel) & 0xffffffffffffffff
    label = names.get(edx, "")
    print(f"dir {edx:2d} {label:6} -> {hex(target)}")
    if label and target not in seen:
        seen[target] = label
for target, label in seen.items():
    print("====", label, hex(target))
    n=0
    for ins in md.disasm(data[va_off(target):va_off(target)+0x80], target):
        print(f"{ins.address:012x}  {ins.mnemonic} {ins.op_str}")
        n+=1
        if n>18:
            break
