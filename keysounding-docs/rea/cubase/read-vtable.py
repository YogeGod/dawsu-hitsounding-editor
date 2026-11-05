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
    name = data[off:off+8].split(b"\x00",1)[0]
    vsize, va, rawsize, raw = struct.unpack_from("<IIII", data, off + 8)
    sections.append((name, va, vsize, raw, rawsize))

def va_off(va):
    rva = va - IMAGE
    for name, sva, vsize, raw, rawsize in sections:
        if sva <= rva < sva + max(vsize, rawsize):
            return raw + (rva - sva)
    raise SystemExit(hex(va))

vt = 0x1465932A0
off = va_off(vt)
print("vtable", hex(vt))
for i in range(20):
    slot = struct.unpack_from("<Q", data, off + i*8)[0]
    print(f"  +{i*8:02x} {slot:012x}")

# also the two earlier candidates
for label, addr, disp in [
    ("vt1", 0x144558119, 0x197a5ff),
    ("vt2", 0x14455812a, 0x197a65e),
    ("vt3", 0x144558134, 0x203e16c),
]:
    print(label, hex(addr + disp))
