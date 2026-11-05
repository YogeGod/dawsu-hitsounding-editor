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

def read_cstr(va):
    off = va_off(va)
    if off is None: return None
    raw = data[off:off+96]
    if not raw or raw[0] < 32 or raw[0] > 126: return None
    s = raw.split(b"\x00", 1)[0]
    if len(s) < 1 or any(b < 32 or b > 126 for b in s): return None
    return s.decode("ascii")

start = 0x141A505D0
blob = data[va_off(start):va_off(start)+0x4000]
# stop at 16 int3s
end = len(blob)
run = 0
for i, b in enumerate(blob):
    if b == 0xCC:
        run += 1
        if run >= 8 and i > 0x40:
            end = i
            break
    else:
        run = 0
print("span", hex(end))
i = 0
while i < end - 7:
    s = None
    if blob[i:i+3] == b"\x48\x8d\x15":
        disp = struct.unpack_from("<i", blob, i+3)[0]
        s = read_cstr(start + i + 7 + disp)
    elif blob[i:i+3] == b"\x48\x8b\x15":
        disp = struct.unpack_from("<i", blob, i+3)[0]
        slot = start + i + 7 + disp
        off = va_off(slot)
        if off is not None:
            ptr = struct.unpack_from("<Q", data, off)[0]
            s = read_cstr(ptr)
            if s: s = "-> " + s
    if s:
        print(hex(start+i), s)
    i += 1
