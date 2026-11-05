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
lea=0x14485745b
b=data[va_off(lea):va_off(lea)+20]
print(b.hex())
# 48 8D 15 disp32
disp=struct.unpack_from("<i", data, va_off(lea)+3)[0]
base=(lea+7+disp) & 0xffffffffffffffff
print("base", hex(base))
# mov ecx, [rdx+rax*4+imm] at lea+7
op=data[va_off(lea)+7:va_off(lea)+14]
print("mov", op.hex())
imm=struct.unpack_from("<i", data, va_off(lea)+7+3)[0]
table=(base+imm)&0xffffffffffffffff
print("table", hex(table))
md=Cs(CS_ARCH_X86, CS_MODE_64)
names={12:"Up",14:"Down",11:"Left",13:"Right"}
seen={}
for cmd in range(3, 15):
    rel=struct.unpack_from("<i", data, va_off(table)+(cmd-3)*4)[0]
    tgt=(base+rel)&0xffffffffffffffff
    print(f"cmd {cmd:2d} {names.get(cmd,''):6} -> {hex(tgt)}")
    if cmd in names:
        seen[cmd]=tgt
for cmd,tgt in seen.items():
    print("====", names[cmd], hex(tgt))
    n=0
    for ins in md.disasm(data[va_off(tgt):va_off(tgt)+0x40], tgt):
        print(f"{ins.address:012x} {ins.mnemonic} {ins.op_str}")
        n+=1
        if ins.mnemonic in ("ret","jmp") and n>1:
            break
        if n>12:
            break
