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
rdata = next(s for s in sections if s[0] == ".rdata")
text_lo, text_hi = IMAGE + text[1], IMAGE + text[1] + text[2]

def va_off(va):
    rva = va - IMAGE
    for name, sva, vsize, raw, rawsize in sections:
        if sva <= rva < sva + rawsize:
            return raw + (rva - sva)
    return None

def in_text(p):
    return text_lo <= p < text_hi

md = Cs(CS_ARCH_X86, CS_MODE_64)
seen = set()
found = []
off = rdata[3]
end = rdata[3] + rdata[4] - 16
while off < end:
    down, up = struct.unpack_from("<QQ", data, off)
    if in_text(down) and in_text(up) and down != up and abs(up - down) < 0x600 and up not in seen:
        blob = data[va_off(up):va_off(up)+0x180]
        insns = list(md.disasm(blob, up))
        if len(insns) >= 8 and insns[0].mnemonic not in ("ret", "jmp", "int3"):
            textish = " ".join(i.mnemonic + " " + i.op_str for i in insns[:25])
            if any(tok in textish for tok in (" 1", ", 1", "0xffffffff", "inc ", "dec ")):
                seen.add(up)
                slot = IMAGE + rdata[1] + (off - rdata[3])
                found.append((slot - 0x128, down, up, insns[:18]))
    off += 8
print("candidates", len(found))
for base, down, up, insns in found[:40]:
    print(f"\nvtable {base:012x} down {down:012x} up {up:012x}")
    for ins in insns:
        print(f"  {ins.address:012x}  {ins.mnemonic} {ins.op_str}")
