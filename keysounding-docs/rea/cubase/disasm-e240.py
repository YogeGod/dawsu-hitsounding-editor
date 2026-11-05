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
print("---- tail of e240")
# find ret/jmp near the function by scanning until int3 run
raw=data[va_off(0x14467e240):va_off(0x14467e240)+0x280]
prev=None
for ins in md.disasm(raw, 0x14467e240):
    if ins.mnemonic in ("call","jmp","ret") or "0x190" in ins.op_str:
        print(f"{ins.address:012x} {ins.mnemonic} {ins.op_str}")
    if ins.mnemonic=="int3":
        break
