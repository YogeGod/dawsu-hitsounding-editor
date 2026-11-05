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

r9 = 0x140000000
byte_table = 0x1446e4264
dword_table = 0x1446e4258
bt = data[va_off(byte_table):va_off(byte_table)+0x12]
names = {7:"Left",8:"Right",9:"Up",10:"Down"}
targets = set()
for edx in (7,8,9,10):
    slot = bt[edx-1]
    rel = struct.unpack_from("<I", data, va_off(dword_table)+slot*4)[0]
    target = r9 + rel
    targets.add(target)
    print(f"edx {edx} {names[edx]} slot {slot} -> {hex(target)}")
md = Cs(CS_ARCH_X86, CS_MODE_64)
for fn in sorted(targets):
    print("====", hex(fn))
    n = 0
    for ins in md.disasm(data[va_off(fn):va_off(fn)+0x180], fn):
        print(f"{ins.address:012x}  {ins.mnemonic} {ins.op_str}")
        n += 1
        if ins.mnemonic == "ret" or (ins.mnemonic == "jmp" and ins.op_str.startswith("0x")):
            break
        if n > 40:
            break
