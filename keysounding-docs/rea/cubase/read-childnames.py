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

for name, nxt, disp in [("a", 0x144726f31, 0x1925dde), ("b", 0x144726f42, 0x184ed82), ("c", 0x144726f5a, 0x1b9b286)]:
    va = nxt + disp
    raw = data[va_off(va):va_off(va)+40]
    print(name, hex(va), raw.split(b"\x00",1)[0])
