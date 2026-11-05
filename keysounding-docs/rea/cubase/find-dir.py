import struct
PATH = r"C:\Program Files\Steinberg\Cubase 15\Cubase15.exe"
IMAGE = 0x140000000
data = open(PATH, "rb").read()
pe = struct.unpack_from("<I", data, 0x3C)[0]
nsec = struct.unpack_from("<H", data, pe + 6)[0]
opt = struct.unpack_from("<H", data, pe + 20)[0]
sec_off = pe + 24 + opt
text = None
for i in range(nsec):
    off = sec_off + i * 40
    name = data[off:off+8].split(b"\x00",1)[0]
    vsize, va, rawsize, raw = struct.unpack_from("<IIII", data, off + 8)
    if name == b".text":
        text = (va, raw, rawsize)
blob = data[text[1]:text[1]+text[2]]
base = IMAGE + text[0]
# cmp edx, 9 = 83 FA 09 ; cmp edx, 0Ah = 83 FA 0A
hits = []
i = 0
while i < len(blob) - 3:
    if blob[i:i+3] == b"\x83\xfa\x09":
        window = blob[i:i+80]
        if b"\x83\xfa\x0a" in window or b"\x83\xfa\x07" in window:
            hits.append(base + i)
    i += 1
print("hits", len(hits))
for h in hits[:40]:
    print(hex(h))
