import struct
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

base = 0x1465c12a0
off = va_off(base)
for i in range(16):
    q = struct.unpack_from("<Q", data, off + i*8)[0]
    mark = "  <-- handler" if q == 0x1446E37F0 else ""
    print(hex(base + i*8), hex(q), mark)
