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
    if off is None:
        return None
    raw = data[off:off+80]
    if not raw or raw[0] < 32 or raw[0] > 126:
        return None
    s = raw.split(b"\x00", 1)[0]
    if len(s) < 1 or any(b < 32 or b > 126 for b in s):
        return None
    return s.decode("ascii")

start = 0x143783E49
end = 0x143784980
md = Cs(CS_ARCH_X86, CS_MODE_64)
last_ptr = None
last_at = None
for ins in md.disasm(data[va_off(start):va_off(end)], start):
    if ins.mnemonic == "mov" and "rip" in ins.op_str and ins.op_str.startswith("r"):
        tail = ins.op_str.split("rip")[-1]
        neg = "-" in tail and "+" not in tail
        disp = int(tail.split("0x")[-1].rstrip("]"), 16)
        if neg:
            disp = -disp
        slot = ins.address + ins.size + disp
        off = va_off(slot)
        if off is None:
            last_ptr = None
            continue
        ptr = struct.unpack_from("<Q", data, off)[0]
        last_ptr = ptr
        last_at = ins.address
    if ins.mnemonic == "call" and ins.op_str == "0x141997a10" and last_ptr:
        s = read_cstr(last_ptr)
        # also try pointer+8/+16 as a string object
        extra = ""
        if s is None:
            for add in (0, 8, 0x10, 0x18, 0x20):
                t = read_cstr(last_ptr + add)
                if t:
                    extra = f" +{hex(add)} {t}"
                    break
            if not extra:
                off = va_off(last_ptr)
                raw = data[off:off+32].hex() if off is not None else "?"
                extra = " bytes " + raw
        print(hex(last_at), hex(last_ptr), s or extra)
        last_ptr = None
