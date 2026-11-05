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
# 12: 4C 8D 0D disp32
disp = struct.unpack_from("<i", b, 15)[0]
r9 = (start + 12 + 7 + disp) & 0xFFFFFFFFFFFFFFFF
print("r9", hex(r9))
# movzx at 23? print from 19
print("after lea", b[19:40].hex())
# 41 0F B6 84 01 imm32 = movzx eax, byte ptr [r9+rax+imm]
assert b[19:24] == bytes.fromhex("410fb68401")
imm = struct.unpack_from("<i", b, 24)[0]
byte_table = (r9 + imm) & 0xFFFFFFFFFFFFFFFF
print("byte table", hex(byte_table), "imm", hex(imm & 0xffffffff))
# 44 8B 84 81 imm32 = mov r8d, [r9+rax*4+imm]
assert b[28:33] == bytes.fromhex("448b8481")
imm2 = struct.unpack_from("<i", b, 33)[0]
dword_table = (r9 + imm2) & 0xFFFFFFFFFFFFFFFF
print("dword table", hex(dword_table))
bt = data[va_off(byte_table):va_off(byte_table)+0x12]
dt = data[va_off(dword_table):va_off(dword_table)+16]
print("bytes", bt.hex())
print("dwords", [hex(struct.unpack_from("<I", dt, i)[0]) for i in range(0,16,4)])
names = {1:"?",2:"?",7:"Left",8:"Right",9:"Up",10:"Down"}
for edx in range(1, 0x13):
    idx = edx - 1
    slot = bt[idx]
    rel = struct.unpack_from("<I", data, va_off(dword_table)+slot*4)[0]
    target = (r9 + rel) & 0xFFFFFFFFFFFFFFFF
    print(f"edx {edx:2d} {names.get(edx,''):6} byte {slot} -> {hex(target)}")
