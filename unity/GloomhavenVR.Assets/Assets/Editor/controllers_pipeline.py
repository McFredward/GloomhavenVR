#!/usr/bin/env python3
"""GloomhavenVR - VR controller model download + conversion pipeline.

Source: the W3C Immersive Web group's `webxr-input-profiles` asset package
(https://github.com/immersive-web/webxr-input-profiles), MIT licensed
(packages/assets/LICENSE.md, Copyright (c) 2019 Amazon). Those .glb files are the
vendor-accurate models browsers already show for each controller, and every profile
uses the SAME standardised node names -- `xr_standard_trigger`, `xr_standard_squeeze`,
`xr_standard_thumbstick`, `a_button`/`x_button`, `b_button`/`y_button` -- which lets one runtime
highlight light the right key on any of them without a per-device table.

TRADEMARK NOTE (from the upstream README): the models depict registered trademarks of
their manufacturers in order to portray the physical device accurately. That is exactly
what we use them for -- showing the player the controller in their own hand. They are
NOT re-skinned, NOT used as branding, and NOT used to imply endorsement.

WHY A CONVERTER AND NOT AN IMPORTER: Unity 2021.3 cannot import .glb, and adding
glTFast/UnityGLTF would put a package dependency into the companion project for four
static meshes. glTF is JSON plus a binary blob, so the read is ~200 lines, and OBJ is a
format Unity has imported natively forever. Each logical PART becomes its own OBJ, so
the assembled prefab has one renderer per key and "the trigger lights up" is a material
swap on that renderer -- no submesh indexing, no per-device mesh surgery.

Coordinate conversion: glTF is right-handed, +Y up, -Z forward; Unity is left-handed,
+Y up, +Z forward. Positions and normals map (x, y, -z), UVs flip V, and the triangle
winding is reversed when the node's world matrix has a POSITIVE determinant (negating Z
flips handedness once, so an unmirrored node needs the reversal and an already-mirrored
one -- pico-4's root carries scale (-1,1,1) -- does not).

Output -> unity/GloomhavenVR.Assets/Assets/Bundle/Controllers/
    <profile>/<hand>_<part>__m<N>.obj     one mesh per part per material
    <profile>/tex_<N>.(png|jpg)           base-colour textures, downscaled
    <profile>/controller.json             parts, materials, key anchors, bounds
    LICENSE-webxr-input-profiles.txt      upstream MIT text, shipped with the assets
"""
import io
import json
import os
import struct
import sys
import urllib.request

import numpy as np
from PIL import Image

_HERE = os.path.dirname(os.path.abspath(__file__))
CACHE = os.environ.get("CTRL_CACHE") or os.path.join(_HERE, "..", "..", "..", "..", ".ctrl-cache")
OUT = os.path.normpath(os.path.join(_HERE, "..", "Bundle", "Controllers"))
BASE = ("https://raw.githubusercontent.com/immersive-web/webxr-input-profiles/"
        "main/packages/assets/profiles")
LICENSE_URL = ("https://raw.githubusercontent.com/immersive-web/webxr-input-profiles/"
               "main/packages/assets/LICENSE.md")
UA = {"User-Agent": "GloomhavenVR-controller-build/1.0"}

# The four we ship, and the mod-side id each maps to. The generic profile is the
# fallback for every device without a bespoke model -- including Valve's Steam Frame,
# for which no openly-licensed controller model exists anywhere (checked: the profiles
# registry has no valve entry beyond the Index, and the trademark clause above forbids
# inventing one).
PROFILES = {
    "quest3":  "meta-quest-touch-plus",
    "pico4":   "pico-4",
    "index":   "valve-index",
    "generic": "generic-trigger-squeeze-thumbstick",
}
HANDS = ("left", "right")

# Longest prefix wins, so `xr_standard_thumbstick_xaxis...` still lands on `thumbstick`.
PART_RULES = (
    ("xr_standard_trigger", "trigger"),
    ("xr_standard_squeeze", "squeeze"),
    ("xr_standard_thumbstick", "thumbstick"),
    ("xr_standard_touchpad", "touchpad"),
    # A/X is the mod's PrimaryButton and B/Y its SecondaryButton on every device, so the
    # parts carry THOSE names: the right-hand assets say a_/b_ and the left-hand ones say
    # x_/y_, and a runtime that had to know which is which per hand would learn nothing.
    ("a_button", "button_primary"),
    ("x_button", "button_primary"),
    ("b_button", "button_secondary"),
    ("y_button", "button_secondary"),
    ("thumbrest", "thumbrest"),
    ("grip", "squeeze"),        # pico-4 names its squeeze mesh `grip`
    ("joystick", "thumbstick"),  # pico-4 names its stick mesh `joystick`
)
MAX_TEX = 1024


def fetch(url, dest):
    if os.path.exists(dest) and os.path.getsize(dest) > 0:
        return dest
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    req = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(req) as r, open(dest + ".part", "wb") as f:
        f.write(r.read())
    os.replace(dest + ".part", dest)
    print(f"  dl {os.path.basename(dest)} {os.path.getsize(dest)/1e6:.2f} MB")
    return dest


def read_glb(path):
    data = open(path, "rb").read()
    if data[:4] != b"glTF":
        raise SystemExit(f"{path} is not a binary glTF")
    length = struct.unpack("<I", data[8:12])[0]
    off, js, binary = 12, None, b""
    while off < length:
        clen, ctype = struct.unpack("<II", data[off:off + 8])
        off += 8
        chunk = data[off:off + clen]
        off += clen
        if ctype == 0x4E4F534A:
            js = json.loads(chunk.decode("utf-8"))
        elif ctype == 0x004E4942:
            binary = chunk
    return js, binary


# ---- accessors ---------------------------------------------------------------------

_COMP = {5120: "b", 5121: "B", 5122: "h", 5123: "H", 5125: "I", 5126: "f"}
_NCOMP = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT4": 16}


def accessor(g, binary, index):
    acc = g["accessors"][index]
    n = _NCOMP[acc["type"]]
    fmt = _COMP[acc["componentType"]]
    size = np.dtype(fmt).itemsize
    count = acc["count"]
    if "bufferView" not in acc:
        return np.zeros((count, n), dtype=np.float64)
    bv = g["bufferViews"][acc["bufferView"]]
    start = bv.get("byteOffset", 0) + acc.get("byteOffset", 0)
    stride = bv.get("byteStride") or (size * n)
    raw = np.frombuffer(binary, dtype=np.uint8, count=stride * (count - 1) + size * n, offset=start)
    idx = (np.arange(count)[:, None] * stride + np.arange(n)[None, :] * size)
    out = np.empty((count, n), dtype=fmt)
    flat = raw
    for j in range(n):
        cols = idx[:, j]
        byts = np.stack([flat[cols + k] for k in range(size)], axis=1)
        out[:, j] = byts.copy().view(fmt).reshape(-1)
    vals = out.astype(np.float64)
    if acc.get("normalized"):
        vals /= {"b": 127.0, "B": 255.0, "h": 32767.0, "H": 65535.0}.get(fmt, 1.0)
    return vals


# ---- node hierarchy ----------------------------------------------------------------

def trs(node):
    m = np.eye(4)
    if "matrix" in node:
        return np.array(node["matrix"], dtype=np.float64).reshape(4, 4).T
    t = node.get("translation", [0, 0, 0])
    r = node.get("rotation", [0, 0, 0, 1])
    s = node.get("scale", [1, 1, 1])
    x, y, z, w = r
    rot = np.array([
        [1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
        [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
        [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)],
    ])
    m[:3, :3] = rot @ np.diag(s)
    m[:3, 3] = t
    return m


def world_matrices(g):
    """node index -> (world matrix, ancestor name chain)."""
    parent = {}
    for i, n in enumerate(g["nodes"]):
        for c in n.get("children", []):
            parent[c] = i
    out = {}

    def solve(i):
        if i in out:
            return out[i]
        n = g["nodes"][i]
        local = trs(n)
        if i in parent:
            pm, chain = solve(parent[i])
            out[i] = (pm @ local, chain + [n.get("name", "")])
        else:
            out[i] = (local, [n.get("name", "")])
        return out[i]

    for i in range(len(g["nodes"])):
        solve(i)
    return out


def classify(chain):
    """Logical part for a mesh node, from its own name and its ancestors'."""
    for name in reversed(chain):
        low = (name or "").lower()
        for prefix, part in PART_RULES:
            if low.startswith(prefix):
                return part
    return "body"


def key_anchors(g, wm):
    """Where each key SITS on the controller, in Unity-space model coordinates.

    Taken from the profile's own `*_pressed_value` node (falling back to `_min`), which
    exists even for a key that has NO separate mesh -- and that is the whole point. The
    Valve Index's grip is a force sensor in the handle with nothing to move, and the
    generic profile has no face buttons at all, so "tint the key's renderer" cannot be the
    only way to point at a key. An anchor lets the runtime put a marker exactly where the
    key is on a device that cannot light it up."""
    info = {}
    for i, n in enumerate(g["nodes"]):
        name = (n.get("name") or "").lower()
        for suffix in ("_pressed_value", "_pressed_min", "_pressed_max"):
            if not name.endswith(suffix):
                continue
            part = classify([n.get("name", "")])
            if part == "body":
                continue
            slot = info.setdefault(part, {"part": part})
            p = wm[i][0][:3, 3]
            slot[suffix[9:]] = [float(p[0]), float(p[1]), float(-p[2])]
    # A LIST, not a dict: Unity's JsonUtility (which BuildControllers.cs reads this with)
    # has no dictionary support at all, and would silently produce an empty object.
    out = []
    for part in sorted(info):
        e = info[part]
        pos = e.get("value") or e.get("min") or e.get("max")
        if pos is not None:
            out.append({"part": part, "position": pos})
    return out


# ---- conversion --------------------------------------------------------------------

def convert(profile_dir, hand, glb, out_dir, prefix):
    g, binary = read_glb(glb)
    wm = world_matrices(g)
    # part+material -> lists of (positions, normals, uvs, indices)
    buckets = {}
    for i, node in enumerate(g["nodes"]):
        if "mesh" not in node:
            continue
        m, chain = wm[i]
        part = classify(chain)
        nrm_m = np.linalg.inv(m[:3, :3]).T
        flip = np.linalg.det(m[:3, :3]) > 0
        for prim in g["meshes"][node["mesh"]]["primitives"]:
            if prim.get("mode", 4) != 4:
                continue
            attrs = prim["attributes"]
            pos = accessor(g, binary, attrs["POSITION"])
            pos = (m @ np.hstack([pos, np.ones((len(pos), 1))]).T).T[:, :3]
            pos[:, 2] *= -1.0
            if "NORMAL" in attrs:
                nrm = accessor(g, binary, attrs["NORMAL"]) @ nrm_m.T
                ln = np.linalg.norm(nrm, axis=1, keepdims=True)
                nrm = nrm / np.where(ln < 1e-12, 1.0, ln)
                nrm[:, 2] *= -1.0
            else:
                nrm = np.zeros_like(pos)
            uv = accessor(g, binary, attrs["TEXCOORD_0"])[:, :2] if "TEXCOORD_0" in attrs \
                else np.zeros((len(pos), 2))
            uv = np.column_stack([uv[:, 0], 1.0 - uv[:, 1]])
            if "indices" in prim:
                idx = accessor(g, binary, prim["indices"]).astype(np.int64).reshape(-1)
            else:
                idx = np.arange(len(pos), dtype=np.int64)
            tris = idx.reshape(-1, 3)
            if flip:
                tris = tris[:, ::-1]
            key = (part, prim.get("material", -1))
            buckets.setdefault(key, []).append((pos, nrm, uv, tris))

    written = []
    for (part, mat), chunks in sorted(buckets.items()):
        vs, ns, ts, fs, base = [], [], [], [], 0
        for pos, nrm, uv, tris in chunks:
            vs.append(pos)
            ns.append(nrm)
            ts.append(uv)
            fs.append(tris + base)
            base += len(pos)
        pos = np.vstack(vs)
        nrm = np.vstack(ns)
        uv = np.vstack(ts)
        tris = np.vstack(fs)
        name = f"{prefix}_{part}__m{mat}"
        path = os.path.join(out_dir, name + ".obj")
        with open(path, "w") as f:
            f.write(f"# GloomhavenVR - {profile_dir} {hand} '{part}' (glTF material {mat})\n")
            f.write(f"# generated by controllers_pipeline.py from webxr-input-profiles (MIT)\n")
            f.write(f"o {name}\n")
            np.savetxt(f, pos, fmt="v %.6f %.6f %.6f")
            np.savetxt(f, uv, fmt="vt %.6f %.6f")
            np.savetxt(f, nrm, fmt="vn %.6f %.6f %.6f")
            # v/vt/vn share one index space here (the three arrays are written in
            # parallel and are the same length), so each corner is repeated three
            # times: [a,a,a, b,b,b, c,c,c] -> "f a/a/a b/b/b c/c/c".
            np.savetxt(f, np.repeat(tris + 1, 3, axis=1),
                       fmt="f %d/%d/%d %d/%d/%d %d/%d/%d")
        # BOUNDS AND SIGNED VOLUME TRAVEL WITH THE MESH so BuildControllers.cs can check
        # what Unity's OBJ importer actually produced instead of trusting it. A mirroring
        # importer (some flip X on import) would leave the volume's SIGN untouched -- two
        # flips cancel -- but it cannot hide from the bounds, which is why both are here.
        t = pos[tris]
        vol = float(np.einsum("ij,ij->i", t[:, 0],
                              np.cross(t[:, 1], t[:, 2])).sum() / 6.0)
        written.append({"part": part, "material": mat, "mesh": name,
                        "vertices": int(len(pos)), "triangles": int(len(tris)),
                        "min": [float(x) for x in pos.min(0)],
                        "max": [float(x) for x in pos.max(0)],
                        "signedVolume": vol})
    return g, wm, written


def export_textures(g, binary, out_dir):
    mats = []
    tex_files = {}
    for mi, mat in enumerate(g.get("materials", [])):
        pbr = mat.get("pbrMetallicRoughness", {})
        entry = {"index": mi, "name": mat.get("name", f"material{mi}"),
                 "baseColorFactor": pbr.get("baseColorFactor", [1, 1, 1, 1]),
                 "metallic": pbr.get("metallicFactor", 1.0),
                 "roughness": pbr.get("roughnessFactor", 1.0),
                 "texture": None}
        bct = pbr.get("baseColorTexture")
        if bct is not None:
            src = g["textures"][bct["index"]].get("source")
            if src is not None:
                if src not in tex_files:
                    img = g["images"][src]
                    bv = g["bufferViews"][img["bufferView"]]
                    start = bv.get("byteOffset", 0)
                    raw = binary[start:start + bv["byteLength"]]
                    im = Image.open(io.BytesIO(raw))
                    im.load()
                    if max(im.size) > MAX_TEX:
                        scale = MAX_TEX / max(im.size)
                        im = im.resize((max(1, int(im.width * scale)),
                                        max(1, int(im.height * scale))), Image.LANCZOS)
                    has_alpha = im.mode in ("RGBA", "LA") and \
                        np.asarray(im.convert("RGBA"))[..., 3].min() < 255
                    if has_alpha:
                        fn = f"tex_{src}.png"
                        im.convert("RGBA").save(os.path.join(out_dir, fn), optimize=True)
                    else:
                        fn = f"tex_{src}.jpg"
                        im.convert("RGB").save(os.path.join(out_dir, fn), quality=92)
                    tex_files[src] = fn
                    print(f"    tex {fn} {im.size[0]}x{im.size[1]} "
                          f"{os.path.getsize(os.path.join(out_dir, fn))/1e3:.0f} kB")
                entry["texture"] = tex_files[src]
        mats.append(entry)
    return mats


def main():
    only = sys.argv[1:]
    os.makedirs(OUT, exist_ok=True)
    fetch(LICENSE_URL, os.path.join(CACHE, "LICENSE.md"))
    with open(os.path.join(CACHE, "LICENSE.md")) as src, \
            open(os.path.join(OUT, "LICENSE-webxr-input-profiles.txt"), "w") as dst:
        dst.write("Controller models under Assets/Bundle/Controllers/ come from\n"
                  "https://github.com/immersive-web/webxr-input-profiles\n"
                  "(packages/assets), converted to OBJ by controllers_pipeline.py.\n"
                  "They are shipped unmodified in shape and texture.\n\n" + src.read())

    for mod_id, profile in PROFILES.items():
        if only and mod_id not in only:
            continue
        out_dir = os.path.join(OUT, mod_id)
        os.makedirs(out_dir, exist_ok=True)
        print(f"[{mod_id}] {profile}")
        manifest = {"modId": mod_id, "profile": profile, "source": f"{BASE}/{profile}"}
        for hand in HANDS:
            glb = fetch(f"{BASE}/{profile}/{hand}.glb",
                        os.path.join(CACHE, profile, f"{hand}.glb"))
            g, wm, meshes = convert(profile, hand, glb, out_dir, f"{mod_id}_{hand}")
            _, binary = read_glb(glb)
            mats = export_textures(g, binary, out_dir)
            manifest[hand] = {
                "meshes": meshes,
                "materials": mats,
                "anchors": key_anchors(g, wm),
            }
            tris = sum(m["triangles"] for m in meshes)
            parts = sorted({m["part"] for m in meshes})
            print(f"    {hand}: {len(meshes)} mesh(es), {tris} tris, parts {parts}")
        with open(os.path.join(out_dir, "controller.json"), "w") as f:
            json.dump(manifest, f, indent=1)
    print(f"\nwrote {OUT}")


if __name__ == "__main__":
    main()
