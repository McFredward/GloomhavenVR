# rig_hand.py — Blender headless rig of the AI-generated left-hand glove mesh into a
# bundle-ready SKINNED FBX pair (left + mirrored right) for GloomhavenVR's FingerCurler.
#
# LICENSE-FREE Blender-only step. Does NOT touch Unity.
#
# Mirrors the unity/board-prep/prepare_playtray.py convention: one reproducible headless
# script, run with Blender 4.2. It turns ressources/hands/Hand_prepped.glb (root at wrist,
# +Z along fingers, +Y = back of hand, palm at -Y, ~0.19 m, ~18k tris) into a 16-bone
# armature (Anchor_Wrist + 5 fingers x 3 joints) plus three anchor empties (Anchor_Palm,
# Anchor_IndexTip, Anchor_Grab), skins it with automatic weights, verifies the flexion
# invariant numerically, then mirrors to a right hand and exports both FBX files.
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

import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix
from mathutils.kdtree import KDTree

SRC = "/home/claw/gloomhaven_vr/ressources/hands/Hand_prepped.glb"
# OUT_DIR: overridable via env so this can target an isolated worktree without
# touching the main checkout. Default remains the main checkout (unchanged behaviour).
OUT_DIR = os.environ.get(
    "RIG_HAND_OUT_DIR",
    "/home/claw/gloomhaven_vr/unity/GloomhavenVR.Assets/Assets/Bundle/Hands")
NEG_Y = Vector((0.0, -1.0, 0.0))  # palm-out / flexion reference

FINGERS = ["Thumb", "Index", "Middle", "Ring", "Pinky"]

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
CAP_UV = (0.3672, 0.0391)


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
        res = bmesh.ops.holes_fill(bm, edges=bm.edges, sides=0)
        uv_fixed += _fix_new_face_uvs(bm, res.get("faces", []))
        bnd = [v for v in bm.verts if any(e.is_boundary for e in v.link_edges)]
        if bnd:
            bmesh.ops.remove_doubles(bm, verts=bnd, dist=WT_BWELD)
        res = bmesh.ops.holes_fill(bm, edges=[e for e in bm.edges if e.is_boundary], sides=0)
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
    if wrist_open == 0:
        raise SystemExit("watertight: wrist rim got sealed by holes_fill — it must stay open "
                         "(no cap); loosen WT_BWELD or exclude the wrist band from step 2")
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
def build_armature(joints, wrist, palm, grab, indextip, name):
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
        p = joints[finger]                          # [Root, Mid, Tip]
        dir_tip = (p[2] - p[1]).normalized()
        heads = [p[0], p[1], p[2]]
        tails = [p[1], p[2], p[2] + dir_tip * TIP_TAIL_LEN]
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


def skin(mesh, arm, joints, wrist, palm):
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
        dir_tip = (p[2] - p[1]).normalized()
        finger_segs[f] = [
            (f"Anchor_{f}_Root", p[0], p[1]),
            (f"Anchor_{f}_Mid",  p[1], p[2]),
            (f"Anchor_{f}_Tip",  p[2], p[2] + dir_tip * TIP_TAIL_LEN),
        ]

    # ensure a vertex group per deform bone + wrist
    names = ["Anchor_Wrist"] + [f"Anchor_{f}_{s}" for f in FINGERS for s in ("Root", "Mid", "Tip")]
    groups = {}
    for n in names:
        groups[n] = mesh.vertex_groups.get(n) or mesh.vertex_groups.new(name=n)

    P = 2.0            # gentler than the old cubic -> wider, smoother influence bands
    EPS = 1e-9
    verts = mesh.data.vertices
    nV = len(verts)

    # ---- pass 1: raw proximity weights (dict per vertex) --------------------------------
    vert_w = [dict() for _ in range(nV)]
    for v in verts:
        co = v.co
        dW = _seg_dist(co, *wrist_seg)
        # nearest finger by its closest segment
        best_f, best_d = None, 1e9
        for f in FINGERS:
            dmin = min(_seg_dist(co, a, b) for _, a, b in finger_segs[f])
            if dmin < best_d:
                best_d, best_f = dmin, f
        cands = [("Anchor_Wrist", dW)]
        for nm, a, b in finger_segs[best_f]:
            cands.append((nm, _seg_dist(co, a, b)))
        raws = [(nm, 1.0 / (d ** P + EPS)) for nm, d in cands]
        tot = sum(w for _, w in raws)
        vert_w[v.index] = {nm: w / tot for nm, w in raws}

    # ---- pass 2: position-based Laplacian smoothing (blend across shells) ---------------
    kd = KDTree(nV)
    for v in verts:
        kd.insert(v.co, v.index)
    kd.balance()

    RADIUS = 0.007     # 7 mm: within a finger, below the ~20 mm inter-finger spacing
    ALPHA = 0.5        # blend toward the neighbourhood average
    ITERS = 3
    for _ in range(ITERS):
        smoothed = [None] * nV
        for v in verts:
            acc = dict(vert_w[v.index])          # start from self
            near = kd.find_range(v.co, RADIUS)
            cnt = 0
            for (_co, ni, _d) in near:
                if ni == v.index:
                    continue
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

    # ---- write: clamp to <=4 influences, renormalise ------------------------------------
    for v in verts:
        items = sorted(vert_w[v.index].items(), key=lambda kv: -kv[1])[:4]
        tot = sum(w for _, w in items) or 1.0
        for nm, w in items:
            wn = w / tot
            if wn > 1e-4:
                groups[nm].add([v.index], wn, 'REPLACE')

    # armature modifier + parent (no auto weights)
    mesh.parent = arm
    mesh.matrix_parent_inverse = arm.matrix_world.inverted()
    if not any(m.type == 'ARMATURE' for m in mesh.modifiers):
        m = mesh.modifiers.new("Armature", 'ARMATURE')
        m.object = arm


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


def pose_test(mesh, arm, joints, side_label):
    """Rotate each finger's 3 joints +40 deg about local X; assert fingertip moves -Y."""
    results = {}
    bpy.context.view_layer.objects.active = arm
    for finger in FINGERS:
        tip_pos = joints[finger][2]
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
        curl_ok = dy < -0.003
        column_ok = abs(dy) > dx * 0.8
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
def build_core(src_mesh, arm, joints, wrist, palm, side):
    """Watertight inset backing core skinned to the same armature (see CORE_* notes)."""
    me = src_mesh.data.copy()
    core = bpy.data.objects.new(f"VRHand_{side}_core", me)
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
    skin(core, arm, joints, wrist, palm)
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
        path_mode='COPY',
        embed_textures=True,
        mesh_smooth_type='FACE',
        use_armature_deform_only=False,
        bake_anim=False,
    )
    log("exported", path, f"({os.path.getsize(path)} bytes)")


# ---------------------------------------------------------------------------------------
def build_hand(side):
    """side: 'L' or 'R'. Returns (mesh, arm, joints, pose_results)."""
    mesh = import_mesh()
    joints = {f: [v.copy() for v in JOINTS_L[f]] for f in FINGERS}
    wrist, palm, grab, itip = WRIST_L.copy(), PALM_L.copy(), GRAB_L.copy(), INDEXTIP_L.copy()

    if side == 'R':
        mirror_mesh_x(mesh)
        joints = {f: [mirror_x(v) for v in joints[f]] for f in FINGERS}
        wrist, palm, grab, itip = (mirror_x(v) for v in (wrist, palm, grab, itip))

    mesh.name = f"VRHand_{side}_mesh"
    if WT_ENABLE:
        make_watertight(mesh)   # close AI see-through holes BEFORE skinning (proximity re-skins)
    arm = build_armature(joints, wrist, palm, grab, itip, f"VRHand_{side}")
    skin(mesh, arm, joints, wrist, palm)
    sample_weights(mesh, joints)
    results = pose_test(mesh, arm, joints, side)
    core = build_core(mesh, arm, joints, wrist, palm, side) if CORE_ENABLE else None
    return mesh, arm, joints, results, core


def main():
    os.makedirs(OUT_DIR, exist_ok=True)
    summary = {}

    for side, fname in (("L", "VRHand_L_rig.fbx"), ("R", "VRHand_R_rig.fbx")):
        log(f"==================== BUILD {side} ====================")
        mesh, arm, joints, results, core = build_hand(side)
        allpass = print_table(results, side)
        summary[side] = (results, allpass)
        # ensure rest pose before export
        bpy.context.view_layer.objects.active = arm
        bpy.ops.object.mode_set(mode='POSE')
        for pb in arm.pose.bones:
            pb.rotation_mode = 'XYZ'
            pb.rotation_euler = (0.0, 0.0, 0.0)
        bpy.ops.object.mode_set(mode='OBJECT')
        meshes = [mesh] + ([core] if core else [])
        export_fbx(os.path.join(OUT_DIR, fname), meshes, arm)
        # wipe scene for the next hand
        bpy.ops.wm.read_factory_settings(use_empty=True)

    log("==================== SUMMARY ====================")
    for side in ("L", "R"):
        results, allpass = summary[side]
        log(f"  {side}: {'ALL PASS' if allpass else 'FAIL'} -> " +
            ", ".join(f"{f}:{results[f][0]}({results[f][1]:+.3f})" for f in FINGERS))


if __name__ == "__main__":
    main()
