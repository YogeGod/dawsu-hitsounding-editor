import struct
from capstone import CS_ARCH_X86, CS_MODE_64, Cs
PATH = r"C:\Program Files\Steinberg\Cubase 15\Cubase15.exe"
IMAGE = 0x140000000
data = open(PATH,"rb").read()
pe = struct.unpack_from("<I", data, 0x3C)[0]
nsec = struct.unpack_from("<H", data, pe+6)[0]
opt = struct.unpack_from("<H", data, pe+20)[0]
sec = pe+24+opt
sections=[]
for i in range(nsec):
    off=sec+i*40
    vsize,va,rawsize,raw=struct.unpack_from("<IIII", data, off+8)
    sections.append((va,raw,rawsize,vsize))

def va_off(v):
    rva=v-IMAGE
    for sva,raw,rawsize,vsize in sections:
        if sva<=rva<sva+max(vsize,rawsize):
            return raw+(rva-sva)
md=Cs(CS_ARCH_X86, CS_MODE_64)
fn=0x141d2bf40
ins=next(md.disasm(data[va_off(fn):va_off(fn)+16], fn))
print("pro", ins.bytes.hex(), ins.mnemonic, ins.size)
n=0
for ins in md.disasm(data[va_off(fn):va_off(fn)+0x600], fn):
    if ins.mnemonic in ("call","ret","inc","dec") or (ins.mnemonic=="lea" and ("1" in ins.op_str)):
        print(f"{ins.address:012x} {ins.mnemonic} {ins.op_str}")
    n+=1
    if ins.mnemonic=="ret" and n>4:
        break
    if n>120:
        print("trunc", hex(ins.address))
        break
