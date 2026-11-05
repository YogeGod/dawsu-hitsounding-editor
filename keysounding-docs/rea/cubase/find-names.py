PATH = r"C:\Program Files\Steinberg\Cubase 15\Cubase15.exe"
data = open(PATH, "rb").read()
needles = [b"Nudge", b"Transpose", b"Semitone", b"Move Up", b"Pitch Up", b"Navigate", b"Set Note Pitch"]
for n in needles:
    idx = 0
    hits = []
    while len(hits) < 8:
        i = data.find(n, idx)
        if i < 0:
            break
        hits.append(i)
        idx = i + 1
    print(n, len(hits), hits[:8])
    # utf16
    u = n.decode().encode("utf-16le")
    idx = 0
    uh = []
    while len(uh) < 6:
        i = data.find(u, idx)
        if i < 0:
            break
        uh.append(i)
        idx = i + 1
    print("  utf16", uh[:6])
