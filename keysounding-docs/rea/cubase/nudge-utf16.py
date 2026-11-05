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

def off_va(off):
    for va, vsize, raw, rawsize in sections:
        if raw <= off < raw + rawsize:
            return IMAGE + va + (off - raw)
    return None

needle = "Nudge".encode("utf-16le")
idx = 0
while True:
    i = data.find(needle, idx)
    if i < 0:
        break
    raw = data[i:i+80]
    # decode until double null
    chars = []
    for j in range(0, len(raw)-1, 2):
        c = raw[j] | (raw[j+1] << 8)
        if c == 0:
            break
        chars.append(chr(c) if c < 128 else "?")
    print(hex(off_va(i) or 0), "".join(chars))
    idx = i + 2
