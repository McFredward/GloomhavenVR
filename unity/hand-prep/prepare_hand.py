# prepare_hand.py — Blender headless prep: a raw Hunyuan3D hand/glove GLB -> the canonical
# "prepped" form that rig_hand.py consumes (the same contract Hand_prepped.glb satisfies):
#
#   - LEFT hand, root at the wrist crease, +Z along the fingers, +Y = back of hand,
#     palm at -Y, thumb on -X, middle fingertip at Z ~= 0.19 m, ~18k tris, UVs intact.
#
# It also DERIVES the per-model rig landmarks (finger Root/Mid/Tip joints, wrist, palm,
# grab, index-tip anchors) from the mesh geometry and writes them to a committed JSON that
# rig_hand.py loads via RIG_HAND_JOINTS — the original glove's hardcoded constants remain
# the default, so the existing VRHand build stays byte-compatible.
#
# NOTE (2026-07): the finger/thumb JOINT CHAINS in the JSON are only SEEDS now —
# rig_hand.py's refit_chains() re-derives MCP placement, digit direction and the fingertip
# from the actual mesh before building the armature (this file's webbing detection proved
# unreliable on armored knuckles: finger side-bulge verts pollute the inter-column gap
# max, which parked the styled MCPs at the 0.62*tip_z clamp — mid-finger — so only the
# fingertips curled). wrist/palm/grab/cap_uv/wrist_mode are used as-is.
#
# CLEANUP RECIPES applied (established in this repo):
#   - PRE-DECIMATE GLOBAL WELD (the mask lesson): the AI mesh is thousands of disconnected
#     shells whose seams coincide EXACTLY only before decimation moves verts — weld first,
#     then decimate, or the seams can never be fused again.
#   - Decimate COLLAPSE (preserves UVs) to ~18k tris — same budget as the original glove;
#     stalled collapses are freed with light welds between passes.
#   - *No* hole filling here: rig_hand.py's watertight pass does that (minus the wrist cap)
#     at the final scale, after the L/R mirror.
#   - Albedo extracted as a loose 2K PNG (BuildHands.cs loose-texture pattern) + a dark
#     uniform CAP_UV texel scanned per model (rig_hand's hole-fill UV fallback).
#
# ORIENTATION (per-model config below): both current Hunyuan hands probed identical —
# fingers +Z, back of hand -Y, palm +Y, thumb -X. That is a RIGHT hand and exactly the
# contract frame MIRRORED across the XZ plane, so canonicalization = negate Y + flip
# normals (one op, keeps thumb on -X). A future model with a different frame gets its own
# entry in MODEL_CONFIG.
#
# DE-LEANING: Hunyuan hands are not authored straight — the plate gauntlet leans ~20 deg
# toward the palm over its height. Pass 1 detects the fingers/webbing/wrist roughly, fits
# the palm-section centroid axis (wrist->webbing) and rotates it onto +Z about the wrist;
# pass 2 re-detects everything on the straightened mesh.
#
# WRIST DETECTION: cross-section minima are unreliable on armored cuffs, so the wrist
# crease comes from ANATOMY: on the original glove, (middleTip - webbing) / (middleTip -
# wrist) = 0.475. Models with PRE-CURLED fingers (foreshortened tips skew that ratio) get
# a manual "wrist_frac" override in MODEL_CONFIG, verified against the marker renders.
#
# RUN (headless):
#   /home/claw/blender-4.2/blender --background --python unity/hand-prep/prepare_hand.py -- \
#       <src.glb> <name> [render <render_dir>]
#   <name>  HandPlate | HandArcane  ->  writes:
#       ressources/hands/<name>_prepped.glb      (gitignored intermediate, regenerable)
#       unity/hand-prep/<name>_joints.json       (committed rig landmarks)
#       unity/GloomhavenVR.Assets/Assets/Bundle/Hands/<asset>_albedo.png (committed 2K albedo)

import bpy, bmesh, json, math, os, sys
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
SRC = argv[0]
NAME = argv[1]
DO_RENDER = len(argv) > 2 and argv[2] == "render"
RENDER_DIR = argv[3] if len(argv) > 3 else "/tmp"

REPO = os.environ.get("PREPARE_HAND_REPO",
                      os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))))
GLB_OUT_DIR = os.environ.get("PREPARE_HAND_GLB_DIR", "/home/claw/gloomhaven_vr/ressources/hands")
JSON_OUT = os.path.join(REPO, "unity", "hand-prep", f"{NAME}_joints.json")
BUNDLE_HANDS = os.path.join(REPO, "unity", "GloomhavenVR.Assets", "Assets", "Bundle", "Hands")

TARGET_TRIS = 18000        # original glove budget
TARGET_LEN = 0.19          # metres wrist crease -> middle fingertip (contract)
TEX_MAX = 2048             # albedo downscale cap (original glove ships 2K)
WEB_RATIO = 0.475          # (tip - webbing) / (tip - wrist), measured on the original glove

# Per-model constants (see header). Values verified against the joint-marker renders.
MODEL_CONFIG = {
    # plate-armor gauntlet (hunyuan3d-a22a9142…): fingers pre-curled -> manual crease at
    # the top of the buckled cuff band (read off the renders).
    "HandPlate":  {"asset": "VRHandPlate",  "canon": "mirror_y", "wrist_frac": 0.46},
    # arcane-runes mage glove (hunyuan3d-31b7b393…): fingers straight -> anatomy ratio.
    "HandArcane": {"asset": "VRHandArcane", "canon": "mirror_y"},
}
CFG = MODEL_CONFIG.get(NAME)
if CFG is None:
    raise SystemExit(f"unknown model name '{NAME}' — add it to MODEL_CONFIG")


def log(*a):
    print("[prepare_hand]", *a)


# ---------------------------------------------------------------------------------------
# import + canonicalize
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
if not meshes:
    raise SystemExit("no mesh in GLB")
bpy.ops.object.select_all(action='DESELECT')
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active
obj.name = NAME
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

if CFG["canon"] == "mirror_y":
    # RIGHT hand, palm +Y  ->  LEFT hand, palm -Y (thumb stays -X). Mirror = flip normals.
    obj.data.transform(Matrix.Diagonal((1.0, -1.0, 1.0, 1.0)))
    bm = bmesh.new(); bm.from_mesh(obj.data)
    for f in bm.faces:
        f.normal_flip()
    bm.to_mesh(obj.data); bm.free()
    obj.data.update()
elif CFG["canon"] != "identity":
    raise SystemExit(f"unknown canon op {CFG['canon']}")

me = obj.data
raw_h0 = (max(v.co.z for v in me.vertices) - min(v.co.z for v in me.vertices))

# ---------------------------------------------------------------------------------------
# 1) pre-decimate global weld while the AI shell seams still coincide (mask lesson).
weld = 0.0005 * (raw_h0 / TARGET_LEN)
bm = bmesh.new(); bm.from_mesh(me)
v0, b0 = len(bm.verts), sum(1 for e in bm.edges if e.is_boundary)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=weld)
bm.to_mesh(me); bm.free(); me.update()
bm = bmesh.new(); bm.from_mesh(me)
log(f"pre-weld @{weld:.4f}: verts {v0} -> {len(bm.verts)}, boundary edges {b0} -> "
    f"{sum(1 for e in bm.edges if e.is_boundary)}")
bm.free()

# 2) decimate (COLLAPSE preserves UVs) to the glove budget; iterate — COLLAPSE stalls on
#    the non-manifold AI mesh, so between stalled passes re-weld slightly to free it up.
prev = None
for _ in range(6):
    tris = sum(len(p.vertices) - 2 for p in me.polygons)
    if tris <= TARGET_TRIS * 1.08:
        break
    if prev is not None and tris > prev * 0.95:
        # stalled: drop loose geometry, dissolve degenerates, re-weld a touch stronger
        bm = bmesh.new(); bm.from_mesh(me)
        loose = [v for v in bm.verts if not v.link_faces]
        if loose:
            bmesh.ops.delete(bm, geom=loose, context='VERTS')
        bmesh.ops.dissolve_degenerate(bm, edges=bm.edges, dist=weld * 0.1)
        bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=weld * 1.5)
        bm.to_mesh(me); bm.free(); me.update()
    prev = tris
    m = obj.modifiers.new("dec", 'DECIMATE')
    m.decimate_type = 'COLLAPSE'
    m.use_collapse_triangulate = True
    m.ratio = TARGET_TRIS / tris
    bpy.ops.object.modifier_apply(modifier=m.name)
tris = sum(len(p.vertices) - 2 for p in me.polygons)
log(f"decimated to {tris} tris / {len(me.vertices)} verts"
    + (" (WARNING: above budget — collapse stalled)" if tris > TARGET_TRIS * 1.2 else ""))


# ---------------------------------------------------------------------------------------
# landmark detection helpers (work on a plain list of Vectors in the CURRENT mesh frame)
def detect(V):
    """Find the 4 finger columns, webbing height and wrist crease. Returns a dict or
    raises SystemExit when the geometry cannot satisfy the contract."""
    zmin = min(c.z for c in V); zmax = max(c.z for c in V)
    h = zmax - zmin

    def cluster_band(z0, z1):
        band = sorted((c for c in V if z0 <= c.z <= z1), key=lambda c: c.x)
        if len(band) < 40:
            return []
        gap = 0.02 * h
        out = [[band[0]]]
        for c in band[1:]:
            if c.x - out[-1][-1].x > gap:
                out.append([c])
            else:
                out[-1].append(c)
        return [cl for cl in out if len(cl) >= 8]

    cols = band_frac = None
    for i in range(24):
        f0 = 0.92 - 0.02 * i
        cl = cluster_band(zmin + h * (f0 - 0.02), zmin + h * f0)
        if len(cl) == 4:
            cols, band_frac = cl, f0
            break
    if cols is None:
        raise SystemExit("could not find a band cutting 4 finger columns — hand unusable?")

    colinfo = []
    for cl in cols:
        xs_ = [c.x for c in cl]
        ys_ = [c.y for c in cl]
        colinfo.append({"x0": min(xs_), "x1": max(xs_), "xc": sum(xs_) / len(xs_),
                        "yc": sum(ys_) / len(ys_)})

    band_lo = zmin + h * (band_frac - 0.02)
    web_zs = []
    for a, b in zip(colinfo, colinfo[1:]):
        g0, g1 = a["x1"] + 0.002 * h, b["x0"] - 0.002 * h
        gap = [c.z for c in V if g0 <= c.x <= g1 and c.z < band_lo]
        if gap:
            web_zs.append(max(gap))
    web_z = sum(web_zs) / len(web_zs)

    if "wrist_frac" in CFG:
        wrist_z = zmin + h * CFG["wrist_frac"]
    else:
        wrist_z = zmax - (zmax - web_z) / WEB_RATIO

    # wrist centre from PALM-column verts only (thumb/cuff ornaments must not bias it)
    px0, px1 = colinfo[0]["x0"], colinfo[-1]["x1"]
    wsl = [c for c in V if abs(c.z - wrist_z) < 0.02 * h and px0 <= c.x <= px1]
    if len(wsl) < 10:
        wsl = [c for c in V if abs(c.z - wrist_z) < 0.02 * h]
    wc = Vector((sum(c.x for c in wsl) / len(wsl), sum(c.y for c in wsl) / len(wsl), wrist_z))

    return {"zmin": zmin, "zmax": zmax, "h": h, "cols": colinfo, "band_frac": band_frac,
            "web_zs": web_zs, "web_z": web_z, "wrist": wc}


# ---------------------------------------------------------------------------------------
# 3) PASS 1: rough detection -> de-leaning rotation (palm centroid axis -> +Z).
V = [v.co.copy() for v in me.vertices]
d1 = detect(V)
log(f"pass1: band z-frac {d1['band_frac']:.2f}, webbing z {d1['web_z']:.4f}, wrist "
    f"{tuple(round(c, 4) for c in d1['wrist'])} (frac {(d1['wrist'].z - d1['zmin']) / d1['h']:.3f})")

px0, px1 = d1["cols"][0]["x0"], d1["cols"][-1]["x1"]
pts = []
n = 12
for i in range(n):
    z = d1["wrist"].z + (d1["web_z"] - d1["wrist"].z) * i / (n - 1)
    sl = [c for c in V if abs(c.z - z) < 0.015 * d1["h"] and px0 <= c.x <= px1]
    if len(sl) >= 10:
        pts.append(Vector((sum(c.x for c in sl) / len(sl), sum(c.y for c in sl) / len(sl), z)))
if len(pts) >= 4:
    # least-squares slope of x(z), y(z)
    mz = sum(p.z for p in pts) / len(pts)
    mx = sum(p.x for p in pts) / len(pts)
    my = sum(p.y for p in pts) / len(pts)
    den = sum((p.z - mz) ** 2 for p in pts)
    sx = sum((p.z - mz) * (p.x - mx) for p in pts) / den
    sy = sum((p.z - mz) * (p.y - my) for p in pts) / den
    axis = Vector((sx, sy, 1.0)).normalized()
    rot = axis.rotation_difference(Vector((0.0, 0.0, 1.0))).to_matrix().to_4x4()
    lean_deg = math.degrees(math.acos(max(-1.0, min(1.0, axis.z))))
    log(f"de-lean: palm axis ({sx:+.3f},{sy:+.3f},1) -> rotating {lean_deg:.1f} deg onto +Z")
    wp = d1["wrist"]
    me.transform(Matrix.Translation(wp) @ rot @ Matrix.Translation(-wp))
    me.update()
else:
    log("de-lean: not enough palm slices — skipped")

# 4) PASS 2: full detection on the straightened mesh, then scale + origin at the wrist.
V = [v.co.copy() for v in me.vertices]
d2 = detect(V)
wc = d2["wrist"]
log(f"pass2: wrist {tuple(round(c, 4) for c in wc)} "
    f"(frac {(wc.z - d2['zmin']) / d2['h']:.3f}), webbing z {d2['web_z']:.4f}")

s = TARGET_LEN / (d2["zmax"] - wc.z)
xform = Matrix.Scale(s, 4) @ Matrix.Translation(-wc)
me.transform(xform)
me.update()
V = [v.co.copy() for v in me.vertices]
log(f"scaled by {s:.4f}; mesh spans z [{min(c.z for c in V):.4f}, {max(c.z for c in V):.4f}] "
    f"(cuff below 0 = along the forearm)")

# ---------------------------------------------------------------------------------------
# 5) joints in the final frame.
ztip = TARGET_LEN
colinfo = [{"x0": (xform @ Vector((c["x0"], 0, 0))).x,
            "x1": (xform @ Vector((c["x1"], 0, 0))).x,
            "xc": (xform @ Vector((c["xc"], 0, 0))).x} for c in d2["cols"]]
web_scaled = [(xform @ Vector((0, 0, w))).z for w in d2["web_zs"]]
band_z_scaled = (xform @ Vector((0, 0, d2["zmin"] + d2["h"] * (d2["band_frac"] - 0.01)))).z

FINGERS4 = ["Index", "Middle", "Ring", "Pinky"]  # ascending X in the left-hand frame
joints = {}
for i, fname in enumerate(FINGERS4):
    ci = colinfo[i]
    inband = [c for c in V if ci["x0"] - 0.003 <= c.x <= ci["x1"] + 0.003 and c.z > 0.40 * ztip]
    tip_z = max(c.z for c in inband)
    tip_v = [c for c in inband if c.z > tip_z - 0.008]
    tip = Vector((sum(c.x for c in tip_v) / len(tip_v),
                  sum(c.y for c in tip_v) / len(tip_v), tip_z - 0.002))
    # second sample at the detection band height -> finger AXIS, so the MCP is
    # extrapolated down splayed/curled fingers instead of assuming a vertical column
    bnd_v = [c for c in inband if abs(c.z - band_z_scaled) < 0.006]
    bnd = Vector((sum(c.x for c in bnd_v) / len(bnd_v),
                  sum(c.y for c in bnd_v) / len(bnd_v), band_z_scaled)) if bnd_v else tip
    adj = []
    if i - 1 >= 0 and i - 1 < len(web_scaled):
        adj.append(web_scaled[i - 1])
    if i < len(web_scaled):
        adj.append(web_scaled[i])
    mcp_z = min(0.97 * max(adj), 0.62 * tip_z)
    if (tip.z - bnd.z) > 0.005:
        t = (tip.z - mcp_z) / (tip.z - bnd.z)
        root = tip + (bnd - tip) * t          # linear extrapolation along the digit
        root.z = mcp_z
    else:
        root = Vector((ci["xc"], tip.y, mcp_z))
    mid = (root + tip) * 0.5
    joints[fname] = [list(root), list(mid), list(tip)]
    log(f"{fname:6s} root={tuple(round(v, 3) for v in root)} tip={tuple(round(v, 3) for v in tip)}")

# finger-tilt correction: the palm-axis de-lean can leave the FINGERS off vertical when
# the model's hand bends back at the knuckles (the arcane glove: ~9 deg). Rotate about X
# (through the wrist origin) so the middle finger's root->tip direction lies in the XZ
# plane, then recompute the finger joints in the corrected frame.
mid_root = Vector(joints["Middle"][0]); mid_tip = Vector(joints["Middle"][2])
f_ang = math.atan2(mid_tip.y - mid_root.y, mid_tip.z - mid_root.z)
if abs(f_ang) > math.radians(2.0):
    log(f"finger-tilt correction: rotating {math.degrees(f_ang):+.1f} deg about X")
    rx = Matrix.Rotation(-f_ang, 4, 'X')
    me.transform(rx)
    me.update()
    V = [v.co.copy() for v in me.vertices]
    for fname in list(joints.keys()):
        joints[fname] = [list(rx @ Vector(p)) for p in joints[fname]]
    for i, fname in enumerate(FINGERS4):
        r_, m_, t_ = (Vector(p) for p in joints[fname])
        log(f"{fname:6s} root={tuple(round(v, 3) for v in r_)} tip={tuple(round(v, 3) for v in t_)} (corrected)")

# thumb: the protrusion clearly beyond the palm edge on -X. Root/mid ON the thumb axis.
palm_x0 = colinfo[0]["x0"]
cand = [c for c in V if 0.02 * ztip <= c.z <= 0.70 * ztip and c.x < palm_x0 - 0.012]
if len(cand) < 20:
    raise SystemExit("thumb not found on -X — hand unusable for the contract rig")
ext = min(cand, key=lambda c: c.x)
near = [c for c in cand if (c - ext).length < 0.012]
t_tip = Vector((sum(c.x for c in near) / len(near), sum(c.y for c in near) / len(near),
                sum(c.z for c in near) / len(near)))
cand.sort(key=lambda c: c.x)
att_v = cand[-max(10, len(cand) // 5):]
t_att = Vector((sum(c.x for c in att_v) / len(att_v), sum(c.y for c in att_v) / len(att_v),
                sum(c.z for c in att_v) / len(att_v)))
t_root = t_att + (t_tip - t_att) * 0.30
t_mid = t_att + (t_tip - t_att) * 0.65
joints["Thumb"] = [list(t_root), list(t_mid), list(t_tip)]
log(f"Thumb  att={tuple(round(v, 3) for v in t_att)} root={tuple(round(v, 3) for v in t_root)} "
    f"tip={tuple(round(v, 3) for v in t_tip)}")

# anchors
mcp_zs = [joints[f][0][2] for f in FINGERS4]
palm_z = 0.55 * (sum(mcp_zs) / 4)
psl = [c for c in V if abs(c.z - palm_z) < 0.010 and colinfo[0]["x0"] <= c.x <= colinfo[-1]["x1"]]
pyc = sum(c.y for c in psl) / len(psl)
palm = [0.5 * (joints["Index"][0][0] + joints["Ring"][0][0]), pyc - 0.004, palm_z]
grab = [palm[0], palm[1] - 0.010, palm[2] + 0.010]
wsl = [c for c in V if abs(c.z - 0.006) < 0.010 and colinfo[0]["x0"] <= c.x <= colinfo[-1]["x1"]]
wrist = [sum(c.x for c in wsl) / len(wsl), sum(c.y for c in wsl) / len(wsl), 0.006]
indextip = list(Vector(joints["Index"][2]) + Vector((0, 0, 0.002)))

# ---------------------------------------------------------------------------------------
# 6) albedo: loose 2K PNG for BuildHands + a dark uniform CAP_UV texel for hole fills.
albedo_img = None
for mat in me.materials:
    if not mat or not mat.use_nodes:
        continue
    for n_ in mat.node_tree.nodes:
        if n_.type == 'BSDF_PRINCIPLED':
            bc = n_.inputs.get('Base Color')
            if bc and bc.is_linked:
                sn = bc.links[0].from_node
                if sn.type == 'TEX_IMAGE' and sn.image:
                    albedo_img = sn.image
if albedo_img is None:
    imgs = [im for im in bpy.data.images if im.size[0] > 0]
    albedo_img = max(imgs, key=lambda im: im.size[0] * im.size[1]) if imgs else None
if albedo_img is None:
    raise SystemExit("no albedo image in GLB")

cap_uv = (0.5, 0.5)
try:
    w_, h_ = albedo_img.size
    px = albedo_img.pixels[:]  # RGBA floats
    best = None
    for by in range(4, 60):
        for bx in range(4, 60):
            cx, cy = int(bx * w_ / 64), int(by * h_ / 64)
            vals = []
            for dy in (-4, 0, 4):
                for dx in (-4, 0, 4):
                    o = ((cy + dy) * w_ + (cx + dx)) * 4
                    vals.append((px[o], px[o + 1], px[o + 2]))
            mean = [sum(v[k] for v in vals) / 9 for k in range(3)]
            var = sum(sum((v[k] - mean[k]) ** 2 for k in range(3)) for v in vals) / 9
            lum = sum(mean) / 3
            if 0.02 < lum < 0.35 and var < 0.0004:
                score = var + lum * 0.001
                if best is None or score < best[0]:
                    best = (score, cx / w_, cy / h_)
    if best:
        cap_uv = (round(best[1], 4), round(best[2], 4))
except Exception as e:  # noqa
    log("CAP_UV scan failed:", e)
log(f"CAP_UV = {cap_uv}")

if albedo_img.size[0] > TEX_MAX:
    albedo_img.scale(TEX_MAX, TEX_MAX)
os.makedirs(BUNDLE_HANDS, exist_ok=True)
albedo_path = os.path.join(BUNDLE_HANDS, f"{CFG['asset']}_albedo.png")
albedo_img.filepath_raw = albedo_path
albedo_img.file_format = 'PNG'
albedo_img.save()
log("wrote albedo", albedo_path, tuple(albedo_img.size))

# shrink the other packed PBR textures too so the prepped GLB stays small
for im in bpy.data.images:
    if im.size[0] > TEX_MAX:
        im.scale(TEX_MAX, TEX_MAX)

# ---------------------------------------------------------------------------------------
# 7) write the joints JSON (committed; consumed by rig_hand.py via RIG_HAND_JOINTS).
data = {
    "name": NAME,
    "asset": CFG["asset"],
    "source": os.path.basename(SRC),
    "joints": joints,          # LEFT hand, metres: {finger: [[Root],[Mid],[Tip]]}
    "wrist": wrist,
    "palm": palm,
    "grab": grab,
    "indextip": indextip,
    "cap_uv": list(cap_uv),
    "wrist_mode": "protect",   # rig_hand: exclude the wrist band from hole fills
}
with open(JSON_OUT, "w") as f:
    json.dump(data, f, indent=2)
log("wrote", JSON_OUT)

# 8) export the prepped GLB.
os.makedirs(GLB_OUT_DIR, exist_ok=True)
glb_path = os.path.join(GLB_OUT_DIR, f"{NAME}_prepped.glb")
bpy.ops.object.select_all(action='DESELECT')
obj.select_set(True)
bpy.context.view_layer.objects.active = obj
bpy.ops.export_scene.gltf(filepath=glb_path, use_selection=True, export_format='GLB')
log("wrote", glb_path, f"({os.path.getsize(glb_path)} bytes)")

# ---------------------------------------------------------------------------------------
# 9) optional verification renders with joint markers.
if DO_RENDER:
    os.makedirs(RENDER_DIR, exist_ok=True)
    mk = bpy.data.materials.new("mk"); mk.use_nodes = True
    bsdf = mk.node_tree.nodes["Principled BSDF"]
    bsdf.inputs["Base Color"].default_value = (1, 0.1, 0.1, 1)
    bsdf.inputs["Emission Color"].default_value = (1, 0.1, 0.1, 1)
    bsdf.inputs["Emission Strength"].default_value = 2.0
    for fname, jpts in joints.items():
        for p in jpts:
            bpy.ops.mesh.primitive_uv_sphere_add(radius=0.003, location=p)
            bpy.context.active_object.data.materials.append(mk)
    for p in (wrist, palm, grab):
        bpy.ops.mesh.primitive_uv_sphere_add(radius=0.004, location=p)
        bpy.context.active_object.data.materials.append(mk)

    world = bpy.data.worlds.new("W"); world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[1].default_value = 1.2
    scn = bpy.context.scene
    scn.world = world
    scn.render.engine = 'BLENDER_EEVEE_NEXT'
    scn.render.resolution_x = 640; scn.render.resolution_y = 640
    cam_data = bpy.data.cameras.new("Cam"); cam_data.lens = 50
    cam = bpy.data.objects.new("Cam", cam_data)
    scn.collection.objects.link(cam); scn.camera = cam
    sun_d = bpy.data.lights.new("Sun", 'SUN'); sun_d.energy = 3.0
    sun = bpy.data.objects.new("Sun", sun_d); scn.collection.objects.link(sun)

    ctr = Vector((0, 0, 0.03))
    d = 0.75

    def look_at(o, frm, to):
        o.rotation_euler = (to - frm).to_track_quat('-Z', 'Y').to_euler()

    for shot, off in {"back": Vector((0, d, 0.1)), "palm": Vector((0, -d, 0.1)),
                      "threeq": Vector((-d * 0.7, d * 0.7, d * 0.4))}.items():
        pos = ctr + off
        cam.location = pos; look_at(cam, pos, ctr)
        sun.location = pos; look_at(sun, pos, ctr)
        scn.render.filepath = os.path.join(RENDER_DIR, f"{NAME}_prepped_{shot}.png")
        bpy.ops.render.render(write_still=True)
        log("wrote render", scn.render.filepath)
