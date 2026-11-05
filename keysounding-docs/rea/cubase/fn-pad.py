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
    sections.append((va, vsize, raw, rawsize))

def va_off(va):
    rva = va - IMAGE
    for sva, vsize, raw, rawsize in sections:
        if sva <= rva < sva + max(vsize, rawsize):
            return raw + (rva - sva)
    raise SystemExit(hex(va))

end = va_off(0x1446E3A40)
blob = data[end-0x600:end]
# find runs of at least 2 cc
i = len(blob) - 1
found = []
while i > 1 and len(found) < 6:
    if blob[i] == 0xCC and blob[i-1] == 0xCC:
        j = i
        while j > 0 and blob[j] == 0xCC:
            j -= 1
        start = 0x1446E3A40 - (len(blob) - (j+1))
        found.append(start)
        i = j - 1
    else:
        i -= 1
print("candidates after padding")
for s in found:
    print(hex(s), data[va_off(s):va_off(s)+8].hex())
