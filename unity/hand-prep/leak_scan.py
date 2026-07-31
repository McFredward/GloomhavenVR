# leak_scan.py — Blender headless: hunt for SEE-THROUGH GAPS in a hand shell.
#
# Renders the mesh flat against a MAGENTA sky from a sphere of directions; every magenta pixel
# that a flood fill from the image border cannot reach is a spot where the player looks straight
# through the glove. The 3-D position of each leak is printed so it can be found on the mesh.
#
# By default backface culling is OFF, because that is what the game does: the shipped hand
# material sets _Cull = 0 (BoardLit's hand mode, two-sided lighting included), so a pinhole in
# the near wall shows the inside of the far wall, not the sky. Judging this asset with culling
# ON is far too harsh — it reported 272 leak pixels over 42 views where the game has 60, and all
# 60 are the design gap between the fingers. Pass --cull for the stricter test of the shell
# itself.
#
# RUN:
#   /home/claw/blender-4.2/blender --background --python unity/hand-prep/leak_scan.py -- \
#       <in.fbx> <outdir> <tag> [--n 42] [--res 1000] [--cull]
import bpy, sys, os, math
import numpy as np
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
pos = [a for a in argv if not a.startswith("--")]
SRC, OUTDIR, TAG = pos[0], pos[1], pos[2]


def opt(n, d):
    return argv[argv.index(n) + 1] if n in argv else d


N = int(opt("--n", "42"))
CULL = "--cull" in argv   # OFF by default: the shipped hand material sets _Cull = 0 (two-sided)
RES = int(opt("--res", "1000"))

os.makedirs(OUTDIR, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=SRC)
ob = [o for o in bpy.data.objects if o.type == 'MESH'][0]
for o in list(bpy.data.objects):
    if o.type == 'ARMATURE':
        o.hide_render = True
mw = ob.matrix_world
pts = [mw @ Vector(c) for c in ob.bound_box]
lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
ctr = (lo + hi) * 0.5
sc = max(hi - lo) * 1.05

m = bpy.data.materials.new("flat")
m.use_nodes = True
nt = m.node_tree
nt.nodes.clear()
o_ = nt.nodes.new("ShaderNodeOutputMaterial")
e_ = nt.nodes.new("ShaderNodeEmission")
e_.inputs[0].default_value = (0.05, 0.05, 0.05, 1)
nt.links.new(e_.outputs[0], o_.inputs[0])
m.use_backface_culling = CULL
ob.data.materials.clear()
ob.data.materials.append(m)

scene = bpy.context.scene
scene.render.engine = 'BLENDER_EEVEE_NEXT'
scene.render.resolution_x = scene.render.resolution_y = RES
scene.render.film_transparent = False
scene.view_settings.view_transform = 'Standard'
scene.eevee.taa_render_samples = 1
w = scene.world or bpy.data.worlds.new("w")
scene.world = w
w.use_nodes = True
w.node_tree.nodes["Background"].inputs[0].default_value = (1, 0, 1, 1)

cd = bpy.data.cameras.new("c")
cd.type = 'ORTHO'
cd.ortho_scale = sc
cam = bpy.data.objects.new("c", cd)
scene.collection.objects.link(cam)
scene.camera = cam

# Fibonacci sphere of directions
dirs = []
ga = math.pi * (3 - math.sqrt(5))
for i in range(N):
    z = 1 - 2 * (i + 0.5) / N
    r = math.sqrt(max(0.0, 1 - z * z))
    t = ga * i
    dirs.append(Vector((r * math.cos(t), r * math.sin(t), z)))

from collections import deque

total = 0
worst = []
for i, d in enumerate(dirs):
    up = Vector((0, 0, 1)) if abs(d.z) < 0.95 else Vector((0, 1, 0))
    xc = up.cross(d).normalized()
    yc = d.cross(xc).normalized()
    loc = ctr + d * 2.0
    cam.matrix_world = Matrix(((xc.x, yc.x, d.x, loc.x), (xc.y, yc.y, d.y, loc.y),
                               (xc.z, yc.z, d.z, loc.z), (0, 0, 0, 1)))
    path = os.path.join(OUTDIR, f"{TAG}_scan{i:02d}.png")
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    im = bpy.data.images.load(path)
    a = np.array(im.pixels[:]).reshape(RES, RES, 4)[..., :3]
    bpy.data.images.remove(im)
    mag = (a[..., 0] > 0.5) & (a[..., 1] < 0.2) & (a[..., 2] > 0.5)
    seen = np.zeros_like(mag)
    dq = deque()
    for x in range(RES):
        for y in (0, RES - 1):
            if mag[y, x] and not seen[y, x]:
                seen[y, x] = 1
                dq.append((y, x))
    for y in range(RES):
        for x in (0, RES - 1):
            if mag[y, x] and not seen[y, x]:
                seen[y, x] = 1
                dq.append((y, x))
    while dq:
        y, x = dq.popleft()
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            ny, nx = y + dy, x + dx
            if 0 <= ny < RES and 0 <= nx < RES and mag[ny, nx] and not seen[ny, nx]:
                seen[ny, nx] = 1
                dq.append((ny, nx))
    interior = mag & (seen == 0)
    n = int(interior.sum())
    total += n
    worst.append((n, i, tuple(round(v, 4) for v in d)))
    if n == 0:
        os.remove(path)
    else:
        # where, in 3-D: unproject the interior pixel centroid onto the camera plane
        ys, xs = np.nonzero(interior)
        cx, cy = xs.mean(), ys.mean()
        u = (cx / RES - 0.5) * sc
        v = (cy / RES - 0.5) * sc
        p = ctr + xc * u + yc * v
        print(f"[leak] view {i:02d} dir {tuple(round(c,2) for c in d)}: {n} px near world "
              f"{tuple(round(c*1000,1) for c in p)} mm -> {path}")

worst.sort(reverse=True)
print(f"[leak] TOTAL interior magenta over {N} views at {RES}px (backface culling {'ON' if CULL else 'OFF'}): {total}")
print("[leak] worst views:", worst[:8])
