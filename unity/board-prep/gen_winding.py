"""gen_winding.py — how many faces of a board are wound against the outside?

    /home/claw/blender-4.2/blender --background --factory-startup \
        --python unity/board-prep/gen_winding.py -- <fbx> [<fbx> ...]

WHY THIS EXISTS. `gen_stats.py` answers "is the mesh closed" — 0 boundary edges, 0 non-manifold
edges, 0 loose verts — and all three boards pass it. That is TOPOLOGY, and it is silent about
ORIENTATION: a face can sit in a perfectly closed shell and still face inward. With the shipped
material's `Cull Off` such a face is invisible, because BoardLit flips the normal on a back face
(VFACE) and lights it; switch to `Cull Back` and it vanishes, showing whatever is behind it.

That is not hypothetical here. Rendering the built prefabs with `_Cull = 2` and diffing against the
shipped `_Cull = 0` changes 83 / 5 / 6 INTERIOR pixels on oak / steel / bronze (Editor/PreviewBoard
CullBackCheck, 2026-08-25) — pixels with board on all eight sides, so not a silhouette seam. Closed
shells, a few inverted faces. This file counts them instead of leaving "a few" as the number.

HOW. `bmesh.ops.recalc_face_normals` makes the winding consistently outward on a closed shell; the
faces whose vertex order it had to reverse are exactly the ones that were wrong. Comparing each
face's normal before and after is therefore the measurement, not an estimate of it. Signed volume
is reported alongside as the sanity check that "outward" means what it should: a solid wound
outward has positive signed volume, and a wholly inverted one would come out negative, which would
mean recalc had flipped the entire mesh and the per-face count was measuring the opposite thing.
"""
import os
import sys

import bmesh
import bpy
from mathutils import Vector

paths = sys.argv[sys.argv.index("--") + 1:]
if not paths:
    raise SystemExit("usage: ... gen_winding.py -- <fbx> [<fbx> ...]")

for p in paths:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=p)
    for ob in bpy.data.objects:
        if ob.type != "MESH":
            continue
        bm = bmesh.new()
        bm.from_mesh(ob.data)
        bm.faces.ensure_lookup_table()
        before = [f.normal.copy() for f in bm.faces]

        # Signed volume of the shell as authored, via the divergence theorem over triangle fans.
        def signed_volume(mesh):
            v = 0.0
            for f in mesh.faces:
                vs = f.verts
                for i in range(1, len(vs) - 1):
                    a, b, c = vs[0].co, vs[i].co, vs[i + 1].co
                    v += a.dot(b.cross(c)) / 6.0
            return v

        vol_before = signed_volume(bm)
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        bm.faces.ensure_lookup_table()
        vol_after = signed_volume(bm)

        flipped = 0
        worst_area = 0.0
        total_area = 0.0
        flipped_area = 0.0
        for i, f in enumerate(bm.faces):
            total_area += f.calc_area()
            if before[i].dot(f.normal) < 0.0:
                flipped += 1
                a = f.calc_area()
                flipped_area += a
                worst_area = max(worst_area, a)

        n = len(bm.faces)
        print(f"{os.path.basename(p)} :: {ob.name}")
        print(f"   faces {n}   INWARD-WOUND {flipped}  ({100.0 * flipped / max(n, 1):.3f} %)")
        print(f"   flipped area {flipped_area * 1e6:.1f} mm^2 of {total_area * 1e6:.1f} mm^2 "
              f"({100.0 * flipped_area / max(total_area, 1e-12):.4f} %), "
              f"largest single face {worst_area * 1e6:.2f} mm^2")
        print(f"   signed volume {vol_before * 1e6:.1f} cm^3 -> {vol_after * 1e6:.1f} cm^3 after recalc "
              f"({'OK: outward already' if vol_before > 0 else 'ALARM: the shell was inside-out'})")
        bm.free()
