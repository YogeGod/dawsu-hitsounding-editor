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
    sections.append((va, raw, rawsize))

def off_va(file_off):
    for va, raw, rawsize in sections:
        if raw <= file_off < raw + rawsize:
            return IMAGE + va + (file_off - raw)
    return None

needles = [
    bytes.fromhex("807A020C"),
    bytes.fromhex("8079020C"),
    bytes.fromhex("8078020C"),
    bytes.fromhex("807B020C"),
    bytes.fromhex("807E020C"),
    bytes.fromhex("807F020C"),
    bytes.fromhex("418078020C"),
    bytes.fromhex("418079020C"),
    bytes.fromhex("0FB6420283F80C"),
    bytes.fromhex("0FB64A0283F90C"),
    bytes.fromhex("0FB6420280F80C"),
]
hits = []
for needle in needles:
    start = 0
    while True:
        i = data.find(needle, start)
        if i < 0:
            break
        va = off_va(i)
        if va:
            hits.append((va, needle.hex()))
        start = i + 1
print("hits", len(hits))
for va, hx in hits:
    print(f"{va:012x}  {hx}")
