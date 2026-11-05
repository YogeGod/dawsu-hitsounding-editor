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
    name = data[off:off+8].split(b"\0", 1)[0].decode(errors="replace")
    vsize, va, rawsize, raw = struct.unpack_from("<IIII", data, off + 8)
    sections.append((name, va, vsize, raw, rawsize))

def off_va(file_off):
    for name, va, vsize, raw, rawsize in sections:
        if raw <= file_off < raw + rawsize:
            return IMAGE + va + (file_off - raw), name
    return None, None

needle = struct.pack("<Q", 0x1447C7FC0)
start = 0
hits = []
while True:
    i = data.find(needle, start)
    if i < 0:
        break
    va, name = off_va(i)
    hits.append((va, name, i))
    start = i + 1
print("refs", len(hits))
for va, name, i in hits:
    print(f"{va:012x}  {name}")
