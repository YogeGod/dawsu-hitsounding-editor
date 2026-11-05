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
    raise SystemExit("no section")

target = 0x1447C81E1
off = va_off(target)
start = off
while start > off - 0x400:
    if data[start - 1] == 0xCC and data[start] != 0xCC:
        break
    start -= 1
va = target - (off - start)
print(f"function-ish {va:012x}")
md = Cs(CS_ARCH_X86, CS_MODE_64)
blob = data[start:start + 0x500]
for ins in md.disasm(blob, va):
    mark = " <<" if ins.address == target else ""
    print(f"{ins.address:012x}  {ins.mnemonic} {ins.op_str}{mark}")
    if ins.address > target and ins.mnemonic == "int3":
        break
