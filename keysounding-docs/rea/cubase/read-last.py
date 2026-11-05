import struct
PATH = r"C:\Program Files\Steinberg\Cubase 15\Cubase15.exe"
IMAGE = 0x140000000
data = open(PATH, "rb").read()
pe = struct.unpack_from("<I", data, 0x3C)[0]
nsec = struct.unpack_from("<H", data, pe + 6)[0]
opt = struct.unpack_from("<H", data, pe + 20)[0]
sec_off = pe + 24 + opt
va = 0x14476291e + 0x1785db2
rva = va - IMAGE
off = None
for i in range(nsec):
    o = sec_off + i * 40
    vsize, sva, rawsize, raw = struct.unpack_from("<IIII", data, o + 8)
    if sva <= rva < sva + max(vsize, rawsize):
        off = raw + (rva - sva)
print(hex(va), data[off:off+32].split(b"\x00",1)[0])
