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
    sections.append((va, vsize, raw, rawsize))

def va_off(va):
    rva = va - IMAGE
    for sva, vsize, raw, rawsize in sections:
        if sva <= rva < sva + max(vsize, rawsize):
            return raw + (rva - sva)
    return None

def read_str(va):
    off = va_off(va)
    if off is None:
        return None
    raw = data[off:off+48]
    if not raw or raw[0] < 32 or raw[0] > 126:
        return None
    s = raw.split(b"\x00", 1)[0]
    if len(s) < 2 or any(b < 32 or b > 126 for b in s):
        return None
    return s.decode("ascii")

start = 0x143783890
md = Cs(CS_ARCH_X86, CS_MODE_64)
md.detail = True
n = 0
names = []
for ins in md.disasm(data[va_off(start):va_off(start)+0x900], start):
    extra = ""
    if ins.mnemonic == "lea" and "rip" in ins.op_str:
        # op_str like "rdx, [rip + 0x123]"
        try:
            disp = int(ins.op_str.split("0x")[-1].rstrip("]"), 16)
            if "-" in ins.op_str.split("rip")[-1].split("]")[0] and "+" not in ins.op_str.split("rip")[-1]:
                disp = -disp
            nxt = ins.address + ins.size
            s = read_str(nxt + disp)
            if s:
                extra = "  ; " + s
                names.append((hex(ins.address), s, hex(nxt + disp)))
        except Exception as e:
            extra = "  ; " + str(e)
    print(f"{ins.address:012x}  {ins.mnemonic} {ins.op_str}{extra}")
    n += 1
    if ins.address > start and ins.mnemonic == "int3":
        break
    if n > 220:
        print("...")
        break
print("---NAMES---")
for item in names:
    print(item)
