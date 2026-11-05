import struct
PATH = r"C:\Program Files\Steinberg\Cubase 15\Cubase15.exe"
IMAGE = 0x140000000
data = open(PATH, "rb").read()
pe = struct.unpack_from("<I", data, 0x3C)[0]
nsec = struct.unpack_from("<H", data, pe + 6)[0]
opt = struct.unpack_from("<H", data, pe + 20)[0]
sec_off = pe + 24 + opt

def va_off(va):
    rva = va - IMAGE
    for i in range(nsec):
        o = sec_off + i * 40
        vsize, sva, rawsize, raw = struct.unpack_from("<IIII", data, o + 8)
        if sva <= rva < sva + max(vsize, rawsize):
            return raw + (rva - sva)
    return None

def read_cstr(va):
    off = va_off(va)
    if off is None: return None
    raw = data[off:off+80]
    if not raw or raw[0] < 32 or raw[0] > 126: return None
    s = raw.split(b"\x00", 1)[0]
    if not s or any(b < 32 or b > 126 for b in s): return None
    return s.decode("ascii")

# mov rbx, [rip+disp] is 7 bytes: 48 8B 1D disp32
pairs = [
    (0x141af3c83, 0x458a416),
    (0x141af3cce, 0x458a43b),
    (0x141af3cec, 0x458a425),
    (0x141af3d17, 0x458a3f2),
]
for at, disp in pairs:
    slot = at + 7 + disp
    off = va_off(slot)
    ptr = struct.unpack_from("<Q", data, off)[0]
    print(hex(at), hex(ptr), read_cstr(ptr))
