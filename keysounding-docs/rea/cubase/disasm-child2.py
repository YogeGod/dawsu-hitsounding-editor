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

def read_cstr(va):
    off = va_off(va)
    if off is None: return None
    raw = data[off:off+80]
    if not raw or raw[0] < 32 or raw[0] > 126: return None
    s = raw.split(b"\x00", 1)[0]
    if len(s) < 1 or any(b < 32 or b > 126 for b in s): return None
    return s.decode("ascii")

start = 0x141AF0DB0
blob = data[va_off(start):va_off(start)+0x2000]
print("entry", blob[:8].hex())
# find ret;int3
end = None
for i in range(16, len(blob)-1):
    if blob[i] == 0xC3 and blob[i+1] == 0xCC:
        end = start + i
        break
print("first ret", hex(end) if end else None)
md = Cs(CS_ARCH_X86, CS_MODE_64)
n = 0
names = []
for ins in md.disasm(data[va_off(start):va_off(start)+0x800], start):
    extra = ""
    if ins.mnemonic in ("lea","mov") and "rip" in ins.op_str and "0x" in ins.op_str:
        tail = ins.op_str.split("rip")[-1]
        neg = "-" in tail and "+" not in tail
        disp = int(tail.split("0x")[-1].rstrip("]"), 16)
        if neg: disp = -disp
        s = read_cstr(ins.address + ins.size + disp)
        if s is None and ins.mnemonic == "mov":
            off = va_off(ins.address + ins.size + disp)
            if off is not None:
                ptr = struct.unpack_from("<Q", data, off)[0]
                s = read_cstr(ptr)
                if s: s = "-> " + s
        if s:
            extra = "  ; " + s
            names.append(s)
    if extra or ins.mnemonic in ("call","ret","jmp"):
        print(f"{ins.address:012x}  {ins.mnemonic} {ins.op_str}{extra}")
    n += 1
    if ins.address > start and ins.mnemonic == "int3":
        break
    if n > 180:
        print("...")
        break
print("NAMES", names)
