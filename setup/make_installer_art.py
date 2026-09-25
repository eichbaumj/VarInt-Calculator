"""
Builds the Varint Calculator installer's wizard art: the header band across the top of
the inner pages (VarintCalculator_Header.bmp, 1491x174) and the panel on the finished
page (VarintCalculator_Welcome.bmp, 410x785). Inno Setup needs 24-bit BMPs.

The method is Firefly's (_gen_firefly_installer_art.py in the suite): start from the
pristine blue-wave bitmaps (setup/art/wave-*-base.bmp, the originals Firefly's art is
repainted from), solve the wave's crossfade from a region with no artwork, repaint only
the bands that change, and place every logo by its ink, not its canvas. The script
refuses to run if the regenerated wave does not match the original, so new art can never
land on a subtly wrong background.

What goes on it: Elusive Data branding, the new ED mark (cut out of
setup/art/elusive-data-mark.webp) with the ELUSIVE DATA word art (Images/
elusive-data-wordmark.png), and on the finished page the product name in the app's own
wordmark type (Saira, from Fonts/).

    python setup/make_installer_art.py
"""
import os

import numpy as np
from PIL import Image, ImageDraw, ImageFilter, ImageFont
from scipy import ndimage

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
ART = os.path.join(HERE, "art")

# --- the blue wave, verbatim from Firefly's AboutWindow.xaml -------------------
TOP = [(0.00, '00148E'), (0.10, '0024B9'), (0.18, '1953E0'), (0.28, '4199F1'), (0.40, '134CE6'),
       (0.52, '001FAD'), (0.64, '0635CB'), (0.78, '2F78ED'), (0.90, '3E94EF'), (1.00, '439CF1')]
BOT = [(0.00, '0028C8'), (0.10, '0018A0'), (0.18, '0023B9'), (0.28, '1046DC'), (0.40, '144DE5'),
       (0.52, '154DDB'), (0.64, '2A6FE2'), (0.78, '56C1F5'), (0.90, '65DBF7'), (1.00, '55BEF6')]


def ramp(stops, n):
    out = np.zeros((n, 3))
    for i in range(n):
        t = i / max(1, n - 1)
        for j in range(len(stops) - 1):
            o0, v0 = stops[j]
            o1, v1 = stops[j + 1]
            if o0 <= t <= o1:
                f = 0.0 if o1 == o0 else (t - o0) / (o1 - o0)
                a = [int(v0[k:k + 2], 16) for k in (0, 2, 4)]
                b = [int(v1[k:k + 2], 16) for k in (0, 2, 4)]
                out[i] = [a[k] + (b[k] - a[k]) * f for k in range(3)]
                break
    return out


def header_bg(orig, clean_x):
    """Profile along x; the crossfade to the bottom profile runs down y (solved per row)."""
    h, w, _ = orig.shape
    top, bot = ramp(TOP, w), ramp(BOT, w)
    a = np.zeros(h)
    for y in range(h):
        num = (orig[y, clean_x, :] - top[clean_x]).ravel()
        den = (bot[clean_x] - top[clean_x]).ravel()
        keep = np.abs(den) > 8
        a[y] = (num[keep] / den[keep]).mean()
    a = a[:, None, None]
    return top[None, :, :] * (1 - a) + bot[None, :, :] * a


def welcome_bg(orig, clean_y):
    """Rotated 90 degrees: profile down y, crossfade across x (solved per column)."""
    h, w, _ = orig.shape
    top, bot = ramp(TOP, h), ramp(BOT, h)
    a = np.zeros(w)
    for x in range(w):
        num = (orig[clean_y, x, :] - bot[clean_y]).ravel()
        den = (top[clean_y] - bot[clean_y]).ravel()
        keep = np.abs(den) > 8
        a[x] = (num[keep] / den[keep]).mean()
    a = a[None, :, None]
    return bot[:, None, :] * (1 - a) + top[:, None, :] * a


def assert_bg_matches(orig, bg, region, label):
    ys, ye, xs, xe = region
    d = np.abs(orig[ys:ye, xs:xe] - np.round(bg[ys:ye, xs:xe]).astype(int))
    if d.max() > 2:
        raise SystemExit("%s: the regenerated wave does not match the base art (max delta %d)." % (label, d.max()))
    print("  wave check OK (%s): max delta %d" % (label, d.max()))


def repaint(orig, bg, y0, y1, x0=0, x1=None):
    x1 = orig.shape[1] if x1 is None else x1
    out = orig.copy()
    out[y0:y1, x0:x1] = np.round(bg[y0:y1, x0:x1]).astype(int)
    return out


# --- logos --------------------------------------------------------------------

def ed_mark():
    """The white ED mark, cut out of the new logo (a white mark on a blue field).

    The field's red channel never rises above 28 while the mark is white, so alpha is the
    red channel measured against a local estimate of the field (normalized convolution of
    the field pixels, which keeps the antialiased edge exact).
    """
    rgb = np.asarray(Image.open(os.path.join(ART, "elusive-data-mark.webp")).convert("RGB")).astype(float)
    r = rgb[:, :, 0]
    glyph = ndimage.binary_dilation(r > 60, iterations=6)
    field = np.where(glyph, 0.0, r)
    weight = (~glyph).astype(float)
    sigma = 25
    local = ndimage.gaussian_filter(field, sigma) / np.maximum(ndimage.gaussian_filter(weight, sigma), 1e-6)
    alpha = np.clip((r - local) / (255.0 - local), 0.0, 1.0)
    alpha[alpha < 0.03] = 0.0
    out = np.zeros(rgb.shape[:2] + (4,), dtype=np.uint8)
    out[:, :, :3] = 255
    out[:, :, 3] = np.round(alpha * 255).astype(np.uint8)
    return Image.fromarray(out, "RGBA")


def ink_bbox(im):
    a = np.asarray(im)
    ys, xs = np.nonzero(a[:, :, 3] > 16)
    return xs.min(), ys.min(), xs.max(), ys.max()


def scaled_to_ink(im, ink_w=None, ink_h=None):
    x0, y0, x1, y1 = ink_bbox(im)
    im = im.crop((x0, y0, x1 + 1, y1 + 1))
    scale = (ink_w / im.width) if ink_w else (ink_h / im.height)
    return im.resize((max(1, round(im.width * scale)), max(1, round(im.height * scale))), Image.Resampling.LANCZOS)


def paste(canvas, logo, left=None, cx=None, cy=None, top=None):
    x = round(left if left is not None else cx - logo.width / 2)
    y = round(top if top is not None else cy - logo.height / 2)
    canvas.paste(logo, (int(x), int(y)), logo)


def wordmark_text(parts, size):
    """Draws text in runs of (text, font file) on a transparent canvas, cropped to its ink."""
    fonts = [(t, ImageFont.truetype(os.path.join(ROOT, "Fonts", f), size)) for t, f in parts]
    widths = [f.getlength(t) for t, f in fonts]
    im = Image.new("RGBA", (int(sum(widths)) + size, size * 2), (255, 255, 255, 0))
    d = ImageDraw.Draw(im)
    x = size // 2
    for (t, f), w in zip(fonts, widths):
        d.text((x, size // 2), t, font=f, fill=(255, 255, 255, 255))
        x += w
    x0, y0, x1, y1 = ink_bbox(im)
    return im.crop((x0, y0, x1 + 1, y1 + 1))


def main():
    mark = ed_mark()
    word = Image.open(os.path.join(ROOT, "Images", "elusive-data-wordmark.png")).convert("RGBA")

    # --- header band -------------------------------------------------------------
    orig = np.asarray(Image.open(os.path.join(ART, "wave-header-base.bmp")).convert("RGB")).astype(int)
    h, w, _ = orig.shape
    bg = header_bg(orig, np.arange(950, w))            # right of the base art's word art
    assert_bg_matches(orig, bg, (0, h, 950, w), "header")
    band = Image.fromarray(repaint(orig, bg, 0, h, 0, 1300).astype(np.uint8))
    # The base art's word art sat at left 65 with ink height 56, centred on y 86.5; the
    # lockup keeps that line and puts the mark in front of it.
    m = scaled_to_ink(mark, ink_h=96)
    wd = scaled_to_ink(word, ink_h=56)
    paste(band, m, left=65, cy=86.5)
    paste(band, wd, left=65 + m.width + 42, cy=86.5)
    band.save(os.path.join(HERE, "VarintCalculator_Header.bmp"))
    print("wrote VarintCalculator_Header.bmp", band.size, "mark", m.size, "word art", wd.size)

    # --- finished-page panel -------------------------------------------------------
    orig = np.asarray(Image.open(os.path.join(ART, "wave-welcome-base.bmp")).convert("RGB")).astype(int)
    h, w, _ = orig.shape
    bg = welcome_bg(orig, np.arange(450, 660))         # below the logo, above the tagline
    assert_bg_matches(orig, bg, (450, 660, 0, w), "panel")
    # The foot of the panel is repainted too (the base art's tagline is Firefly's), so the
    # wave must also match below and beside that tagline.
    assert_bg_matches(orig, bg, (762, h, 0, w), "panel foot")
    assert_bg_matches(orig, bg, (660, h, 0, 60), "panel foot, left margin")
    panel = Image.fromarray(repaint(repaint(orig, bg, 150, 470), bg, 660, h).astype(np.uint8))

    # The new logo's own proportions: the word art is about 1.6 times the mark's width,
    # with a gap of about a fifth of the mark's height.
    m = scaled_to_ink(mark, ink_h=150)
    wd = scaled_to_ink(word, ink_w=round(m.width * 1.58))
    gap = round(m.height * 0.21)
    block = m.height + gap + wd.height
    top = 300 - block / 2
    paste(panel, m, cx=w / 2, top=top)
    paste(panel, wd, cx=w / 2, top=top + m.height + gap)

    name = wordmark_text([("Varint", "Saira-SemiBold.ttf"), (" Calculator", "Saira-Regular.ttf")], 44)
    name = scaled_to_ink(name, ink_w=270)
    paste(panel, name, cx=w / 2, cy=716)
    panel.save(os.path.join(HERE, "VarintCalculator_Welcome.bmp"))
    print("wrote VarintCalculator_Welcome.bmp", panel.size, "mark", m.size, "word art", wd.size, "name", name.size)

    # Previews for review (not used by the installer).
    band.save(os.path.join(ART, "preview-header.png"))
    panel.save(os.path.join(ART, "preview-panel.png"))


if __name__ == "__main__":
    main()
