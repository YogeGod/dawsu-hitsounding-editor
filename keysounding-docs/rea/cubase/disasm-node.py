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

def dump(start, n):
    print(f"==== {start:012x}")
    c = 0
    for ins in md.disasm(data[va_off(start):va_off(start)+n], start):
        print(f"{ins.address:012x}  {ins.mnemonic} {ins.op_str}")
        c += 1
        if ins.address > start and ins.mnemonic == "int3":
            break
        if c > 160:
            print("...")
            break
    print()

dump(0x144A87A80, 0x500)
dump(0x144A87240, 0x200)
