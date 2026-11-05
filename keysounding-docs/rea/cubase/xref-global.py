import struct
PATH = r"C:\Program Files\Steinberg\Cubase 15\Cubase15.exe"
IMAGE = 0x140000000
TARGET = 0x1477C9E18
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
    sections.append((name, va, raw, rawsize))
text = [s for s in sections if s[0] == ".text"][0]
_, va, raw, rawsize = text
blob = data[raw:raw+rawsize]
hits = []
for i in range(len(blob) - 7):
    # mov/lea/cmp rip-relative: look for disp32 that lands on TARGET
    # instruction end is i+7 for the common 7-byte form (rex, opcode, modrm, disp32)
    b0, b1, b2 = blob[i], blob[i+1], blob[i+2]
    if b0 in (0x48, 0x4C) and b1 in (0x8B, 0x89, 0x8D, 0x3B, 0x85) and (b2 & 0xC7) == 0x05:
        disp = struct.unpack_from("<i", blob, i+3)[0]
        dest = IMAGE + va + i + 7 + disp
        if dest == TARGET:
            hits.append((IMAGE + va + i, b1))
print("hits", [(hex(a), hex(op)) for a, op in hits])
