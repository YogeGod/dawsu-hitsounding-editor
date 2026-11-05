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

start = 0x141A505D0
blob = data[va_off(start):va_off(start)+0x2200]
print("mov eax,1 at:")
sig = b"\xb8\x01\x00\x00\x00"
i = 0
while True:
    j = blob.find(sig, i)
    if j < 0: break
    print(hex(start+j))
    i = j+1
md = Cs(CS_ARCH_X86, CS_MODE_64)
print("---- epilogue")
for ins in md.disasm(data[va_off(0x141A50820):va_off(0x141A50820)+0x30], 0x141A50820):
    print(f"{ins.address:012x}  {ins.bytes.hex():20}  {ins.mnemonic} {ins.op_str}")
    if ins.mnemonic == "ret":
        break
