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
    raise SystemExit(hex(va))

def read_cstr(va):
    off = va_off(va)
    if off is None:
        return None
    raw = data[off:off+96]
    if not raw or raw[0] < 32 or raw[0] > 126:
        return None
    s = raw.split(b"\x00", 1)[0]
    if len(s) < 1 or any(b < 32 or b > 126 for b in s):
        return None
    return s.decode("ascii")

start = 0x1437C39C6
print("bytes", data[va_off(start):va_off(start)+16].hex())
md = Cs(CS_ARCH_X86, CS_MODE_64)
last_ptr = None
n = 0
for ins in md.disasm(data[va_off(start):va_off(start)+0x500], start):
    extra = ""
    if ins.mnemonic == "mov" and "rip" in ins.op_str and "0x" in ins.op_str:
        tail = ins.op_str.split("rip")[-1]
        neg = "-" in tail and "+" not in tail
        disp = int(tail.split("0x")[-1].rstrip("]"), 16)
        if neg: disp = -disp
        slot = ins.address + ins.size + disp
        off = va_off(slot)
        if off is not None:
            ptr = struct.unpack_from("<Q", data, off)[0]
            s = read_cstr(ptr)
            extra = "  ; " + (s or hex(ptr))
            last_ptr = s or hex(ptr)
    if ins.mnemonic == "lea" and "rip" in ins.op_str and "0x" in ins.op_str:
        tail = ins.op_str.split("rip")[-1]
        neg = "-" in tail and "+" not in tail
        disp = int(tail.split("0x")[-1].rstrip("]"), 16)
        if neg: disp = -disp
        s = read_cstr(ins.address + ins.size + disp)
        if s: extra = "  ; " + s
    mark = ""
    if ins.mnemonic == "call" and ins.op_str == "0x141997a10":
        mark = "  <= " + str(last_ptr)
    print(f"{ins.address:012x}  {ins.mnemonic} {ins.op_str}{extra}{mark}")
    n += 1
    if n > 120:
        break
