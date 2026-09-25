# Generates FLPresence assets without external dependencies:
#   assets/flpresence.ico           (app icon, 256/64/32/16 PNG entries)
#   assets/discord/*.png            (Rich Presence assets to upload in the Dev Portal)
# Pure-python PNG writer: flat geometric art, dark + FL-orange.
import struct, zlib, os, math

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

def png(width, height, pixel_fn):
    rows = bytearray()
    for y in range(height):
        rows.append(0)  # filter: none
        for x in range(width):
            r, g, b, a = pixel_fn(x, y)
            rows += bytes((r, g, b, a))
    def chunk(tag, data):
        c = struct.pack(">I", len(data)) + tag + data
        return c + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
    sig = b"\x89PNG\r\n\x1a\n"
    ihdr = struct.pack(">IIBBBBB", width, height, 8, 6, 0, 0, 0)
    return sig + chunk(b"IHDR", ihdr) + chunk(b"IDAT", zlib.compress(bytes(rows), 9)) + chunk(b"IEND", b"")

def ico(path, entries):
    data = b"\x00\x00\x01\x00" + struct.pack("<H", len(entries))
    offset = 6 + 16 * len(entries)
    blobs = []
    for size, img in entries:
        blobs.append((size, img))
    for size, img in blobs:
        w = size if size < 256 else 0
        data += struct.pack("<BBBBHHII", w, w, 0, 0, 1, 32, len(img), offset)
        offset += len(img)
    for size, img in blobs:
        data += img
    with open(path, "wb") as f:
        f.write(data)

BG = (24, 24, 28, 255)
DARK = (32, 32, 40, 255)
ORANGE = (255, 122, 47, 255)
ORANGE_DIM = (255, 122, 47, 90)

def canvas(s):
    def px(x, y):
        # rounded dark square background
        r = s * 0.14
        cx, cy = min(max(x, r), s - r), min(max(y, r), s - r)
        if (x - cx) ** 2 + (y - cy) ** 2 > r ** 2:
            return (0, 0, 0, 0)
        return None
    return px

def logo(size):
    s = size
    base = canvas(s)

    def px(x, y):
        b = base(x, y)
        if b: return b
        cx = cy = s / 2
        # outer knob ring
        d = math.hypot(x - cx, y - cy)
        if 0.30 * s < d < 0.36 * s:
            return ORANGE_DIM
        # indicator dot (like FL's knob pointer)
        ang = -math.pi / 4
        px_, py_ = cx + 0.33 * s * math.cos(ang), cy + 0.33 * s * math.sin(ang)
        if math.hypot(x - px_, y - py_) < 0.035 * s:
            return ORANGE
        # knob body
        if d < 0.27 * s:
            if d > 0.24 * s:
                return ORANGE
            return DARK if d < 0.22 * s else ORANGE
        # needle
        px_, py_ = cx + 0.10 * s * math.cos(ang), cy + 0.10 * s * math.sin(ang)
        if math.hypot(x - px_, y - py_) < 0.02 * s:
            return ORANGE
        return BG
    return px

def play(size):
    s = size; base = canvas(s)
    def px(x, y):
        b = base(x, y)
        if b: return b
        u, v = (x - s * 0.34) / (s * 0.44), (y - s * 0.18) / (s * 0.64)
        return ORANGE if (0 <= u <= 1 and 0 <= v <= 1 and u >= abs(v - 0.5) * 2 * 0.92) else BG
    return px

def stop(size):
    s = size; base = canvas(s); pad = 0.22 * s
    def px(x, y):
        b = base(x, y)
        if b: return b
        return ORANGE if (pad < x < s - pad and pad < y < s - pad) else BG
    return px

def record(size):
    s = size; base = canvas(s); cx = cy = s / 2; r = 0.26 * s
    def px(x, y):
        b = base(x, y)
        if b: return b
        return ORANGE if math.hypot(x - cx, y - cy) < r else BG
    return px

def midi_note(size):
    s = size; base = canvas(s)
    def px(x, y):
        b = base(x, y)
        if b: return b
        # eighth note: head + stem + flag
        hx, hy, hr = s * 0.38, s * 0.72, s * 0.13
        if math.hypot(x - hx, y - hy) < hr:
            return ORANGE
        if abs(x - (hx + hr * 0.85)) < 0.035 * s and s * 0.22 < y <= hy:
            return ORANGE
        if hx + hr * 0.85 < x < s * 0.72 and s * 0.22 < y < s * 0.40:
            return ORANGE
        return BG
    return px

def main():
    os.makedirs(f"{ROOT}/assets/discord", exist_ok=True)
    with open(f"{ROOT}/assets/flpresence.ico", "wb") as f:
        pass
    entries = [(sz, png(sz, sz, logo(sz))) for sz in (256, 64, 32, 16)]
    ico(f"{ROOT}/assets/flpresence.ico", entries)
    with open(f"{ROOT}/assets/discord/flpresence.png", "wb") as f:
        f.write(png(1024, 1024, logo(1024)))
    for name, fn in (("play", play), ("stop", stop), ("record", record), ("midi", midi_note)):
        with open(f"{ROOT}/assets/discord/{name}.png", "wb") as f:
            f.write(png(512, 512, fn(512)))
    print("assets written:", sorted(os.listdir(f"{ROOT}/assets/discord")), "+ flpresence.ico")

if __name__ == "__main__":
    main()
