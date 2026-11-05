import struct
from capstone import CS_ARCH_X86, CS_MODE_64, Cs
PATH = r"C:\Program Files\Steinberg\Cubase 15\Cubase15.exe"
IMAGE = 0x140000000
data = open(PATH, "rb").read()
pe = struct.unpack_from("<I", data, 0x3C)[0]
nsec = struct.unpack_from("<H", data, pe + 6)[0]
opt = struct.unpack_from("<H", data, pe + 20)[0]
sec = pe + 24 + opt
sections = []
for i in range(nsec):
    off = sec + i * 40
    vsize, va, rawsize, raw = struct.unpack_from("<IIII", data, off + 8)
    sections.append((va, raw, rawsize, vsize))

def va_off(v):
    rva = v - IMAGE
    for sva, raw, rawsize, vsize in sections:
        if sva <= rva < sva + max(vsize, rawsize):
            return raw + (rva - sva)
md = Cs(CS_ARCH_X86, CS_MODE_64)
n=0
for ins in md.disasm(data[va_off(0x1447a7b60):va_off(0x1447a7b60)+0x200], 0x1447a7b60):
    if ins.mnemonic in ("call","jmp","ret"):
        print(f"{ins.address:012x} {ins.bytes.hex():16} {ins.mnemonic} {ins.op_str}")
    n+=1
    if ins.mnemonic=="ret" and n>8:
        break
