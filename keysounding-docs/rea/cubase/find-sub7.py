import struct
from capstone import CS_ARCH_X86, CS_MODE_64, Cs
PATH = r"C:\Program Files\Steinberg\Cubase 15\Cubase15.exe"
IMAGE = 0x140000000
data = open(PATH, "rb").read()
pe = struct.unpack_from("<I", data, 0x3C)[0]
nsec = struct.unpack_from("<H", data, pe + 6)[0]
opt = struct.unpack_from("<H", data, pe + 20)[0]
sec_off = pe + 24 + opt
text = None
sections = []
for i in range(nsec):
    off = sec_off + i * 40
    name = data[off:off+8].split(b"\x00",1)[0]
    vsize, va, rawsize, raw = struct.unpack_from("<IIII", data, off + 8)
    sections.append((va, vsize, raw, rawsize))
    if name == b".text":
        text = (va, raw, rawsize)

def va_off(va):
    rva = va - IMAGE
    for sva, vsize, raw, rawsize in sections:
        if sva <= rva < sva + max(vsize, rawsize):
            return raw + (rva - sva)
    raise SystemExit(hex(va))

blob = data[text[1]:text[1]+text[2]]
base = IMAGE + text[0]
sig = b"\x83\xea\x07"  # sub edx, 7
i = 0
n = 0
while i < len(blob) - 3 and n < 30:
    j = blob.find(sig, i)
    if j < 0: break
    print(hex(base+j))
    n += 1
    i = j+1
print("count shown", n)
md = Cs(CS_ARCH_X86, CS_MODE_64)
print("---- sample")
start = 0x144b80420
for ins in md.disasm(data[va_off(start):va_off(start)+0x60], start):
    print(f"{ins.address:012x}  {ins.mnemonic} {ins.op_str}")
