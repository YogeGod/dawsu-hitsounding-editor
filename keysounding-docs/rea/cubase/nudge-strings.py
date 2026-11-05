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

def off_va(off):
    for name, va, vsize, raw, rawsize in sections:
        if raw <= off < raw + rawsize:
            return IMAGE + va + (off - raw), name
    return None, None

for off in (101183352, 101183376, 101183400, 101183424, 101183456, 101183488, 101183512, 100683822):
    chunk = data[off:off+48].split(b"\x00",1)[0]
    va, name = off_va(off)
    print(hex(off), name, hex(va) if va else "?", chunk)
