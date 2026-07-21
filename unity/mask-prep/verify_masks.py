# verify_masks.py — offscreen verification that the prepped Mask_<n>.fbx head-avatars
# have no see-through gaps (the Hunyuan AI-shell crack disease; see prepare_masks.py
# WATERTIGHT pass and unity/hand-prep/rig_hand.py for the hand precedent).
#
# What it does, per mask index:
#   1. imports Assets/Bundle/Head/Mask_<n>.fbx and binds the loose Mask_<n>_albedo.png
#      on an EMISSION material with backface culling OFF — i.e. exactly the in-game
#      look of the unlit, two-sided GloomhavenVR/HeadUnlit shader;
#   2. prints gap stats of the shipped FBX geometry (boundary edges, non-manifold
#      edges, shells) after a 0-distance weld (FBX normal-splitting collapsed, so the
#      numbers reflect real geometric connectivity, not exporter vertex splits);
#   3. renders 4 POVs (front / three-quarter / side / back) with EEVEE_NEXT on a
#      TRANSPARENT film and counts ENCLOSED transparent pixels — background visible
#      *through* the head silhouette = a genuine see-through gap. Border-connected
#      transparency (around the head, through the open back rim seen edge-on) is not
#      counted. PNGs land in <out_dir> for eyeball review.
#
# Usage:
#   blender --background --python verify_masks.py -- <head_dir> <out_dir> [n ...]
#     <head_dir>  unity/GloomhavenVR.Assets/Assets/Bundle/Head
#     <out_dir>   where the verification PNGs go (gitignored dir recommended)
#     [n ...]     mask indices, default 0 1 2
import bpy, bmesh, sys, os
import numpy as np
from mathutils import Vector

argv = sys.argv[sys.argv.index("--")+1:]
head_dir, out_dir = argv[0], argv[1]
indices = [int(a) for a in argv[2:]] or [0, 1, 2]
os.makedirs(out_dir, exist_ok=True)

RES = 900


def gap_stats(me):
    bm = bmesh.new(); bm.from_mesh(me)
    # collapse exporter vertex splits (normals/UV seams) before counting
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-7)
    nb = sum(1 for e in bm.edges if e.is_boundary)
    nm = sum(1 for e in bm.edges if not e.is_manifold)
    parent = list(range(len(bm.verts)))
    def find(a):
        while parent[a] != a:
            parent[a] = parent[parent[a]]; a = parent[a]
        return a
    for e in bm.edges:
        a, b = find(e.verts[0].index), find(e.verts[1].index)
        if a != b: parent[a] = b
    shells = len({find(i) for i in range(len(parent))})
    tris = sum(len(f.verts) - 2 for f in bm.faces)
    bm.free()
    return nb, nm, shells, tris


def enclosed_transparent(png):
    """Count transparent pixels NOT flood-connected to the image border."""
    img = bpy.data.images.load(png)
    w, h = img.size
    px = np.array(img.pixels[:], dtype=np.float32).reshape(h, w, 4)
    bpy.data.images.remove(img)
    transp = px[:, :, 3] < 0.5
    reach = np.zeros_like(transp)
    reach[0, :] = transp[0, :]; reach[-1, :] = transp[-1, :]
    reach[:, 0] |= transp[:, 0]; reach[:, -1] |= transp[:, -1]
    while True:
        grown = reach.copy()
        grown[1:, :] |= reach[:-1, :]
        grown[:-1, :] |= reach[1:, :]
        grown[:, 1:] |= reach[:, :-1]
        grown[:, :-1] |= reach[:, 1:]
        grown &= transp
        if (grown == reach).all():
            break
        reach = grown
    return int((transp & ~reach).sum())


for n in indices:
    name = f"Mask_{n}"
    fbx = os.path.join(head_dir, f"{name}.fbx")
    albedo = os.path.join(head_dir, f"{name}_albedo.png")

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=fbx)
    meshes = [o for o in bpy.context.scene.objects if o.type == 'MESH']
    if not meshes:
        raise SystemExit(f"no mesh in {fbx}")
    bpy.ops.object.select_all(action='DESELECT')
    for o in meshes: o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    if len(meshes) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

    nb, nm, shells, tris = gap_stats(obj.data)
    print(f"VERIFY {name}: tris={tris} boundary_edges={nb} nonmanifold_edges={nm} shells={shells}")

    # in-game look: unlit albedo, two-sided (HeadUnlit equivalent)
    mat = bpy.data.materials.new(name + "_unlit")
    mat.use_nodes = True
    mat.use_backface_culling = False
    nt = mat.node_tree
    nt.nodes.clear()
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    emi = nt.nodes.new("ShaderNodeEmission")
    tex = nt.nodes.new("ShaderNodeTexImage")
    if os.path.exists(albedo):
        tex.image = bpy.data.images.load(albedo)
    nt.links.new(tex.outputs["Color"], emi.inputs["Color"])
    nt.links.new(emi.outputs["Emission"], out.inputs["Surface"])
    obj.data.materials.clear()
    obj.data.materials.append(mat)

    # frame the head: pivot is the eye midpoint, so aim at the bounds center
    ws = [obj.matrix_world @ v.co for v in obj.data.vertices]
    mn = Vector((min(v.x for v in ws), min(v.y for v in ws), min(v.z for v in ws)))
    mx = Vector((max(v.x for v in ws), max(v.y for v in ws), max(v.z for v in ws)))
    ctr = (mn + mx) / 2
    d = max((mx - mn).length, 1e-3) * 1.6

    scn = bpy.context.scene
    scn.render.engine = 'BLENDER_EEVEE_NEXT'
    scn.render.resolution_x = RES; scn.render.resolution_y = RES
    scn.render.film_transparent = True
    cam_data = bpy.data.cameras.new("Cam"); cam_data.lens = 50
    cam = bpy.data.objects.new("Cam", cam_data)
    scn.collection.objects.link(cam); scn.camera = cam

    # Blender FBX import maps the Unity-frame FBX back to Blender canonical:
    # face -> -Y, up -> +Z. POVs named for the face side accordingly.
    shots = {
        "front":  Vector((0, -d, 0.05)),
        "threeq": Vector((-d * 0.7, -d * 0.7, d * 0.4)),
        "side":   Vector((d, 0, 0.05)),
        "back":   Vector((0, d, 0.1)),
    }
    total = 0
    for shot, off in shots.items():
        pos = ctr + off
        cam.location = pos
        cam.rotation_euler = (ctr - pos).to_track_quat('-Z', 'Y').to_euler()
        png = os.path.join(out_dir, f"{name}_{shot}.png")
        scn.render.filepath = png
        bpy.ops.render.render(write_still=True)
        gaps = enclosed_transparent(png)
        total += gaps
        print(f"VERIFY {name} {shot}: enclosed_seethrough_px={gaps} -> {png}")
    print(f"VERIFY {name} TOTAL enclosed_seethrough_px={total}")
