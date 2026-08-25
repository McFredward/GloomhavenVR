#!/usr/bin/env python3
"""gen_rimao.py -- bake the board's own AMBIENT OCCLUSION into ATLAS SPACE.

    /home/claw/blender-4.2/blender --background --factory-startup \
        --python unity/board-prep/gen_rimao.py -- <fbx> <out.png> [samples]

WHY, AND WHY ONLY THE RIM USES IT
---------------------------------
`tex_composite` builds the front face as `albedo = material * AO^gamma`, with the AO from a
real cavity pass -- which is why every moulding and recess on the front reads as depth even
under BoardLit's flat baked key.  The RIM has never had that term: `tex_backfill` blends
the two faces' materials across the band and multiplies by nothing.

That did not matter while the rim was one flat wall.  ModBuild 280 cuts a 2 mm rebate into
it, and a 2 mm step whose walls sit 24 degrees off the thickness axis (the limit
MAX_RIM_TILT_DEG imposes, see BOARD-CONTRACT.md) changes the surface normal by 24 degrees
and no more.  Against two baked light directions that is a real but quiet tonal step -- it
reads at a corner and it nearly disappears flat-on.  Occlusion is the term that does not
depend on the light direction, and it is the one the front already has.

A BAKE, NOT A FORMULA.  The obvious cheap version is an analytic cavity term: signed
distance to the nominal rounded-rect silhouette, darkened by depth.  That would be a
picture of what I believe the profile to be, and it would be blind to everything the
profile does not describe -- oak's battens shading their own flanks, the rails shading the
core beside them, the corners where the rim turns.  Cycles renders the actual mesh, so the
number comes out of the geometry that is really there.

THE OUTPUT IS RAW AO AT FULL RANGE.  Normalising it against the side strip's own median,
so only the RELATIVE cavity is applied and the band's overall brightness is untouched, is
the caller's job (img2img/tex_rimao.py) -- the slab's two big faces darken their whole rim
by a constant that has nothing to do with this round's relief, and folding that constant in
here would make the sides darker for no reason a viewer could name.
"""

import sys

import bpy

argv = sys.argv[sys.argv.index("--") + 1:]
src, dst = argv[0], argv[1]
samples = int(argv[2]) if len(argv) > 2 else 64
distance = float(argv[3]) if len(argv) > 3 else 0.010   # metres; see the gather-distance note
N = 2048

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system = 'METRIC'
bpy.context.scene.unit_settings.scale_length = 1.0
bpy.ops.import_scene.fbx(filepath=src)

meshes = [o for o in bpy.data.objects if o.type == 'MESH']
assert len(meshes) == 1, "expected exactly one mesh, found %d" % len(meshes)
ob = meshes[0]

img = bpy.data.images.new("AO", N, N, alpha=False, float_buffer=False)
mat = bpy.data.materials.new("AOBake")
mat.use_nodes = True
nt = mat.node_tree
# The bake target is whichever image texture node is ACTIVE on the material, so the node
# has to exist and be selected even though nothing is linked to it.
tex = nt.nodes.new("ShaderNodeTexImage")
tex.image = img
nt.nodes.active = tex
tex.select = True
ob.data.materials.clear()
ob.data.materials.append(mat)

sc = bpy.context.scene
sc.render.engine = 'CYCLES'
# THE GATHER DISTANCE IS THE WHOLE MEASUREMENT, and the default is useless here.  Cycles'
# AO bake integrates over an UNBOUNDED hemisphere unless told otherwise, and a 2 mm rebate
# on a 640 x 320 x 36 mm slab blocks almost none of that: measured on oak at the default,
# the rails came back 0.9981 and the rebate floor 0.9834 -- a 1.5 % difference, invisible
# through any shader, and a thumbnail of it looks like a perfectly plausible white map.
# A cavity term is a LOCAL question ("how enclosed is this point at the scale of the
# feature?"), so the gather distance is set to the scale of the features being asked about.
if sc.world is None:
    sc.world = bpy.data.worlds.new("W")
sc.world.light_settings.distance = distance
sc.cycles.samples = samples
sc.cycles.use_denoising = False
sc.render.bake.use_selected_to_active = False
sc.render.bake.margin = 16           # dilate past every island edge, so a bilinear fetch
                                     # at the seam never picks up the empty background
# The bake operator walks the SELECTION, and the FBX carries ten anchor EMPTIES; with any
# of them selected it aborts with 'Object "Slot1" is not a mesh'.  Deselect everything
# first rather than assuming the import left a clean selection.
for o in bpy.data.objects:
    o.select_set(False)
bpy.context.view_layer.objects.active = ob
ob.select_set(True)
bpy.ops.object.bake(type='AO')

img.filepath_raw = dst
img.file_format = 'PNG'
img.save()
print("[gen_rimao] %s -> %s  (%d samples, %d^2, gather %.1f mm)"
      % (src, dst, samples, N, distance * 1000.0))
