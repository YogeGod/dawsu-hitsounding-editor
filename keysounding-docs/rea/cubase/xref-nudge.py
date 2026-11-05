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
    name = data[off:off+8].split(b"\x00",1)[0].decode()
    vsize, va, rawsize, raw = struct.unpack_from("<IIII", data, off + 8)
    sections.append((name, va, vsize, raw, rawsize))
    print(name, hex(va), hex(raw), hex(rawsize))

text = [s for s in sections if s[0] == ".text"][0]
_, va, vsize, raw, rawsize = text
target = 0x1460EA3B8  # Nudge Top Up
blob = data[raw:raw+rawsize]
hits = []
i = 0
n = len(blob)
while i < n - 7:
    if blob[i] == 0x48 and blob[i+1] == 0x8D and (blob[i+2] & 0xC7) == 0x05:
        disp = struct.unpack_from("<i", blob, i+3)[0]
        dest = IMAGE + va + i + 7 + disp
        if dest == target:
            hits.append(IMAGE + va + i)
    i += 1
print("hits", [hex(h) for h in hits])
