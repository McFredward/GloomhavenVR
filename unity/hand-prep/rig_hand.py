# rig_hand.py — Blender headless rig of the AI-generated left-hand glove mesh into a
# bundle-ready SKINNED FBX pair (left + mirrored right) for GloomhavenVR's FingerCurler.
#
# LICENSE-FREE Blender-only step. Does NOT touch Unity.
#
# Mirrors the unity/board-prep/prepare_playtray.py convention: one reproducible headless
# script, run with Blender 4.2. It turns ressources/hands/Hand_prepped.glb (root at wrist,
# +Z along fingers, +Y = back of hand, palm at -Y, ~0.19 m, ~18k tris) into a 16-bone
# armature (Anchor_Wrist + 5 fingers x 3 joints) plus three anchor empties (Anchor_Palm,
# Anchor_IndexTip, Anchor_Grab), skins it with EXPLICIT GEOMETRIC digit weights (see the
# GEO_SKIN block below; the detected fingertip landmarks are first converted to
# anatomical DIP joints — see DIP_ENABLE), verifies the flexion invariant numerically,
# then mirrors to a right hand and exports both FBX files.
#
# RUN (headless):
#   /home/claw/blender-4.2/blender --background --python rig_hand.py
#
# Outputs:
#   unity/GloomhavenVR.Assets/Assets/Bundle/Hands/VRHand_L_rig.fbx
#   unity/GloomhavenVR.Assets/Assets/Bundle/Hands/VRHand_R_rig.fbx
#
# ---------------------------------------------------------------------------------------
# RIG CONTRACT (frozen in src/GloomhavenVR/Hands/HandVisuals.cs + .../HandRig.cs +
# Assets/Bundle/Hands/README.md):
#   - The mod finds transforms by name (FindDeep) and, per frame, sets
#         joint.localRotation = base * Quaternion.Euler(maxAngle*curl, 0, 0)
#     on each of a finger's 3 joints. So every finger/thumb joint's LOCAL +X must be the
#     flexion axis, and POSITIVE local-X rotation must curl the fingertip INTO the palm.
#   - Here +Z = fingers, +Y = back of hand  =>  palm faces world -Y  =>  "curl into palm"
#     means the fingertip must move toward -Y under +X rotation.
#   - Anchor_Palm / Anchor_Grab: local +Y points OUT OF THE PALM (world -Y here), +Z along
#     fingers. (HandRig.PalmNormal = PalmCenter.up; CardsConfig documents GrabAnchor-local
#     "+Y out of the palm, +Z along the fingers". The mod's own fallback builds the palm
#     anchor with Euler(0,0,180). NOTE: the task brief said "+Y out of the BACK"; that
#     contradicts the shipping code, which is authoritative for making the mod work, so we
#     follow the code. See hand-rig-report.md.)
#
# FLEXION-AXIS MATH (why align_roll(-Y) is exact):
#   For a joint at position p with child direction d (unit, head->tail) lying in the world
#   X-Z plane (all our finger/thumb bones do, Y component ~0), rotating the tip (offset r||d)
#   about a unit axis a by small angle gives delta ~ theta*(a x r). We want delta || -Y, i.e.
#   a x d || -Y. With a = normalize(d x -Y) one gets a x d = -Y exactly, and the induced
#   local Z = a x d = -Y for EVERY such bone. Blender's EditBone.align_roll(v) sets roll so
#   the bone's local +Z aims at v; align_roll(-Y) therefore makes local +X the exact flexion
#   axis with +X curling toward -Y. Verified numerically by the pose test below.
# ---------------------------------------------------------------------------------------

import bpy, bmesh, json, math, os, sys
from mathutils import Vector, Matrix
from mathutils.kdtree import KDTree

# ---- GENERALIZATION (alternative hand styles) -----------------------------------------
# The DEFAULT invocation (no env) builds the original glove: same source, same hardcoded
# joints, same output names. (The former "byte-for-byte legacy weights" invariant was
# RETIRED 2026-07 by the fist fix — the glove now gets the same geometric skinning + DIP
# joints as the styled hands; RIG_HAND_GEO_SKIN=0 / RIG_HAND_DIP=0 reproduce the old
# build for A/B only.) Alternative hands (prepared by prepare_hand.py) are rigged by
# pointing these env vars at their artifacts:
#   RIG_HAND_SRC     input prepped GLB     (default: the original Hand_prepped.glb)
#   RIG_HAND_NAME    asset base name       (default "VRHand" -> VRHand_L_rig.fbx / _R_;
#                    e.g. "VRHandPlate" -> VRHandPlate_L_rig.fbx / VRHandPlate_R_rig.fbx)
#   RIG_HAND_JOINTS  path to a <model>_joints.json from prepare_hand.py — replaces the
#                    hardcoded joint/anchor constants + CAP_UV and switches the watertight
#                    pass's wrist handling to the JSON's "wrist_mode"
#   RIG_HAND_EMBED   "0" -> do not embed textures in the FBX (path_mode STRIP; the loose
#                    albedo PNG is what Unity binds anyway). Default "1" (original glove).
#   RIG_HAND_RENDER_DIR  when set, write offscreen verification renders (rest + fist)
#   RIG_HAND_POSES   "1" -> render the FULL runtime pose matrix instead of rest+fist:
#                    open / half / fist / thumbtuck / point / per-finger curls, driven
#                    with the EXACT FingerCurler convention (per-joint max angles
#                    75/95/65 deg for fingers, 25/45/60 for the thumb, local +X)
#                    so the renders show what the mod will actually display.
#   RIG_HAND_POSE_ONLY  comma-separated subset of the pose matrix (e.g. "fist,point")
SRC = os.environ.get("RIG_HAND_SRC",
                     "/home/claw/gloomhaven_vr/ressources/hands/Hand_prepped.glb")
NAME = os.environ.get("RIG_HAND_NAME", "VRHand")
JOINTS_JSON = os.environ.get("RIG_HAND_JOINTS", "")
EMBED_TEX = os.environ.get("RIG_HAND_EMBED", "1") != "0"
RENDER_DIR = os.environ.get("RIG_HAND_RENDER_DIR", "")
# OUT_DIR: overridable via env so this can target an isolated worktree without
# touching the main checkout. Default remains the main checkout (unchanged behaviour).
OUT_DIR = os.environ.get(
    "RIG_HAND_OUT_DIR",
    "/home/claw/gloomhaven_vr/unity/GloomhavenVR.Assets/Assets/Bundle/Hands")
POSE_MATRIX = os.environ.get("RIG_HAND_POSES", "0") != "0"
# Preview of the RUNTIME per-style curl clamp (FingerCurler.StyleCurlScale): scales every
# pose's joint angles, so the after-renders show exactly what the mod displays in-game.
CURL_SCALE = float(os.environ.get("RIG_HAND_CURL_SCALE", "1.0"))
NEG_Y = Vector((0.0, -1.0, 0.0))  # palm-out / flexion reference

FINGERS = ["Thumb", "Index", "Middle", "Ring", "Pinky"]

# Runtime curl convention (src/GloomhavenVR/Hands/FingerCurler.cs): per-joint FULL-curl
# angles in degrees around local +X, scaled by the 0..1 curl value.
#   fingers: root 75, mid 95, tip 65      thumb: root 25, mid 45, tip 60
# (2026-07 fist fix: raised from 65/80/50 — even at curl 1.0 the old angles read as a
# visibly open fist on hardware; keep in sync with FingerCurler.DefaultFingerMaxAngles.)
CURL_MAX_FINGER = (75.0, 95.0, 65.0)
CURL_MAX_THUMB = (25.0, 45.0, 60.0)

# RIG_HAND_POSE_ONLY: comma-separated subset of POSES to render (with RIG_HAND_POSES=1),
# e.g. "fist" or "fist,point" — keeps targeted verification runs fast. Empty = all.
POSE_ONLY = [p for p in os.environ.get("RIG_HAND_POSE_ONLY", "").split(",") if p]

# ---- FIST BUG, FINAL ROUND (2026-07): explicit geometric digit skinning ----------------
# Hardware evidence (LogOutput 2026-07-23): "FIST Right (style Glove …) curl 1.00 on all
# fingers, applied° 75/95/65, externalDrift 0.0" — the bones get the FULL commanded
# rotation, yet on-device the mesh barely bends ("only fingertips move"). Input was never
# the problem; the SKIN WEIGHTS are. Every previous weight fix (rounds 1-3 above) was
# gated ALT_HAND-only, so the GLOVE — the style the log proves the player uses — still
# shipped the round-0 proximity blend: 1/d^2 over {wrist-segment, root, mid, tip} plus
# 3 passes of 0.5-alpha KD smoothing. Measured on that build (RIG_HAND_DIAG=1): the
# PROXIMAL tube of each finger only follows ~55-70 % of a rigid Root rotation and the
# palm-side knuckle verts sit closer to the long wrist segment than to their own Root
# bone, so a commanded 75° MCP flexion renders as a shallow lean — a fist that never
# closes even though every joint angle is proven applied.
#
# Fix (RIG_HAND_GEO_SKIN, default ON — applies to ALL styles including the glove):
# digit-tube verts are no longer weighted by inverse-distance mixing at all. Each vert
# inside its nearest finger's tube (off-axis distance < the adaptive per-finger cap) is
# assigned by ARC LENGTH s along the 3-bone polyline (projection):
#   - pure zones get 100 % of their segment's bone (Root / Mid / Tip);
#   - joint boundaries get a smooth 2-bone smoothstep blend across ±15 % of the
#     shorter adjacent segment length (MCP boundary: wider ±25 % of the proximal
#     length, blending Wrist<->Root so the knuckle bulge creases at the joint);
#   - the off-axis smoothstep fade (soft..cap) still moves weight to the wrist so the
#     tube boundary has no seam. Palm/back/webbing verts (outside every tube) are 100 %
#     wrist-bound — the batwing-membrane and palm-fan fixes stay by construction.
# Weights are a C1-continuous function of POSITION only, so the 310 disconnected shells
# can never tear apart and no KD smoothing is needed (it is what diluted Root/Mid
# ownership in the first place). RIG_HAND_GEO_SKIN=0 reproduces the legacy weighting
# (kept for A/B diagnostics only — do NOT ship it).
GEO_SKIN = os.environ.get("RIG_HAND_GEO_SKIN", "1") != "0"
MCP_BLEND = 0.25   # wrist<->root blend halfwidth, fraction of proximal segment length
IPJ_BLEND = 0.15   # inter-phalangeal blend halfwidth, fraction of shorter neighbour seg

# RIG_HAND_DIAG=1: diagnostics-only run — build the LEFT hand, print per-finger/zone
# weight stats + the rigid-follow test (does the MESH actually track each bone's
# rotation?), render weight heatmaps into RENDER_DIR, write <NAME>_weights.json, and
# EXIT without exporting FBX. The metric the earlier "closure" checks were blind to.
DIAG = os.environ.get("RIG_HAND_DIAG", "0") != "0"

# ---- CHAIN REFIT (2026-07 hardware round: "Arcane/Plate still only curl at the tips",
# "glove pinky splays outward in a fist") --------------------------------------------------
# Both bugs turned out to be JOINT-CHAIN placement, not weighting:
#   - prepare_hand.py's webbing detection is polluted on the armored Hunyuan meshes (finger
#     side-bulge verts fall into the inter-column gap window), so the styled MCPs landed at
#     the 0.62*tip_z clamp — MID-FINGER (Plate index MCP z=0.109 vs true valley z=0.080/
#     ring-pinky 0.054, measured). Everything below the MCP projects to s < -h0 and is
#     wrist-rigid: heatmap-verified "gray proximal half", i.e. only the fingertips curled.
#   - the glove pinky MESH tube points ~18 deg outward in XZ (PCA of the tube verts:
#     (+0.30, +0.25, +0.92)) while its hardcoded chain is dead straight (0,0,1). The curl
#     axis derived from the CHAIN (world +X) is then not perpendicular to the finger's real
#     plane, so a full curl drives the tip straight down parallel to the middle finger while
#     the shaft points outward — the reported unnatural outward splay. (Glove index is
#     likewise ~16 deg off, toward the thumb.)
# Fix: refit every finger chain FROM THE MESH before building the armature:
#   1. per finger, PCA-fit the digit tube axis (verts nearest the seed chain, distal 2/3,
#      off-axis < 3 cm; thumb 4 cm) -> centroid + direction; fingertip = 98th-pct projection;
#   2. finger MCPs: scan the inter-finger VALLEYS on the fitted axes (lowest 8 mm z-slab
#      whose column window still has a >=6 mm empty x-gap) and drop the MCP a fixed
#      MCP_DROP below the valley (calibrated so the glove's proven MCPs reproduce);
#   3. thumb MCP: walk DOWN the fitted thumb axis until the slab's r90 off-axis radius
#      blows past the mid-thumb reference (the tube merging into the palm mass).
# The bones then follow the real digits, so align_roll(-Y) automatically yields a curl
# axis perpendicular to each finger's true plane (splay fix) and the proximal phalanx
# starts at the true knuckle (fingertip-only-curl fix). RIG_HAND_REFIT=0 for A/B.
REFIT = os.environ.get("RIG_HAND_REFIT", "1") != "0"
MCP_DROP = float(os.environ.get("RIG_HAND_MCP_DROP", "0.030"))  # valley -> MCP z drop (m)
VALLEY_SLAB = 0.008     # z-slab thickness for the valley scan (m)
VALLEY_GAP = 0.006      # required empty x-gap between adjacent digit columns (m)
MCP_MIN_Z = 0.018       # never place a finger MCP below this (m above the wrist crease)

# ---- DIP relocation (found by the same diagnostics, all styles) ------------------------
# The 3rd landmark of every finger is the FINGERTIP ("Tip(fingertip/DIP)" in the
# JOINTS_L comment; prepare_hand.py detects the same for the styled hands — all chains
# are perfectly evenly spaced MCP/PIP/fingertip). So the exported Anchor_*_Tip bone
# STARTED at the fingertip and owned ~2 mm of mesh (weight stats: tip zone n=0 on every
# finger of every style): the runtime's 65° tip-joint rotation was cosmetically dead and
# each finger really articulated on TWO hinges — one big reason the "fist" never wrapped.
# Fix: move the Tip joint back to an anatomical DIP/IP (fingers: 60 % of PIP->fingertip,
# i.e. middle:distal phalanx ≈ 1.5:1; thumb IP: 55 %) and give the Tip bone the real
# distal phalanx, ending just past the fingertip. Anchor_IndexTip is positioned from the
# separate INDEXTIP landmark and is unaffected. RIG_HAND_DIP=0 restores the old chains.
DIP_ENABLE = os.environ.get("RIG_HAND_DIP", "1") != "0"
DIP_FRAC_FINGER = 0.60
DIP_FRAC_THUMB = 0.55
TIP_TAIL_PAD = 0.004     # tip-bone tail beyond the fingertip (m)


def derive_dip(joints):
    """Rewrite each finger chain [MCP, PIP, fingertip] -> [MCP, PIP, DIP] and return
    (joints, tipends) where tipends[f] is the tip-bone TAIL (just past the fingertip)."""
    out, tipends = {}, {}
    for f in FINGERS:
        p0, p1, tip = joints[f]
        dirt = (tip - p1).normalized()
        if DIP_ENABLE:
            frac = DIP_FRAC_THUMB if f == "Thumb" else DIP_FRAC_FINGER
            dip = p1 + (tip - p1) * frac
            tipends[f] = tip + dirt * TIP_TAIL_PAD
        else:
            dip = tip.copy()
            tipends[f] = tip + dirt * TIP_TAIL_LEN
        out[f] = [p0.copy(), p1.copy(), dip]
    return out, tipends

# Pose matrix rendered by RIG_HAND_POSES=1 — curl value per finger, mirroring what
# VRHand.UpdateCurlTargets actually produces on hardware:
#   open      controller untouched, curls 0
#   half      trigger+grip half pulled
#   fist      full grip+trigger (the pose test's worst case)
#   thumbtuck idle rest pose: thumb on the stick (0.65), light 0.15 rest curl
#   point     grip held, index extended (UI pointing — the pose players stare at)
#   curl_*    one finger alone at full curl (isolates per-finger weight bleed)
POSES = {
    "open":      {"Thumb": 0.0, "Index": 0.0, "Middle": 0.0, "Ring": 0.0, "Pinky": 0.0},
    "half":      {"Thumb": 0.5, "Index": 0.5, "Middle": 0.5, "Ring": 0.5, "Pinky": 0.5},
    "fist":      {"Thumb": 1.0, "Index": 1.0, "Middle": 1.0, "Ring": 1.0, "Pinky": 1.0},
    "thumbtuck": {"Thumb": 0.65, "Index": 0.15, "Middle": 0.15, "Ring": 0.15, "Pinky": 0.15},
    "point":     {"Thumb": 0.65, "Index": 0.0, "Middle": 1.0, "Ring": 1.0, "Pinky": 1.0},
    "curl_index":  {"Thumb": 0.1, "Index": 1.0, "Middle": 0.1, "Ring": 0.1, "Pinky": 0.1},
    "curl_middle": {"Thumb": 0.1, "Index": 0.1, "Middle": 1.0, "Ring": 0.1, "Pinky": 0.1},
    "curl_ring":   {"Thumb": 0.1, "Index": 0.1, "Middle": 0.1, "Ring": 1.0, "Pinky": 0.1},
    "curl_pinky":  {"Thumb": 0.1, "Index": 0.1, "Middle": 0.1, "Ring": 0.1, "Pinky": 1.0},
    # splay/abduction QA (future hand-tracking: rigs must tolerate ARBITRARY poses):
    # +/-15 deg on every PROXIMAL bone about its local Z — the axis perpendicular to the
    # curl axis and the bone, i.e. pure abduction. Verifies the skinning doesn't tear
    # when fingers spread/adduct and that every chain has a consistent abduction axis.
    "splay_p":   {"_splay": 15.0},
    "splay_m":   {"_splay": -15.0},
}


def apply_pose(arm, curls):
    """Pose the armature exactly like the runtime FingerCurler (local-X, per-joint max).

    Optional "_splay" key: degrees of abduction applied to every Root bone about its
    local Z (the axis orthogonal to both the bone and its curl axis)."""
    splay = curls.get("_splay", 0.0)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='POSE')
    for pb in arm.pose.bones:
        pb.rotation_mode = 'XYZ'
        pb.rotation_euler = (0.0, 0.0, 0.0)
    for f in FINGERS:
        maxes = CURL_MAX_THUMB if f == "Thumb" else CURL_MAX_FINGER
        c = curls.get(f, 0.0)
        for i, seg in enumerate(("Root", "Mid", "Tip")):
            rx = math.radians(maxes[i] * c * CURL_SCALE)
            rz = math.radians(splay) if seg == "Root" else 0.0
            arm.pose.bones[f"Anchor_{f}_{seg}"].rotation_euler = (rx, 0.0, rz)
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.context.view_layer.update()


def clear_pose(arm):
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='POSE')
    for pb in arm.pose.bones:
        pb.rotation_euler = (0.0, 0.0, 0.0)
    bpy.ops.object.mode_set(mode='OBJECT')
    bpy.context.view_layer.update()

# Joint positions (metres, LEFT hand, in the imported GLB's world space:
# +Z fingers, +Y back of hand, palm at -Y). Estimated from mesh clustering
# (see clusters*.py analysis). Right hand mirrors X.
#   each finger: [Root(MCP), Mid(PIP), Tip(fingertip/DIP)]
JOINTS_L = {
    "Thumb":  [Vector((-0.022, -0.004, 0.050)), Vector((-0.043, 0.000, 0.071)), Vector((-0.064, 0.002, 0.092))],
    "Index":  [Vector((-0.013,  0.012, 0.100)), Vector((-0.013, 0.012, 0.138)), Vector((-0.013, 0.012, 0.176))],
    "Middle": [Vector(( 0.008,  0.015, 0.102)), Vector(( 0.008, 0.015, 0.146)), Vector(( 0.008, 0.015, 0.190))],
    "Ring":   [Vector(( 0.030,  0.008, 0.098)), Vector(( 0.030, 0.008, 0.138)), Vector(( 0.030, 0.008, 0.179))],
    "Pinky":  [Vector(( 0.061,  0.004, 0.092)), Vector(( 0.061, 0.004, 0.123)), Vector(( 0.061, 0.004, 0.155))],
}
WRIST_L      = Vector((0.018, -0.002, 0.006))   # wrist-base centre
PALM_L       = Vector((0.010, -0.005, 0.055))   # palm centre
GRAB_L       = Vector((0.008, -0.015, 0.065))   # grip point (fingers close against palm)
INDEXTIP_L   = Vector((-0.013, 0.012, 0.178))   # index fingertip point anchor
TIP_TAIL_LEN = 0.014                            # leaf-bone (Tip) tail length along finger dir

# Wrist handling in the watertight pass: 'require_open' (original glove — the ragged rim
# must stay open, hard-fail if a fill seals it) or 'protect' (alternative hands — wrist-
# band boundary edges are EXCLUDED from hole fills; a naturally closed cuff is fine).
WRIST_MODE = "require_open"

# RIG_HAND_JOINTS: replace the hardcoded landmark constants with prepare_hand.py's
# per-model detection output. CAP_UV is overridden further below (defined later).
_JSON_CAP_UV = None
if JOINTS_JSON:
    with open(JOINTS_JSON) as _f:
        _jd = json.load(_f)
    JOINTS_L = {f: [Vector(p) for p in _jd["joints"][f]] for f in FINGERS}
    WRIST_L = Vector(_jd["wrist"])
    PALM_L = Vector(_jd["palm"])
    GRAB_L = Vector(_jd["grab"])
    INDEXTIP_L = Vector(_jd["indextip"])
    WRIST_MODE = _jd.get("wrist_mode", "protect")
    _JSON_CAP_UV = tuple(_jd.get("cap_uv", (0.5, 0.5)))
    print(f"[rig_hand] joints loaded from {JOINTS_JSON} (asset {NAME}, wrist_mode {WRIST_MODE})")

# ALTERNATIVE-HAND-ONLY fixes (pose-matrix QA, 2026-07): the region-aware finger-weight
# cap in skin() and the flat-shaded hole fills in _fix_new_face_uvs() repair the Plate/
# Arcane curl distortions (palm fan sheet, thumb-index batwing membrane, starburst palm
# cap). They are GATED to JSON-driven builds so the DEFAULT no-env invocation still
# reproduces the original glove FBX byte-for-byte (documented invariant above).
ALT_HAND = bool(JOINTS_JSON)

# ---- PRIORITY-3: watertight inner "backing core" (OPT-IN, OFF by default) -------------
# The AI outer shell is 310 non-manifold shells with small see-through gaps (worst on the
# curled finger tips). This builds a SECOND skinned mesh: a voxel-remeshed (=> watertight,
# manifold) copy of the hand, shrunk a few mm INSIDE the outer shell and skinned to the same
# armature. Unity gives it a dark leather material (see BuildHands.cs) so a residual outer
# hole reveals the dark core instead of the background.
#
# STATUS: DISABLED by default (RIG_HAND_CORE=1 to enable). The AI mesh carries a lot of
# internal/overlapping non-manifold geometry, so its voxel remesh is lumpy and — at the small
# inset needed to still back the THIN finger-tip holes — pokes back OUT through the thin outer
# shell in places (finger tips, wrist strap), speckling the *relaxed* hand with dark spots
# (a net regression on the pose the player sees most). A larger inset removes the poke-through
# but then no longer backs the thin tips. Shipping state is the clean two-sided (Cull Off)
# hand; the residual see-through is minor tip speckling at VR arm's length. Kept here, gated,
# as a starting point for a better fit (shrinkwrap-constrained or smoothed/per-region core).
CORE_ENABLE = os.environ.get("RIG_HAND_CORE", "0") != "0"
CORE_VOXEL = float(os.environ.get("RIG_HAND_CORE_VOXEL", "0.003"))  # voxel size (m)
CORE_INSET = float(os.environ.get("RIG_HAND_CORE_INSET", "0.0035")) # shrink along normals (m)


def log(*a):
    print("[rig_hand]", *a)


def mirror_x(v):
    return Vector((-v.x, v.y, v.z))


# ---------------------------------------------------------------------------------------
def import_mesh():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=SRC)
    ms = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if not ms:
        raise SystemExit("no mesh in GLB")
    bpy.ops.object.select_all(action='DESELECT')
    for o in ms:
        o.select_set(True)
    bpy.context.view_layer.objects.active = ms[0]
    if len(ms) > 1:
        bpy.ops.object.join()
    o = bpy.context.view_layer.objects.active
    # Bake any import transform into the mesh so object space == world space.
    if o.matrix_world != Matrix.Identity(4):
        o.data.transform(o.matrix_world)   # direct mesh transform (headless-safe)
        o.matrix_world = Matrix.Identity(4)
    return o


def mirror_mesh_x(o):
    """Produce right-hand geometry: negate X, flip normals so winding stays outward."""
    o.data.transform(Matrix.Diagonal((-1.0, 1.0, 1.0, 1.0)))
    bm = bmesh.new()
    bm.from_mesh(o.data)
    for f in bm.faces:
        f.normal_flip()
    bm.to_mesh(o.data)
    bm.free()
    o.data.update()


# ---------------------------------------------------------------------------------------
# WATERTIGHT PASS — close the AI shell's see-through holes without wrecking the silhouette.
#
# The raw glove is 310 disconnected shells / 16 074 non-manifold (all boundary) edges, so at
# MR-passthrough the bright background shows through the gaps between shells and through the
# cracked finger tubes ("holes in a hollow finger"). The earlier voxel "backing core" (below,
# still gated OFF) was too lumpy and poked back out through the thin outer shell.
#
# This instead repairs the OUTER shell in place, which preserves the exact silhouette, the UVs
# and (because our skinning is purely proximity-on-position) the whole rig contract:
#   1. one global weld at 0.5 mm — fuses the coincident seams BETWEEN the 310 shells into a
#      single connected surface (verts 17 251 -> ~8 100, shells 310 -> 1);
#   2. a few close passes, each: holes_fill (caps every clean boundary loop) + a BOUNDARY-ONLY
#      weld at 1.8 mm (pulls the ragged, non-loop hole rims — which holes_fill cannot cap —
#      together so the next fill closes them) + a second boundary holes_fill. Welding only the
#      boundary verts leaves the interior detail untouched, so fingers/knuckles keep their shape;
#   3. recalc_face_normals for consistent outward winding (belt-and-suspenders with the Cull Off
#      two-sided glove material).
# The WRIST STUMP is deliberately left OPEN (no cap): the two-sided glove material shows the
# lit interior shell when looking in from behind — a hollow glove, not a plugged disc.
# Result (offscreen green-background render, 5 POVs): boundary edges 16 074 -> ~90, enclosed
# "see-through" pixels 51 -> ~4 (all on the back-of-hand crinkle; none on the palm/relaxed view),
# silhouette coverage unchanged. The exported, skinned FBX renders 0 see-through px at REST from
# every POV (the relaxed, most-seen pose); the only residual is small joint-gap tearing under a
# heavy fist. Tunable via RIG_HAND_WT_* env; set RIG_HAND_WATERTIGHT=0 to disable.
WT_MERGE = float(os.environ.get("RIG_HAND_WT_MERGE", "0.0005"))   # global seam weld (m)
WT_BWELD = float(os.environ.get("RIG_HAND_WT_BWELD", "0.0018"))   # boundary-only gap weld (m)
WT_PASSES = int(os.environ.get("RIG_HAND_WT_PASSES", "3"))
WT_ENABLE = os.environ.get("RIG_HAND_WATERTIGHT", "1") != "0"     # ON by default

# UV fallback for freshly created hole-fill faces. BMesh gives NEW faces zeroed loop UVs,
# so filled holes would sample texel (0,0) of the albedo atlas — which is pure BLACK.
# Small hole fills INHERIT the UV of an existing loop on the same vertex (they blend into
# the surrounding texture); a corner with no prior loop falls back to this texel, chosen by
# scanning VRHand_albedo.png for a dark, uniform 32 px block:
# px(752,1968) of 2048², mean RGB (74,56,37), std < 1.
# For alternative hands the equivalent texel is scanned per model by prepare_hand.py and
# arrives via the joints JSON.
CAP_UV = (0.3672, 0.0391)
if _JSON_CAP_UV is not None:
    CAP_UV = _JSON_CAP_UV


def _fix_new_face_uvs(bm, new_faces):
    """Give the zero-UV loops of freshly created faces sensible texture coords.

    Each corner copies the UV of any PRE-EXISTING loop on the same vertex (hole fills
    disappear into the surrounding texture); corners with no prior loop fall back to
    CAP_UV (a flat dark-leather texel).
    Returns the number of loops written."""
    uv = bm.loops.layers.uv.active
    if uv is None or not new_faces:
        return 0
    new_set = set(new_faces)
    fixed = 0
    for f in new_faces:
        if not f.is_valid:
            continue
        # Flat-shade the fill (ALT hands only — the glove build stays byte-identical):
        # a large cap (the palm disc of the alternative hands) otherwise smooth-blends
        # its fan normals with the surrounding shell and renders as an ugly radial
        # STARBURST gradient (render-verified on the 9-pose matrix). A flat facet reads
        # as an intentional armor/leather plate instead.
        if ALT_HAND:
            f.smooth = False
        for loop in f.loops:
            src = None
            for other in loop.vert.link_loops:
                if other.face not in new_set:
                    src = other[uv].uv.copy()
                    break
            loop[uv].uv = src if src is not None else CAP_UV
            fixed += 1
    return fixed


def make_watertight(o):
    """Weld the fragmented AI shell into one connected, hole-closed surface (in place)."""
    me = o.data

    def _nonman(tag):
        bm = bmesh.new(); bm.from_mesh(me)
        nb = sum(1 for e in bm.edges if e.is_boundary)
        bm.free()
        return nb

    before = _nonman("before")
    # Wrist-band protection ('protect' mode, alternative hands): the cuff rim of a
    # prepped Hunyuan hand can be a CLEAN boundary loop, which holes_fill would plug
    # with an ugly pie-disc. Exclude everything in the bottom band from fills/welds.
    zmin_all = min(v.co.z for v in me.vertices)
    band_top = zmin_all + 0.02
    protect = WRIST_MODE == "protect"

    def _in_band(v):
        return v.co.z < band_top

    # 1) global weld of coincident shell seams
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=WT_MERGE)
    bm.to_mesh(me); bm.free(); me.update()

    # 2) iterative boundary-close passes. Every face holes_fill creates has ZEROED loop
    #    UVs (would sample the atlas's black (0,0) texel) — inherit surrounding UVs
    #    immediately, inside the same bmesh session, while the new-face refs are valid.
    uv_fixed = 0
    for _ in range(WT_PASSES):
        bm = bmesh.new(); bm.from_mesh(me)
        edges1 = bm.edges if not protect else \
            [e for e in bm.edges if not (_in_band(e.verts[0]) and _in_band(e.verts[1]))]
        res = bmesh.ops.holes_fill(bm, edges=edges1, sides=0)
        uv_fixed += _fix_new_face_uvs(bm, res.get("faces", []))
        bnd = [v for v in bm.verts if any(e.is_boundary for e in v.link_edges)
               and not (protect and _in_band(v))]
        if bnd:
            bmesh.ops.remove_doubles(bm, verts=bnd, dist=WT_BWELD)
        edges2 = [e for e in bm.edges if e.is_boundary
                  and not (protect and _in_band(e.verts[0]) and _in_band(e.verts[1]))]
        res = bmesh.ops.holes_fill(bm, edges=edges2, sides=0)
        uv_fixed += _fix_new_face_uvs(bm, res.get("faces", []))
        bm.to_mesh(me); bm.free(); me.update()

    # 3) the OPEN WRIST STUMP stays OPEN — no cap. A fan/tri cap here (tried in earlier
    #    revisions) always reads as a flat "pie-chart" disc plugging the glove, which is
    #    exactly what the player sees when the hand curls. Instead we rely on the glove
    #    material being two-sided (Cull Off + VFACE normal flip in BoardLit.shader): looking
    #    into the stump from behind shows the LIT INTERIOR of the glove shell, sampling the
    #    same albedo texels as the outside — a genuine hollow glove. The rim is a ragged
    #    near-loop (NOT a clean loop), so step 2's holes_fill cannot cap it; the guard below
    #    verifies that stays true (if a future weld tweak turned the rim into a clean loop,
    #    holes_fill would silently plug it again).
    bm = bmesh.new(); bm.from_mesh(me)
    zmin = min(v.co.z for v in bm.verts)
    band = zmin + 0.020
    wrist_open = sum(1 for e in bm.edges
                     if e.is_boundary and e.verts[0].co.z < band and e.verts[1].co.z < band)
    bm.free()
    if wrist_open == 0 and not protect:
        raise SystemExit("watertight: wrist rim got sealed by holes_fill — it must stay open "
                         "(no cap); loosen WT_BWELD or exclude the wrist band from step 2")
    if protect:
        log(f"watertight: wrist band protected from fills — {wrist_open} boundary edges remain "
            f"in the 2 cm min-Z band ({'open rim' if wrist_open else 'naturally closed cuff'})")
    else:
        log(f"watertight: wrist rim left OPEN ({wrist_open} boundary edges in the 2 cm min-Z band; "
            f"interior visible via two-sided material)")
    log(f"watertight: assigned real UVs to {uv_fixed} loops of filled faces "
        f"(new BMesh faces default to UV (0,0) — a BLACK texel in this atlas)")

    # 4) consistent outward normals
    bm = bmesh.new(); bm.from_mesh(me)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free(); me.update()

    after = _nonman("after")
    log(f"watertight: boundary edges {before} -> {after}, verts now {len(me.vertices)}")


# ---------------------------------------------------------------------------------------
def build_armature(joints, wrist, palm, grab, indextip, name, tipends):
    arm_data = bpy.data.armatures.new(name)
    arm = bpy.data.objects.new(name, arm_data)
    bpy.context.scene.collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm_data.edit_bones

    root = eb.new("Anchor_Wrist")
    root.head = wrist
    root.tail = wrist + Vector((0.0, 0.0, 0.03))   # points +Z (along hand)

    finger_bones = []
    for finger in FINGERS:
        p = joints[finger]                          # [Root(MCP), Mid(PIP), Tip(DIP)]
        heads = [p[0], p[1], p[2]]
        tails = [p[1], p[2], tipends[finger]]       # tip bone spans the distal phalanx
        parent = root
        connect = False                             # Root is detached from wrist
        for i, seg in enumerate(("Root", "Mid", "Tip")):
            b = eb.new(f"Anchor_{finger}_{seg}")
            b.head = heads[i]
            b.tail = tails[i]
            b.parent = parent
            b.use_connect = connect
            parent = b
            connect = True                          # Mid/Tip connect to their parent
            finger_bones.append(b.name)

    # Finger/thumb roll.
    #
    # FINGERS (Index/Middle/Ring/Pinky): local +Z -> -Y  =>  local +X = flexion axis and
    #   +X curls the tip straight into the palm (-Y). This is exact (every finger bone lies
    #   in the world X-Z plane) and is render- + numerically-verified in Unity (a +local-X
    #   rotation moves each fingertip purely toward -Y with zero X drift). UNCHANGED.
    #
    # THUMB (P2 tuck fix): the thumb mesh is a straight T-pose digit pointing out to the
    #   side (-X for the left hand). With the plain -Y roll, +local-X flexion just drops the
    #   thumb straight DOWN its own splayed axis, so on a fist it stays out to the side
    #   instead of folding across the palm. We instead roll the thumb so its flexion axis
    #   (local +X) is the world direction FLEX_XD — a 45deg blend of "across the palm"
    #   (+Y = palm normal, sweeps the tip in the palm plane toward the fingers) and "down"
    #   (+X, the finger flexion axis). A +local-X rotation then carries the thumb tip both
    #   ACROSS toward the palm centre and DOWN, i.e. it tucks. FLEX_XD mirrors in X for the
    #   right hand so L/R stay mirror images. To make the bone's local +X equal a desired
    #   world axis Xd (given the bone's Y = head->tail direction), align_roll must aim local
    #   +Z at Xd x boneDir (since Blender sets local +X = boneY x boneZ).
    side = name[-1]
    thumb_xd = Vector((1.0, 1.0, 0.0)).normalized()
    if side == 'R':
        # The flexion axis is a rotation axis (pseudovector): reflecting the rig across
        # the X-plane (the L->R mirror) negates its Y,Z and keeps X, so the same +local-X
        # rotation the mod applies produces the mirror-image tuck on the right hand.
        thumb_xd = Vector((thumb_xd.x, -thumb_xd.y, -thumb_xd.z))
    for name_ in finger_bones:
        b = eb[name_]
        if name_.startswith("Anchor_Thumb_"):
            bdir = (b.tail - b.head).normalized()
            z_target = thumb_xd.cross(bdir)
            if z_target.length < 1e-6:
                z_target = NEG_Y
            b.align_roll(z_target.normalized())
        else:
            b.align_roll(NEG_Y)

    # ----- Anchor bones (non-deforming) -----
    # Kept as BONES (not empties) so they ride the SAME export/axis pipeline as the
    # skeleton (Blender's FBX EMPTY-rotation handling is unreliable). primary_bone_axis
    # ='Y' => Unity node +Y = the bone's length direction; secondary 'X' => node +X = the
    # bone's local X. So point Palm/Grab DOWN the palm normal (world -Y) to get Unity
    # +Y = palm-out, and roll local +Z to world +Z (fingers) => node +Z = along fingers.
    def anchor_bone(nm, head_pos, length_dir, roll_z, parent_bone):
        b = eb.new(nm)
        b.head = head_pos
        b.tail = head_pos + length_dir * 0.02
        b.parent = parent_bone
        b.use_connect = False
        b.align_roll(roll_z)
        return b

    anchor_bone("Anchor_Palm", palm, NEG_Y, Vector((0.0, 0.0, 1.0)), root)      # +Y palm-out
    anchor_bone("Anchor_Grab", grab, NEG_Y, Vector((0.0, 0.0, 1.0)), root)      # +Y palm-out
    # IndexTip: pure position anchor (orientation not read); point it along the finger.
    anchor_bone("Anchor_IndexTip", indextip, Vector((0.0, 0.0, 1.0)), NEG_Y,
                eb["Anchor_Index_Tip"])

    bpy.ops.object.mode_set(mode='OBJECT')
    return arm


def _seg_dist(p, a, b):
    ab = b - a
    L2 = ab.length_squared
    t = 0.0 if L2 == 0.0 else max(0.0, min(1.0, (p - a).dot(ab) / L2))
    return (p - (a + ab * t)).length


# ---- geometric digit skinning + diagnostics helpers ------------------------------------
# Shared region constants (were local to skin(); the diagnostics need them too).
PALM_SOFT, PALM_CAP = 0.012, 0.018
DIGIT_SOFT = 0.014                            # floor; raised per finger by r95+2 mm
DIGIT_CAP_PAD = 0.008                         # cap = soft + this
DIGIT_T0 = -0.10                              # digit region starts behind the MCP


def _chain_param(co, pts):
    """Project a point onto the finger polyline [MCP, PIP, DIP, tip-end].

    Returns (s, d): the ARC LENGTH along the chain of the closest point (s < 0 before
    the MCP via the unclamped extension of the first segment, s > total beyond the tip
    likewise) and the distance to the polyline."""
    best_d, best_s = None, 0.0
    acc = 0.0
    n = len(pts) - 1
    for i in range(n):
        a, b = pts[i], pts[i + 1]
        ab = b - a
        L = ab.length
        t = (co - a).dot(ab) / (L * L)
        tc = max(0.0, min(1.0, t))
        d = (co - (a + ab * tc)).length
        s = acc + tc * L
        if i == 0 and t < 0.0:
            s = t * L                          # signed distance behind the MCP
        if i == n - 1 and t > 1.0:
            s = acc + t * L                    # beyond the fingertip end
        if best_d is None or d < best_d:
            best_d, best_s = d, s
        acc += L
    return best_s, best_d


def _smoothstep01(x):
    x = max(0.0, min(1.0, x))
    return x * x * (3.0 - 2.0 * x)


# ---- chain refit (see the CHAIN REFIT block near the top) ------------------------------
def _pca_dir(vs, C):
    """Principal axis of a point cloud via power iteration on the covariance matrix."""
    M = [[0.0] * 3 for _ in range(3)]
    for c in vs:
        d = c - C
        for i in range(3):
            for j in range(3):
                M[i][j] += d[i] * d[j]
    v = Vector((0.05, 0.05, 1.0))
    for _ in range(60):
        v = Vector((sum(M[0][k] * v[k] for k in range(3)),
                    sum(M[1][k] * v[k] for k in range(3)),
                    sum(M[2][k] * v[k] for k in range(3))))
        if v.length < 1e-12:
            return Vector((0.0, 0.0, 1.0))
        v.normalize()
    return v


def refit_chains(mesh, joints, indextip):
    """Refit every [MCP, PIP, fingertip] chain to the ACTUAL mesh digits (pre-DIP).

    Returns (joints_new, indextip_new). The seed chains (hardcoded glove constants or
    prepare_hand.py's JSON) only bootstrap the per-vert nearest-digit assignment; the
    exported bones come from the mesh itself."""
    V = [v.co.copy() for v in mesh.data.vertices]
    pts = {f: [joints[f][0], joints[f][1], joints[f][2]] for f in FINGERS}
    tot = {f: (pts[f][1] - pts[f][0]).length + (pts[f][2] - pts[f][1]).length
           for f in FINGERS}
    near = []
    for co in V:
        best = None
        for f in FINGERS:
            s, d = _chain_param(co, pts[f])
            if best is None or d < best[2]:
                best = (f, s, d)
        near.append(best)

    # 1) per-finger tube fit: centroid + principal axis of the distal 2/3 of the digit.
    # Two passes: the seed-chain neighbourhood bootstraps a first PCA axis, then the
    # sample is re-selected around THAT axis and refit once (the welded styled meshes
    # carry hole-fill caps/plates that can dominate a small first sample).
    fits = {}
    for f in FINGERS:
        dcap = 0.05 if f == "Thumb" else 0.035
        sel = [V[i] for i, (g, s, d) in enumerate(near)
               if g == f and 0.30 * tot[f] <= s <= 1.02 * tot[f] and d < dcap]
        olddir = (pts[f][2] - pts[f][0]).normalized()
        if len(sel) < 40:
            log(f"refit {f}: only {len(sel)} tube verts — keeping seed chain")
            C = (pts[f][0] + pts[f][2]) * 0.5
            fits[f] = (C, olddir, pts[f][2].copy())
            continue
        C = sum(sel, Vector()) / len(sel)
        u = _pca_dir(sel, C)
        if u.dot(olddir) < 0:
            u = -u
        for _ in range(2):                     # refinement: re-select around the fit
            rad = [((v - C) - u * (v - C).dot(u)).length for v in sel]
            rad.sort()
            r95 = rad[min(len(rad) - 1, int(0.95 * (len(rad) - 1)))]
            sel2 = [v for v in sel if ((v - C) - u * (v - C).dot(u)).length < r95 + 0.004]
            if len(sel2) < 40:
                break
            C = sum(sel2, Vector()) / len(sel2)
            u2 = _pca_dir(sel2, C)
            if u2.dot(u) < 0:
                u2 = -u2
            u = u2
        ang = math.degrees(math.acos(max(-1.0, min(1.0, u.dot(olddir)))))
        if ang > 30.0:
            log(f"refit {f}: fitted axis {ang:.1f} deg off the seed — degenerate sample, "
                f"keeping seed direction")
            u = olddir
        projs = sorted((v - C).dot(u) for v in sel)
        s_tip = projs[min(len(projs) - 1, int(0.98 * (len(projs) - 1)))]
        F = C + u * s_tip
        if (F - pts[f][2]).length > 0.03:
            log(f"refit {f}: fitted tip {1000*(F - pts[f][2]).length:.0f} mm from seed tip "
                f"— keeping seed tip")
            F = pts[f][2].copy()
        fits[f] = (C, u, F)
        log(f"refit {f}: n={len(sel)} dir=({u.x:+.3f},{u.y:+.3f},{u.z:+.3f}) "
            f"({ang:.1f} deg off seed) tip=({F.x:+.4f},{F.y:+.4f},{F.z:+.4f})")

    # 2) inter-finger valleys on the fitted axes -> finger MCP heights
    def x_at_z(f, z):
        C, u, _ = fits[f]
        if abs(u.z) < 0.2:
            return C.x
        return (C + u * ((z - C.z) / u.z)).x

    order = ["Index", "Middle", "Ring", "Pinky"]
    valley = {}
    for fa, fb in zip(order, order[1:]):
        z0 = min(fits[fa][0].z, fits[fb][0].z)
        z, vz = z0, None
        while z > 0.015:
            xa, xb = x_at_z(fa, z), x_at_z(fb, z)
            lo, hi = min(xa, xb), max(xa, xb)
            if hi - lo < 0.004:
                break                        # fitted axes converged — stop the scan
            xs = sorted(c.x for c in V
                        if abs(c.z - z) < VALLEY_SLAB * 0.5 and lo <= c.x <= hi)
            gap, prev = 0.0, lo
            for x in xs + [hi]:
                gap = max(gap, x - prev)
                prev = x
            if gap < VALLEY_GAP:
                break                        # columns merged: below the webbing valley
            vz = z
            z -= 0.002
        if vz is None:
            vz = z0
            log(f"refit valley {fa}-{fb}: NO separating gap found — falling back to {vz:.4f}")
        valley[(fa, fb)] = vz
    log("refit valleys (z m): " + ", ".join(
        f"{a}-{b} {v:.4f}" for (a, b), v in valley.items()))

    new = {}
    for f in order:
        C, u, F = fits[f]
        # MEAN of the adjacent valleys (min() dragged ring/middle down to the low pinky
        # attachment on the styled hands), with the knuckle drop SCALED by valley height
        # (calibrated =1 on the glove's 0.132 m valleys; the armored hands' much lower
        # valleys sit on proportionally shorter palms).
        adj = [v for pair, v in valley.items() if f in pair]
        vz = sum(adj) / len(adj)
        drop = MCP_DROP * max(0.6, min(1.0, vz / 0.132))
        mcp_z = max(MCP_MIN_Z, vz - drop)
        mcp_z = min(mcp_z, 0.60 * F.z)       # sanity: keep a real palm below the knuckle
        if abs(u.z) < 0.5:
            mcp = pts[f][0].copy()           # near-horizontal fit: keep the seed MCP
        else:
            mcp = C + u * ((mcp_z - C.z) / u.z)
        # Y-RECENTRE the MCP inside the hand: the long extrapolation down the fitted
        # axis amplifies its small y-slope error (arcane MCPs landed 20-30 mm palm-side
        # of the mesh, inflating the proximal tube radius). The knuckle joint belongs at
        # the mid-plane of the hand's local column: mean y of the mesh slab around it.
        ys = [c.y for c in V
              if abs(c.z - mcp_z) < 0.005 and abs(c.x - mcp.x) < 0.014]
        if len(ys) >= 8:
            yc = sum(ys) / len(ys)
            if abs(yc - mcp.y) > 0.004:
                mcp = Vector((mcp.x, yc, mcp.z))
        new[f] = [mcp, (mcp + F) * 0.5, F]
        log(f"refit {f}: MCP ({pts[f][0].x:+.4f},{pts[f][0].y:+.4f},{pts[f][0].z:+.4f})"
            f" -> ({mcp.x:+.4f},{mcp.y:+.4f},{mcp.z:+.4f})  (valley {vz:.4f}, drop {drop:.4f})")

    # 3) thumb: EXTEND the root proximally along the SEED axis until the tube merges
    # into the palm mass. prepare_hand.py places the thumb Root 30 % out along the free
    # thumb, so the proximal 30 % of the styled thumbs was rigid ("gray half thumb" in
    # the heatmaps). Extension-only (never moves the proven glove thumb distally, never
    # re-aims its tuck geometry): the seed direction and tip stay, the Root slides down
    # the axis to the detected attachment. Merge test per 2.5 mm slab: off-axis r90
    # blow-up OR the slab vert count jumping past 3x the mid-thumb reference (the
    # radius alone misses the merge on fat armored thumbs whose reference r90 is
    # already palm-sized).
    f = "Thumb"
    seed_root, seed_tip = pts[f][0], pts[f][2]
    u = (seed_tip - seed_root).normalized()
    C0 = seed_root
    F = fits[f][2]
    if (F - seed_tip).length > 0.02:
        F = seed_tip.copy()
    projd = []
    for i, v in enumerate(V):
        if near[i][0] != f:
            continue
        rel = v - C0
        p = rel.dot(u)
        r = (rel - u * p).length
        if r < 0.05:
            projd.append((p, r))
    s_tip = (F - C0).dot(u)

    def slab(s):
        return sorted(r for (p, r) in projd if abs(p - s) < 0.003)

    refs = [slab(0.30 * s_tip + 0.06 * s_tip * k) for k in range(6)]
    refs = [ds for ds in refs if len(ds) >= 6]
    if not refs:
        log("refit Thumb: no reference slabs — keeping seed chain")
        new[f] = [seed_root.copy(), pts[f][1].copy(), F]
    else:
        r90s = sorted(ds[min(len(ds) - 1, int(0.9 * (len(ds) - 1)))] for ds in refs)
        ns = sorted(len(ds) for ds in refs)
        ref_r, ref_n = r90s[len(r90s) // 2], ns[len(ns) // 2]
        lim_r = max(1.7 * ref_r, ref_r + 0.012)
        lim_n = 3.0 * ref_n
        s, base_s, bad = 0.0, 0.0, 0
        while True:
            s_next = s - 0.0025
            pt = C0 + u * s_next
            if pt.z < 0.012 or s_next <= -0.9 * s_tip:
                break                        # hit the wrist band / scan floor
            ds = slab(s_next)
            r = None if len(ds) < 6 else ds[min(len(ds) - 1, int(0.9 * (len(ds) - 1)))]
            if r is None or r > lim_r or len(ds) > lim_n:
                bad += 1
                if bad >= 2:
                    break                    # tube merged into the palm mass
            else:
                bad = 0
                base_s = s_next              # last slab that still looked like a tube
            s = s_next
        mcp = C0 + u * base_s                # base_s <= 0: extension only
        new[f] = [mcp, (mcp + F) * 0.5, F]
        log(f"refit Thumb: ref r90 {1000*ref_r:.1f} mm n {ref_n}, extended root "
            f"{-1000*base_s:.1f} mm proximally (tip s {1000*s_tip:.1f}), MCP "
            f"({seed_root.x:+.4f},{seed_root.y:+.4f},{seed_root.z:+.4f}) -> "
            f"({mcp.x:+.4f},{mcp.y:+.4f},{mcp.z:+.4f})")

    Ci, ui, Fi = fits["Index"]
    return new, Fi + ui * 0.002


def _seg_weights(s, lens, h0, h1, h2, bone_names):
    """Explicit per-segment bone weights for a digit-tube vert at arc length s.

    Pure zones own 100 % of their segment bone; each joint boundary is a smoothstep
    2-bone cross-fade (halfwidths h0 at the MCP vs the wrist, h1 at Root|Mid, h2 at
    Mid|Tip). Returns {bone: w} summing to 1; "WRIST" is the caller's wrist bone."""
    s1 = lens[0]
    s2 = lens[0] + lens[1]
    if s < -h0:
        return {"WRIST": 1.0}
    wr = _smoothstep01((s + h0) / (2.0 * h0)) if s < h0 else 1.0       # wrist -> root
    rm = _smoothstep01((s - (s1 - h1)) / (2.0 * h1)) if s1 - h1 <= s <= s1 + h1 \
        else (1.0 if s > s1 else 0.0)                                   # root -> mid
    mt = _smoothstep01((s - (s2 - h2)) / (2.0 * h2)) if s2 - h2 <= s <= s2 + h2 \
        else (1.0 if s > s2 else 0.0)                                   # mid -> tip
    w = {
        bone_names[0]: wr * (1.0 - rm),
        bone_names[1]: rm * (1.0 - mt),
        bone_names[2]: mt,
    }
    if wr < 1.0:
        w["WRIST"] = 1.0 - wr
    return {nm: wv for nm, wv in w.items() if wv > 0.0}


def _digit_geometry(mesh, joints, tipends):
    """Per-vert nearest finger chain + (s, d) params, and adaptive PER-SEGMENT caps.

    2026-07 thick-mesh fix: a single r95 per FINGER under-measured the armored styles
    (knuckle shells are ~2x thicker than the fingertips), so proximal armor fell outside
    the one cap and went wrist-rigid. The tube radius is now measured per SEGMENT (prox/
    mid/tip s-bins, each from its own off-axis d distribution: soft = r95 + 2 mm, cap =
    soft + 8 mm) and soft/cap interpolate piecewise-linearly along s (_radius_at), so
    tube membership follows the mesh's real local thickness.
    soft[f]/cap[f] are 3-element lists [prox, mid, tip]."""
    pts, lens = {}, {}
    for f in FINGERS:
        p = joints[f]
        pts[f] = [p[0], p[1], p[2], tipends[f]]
        lens[f] = [(pts[f][i + 1] - pts[f][i]).length for i in range(3)]
    verts = mesh.data.vertices
    info = [None] * len(verts)                 # (finger, s, d) per vert
    shell = {f: [[], [], []] for f in FINGERS}
    for v in verts:
        co = v.co
        best = None
        for f in FINGERS:
            s, d = _chain_param(co, pts[f])
            if best is None or d < best[2]:
                best = (f, s, d)
        info[v.index] = best
        f, s, d = best
        total = sum(lens[f])
        # segment bin for the radius sample; the prox bin starts at 0.35*L0 so palm/
        # webbing mass just below the knuckle cannot inflate the proximal radius.
        if d < 0.04 and 0.35 * lens[f][0] <= s <= 1.1 * total:
            b = 0 if s < lens[f][0] else (1 if s < lens[f][0] + lens[f][1] else 2)
            shell[f][b].append(d)
    soft, cap = {}, {}
    for f in FINGERS:
        ss, prev = [], DIGIT_SOFT
        for b in range(3):
            ds = sorted(shell[f][b])
            if ds:
                r95 = ds[min(len(ds) - 1, int(0.95 * len(ds)))]
                prev = max(DIGIT_SOFT, r95 + 0.002)
            ss.append(prev)                    # empty bin: inherit the previous segment
        if f != "Thumb":
            # deep-splayed MCPs (arcane pinky) let palm mass into the prox/mid radius
            # sample and blew soft to 28-36 mm — clamp both to the TIP segment's radius
            # (always a clean tube) x1.5 so a finger can never annex the palm.
            lim = max(0.020, 1.5 * ss[2])
            ss[0] = min(ss[0], lim)
            ss[1] = min(ss[1], lim)
        soft[f] = ss
        cap[f] = [x + DIGIT_CAP_PAD for x in ss]
    return pts, lens, info, soft, cap


def _radius_at(lens_f, arr, s):
    """Piecewise-linear soft/cap radius along the chain (arr = [prox, mid, tip] values,
    anchored at the segment midpoints; constant beyond the first/last midpoint)."""
    L0, L1, L2 = lens_f
    m0, m1, m2 = 0.5 * L0, L0 + 0.5 * L1, L0 + L1 + 0.5 * L2
    if s <= m0:
        return arr[0]
    if s <= m1:
        return arr[0] + (arr[1] - arr[0]) * (s - m0) / (m1 - m0)
    if s <= m2:
        return arr[1] + (arr[2] - arr[1]) * (s - m1) / (m2 - m1)
    return arr[2]


def _geo_fade(d, soft, cap):
    if d <= soft:
        return 1.0
    if d >= cap:
        return 0.0
    t = (d - soft) / (cap - soft)
    return 1.0 - t * t * (3.0 - 2.0 * t)


def _geo_weights(mesh, joints, tipends):
    """GEO_SKIN pass: explicit geometric weights for every vert (see GEO_SKIN doc)."""
    pts, lens, info, soft, cap = _digit_geometry(mesh, joints, tipends)
    log("geo-skin digit caps (soft/cap mm, prox|mid|tip): " + ", ".join(
        f"{f} " + "|".join(f"{soft[f][b]*1000:.0f}/{cap[f][b]*1000:.0f}" for b in range(3))
        for f in FINGERS))
    verts = mesh.data.vertices
    vert_w = [None] * len(verts)
    CUFF_Z = -0.002
    for v in verts:
        co = v.co
        if co.z < CUFF_Z:                      # forearm cuff: rigid with the wrist
            vert_w[v.index] = {"Anchor_Wrist": 1.0}
            continue
        f, s, d = info[v.index]
        h0 = MCP_BLEND * lens[f][0]
        soft_s = _radius_at(lens[f], soft[f], s)
        cap_s = _radius_at(lens[f], cap[f], s)
        if s < -h0 or d >= cap_s:              # palm / back / webbing: rigid hand block
            vert_w[v.index] = {"Anchor_Wrist": 1.0}
            continue
        seg_w = _seg_weights(
            s, lens[f], h0,
            IPJ_BLEND * min(lens[f][0], lens[f][1]),
            IPJ_BLEND * min(lens[f][1], lens[f][2]),
            [f"Anchor_{f}_{seg}" for seg in ("Root", "Mid", "Tip")])
        fade = _geo_fade(d, soft_s, cap_s)
        w = {}
        for nm, wv in seg_w.items():
            nm = "Anchor_Wrist" if nm == "WRIST" else nm
            w[nm] = w.get(nm, 0.0) + wv * fade
        if fade < 1.0:
            w["Anchor_Wrist"] = w.get("Anchor_Wrist", 0.0) + (1.0 - fade)
        vert_w[v.index] = w
    return vert_w


def _write_weights(mesh, arm, groups, vert_w):
    """Clamp to <=4 influences, renormalise, write groups, parent + modifier."""
    for v in mesh.data.vertices:
        items = sorted(vert_w[v.index].items(), key=lambda kv: -kv[1])[:4]
        tot = sum(w for _, w in items) or 1.0
        for nm, w in items:
            wn = w / tot
            if wn > 1e-4:
                groups[nm].add([v.index], wn, 'REPLACE')
    mesh.parent = arm
    mesh.matrix_parent_inverse = arm.matrix_world.inverted()
    if not any(m.type == 'ARMATURE' for m in mesh.modifiers):
        m = mesh.modifiers.new("Armature", 'ARMATURE')
        m.object = arm


def skin(mesh, arm, joints, wrist, palm, tipends):
    """Deterministic proximity skinning with a KDTree smoothing pass.

    The AI mesh is fragmented (~310 disconnected shells, heavily non-manifold), so
    Blender's heat/bone weighting fails outright. Instead every vertex is bound to the
    nearest finger chain (segment distance) blended with a long virtual WRIST segment
    that runs through the palm, so the palm/back/wrist stay rigid while finger verts
    curl with their own 3 joints. <=4 influences by construction, then normalised.

    DEFECT 3 improvement: the raw proximity weights are near-rigid (a hard nearest-
    finger pick with a cubic falloff), which pinches/collapses the exposed fingertips
    under curl. Two changes soften the deformation without breaking the flexion
    invariant:
      (1) a gentler falloff (P=2 instead of 3) spreads each joint's influence across a
          wider band, so a bend distributes over several rings of verts instead of
          creasing at one; and
      (2) a spatial (position-based) Laplacian smoothing pass — topology is useless on a
          310-shell mesh, so neighbours are found with a mathutils KDTree inside a small
          radius. This blends weights ACROSS the disconnected finger shells so they curl
          as one soft digit rather than tearing shell-from-shell. The radius is kept
          small (7 mm) so fingers stay separate (MCPs are ~20 mm apart).
    """
    # capture segments (weighting only; the real Wrist bone stays short for correct pose)
    wrist_seg = (wrist, palm + Vector((0.0, 0.005, 0.045)))   # long: wrist -> top of palm
    finger_segs = {}
    for f in FINGERS:
        p = joints[f]
        finger_segs[f] = [
            (f"Anchor_{f}_Root", p[0], p[1]),
            (f"Anchor_{f}_Mid",  p[1], p[2]),
            (f"Anchor_{f}_Tip",  p[2], tipends[f]),
        ]

    # ensure a vertex group per deform bone + wrist
    names = ["Anchor_Wrist"] + [f"Anchor_{f}_{s}" for f in FINGERS for s in ("Root", "Mid", "Tip")]
    groups = {}
    for n in names:
        groups[n] = mesh.vertex_groups.get(n) or mesh.vertex_groups.new(name=n)

    # FIST FIX FINAL (2026-07, all styles): explicit geometric digit skinning — see the
    # GEO_SKIN block comment near the top. The legacy inverse-distance path below is
    # retained ONLY for RIG_HAND_GEO_SKIN=0 A/B diagnostics.
    if GEO_SKIN:
        _write_weights(mesh, arm, groups, _geo_weights(mesh, joints, tipends))
        return

    P = 2.0            # gentler than the old cubic -> wider, smoother influence bands
    EPS = 1e-9
    verts = mesh.data.vertices
    nV = len(verts)

    # POSE-MATRIX FIX (membrane/fan stretch): the raw 1/(d^P) blend gives verts that are
    # FAR from every finger chain (mid-palm, thumb-index webbing rim) a substantial
    # residual finger weight, so a fist pulled a batwing membrane between the thumb and
    # index (Arcane) and a radial "fan" sheet across the palm (Plate) — render-verified
    # on the 9-pose matrix. Finger influence is therefore DISTANCE-CAPPED with a
    # smoothstep taper toward the wrist bone, and the cap is REGION-AWARE.
    #
    # FIST FIX (2026-07, round 2): the first region-aware tuning (digit zone t>=0.05,
    # flat 14/22 mm digit caps) OVERSHOT — on hardware "almost only the fingertips
    # move". Render matrix + per-vert stats confirmed the weights (not the runtime
    # clamp) were the culprit: the armored shells sit 13-21 mm off the bone axis, so
    # most of the digit surface fell into the 14-22 mm taper band (Plate thumb 74-83 %
    # tapered), and the MCP knuckle bulge — which projects BEHIND t=0.05 — was treated
    # as palm and faded at 12-18 mm (Plate index/middle knuckles 100 % tapered, Arcane
    # middle 50 % FULLY frozen to the wrist). Root joints therefore barely moved the
    # mesh: the "flat-cap froze knuckles" lesson repeated at a milder level. Reworked:
    #   - the digit region starts at t >= -0.10 (10 % of chain length BEHIND the MCP),
    #     so the knuckle armor around the finger root belongs to its finger;
    #   - digit caps SCALE WITH MEASURED LOCAL MESH THICKNESS: per finger, soft =
    #     max(14 mm, r95 + 2 mm) where r95 is the 95th-percentile off-axis distance of
    #     that finger's own digit shell (nearest-chain verts, 0.05 <= t <= 1.1,
    #     d < 35 mm), cap = soft + 8 mm. The whole shell — knuckle plates included —
    #     now carries near-full weight to its finger chain;
    #   - the PALM/webbing cap (12-18 mm, t < -0.10) is UNCHANGED: that is what killed
    #     the batwing membrane and the palm fan sheet — do not regress it.
    # The faded share still moves to the wrist bone so the palm stays rigid with the
    # hand; the KD smoothing pass below blends the taper ring.
    # (PALM_SOFT/PALM_CAP/DIGIT_SOFT/DIGIT_CAP_PAD/DIGIT_T0 are module-level now.)

    def finger_fade(d, soft, cap):
        if d <= soft:
            return 1.0
        if d >= cap:
            return 0.0
        t = (d - soft) / (cap - soft)
        return 1.0 - t * t * (3.0 - 2.0 * t)      # smoothstep down

    chain_ends = {}
    for f in FINGERS:
        a = finger_segs[f][0][1]                  # MCP (root head)
        b = finger_segs[f][2][2]                  # tip end
        chain_ends[f] = (a, b, (b - a), max((b - a).length_squared, 1e-9))

    def chain_t(co, f):
        a, b, ab, L2 = chain_ends[f]
        return (co - a).dot(ab) / L2

    # Verts BELOW the wrist crease (long cuffs on the alternative hands; the original
    # glove has no geometry below z=0) are rigid armor/cloth around the forearm — bind
    # them 100% to the wrist bone, or distant finger chains pick up partial weights and
    # SHRED the cuff into stretched sheets when a fist is made (render-verified).
    CUFF_Z = -0.002

    # Pre-pass (ALT hands): nearest finger + off-axis distance per vert, then the
    # per-finger digit thickness r95 that drives the adaptive digit caps.
    nearest = [None] * nV                         # (best_f, best_d) per vert
    digit_soft = {f: DIGIT_SOFT for f in FINGERS}
    digit_cap = {f: DIGIT_SOFT + DIGIT_CAP_PAD for f in FINGERS}
    if ALT_HAND:
        shell_d = {f: [] for f in FINGERS}
        for v in verts:
            co = v.co
            if co.z < CUFF_Z:
                continue
            best_f, best_d = None, 1e9
            for f in FINGERS:
                dmin = min(_seg_dist(co, a, b) for _, a, b in finger_segs[f])
                if dmin < best_d:
                    best_d, best_f = dmin, f
            nearest[v.index] = (best_f, best_d)
            if best_d < 0.035 and 0.05 <= chain_t(co, best_f) <= 1.1:
                shell_d[best_f].append(best_d)
        for f in FINGERS:
            ds = sorted(shell_d[f])
            r95 = ds[min(len(ds) - 1, int(0.95 * len(ds)))] if ds else DIGIT_SOFT
            digit_soft[f] = max(DIGIT_SOFT, r95 + 0.002)
            digit_cap[f] = digit_soft[f] + DIGIT_CAP_PAD
        log("digit caps (soft/cap mm): " + ", ".join(
            f"{f} {digit_soft[f]*1000:.1f}/{digit_cap[f]*1000:.1f}" for f in FINGERS))

    # ---- pass 1: raw proximity weights (dict per vertex) --------------------------------
    vert_w = [dict() for _ in range(nV)]
    for v in verts:
        co = v.co
        if co.z < CUFF_Z:
            vert_w[v.index] = {"Anchor_Wrist": 1.0}
            continue
        dW = _seg_dist(co, *wrist_seg)
        # nearest finger by its closest segment
        if ALT_HAND:
            best_f, best_d = nearest[v.index]
            on_digit = chain_t(co, best_f) >= DIGIT_T0
            soft, cap = (digit_soft[best_f], digit_cap[best_f]) if on_digit \
                else (PALM_SOFT, PALM_CAP)
            fade = finger_fade(best_d, soft, cap)
        else:
            best_f, best_d = None, 1e9
            for f in FINGERS:
                dmin = min(_seg_dist(co, a, b) for _, a, b in finger_segs[f])
                if dmin < best_d:
                    best_d, best_f = dmin, f
            fade = 1.0     # original glove: keep the shipped weighting byte-for-byte
        if fade <= 0.0:
            vert_w[v.index] = {"Anchor_Wrist": 1.0}   # palm/webbing rim: rigid with the hand
            continue
        # BONE-TRANSFER STIFFENING (ALT hands, fist fix round 3): the styled shells sit
        # 13-21 mm off the bone axis (vs ~8 mm for the glove), which flattens the 1/d^P
        # ratios — the long wrist segment and the neighbouring joints keep sizeable
        # shares on the digit surface, so the mesh only followed ~60 % of the commanded
        # joint rotation and even a full-range fist read as half closed. Two targeted
        # changes for verts ON the digit (never the palm, so the membrane fix stands):
        #   - past t >= 0.2 the wrist bone is dropped from the blend entirely (its
        #     ~0.1 residual share moves to the finger's own joints);
        #   - the falloff sharpens to P=2.5, restoring the segment-dominance the thin
        #     glove shell gets naturally at P=2.
        t_chain = chain_t(co, best_f) if ALT_HAND else 0.0
        cands = [] if (ALT_HAND and t_chain >= 0.2) else [("Anchor_Wrist", dW)]
        for nm, a, b in finger_segs[best_f]:
            cands.append((nm, _seg_dist(co, a, b)))
        Pv = 2.5 if (ALT_HAND and t_chain >= DIGIT_T0) else P
        raws = [(nm, 1.0 / (d ** Pv + EPS)) for nm, d in cands]
        tot = sum(w for _, w in raws)
        w = {nm: wv / tot for nm, wv in raws}
        if fade < 1.0:
            # taper band: shift the faded share of the finger weights onto the wrist
            fsum = sum(wv for nm, wv in w.items() if nm != "Anchor_Wrist")
            for nm in list(w):
                if nm != "Anchor_Wrist":
                    w[nm] *= fade
            w["Anchor_Wrist"] = w.get("Anchor_Wrist", 0.0) + fsum * (1.0 - fade)
        vert_w[v.index] = w

    # ---- pass 2: position-based Laplacian smoothing (blend across shells) ---------------
    kd = KDTree(nV)
    for v in verts:
        kd.insert(v.co, v.index)
    kd.balance()

    # ALT hands smooth LESS (fist fix round 3): 3 iterations at alpha 0.5 diffuse the
    # weights over ~12 mm — a third of a phalanx — which flattens the root/mid/tip
    # contrast on the thick styled shells and was a main reason the mesh only followed
    # ~60 % of the commanded joint rotation ("fists barely close"). Two lighter passes
    # still fuse the disconnected armor shells into one digit (render-verified: no
    # shell tearing in the 9-pose matrix) while keeping the knuckle crease articulate.
    # The original glove keeps the shipped 3x0.5 smoothing byte-for-byte.
    RADIUS = 0.007     # 7 mm: within a finger, below the ~20 mm inter-finger spacing
    ALPHA = 0.35 if ALT_HAND else 0.5    # blend toward the neighbourhood average
    ITERS = 2 if ALT_HAND else 3

    # NO CROSS-FINGER SMOOTHING on the styled digits (fist fix round 3): the armored
    # finger shells are so thick they nearly touch, so the 7 mm KD radius bridges the
    # inter-finger gap and bleeds each digit's weights onto its neighbours. The thin
    # glove never had this (its fingers sit ~12 mm of air apart), which is why the
    # glove articulates crisply (a lone curled index folds fully away) while the
    # styled hands moved mushily. Digit verts (t >= 0.05 on their own chain) therefore
    # only average with SAME-finger digit verts and palm/webbing verts, never with a
    # different finger's shell. Palm-region smoothing is untouched (taper blending).
    digit_of = [None] * nV
    if ALT_HAND:
        for v in verts:
            info = nearest[v.index]
            if info is not None and chain_t(v.co, info[0]) >= 0.05:
                digit_of[v.index] = info[0]

    for _ in range(ITERS):
        smoothed = [None] * nV
        for v in verts:
            if v.co.z < CUFF_Z:                  # cuff stays rigidly on the wrist bone
                smoothed[v.index] = vert_w[v.index]
                continue
            acc = dict(vert_w[v.index])          # start from self
            near = kd.find_range(v.co, RADIUS)
            cnt = 0
            own_digit = digit_of[v.index]
            for (_co, ni, _d) in near:
                if ni == v.index:
                    continue
                if ALT_HAND and digit_of[ni] is not None and own_digit is not None \
                        and digit_of[ni] != own_digit:
                    continue                     # never blend across the finger gap
                for nm, w in vert_w[ni].items():
                    acc[nm] = acc.get(nm, 0.0) + w
                cnt += 1
            if cnt == 0:
                smoothed[v.index] = vert_w[v.index]
                continue
            navg = {nm: s / (cnt + 1) for nm, s in acc.items()}  # +1 counts self
            self_w = vert_w[v.index]
            blend = {}
            for nm in set(self_w) | set(navg):
                blend[nm] = (1.0 - ALPHA) * self_w.get(nm, 0.0) + ALPHA * navg.get(nm, 0.0)
            smoothed[v.index] = blend
        vert_w = smoothed

    # ---- write: clamp to <=4 influences, renormalise, parent ----------------------------
    _write_weights(mesh, arm, groups, vert_w)


# ---------------------------------------------------------------------------------------
def tip_vert_indices(mesh, tip_pos, radius=0.013):
    idx = []
    for v in mesh.data.vertices:
        if (v.co - tip_pos).length < radius:
            idx.append(v.index)
    return idx


def evaluated_positions(mesh, indices):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    me = ev.data
    return {i: (mesh.matrix_world @ me.vertices[i].co).copy() for i in indices}


def pose_test(mesh, arm, joints, tipends, side_label):
    """Rotate each finger's 3 joints +40 deg about local X; assert fingertip moves -Y."""
    results = {}
    bpy.context.view_layer.objects.active = arm
    for finger in FINGERS:
        tip_pos = tipends[finger]
        idx = tip_vert_indices(mesh, tip_pos)
        if not idx:
            results[finger] = ("NO_VERTS", 0.0, 0.0)
            continue
        rest = evaluated_positions(mesh, idx)

        bpy.ops.object.mode_set(mode='POSE')
        for pb in arm.pose.bones:
            pb.rotation_mode = 'XYZ'
            pb.rotation_euler = (0.0, 0.0, 0.0)
        for seg in ("Root", "Mid", "Tip"):
            pb = arm.pose.bones[f"Anchor_{finger}_{seg}"]
            pb.rotation_euler = (math.radians(40.0), 0.0, 0.0)  # +40 about LOCAL X
        bpy.context.view_layer.update()

        posed = evaluated_positions(mesh, idx)
        dy = sum((posed[i].y - rest[i].y) for i in idx) / len(idx)
        # sideways drift within the finger's X column
        dx = sum(abs(posed[i].x - rest[i].x) for i in idx) / len(idx)
        # reset
        for pb in arm.pose.bones:
            pb.rotation_euler = (0.0, 0.0, 0.0)
        bpy.ops.object.mode_set(mode='OBJECT')
        bpy.context.view_layer.update()

        # PASS if the tip moved clearly toward -Y and did not mostly slide sideways.
        # THUMB: the P2 tuck fix rolls its flexion axis 45° so the tip deliberately
        # sweeps ACROSS the palm (large dX) while dropping (-Y) — the column check
        # would flag the intended motion, so the thumb only asserts the -Y drop.
        curl_ok = dy < -0.003
        column_ok = finger == "Thumb" or abs(dy) > dx * 0.8
        results[finger] = ("PASS" if (curl_ok and column_ok) else "FAIL", dy, dx)
    return results


def print_table(results, side_label):
    log(f"--- POSE TEST ({side_label}) : +40deg local-X on each finger's 3 joints ---")
    log(f"    {'finger':8s} {'verdict':6s} {'dY(m)':>9s} {'dX(m)':>9s}   (PASS => tip moves -Y into palm)")
    allpass = True
    for f in FINGERS:
        verdict, dy, dx = results[f]
        if verdict != "PASS":
            allpass = False
        log(f"    {f:8s} {verdict:6s} {dy:9.4f} {dx:9.4f}")
    log(f"    OVERALL: {'ALL PASS' if allpass else 'FAILURES PRESENT'}")
    return allpass


def sample_weights(mesh, joints):
    """Numeric sanity check: near each MCP, which bones own the top weights?"""
    log("--- weight sanity: dominant bone near each finger MCP (Root) ---")
    grp = {g.index: g.name for g in mesh.vertex_groups}
    for finger in FINGERS:
        root = joints[finger][0]
        near = [v for v in mesh.data.vertices if (v.co - root).length < 0.012]
        counts = {}
        for v in near:
            if not v.groups:
                continue
            best = max(v.groups, key=lambda g: g.weight)
            nm = grp.get(best.group, "?")
            counts[nm] = counts.get(nm, 0) + 1
        top = sorted(counts.items(), key=lambda kv: -kv[1])[:3]
        log(f"    {finger:8s} n={len(near):3d} top={top}")


# ---------------------------------------------------------------------------------------
# WEIGHT DIAGNOSTICS (RIG_HAND_DIAG=1) — the checks the old "closure metrics" were blind
# to. The pose_test/closure numbers tracked TIP verts (which ride the bone-tracked tip
# in any skinning), so a hand whose proximal/middle tubes are palm-bound still "passed".
# These three measure the SKINNING itself.

def _vert_weight_maps(mesh):
    idx2name = {g.index: g.name for g in mesh.vertex_groups}
    return [{idx2name[g.group]: g.weight for g in v.groups} for v in mesh.data.vertices]


def weight_stats(mesh, joints, tipends, tag):
    """Per-finger, per-zone weight audit: in each PURE segment zone (outside the joint
    blend bands) the expected bone should own ~100 % of the weight. Prints a table and
    returns a JSON-serialisable dict."""
    pts, lens, info, soft, cap = _digit_geometry(mesh, joints, tipends)
    wmap = _vert_weight_maps(mesh)
    report = {}
    log(f"--- WEIGHT STATS ({tag}) ---")
    log(f"    {'finger':7s} {'zone':5s} {'n':>5s} {'wExpect':>8s} {'wWrist':>7s} "
        f"{'wOther':>7s} {'dom%':>6s}   (dom% = verts whose top bone IS the segment bone)")
    for f in FINGERS:
        L0, L1, L2 = lens[f]
        zones = {
            "prox": (0.20 * L0, 0.80 * L0, f"Anchor_{f}_Root"),
            "mid":  (L0 + 0.20 * L1, L0 + 0.80 * L1, f"Anchor_{f}_Mid"),
            "tip":  (L0 + L1 + 0.20 * L2, 1e9, f"Anchor_{f}_Tip"),
        }
        report[f] = {}
        # COVERAGE: what fraction of the finger's whole digit mesh (nearest this chain,
        # from the MCP up, any off-axis distance < 5 cm) actually FOLLOWS the chain
        # (>= 0.5 summed weight on its 3 bones)? THE number the thick-mesh bug hid:
        # zone rows below only audit verts already inside the cap.
        total = L0 + L1 + L2
        own = [i for i, inf in enumerate(info)
               if inf[0] == f and 0.0 <= inf[1] <= 1.05 * total and inf[2] < 0.05]
        chain_bones = {f"Anchor_{f}_{sg}" for sg in ("Root", "Mid", "Tip")}
        ass = sum(1 for i in own
                  if sum(w for nm, w in wmap[i].items() if nm in chain_bones) >= 0.5)
        covp = 100.0 * ass / len(own) if own else 0.0
        log(f"    {f:7s} COVERAGE {ass}/{len(own)} = {covp:.1f}% of digit verts follow the chain")
        report[f]["coverage_pct"] = round(covp, 1)
        for zname, (s0, s1, expect) in zones.items():
            sel = [i for i, inf in enumerate(info)
                   if inf[0] == f and s0 <= inf[1] < s1
                   and inf[2] < _radius_at(lens[f], cap[f], inf[1])]
            if not sel:
                log(f"    {f:7s} {zname:5s} {0:5d}      (no verts)")
                report[f][zname] = {"n": 0}
                continue
            m_exp = sum(wmap[i].get(expect, 0.0) for i in sel) / len(sel)
            m_wr = sum(wmap[i].get("Anchor_Wrist", 0.0) for i in sel) / len(sel)
            m_oth = 1.0 - m_exp - m_wr
            dom = sum(1 for i in sel
                      if wmap[i] and max(wmap[i].items(), key=lambda kv: kv[1])[0] == expect)
            domp = 100.0 * dom / len(sel)
            log(f"    {f:7s} {zname:5s} {len(sel):5d} {m_exp:8.3f} {m_wr:7.3f} "
                f"{max(0.0, m_oth):7.3f} {domp:5.1f}%")
            report[f][zname] = {"n": len(sel), "w_expected": round(m_exp, 4),
                                "w_wrist": round(m_wr, 4),
                                "w_other": round(max(0.0, m_oth), 4),
                                "dominant_pct": round(domp, 1)}
    return report


def follow_test(mesh, arm, joints, tipends, tag):
    """THE fist metric: rotate ONE joint +40° and measure how far the verts of ITS OWN
    tube segment actually travel vs the rigid-rotation prediction (projection of the
    actual displacement onto the predicted one). 1.0 = mesh follows the bone fully;
    ~0 = segment is skinned to the palm/wrist (the 'only fingertips move' bug)."""
    pts, lens, info, soft, cap = _digit_geometry(mesh, joints, tipends)
    bpy.context.view_layer.objects.active = arm
    results = {}
    log(f"--- FOLLOW TEST ({tag}) : +40° on ONE joint; ratio of actual/rigid motion of its segment tube ---")
    log(f"    {'finger':7s} {'Root':>6s} {'Mid':>6s} {'Tip':>6s}   (>=0.70 acceptable, >=0.85 good)")
    for f in FINGERS:
        L0, L1, L2 = lens[f]
        zones = [
            ("Root", 0.25 * L0, 0.75 * L0),
            ("Mid", L0 + 0.25 * L1, L0 + 0.75 * L1),
            ("Tip", L0 + L1 + 0.20 * L2, L0 + L1 + 1.2 * L2),
        ]
        row = {}
        for seg, s0, s1 in zones:
            sel = [i for i, inf in enumerate(info)
                   if inf[0] == f and s0 <= inf[1] < s1
                   and inf[2] < _radius_at(lens[f], cap[f], inf[1])]
            if not sel:
                row[seg] = None
                continue
            rest = evaluated_positions(mesh, sel)
            bone = f"Anchor_{f}_{seg}"
            pb = arm.pose.bones[bone]
            axis = pb.x_axis.copy()            # rest-pose world flexion axis
            head = pb.head.copy()
            rot = Matrix.Rotation(math.radians(40.0), 4, axis)
            bpy.ops.object.mode_set(mode='POSE')
            for pbb in arm.pose.bones:
                pbb.rotation_mode = 'XYZ'
                pbb.rotation_euler = (0.0, 0.0, 0.0)
            pb.rotation_euler = (math.radians(40.0), 0.0, 0.0)
            bpy.ops.object.mode_set(mode='OBJECT')
            bpy.context.view_layer.update()
            posed = evaluated_positions(mesh, sel)
            num = den = 0.0
            for i in sel:
                pred = (rot @ (rest[i] - head)) + head - rest[i]
                act = posed[i] - rest[i]
                num += act.dot(pred)
                den += pred.length_squared
            row[seg] = num / den if den > 1e-12 else None
            bpy.ops.object.mode_set(mode='POSE')
            pb.rotation_euler = (0.0, 0.0, 0.0)
            bpy.ops.object.mode_set(mode='OBJECT')
            bpy.context.view_layer.update()
        results[f] = row
        log("    {:7s} {} {} {}".format(
            f, *(f"{row[s]:6.2f}" if row[s] is not None else "   n/a"
                 for s in ("Root", "Mid", "Tip"))))
    return {f: {s: (round(v, 3) if v is not None else None) for s, v in r.items()}
            for f, r in results.items()}


def render_weight_viz(mesh, side, tag):
    """Heatmap render: vertex colors = segment ownership (Root=red, Mid=green, Tip=blue,
    Wrist/palm=dark gray), emission shading. DIAG-only: replaces the mesh materials."""
    me = mesh.data
    attr = me.color_attributes.get("WeightViz") \
        or me.color_attributes.new("WeightViz", 'FLOAT_COLOR', 'POINT')
    class_color = {"Root": Vector((0.95, 0.08, 0.08)), "Mid": Vector((0.05, 0.85, 0.10)),
                   "Tip": Vector((0.15, 0.35, 1.00)), "Wrist": Vector((0.16, 0.16, 0.16))}
    idx2name = {g.index: g.name for g in mesh.vertex_groups}
    for v in me.vertices:
        c = Vector((0.0, 0.0, 0.0))
        tot = 0.0
        for g in v.groups:
            nm = idx2name[g.group]
            cls = "Wrist" if nm == "Anchor_Wrist" else nm.rsplit("_", 1)[-1]
            c += class_color.get(cls, Vector((1, 0, 1))) * g.weight
            tot += g.weight
        if tot > 1e-6:
            c /= tot
        attr.data[v.index].color = (c.x, c.y, c.z, 1.0)
    mat = bpy.data.materials.get("WeightVizMat") or bpy.data.materials.new("WeightVizMat")
    mat.use_nodes = True
    nt = mat.node_tree
    nt.nodes.clear()
    n_attr = nt.nodes.new("ShaderNodeAttribute")
    n_attr.attribute_name = "WeightViz"
    n_emit = nt.nodes.new("ShaderNodeEmission")
    n_out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(n_attr.outputs["Color"], n_emit.inputs["Color"])
    nt.links.new(n_emit.outputs["Emission"], n_out.inputs["Surface"])
    me.materials.clear()
    me.materials.append(mat)
    render_views(side, tag)


# ---------------------------------------------------------------------------------------
def build_core(src_mesh, arm, joints, wrist, palm, tipends, side):
    """Watertight inset backing core skinned to the same armature (see CORE_* notes)."""
    me = src_mesh.data.copy()
    core = bpy.data.objects.new(f"{NAME}_{side}_core", me)
    bpy.context.scene.collection.objects.link(core)
    bpy.ops.object.select_all(action='DESELECT')
    core.select_set(True)
    bpy.context.view_layer.objects.active = core

    # Voxel remesh -> single watertight manifold surface (merges the 310 shells, no UVs).
    me.remesh_voxel_size = CORE_VOXEL
    me.remesh_voxel_adaptivity = 0.0
    me.use_remesh_fix_poles = True
    bpy.ops.object.voxel_remesh()

    # Shrink along vertex normals so the core sits just inside the textured outer shell.
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.normal_update()
    for v in bm.verts:
        v.co -= v.normal * CORE_INSET
    bm.to_mesh(me)
    bm.free()
    me.update()
    log(f"core {side}: voxel-remeshed watertight, {len(me.vertices)} verts, inset {CORE_INSET*1000:.1f} mm")

    # Skin the core to the same armature (same deterministic proximity skinning).
    skin(core, arm, joints, wrist, palm, tipends)
    return core


# ---------------------------------------------------------------------------------------
def export_fbx(path, meshes, arm):
    bpy.ops.object.select_all(action='DESELECT')
    for m in meshes:
        m.select_set(True)
    arm.select_set(True)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.fbx(
        filepath=path,
        use_selection=True,
        object_types={'ARMATURE', 'MESH'},
        add_leaf_bones=False,
        primary_bone_axis='Y',
        secondary_bone_axis='X',
        axis_forward='-Z',
        axis_up='Y',
        bake_space_transform=True,
        # PRIORITY-1 FIX (armature 100x): Blender's FBX exporter, with the default
        # apply_scale_options='FBX_SCALE_NONE', emits the ARMATURE null with
        # Lcl Scaling = 100 (a cm->m leftover) while leaving bone translations in
        # metres. Unity then imports that 100x onto the whole bone chain, so every
        # Anchor_* ends up with lossyScale == WorldScale*100 — which breaks the mod's
        # palm-anchored card fan and corrupts the skinned bounds. 'FBX_SCALE_ALL'
        # keeps every object transform at scale 1 and pushes the unit conversion into
        # the FBX global UnitScaleFactor (=100 / cm) instead; Unity's useFileScale=true
        # then bakes that 0.01 into the geometry, yielding clean scale-1 transforms and
        # a real-world ~0.19 m hand. Mesh/bone orientation and rolls are unchanged
        # (bake_space_transform stays on), so the rig contract is preserved.
        apply_scale_options='FBX_SCALE_ALL',
        path_mode='COPY' if EMBED_TEX else 'STRIP',
        embed_textures=EMBED_TEX,
        mesh_smooth_type='FACE',
        use_armature_deform_only=False,
        bake_anim=False,
    )
    log("exported", path, f"({os.path.getsize(path)} bytes)")


# ---------------------------------------------------------------------------------------
def axis_check(arm, side):
    """Verify each finger bone's local +X (the runtime curl axis) is perpendicular to
    its finger's plane (spanned by the bone direction and the palm normal): curling then
    stays in-plane and adds NO abduction (the 2026-07 pinky-splay bug). The thumb uses
    the deliberate 45-deg tuck axis, so it is reported but not asserted."""
    ok = True
    log(f"--- CURL-AXIS CHECK ({side}) : angle(local +X, finger-plane normal d x -Y) ---")
    for f in FINGERS:
        angs = []
        for seg in ("Root", "Mid", "Tip"):
            pb = arm.pose.bones[f"Anchor_{f}_{seg}"]
            d = (pb.tail - pb.head).normalized()
            n = d.cross(NEG_Y)
            if n.length < 1e-6:
                angs.append(float("nan"))
                continue
            n.normalize()
            ang = math.degrees(math.acos(max(-1.0, min(1.0, abs(pb.x_axis.dot(n))))))
            angs.append(ang)
            if f != "Thumb" and ang > 2.0:
                ok = False
        tagd = " (tuck axis, informational)" if f == "Thumb" else ""
        log("    {:7s} {}{}".format(
            f, " ".join(f"{a:5.2f}" for a in angs), tagd))
    log(f"    CURL-AXIS: {'OK (all fingers <= 2 deg)' if ok else 'FAIL — curl adds abduction'}")
    return ok


def build_hand(side):
    """side: 'L' or 'R'. Returns (mesh, arm, joints, tipends, pose_results, core)."""
    mesh = import_mesh()
    joints = {f: [v.copy() for v in JOINTS_L[f]] for f in FINGERS}
    wrist, palm, grab, itip = WRIST_L.copy(), PALM_L.copy(), GRAB_L.copy(), INDEXTIP_L.copy()

    if side == 'R':
        mirror_mesh_x(mesh)
        joints = {f: [mirror_x(v) for v in joints[f]] for f in FINGERS}
        wrist, palm, grab, itip = (mirror_x(v) for v in (wrist, palm, grab, itip))

    mesh.name = f"{NAME}_{side}_mesh"
    if WT_ENABLE:
        make_watertight(mesh)   # close AI see-through holes BEFORE skinning (proximity re-skins)
    if REFIT:
        # fit the chains to the ACTUAL mesh digits (fixes mid-finger MCPs on the styled
        # hands + the glove pinky's off-axis chain); runs on the welded mesh, pre-DIP.
        joints, itip = refit_chains(mesh, joints, itip)
    joints, tipends = derive_dip(joints)   # fingertip landmark -> anatomical DIP chains
    arm = build_armature(joints, wrist, palm, grab, itip, f"{NAME}_{side}", tipends)
    axis_check(arm, side)
    skin(mesh, arm, joints, wrist, palm, tipends)
    sample_weights(mesh, joints)
    results = pose_test(mesh, arm, joints, tipends, side)
    core = build_core(mesh, arm, joints, wrist, palm, tipends, side) if CORE_ENABLE else None
    return mesh, arm, joints, tipends, results, core


def render_views(side, tag):
    """Offscreen verification renders of the CURRENT scene (mesh + armature), rest or
    posed. Only runs when RIG_HAND_RENDER_DIR is set — the default build is untouched."""
    world = bpy.data.worlds.get("RigW") or bpy.data.worlds.new("RigW")
    world.use_nodes = True
    world.node_tree.nodes["Background"].inputs[1].default_value = 1.2
    scn = bpy.context.scene
    scn.world = world
    scn.render.engine = 'BLENDER_EEVEE_NEXT'
    scn.render.resolution_x = 640
    scn.render.resolution_y = 640

    cam = bpy.data.objects.get("RigCam")
    if cam is None:
        cam_data = bpy.data.cameras.new("RigCam")
        cam_data.lens = 50
        cam = bpy.data.objects.new("RigCam", cam_data)
        scn.collection.objects.link(cam)
        sun_d = bpy.data.lights.new("RigSun", 'SUN')
        sun_d.energy = 3.0
        sun = bpy.data.objects.new("RigSun", sun_d)
        scn.collection.objects.link(sun)
    scn.camera = cam
    sun = bpy.data.objects["RigSun"]

    ctr = Vector((0, 0, 0.05))
    d = 0.62

    def look_at(o, frm, to):
        o.rotation_euler = (to - frm).to_track_quat('-Z', 'Y').to_euler()

    mirror = -1.0 if side == "R" else 1.0
    for shot, off in {"back": Vector((0, d, 0.08)), "palm": Vector((0, -d, 0.08)),
                      "threeq": Vector((mirror * -d * 0.7, d * 0.7, d * 0.4)),
                      # true profile (pinky side): the only POV where MCP/PIP flexion
                      # angles read unambiguously — fist verification depends on it
                      "side": Vector((mirror * d, 0.0, 0.06))}.items():
        pos = ctr + off
        cam.location = pos
        look_at(cam, pos, ctr)
        sun.location = pos
        look_at(sun, pos, ctr)
        scn.render.filepath = os.path.join(RENDER_DIR, f"{NAME}_{side}_{tag}_{shot}.png")
        bpy.ops.render.render(write_still=True)
        log("render", scn.render.filepath)


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    summary = {}

    sides = (("L", f"{NAME}_L_rig.fbx"),) if DIAG \
        else (("L", f"{NAME}_L_rig.fbx"), ("R", f"{NAME}_R_rig.fbx"))
    for side, fname in sides:
        log(f"==================== BUILD {side} ====================")
        mesh, arm, joints, tipends, results, core = build_hand(side)
        allpass = print_table(results, side)
        summary[side] = (results, allpass)

        if DIAG:
            # Diagnostics-only: weight audit + rigid-follow test + heatmap renders,
            # NO export (the mesh materials get replaced by the viz shading).
            mode = "geo" if GEO_SKIN else "legacy"
            stats = weight_stats(mesh, joints, tipends, f"{NAME} {side} {mode}")
            follow = follow_test(mesh, arm, joints, tipends, f"{NAME} {side} {mode}")
            if RENDER_DIR:
                os.makedirs(RENDER_DIR, exist_ok=True)
                render_weight_viz(mesh, side, f"weights_{mode}")
                with open(os.path.join(RENDER_DIR,
                                       f"{NAME}_{side}_weights_{mode}.json"), "w") as fh:
                    json.dump({"stats": stats, "follow": follow}, fh, indent=1)
            bpy.ops.wm.read_factory_settings(use_empty=True)
            continue
        # ensure rest pose before export
        bpy.context.view_layer.objects.active = arm
        bpy.ops.object.mode_set(mode='POSE')
        for pb in arm.pose.bones:
            pb.rotation_mode = 'XYZ'
            pb.rotation_euler = (0.0, 0.0, 0.0)
        bpy.ops.object.mode_set(mode='OBJECT')

        if RENDER_DIR:
            os.makedirs(RENDER_DIR, exist_ok=True)
            if POSE_MATRIX:
                # Full runtime pose matrix (RIG_HAND_POSES=1): every pose the mod's
                # FingerCurler can produce, with the real per-joint max angles.
                for pose_name, curls in POSES.items():
                    if POSE_ONLY and pose_name not in POSE_ONLY:
                        continue
                    apply_pose(arm, curls)
                    render_views(side, pose_name)
                clear_pose(arm)
            else:
                render_views(side, "rest")
                # fist pose: +40deg on every finger joint (what the pose test asserts)
                bpy.ops.object.mode_set(mode='POSE')
                for f in FINGERS:
                    for seg in ("Root", "Mid", "Tip"):
                        arm.pose.bones[f"Anchor_{f}_{seg}"].rotation_euler = \
                            (math.radians(40.0), 0.0, 0.0)
                bpy.ops.object.mode_set(mode='OBJECT')
                bpy.context.view_layer.update()
                render_views(side, "fist")
                bpy.ops.object.mode_set(mode='POSE')
                for pb in arm.pose.bones:
                    pb.rotation_euler = (0.0, 0.0, 0.0)
                bpy.ops.object.mode_set(mode='OBJECT')
                bpy.context.view_layer.update()

        meshes = [mesh] + ([core] if core else [])
        export_fbx(os.path.join(OUT_DIR, fname), meshes, arm)
        # wipe scene for the next hand
        bpy.ops.wm.read_factory_settings(use_empty=True)

    log("==================== SUMMARY ====================")
    for side in summary:
        results, allpass = summary[side]
        log(f"  {side}: {'ALL PASS' if allpass else 'FAIL'} -> " +
            ", ".join(f"{f}:{results[f][0]}({results[f][1]:+.3f})" for f in FINGERS))


if __name__ == "__main__":
    main()
