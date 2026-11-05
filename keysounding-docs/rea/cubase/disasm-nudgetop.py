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

hit = 0x1416EBAAB
off = va_off(hit)
start = off
while start > off - 0x500:
    if data[start-1] == 0xCC and data[start] != 0xCC:
        break
    start -= 1
va = hit - (off - start)
print("function", hex(va))
md = Cs(CS_ARCH_X86, CS_MODE_64)
for ins in md.disasm(data[start:start+0x700], va):
    mark = " <<" if ins.address == hit else ""
    print(f"{ins.address:012x}  {ins.mnemonic} {ins.op_str}{mark}")
    if ins.address > hit and ins.mnemonic == "int3":
        break
