"""WINDOW MATERIALISE, STAGE 1 -- the audits, the window textures and the shard simulation.

This script does NOT render the pictures a human looks at.  It cannot: the whole point of
ModBuild 294 is that the debris is real geometry in the room, and a numpy compositor can
only ever draw a decal on a pane -- which is exactly the defect being fixed.  What it does
is produce the two things the Blender stage needs and the three things that settle the
questions no picture can:

  sim/meta.json                the frame table: elapsed time, k, element progress, debris
                               front, size scale, and the window texture for each frame
  sim/<dir>_verts.npy          float32 (frames, N*12, 3) -- every shard's world position,
                               in metres, in Blender axes, for every frame
  sim/faces.npy                int32 (N*4, 3) -- constant across frames
  sim/<dir>_shade.npy          float32 (N*12,) -- the per-shard shade jitter
  win/<dir>_%04d.png           the window's ELEMENT dissolve at that frame, RGBA

  camera_clock_audit.txt       a MECHANICAL check of the shipped shader for every camera,
                               head-pose, screen-space and clock input. This is the
                               deliverable that settles "the effect is not bound to head
                               movement" and the stereo-rivalry question. Two rendered eye
                               images cannot settle either: a head-bound effect looks
                               perfectly correct in each eye separately, and that is the
                               whole trap.
  endpoint_audit               presence is EXACTLY 1 at progress 0 and EXACTLY 0 at
                               progress 1, and shard size is EXACTLY 0 at both ends of both
                               directions, over thousands of random samples
  mesh_gate                    the shard's signed volume and face outwardness

THE HALF THIS CANNOT SHOW is the real game's windows.  There is no game install on this
machine, so the panel below is a SYNTHETIC stand-in built to the shape of a Gloomhaven
dialog.  Its element rectangles drive the dissolve and seed the shards exactly as the real
CanvasRenderers do, so what the renders show about the MECHANISM is faithful; what they show
about the CONTENT is a guess.  Say so when you send them on.

RUN:
  python3 unity/asset-preview/windowmaterialise_preview.py [outdir]
"""

from __future__ import annotations

import json
import os
import pathlib
import re
import sys

import numpy as np
from PIL import Image

sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent))
import windowmaterialise_field as WF  # noqa: E402

ROOT = pathlib.Path(__file__).resolve().parents[2]
SHADER = ROOT / "unity" / "GloomhavenVR.Assets" / "Assets" / "Bundle" / "Table" / "WindowMaterialise.shader"

# The panel, at the size a floated modal actually is: ModalFallback.ModalTargetWidthMeters
# is 0.80 m, and the shipped windows sit close to a 1.54 aspect.
PANEL_W_M = 0.80
PANEL_H_M = 0.52
PANEL_PX_W = 800
PANEL_PX_H = 520
ASPECT = PANEL_W_M / PANEL_H_M

FPS = 60


# ---------------------------------------------------------------------------------------
# the synthetic window
# ---------------------------------------------------------------------------------------
# (u0, v0, u1, v1, rgb, alpha).  Drawn in order, so later entries sit on top -- the same
# order uGUI would draw them in, which matters because the dissolve is per element.

PARCH = (0.847, 0.796, 0.678)
INK = (0.184, 0.145, 0.114)
BRASS = (0.706, 0.545, 0.271)
PLATE = (0.129, 0.106, 0.086)


def build_elements():
    e = [
        (0.000, 0.000, 1.000, 1.000, PLATE, 0.94),          # the background plate
        (0.022, 0.036, 0.978, 0.964, PARCH, 1.00),          # the parchment
        (0.022, 0.856, 0.978, 0.964, BRASS, 0.85),          # title bar
        (0.055, 0.882, 0.560, 0.940, INK, 0.95),            # title text
        (0.925, 0.878, 0.966, 0.944, INK, 0.90),            # the close X
        (0.048, 0.470, 0.290, 0.812, INK, 0.55),            # portrait box
        (0.048, 0.440, 0.952, 0.450, INK, 0.45),            # divider
    ]
    # ten text rows down the right column
    for i in range(10):
        v1 = 0.800 - i * 0.0335
        e.append((0.320, v1 - 0.021, 0.320 + 0.62 * (0.55 + 0.45 * ((i * 7) % 5) / 4.0),
                  v1, INK, 0.88))
    # two buttons with labels
    e.append((0.100, 0.075, 0.440, 0.185, BRASS, 0.92))
    e.append((0.150, 0.105, 0.390, 0.155, INK, 0.95))
    e.append((0.560, 0.075, 0.900, 0.185, BRASS, 0.92))
    e.append((0.610, 0.105, 0.850, 0.155, INK, 0.95))
    return e


ELEMENTS = build_elements()


def element_presences(element_progress):
    """One presence per element, exactly as WindowMaterialiseRunner.Apply computes it."""
    out = np.empty(len(ELEMENTS), dtype=np.float32)
    for i, (u0, v0, u1, v1, _rgb, _a) in enumerate(ELEMENTS):
        th = WF.element_thresholds(u0, v0, u1, v1, ASPECT)
        out[i] = WF.presence_of(th, element_progress)
    return out


def render_window(element_progress):
    """The window's own uGUI half, as an RGBA image.  Straight 'over' compositing, because
    that is what a canvas does; the shards are not in here at all -- they are geometry."""
    h, w = PANEL_PX_H, PANEL_PX_W
    rgb = np.zeros((h, w, 3), dtype=np.float32)
    acc = np.zeros((h, w, 1), dtype=np.float32)
    pres = element_presences(element_progress)

    ys = np.arange(h, dtype=np.float32)
    xs = np.arange(w, dtype=np.float32)
    for i, (u0, v0, u1, v1, col, base) in enumerate(ELEMENTS):
        a = float(base) * float(pres[i])
        if a <= 0.0005:
            continue
        x0, x1 = int(round(u0 * w)), int(round(u1 * w))
        # v runs UP the panel; image rows run down.
        y0, y1 = int(round((1.0 - v1) * h)), int(round((1.0 - v0) * h))
        x0, x1 = max(0, x0), min(w, x1)
        y0, y1 = max(0, y0), min(h, y1)
        if x1 <= x0 or y1 <= y0:
            continue
        src = np.array(col, dtype=np.float32)
        sub_rgb = rgb[y0:y1, x0:x1]
        sub_a = acc[y0:y1, x0:x1]
        sub_rgb *= (1.0 - a)
        sub_rgb += src * a
        sub_a *= (1.0 - a)
        sub_a += a
    del ys, xs

    out = np.concatenate([np.clip(rgb, 0, 1), np.clip(acc, 0, 1)], axis=2)
    return Image.fromarray((out * 255.0 + 0.5).astype(np.uint8), mode="RGBA")


# ---------------------------------------------------------------------------------------
# the audits
# ---------------------------------------------------------------------------------------

# THE LIST GREW, AND ONE ENTRY WAS RETIRED AS INERT.
#
# The previous round's audit carried `SV_Position` and reported PASS. It could never have
# fired: the grep is case-sensitive and every vertex shader in existence declares
# `SV_POSITION`, so that entry tested nothing and padded the count from 17 to 18. It is
# replaced here by `VPOS`, which is the semantic that actually is a hazard (the pixel
# position in the fragment stage), and by an explicit note that the mandatory clip-space
# transform is not what is being looked for.
#
# The CAMERA/HEAD group is new, and it is now the headline of the audit rather than a
# footnote, because the user's 2026-08-26 ruling made it a requirement in its own right:
# "Der Effekt soll nicht an den Kopfbewegungen gebunden sein."

FORBIDDEN = [
    # ---- head pose and camera. The user's ruling; also the stereo trap ----
    ("_WorldSpaceCameraPos", "HEAD POSE: the eye position, and a different value per eye"),
    ("unity_CameraToWorld", "HEAD POSE: a camera matrix, stale in the second eye pass"),
    ("unity_WorldToCamera", "HEAD POSE: a camera matrix"),
    ("unity_CameraInvProjection", "HEAD POSE: a camera matrix"),
    ("UNITY_MATRIX_V", "HEAD POSE: the view matrix"),
    ("UNITY_MATRIX_I_V", "HEAD POSE: the inverse view matrix"),
    ("UNITY_MATRIX_VP", "HEAD POSE: view-projection read explicitly"),
    ("WorldSpaceViewDir", "HEAD POSE: a view direction"),
    ("UnityWorldSpaceViewDir", "HEAD POSE: a view direction"),
    ("ObjSpaceViewDir", "HEAD POSE: a view direction"),
    ("_ProjectionParams", "HEAD POSE: projection state, differs per eye"),
    ("unity_StereoEyeIndex", "an explicit per-eye branch"),
    ("unity_StereoMatrix", "an explicit per-eye matrix"),
    # ---- screen space ----
    ("VPOS", "screen pixel position in the fragment stage"),
    ("_ScreenParams", "viewport size, differs per eye pass"),
    ("ComputeScreenPos", "screen-space UV"),
    ("ComputeGrabScreenPos", "grab-pass screen UV"),
    ("GrabPass", "reads the eye's own framebuffer"),
    ("_CameraDepthTexture", "per-eye depth, stale in the second pass"),
    ("screenPos", "a screen-space coordinate by convention"),
    ("ddx", "screen-space derivative"),
    ("ddy", "screen-space derivative"),
    ("fwidth", "screen-space derivative"),
    # ---- the clock, i.e. the frequency-scrubbing trap ----
    ("_Time", "a shared clock; also the frequency-scrubbing trap"),
    ("_SinTime", "a shared clock"),
    ("_CosTime", "a shared clock"),
    ("unity_DeltaTime", "a shared clock"),
    ("Time.", "a clock reached through the C# side of a shader include"),
]


def camera_clock_audit(path):
    src = SHADER.read_text(encoding="utf-8")
    # Strip // comments: the header DISCUSSES these identifiers at length, and an audit that
    # trips over its own explanation is one nobody will trust twice.
    body = "\n".join(re.sub(r"//.*$", "", ln) for ln in src.splitlines())
    hits = [(tok, why) for tok, why in FORBIDDEN
            if re.search(r"(?<![A-Za-z0-9_])%s(?![A-Za-z0-9_])" % re.escape(tok), body)]
    ok = not hits
    lines = [
        "CAMERA / HEAD-POSE / CLOCK AUDIT of %s" % SHADER.relative_to(ROOT),
        "",
        "WHAT THIS IS FOR. Two things a rendered picture cannot settle:",
        "  (1) the user's ruling of 2026-08-26 -- \"Der Effekt soll nicht an den",
        "      Kopfbewegungen gebunden sein\". A head-bound effect looks perfectly correct",
        "      in a still and perfectly correct in each eye; what gives it away is that it",
        "      moves when he turns his head, which no render on this machine can show.",
        "  (2) stereo rivalry. A screen-space dissolve also looks fine in each eye",
        "      SEPARATELY. What makes it flicker is that the two eyes disagree about the",
        "      same surface point, and the only way to know they cannot is that no per-eye",
        "      input reaches the effect at all.",
        "",
        "WHAT IS DELIBERATELY NOT LOOKED FOR: UnityObjectToClipPos and",
        "UnityObjectToWorldNormal. The first is the mandatory clip-space transform every",
        "vertex shader must perform and the second reads unity_WorldToObject, an OBJECT",
        "matrix. Neither carries eye or head state into the effect's own arithmetic.",
        "",
        "%d identifiers checked, comments stripped." % len(FORBIDDEN),
        "",
    ]
    for tok, why in FORBIDDEN:
        mark = "HIT " if any(t == tok for t, _ in hits) else "  . "
        lines.append("%s%-28s %s" % (mark, tok, why))
    lines += ["", "RESULT: %s" % ("PASS -- all absent" if ok else "FAIL")]
    if hits:
        lines.append("  the effect reads per-eye or head-bound state and MUST NOT SHIP as is.")
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return ok, lines


def endpoint_audit(cloud):
    """Both ends of both directions.

    THREE of the four corners must be EXACT and the fourth must be the opposite of exact,
    and getting that backwards is a real trap -- the first run of this audit called the
    effect broken because an APPEAR at k=0 still has a full cloud in the air. It must. An
    appear that begins with nothing on screen is a window that is live and clickable while
    showing the player an empty space, which is precisely the defect the previous round's
    first preview strip caught and the reason _TailFade is a per-direction uniform. So:

      VANISH k=0  window whole (exactly 1), no debris (exactly 0)
      VANISH k=1  window gone  (exactly 0), no debris (exactly 0)
      APPEAR k=0  window gone  (exactly 0), debris PRESENT -- asserted as a lower bound
      APPEAR k=1  window whole (exactly 1), no debris (exactly 0)
    """
    rng = np.random.default_rng(7)
    n = 4000
    u0 = rng.random(n).astype(np.float32) * 0.9
    v0 = rng.random(n).astype(np.float32) * 0.9
    u1 = u0 + rng.random(n).astype(np.float32) * 0.1
    v1 = v0 + rng.random(n).astype(np.float32) * 0.1

    th = np.stack([WF.threshold(np.stack([u0, u1, u0, u1, (u0 + u1) / 2], axis=1)[:, k],
                                np.stack([v0, v0, v1, v1, (v0 + v1) / 2], axis=1)[:, k],
                                ASPECT) for k in range(5)], axis=1)

    out = []
    for label, materialising in (("VANISH", False), ("APPEAR", True)):
        for k in (0.0, 1.0):
            ep, df = WF.progresses(k, materialising)
            pres = WF.presence_of(th, ep)
            want = 1.0 if ((not materialising and k == 0.0) or (materialising and k == 1.0)) else 0.0
            err = float(np.max(np.abs(pres - want)))
            ss = WF.debris_size_scale(k, materialising)
            a = np.clip((df - cloud.thr) / float(WF.D_LIFE), 0.0, 1.0)
            env = (np.asarray(WF.smoothstep01(0.0, 0.06, a), dtype=np.float64)
                   * (1.0 - np.asarray(WF.smoothstep01(0.72, 1.0, a), dtype=np.float64)))
            shard_max = float(np.max(cloud.size * env * ss))
            want_debris = "PRESENT" if (materialising and k == 0.0) else "none"
            out.append((label, k, want, err, shard_max, want_debris))
    return out


# ---------------------------------------------------------------------------------------
# main
# ---------------------------------------------------------------------------------------

def main():
    outdir = pathlib.Path(sys.argv[1] if len(sys.argv) > 1 else ROOT / "render" / "windowmaterialise")
    sim = outdir / "sim"
    win = outdir / "win"
    for d in (outdir, sim, win):
        d.mkdir(parents=True, exist_ok=True)

    appear_s, vanish_s = WF.read_durations()
    # THE TWO DURATIONS LIVE IN Defaults/Defaults.WorldUI.cs, WHICH THIS LANE MAY NOT EDIT.
    # They are delivered to the integrator as a hunk. So that the renders show the durations
    # actually being proposed rather than the ones still on disk, both can be overridden --
    # and when they are, it is printed loudly and stamped into meta.json, because a strip
    # labelled with a number nothing ships is worse than no strip.
    ov_a, ov_v = os.environ.get("WM_APPEAR_S"), os.environ.get("WM_VANISH_S")
    overridden = False
    if ov_a or ov_v:
        overridden = True
        print("!! DURATION OVERRIDE: Defaults.WorldUI.cs still reads appear %.2f s / vanish "
              "%.2f s. Rendering at the PROPOSED values instead." % (appear_s, vanish_s))
        appear_s = float(ov_a) if ov_a else appear_s
        vanish_s = float(ov_v) if ov_v else vanish_s
    print("durations used: appear %.2f s, vanish %.2f s%s"
          % (appear_s, vanish_s, "  (OVERRIDDEN)" if overridden else "  (from Defaults)"))
    print("element span %.3f  =>  the window finishes at %.3f s (appear) / %.3f s (vanish)"
          % (WF.ELEMENT_SPAN, appear_s * float(WF.ELEMENT_SPAN), vanish_s * float(WF.ELEMENT_SPAN)))

    cloud = WF.build_cloud(ELEMENTS, PANEL_W_M, PANEL_H_M, ASPECT)
    print("cloud: %d shards, %d in front of the plane, %d behind it"
          % (cloud.n, int(np.sum(~cloud.behind)), int(np.sum(cloud.behind))))

    vol, outward = WF.shard_winding_gate()
    print("mesh gate: signed volume %.4f (must be > 0), worst face outwardness %.4f (must be > 0)"
          % (vol, outward))
    if vol <= 0 or outward <= 0:
        raise SystemExit("MESH GATE FAILED -- the shard is inside out")

    ang_lo = 2 * np.degrees(np.arctan2(float(WF.D_MIN_M) / 2, 0.9))
    ang_hi = 2 * np.degrees(np.arctan2(float(WF.D_MAX_M) / 2, 0.9))
    print("shard size %.1f-%.1f mm  =>  %.2f-%.2f deg at 0.9 m (above the sub-pixel regime)"
          % (float(WF.D_MIN_M) * 1000, float(WF.D_MAX_M) * 1000, ang_lo, ang_hi))

    meta = {
        "panel": {"w_m": PANEL_W_M, "h_m": PANEL_H_M,
                  "px_w": PANEL_PX_W, "px_h": PANEL_PX_H},
        "fps": FPS,
        "shards": int(cloud.n),
        "durations": {"appear_s": appear_s, "vanish_s": vanish_s,
                      "overridden": overridden},
        "element_span": float(WF.ELEMENT_SPAN),
        "directions": {},
    }

    faces_written = False
    for label, seconds, materialising in (("appear", appear_s, True),
                                          ("vanish", vanish_s, False)):
        frames = max(2, int(round(seconds * FPS)) + 1)
        verts = np.zeros((frames, cloud.n * 12, 3), dtype=np.float32)
        table = []
        for f in range(frames):
            k = f / float(frames - 1)
            ep, df = WF.progresses(k, materialising)
            ss = WF.debris_size_scale(k, materialising)
            v, faces = WF.shard_mesh(cloud, df, ss, ASPECT)
            verts[f] = v.astype(np.float32)
            if not faces_written:
                np.save(sim / "faces.npy", faces.astype(np.int32))
                faces_written = True
            name = "%s_%04d.png" % (label, f)
            render_window(ep).save(win / name)
            table.append({
                "f": f, "t": round(k * seconds, 5), "k": round(k, 5),
                "element_progress": round(float(ep), 5),
                "debris_front": round(float(df), 5),
                "size_scale": round(float(ss), 5),
                "window": "win/" + name,
            })
        np.save(sim / ("%s_verts.npy" % label), verts)
        np.save(sim / ("%s_shade.npy" % label), WF.shard_vertex_shade(cloud))
        meta["directions"][label] = {"seconds": seconds, "frames": table}
        print("%-7s %3d frames, verts %s" % (label, frames, verts.shape))

    (sim / "meta.json").write_text(json.dumps(meta, indent=1), encoding="utf-8")

    ok, _ = camera_clock_audit(outdir / "camera_clock_audit.txt")
    print("camera/head-pose/clock audit: %s (%d identifiers) -> %s"
          % ("PASS" if ok else "FAIL", len(FORBIDDEN), outdir / "camera_clock_audit.txt"))

    print("\nENDPOINT AUDIT:")
    endpoints_ok = True
    for label, k, want, err, shard_max, want_debris in endpoint_audit(cloud):
        if want_debris == "none":
            good = (err == 0.0 and shard_max == 0.0)
        else:
            good = (err == 0.0 and shard_max > 0.005)
        endpoints_ok = endpoints_ok and good
        print("  %-6s k=%.1f  element presence should be %.0f (max error %.3e), debris "
              "should be %-7s (largest shard %.3e m)  -> %s"
              % (label, k, want, err, want_debris, shard_max, "ok" if good else "WRONG"))
    print("  -> %s" % ("EXACT at every end, and the appear starts with a cloud as it must"
                       if endpoints_ok else "AN ENDPOINT IS WRONG"))

    (outdir / "README.txt").write_text(
        "WINDOW MATERIALISE renders -- read this before treating any of them as proof.\n"
        "\n"
        "THE PANEL IS A SYNTHETIC STAND-IN. There is no game install on this machine. The\n"
        "element rectangles below drive the dissolve and seed the shards exactly as the real\n"
        "CanvasRenderers do, so what these show about the MECHANISM is faithful; what they\n"
        "show about the CONTENT is a guess.\n"
        "\n"
        "THE SHARDS ARE THE SHIPPED ARITHMETIC. Every position in sim/*_verts.npy comes from\n"
        "windowmaterialise_field.py, which reads its constants out of the C# and mirrors the\n"
        "shader's vertex stage line for line. It is not an artist's impression of the effect.\n"
        "\n"
        "THE LIGHTING IS THE SHIPPED SHADER'S. The room renders shade the shards with the same\n"
        "fixed world key direction and the same ambient/key/fill terms the shader uses, NOT with\n"
        "the room's own lights -- so a facet's brightness in these pictures is the brightness the\n"
        "headset will draw.\n"
        "\n"
        "WHAT NO RENDER HERE CAN SHOW: that the effect does not move with the head. That is\n"
        "settled by camera_clock_audit.txt, mechanically, and by nothing else.\n",
        encoding="utf-8")

    print("\nwrote %s" % outdir)
    return 0 if (ok and endpoints_ok) else 1


if __name__ == "__main__":
    raise SystemExit(main())
