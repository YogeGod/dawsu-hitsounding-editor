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
    print(f"{name:8} va={va:08x} vsize={vsize:08x} raw={raw:08x}")

text = next(s for s in sections if s[0] == ".text")
rdata = next(s for s in sections if s[0] == ".rdata")
text_lo, text_hi = text[1], text[1] + text[2]
rdata_lo, rdata_hi = rdata[1], rdata[1] + rdata[2]

def va_off(va):
    rva = va - IMAGE
    for name, sva, vsize, raw, rawsize in sections:
        if sva <= rva < sva + max(vsize, rawsize) and rva - sva < rawsize:
            return raw + (rva - sva)
    return None

def read_q(va):
    off = va_off(va)
    if off is None:
        return None
    return struct.unpack_from("<Q", data, off)[0]

def in_text(p):
    return p is not None and text_lo <= (p - IMAGE) < text_hi

# slots: candidate address holds the pointer at vtable+0x128
# vtable+0x130 is +8, vtable+0x220 is +0xF8 from the +0x128 slot
hits = []
off = rdata[3]
end = rdata[3] + rdata[4] - 0x100
while off + 0x100 < rdata[3] + rdata[4]:
    down = struct.unpack_from("<Q", data, off)[0]
    if in_text(down):
        up = struct.unpack_from("<Q", data, off + 8)[0]
        mode = struct.unpack_from("<Q", data, off + 0xF8)[0]
        if in_text(up) and in_text(mode):
            slot = IMAGE + rdata[1] + (off - rdata[3])
            hits.append((slot - 0x128, down, up, mode))
    off += 8
print("vtable hits", len(hits))
md = Cs(CS_ARCH_X86, CS_MODE_64)
for base, down, up, mode in hits[:30]:
    print(f"vtable {base:012x} down={down:012x} up={up:012x} mode={mode:012x}")
    blob_off = va_off(up)
    blob = data[blob_off:blob_off+0x60]
    print("  up:")
    n = 0
    for ins in md.disasm(blob, up):
        print(f"    {ins.address:012x}  {ins.mnemonic} {ins.op_str}")
        n += 1
        if n >= 12 or ins.mnemonic in ("ret", "int3"):
            break
