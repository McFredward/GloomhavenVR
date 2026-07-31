# compare_fbx.py — Blender headless: re-import two hand FBXs and diff them, element by element.
#
# WHY: a script's own log is not evidence. An earlier round of this work spent two full cycles
# reporting improvements that were computed on an in-memory bmesh and never written to disk
# (a deleted bm.to_mesh call). The only proof that an edit reached the file is to read the file
# back and compare it with its input.
#
# Prints counts, then max/percentile deltas for vertex positions, UVs, custom split normals and
# skin weights, plus the bone set.
#
# RUN:
#   /home/claw/blender-4.2/blender --background --python unity/hand-prep/compare_fbx.py -- \
#       <a.fbx> <b.fbx>
import bpy, sys
import numpy as np

A, B = sys.argv[sys.argv.index("--") + 1:][:2]


def load(path):
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=path)
    arm = [o for o in bpy.data.objects if o.type == 'ARMATURE'][0]
    ob = [o for o in bpy.data.objects if o.type == 'MESH'][0]
    me = ob.data
    MW = ob.matrix_world
    # Normals must be compared in WORLD space. Exporting with bake_space_transform=True folds
    # the object transform into the vertex data, so the re-imported object carries a different
    # matrix and its OBJECT-space normals are the same normals seen through a 90 deg rotation —
    # comparing those reported "all 28974 normals changed" on a file that had not changed at all.
    NRM = MW.to_3x3().inverted().transposed()
    d = {
        "verts": np.array([list(MW @ v.co) for v in me.vertices]) * 1000.0,
        "uv": np.array([list(l.uv) for l in me.uv_layers.active.data]),
        "nrm": np.array([list(NRM @ l.vector) for l in me.corner_normals]),
        "nface": len(me.polygons),
        "bones": sorted(b.name for b in arm.data.bones),
        "bonepos": {b.name: (tuple(arm.matrix_world @ b.head_local),
                             tuple(arm.matrix_world @ b.tail_local)) for b in arm.data.bones},
        "groups": sorted(g.name for g in ob.vertex_groups),
        "wsum": np.array([sum(g.weight for g in v.groups) for v in me.vertices]),
    }
    d["nrm"] /= np.maximum(np.linalg.norm(d["nrm"], axis=1, keepdims=True), 1e-20)
    return d


a, b = load(A), load(B)
print(f"[cmp] A {A}")
print(f"[cmp] B {B}")
print(f"[cmp] verts {len(a['verts'])} vs {len(b['verts'])}, faces {a['nface']} vs {b['nface']}, "
      f"loops {len(a['uv'])} vs {len(b['uv'])}, bones {len(a['bones'])} vs {len(b['bones'])}")
ok = True
if len(a["verts"]) != len(b["verts"]) or len(a["uv"]) != len(b["uv"]):
    print("[cmp] COUNTS DIFFER — cannot compare element-wise")
    raise SystemExit(1)
if a["bones"] != b["bones"]:
    print("[cmp] BONE SET DIFFERS")
    ok = False
bd = max(max(np.linalg.norm(np.array(a["bonepos"][n][0]) - np.array(b["bonepos"][n][0])),
             np.linalg.norm(np.array(a["bonepos"][n][1]) - np.array(b["bonepos"][n][1])))
         for n in a["bonepos"]) * 1000.0
dv = np.linalg.norm(a["verts"] - b["verts"], axis=1)
du = np.linalg.norm(a["uv"] - b["uv"], axis=1)
dn = np.degrees(np.arccos(np.clip((a["nrm"] * b["nrm"]).sum(1), -1, 1)))
dw = np.abs(a["wsum"] - b["wsum"])
print(f"[cmp] bone head/tail max drift {bd:.6f} mm")
print(f"[cmp] vertex positions: {int((dv > 1e-6).sum())} moved, max {dv.max():.6f} mm, "
      f"p99 {np.percentile(dv, 99):.6f} mm")
print(f"[cmp] UVs: {int((du > 0).sum())} changed, max {du.max():.3e}, "
      f"max in texels at 2048 {du.max() * 2048:.4f}")
# A no-op export/import round-trip already perturbs every normal by up to 0.47 deg — the FBX
# stores them quantised. Anything under NOISE_DEG is that, not an edit.
NOISE_DEG = 0.5
real = dn > NOISE_DEG
print(f"[cmp] custom normals: {int(real.sum())} of {len(dn)} changed by more than "
      f"{NOISE_DEG} deg (the file-format noise floor); {int((dn <= NOISE_DEG).sum())} within it. "
      f"Of the real changes: p50 {np.median(dn[real]) if real.any() else 0:.2f} deg, "
      f"p95 {np.percentile(dn[real], 95) if real.any() else 0:.2f} deg, max {dn.max():.2f} deg")
print(f"[cmp] skin weight sums: max delta {dw.max():.6f}; "
      f"vertex groups {'same' if a['groups'] == b['groups'] else 'DIFFER'}")
print("[cmp] verdict:", "OK" if ok else "PROBLEM")
