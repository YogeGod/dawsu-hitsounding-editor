import struct
from capstone import CS_ARCH_X86, CS_MODE_64, Cs
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
    name = data[off:off+8].split(b"\0", 1)[0].decode(errors="replace")
    vsize, va, rawsize, raw = struct.unpack_from("<IIII", data, off + 8)
    sections.append((name, va, vsize, raw, rawsize))
text = next(s for s in sections if s[0] == ".text")
text_lo, text_hi = IMAGE + text[1], IMAGE + text[1] + text[2]

def va_off(va):
    rva = va - IMAGE
    for name, sva, vsize, raw, rawsize in sections:
        if sva <= rva < sva + rawsize:
            return raw + (rva - sva)
    return None

refs = [0x145ef8bb8,0x145f508f8,0x14604d968,0x146154690,0x1461768f8,0x146187720,0x1462143e0,0x146220f80,0x1465d6d90,0x1465d7268,0x1465d7b28,0x146635eb8]
for ref in refs:
    off = va_off(ref)
    # walk back while qwords are code pointers
    base = ref
    while True:
        prev = struct.unpack_from("<Q", data, va_off(base - 8))[0]
        if not (text_lo <= prev < text_hi):
            break
        base -= 8
        if ref - base > 0x800:
            break
    slots = []
    p = base
    for n in range(80):
        q = struct.unpack_from("<Q", data, va_off(p))[0]
        if not (text_lo <= q < text_hi):
            break
        slots.append(q)
        p += 8
    idx = (ref - base) // 8
    print(f"ref {ref:012x} vtable {base:012x} slot {idx:#x} (+{idx*8:#x}) len {len(slots)}")
