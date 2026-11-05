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
addrs = [0x141a5076c,0x141a507ad,0x141a509d4,0x141a50b6e,0x141a50e5e,0x141a50eb7,0x141a5112b,0x141a51342,0x141a5147f,0x141a514d0,0x141a5160e,0x141a51647,0x141a517fa,0x141a51a6f,0x141a51b59,0x141a51b85,0x141a51e2f]
for a in addrs:
    ins = next(md.disasm(data[va_off(a):va_off(a)+8], a))
    print(hex(a), ins.mnemonic, ins.op_str, ins.bytes.hex())
