import struct
from capstone import CS_ARCH_X86, CS_MODE_64, Cs

PATH = r"C:\Program Files\Steinberg\Cubase 15\Cubase15.exe"
IMAGE = 0x140000000

def load():
    data = open(PATH, "rb").read()
    pe = struct.unpack_from("<I", data, 0x3C)[0]
    magic = struct.unpack_from("<H", data, pe + 24)[0]
    if magic != 0x20B:
        raise SystemExit(f"unexpected magic {magic:#x}")
    nsec = struct.unpack_from("<H", data, pe + 6)[0]
    opt = struct.unpack_from("<H", data, pe + 20)[0]
    sec_off = pe + 24 + opt
    sections = []
    for i in range(nsec):
        off = sec_off + i * 40
        name = data[off:off + 8].split(b"\0", 1)[0].decode()
        vsize, va, rawsize, raw = struct.unpack_from("<IIII", data, off + 8)
        sections.append((name, va, vsize, raw, rawsize))
    return data, sections

def va_off(sections, va):
    rva = va - IMAGE
    for name, sva, vsize, raw, rawsize in sections:
        span = max(vsize, rawsize)
        if sva <= rva < sva + span:
            delta = rva - sva
            if delta >= rawsize:
                return None
            return raw + delta
    return None

def disasm(data, sections, va, count=120):
    md = Cs(CS_ARCH_X86, CS_MODE_64)
    md.detail = False
    off = va_off(sections, va)
    blob = data[off:off + 0x800]
    lines = []
    for ins in md.disasm(blob, va):
        lines.append(ins)
        if len(lines) >= count:
            break
        if ins.mnemonic == "int3":
            break
    return lines

def main():
    data, sections = load()
    print("== 0x14467e365 ==")
    for ins in disasm(data, sections, 0x14467E365, 90):
        print(f"{ins.address:012x}  {ins.mnemonic} {ins.op_str}")

if __name__ == "__main__":
    main()
