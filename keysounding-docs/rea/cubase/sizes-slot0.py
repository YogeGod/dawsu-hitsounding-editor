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

md = Cs(CS_ARCH_X86, CS_MODE_64)
md.detail = True
start = 0x1447ACB60
for ins in md.disasm(data[va_off(start):va_off(start)+0x220], start):
    if ins.mnemonic == "call" or ins.address in (0x1447acb84, 0x1447accbb, 0x1447acd1f, 0x1447acd39, 0x1447acd4e):
        raw = data[va_off(ins.address):va_off(ins.address)+ins.size].hex()
        print(f"{ins.address:012x} sz={ins.size} {raw}  {ins.mnemonic} {ins.op_str}")
    if ins.address > start and ins.mnemonic == "int3":
        break
