import bpy, bmesh, sys, os

paths = sys.argv[sys.argv.index("--")+1:]
for p in paths:
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.fbx(filepath=p)
    for ob in bpy.data.objects:
        if ob.type != 'MESH':
            continue
        me = ob.data
        bm = bmesh.new(); bm.from_mesh(me)
        bm.verts.ensure_lookup_table(); bm.edges.ensure_lookup_table()
        boundary = [e for e in bm.edges if len(e.link_faces) == 1]
        nonman   = [e for e in bm.edges if len(e.link_faces) > 2]
        loose    = [v for v in bm.verts if not v.link_edges]
        tris = sum(len(f.verts)-2 for f in bm.faces)
        # count boundary LOOPS (holes)
        seen=set(); holes=0
        bset=set(boundary)
        for e in boundary:
            if e in seen: continue
            holes+=1; stack=[e]
            while stack:
                x=stack.pop()
                if x in seen: continue
                seen.add(x)
                for v in x.verts:
                    for ne in v.link_edges:
                        if ne in bset and ne not in seen: stack.append(ne)
        print(f"{os.path.basename(p)} :: {ob.name}")
        print(f"   verts {len(bm.verts):7d}  faces {len(bm.faces):7d}  tris {tris:7d}")
        print(f"   BOUNDARY edges {len(boundary):6d}  -> {holes} hole loop(s)")
        print(f"   non-manifold edges {len(nonman):5d}   loose verts {len(loose):5d}")
        print(f"   uv layers {len(me.uv_layers)}  materials {len(ob.material_slots)}")
        print(f"   dims {tuple(round(d,4) for d in ob.dimensions)}")
        bm.free()
