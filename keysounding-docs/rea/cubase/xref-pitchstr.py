import struct
PATH = r"C:\Program Files\Steinberg\Cubase 15\Cubase15.exe"
IMAGE = 0x140000000
TARGET = 0x145ee1e78  # Set Note Pitch
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
hits = []
i = 0
while i < len(blob) - 7:
    if blob[i] in (0x48, 0x4c) and blob[i+1] == 0x8D and (blob[i+2] & 0xC7) == 0x05:
        disp = struct.unpack_from("<i", blob, i+3)[0]
        dest = base + i + 7 + disp
        if dest == TARGET:
            hits.append(hex(base+i))
    i += 1
print("pitch string refs", hits)
