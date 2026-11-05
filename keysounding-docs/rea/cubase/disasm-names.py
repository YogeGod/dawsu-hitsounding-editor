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
    vsize, va, rawsize, raw = struct.unpack_from("<IIII", data, off + 8)
    sections.append((va, raw, rawsize, vsize))

def va_off(va):
    rva = va - IMAGE
    for sva, raw, rawsize, vsize in sections:
        if sva <= rva < sva + max(vsize, rawsize):
            return raw + (rva - sva)
    raise SystemExit(hex(va))

def cstr(va):
    o = va_off(va)
    return data[o:o+32].split(b"\x00",1)[0].decode("ascii", "replace")

md = Cs(CS_ARCH_X86, CS_MODE_64)
start = 0x1446e3a40
for ins in md.disasm(data[va_off(start):va_off(start)+0x250], start):
    line = f"{ins.address:012x}  {ins.mnemonic} {ins.op_str}"
    if ins.mnemonic == "lea" and "rip" in ins.op_str:
        disp = struct.unpack_from("<i", data, va_off(ins.address)+3)[0]
        dest = ins.address + ins.size + disp
        line += "  ; " + cstr(dest)
    if ins.address >= 0x1446e3c70 and ins.mnemonic == "ret":
        print(line)
        break
    if ins.address < 0x1446e3c80:
        print(line)
