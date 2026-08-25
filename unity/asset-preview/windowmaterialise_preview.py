# windowmaterialise_preview.py - photograph the window-materialise effect WITHOUT a headset.
#
# WHAT THIS PRODUCES, and why each piece is there rather than "a nice picture":
#
#   appear_strip.png / vanish_strip.png   evenly sampled frames across the FULL duration, each
#                                         labelled with its elapsed time and its progress, so a
#                                         reviewer can say "at 0.30 s it looks like X" instead of
#                                         "it looks nice".
#   appear.mp4 / vanish.mp4               the same thing at real time (60 fps, real durations), so
#                                         the question "does 1.0 s feel like it holds you up?" is
#                                         answerable rather than argued.
#   stereo_audit.txt                      a MECHANICAL check of the shipped shader source for every
#                                         per-eye-unstable input. This is the deliverable that
#                                         actually settles the stereo-rivalry question; two rendered
#                                         eye images cannot, because a screen-space dissolve looks
#                                         perfectly fine in each eye SEPARATELY - that is the whole
#                                         trap. What makes it flicker is that the two eyes disagree
#                                         about the same surface point, and the only way to know
#                                         they cannot is that no per-eye input reaches the field.
#
# THE HALF THIS CANNOT SHOW is the real game's windows. There is no game install on this machine, so
# the panel below is a SYNTHETIC stand-in built to the shape of a Gloomhaven dialog (plate, title
# bar, portrait, text rows, two buttons, an X). Its element rectangles are what drive the
# element-granularity dissolve, exactly as the real CanvasRenderers do - so what the strip shows
# about the MECHANISM (a wave along the wind, big elements dissolving across the whole sweep, small
# ones snapping) is true; what it shows about the CONTENT is a guess. Say so when you send it on.
#
# RUN:
#   python3 unity/asset-preview/windowmaterialise_preview.py [outdir]
import json
import os
import pathlib
import re
import subprocess
import sys

import numpy as np
from PIL import Image, ImageDraw, ImageFont

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
from windowmaterialise_field import Field, F32, CONSTANTS  # noqa: E402

ROOT = pathlib.Path(__file__).resolve().parents[2]
SHADER = ROOT / "unity" / "GloomhavenVR.Assets" / "Assets" / "Bundle" / "Table" / "WindowMaterialise.shader"

# Durations: the SHIPPED defaults, read out of Defaults.WorldUI.cs so the preview cannot show a
# timing the mod does not have.
DEFAULTS = ROOT / "src" / "GloomhavenVR" / "Defaults" / "Defaults.WorldUI.cs"


def shipped_seconds(key):
    m = re.search(r"internal const float WindowMaterialise%sSeconds = ([0-9.]+)f;" % key,
                  DEFAULTS.read_text(encoding="utf-8"))
    if not m:
        raise SystemExit("could not read WindowMaterialise%sSeconds out of %s" % (key, DEFAULTS))
    return float(m.group(1))


PANEL_W, PANEL_H = 620, 400          # preview pixels; the real panel is canvas units
FPS = 60
BG = (0.055, 0.062, 0.075)           # a dark VR room, so pale flakes are honestly visible


# ---- the synthetic window -----------------------------------------------------------------
# Each entry is (u0, v0, u1, v1, rgb, alpha, label) in PANEL UV with v UP, matching uGUI.
# The FIRST one is the full-rect background plate on purpose: it is the element that exposes the
# difference between judging an element by its centre (it cross-fades as one block) and averaging
# over its extent (it dissolves across the whole sweep).
PARCH = (0.847, 0.780, 0.639)
INK = (0.157, 0.125, 0.098)
PLATE = (0.129, 0.106, 0.086)
BRASS = (0.678, 0.518, 0.243)


def build_window():
    e = [(0.00, 0.00, 1.00, 1.00, PLATE, 0.96, "plate"),
         (0.025, 0.045, 0.975, 0.885, PARCH, 1.00, "parchment"),
         (0.025, 0.885, 0.975, 0.985, PLATE, 1.00, "titlebar"),
         (0.055, 0.912, 0.42, 0.962, PARCH, 0.92, "title"),
         (0.915, 0.905, 0.965, 0.968, BRASS, 1.00, "closeX"),
         (0.055, 0.575, 0.30, 0.855, PLATE, 1.00, "portraitbox"),
         (0.068, 0.592, 0.287, 0.838, (0.40, 0.33, 0.26), 1.00, "portrait"),
         (0.055, 0.520, 0.945, 0.528, INK, 0.55, "divider")]
    # text rows on the right of the portrait, then full width below the divider
    for i in range(5):
        v = 0.815 - i * 0.055
        e.append((0.325, v, 0.325 + 0.60 * (0.72 + 0.28 * ((i * 7) % 5) / 5.0),
                  v + 0.030, INK, 0.85, "line_r%d" % i))
    for i in range(5):
        v = 0.470 - i * 0.055
        e.append((0.055, v, 0.055 + 0.885 * (0.55 + 0.45 * ((i * 3) % 5) / 5.0),
                  v + 0.030, INK, 0.82, "line_b%d" % i))
    # two buttons
    e.append((0.10, 0.085, 0.44, 0.175, BRASS, 1.00, "btnA"))
    e.append((0.56, 0.085, 0.90, 0.175, BRASS, 1.00, "btnB"))
    e.append((0.17, 0.115, 0.37, 0.145, PLATE, 0.95, "btnAlabel"))
    e.append((0.63, 0.115, 0.83, 0.145, PLATE, 0.95, "btnBlabel"))
    return e


def rect_px(u0, v0, u1, v1):
    """UV (v up) to pixel box (y down), inclusive-exclusive."""
    x0 = int(round(u0 * PANEL_W))
    x1 = int(round(u1 * PANEL_W))
    y0 = int(round((1.0 - v1) * PANEL_H))
    y1 = int(round((1.0 - v0) * PANEL_H))
    return max(0, x0), max(0, y0), min(PANEL_W, x1), min(PANEL_H, y1)


def render_window(field, elements, thresholds, progress):
    """The window itself, at element granularity - exactly what CanvasRenderer.SetAlpha does."""
    img = np.zeros((PANEL_H, PANEL_W, 3), dtype=np.float32)
    acc = np.zeros((PANEL_H, PANEL_W), dtype=np.float32)
    for (u0, v0, u1, v1, rgb, a0, _label), th in zip(elements, thresholds):
        a = a0 * field.presence_of(th, progress)
        if a <= 0.0005:
            continue
        x0, y0, x1, y1 = rect_px(u0, v0, u1, v1)
        if x1 <= x0 or y1 <= y0:
            continue
        sub = img[y0:y1, x0:x1]
        suba = acc[y0:y1, x0:x1]
        src = np.array(rgb, dtype=np.float32)
        img[y0:y1, x0:x1] = src * a + sub * (1.0 - a)
        acc[y0:y1, x0:x1] = a + suba * (1.0 - a)
    return img, acc


def render_frame(field, elements, thresholds, progress, quad_pad):
    """One composited frame: dark room, then the window, then the premultiplied flake quad."""
    pad_l, pad_r, pad_d, pad_u = quad_pad
    W = int(round(PANEL_W * (1.0 + pad_l + pad_r)))
    H = int(round(PANEL_H * (1.0 + pad_d + pad_u)))
    canvas = np.zeros((H, W, 3), dtype=np.float32)
    canvas[:, :] = np.array(BG, dtype=np.float32)

    ox, oy = int(round(PANEL_W * pad_l)), int(round(PANEL_H * pad_u))
    win_rgb, win_a = render_window(field, elements, thresholds, progress)
    dst = canvas[oy:oy + PANEL_H, ox:ox + PANEL_W]
    canvas[oy:oy + PANEL_H, ox:ox + PANEL_W] = (
        win_rgb * win_a[..., None] + dst * (1.0 - win_a[..., None]))

    # The flake quad spans the padded area; its UV runs past 0..1 exactly as the mesh's does.
    u = np.linspace(-pad_l, 1.0 + pad_r, W, dtype=np.float32)[None, :]
    v = np.linspace(1.0 + pad_u, -pad_d, H, dtype=np.float32)[:, None]
    u = np.broadcast_to(u, (H, W)).astype(np.float32)
    v = np.broadcast_to(v, (H, W)).astype(np.float32)
    rgb, a = field.flakes(u, v, progress)
    canvas = rgb + canvas * (1.0 - a[..., None])          # Blend One OneMinusSrcAlpha
    return np.clip(canvas, 0.0, 1.0)


def to_pil(arr):
    return Image.fromarray((np.power(arr, 1.0 / 2.2) * 255.0 + 0.5).astype(np.uint8))


def font(size):
    for p in ("/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf",
              "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
              "/usr/share/fonts/TTF/DejaVuSans.ttf"):
        if os.path.exists(p):
            return ImageFont.truetype(p, size)
    return ImageFont.load_default()


def strip(frames, labels, path, cols=6, scale=0.52):
    tw = int(frames[0].width * scale)
    th = int(frames[0].height * scale)
    rows = (len(frames) + cols - 1) // cols
    pad, cap = 8, 26
    out = Image.new("RGB", (cols * (tw + pad) + pad, rows * (th + cap + pad) + pad), (16, 17, 20))
    d = ImageDraw.Draw(out)
    f = font(15)
    for i, (fr, lab) in enumerate(zip(frames, labels)):
        r, c = divmod(i, cols)
        x = pad + c * (tw + pad)
        y = pad + r * (th + cap + pad)
        out.paste(fr.resize((tw, th), Image.LANCZOS), (x, y))
        d.text((x + 2, y + th + 4), lab, fill=(226, 226, 232), font=f)
    out.save(path)
    return path


def mp4(frames, path, fps=FPS):
    if not frames:
        return None
    tmp = pathlib.Path(str(path) + ".frames")
    tmp.mkdir(exist_ok=True)
    for i, fr in enumerate(frames):
        fr.save(tmp / ("%04d.png" % i))
    cmd = ["ffmpeg", "-y", "-loglevel", "error", "-framerate", str(fps),
           "-i", str(tmp / "%04d.png"), "-c:v", "libx264", "-pix_fmt", "yuv420p",
           "-vf", "pad=ceil(iw/2)*2:ceil(ih/2)*2", str(path)]
    try:
        subprocess.run(cmd, check=True)
    except Exception as ex:                                    # noqa: BLE001
        print("  (ffmpeg failed: %s - the PNG frames are in %s)" % (ex, tmp))
        return None
    for p in tmp.iterdir():
        p.unlink()
    tmp.rmdir()
    return path


# ---- the stereo audit ----------------------------------------------------------------------
# EVERY input a fragment shader can read that is not identical for both eyes at the same surface
# point. A screen-space dissolve is not "a bit worse" in a headset - it is the failure mode this
# project has already paid for twice, and it is invisible on a monitor because each eye on its own
# looks correct.
FORBIDDEN = [
    ("VPOS", "screen pixel position"),
    ("SV_Position", "screen pixel position in the fragment stage"),
    ("_ScreenParams", "viewport size, differs per eye pass"),
    ("ComputeScreenPos", "screen-space UV"),
    ("ComputeGrabScreenPos", "grab-pass screen UV"),
    ("GrabPass", "reads the eye's own framebuffer"),
    ("_CameraDepthTexture", "per-eye depth, stale in the second pass"),
    ("unity_CameraInvProjection", "stale mono/left-eye matrix in multipass"),
    ("unity_CameraToWorld", "stale mono/left-eye matrix in multipass"),
    ("_WorldSpaceCameraPos", "eye position - a per-eye value"),
    ("unity_StereoEyeIndex", "an explicit per-eye branch"),
    ("_Time", "a shared clock; also the frequency-scrubbing trap"),
    ("_SinTime", "a shared clock"),
    ("_CosTime", "a shared clock"),
    ("unity_DeltaTime", "a shared clock"),
    ("ddx", "screen-space derivative"),
    ("ddy", "screen-space derivative"),
    ("fwidth", "screen-space derivative"),
]


def stereo_audit(path):
    src = SHADER.read_text(encoding="utf-8")
    # Strip // comments: the header DISCUSSES these identifiers at length, and an audit that trips
    # over its own explanation is an audit nobody will trust twice.
    body = "\n".join(re.sub(r"//.*$", "", ln) for ln in src.splitlines())
    hits = [(tok, why) for tok, why in FORBIDDEN if re.search(r"\b%s\b" % re.escape(tok), body)]
    lines = [
        "STEREO AUDIT of %s" % SHADER.relative_to(ROOT),
        "",
        "The question is NOT 'does each eye look right' - a screen-space dissolve passes that test",
        "and still reads as flicker in a headset. The question is whether the two eyes can DISAGREE",
        "about the same surface point. They can only disagree if a per-eye value reaches the field.",
        "",
        "Checked %d per-eye-unstable inputs:" % len(FORBIDDEN),
    ]
    for tok, why in FORBIDDEN:
        lines.append("  %-28s %-52s %s" % (tok, why, "FOUND" if (tok, why) in hits else "absent"))
    lines += ["",
              "RESULT: %s" % ("FAIL - %d per-eye input(s) reach the shader" % len(hits) if hits
                              else "PASS - the fragment stage reads ONLY the interpolated panel UV "
                                   "and per-draw uniforms."),
              "",
              "Corollary for the frequency-scrubbing trap: with no clock in the shader at all, the",
              "one time-like input is _Progress, which C# writes per frame and which is used as a",
              "POSITION (the erosion front) and an AMPLITUDE (plume travel). No dial can multiply a",
              "frequency here because no frequency exists.",
              ""]
    pathlib.Path(path).write_text("\n".join(lines), encoding="utf-8")
    return not hits, "\n".join(lines)


def endpoint_audit():
    """The property every interruption path depends on: presence is EXACTLY 1 at progress 0 and
    EXACTLY 0 at progress 1, for every element, so 'restore' means the number that was there."""
    f = Field(aspect=PANEL_W / PANEL_H)
    worst0 = worst1 = 0.0
    rng = np.random.default_rng(7)
    for _ in range(4000):
        u0, v0 = rng.random() * 0.9, rng.random() * 0.9
        th = f.element_thresholds(u0, v0, u0 + rng.random() * 0.1, v0 + rng.random() * 0.1)
        worst0 = max(worst0, abs(1.0 - f.presence_of(th, 0.0)))
        worst1 = max(worst1, abs(f.presence_of(th, 1.0)))
    return worst0, worst1


def main():
    out = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else ROOT / "render" / "windowmaterialise")
    out.mkdir(parents=True, exist_ok=True)

    aspect = PANEL_W / PANEL_H
    f = Field(aspect=aspect)
    elements = build_window()
    thresholds = [f.element_thresholds(e[0], e[1], e[2], e[3]) for e in elements]

    # the quad's padding, computed exactly as WindowMaterialise.Quad.cs does
    du = f.wind[0] / aspect * float(f.drift)
    dv = float(f.wind[1]) * float(f.drift)
    margin = 0.04
    pad = (max(0.0, -du) + margin, max(0.0, du) + margin,
           max(0.0, -dv) + margin, max(0.0, dv) + margin)

    appear = shipped_seconds("Appear")
    vanish = shipped_seconds("Vanish")
    print("shipped durations: appear %.2f s, vanish %.2f s (hard code ceiling 2.0 s)" % (appear, vanish))
    print("field constants read from C#: %s" % CONSTANTS)

    results = {}
    for name, seconds, materialising in (("appear", appear, True), ("vanish", vanish, False)):
        fd = Field(aspect=aspect, tail_fade=0.0 if materialising else 1.0)
        n = max(2, int(round(seconds * FPS)))
        frames, labels = [], []
        for i in range(n + 1):
            k = i / n
            progress = (1.0 - k) if materialising else k   # the runner's LINEAR ramp
            frames.append(to_pil(render_frame(fd, elements, thresholds, progress, pad)))
            labels.append("%.3f s   p=%.2f" % (k * seconds, progress))
        results[name] = (frames, labels, seconds)

        # the strip: 12 evenly sampled frames across the FULL duration, endpoints included
        idx = [int(round(j * n / 11.0)) for j in range(12)]
        strip([frames[i] for i in idx], [labels[i] for i in idx],
              out / ("%s_strip.png" % name), cols=6)
        mp4(frames, out / ("%s.mp4" % name), fps=FPS)
        print("  %-7s %d frames at %d fps -> %s_strip.png, %s.mp4"
              % (name, len(frames), FPS, name, name))

    # ---- the frames Blender re-photographs at real world scale -------------------------------
    # Six moments per direction plus one stereo pair from the middle of the vanish, which is where
    # the debris is furthest from the panel and therefore where a screen-locked pattern would be
    # most obvious.
    room = out / "room"
    room.mkdir(exist_ok=True)
    manifest = []
    for name in ("appear", "vanish"):
        frames, labels, seconds = results[name]
        n = len(frames) - 1
        for j, frac_ in enumerate((0.0, 0.2, 0.4, 0.6, 0.8, 1.0)):
            i = int(round(frac_ * n))
            fn = "%s_%02d.png" % (name, j)
            frames[i].save(room / fn)
            manifest.append({"file": fn, "label": labels[i]})
    mid = results["vanish"][0][int(round(0.6 * (len(results["vanish"][0]) - 1)))]
    mid.save(room / "vanish_mid_stereo.png")
    manifest.append({"file": "vanish_mid_stereo.png", "stereo": True,
                     "label": results["vanish"][1][int(round(0.6 * (len(results["vanish"][0]) - 1)))]})
    img_w = int(round(PANEL_W * (1.0 + pad[0] + pad[1])))
    img_h = int(round(PANEL_H * (1.0 + pad[2] + pad[3])))
    (room / "room.json").write_text(json.dumps({
        "pad": [float(x) for x in pad], "panel_px": [PANEL_W, PANEL_H], "image_px": [img_w, img_h],
        "frames": manifest}, indent=1), encoding="utf-8")
    print("  room frames + room.json in %s (feed to windowmaterialise_room.py under Blender)" % room)

    ok, text = stereo_audit(out / "stereo_audit.txt")
    print(text.rsplit("RESULT:", 1)[-1].splitlines()[0].strip())

    w0, w1 = endpoint_audit()
    print("endpoint audit over 4000 random elements: |1 - presence(p=0)| max %.3e, "
          "|presence(p=1)| max %.3e  ->  %s" % (w0, w1, "EXACT" if max(w0, w1) == 0.0 else "NOT EXACT"))

    (out / "README.txt").write_text(
        "Rendered by unity/asset-preview/windowmaterialise_preview.py.\n"
        "The panel is a SYNTHETIC stand-in (no game install on this machine); its element rects\n"
        "drive the dissolve exactly as the real CanvasRenderers do, so the MECHANISM is faithful\n"
        "and the CONTENT is a guess.\n", encoding="utf-8")
    print("out: %s" % out)
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
