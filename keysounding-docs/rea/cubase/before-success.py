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
    if off is None: return None
    raw = data[off:off+64]
    if not raw or raw[0] < 32 or raw[0] > 126: return None
    s = raw.split(b"\x00", 1)[0]
    if not s or any(b < 32 or b > 126 for b in s): return None
    return s.decode("ascii")

md = Cs(CS_ARCH_X86, CS_MODE_64)
addrs = [0x141a5076c,0x141a507ad,0x141a509d4,0x141a50b6e,0x141a50e5e,0x141a50eb7,0x141a5112b,0x141a51342,0x141a5147f,0x141a514d0,0x141a5160e,0x141a51647,0x141a517fa,0x141a51a6f,0x141a51b59,0x141a51b85,0x141a51e2f]
for a in addrs:
    print("====", hex(a))
    window = data[va_off(a-0x40):va_off(a)]
    # disassemble the 0x40 bytes before, may be misaligned; walk calls only via capstone from a-0x40 and ignore errors by starting at several offsets
    start = a - 0x28
    for ins in md.disasm(data[va_off(start):va_off(a+5)], start):
        if ins.address >= a:
            break
        extra = ""
        if ins.mnemonic in ("lea","mov") and "rip" in ins.op_str and "0x" in ins.op_str:
            tail = ins.op_str.split("rip")[-1]
            if "0x" in tail:
                neg = "-" in tail and "+" not in tail
                disp = int(tail.split("0x")[-1].rstrip("]"), 16)
                if neg: disp = -disp
                s = read_cstr(ins.address + ins.size + disp)
                if s: extra = "  ; " + s
        if ins.mnemonic in ("call","lea") or extra:
            print(f"  {ins.address:012x}  {ins.mnemonic} {ins.op_str}{extra}")
