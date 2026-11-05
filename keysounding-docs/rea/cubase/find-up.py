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
    return None

# find exact ASCII Up and Down as isolated cstrings
needles = [b"\x00Up\x00", b"\x00Down\x00", b"\x00Navigate\x00"]
text = None
for name, va, vsize, raw, rawsize in sections:
    if name == b".text":
        text = (va, raw, rawsize)
hits = {n: [] for n in needles}
for n in needles:
    start = 0
    while len(hits[n]) < 30:
        i = data.find(n, start)
        if i < 0: break
        # file offset to VA
        va = None
        for name, sva, vsize, raw, rawsize in sections:
            if raw <= i < raw + rawsize:
                va = IMAGE + sva + (i - raw) + 1
                break
        hits[n].append(va)
        start = i + 1
for n, vas in hits.items():
    print(n, len(vas))
    for va in vas[:12]:
        print(" ", hex(va) if va else None)
