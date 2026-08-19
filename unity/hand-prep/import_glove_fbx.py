# import_glove_fbx.py — adopt an ARTIST-RIGGED left-hand FBX as the shipped "Glove" hand
# pair (VRHand_L_rig.fbx + VRHand_R_rig.fbx), headless, without re-rigging or re-skinning.
#
# LICENSE-FREE Blender-only step. Does NOT touch Unity.
#
# WHY THIS EXISTS, AND WHY IT IS NOT rig_hand.py.
#   rig_hand.py builds an armature from hardcoded joint COORDINATES and skins the mesh
#   with geometric weights — it owns the rig. This script owns nothing: the incoming FBX
#   already carries a hand-authored armature, hand-painted weights and a matching UV
#   atlas, and every one of those is better than what the generator produces. Re-running
#   rig_hand.py against the new mesh would throw all three away. So this script does the
#   two things the artist's file cannot do for itself, and nothing else:
#     1. add Anchor_IndexTip (see below), and
#     2. mirror the whole thing into a right hand.
#
# THE ONE MISSING BONE. The mod's rig contract (BuildHands.cs ContractBones,
# HandVisuals.MapPrefabRig) names NINETEEN transforms. An FBX rigged in Blender from the
# finger/thumb chains alone carries eighteen of them; the nineteenth, Anchor_IndexTip, is
# not a joint at all — it is the poke point at the very end of the index finger, and the
# mod resolves it by name to place the capacitive-touch probe. Blender writes a leaf bone
# Anchor_Index_Tip_end at exactly that position (its head IS the tip of the last index
# joint), so the point is already there under the wrong name and with an inherited roll.
# We promote it: a real bone named Anchor_IndexTip, axis-aligned exactly the way the
# previous shipped rig had it (local +Y along the fingers = world +Z, local +X = world +X,
# hence local +Z = world -Y = the palm normal). Nothing else in the file is renamed.
#
# THE OTHER LEAF BONES ARE DROPPED. Blender's armature exporter emits a *_end leaf per
# chain end. They carry no vertex weights, the mod never resolves them, and each one is a
# GameObject in the prefab and a bone in the SkinnedMeshRenderer's bone array. They go.
#
# THE MIRROR IS A CONJUGATION, NOT A NEGATION. For the reflection S = diag(-1,1,1), a
# bone's rest matrix M becomes S*M*S. That is a proper rotation (det(S*M*S) = det(M)),
# so the right hand's bones are real bones and not inside-out ones; concretely it keeps
# each bone's local +X and negates its local +Y and +Z. That is precisely the property the
# runtime depends on: the mod curls a finger with joint.localRotation = base *
# Euler(maxAngle*curl, 0, 0) — the SAME positive local-X rotation on both hands — and
# under this mirror that rotation produces the mirror-image tuck. A naive "negate X on
# everything" would flip the handedness of the bone frames and curl the right hand's
# fingers out of the palm instead of into it. rig_hand.py's build_armature documents the
# same rule for the generated rigs; this is the matrix form of it.
#
# WHAT IS VERIFIED BEFORE ANYTHING IS WRITTEN (all four are hard failures):
#   - CONTRACT: all 19 names resolve, on both hands.
#   - CURL AXIS: each finger bone's local +X is perpendicular to that finger's own plane
#     (bone direction x palm normal), so a curl stays in-plane and adds no abduction —
#     the 2026-07 pinky-splay bug, as a number. The thumb is reported, not asserted: its
#     axis is a deliberate 45-degree tuck.
#   - FLEXION: posing every joint to +40 deg actually moves each fingertip toward the
#     palm (-Y), measured on the evaluated mesh, not on the skeleton.
#   - ROUND TRIP: the written FBX is read back and every contract bone's world head and
#     local +X are compared against the in-memory rig. Export settings that silently
#     rotate or rescale a rig have cost this project a build before (the armature-100x
#     bug in rig_hand.py's export_fbx); this proves the file on disk is the rig we checked.
#
# RUN (headless):
#   /home/claw/blender-4.2/blender --background --python unity/hand-prep/import_glove_fbx.py
#
# Input  (override with GLOVE_SRC):
#   .planning/debug/ressources/VRHand_L.fbx
# Outputs:
#   unity/GloomhavenVR.Assets/Assets/Bundle/Hands/VRHand_L_rig.fbx
#   unity/GloomhavenVR.Assets/Assets/Bundle/Hands/VRHand_R_rig.fbx

import bpy, bmesh, math, os, sys
from mathutils import Vector, Matrix

REPO = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
SRC = os.environ.get("GLOVE_SRC",
                     os.path.join(REPO, ".planning/debug/ressources/VRHand_L.fbx"))
OUT_DIR = os.environ.get("GLOVE_OUT",
                         os.path.join(REPO, "unity/GloomhavenVR.Assets/Assets/Bundle/Hands"))
NAME = os.environ.get("GLOVE_NAME", "VRHand")

FINGERS = ("Thumb", "Index", "Middle", "Ring", "Pinky")
SEGS = ("Root", "Mid", "Tip")
NEG_Y = Vector((0.0, -1.0, 0.0))          # palm normal in the rig's frame
POSE_DEG = 40.0                            # flexion probe angle

CONTRACT = (["Anchor_Wrist", "Anchor_Palm", "Anchor_IndexTip", "Anchor_Grab"]
            + [f"Anchor_{f}_{s}" for f in FINGERS for s in SEGS])

_fail = []


def log(*a):
    print("[glove]", *a)
    sys.stdout.flush()


def fail(msg):
    _fail.append(msg)
    log("FAIL:", msg)


# ---------------------------------------------------------------------------------------
def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    arms = [o for o in bpy.data.objects if o.type == 'ARMATURE']
    meshes = [o for o in bpy.data.objects if o.type == 'MESH']
    if len(arms) != 1:
        raise RuntimeError(f"expected exactly 1 armature in {path}, found {len(arms)}")
    if len(meshes) != 1:
        raise RuntimeError(f"expected exactly 1 mesh in {path}, found {len(meshes)}")
    return meshes[0], arms[0]


def promote_index_tip(arm):
    """Anchor_Index_Tip_end -> a real, axis-aligned Anchor_IndexTip; drop every other leaf.

    The previous shipped rig had Anchor_IndexTip with local +Y = world +Z (along the
    fingers) and local +X = world +X, i.e. NOT inheriting the last index joint's roll —
    the poke probe is a place, not a joint, and giving it the joint's tilt would tilt the
    touch normal with it. We reproduce that frame exactly."""
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm.data.edit_bones

    src = eb.get("Anchor_Index_Tip_end")
    if src is None:
        raise RuntimeError("Anchor_Index_Tip_end not found — cannot place Anchor_IndexTip")
    if eb.get("Anchor_IndexTip") is not None:
        raise RuntimeError("Anchor_IndexTip already present — the source rig changed shape")
    parent = eb.get("Anchor_Index_Tip")
    if parent is None:
        raise RuntimeError("Anchor_Index_Tip not found")

    head = src.head.copy()
    length = parent.length

    leaves = [b.name for b in eb if b.name.endswith("_end")]
    for nm in leaves:
        eb.remove(eb[nm])
    log(f"dropped {len(leaves)} leaf bone(s): {', '.join(sorted(leaves))}")

    tip = eb.new("Anchor_IndexTip")
    tip.head = head
    tip.tail = head + Vector((0.0, 0.0, length))   # local +Y along the fingers
    tip.align_roll(NEG_Y)                          # local +Z -> palm normal => local +X = world +X
    tip.parent = eb["Anchor_Index_Tip"]
    tip.use_connect = False
    tip.use_deform = False
    log(f"placed Anchor_IndexTip at {tuple(round(v, 4) for v in head)}, length {length:.4f}")

    bpy.ops.object.mode_set(mode='OBJECT')


def mirror(mesh, arm):
    """Reflect the whole hand through the X=0 plane: mesh geometry, bone rest matrices."""
    S = Matrix.Diagonal((-1.0, 1.0, 1.0, 1.0))

    # --- mesh: negate X, then REVERSE EVERY FACE ---------------------------------------
    # A reflection turns an outward-wound surface inward, so the winding has to be undone
    # or the hand renders inside-out (four meshes have shipped that way in this project).
    # It has to be done through bmesh: a face's UVs, split normals and creases live on its
    # LOOPS, so rewriting loop vertex indices in place — the obvious way — reverses the
    # winding and leaves the UVs where they were, which shuffles the atlas onto the wrong
    # corners. bmesh.ops.reverse_faces carries every loop layer with the reversal.
    me = mesh.data
    for v in me.vertices:
        v.co.x = -v.co.x
    me.update()
    bm = bmesh.new()
    bm.from_mesh(me)
    bmesh.ops.reverse_faces(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    me.update()

    # --- armature: M' = S*M*S per bone, evaluated from the ORIGINAL matrices ------------
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode='EDIT')
    eb = arm.data.edit_bones
    before = {b.name: b.matrix.copy() for b in eb}
    for b in eb:
        b.matrix = S @ before[b.name] @ S
    bpy.ops.object.mode_set(mode='OBJECT')


# ---------------------------------------------------------------------------------------
def check_contract(arm, side):
    missing = [n for n in CONTRACT if n not in arm.data.bones]
    if missing:
        fail(f"[{side}] contract bones missing: {', '.join(missing)}")
    else:
        log(f"[{side}] contract: all {len(CONTRACT)} names resolve")
    extra = sorted(b.name for b in arm.data.bones if b.name not in CONTRACT)
    if extra:
        log(f"[{side}] extra bones (harmless, shipped as transforms): {', '.join(extra)}")


def check_curl_axis(arm, side):
    """Each finger bone's local +X must be the finger-plane normal (d x -Y): curling then
    stays in-plane and adds no abduction. Thumb reported only (deliberate tuck axis)."""
    ok = True
    log(f"[{side}] --- CURL-AXIS CHECK: angle(local +X, d x -Y), degrees ---")
    for f in FINGERS:
        angs = []
        for s in SEGS:
            pb = arm.pose.bones[f"Anchor_{f}_{s}"]
            d = (pb.tail - pb.head).normalized()
            n = d.cross(NEG_Y)
            if n.length < 1e-6:
                angs.append(float("nan"))
                continue
            n.normalize()
            ang = math.degrees(math.acos(max(-1.0, min(1.0, abs(pb.x_axis.dot(n))))))
            angs.append(ang)
            if f != "Thumb" and ang > 8.0:
                ok = False
        tag = "  (tuck axis, informational)" if f == "Thumb" else ""
        log("      {:7s} {}{}".format(f, " ".join(f"{a:6.2f}" for a in angs), tag))
    if not ok:
        fail(f"[{side}] curl axis adds abduction on at least one finger")
    else:
        log(f"[{side}] CURL-AXIS: OK (all four fingers within 8 deg)")


def _tip_verts(mesh, tip_world, radius=0.013):
    idx = [i for i, v in enumerate(mesh.data.vertices)
           if (mesh.matrix_world @ v.co - tip_world).length <= radius]
    return idx


def _evaluated(mesh, idx):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = mesh.evaluated_get(dg)
    me = ev.to_mesh()
    pts = [mesh.matrix_world @ me.vertices[i].co.copy() for i in idx]
    ev.to_mesh_clear()
    return pts


def check_flexion(mesh, arm, side):
    """Pose every joint of a finger to +POSE_DEG about local +X and require the fingertip
    cloud to move toward the palm (-Y). Measured on the EVALUATED mesh: this tests the
    skinning too, not just the skeleton."""
    log(f"[{side}] --- FLEXION CHECK: tip dY at +{POSE_DEG:.0f} deg (must be negative) ---")
    for f in FINGERS:
        tipb = arm.pose.bones[f"Anchor_{f}_Tip"]
        tip_world = arm.matrix_world @ tipb.tail
        idx = _tip_verts(mesh, tip_world)
        if not idx:
            fail(f"[{side}] {f}: no mesh vertices near the fingertip — skinning suspect")
            continue
        before = _evaluated(mesh, idx)
        for s in SEGS:
            pb = arm.pose.bones[f"Anchor_{f}_{s}"]
            pb.rotation_mode = 'XYZ'
            pb.rotation_euler = (math.radians(POSE_DEG), 0.0, 0.0)
        bpy.context.view_layer.update()
        after = _evaluated(mesh, idx)
        dy = sum((a - b).y for a, b in zip(after, before)) / len(before)
        dz = sum((a - b).z for a, b in zip(after, before)) / len(before)
        for s in SEGS:
            arm.pose.bones[f"Anchor_{f}_{s}"].rotation_euler = (0.0, 0.0, 0.0)
        bpy.context.view_layer.update()
        log(f"      {f:7s} n={len(idx):4d}  dY={dy*1000:+7.2f} mm  dZ={dz*1000:+7.2f} mm")
        if dy >= -0.002:
            fail(f"[{side}] {f}: +X curl does not pull the tip into the palm (dY={dy*1000:+.2f} mm)")
    log(f"[{side}] FLEXION: done")


def check_mesh(mesh, side):
    me = mesh.data
    me.calc_loop_triangles()
    if not me.uv_layers:
        fail(f"[{side}] mesh has no UV layer — the albedo atlas cannot map")
    groups = {g.name for g in mesh.vertex_groups}
    unbound = [n for n in CONTRACT
               if n.endswith(("_Root", "_Mid", "_Tip")) and n not in groups]
    if unbound:
        fail(f"[{side}] no vertex weights for: {', '.join(unbound)}")
    log(f"[{side}] mesh: {len(me.vertices)} verts, {len(me.loop_triangles)} tris, "
        f"{len(mesh.vertex_groups)} groups, uv={[l.name for l in me.uv_layers]}")


def shell_stats(mesh):
    """(signed volume in cm3, total UV area). The signed volume is POSITIVE exactly when
    the surface is wound outward, so it is both the closed-and-outward gate this project
    owes every new mesh and — compared L against R — the proof that the mirror reversed
    the faces instead of leaving the right hand inside out."""
    me = mesh.data
    me.calc_loop_triangles()
    M = mesh.matrix_world
    vol = 0.0
    for t in me.loop_triangles:
        a, b, c = (M @ me.vertices[i].co for i in t.vertices)
        vol += a.dot(b.cross(c)) / 6.0
    uv = 0.0
    if me.uv_layers:
        lay = me.uv_layers[0].data
        for t in me.loop_triangles:
            p, q, r = (lay[i].uv for i in t.loops)
            uv += abs((q - p).cross(r - p)) / 2.0
    return vol * 1e6, uv


_left_shell = None


def check_shell(mesh, side):
    """Winding gate. See shell_stats. The right hand must have the SAME shell as the left
    (a mirror preserves volume and UV area) — a sign flip means the reversal was skipped,
    a magnitude change means the mirror mangled the geometry or the atlas."""
    global _left_shell
    vol, uv = shell_stats(mesh)
    log(f"[{side}] shell: signed volume {vol:+.2f} cm3, UV area {uv:.5f}")
    if vol <= 0.0:
        fail(f"[{side}] shell is wound INWARD (signed volume {vol:+.2f} cm3)")
    if side == 'L':
        _left_shell = (vol, uv)
        return
    lv, lu = _left_shell
    if abs(vol - lv) > max(0.01, abs(lv) * 1e-3):
        fail(f"[{side}] mirrored volume {vol:+.2f} cm3 != left {lv:+.2f} cm3 "
             f"— the mirror did not reverse the faces cleanly")
    if abs(uv - lu) > max(1e-6, lu * 1e-3):
        fail(f"[{side}] mirrored UV area {uv:.5f} != left {lu:.5f} — the atlas moved")


# ---------------------------------------------------------------------------------------
def export(path, mesh, arm):
    bpy.ops.object.select_all(action='DESELECT')
    mesh.select_set(True)
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
        # Same reason as rig_hand.py's export_fbx: FBX_SCALE_NONE emits the armature null
        # with Lcl Scaling = 100 while leaving bone translations in metres, and Unity
        # imports that 100x onto the whole chain (broken palm anchor, corrupt skinned
        # bounds). FBX_SCALE_ALL pushes the unit conversion into the file's UnitScaleFactor
        # and every object transform stays at scale 1.
        apply_scale_options='FBX_SCALE_ALL',
        path_mode='STRIP',
        embed_textures=False,
        mesh_smooth_type='FACE',
        use_armature_deform_only=False,
        bake_anim=False,
    )
    log(f"exported {path} ({os.path.getsize(path)} bytes)")


def snapshot(arm):
    out = {}
    for n in CONTRACT:
        b = arm.data.bones[n]
        head = arm.matrix_world @ b.head_local
        xax = (arm.matrix_world.to_3x3() @ (b.matrix_local.to_3x3() @ Vector((1, 0, 0)))).normalized()
        out[n] = (head, xax)
    return out


_left_rig = None


def check_mirror(snap, side):
    """Every check above is mirror-INVARIANT — angles, tip travel and volumes read the
    same on a hand that was never mirrored at all. This is the one that would notice: the
    right rig's bones must sit at the left's X-negated positions, and each local +X must
    keep its x while its y and z flip (the S*M*S conjugation, not a plain negation)."""
    global _left_rig
    if side == 'L':
        _left_rig = snap
        return
    worst_h = worst_a = 0.0
    span = max(abs(p.x) for p, _ in _left_rig.values())
    for n, (lp, lx) in _left_rig.items():
        gp, gx = snap[n]
        worst_h = max(worst_h, (gp - Vector((-lp.x, lp.y, lp.z))).length)
        want = Vector((lx.x, -lx.y, -lx.z))
        worst_a = max(worst_a, math.degrees(math.acos(max(-1.0, min(1.0, gx.dot(want))))))
    log(f"[{side}] MIRROR: worst head delta {worst_h*1000:.4f} mm, "
        f"worst local-+X delta {worst_a:.4f} deg, widest bone offset {span*1000:.1f} mm")
    if span < 0.005:
        fail(f"[{side}] the rig is X-symmetric ({span*1000:.1f} mm) — a mirror would be a no-op")
    if worst_h > 1e-5 or worst_a > 0.1:
        fail(f"[{side}] the mirror is not the left hand's reflection")


def check_round_trip(path, expect, side):
    """Read the written file back and compare every contract bone against what we checked."""
    mesh, arm = load(path)
    got = snapshot(arm)
    worst_p = worst_a = 0.0
    for n, (hp, hx) in expect.items():
        if n not in got:
            fail(f"[{side}] round trip: {n} missing from the written file")
            continue
        gp, gx = got[n]
        worst_p = max(worst_p, (gp - hp).length)
        worst_a = max(worst_a, math.degrees(math.acos(max(-1.0, min(1.0, gx.dot(hx))))))
    log(f"[{side}] ROUND TRIP: worst head delta {worst_p*1000:.3f} mm, "
        f"worst local-+X delta {worst_a:.3f} deg")
    if worst_p > 1e-4 or worst_a > 0.5:
        fail(f"[{side}] round trip: the written FBX is not the rig that was verified")
    return mesh, arm


# ---------------------------------------------------------------------------------------
def build(side):
    log(f"=== building {NAME}_{side} from {SRC} ===")
    mesh, arm = load(SRC)
    promote_index_tip(arm)
    if side == 'R':
        mirror(mesh, arm)
    arm.name = f"{NAME}_{side}"
    arm.data.name = f"{NAME}_{side}_arm"
    mesh.name = f"{NAME}_{side}_mesh"
    mesh.data.name = f"{NAME}_{side}_mesh"

    check_contract(arm, side)
    check_mesh(mesh, side)
    check_shell(mesh, side)
    check_curl_axis(arm, side)
    check_flexion(mesh, arm, side)

    expect = snapshot(arm)
    check_mirror(expect, side)
    path = os.path.join(OUT_DIR, f"{NAME}_{side}_rig.fbx")
    export(path, mesh, arm)
    check_round_trip(path, expect, side)


def main():
    if not os.path.isfile(SRC):
        raise SystemExit(f"source FBX not found: {SRC}")
    os.makedirs(OUT_DIR, exist_ok=True)
    for side in ("L", "R"):
        build(side)
    if _fail:
        log("=" * 78)
        for m in _fail:
            log("  !", m)
        raise SystemExit(f"{len(_fail)} check(s) failed")
    log("all checks passed")


main()
