import struct
PATH = r"C:\Program Files\Steinberg\Cubase 15\Cubase15.exe"
IMAGE = 0x140000000
data = open(PATH, "rb").read()
pe = struct.unpack_from("<I", data, 0x3C)[0]
nsec = struct.unpack_from("<H", data, pe + 6)[0]
opt = struct.unpack_from("<H", data, pe + 20)[0]
sec_off = pe + 24 + opt
text = None
for i in range(nsec):
    off = sec_off + i * 40
    name = data[off:off+8].split(b"\x00",1)[0]
    vsize, va, rawsize, raw = struct.unpack_from("<IIII", data, off + 8)
    if name == b".text":
        text = (va, raw, rawsize)
        break
va, raw, rawsize = text
blob = data[raw:raw+rawsize]
targets = {0x146081f7c: "Up", 0x146081f80: "Down", 0x145ef6c48: "Navigate"}
# also the pointer slots: search .rdata for the qword
i = 0
while i < len(blob) - 7:
    if blob[i:i+3] in (b"\x48\x8d\x15", b"\x48\x8b\x15", b"\x4c\x8d\x15"):
        disp = struct.unpack_from("<i", blob, i+3)[0]
        insn = IMAGE + va + i
        dest = insn + 7 + disp
        if dest in targets:
            print(hex(insn), blob[i:i+3].hex(), targets[dest])
    i += 1
