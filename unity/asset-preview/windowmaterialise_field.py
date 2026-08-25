"""THE WINDOW-MATERIALISE FIELD AND ITS DEBRIS, IN NUMPY -- the third copy.

The effect exists three times on purpose, and this is the copy the reviewed renders are
driven from:

  1. src/GloomhavenVR/WorldUI/WindowMaterialiseField.cs   the erosion field + the two fronts
     src/GloomhavenVR/WorldUI/WindowMaterialiseDebris.cs  the shard seeding and the mesh gate
  2. unity/GloomhavenVR.Assets/Assets/Bundle/Table/WindowMaterialise.shader   the trajectory
  3. this file

CHANGE ONE, CHANGE ALL THREE.  Nothing here carries its own constants: every number is
regex-read out of the C# at import time and the module refuses to load if one is missing,
so a tuning pass cannot silently make the strips stale.

WHY float32 EVERYWHERE.  The value-noise hash folds through frac(); in float64 the same
expression gives a visibly different field.  F32 is not tidiness, it is the shader.

NO CAMERA, NO HEAD POSE, NO CLOCK.  Every function below takes a panel UV or a per-shard
constant plus one progress scalar.  That is the property the whole design rests on, and it
is checked mechanically against the shipped shader by windowmaterialise_preview.py.
"""

from __future__ import annotations

import os
import re

import numpy as np

F32 = np.float32

_HERE = os.path.dirname(os.path.abspath(__file__))
_ROOT = os.path.abspath(os.path.join(_HERE, "..", ".."))
_FIELD_CS = os.path.join(_ROOT, "src", "GloomhavenVR", "WorldUI", "WindowMaterialiseField.cs")
_DEFAULTS_CS = os.path.join(_ROOT, "src", "GloomhavenVR", "Defaults", "Defaults.WorldUI.cs")


# ---------------------------------------------------------------------------------------
# the constants, read out of the C# rather than duplicated
# ---------------------------------------------------------------------------------------

_FLOATS = (
    "Softness", "Ragged", "FrontScale",
    "ElementSpan", "DebrisOverrun", "TailStart",
    "DebrisPerSquareMetre", "DebrisMinMetres", "DebrisMaxMetres", "DebrisSizePower",
    "DebrisDriftMetres", "DebrisLiftMetres", "DebrisBehindFraction", "DebrisFallMetres",
    "DebrisWanderMetres", "DebrisSpinTurns", "DebrisLifeSpan",
)
_INTS = ("Samples", "DebrisMaxCount", "DebrisMinCount")


def _read_cs_constants() -> dict:
    try:
        src = open(_FIELD_CS, encoding="utf-8").read()
    except OSError as exc:  # pragma: no cover - environmental
        raise SystemExit(f"cannot read {_FIELD_CS}: {exc}")

    out: dict = {}
    for name in _FLOATS:
        m = re.search(r"internal const float %s\s*=\s*([-0-9.eE]+)f?\s*;" % name, src)
        if not m:
            raise SystemExit(
                f"windowmaterialise_field.py: could not find 'internal const float {name}' in "
                f"{_FIELD_CS}. The mirror refuses to render a stale effect -- if the constant was "
                f"renamed, rename it here too."
            )
        out[name] = F32(m.group(1))
    for name in _INTS:
        m = re.search(r"internal const int %s\s*=\s*([0-9]+)\s*;" % name, src)
        if not m:
            raise SystemExit(
                f"windowmaterialise_field.py: could not find 'internal const int {name}' in "
                f"{_FIELD_CS}."
            )
        out[name] = int(m.group(1))

    m = re.search(r"Wind\s*=\s*new Vector2\(\s*([-0-9.]+)f?\s*,\s*([-0-9.]+)f?\s*\)\.normalized", src)
    if not m:
        raise SystemExit("windowmaterialise_field.py: could not find the Wind vector in " + _FIELD_CS)
    w = np.array([float(m.group(1)), float(m.group(2))], dtype=F32)
    out["Wind"] = w / F32(np.sqrt(float(w[0]) ** 2 + float(w[1]) ** 2))
    return out


C = _read_cs_constants()

SOFTNESS = C["Softness"]
RAGGED = C["Ragged"]
FRONT_SCALE = C["FrontScale"]
WIND = C["Wind"]
SAMPLES = C["Samples"]

ELEMENT_SPAN = C["ElementSpan"]
DEBRIS_OVERRUN = C["DebrisOverrun"]
TAIL_START = C["TailStart"]

D_PER_M2 = C["DebrisPerSquareMetre"]
D_MAX_COUNT = C["DebrisMaxCount"]
D_MIN_COUNT = C["DebrisMinCount"]
D_MIN_M = C["DebrisMinMetres"]
D_MAX_M = C["DebrisMaxMetres"]
D_SIZE_POW = C["DebrisSizePower"]
D_DRIFT_M = C["DebrisDriftMetres"]
D_LIFT_M = C["DebrisLiftMetres"]
D_BEHIND_FRAC = C["DebrisBehindFraction"]
D_FALL_M = C["DebrisFallMetres"]
D_WANDER_M = C["DebrisWanderMetres"]
D_SPIN_TURNS = C["DebrisSpinTurns"]
D_LIFE = C["DebrisLifeSpan"]


def read_durations() -> tuple[float, float]:
    """The two shipped durations, out of Defaults.WorldUI.cs. Read rather than repeated, so a
    strip is always labelled with the numbers that actually ship."""
    try:
        src = open(_DEFAULTS_CS, encoding="utf-8").read()
    except OSError as exc:  # pragma: no cover - environmental
        raise SystemExit(f"cannot read {_DEFAULTS_CS}: {exc}")
    out = []
    for key in ("WindowMaterialiseAppearSeconds", "WindowMaterialiseVanishSeconds"):
        m = re.search(r"internal const float %s\s*=\s*([-0-9.]+)f?\s*;" % key, src)
        if not m:
            raise SystemExit(f"could not find {key} in {_DEFAULTS_CS}")
        out.append(float(m.group(1)))
    return out[0], out[1]


# ---------------------------------------------------------------------------------------
# the erosion field. Byte-for-byte the shader's and the C#'s
# ---------------------------------------------------------------------------------------

def _frac(x):
    return np.asarray(x, dtype=F32) - np.floor(np.asarray(x, dtype=F32))


def hash21(x, y):
    px = _frac(np.asarray(x, dtype=F32) * F32(123.34))
    py = _frac(np.asarray(y, dtype=F32) * F32(456.21))
    d = px * (px + F32(45.32)) + py * (py + F32(45.32))
    return _frac((px + d) * (py + d))


def vnoise(x, y):
    x = np.asarray(x, dtype=F32)
    y = np.asarray(y, dtype=F32)
    ix, iy = np.floor(x), np.floor(y)
    fx, fy = x - ix, y - iy
    ux = fx * fx * (F32(3.0) - F32(2.0) * fx)
    uy = fy * fy * (F32(3.0) - F32(2.0) * fy)
    a = hash21(ix, iy)
    b = hash21(ix + F32(1.0), iy)
    c = hash21(ix, iy + F32(1.0))
    d = hash21(ix + F32(1.0), iy + F32(1.0))
    return (a + (b - a) * ux) + ((c + (d - c) * ux) - (a + (b - a) * ux)) * uy


def sweep(u, v):
    n = F32(max(abs(float(WIND[0])) + abs(float(WIND[1])), 1e-4))
    return ((np.asarray(u, dtype=F32) - F32(0.5)) * WIND[0]
            + (np.asarray(v, dtype=F32) - F32(0.5)) * WIND[1] + F32(0.5) * n) / n


def threshold(u, v, aspect):
    n = vnoise(np.asarray(u, dtype=F32) * F32(aspect) * FRONT_SCALE,
               np.asarray(v, dtype=F32) * FRONT_SCALE)
    s = sweep(u, v)
    return s + (n - s) * RAGGED


def front(progress):
    return F32(progress) * (F32(1.0) + F32(2.0) * SOFTNESS) - SOFTNESS


def smoothstep01(e0, e1, x):
    t = np.clip((np.asarray(x, dtype=F32) - F32(e0)) / F32(max(float(e1) - float(e0), 1e-6)),
                F32(0.0), F32(1.0))
    return t * t * (F32(3.0) - F32(2.0) * t)


def progresses(k, materialising):
    """WindowMaterialiseField.Progresses. Returns (element_progress, debris_front)."""
    e = float(np.clip(k, 0.0, 1.0))
    pe = float(np.clip(e / float(ELEMENT_SPAN), 0.0, 1.0))
    if materialising:
        return 1.0 - pe, float(front(1.0 - e))
    return pe, float(front(e * (1.0 + float(DEBRIS_OVERRUN))))


def debris_size_scale(k, materialising, intensity=1.0):
    """WindowMaterialiseField.DebrisSizeScale."""
    if materialising:
        return float(intensity)
    return float(intensity) * (1.0 - float(smoothstep01(TAIL_START, 1.0, float(np.clip(k, 0, 1)))))


def age(thr, debris_front):
    return np.clip((F32(debris_front) - np.asarray(thr, dtype=F32)) / D_LIFE, F32(0.0), F32(1.0))


def size_envelope(a):
    return smoothstep01(0.0, 0.06, a) * (F32(1.0) - smoothstep01(0.72, 1.0, a))


def presence_of(thresholds, element_progress):
    """WindowMaterialiseField.PresenceOf -- the mean presence over an element's SAMPLES points.

    `thresholds` is (..., SAMPLES)."""
    f = front(element_progress)
    lo = f - SOFTNESS
    inv = F32(1.0) / F32(max(2.0 * float(SOFTNESS), 1e-6))
    t = np.clip((np.asarray(thresholds, dtype=F32) - lo) * inv, F32(0.0), F32(1.0))
    return (t * t * (F32(3.0) - F32(2.0) * t)).mean(axis=-1)


def element_thresholds(u0, v0, u1, v1, aspect):
    """The five points WindowMaterialiseRunner samples: four corners and the centre."""
    us = np.array([u0, u1, u0, u1, 0.5 * (u0 + u1)], dtype=F32)
    vs = np.array([v0, v0, v1, v1, 0.5 * (v0 + v1)], dtype=F32)
    return threshold(us, vs, aspect)


# ---------------------------------------------------------------------------------------
# the RNG. Bit-for-bit WindowMaterialiseDebris' xorshift, and the draws are consumed in the
# identical order, so a rerun of a preview is the same cloud rather than a fresh sample.
# ---------------------------------------------------------------------------------------

class Xorshift:
    __slots__ = ("s",)

    def __init__(self, seed: int):
        s = seed & 0xFFFFFFFF
        self.s = s if s else 0x9E3779B9

    def r01(self) -> float:
        s = self.s
        s ^= (s << 13) & 0xFFFFFFFF
        s ^= s >> 17
        s ^= (s << 5) & 0xFFFFFFFF
        self.s = s
        return (s & 0xFFFFFF) / 16777216.0

    def signed(self) -> float:
        return self.r01() * 2.0 - 1.0


# ---------------------------------------------------------------------------------------
# the shard solid. Mirrors WindowMaterialiseDebris.TetraCorner / TetraFace, and re-derives
# the winding gate rather than trusting the table -- nine meshes have shipped in this
# project wound against the side they are seen from.
# ---------------------------------------------------------------------------------------

TETRA_CORNER = np.array(
    [[1.0, 1.0, 1.0], [1.0, -1.0, -1.0], [-1.0, 1.0, -1.0], [-1.0, -1.0, 1.0]],
    dtype=np.float64) * 0.5773503
TETRA_FACE = np.array([[0, 1, 2], [0, 2, 3], [0, 3, 1], [1, 3, 2]], dtype=np.int32)


def shard_winding_gate():
    """(signed_volume, worst_outwardness). Both must be > 0.

    UV signed area is deliberately absent: a shard carries no texture coordinates and the
    shader samples no texture, so a UV area here would be a number with no referent."""
    centroid = TETRA_CORNER.mean(axis=0)
    vol = 0.0
    worst = float("inf")
    for f in TETRA_FACE:
        a, b, c = TETRA_CORNER[f[0]], TETRA_CORNER[f[1]], TETRA_CORNER[f[2]]
        vol += float(np.dot(a, np.cross(b, c)))
        n = np.cross(b - a, c - a)
        n = n / np.linalg.norm(n)
        worst = min(worst, float(np.dot(n, (a + b + c) / 3.0 - centroid)))
    return vol / 6.0, worst


# ---------------------------------------------------------------------------------------
# the cloud
# ---------------------------------------------------------------------------------------

class Cloud:
    """A built shard cloud: the per-shard constants, in APPARENT METRES, in a frame whose
    origin is the window's centre.

    Axes are already Blender's, so the room script does no conversion and cannot get it
    wrong:  +x right across the window, +y AWAY from the viewer (the host canvas's +Z),
    +z up the window (the host canvas's +Y).  The viewer sits at negative y.
    """

    __slots__ = ("birth", "size", "axis", "spin_turns", "lift", "drift_scale",
                 "wander", "seed_a", "seed_b", "shade", "thr", "behind", "squash",
                 "panel_w", "panel_h", "n")

    def __init__(self, **kw):
        for k, v in kw.items():
            setattr(self, k, v)


def build_cloud(elements, panel_w_m, panel_h_m, aspect, seed=0x5EED1234, count=None):
    """Mirror of WindowMaterialiseDebris.TryBuildDebris, in apparent metres.

    `elements` is a list of (u0, v0, u1, v1, alpha) in panel UV -- the stand-in for the
    window's CanvasRenderers.  Shards are drawn from it in proportion to visible area,
    which is what makes the debris come from where the window actually broke up rather
    than from a rectangle.
    """
    area = max(panel_w_m * panel_h_m, 1e-5)
    if count is None:
        count = int(round(area * float(D_PER_M2)))
    count = int(np.clip(count, D_MIN_COUNT, D_MAX_COUNT))

    weights, keep = [], []
    total = 0.0
    for i, el in enumerate(elements):
        u0, v0, u1, v1, a = el[0], el[1], el[2], el[3], el[-1]
        if a <= 0.02:
            continue
        w, h = abs(u1 - u0), abs(v1 - v0)
        if w <= 0 or h <= 0:
            continue
        total += float(np.clip(w * h, 0.0004, 1.0))
        weights.append(total)
        keep.append(i)
    if not keep:
        raise SystemExit("build_cloud: no visible element to tear a shard out of")
    weights = np.array(weights, dtype=np.float64)

    rng = Xorshift(seed)
    birth = np.zeros((count, 3), dtype=np.float64)
    thr = np.zeros(count, dtype=np.float64)
    size = np.zeros(count, dtype=np.float64)
    lift = np.zeros(count, dtype=np.float64)
    axis = np.zeros((count, 3), dtype=np.float64)
    spin = np.zeros(count, dtype=np.float64)
    dscale = np.zeros(count, dtype=np.float64)
    wander = np.zeros(count, dtype=np.float64)
    sa = np.zeros(count, dtype=np.float64)
    sb = np.zeros(count, dtype=np.float64)
    shade = np.zeros(count, dtype=np.float64)
    squash = np.zeros(count, dtype=np.float64)
    behind = np.zeros(count, dtype=bool)

    for s in range(count):
        # --- birth point: an element by area, then a uniform point inside it -------------
        pick = rng.r01() * float(weights[-1])
        idx = int(np.searchsorted(weights, pick, side="left"))
        idx = min(idx, len(keep) - 1)
        el = elements[keep[idx]]
        u0, v0, u1, v1 = el[0], el[1], el[2], el[3]
        u = u0 + (u1 - u0) * rng.r01()
        v = v0 + (v1 - v0) * rng.r01()
        u = float(np.clip(u, 0.0, 1.0))
        v = float(np.clip(v, 0.0, 1.0))
        thr[s] = float(threshold(u, v, aspect))
        birth[s] = ((u - 0.5) * panel_w_m, 0.0, (v - 0.5) * panel_h_m)

        # --- size, skewed small ----------------------------------------------------------
        size[s] = float(D_MIN_M) + (float(D_MAX_M) - float(D_MIN_M)) * (rng.r01() ** float(D_SIZE_POW))

        # --- out of the plane. The viewer is at -y, so "toward the head" is -y ------------
        beh = rng.r01() < float(D_BEHIND_FRAC)
        behind[s] = beh
        lift[s] = float(D_LIFT_M) * (0.35 + 0.65 * rng.r01()) * (1.0 if beh else -1.0)

        # --- tumble ----------------------------------------------------------------------
        # THE AXIS PERMUTATION IS NOT COSMETIC. Unity is left-handed (+x right, +y up,
        # +z away); Blender is right-handed (+x right, +y away, +z up). Swapping two axes
        # flips handedness exactly once, so (x, y, z)_unity -> (x, z, y)_blender is the
        # handedness-PRESERVING map between them -- which means a rotation about the mapped
        # axis by the same angle is the same rotation, not its mirror. Drawing the three
        # components in the C# order and then mapping them is what keeps the RNG stream
        # aligned with the shipped one.
        ax, ay, az = rng.signed(), rng.signed(), rng.signed()
        a = np.array([ax, az, ay], dtype=np.float64)
        n = float(np.linalg.norm(a))
        axis[s] = a / n if n > 1e-3 else np.array([0.0, 0.0, 1.0])
        spin[s] = float(D_SPIN_TURNS) * (-1.0 + 2.0 * rng.r01())

        dscale[s] = 0.6 + 0.8 * rng.r01()
        wander[s] = float(D_WANDER_M) * rng.r01()
        sa[s] = rng.r01()
        sb[s] = rng.r01()
        shade[s] = 0.72 + 0.43 * rng.r01()
        squash[s] = 0.30 + 0.42 * rng.r01()

    return Cloud(birth=birth, size=size, axis=axis, spin_turns=spin, lift=lift,
                 drift_scale=dscale, wander=wander, seed_a=sa, seed_b=sb, shade=shade,
                 thr=thr, behind=behind, squash=squash,
                 panel_w=panel_w_m, panel_h=panel_h_m, n=count)


def wind_in_plane(aspect):
    """The downwind direction in CANVAS units, renormalised -- exactly what C# pushes into
    the shader's _Wind, so 'drift metres' means the same distance whatever the panel's
    shape."""
    w = np.array([float(WIND[0]) / max(aspect, 1e-3), float(WIND[1])], dtype=np.float64)
    return w / max(float(np.linalg.norm(w)), 1e-9)


def shard_transforms(cloud: Cloud, debris_front: float, size_scale: float, aspect: float):
    """THE VERTEX SHADER, in numpy. Returns (pos[N,3], rotvec_axis[N,3], angle[N], size[N]).

    Every term is a function of the shard's own baked constants and of `debris_front`.
    Nothing here reads a camera, a head pose or a clock -- which is the whole point, and is
    what makes a preview evidence about the shipped effect rather than about an idea of it.
    """
    a = np.clip((debris_front - cloud.thr) / float(D_LIFE), 0.0, 1.0)
    env = (np.asarray(smoothstep01(0.0, 0.06, a), dtype=np.float64)
           * (1.0 - np.asarray(smoothstep01(0.72, 1.0, a), dtype=np.float64)))
    size = cloud.size * env * size_scale

    w = wind_in_plane(aspect)
    p = cloud.birth.copy()
    trav = np.power(a, 1.35) * cloud.drift_scale * float(D_DRIFT_M)
    p[:, 0] += w[0] * trav          # downwind, in the window's plane (x = across)
    p[:, 2] += w[1] * trav          # ... and up it (z = up the window)
    p[:, 1] += cloud.lift * a       # OUT OF THE PLANE. The entire redesign is this line.
    p[:, 2] -= float(D_FALL_M) * a * a

    wa = cloud.wander * a
    tau = 2.0 * np.pi
    p[:, 0] += wa * np.sin(tau * (cloud.seed_a + 1.7 * a))
    p[:, 2] += wa * np.sin(tau * (cloud.seed_b + 2.3 * a))
    p[:, 1] += wa * np.sin(tau * (cloud.seed_a + cloud.seed_b + 1.3 * a))

    angle = tau * cloud.spin_turns * a
    return p, cloud.axis, angle, size


def shard_mesh(cloud: Cloud, debris_front: float, size_scale: float, aspect: float):
    """One frame of the cloud as a flat triangle soup: (verts[N*12,3], faces[N*4,3]).

    Faces are constant across frames, so a caller emitting a sequence needs them once.
    """
    pos, axis, angle, size = shard_transforms(cloud, debris_front, size_scale, aspect)
    n = cloud.n

    # Rodrigues, vectorised over shards.
    c = np.cos(angle)[:, None]
    s = np.sin(angle)[:, None]
    k = axis

    verts = np.zeros((n * 12, 3), dtype=np.float64)
    faces = np.zeros((n * 4, 3), dtype=np.int32)
    for f in range(4):
        for j in range(3):
            # The squash is on the shard's own local z in C# -- which is local y here, by
            # the same handedness-preserving axis map the tumble axis uses.
            corner = TETRA_CORNER[TETRA_FACE[f][j]][None, :] * np.stack(
                [np.ones(n), cloud.squash, np.ones(n)], axis=1)
            rot = (corner * c
                   + np.cross(k, corner) * s
                   + k * (np.sum(k * corner, axis=1)[:, None]) * (1.0 - c))
            verts[np.arange(n) * 12 + f * 3 + j] = pos + rot * size[:, None]
        base = np.arange(n) * 12 + f * 3
        faces[np.arange(n) * 4 + f] = np.stack([base, base + 1, base + 2], axis=1)
    return verts, faces


def shard_vertex_shade(cloud: Cloud):
    """Per-vertex copy of the per-shard shade jitter, constant over a whole animation."""
    return np.repeat(cloud.shade, 12).astype(np.float32)
