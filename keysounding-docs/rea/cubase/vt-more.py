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

vt = 0x1462A6830
off = va_off(vt)
md = Cs(CS_ARCH_X86, CS_MODE_64)
for i in range(28, 70):
    slot = struct.unpack_from("<Q", data, off + i*8)[0]
    if not (0x140000000 <= slot < 0x147800000):
        print(f"stop +{i*8:02x} {slot:012x}")
        break
    print(f"+{i*8:02x} {slot:012x}")
