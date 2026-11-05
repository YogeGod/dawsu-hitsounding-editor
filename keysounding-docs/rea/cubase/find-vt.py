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
    sections.append((name, va, raw, rawsize))

needle = struct.pack("<Q", 0x1446E37F0)
start = 0
found = []
while len(found) < 8:
    i = data.find(needle, start)
    if i < 0: break
    for name, va, raw, rawsize in sections:
        if raw <= i < raw + rawsize:
            found.append((name, hex(IMAGE + va + (i - raw))))
            break
    start = i + 1
print(found)

# first command name
def va_off(va):
    rva = va - IMAGE
    for name, sva, rawsize, raw in [(n,a,c,b) for n,a,b,c in sections]:
        pass
    for name, sva, raw, rawsize in sections:
        if sva <= rva < sva + rawsize:
            return raw + (rva - sva)
    return None
va = 0x1446e3810 + 0x1edded0
off = va_off(va)
print("name", hex(va), data[off:off+32].split(b"\x00",1)[0])
