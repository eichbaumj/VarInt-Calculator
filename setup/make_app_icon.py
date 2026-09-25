"""
Builds icon.ico, the Varint Calculator's application icon, from the V icon of the Apple
version (setup/art/varint-v-1024.png, the App Store icon: a white V on a red-to-orange
gradient).

Every frame is drawn at its own size rather than shrunk from the 1024 image:

  * The gradient is redrawn at each size (horizontal, #FF3131 to #FF914D, sampled from
    the source) inside a rounded square, so the edges stay clean.
  * The V is lifted from the source as an alpha mask and scaled down with area
    averaging. Its strokes are 38 px at 1024, under one pixel at 24 px and below, so the
    small frames (16, 20, 24) use strokes thickened before scaling: the V keeps its
    outline shape but stays readable in the taskbar and in Explorer's small views.

Frames: 16, 20, 24, 32, 40, 48, 64, 96, 128 as 32-bit BMP, 256 as PNG, the layout the
Windows shell and Inno Setup both read.

    python setup/make_app_icon.py
"""
import io
import os
import struct

import numpy as np
from PIL import Image, ImageDraw
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
SOURCE = os.path.join(HERE, "art", "varint-v-1024.png")
OUT = os.path.join(ROOT, "icon.ico")
PREVIEW = os.path.join(HERE, "art", "icon-preview.png")

SIZES = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256]


def load_source():
    rgb = np.asarray(Image.open(SOURCE).convert("RGB")).astype(float)
    # The gradient runs left to right and the V never reaches the top 150 rows, so those
    # rows give the background colour of every column.
    bg = rgb[:150].mean(axis=0)                     # (1024, 3)
    left, right = bg[:8].mean(axis=0), bg[-8:].mean(axis=0)
    # Alpha of the white glyph over the known background, from the green channel (the
    # channel with the most room between the gradient and white).
    g_bg = bg[:, 1][None, :]
    alpha = np.clip((rgb[:, :, 1] - g_bg) / (255.0 - g_bg), 0.0, 1.0)
    return alpha, tuple(left), tuple(right)


def thicken(alpha, radius):
    """Grows the V's strokes by `radius` source pixels, keeping their antialiased edge."""
    if radius <= 0:
        return alpha
    inside = alpha > 0.5
    dist = ndimage.distance_transform_edt(~inside)
    return np.clip(radius + 0.5 - dist, 0.0, 1.0) if radius else alpha


def area_resize(mask, size):
    """Box-filter downscale: every output pixel is the mean of the source pixels under it."""
    img = Image.fromarray((mask * 255).astype(np.uint8), "L")
    return np.asarray(img.resize((size, size), Image.Resampling.BOX)).astype(float) / 255.0


def rounded_square_mask(size, margin, radius, supersample=8):
    big = size * supersample
    m = Image.new("L", (big, big), 0)
    ImageDraw.Draw(m).rounded_rectangle(
        [margin * supersample, margin * supersample, big - 1 - margin * supersample, big - 1 - margin * supersample],
        radius=radius * supersample, fill=255)
    return np.asarray(m.resize((size, size), Image.Resampling.BOX)).astype(float) / 255.0


def frame(size, alpha_src, left, right):
    margin = 0 if size <= 24 else max(1, round(size / 32))
    inner = size - 2 * margin
    radius = inner * 0.2

    # Strokes: 38 source px. Aim for about 1.3 px at the small sizes.
    scale = inner / 1024.0
    stroke_px = 38 * scale
    grow = 0 if stroke_px >= 1.25 else (1.3 / scale - 38) / 2
    glyph = area_resize(thicken(alpha_src, grow), inner)

    x = np.linspace(0.0, 1.0, inner)[None, :, None]
    grad = np.array(left)[None, None, :] * (1 - x) + np.array(right)[None, None, :] * x
    grad = np.repeat(grad, inner, axis=0)
    rgb = grad * (1 - glyph[:, :, None]) + 255.0 * glyph[:, :, None]

    shape = rounded_square_mask(size, margin, radius)
    out = np.zeros((size, size, 4))
    out[margin:margin + inner, margin:margin + inner, :3] = rgb
    out[:, :, 3] = shape * 255.0
    return Image.fromarray(np.round(out).astype(np.uint8), "RGBA")


def bmp_frame(img):
    """A 32-bit ICO frame: BITMAPINFOHEADER, bottom-up BGRA rows, then the 1-bit AND mask."""
    w, h = img.size
    a = np.asarray(img)
    bgra = a[::-1, :, [2, 1, 0, 3]].tobytes()
    row_bytes = ((w + 31) // 32) * 4
    and_mask = bytearray(row_bytes * h)
    for y in range(h):
        src_row = a[h - 1 - y, :, 3]
        for x in range(w):
            if src_row[x] == 0:
                and_mask[y * row_bytes + x // 8] |= 0x80 >> (x % 8)
    header = struct.pack("<IiiHHIIiiII", 40, w, h * 2, 1, 32, 0, len(bgra) + len(and_mask), 0, 0, 0, 0)
    return header + bgra + bytes(and_mask)


def png_frame(img):
    buf = io.BytesIO()
    img.save(buf, "PNG", optimize=True)
    return buf.getvalue()


def write_ico(frames, path):
    blobs = [png_frame(im) if im.width >= 256 else bmp_frame(im) for im in frames]
    offset = 6 + 16 * len(frames)
    head = struct.pack("<HHH", 0, 1, len(frames))
    entries = b""
    for im, blob in zip(frames, blobs):
        w = 0 if im.width >= 256 else im.width
        entries += struct.pack("<BBBBHHII", w, w, 0, 0, 1, 32, len(blob), offset)
        offset += len(blob)
    with open(path, "wb") as f:
        f.write(head + entries + b"".join(blobs))


def main():
    alpha, left, right = load_source()
    frames = [frame(s, alpha, left, right) for s in SIZES]
    write_ico(frames, OUT)
    print("wrote", OUT, os.path.getsize(OUT), "bytes;", "gradient", tuple(round(c) for c in left), "->", tuple(round(c) for c in right))

    # Preview: every frame at 1x on light and dark, then enlarged 4x so the pixels show.
    pad = 12
    width = sum(s for s in SIZES) + pad * (len(SIZES) + 1)
    sheet = Image.new("RGBA", (width, (256 + 2 * pad) * 2 + 4 * 32 + pad * 2), (255, 255, 255, 255))
    for row, bgc in enumerate([(243, 243, 243, 255), (32, 32, 32, 255)]):
        y0 = row * (256 + 2 * pad)
        sheet.paste(Image.new("RGBA", (width, 256 + 2 * pad), bgc), (0, y0))
        x = pad
        for im in frames:
            sheet.alpha_composite(im, (x, y0 + pad + (256 - im.height)))
            x += im.width + pad
    x = pad
    y0 = 2 * (256 + 2 * pad) + pad
    for im in frames[:4]:
        big = im.resize((im.width * 4, im.height * 4), Image.Resampling.NEAREST)
        sheet.alpha_composite(big, (x, y0))
        x += big.width + pad
    sheet.save(PREVIEW)
    print("wrote", PREVIEW)


if __name__ == "__main__":
    main()
