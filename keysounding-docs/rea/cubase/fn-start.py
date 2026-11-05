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

# walk back from 0x1446e3a40 over int3 padding to the function prologue
off = va_off(0x1446E3A40)
blob = data[off-0x400:off]
# find last cc run
idx = len(blob) - 1
while idx > 0 and blob[idx] != 0xCC:
    idx -= 1
# function starts at the byte after the cc run
while idx > 0 and blob[idx] == 0xCC:
    idx -= 1
start = 0x1446E3A40 - (len(blob) - 1 - idx)
print("guess", hex(start+1))
md = Cs(CS_ARCH_X86, CS_MODE_64)
s = start + 1
for ins in md.disasm(data[va_off(s):va_off(s)+0x30], s):
    print(f"{ins.address:012x}  {ins.bytes.hex():20}  {ins.mnemonic} {ins.op_str}")
