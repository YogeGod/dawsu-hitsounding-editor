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
    raise SystemExit(hex(va))

def read_str(va):
    off = va_off(va)
    raw = data[off:off+64]
    if not raw or raw[0] < 32 or raw[0] > 126:
        return None
    s = raw.split(b"\x00", 1)[0]
    if len(s) < 2 or any(b < 32 or b > 126 for b in s):
        return None
    return s.decode("ascii")

start = 0x1437C20F0
blob = data[va_off(start):va_off(start)+0x3000]
# real lea rdx, [rip+disp32]
i = 0
hits = []
while i < len(blob) - 7:
    if blob[i:i+3] == b"\x48\x8d\x15":
        disp = struct.unpack_from("<i", blob, i+3)[0]
        va = start + i
        nxt = va + 7
        s = read_str(nxt + disp)
        if s:
            hits.append((hex(va), s))
    i += 1
print("lea hits", len(hits))
for h in hits:
    print(h[0], h[1])
# ret; int3
print("---rets---")
for i in range(len(blob)-1):
    if blob[i] == 0xC3 and blob[i+1] == 0xCC:
        print(hex(start+i))
