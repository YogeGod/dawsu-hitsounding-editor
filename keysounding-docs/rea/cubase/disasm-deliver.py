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
print("---- delivery target")
for ins in md.disasm(data[va_off(0x144b26d90):va_off(0x144b26d90)+0x18], 0x144b26d90):
    print(f"{ins.address:012x} {ins.bytes.hex()} {ins.mnemonic} {ins.op_str}")
print("---- around 0x144b199d1")
for ins in md.disasm(data[va_off(0x144b199b0):va_off(0x144b199b0)+0x40], 0x144b199b0):
    print(f"{ins.address:012x} {ins.bytes.hex()} {ins.mnemonic} {ins.op_str}")
