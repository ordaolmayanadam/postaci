# Writes multi-size .ico files: BMP (32-bit BGRA + AND mask) entries up to 128 px, PNG for 256 px.
import io, struct, sys
from PIL import Image

def bmp_entry(im: Image.Image) -> bytes:
    w, h = im.size
    px = im.convert("RGBA").load()
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, 0, 0, 0, 0, 0)
    xor = bytearray()
    for y in range(h - 1, -1, -1):
        for x in range(w):
            r, g, b, a = px[x, y]
            xor += bytes((b, g, r, a))
    row = ((w + 31) // 32) * 4  # AND mask rows are 32-bit aligned; all zero = use alpha
    return header + bytes(xor) + bytes(row * h)

def png_entry(im: Image.Image) -> bytes:
    buf = io.BytesIO()
    im.save(buf, "PNG")
    return buf.getvalue()

def write_ico(path, images):
    entries = [(im, png_entry(im) if im.size[0] >= 256 else bmp_entry(im)) for im in images]
    out = bytearray(struct.pack("<HHH", 0, 1, len(entries)))
    offset = 6 + 16 * len(entries)
    for im, data in entries:
        w, h = im.size
        out += struct.pack("<BBBBHHII", w % 256, h % 256, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
    for _, data in entries:
        out += data
    open(path, "wb").write(out)

sizes = [16, 20, 24, 32, 48, 64, 128, 256]
for base, name in (("postaci", "Postaci.ico"), ("postaci-unread", "Postaci-unread.ico")):
    write_ico(name, [Image.open(f"{base}-{s}.png").convert("RGBA") for s in sizes])
