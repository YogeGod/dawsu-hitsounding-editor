import struct
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
    sections.append((va, raw, rawsize, vsize))

def va_off(va):
    rva = va - IMAGE
    for sva, raw, rawsize, vsize in sections:
        if sva <= rva < sva + max(vsize, rawsize):
            return raw + (rva - sva)
    raise SystemExit(hex(va))

start = 0x1446e4200
b = data[va_off(start):va_off(start)+0x80]
print(b.hex())
# decode manually
# 48 83 EC 28
# 8D 42 FF          lea eax,[rdx-1]
# 83 F8 11          cmp eax, 0x11
# 0F 87 xx          ja
# 4C 8D 0D disp32   lea r9,[rip+disp]
p = 0
print("prologue", b[0:4].hex())
print("lea eax", b[4:7].hex(), "cmp", b[7:10].hex())
print("ja", b[10:16].hex())
assert b[16:19] == bytes.fromhex("4c8d0d")
disp = struct.unpack_from("<i", b, 19)[0]
lea_next = start + 16 + 7
r9 = lea_next + disp
print("r9 base", hex(r9), "disp", hex(disp))
# movzx eax, byte [r9+rax+imm]
# 41 0F B6 84 01 imm32   or similar
print("movzx bytes", b[23:32].hex())
