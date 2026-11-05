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
    name = data[off:off+8].split(b"\x00",1)[0]
    vsize, va, rawsize, raw = struct.unpack_from("<IIII", data, off + 8)
    sections.append((name, va, vsize, raw, rawsize))

def va_off(va):
    rva = va - IMAGE
    for name, sva, vsize, raw, rawsize in sections:
        if sva <= rva < sva + max(vsize, rawsize):
            return raw + (rva - sva)
    raise SystemExit(hex(va))

slot = 0x1465c12e0
off = va_off(slot)
start = slot
for back in range(0, 0x200, 8):
    q = struct.unpack_from("<Q", data, off - back)[0]
    if not (0x140000000 <= q < 0x148000000):
        start = slot - back + 8
        print("break at", hex(slot-back), hex(q))
        break
print("vtable start guess", hex(start), "slot", hex((slot-start)//8))
# print a few slots
so = va_off(start)
for i in range(0, 0x120, 8):
    q = struct.unpack_from("<Q", data, so+i)[0]
    mark = " *" if start+i == slot else ""
    print(hex(start+i), hex(q), mark)
