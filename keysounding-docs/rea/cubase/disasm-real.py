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
fn=0x144746fa0
n=0
for ins in md.disasm(data[va_off(fn):va_off(fn)+0x800], fn):
    interesting = ins.mnemonic in ("call","ret","jmp") or ins.mnemonic.startswith("j") or ins.mnemonic in ("inc","dec","add","sub","lea")
    if interesting:
        print(f"{ins.address:012x} {ins.mnemonic} {ins.op_str}")
    n+=1
    if ins.mnemonic=="ret" and ins.address>fn+0x10:
        break
    if n>200:
        print("truncated", hex(ins.address))
        break
