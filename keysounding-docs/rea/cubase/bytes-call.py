import struct
PATH = r"C:\Program Files\Steinberg\Cubase 15\Cubase15.exe"
IMAGE = 0x140000000
data = open(PATH,"rb").read()
pe = struct.unpack_from("<I", data, 0x3C)[0]
nsec = struct.unpack_from("<H", data, pe+6)[0]
opt = struct.unpack_from("<H", data, pe+20)[0]
sec = pe+24+opt
for i in range(nsec):
    off = sec+i*40
    vsize, va, rawsize, raw = struct.unpack_from("<IIII", data, off+8)
    rva = 0x1446e5516-IMAGE
    if va <= rva < va+max(vsize,rawsize):
        o = raw+(rva-va)
        print(data[o:o+8].hex())
        break
print(hex(11871122))
