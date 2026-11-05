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
print("==== 1e0 target")
n=0
for ins in md.disasm(data[va_off(0x1419f6980):va_off(0x1419f6980)+0x40], 0x1419f6980):
    print(f"{ins.address:012x} {ins.bytes.hex():20} {ins.mnemonic} {ins.op_str}")
    n+=1
    if ins.mnemonic=="ret" or n>12:
        break
print("==== body")
for ins in md.disasm(data[va_off(0x144747020):va_off(0x144747020)+0x170], 0x144747020):
    print(f"{ins.address:012x} {ins.mnemonic} {ins.op_str}")
    if ins.address>=0x144747180:
        break
