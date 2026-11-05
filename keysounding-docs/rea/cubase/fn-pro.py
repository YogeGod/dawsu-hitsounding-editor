import struct
from capstone import CS_ARCH_X86, CS_MODE_64, Cs
PATH = r"C:\Program Files\Steinberg\Cubase 15\Cubase15.exe"
IMAGE = 0x140000000
data = open(PATH, "rb").read()
pe = struct.unpack_from("<I", data, 0x3C)[0]
nsec = struct.unpack_from("<H", data, pe + 6)[0]
opt = struct.unpack_from("<H", data, pe + 20)[0]
sec_off = pe + 24 + opt

def va_off(va):
    rva = va - IMAGE
    for i in range(nsec):
        o = sec_off + i * 40
        vsize, sva, rawsize, raw = struct.unpack_from("<IIII", data, o + 8)
        if sva <= rva < sva + max(vsize, rawsize):
            return raw + (rva - sva)
    raise SystemExit(hex(va))

off = va_off(0x1446E37E0)
print(data[off:off+32].hex())
md = Cs(CS_ARCH_X86, CS_MODE_64)
# find first non-cc
raw = data[off:off+64]
k = 0
while raw[k] == 0xCC:
    k += 1
start = 0x1446E37E0 + k
print("start", hex(start))
for ins in md.disasm(data[va_off(start):va_off(start)+0x40], start):
    print(f"{ins.address:012x}  {ins.mnemonic} {ins.op_str}")
