# windowmaterialise_field.py - the THIRD copy of the window-materialise field, in numpy.
#
# WHY A THIRD COPY EXISTS. The effect has two halves that must agree: a shader
# (unity/GloomhavenVR.Assets/Assets/Bundle/Table/WindowMaterialise.shader) paints the wind-borne
# flakes, and C# (src/GloomhavenVR/WorldUI/WindowMaterialiseField.cs) decides which of the window's
# own uGUI elements are still drawn. They only agree - the element vanishing exactly where the
# flakes peel off it - if they compute the same number. Neither of those two can be run on this
# machine (no game install, no headset), and this project has three rejected art rounds behind it
# because a preview was explained away instead of believed. So the frame strips a human reviews are
# rendered from arithmetic that is transcribed line by line from the shader, in float32, and the
# constants are read out of the C# file at import time rather than retyped - see CONSTANTS below.
#
# EVERYTHING IS float32 ON PURPOSE. The hash folds a large product through frac(); in float64 it
# produces a visibly different noise field than the GPU does. A preview in the wrong precision is a
# preview of a different effect.
#
#   from windowmaterialise_field import Field
#   f = Field(aspect=1.55)
#   rgba = f.flakes(uv_u, uv_v, progress)      # premultiplied, exactly the shader's frag()
#   a    = f.presence_of(sample_thresholds, progress)   # exactly C# PresenceOf()
import pathlib
import re

import numpy as np

F32 = np.float32

_CS = (pathlib.Path(__file__).resolve().parents[2]
       / "src" / "GloomhavenVR" / "WorldUI" / "WindowMaterialiseField.cs")


def _read_cs_constants():
    """Read Softness/Ragged/FrontScale/AgeSpan/Drift/Wind straight out of the C# file.

    A preview that carries its own copy of the numbers is a preview that silently goes stale the
    first time one of them is tuned. Parsing the source is ugly and is exactly the point: if the C#
    changes and this file cannot find the constant any more, the preview FAILS instead of lying.
    """
    src = _CS.read_text(encoding="utf-8")
    out = {}
    for name in ("Softness", "Ragged", "FrontScale", "AgeSpan", "Drift", "PlumeSpan", "Spread", "Streak", "Thin"):
        m = re.search(r"internal const float %s = ([0-9.]+)f;" % name, src)
        if not m:
            raise SystemExit(
                "windowmaterialise_field.py: could not find 'internal const float %s' in %s. "
                "The C# field was changed and this preview would now render a DIFFERENT effect "
                "than the one that ships. Fix the parse (or the constant) before rendering "
                "anything a human is going to look at." % (name, _CS))
        out[name] = float(m.group(1))
    m = re.search(r"Wind = new Vector2\(([0-9.\-]+)f, ([0-9.\-]+)f\)\.normalized", src)
    if not m:
        raise SystemExit("windowmaterialise_field.py: could not find the Wind vector in %s." % _CS)
    wx, wy = float(m.group(1)), float(m.group(2))
    n = (wx * wx + wy * wy) ** 0.5
    out["Wind"] = (wx / n, wy / n)
    m = re.search(r"internal const int Samples = (\d+);", src)
    out["Samples"] = int(m.group(1)) if m else 5
    return out


CONSTANTS = _read_cs_constants()


def frac(x):
    return x - np.floor(x)


def hash21(vx, vy):
    """The shader's hash21, verbatim."""
    px = frac(vx * F32(123.34))
    py = frac(vy * F32(456.21))
    d = px * (px + F32(45.32)) + py * (py + F32(45.32))   # dot(p, p + 45.32)
    px = px + d
    py = py + d
    return frac(px * py)


def vnoise(vx, vy):
    """The shader's vnoise, verbatim."""
    ix, iy = np.floor(vx), np.floor(vy)
    fx, fy = vx - ix, vy - iy
    ux = fx * fx * (F32(3.0) - F32(2.0) * fx)
    uy = fy * fy * (F32(3.0) - F32(2.0) * fy)
    a = hash21(ix, iy)
    b = hash21(ix + F32(1.0), iy)
    c = hash21(ix, iy + F32(1.0))
    d = hash21(ix + F32(1.0), iy + F32(1.0))
    return (a + (b - a) * ux) + ((c + (d - c) * ux) - (a + (b - a) * ux)) * uy


def smoothstep(e0, e1, x):
    """HLSL smoothstep(edge0, edge1, x). The edges are always scalars here; x may be an array."""
    denom = max(float(e1) - float(e0), 1e-6)
    t = np.clip((x - F32(e0)) / F32(denom), F32(0.0), F32(1.0))
    return t * t * (F32(3.0) - F32(2.0) * t)


class Field:
    """One panel's worth of the field. `aspect` is width / height of the host rect."""

    def __init__(self, aspect, tail_fade=1.0,
                 tint=(0.86, 0.80, 0.66), glow=1.0, intensity=1.0,
                 flake_density=34.0, flake_cut=0.52, flake_sharp=2.0,
                 edge_gain=1.55, plume_gain=0.85):
        self.aspect = F32(aspect)
        self.wind = (F32(CONSTANTS["Wind"][0]), F32(CONSTANTS["Wind"][1]))
        self.softness = F32(CONSTANTS["Softness"])
        self.ragged = F32(CONSTANTS["Ragged"])
        self.front_scale = F32(CONSTANTS["FrontScale"])
        self.age_span = F32(CONSTANTS["AgeSpan"])
        self.drift = F32(CONSTANTS["Drift"])
        self.plume_span = F32(CONSTANTS["PlumeSpan"])
        self.spread = F32(CONSTANTS["Spread"])
        self.streak = F32(CONSTANTS["Streak"])
        self.thin = F32(CONSTANTS["Thin"])
        self.tail_fade = F32(tail_fade)
        self.samples = CONSTANTS["Samples"]
        self.tint = tuple(F32(c) for c in tint)
        self.glow = F32(glow)
        self.intensity = F32(intensity)
        self.flake_density = F32(flake_density)
        self.flake_cut = F32(flake_cut)
        self.flake_sharp = F32(flake_sharp)
        self.edge_gain = F32(edge_gain)
        self.plume_gain = F32(plume_gain)

    # ---- the field, shared by both halves -------------------------------------------------

    def sweep(self, u, v):
        wx, wy = self.wind
        n = max(abs(float(wx)) + abs(float(wy)), 1e-4)
        return ((u - F32(0.5)) * wx + (v - F32(0.5)) * wy + F32(0.5 * n)) / F32(n)

    def q_of(self, u, v):
        return u * self.aspect, v

    def threshold(self, u, v):
        qx, qy = self.q_of(u, v)
        n = vnoise(qx * self.front_scale, qy * self.front_scale)
        s = self.sweep(u, v)
        return s + (n - s) * self.ragged

    def front(self, progress):
        return F32(progress) * (F32(1.0) + F32(2.0) * self.softness) - self.softness

    # ---- half one: how much of a uGUI ELEMENT is left (mirrors C# PresenceOf) ---------------

    def presence(self, threshold, progress):
        f = self.front(progress)
        return smoothstep(f - self.softness, f + self.softness, threshold)

    def presence_of(self, thresholds, progress):
        """`thresholds` is the element's `Samples` corner+centre thresholds. Mean of exact terms,
        so it is exactly 1 at progress 0 and exactly 0 at progress 1."""
        return float(np.mean([self.presence(F32(t), progress) for t in thresholds]))

    def element_thresholds(self, u0, v0, u1, v1):
        """The five sample points C# uses: the four corners and the centre, clamped into 0..1."""
        pts = [(u0, v0), (u1, v0), (u0, v1), (u1, v1), (0.5 * (u0 + u1), 0.5 * (v0 + v1))]
        return [float(self.threshold(F32(min(max(u, 0.0), 1.0)), F32(min(max(v, 0.0), 1.0))))
                for u, v in pts]

    # ---- half two: the flakes (mirrors the shader's frag(), premultiplied) ------------------

    def _speck(self, n, cut):
        t = np.clip((n - cut) / np.maximum(F32(1.0) - cut, F32(1e-3)), F32(0.0), F32(1.0))
        return np.power(t, self.flake_sharp)

    def _in_rect(self, u, v, w):
        e_u = smoothstep(F32(0.0), F32(w), u) * smoothstep(F32(0.0), F32(w), F32(1.0) - u)
        e_v = smoothstep(F32(0.0), F32(w), v) * smoothstep(F32(0.0), F32(w), F32(1.0) - v)
        return e_u * e_v

    def flakes(self, u, v, progress):
        """Returns (rgb_premultiplied, alpha) for a grid of panel UVs. Verbatim frag()."""
        p = F32(progress)
        front = self.front(p)

        # A - the crumbling edge
        qx, qy = self.q_of(u, v)
        n_a = vnoise(qx * self.front_scale, qy * self.front_scale)
        s_a = self.sweep(u, v)
        t_a = s_a + (n_a - s_a) * self.ragged
        age_a = np.clip((front - t_a) / F32(max(float(self.age_span), 1e-3)), F32(0.0), F32(1.0))
        env_a = (smoothstep(F32(0.0), F32(0.12), age_a)
                 * (F32(1.0) - smoothstep(F32(0.28), F32(0.80), age_a)))
        a_layer = (self._speck(vnoise(qx * self.flake_density, qy * self.flake_density),
                               self.flake_cut)
                   * env_a * self.edge_gain * self._in_rect(u, v, 0.03))

        # B - the plume
        travel = float(self.drift) * (float(p) ** 1.5)
        inv_a = 1.0 / max(float(self.aspect), 1e-3)
        wu, wv = float(self.wind[0]) * inv_a, float(self.wind[1])
        pu, pv = -float(self.wind[1]) * inv_a, float(self.wind[0])
        ub = u - F32(wu * travel)
        vb = v - F32(wv * travel)
        qb0x, qb0y = self.q_of(ub, vb)
        n_b = vnoise(qb0x * self.front_scale, qb0y * self.front_scale)
        shear = (n_b - F32(0.5)) * self.spread * F32(travel)
        ub = ub - F32(pu) * shear
        vb = vb - F32(pv) * shear
        qbx, qby = self.q_of(ub, vb)
        s_b = self.sweep(ub, vb)
        t_b = s_b + (n_b - s_b) * self.ragged
        age_b = np.clip((front - t_b) / F32(max(float(self.plume_span), 1e-3)), F32(0.0), F32(1.0))
        env_b = (smoothstep(F32(0.03), F32(0.22), age_b)
                 * (F32(1.0) - smoothstep(F32(0.55), F32(1.0), age_b)))
        wn = (float(self.wind[0]) ** 2 + float(self.wind[1]) ** 2) ** 0.5
        wqx, wqy = float(self.wind[0]) / wn, float(self.wind[1]) / wn
        pqx, pqy = -wqy, wqx
        stretch = F32(1.0) + self.streak * age_b
        along = (qbx * F32(wqx) + qby * F32(wqy)) / stretch
        across = qbx * F32(pqx) + qby * F32(pqy)
        qsx = F32(wqx) * along + F32(pqx) * across
        qsy = F32(wqy) * along + F32(pqy) * across
        tail = F32(1.0) - self.tail_fade * smoothstep(F32(0.68), F32(1.0), p)
        edge_b = self._in_rect(ub, vb, 0.13) * (F32(0.45) + F32(0.55) * n_b)
        b_layer = (self._speck(vnoise(qsx * self.flake_density * F32(1.83) + F32(7.3),
                                      qsy * self.flake_density * F32(1.83) + F32(7.3)),
                               self.flake_cut + self.thin * age_b)
                   * env_b * self.plume_gain * edge_b * tail)

        alpha = np.clip(a_layer + b_layer, F32(0.0), F32(1.0)) * self.intensity
        rgb = np.stack([self.tint[i] * alpha * self.glow for i in range(3)], axis=-1)
        return rgb, alpha
