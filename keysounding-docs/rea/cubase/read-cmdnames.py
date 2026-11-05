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
    vsize, va, rawsize, raw = struct.unpack_from("<IIII", data, off + 8)
    sections.append((va, vsize, raw, rawsize))

def va_off(va):
    rva = va - IMAGE
    for sva, vsize, raw, rawsize in sections:
        if sva <= rva < sva + max(vsize, rawsize):
            return raw + (rva - sva)
    return None

pairs = [
    ("c1", 0x144762591, 0x1785fbf),
    ("c2", 0x144762608, 0x191fe50),
    ("c3", 0x14476261b, 0x17ede7d),
    ("c4", 0x144762644, 0x1b6a17c),
    ("c5", 0x144762691, 0x17759c7),
    ("n1", 0x1447626d6, 0x18e8156),
    ("n2", 0x14476273e, 0x199c2ca),
    ("n3", 0x1447627d4, 0x18e8534),
    ("n4", 0x144762873, 0x199b529),
    ("n5", 0x1447628b1, 0x199b4f3),
    ("n6", 0x1447625a4+7, 0x181808d),
]
for name, nxt, disp in [
    ("c1", 0x144762591, 0x1785fbf),
    ("c2", 0x144762608, 0x191fe50),
    ("c3", 0x14476261b, 0x17ede7d),
    ("c4", 0x144762644, 0x1b6a17c),
    ("c5", 0x144762691, 0x17759c7),
    ("inner", 0x1447625ab, 0x181808d),
    ("n1", 0x1447626d6, 0x18e8156),
    ("n2", 0x14476273e, 0x199c2ca),
    ("n3", 0x1447627d4, 0x18e8534),
    ("n4", 0x144762873, 0x199b529),
    ("n5", 0x1447628b1, 0x199b4f3),
]:
    va = nxt + disp
    off = va_off(va)
    raw = data[off:off+48]
    s = raw.split(b"\x00", 1)[0]
    # utf16 if looks like it
    chars = []
    for j in range(0, 40, 2):
        c = raw[j] | (raw[j+1] << 8)
        if c == 0:
            break
        if c < 32 or c > 126:
            chars = None
            break
        chars.append(chr(c))
    u16 = "".join(chars) if chars else ""
    print(name, hex(va), s[:40], "|", u16)
