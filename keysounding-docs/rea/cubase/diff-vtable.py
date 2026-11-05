import struct
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

base = 0x145ed2788
derived = 0x1465962a0
bo = va_off(base)
do = va_off(derived)
for i in range(40):
    b = struct.unpack_from("<Q", data, bo + i*8)[0]
    d = struct.unpack_from("<Q", data, do + i*8)[0]
    mark = " *" if b != d else ""
    if b < 0x140000000 or b > 0x148000000:
        print(f"stop at +{i*8:02x} base {b:x}")
        break
    print(f"+{i*8:02x} {d:012x}{mark}")
