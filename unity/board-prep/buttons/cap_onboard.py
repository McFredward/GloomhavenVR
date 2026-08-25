"""ROUND 4 — render the caps IN THEIR OWN SEATS, ON THEIR OWN BOARD.

    /home/claw/blender-4.2/blender --background --python cap_onboard.py -- \
        <style> <before|after> <meshdir> <out.png> [--res 1536] [--yaw 35] [--pitch 50]

WHY THIS EXISTS AND WHY IT IS THE ONLY PICTURE WORTH ARGUING ABOUT
-------------------------------------------------------------------
`plate_forensics.py` is a good instrument for the question it answers — "does this picture have an
inside-outside order at all" — and round 3 moved it from REG 3.7/9.9/3.8 to 28.3/35.3/23.7 and was
rejected anyway. It cannot see what the picture DEPICTS, and it cannot see the only thing the user
is complaining about this round, which is whether the button BELONGS TO THAT BOARD. **A rim in the
wrong place scores exactly like a rim in the right place.**

The `Assets/Editor/PreviewKeycaps.cs` station renders a cap alone on grey at 420 px, which answers
"is this cap well made" and cannot answer "is this cap of this board" either. So this renders the
cap where it actually is: seated in its own recess, at its fitted size, on the board's own surface,
at the angle a player looks down at a tray lying flat.

THE MESHES ARE THE SHIPPED ONES, NOT A REPLICA
-----------------------------------------------
`Assets/Editor/ExportCapMeshes.cs` reflects into the BUILT GloomhavenVR.dll and calls the real
`CardMesh.BuildBeveledKeycap` / `BuildRoundKeycap`, then writes OBJ. Nothing here re-implements a
cap; this script only places what that exported. That matters because the alternative — a third
hand-port of the builder, after the C# and `PreviewKeycaps.cs`'s port — is three chances to draw a
picture of something the game does not build, and this project has already lost rounds to exactly
that ("A picture that cannot show the thing is not evidence either way").

THE SEATS ARE MEASURED, NOT PLACED BY EYE
------------------------------------------
`ButtonSeat1..3` are real empties baked into every board FBX by the assembler (`gen_board.py` writes
`self.anchors["ButtonSeat%d"]` at the recess FLOOR, and `Cards/BoardAnchors.FindSeat` reads them at
runtime). Their XY is used directly. Their Z is NOT: it is re-derived by ray-casting the board mesh
at that XY, because the FBX importer's axis conversion is a convention and a convention is a thing
to check rather than to assume — placing a cap 20 mm inside a plank looks, in a small render, a lot
like placing it correctly.

THE SHADING IS `unity/asset-preview/render_asset.py`'s, TERM FOR TERM
----------------------------------------------------------------------
Same `BLENDER_EEVEE_NEXT`, same ortho camera, same two baked studio directions at the same gains,
same `_Ambient` 0.5, same 'Standard' view transform. The board and the caps are rendered by ONE
renderer under ONE light in ONE projection — a composite of a Blender board and a Unity cap would
have put two different shading models in one picture and invited an argument about which half was
lying.

The caps are shaded from their GEOMETRIC normals with no normal map, and that is the point of the
round rather than a shortcut: since ModBuild 290 the relief IS geometry, so a picture that needed a
normal map to show the bezel would be showing something the mesh does not have.
"""
import math
import os
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
pos = [a for a in argv if not a.startswith("--")]


def opt(flag, default):
    return argv[argv.index(flag) + 1] if flag in argv else default


STYLE, PASS, MESHDIR, OUT = pos[0], pos[1], pos[2], pos[3]
RES = int(opt("--res", "1536"))
YAW = float(opt("--yaw", "35"))
PITCH = float(opt("--pitch", "50"))
SCALE = float(opt("--scale", "0.78"))

HERE = os.path.dirname(os.path.abspath(__file__))
BUNDLE = os.path.abspath(os.path.join(HERE, "../../GloomhavenVR.Assets/Assets/Bundle/Table"))

# oak = PlayTray_prepped, steel = 9capjqp6, bronze = 16vm268h (BoardFrame.cs:68-69,
# VRCardFactory.cs:29-30). Swapped once before; taken from the source, not from the look.
BOARDS = {
    "oak":    ("PlayTray_prepped.fbx", "PlayTray_albedo.png", "PlayTray_normal.png", "Oak"),
    "steel":  ("PlayTray_9capjqp6.fbx", "PlayTray_9capjqp6_albedo.png",
               "PlayTray_9capjqp6_normal.png", "Steel"),
    "bronze": ("PlayTray_16vm268h.fbx", "PlayTray_16vm268h_albedo.png",
               "PlayTray_16vm268h_normal.png", "Bronze"),
}

# Cards/PlayTray.6.Build.BoardIdleColor — the per-board idle FACE colour, and
# Defaults.WorldUI BoardCapTint* = 0.5, the tint the user's config ships at.
IDLE = {"oak": (0.550, 0.514, 0.564), "steel": (0.407, 0.541, 0.607),
        "bronze": (0.753, 0.471, 0.224)}
CAP_TINT = 0.5

# Cards/PlayTray.7.Nested BevelTint / WallTint, verbatim (PreviewKeycaps.cs carries the same set).
BEVEL_HL, BEVEL_LERP = (0.66, 0.53, 0.32), 0.48
WALL_FACTOR, WALL_WARM, WALL_LERP = 0.50, (0.17, 0.11, 0.06), 0.42
# WorldUI/ButtonTuning SeatedCapColor: the face is floored against its own well so a dark state
# colour cannot sink below the recess it sits in.
WELL, SEAT_CONTRAST = (0.15, 0.12, 0.08), 1.35

# unity/asset-preview/render_asset.py, verbatim — BoardLit's two baked studio directions.
GAME_KEY = (0.35, -0.45, 0.85)
GAME_FILL = (-0.55, 0.30, 0.35)
GAME_KEY = tuple(c / math.sqrt(sum(v * v for v in GAME_KEY)) for c in GAME_KEY)
GAME_FILL = tuple(c / math.sqrt(sum(v * v for v in GAME_FILL)) for c in GAME_FILL)
AMBIENT = 0.5

# Assets/Editor/PreviewKeycaps.cs Cell(): a 4x4 atlas with a 2-texel inset, rows counted from the
# BOTTOM. Cards/CapCellMath owns this arithmetic and the wire suite pins it.
GRID_COLS = GRID_ROWS = 4
INSET_TEXELS = 2
CELL_PLAIN, CELL_CONFIRM, CELL_UNDO, CELL_SKIP = 0, 1, 2, 3
CELL_SHORT_REST = 5

# Which control sits in which seat, top to bottom — Cards/PlayTray.BuildButtons' order
# (Confirm/Use, Undo, Skip), so seat 1 never moves.
SEAT_ROLE = {1: CELL_CONFIRM, 2: CELL_UNDO, 3: CELL_SKIP}


def log(*a):
    print("[onboard]", *a)
    sys.stdout.flush()


def lerp3(a, b, t):
    return tuple(a[i] + (b[i] - a[i]) * t for i in range(3))


def bevel_tint(top):
    return lerp3(top, BEVEL_HL, BEVEL_LERP)


def wall_tint(top):
    return lerp3(tuple(c * WALL_FACTOR for c in top), WALL_WARM, WALL_LERP)


def seated(c):
    """WorldUI.ButtonTuning.SeatedCapColor — floor the face against its own well."""
    return tuple(max(c[i], WELL[i] * SEAT_CONTRAST) for i in range(3))


def cell_st(w, h, idx):
    col, row = idx % GRID_COLS, idx // GRID_COLS
    row_from_bottom = GRID_ROWS - 1 - row
    cw, ch = w / GRID_COLS, h / GRID_ROWS
    iu, iv = INSET_TEXELS / w, INSET_TEXELS / h
    return ((cw / w - 2 * iu, ch / h - 2 * iv),
            (col * cw / w + iu, row_from_bottom * ch / h + iv))


# =========================================================================================
# THE OBJ READER. Written here rather than using bpy.ops.wm.obj_import because the ONE thing
# that must not be left to a convention is the HANDEDNESS.
#
# Unity is LEFT-handed (+X right, +Y up, +Z away from the viewer) and Blender is RIGHT-handed
# Z-up. Copying vertices across verbatim mirrors the space, which silently inverts every
# triangle's facing — so a mesh that was just proved correctly wound in Unity would render
# inside-out here and the fix would look like a winding bug that is not there.
#
# Negating exactly one axis converts handedness. Unity (x, y, z) -> Blender (x, y, -z) does it
# AND puts the cap's own face (Unity -Z, toward the viewer) at Blender +Z, i.e. up, which is
# where a tray lying flat wants it.
# =========================================================================================
def read_obj(path):
    verts, uvs, groups = [], [], []
    cur = None
    with open(path) as fh:
        for line in fh:
            p = line.split()
            if not p:
                continue
            if p[0] == "v":
                verts.append((float(p[1]), float(p[2]), -float(p[3])))
            elif p[0] == "vt":
                uvs.append((float(p[1]), float(p[2])))
            elif p[0] == "g":
                cur = (p[1], [])
                groups.append(cur)
            elif p[0] == "f":
                if cur is None:
                    cur = ("default", [])
                    groups.append(cur)
                idx = [int(t.split("/")[0]) - 1 for t in p[1:]]
                uvi = [int(t.split("/")[1]) - 1 for t in p[1:]]
                # REVERSE THE WINDING, because negating z above mirrored the space.
                #
                # Converting handedness by negating one axis flips the sign of every cross
                # product, so a triangle that was front-facing in Unity is back-facing here. With
                # `use_backface_culling = True` — which these materials must have, since every cap
                # material in the mod is `new Material(BoardLit)` and keeps _Cull = Back — that
                # renders the cap's INTERIOR and culls its outside.
                #
                # It was caught by looking: the first render with an undercut skirt showed the
                # board's seat pocket straight through the cap. Before the undercut the cap was a
                # simple convex box and its inside looked enough like its outside to pass, which is
                # the whole reason this comment exists rather than a shrug — three earlier pictures
                # in this round were of the far interior and read as fine.
                cur[1].append((idx[::-1], uvi[::-1]))
    return verts, uvs, groups


def board_material(name, albedo_path, normal_path):
    """render_asset.py's LIT branch: Lambert against two baked directions + _Ambient, times the
    albedo. No --cull: every tray material carries _Cull: 0 and copying the hands' line is wrong."""
    img = bpy.data.images.load(albedo_path)
    img.colorspace_settings.name = 'sRGB'
    nimg = None
    if normal_path and os.path.exists(normal_path):
        nimg = bpy.data.images.load(normal_path)
        nimg.colorspace_settings.name = 'Non-Color'
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    mat.use_backface_culling = False
    mat.blend_method = 'OPAQUE'
    nt = mat.node_tree
    nt.nodes.clear()
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    _shade_chain(nt, tex.outputs["Color"], nimg)
    return mat


def flat_material(name, rgb, tex_img=None, st=None):
    """A cap submesh: one flat state colour, optionally modulated by an atlas CELL.

    BoardLit does `alb = tex2D(_MainTex, uv) * _Color`, so the texture MODULATES the state colour
    — the same order used here. A cap with no atlas is exactly the colour with no texture, which
    is the mod's own fallback path and not a stand-in invented for this picture."""
    mat = bpy.data.materials.new(name)
    mat.use_nodes = True
    # Every cap material in the mod is `new Material(BoardLit)` and keeps the shader's default
    # _Cull = 2 (Back) — unlike the tray. That is what made the round cap's missing bezel visible
    # rather than merely wrong, so this render must cull the same way or it cannot show it.
    mat.use_backface_culling = True
    mat.blend_method = 'OPAQUE'
    nt = mat.node_tree
    nt.nodes.clear()
    src = nt.nodes.new("ShaderNodeRGB")
    src.outputs[0].default_value = (*rgb, 1.0)
    col = src.outputs[0]
    if tex_img is not None and st is not None:
        uvn = nt.nodes.new("ShaderNodeUVMap")
        mapn = nt.nodes.new("ShaderNodeMapping")
        mapn.inputs["Scale"].default_value = (st[0][0], st[0][1], 1.0)
        mapn.inputs["Location"].default_value = (st[1][0], st[1][1], 0.0)
        nt.links.new(uvn.outputs["UV"], mapn.inputs["Vector"])
        tex = nt.nodes.new("ShaderNodeTexImage")
        tex.image = tex_img
        tex.extension = 'CLIP'
        nt.links.new(mapn.outputs["Vector"], tex.inputs["Vector"])
        mul = nt.nodes.new("ShaderNodeMixRGB")
        mul.blend_type = 'MULTIPLY'
        mul.inputs["Fac"].default_value = 1.0
        nt.links.new(tex.outputs["Color"], mul.inputs["Color1"])
        nt.links.new(src.outputs[0], mul.inputs["Color2"])
        col = mul.outputs["Color"]
    _shade_chain(nt, col, None)
    return mat


def _shade_chain(nt, base_color, normal_img):
    geo = nt.nodes.new("ShaderNodeNewGeometry")
    normal_src = geo.outputs["Normal"]
    if normal_img is not None:
        ntex = nt.nodes.new("ShaderNodeTexImage")
        ntex.image = normal_img
        nmap = nt.nodes.new("ShaderNodeNormalMap")
        nt.links.new(ntex.outputs["Color"], nmap.inputs["Color"])
        normal_src = nmap.outputs["Normal"]
    shade = None
    for vec, gain in ((GAME_KEY, 0.85), (GAME_FILL, 0.35)):
        dot = nt.nodes.new("ShaderNodeVectorMath")
        dot.operation = 'DOT_PRODUCT'
        dot.inputs[1].default_value = vec
        nt.links.new(normal_src, dot.inputs[0])
        cl = nt.nodes.new("ShaderNodeMath")
        cl.operation = 'MAXIMUM'
        cl.inputs[1].default_value = 0.0
        nt.links.new(dot.outputs["Value"], cl.inputs[0])
        sc = nt.nodes.new("ShaderNodeMath")
        sc.operation = 'MULTIPLY'
        sc.inputs[1].default_value = gain
        nt.links.new(cl.outputs[0], sc.inputs[0])
        if shade is None:
            shade = sc
        else:
            ad = nt.nodes.new("ShaderNodeMath")
            ad.operation = 'ADD'
            nt.links.new(shade.outputs[0], ad.inputs[0])
            nt.links.new(sc.outputs[0], ad.inputs[1])
            shade = ad
    amb = nt.nodes.new("ShaderNodeMath")
    amb.operation = 'ADD'
    amb.inputs[1].default_value = AMBIENT
    nt.links.new(shade.outputs[0], amb.inputs[0])
    mul = nt.nodes.new("ShaderNodeMixRGB")
    mul.blend_type = 'MULTIPLY'
    mul.inputs["Fac"].default_value = 1.0
    nt.links.new(base_color, mul.inputs["Color1"])
    nt.links.new(amb.outputs[0], mul.inputs["Color2"])
    em = nt.nodes.new("ShaderNodeEmission")
    em.inputs["Strength"].default_value = 1.0
    nt.links.new(mul.outputs["Color"], em.inputs["Color"])
    out = nt.nodes.new("ShaderNodeOutputMaterial")
    nt.links.new(em.outputs["Emission"], out.inputs["Surface"])


# =========================================================================================
def main():
    fbx, alb, nrm, cs_style = BOARDS[STYLE]
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=os.path.join(BUNDLE, fbx))

    board = [o for o in bpy.data.objects if o.type == 'MESH']
    if not board:
        raise RuntimeError("no board mesh")
    bmat = board_material("m_board", os.path.join(BUNDLE, alb), os.path.join(BUNDLE, nrm))
    for o in board:
        o.data.materials.clear()
        o.data.materials.append(bmat)

    empties = {o.name.split('.')[0]: o for o in bpy.data.objects if o.type == 'EMPTY'}

    scene = bpy.context.scene
    scene.render.engine = 'BLENDER_EEVEE_NEXT'
    scene.render.resolution_x = scene.render.resolution_y = RES
    scene.render.image_settings.file_format = 'PNG'
    scene.render.image_settings.color_mode = 'RGBA'
    scene.render.film_transparent = True
    scene.view_settings.view_transform = 'Standard'
    scene.view_settings.look = 'None'
    scene.eevee.taa_render_samples = 128
    if hasattr(scene.eevee, "use_raytracing"):
        scene.eevee.use_raytracing = False

    # ---- WHICH WAY IS THE DECORATED FACE? Ray-cast the board at a seat's XY from both sides and
    #      take the surface the seat empty is closest to. The FBX importer's axis conversion is a
    #      convention, and a cap placed 20 mm inside a plank looks a lot like a cap placed right.
    dg = bpy.context.evaluated_depsgraph_get()
    probe = empties.get("ButtonSeat2") or empties.get("ButtonSeat1")
    if probe is None:
        raise RuntimeError(f"{STYLE}: no ButtonSeat empties in {fbx}")
    px, py, pz = probe.matrix_world.translation
    hits = []
    for direction, start_z in ((Vector((0, 0, -1)), 1.0), (Vector((0, 0, 1)), -1.0)):
        ok, loc, nor, _, obj, _ = scene.ray_cast(dg, Vector((px, py, start_z)), direction)
        if ok:
            hits.append((loc.z, nor.z))
    if not hits:
        raise RuntimeError(f"{STYLE}: no ray hit under seat ({px:.4f}, {py:.4f})")
    # The seat FLOOR is the hit nearest the empty's own z.
    floor_z, floor_n = min(hits, key=lambda h: abs(h[0] - pz))
    up = 1.0 if floor_n >= 0 else -1.0
    log(f"{STYLE}: seat probe at ({px:.4f},{py:.4f},{pz:.4f}); hits {[round(h[0],4) for h in hits]}; "
        f"floor z {floor_z:.4f}, surface normal z {floor_n:+.2f} -> caps face {'+Z' if up > 0 else '-Z'}")

    atlas_path = os.path.join(BUNDLE, f"Keycap{cs_style}_albedo.png")
    atlas = None
    if os.path.exists(atlas_path):
        atlas = bpy.data.images.load(atlas_path)
        atlas.colorspace_settings.name = 'sRGB'
        log(f"{STYLE}: atlas {os.path.basename(atlas_path)} {atlas.size[0]}x{atlas.size[1]}")
    else:
        log(f"{STYLE}: NO ATLAS at {atlas_path} — caps render as the plain state tint, which is the "
            "mod's own fallback")

    face = seated(tuple(c * CAP_TINT for c in IDLE[STYLE]))
    tints = [face, bevel_tint(face), wall_tint(face)]

    sq_obj = os.path.join(MESHDIR, f"{STYLE}_square_{PASS}.obj")
    if not os.path.exists(sq_obj):
        raise RuntimeError(f"missing {sq_obj} — run ExportCapMeshes first")
    verts, uvs, groups = read_obj(sq_obj)
    log(f"{STYLE} {PASS}: {os.path.basename(sq_obj)} {len(verts)} verts, "
        f"{[(g[0], len(g[1])) for g in groups]}")

    for seat in (1, 2, 3):
        e = empties.get(f"ButtonSeat{seat}")
        if e is None:
            continue
        ex, ey, _ = e.matrix_world.translation
        role = SEAT_ROLE[seat]
        for gi, (gname, faces) in enumerate(groups):
            mesh = bpy.data.meshes.new(f"cap{seat}_{gname}")
            # A COMPACT PER-GROUP VERTEX LIST, and the UVs are written through the polygon's own
            # loop indices.
            #
            # The first version handed from_pydata the WHOLE shared vertex pool and then walked
            # uv_layers.data with a running counter, assuming loop order matched the order the
            # faces were listed in. mesh.validate() drops unused vertices and can reorder, so the
            # counter drifted and the recessed field came out with a fan-shaped smear of atlas
            # across it — a picture of a texturing bug in this script, which anyone would have read
            # as a defect in the cap.
            remap, gverts, gfaces, guvs = {}, [], [], []
            for idx, uvi in faces:
                tri = []
                for vi, ui in zip(idx, uvi):
                    if vi not in remap:
                        remap[vi] = len(gverts)
                        gverts.append(Vector(verts[vi]))
                    tri.append(remap[vi])
                gfaces.append(tri)
                guvs.append([uvs[u] for u in uvi])
            mesh.from_pydata(gverts, [], gfaces)
            mesh.validate()
            uvl = mesh.uv_layers.new(name="UVMap")
            for poly, tri_uv in zip(mesh.polygons, guvs):
                for li, uv in zip(poly.loop_indices, tri_uv):
                    uvl.data[li].uv = uv
            ob = bpy.data.objects.new(f"cap{seat}_{gname}", mesh)
            scene.collection.objects.link(ob)
            # The exported cap spans Blender z 0..+thickness with its FACE at +thickness (see
            # read_obj). Seat it so the cap's back sits on the recess floor.
            ob.location = (ex, ey, floor_z if up > 0 else floor_z)
            if up < 0:
                ob.rotation_euler = (math.pi, 0, 0)
            cell = role if gname == "field" else CELL_PLAIN
            st = cell_st(atlas.size[0], atlas.size[1], cell) if atlas else None
            ob.data.materials.append(
                flat_material(f"m_cap{seat}_{gname}", tints[min(gi, 2)], atlas, st))

    # ---- CAMERA: render_asset.py's board block, flags inherited (ortho, yaw 35, pitch 50, one
    #      pinned ortho width so three boards stay comparable).
    pts = []
    for ob in board:
        mw = ob.matrix_world
        pts += [mw @ Vector(c) for c in ob.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    ctr, size = (lo + hi) * 0.5, hi - lo

    yaw, pitch = math.radians(YAW), math.radians(PITCH)
    d = Vector((math.sin(yaw) * math.cos(pitch), -math.cos(yaw) * math.cos(pitch), math.sin(pitch)))
    d.normalize()
    if up < 0:
        d.z = -d.z            # look at the decorated face, whichever side it turned out to be
    cam_data = bpy.data.cameras.new("cam")
    cam_data.type = 'ORTHO'
    cam = bpy.data.objects.new("cam", cam_data)
    scene.collection.objects.link(cam)
    scene.camera = cam
    # --closeup frames the BUTTON CONSOLE rather than the whole tray. Both pictures are wanted and
    # they answer different questions: the whole board says "do these belong here", the console
    # says "is the construction actually built". A sheet with only the first cannot show a rivet.
    if "--capshot" in argv:
        # ONE CAP, ALONE, SQUARE-ON, FILLING THE FRAME — the input `plate_forensics.registration`
        # was built for, but pointed at the RENDERED cap instead of at the texture.
        #
        # This exists because round 4 moves the cap's inside-outside order OUT of the atlas and
        # INTO the mesh, and REG measured on the atlas cell therefore FALLS — correctly. Measuring
        # the cell and reporting a drop would be measuring the wrong stage with a good instrument,
        # which is the failure this project files under "one step too early". The order still has
        # to be there; it is just in the geometry now, so the geometry is what gets measured.
        for ob in board:
            ob.hide_render = True
        ctr = Vector(empties["ButtonSeat2"].matrix_world.translation)
        d = Vector((0.0, 0.0, 1.0)) if up > 0 else Vector((0.0, 0.0, -1.0))
        cam_data.ortho_scale = float(opt("--capshot-scale", "0.066"))
        pass   # film_transparent stays TRUE: the alpha IS the cap silhouette, and cap_regmove
               # crops to it so the measured frame is the cap FOOTPRINT and nothing else.
    elif "--closeup" in argv:
        ctr = Vector(empties["ButtonSeat2"].matrix_world.translation)
        cam_data.ortho_scale = float(opt("--closeup-scale", "0.19"))
    else:
        cam_data.ortho_scale = SCALE
    cam.location = ctr + d * (size.length * 3.0 + 1.0)
    cam.rotation_mode = 'QUATERNION'
    cam.rotation_quaternion = (-d).to_track_quat('-Z', 'Y')
    bpy.context.view_layer.update()

    os.makedirs(os.path.dirname(os.path.abspath(OUT)), exist_ok=True)
    scene.render.filepath = os.path.abspath(OUT)
    bpy.ops.render.render(write_still=True)
    log(f"wrote {OUT}")


main()
