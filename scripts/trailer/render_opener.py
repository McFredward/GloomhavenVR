#!/usr/bin/env python3
"""Render exact-asset cinematic opening plates; source game/mod files stay read-only.

Blender 4.2 example:
  blender -b --python scripts/trailer/render_opener.py -- --output /private/opener \
    --shots npc_enchantress --width 960 --samples 24 --save-blend

All outputs are cinematic staging, never mislabeled as gameplay capture.
"""
import argparse
import hashlib
import importlib.util
import json
import math
import random
import sys
from pathlib import Path

import bpy
from mathutils import Matrix, Quaternion, Vector

_spec = importlib.util.spec_from_file_location("trailer_assets", Path(__file__).with_name("render_assets.py"))
base = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(base)
SHOTS = ("npc_merchant", "npc_enchantress", "npc_priestess", "dungeon_intro", "godmask", "godmask_lift", "board_reveal")


def arguments():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--assets", type=Path, default=Path(__file__).resolve().parents[2] / "unity/GloomhavenVR.Assets/Assets/Bundle")
    p.add_argument("--output", type=Path, required=True)
    p.add_argument("--shots", default=",".join(SHOTS))
    p.add_argument("--width", type=int, default=1920)
    p.add_argument("--samples", type=int, default=96)
    p.add_argument("--native-scene", type=Path)
    p.add_argument("--save-blend", action="store_true")
    p.add_argument("--frame", type=int, default=1)
    p.add_argument("--frames", type=int, default=0, help="Optional locally rendered camera-validation frames")
    a = p.parse_args(sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else [])
    a.assets, a.output = a.assets.resolve(), a.output.resolve()
    a.output.mkdir(parents=True, exist_ok=True)
    a.shots = a.shots.split(",")
    if any(s not in SHOTS for s in a.shots): p.error("unknown shot")
    if any(not s.startswith('npc_') for s in a.shots) and not a.native_scene:
        p.error("dungeon shots require --native-scene with original actor and architecture exports")
    return a


def reset(c, shot):
    s = base.reset(c, shot)
    s.render.film_transparent = False
    s.render.image_settings.color_mode = 'RGB'
    s.render.fps = 24
    s.frame_start, s.frame_end = 1, 96
    s.world.node_tree.nodes["Background"].inputs["Strength"].default_value = .075
    s.view_settings.exposure = -.2
    s.eevee.use_gtao = True if hasattr(s.eevee, 'use_gtao') else False
    return s


def source(path):
    path = Path(path).resolve()
    base.USED.add(path)
    return path


def npc(c, resident, location=(0,0,0), yaw=0):
    src = source(c.assets / f"TownServices/Actors/{resident}/{resident}_rig.fbx")
    before = set(bpy.data.objects)
    bpy.ops.import_scene.fbx(filepath=str(src))
    imported = set(bpy.data.objects) - before
    # The shipped runtime restores bind space before attaching eyes. Apply the
    # same neutral source bones, avoiding a mismatched first animated FBX frame.
    for o in imported:
        if o.animation_data: o.animation_data_clear()
        if o.type == 'ARMATURE':
            for b in o.pose.bones:
                b.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    root = c.assets / f"TownServices/Actors/{resident}/Textures"
    body = base.material(f"{resident}: original garment", root / "body_material_00_basecolor.png", roughness=.64)
    face = base.material(f"{resident}: original face543", root / "face543_albedo.png", roughness=.58)
    face.node_tree.nodes['Principled BSDF'].inputs['Subsurface Weight'].default_value = .045
    hands = base.material("Original town hands545", c.assets / "TownServices/Textures/hands545_albedo.png", roughness=.6)
    eye = base.material(f"{resident}: original iris", c.assets / f"TownServices/Textures/{'green' if resident == 'enchantress' else 'brown'}_eye.png", roughness=.28)
    cornea = base.material("Original clear optical cornea", color=(1,1,1,1), roughness=.06)
    cornea.node_tree.nodes['Principled BSDF'].inputs['Transmission Weight'].default_value = 1
    cornea.node_tree.nodes['Principled BSDF'].inputs['IOR'].default_value = 1.376
    graph=bpy.context.evaluated_depsgraph_get()
    staged=[]
    for o in imported:
        if o.type != 'MESH' or o.name.startswith(('LOD1_', 'LOD2_')): continue
        evaluated=o.evaluated_get(graph)
        mesh=bpy.data.meshes.new_from_object(evaluated, depsgraph=graph)
        mesh.transform(o.matrix_world)
        obj=bpy.data.objects.new(f"Original {resident} {o.name}",mesh)
        bpy.context.scene.collection.objects.link(obj)
        old=[m.name if m else '' for m in mesh.materials]
        slots=[p.material_index for p in mesh.polygons]
        mesh.materials.clear()
        if 'Cornea' in o.name: mesh.materials.append(cornea)
        elif 'Globe' in o.name: mesh.materials.append(eye)
        else:
            for name in old:
                if 'Body' in name: mesh.materials.append(body)
                elif 'Face' in name: mesh.materials.append(face)
                elif 'Hands' in name: mesh.materials.append(hands)
                else: raise ValueError(f"Unmapped original NPC slot {name}")
        # Clearing Blender slots resets polygon assignments. Restore their exact
        # source slot after replacing materials, preserving face and hand UVs.
        for polygon,index in zip(mesh.polygons,slots): polygon.material_index=index
        obj.matrix_world=Matrix.Translation(Vector(location)) @ Matrix.Rotation(math.radians(yaw),4,'Z')
        staged.append(obj)
    for o in imported: bpy.data.objects.remove(o,do_unlink=True)
    base.ASSET_REPORT.append({'shot':bpy.context.scene.name,'source':str(src),'resident':resident,'source_lod':0,'materials':'original distinct garment/face/hands/iris/cornea','source_pose':'neutral bind pose','location':location,'yaw':yaw})
    return staged


def practical(position, energy=35, color=(1,.28,.055), radius=.025):
    d=bpy.data.lights.new('Cinematic candle practical','POINT');d.energy=energy;d.color=color;d.shadow_soft_size=radius
    o=bpy.data.objects.new(d.name,d);bpy.context.scene.collection.objects.link(o);o.location=position
    return o


def candle(position, height=.20):
    wax=base.material('Staging beeswax',color=(.38,.19,.065,1),roughness=.78)
    x,y,z=position
    bpy.ops.mesh.primitive_cylinder_add(vertices=24,radius=.025,depth=height,location=(x,y,z+height/2))
    bpy.context.object.data.materials.append(wax)
    glow=base.material('Candle light source',color=(1,.2,.006,1))
    bs=glow.node_tree.nodes['Principled BSDF'];bs.inputs['Emission Color'].default_value=(1,.28,.015,1);bs.inputs['Emission Strength'].default_value=6
    bpy.ops.mesh.primitive_uv_sphere_add(segments=16,ring_count=8,radius=.012,location=(x,y,z+height+.014));bpy.context.object.scale=(.6,.6,1.9);bpy.context.object.data.materials.append(glow)
    practical((x,y,z+height+.04))


def town_set(c,resident):
    tex=c.assets/'TownServices/Textures'
    stone=base.material('Original town stone, cinematic masonry',tex/'monastery_stone_floor_alb.jpg',roughness=.85,box=True)
    wood=base.material('Original town wood on staging table',tex/'dark_wooden_planks_alb.jpg',roughness=.7,box=True)
    base.cube('Cinematic service table',(0,-.20,.88),(1.4,.85,.09),wood,.02)
    base.cube('Out of focus stone floor',(0,1.4,-.08),(8,8,.15),stone,.01)
    random.seed(318)
    for row in range(9):
        for col in range(9):
            base.cube(f'Cinematic masonry {row} {col}',((col-4)*.63+(row%2)*.315,3.0,row*.37+.12),(.618,.20,.362),stone,.008)
    candle((-.47,-.43,.93),.24);candle((.60,.03,.93),.30)
    # Keep the close-up clear: the genuine NPC is the only character geometry.
    area=base.area
    area('Warm service key',(-1.1,-1.2,2.2),(0,-.1,1.45),(1,.61,.30),100,1.0)
    area('Cool atmospheric edge',(.9,.45,1.95),(0,0,1.45),(.15,.50,1),115,.65)
    area('Gentle face and iris lift',(.05,-2.1,1.7),(0,-.1,1.6),(.58,.72,1),16,.9)
    area('Amber shoulder rim',(-.6,.8,1.8),(0,0,1.5),(1,.24,.045),80,.7)


def npc_plate(c,resident):
    town_set(c,resident)
    npc(c,resident,yaw=-6)
    cam=base.camera((.70,-2.65,1.76),(0,-.015,1.43),lens=72,focus=(0,-.12,1.59),fstop=2.8)
    cam.keyframe_insert('location',frame=1)
    cam.location=(.49,-2.53,1.72);base.look_at(cam,(0,-.015,1.44));cam.keyframe_insert('location',frame=96);cam.keyframe_insert('rotation_euler',frame=96)
    cam.location=(.70,-2.65,1.76);base.look_at(cam,(0,-.015,1.43));cam.keyframe_insert('rotation_euler',frame=1)
    return cam


def original_prop(c, name, location, height, yaw=0):
    root=c.assets/'Environments/Imported'
    src=source(root/f'Models/{name}.obj')
    before=set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=str(src),forward_axis='NEGATIVE_Z',up_axis='Y')
    imported=set(bpy.data.objects)-before
    objects=[o for o in imported if o.type=='MESH']
    points=[o.matrix_world@v.co for o in objects for v in o.data.vertices]
    low=Vector(tuple(min(p[i] for p in points) for i in range(3)))
    high=Vector(tuple(max(p[i] for p in points) for i in range(3)))
    pivot=Vector(((low.x+high.x)/2,(low.y+high.y)/2,low.z))
    gain=height/(high.z-low.z)
    mat=base.material(f'Original imported environment {name}',root/f'Textures/{name}_alb.jpg',roughness=.82,
                      normal=root/f'Textures/{name}_nrm.jpg')
    transform=Matrix.Translation(Vector(location))@Matrix.Rotation(math.radians(yaw),4,'Z')@Matrix.Scale(gain,4)@Matrix.Translation(-pivot)
    for o in objects:
        o.name=f'Exact source prop {name}';o.matrix_world=transform@o.matrix_world
        o.data.materials.clear();o.data.materials.append(mat)
    base.ASSET_REPORT.append({'shot':bpy.context.scene.name,'source':str(src),'name':name,'original_bounds':[list(low),list(high)],'staging_height':height,'location':location,'yaw':yaw})


def native_actor(c, name, location, height=.17, yaw=0):
    if not c.native_scene: return []
    directory=c.native_scene.resolve()/name
    candidates=sorted(directory.glob('*.obj'))
    if not candidates: raise FileNotFoundError(f'Native {name} actor not exported in {directory}')
    src=source(candidates[0])
    metadata=json.loads((directory/'manifest.json').read_text())
    materials={}
    for entry in metadata['materials']:
        tex=entry['textures']
        diffuse=tex.get('_Diffuse',tex.get('_MainTex'))
        normal=tex.get('_NormalMap',tex.get('_BumpMap'))
        packed=tex.get('_MRAO')
        if not diffuse: raise ValueError(f"Native material {entry['obj_name']} lacks original diffuse")
        mat=base.material(f"Original native {entry['obj_name']}",Path(diffuse['output']),roughness=.67,
                          normal=Path(normal['output']) if normal else None,
                          mrs=Path(packed['output']) if packed else None)
        if 'Fur' in entry['source_name']:
            shader=mat.node_tree.nodes['Principled BSDF']
            image=next(n for n in mat.node_tree.nodes if n.type=='TEX_IMAGE' and n.image and n.image.filepath==diffuse['output'])
            mat.node_tree.links.new(image.outputs['Alpha'],shader.inputs['Alpha'])
            mat.surface_render_method='DITHERED'
        materials[entry['obj_name']]=mat
    for p in directory.rglob('*'):
        if p.is_file() and p.suffix.lower() in ('.png','.mtl','.json'): source(p)
    before=set(bpy.data.objects)
    # Native exporter already emits right-handed Blender Z-up coordinates.
    bpy.ops.wm.obj_import(filepath=str(src),forward_axis='Y',up_axis='Z')
    objects=[o for o in set(bpy.data.objects)-before if o.type=='MESH']
    points=[o.matrix_world@v.co for o in objects for v in o.data.vertices]
    low=Vector(tuple(min(p[i] for p in points) for i in range(3)))
    high=Vector(tuple(max(p[i] for p in points) for i in range(3)))
    pivot=Vector(((low.x+high.x)/2,(low.y+high.y)/2,low.z))
    gain=height/(high.z-low.z)
    transform=Matrix.Translation(Vector(location))@Matrix.Rotation(math.radians(yaw),4,'Z')@Matrix.Scale(gain,4)@Matrix.Translation(-pivot)
    for o in objects:
        o.name=f'Original native {name} {o.name}';o.matrix_world=transform@o.matrix_world
        for i,mat in enumerate(o.data.materials):
            if mat.name not in materials: raise ValueError(f'Unmapped native material {mat.name}')
            o.data.materials[i]=materials[mat.name]
    base.ASSET_REPORT.append({'shot':bpy.context.scene.name,'source':str(src),'name':name,'pose':'original native idle exported from game bank','original_bounds':[list(low),list(high)],'staging_height':height,'location':location,'yaw':yaw})
    return objects


def native_architecture(c, name, location, scale=.10, yaw=0):
    directory=c.native_scene.resolve()/'dungeon'
    metadata=json.loads((directory/'manifest.json').read_text())
    entry=next(p for p in metadata['props'] if p['mesh']==name)
    src=source(entry['obj'])
    source(directory/'manifest.json');source(Path(src).with_suffix('.mtl'))
    tex=entry['material']['textures']
    mat=base.material(f'Native dungeon {name}',Path(tex['_MainTex']['output']),roughness=.86,
                      normal=Path(tex['_BumpMap']['output']))
    before=set(bpy.data.objects)
    bpy.ops.wm.obj_import(filepath=str(src),forward_axis='Y',up_axis='Z')
    objects=[o for o in set(bpy.data.objects)-before if o.type=='MESH']
    transform=Matrix.Translation(Vector(location))@Matrix.Rotation(math.radians(yaw),4,'Z')@Matrix.Scale(scale,4)
    for o in objects:
        o.name=f'Original architecture {name}';o.matrix_world=transform@o.matrix_world
        o.data.materials.clear();o.data.materials.append(mat)
    base.ASSET_REPORT.append({'shot':bpy.context.scene.name,'source':str(src),'name':name,'native_material_pairing':entry['material']['source_name'],'location':location,'scale':scale,'yaw':yaw})


def grip_hand(c, contact=(0,.17,.45), scale=1.8):
    src=source(c.assets/'Hands/VRHand_R_rig.fbx')
    before=set(bpy.data.objects);bpy.ops.import_scene.fbx(filepath=str(src))
    imported=set(bpy.data.objects)-before
    armature=next(o for o in imported if o.type=='ARMATURE')
    curls={'Thumb':1.0,'Index':.6,'Middle':.9,'Ring':1,'Pinky':1}
    for finger,curl in curls.items():
        angles=(25,45,60) if finger=='Thumb' else (75,95,65)
        for joint,angle in zip(('Root','Mid','Tip'),angles):
            bone=armature.pose.bones[f'Anchor_{finger}_{joint}']
            bone.rotation_mode='QUATERNION';bone.rotation_quaternion=Quaternion((1,0,0),math.radians(angle*curl))
    bpy.context.view_layer.update()
    thumb=armature.matrix_world@armature.pose.bones['Anchor_Thumb_Tip'].tail
    index=armature.matrix_world@armature.pose.bones['Anchor_Index_Tip'].tail
    pinch=(thumb+index)/2
    gap=index-thumb
    rotation=(Quaternion((0,0,1),math.radians(20))@Quaternion((1,0,0),math.radians(140))@gap.rotation_difference(Vector((1,0,0)))).to_matrix().to_4x4()
    transform=Matrix.Translation(Vector(contact))@rotation@Matrix.Scale(scale,4)@Matrix.Translation(-pinch)
    graph=bpy.context.evaluated_depsgraph_get();mat=base.hand_mat(c,'VRHand');objects=[]
    for o in imported:
        if o.type!='MESH': continue
        mesh=bpy.data.meshes.new_from_object(o.evaluated_get(graph),depsgraph=graph);mesh.transform(o.matrix_world)
        mesh.materials.clear();mesh.materials.append(mat)
        staged=bpy.data.objects.new('Exact leather glove thumb/index grip',mesh);bpy.context.scene.collection.objects.link(staged);staged.matrix_world=transform;objects.append(staged)
    for o in imported:bpy.data.objects.remove(o,do_unlink=True)
    base.ASSET_REPORT.append({'shot':bpy.context.scene.name,'source':str(src),'rig_joint_curls':curls,'contact_world':list(contact),'thumb_pad_tip_world':list(transform@thumb),'index_pad_tip_world':list(transform@index),'tip_separation_metres':gap.length*scale,'scale':scale})
    return objects


def stage_board(c):
    # Simple trailer masonry is explicitly set dressing. Hero/enemy models are
    # original extracted assets once --native-scene is supplied.
    stone=base.material('Native monastery stone surface',c.assets/'Environments/Imported/Textures/monastery_stone_floor_alb.jpg',roughness=.84,box=True)
    wood=base.material('Native wood table surface',c.assets/'TownServices/Textures/dark_wooden_planks_alb.jpg',roughness=.72,box=True)
    base.cube('Trailer table',(0,.12,-.10),(2.4,1.7,.19),wood,.018)
    # A distant, softly lit tavern surround gives the render spatial context.
    # Its source-textured masonry is cinematic dressing, not a native room claim.
    for row in range(13):
        for col in range(16):
            base.cube(f'Distant tavern masonry {row} {col}',((col-7.5)*.5+(row%2)*.25,3.6,row*.27-1.2),(.49,.16,.265),stone,.007)
    base.cube('Rear tavern candle counter',(0,2.15,-.17),(4.1,.66,.30),wood,.015)
    candle((-1.35,2.05,-.02),.40);candle((1.65,2.2,-.02),.53)
    base.area('Distant warm tavern pool',(-1.25,2.5,1.0),(0,3.6,.85),(1,.24,.055),12,1.2)
    if c.native_scene:
        # Actual native full hex tiles retain their original flat top and shallow
        # solid edge. Segment P is procedural sloped trim and cannot be tiled.
        for row in range(6):
            for col in range(7):
                native_architecture(c,'CR_TC_Floor_Basic',(-.516+col*.17204+(row%2)*.08602,-.31+row*.148958,.002),scale=.10)
        for x in (-.52,-.347,-.174,.174,.347,.52):
            native_architecture(c,'CR_Dungeon_Wall_Stone_01',(x,.56,.001),scale=.10)
        for x in (-.64,.64):
            for y in (.14,.32):
                native_architecture(c,'CR_Dungeon_Wall_Stone_01',(x,y,.001),scale=.10,yaw=90)
    else:
        base.cube('Dungeon staging floor',(0,.12,-.035),(1.4,1.0,.065),stone,.004)
    practical((-.44,.36,.25),5,(1,.26,.025),.035)
    practical((.49,.31,.28),4,(1,.38,.10),.035)
    original_prop(c,'rock_moss_set_02',(-.45,-.05,.005),.075,35)
    original_prop(c,'wine_barrel_01',(.42,.28,.005),.14,-12)
    original_prop(c,'wooden_crate_01',(-.39,.33,.005),.11,15)


def godmask(c):
    stage_board(c)
    native_actor(c,'brute',(0,.17,.005),height=.22,yaw=15)
    base.import_asset(c,'Head/Mask_2.fbx',base.mask_mat(c,2),'Exact Grimhorn looming',(0,1.22,.92),euler=(13,0,-5),scale=3.4)
    base.import_asset(c,'Hands/VRHand_R_rig.fbx',base.hand_mat(c,'VRHand'),'Exact glove descending',(.30,.18,.31),euler=(78,-20,-35),scale=1.8,curl=.23)
    base.area('Giant silhouette amber rim',(-.9,1.7,1.6),(0,1.2,.95),(1,.24,.06),90,.8)
    base.area('Cyan god-mask edge',(.8,1.30,1.4),(0,1.2,.95),(.16,.50,1),70,.65)
    base.area('Soft revelation face',(0,-.1,1.25),(0,1.2,.99),(1,.66,.37),25,.9)
    base.area('Ground texture skim',(-.5,-.4,.25),(0,.3,.03),(1,.5,.15),4,.25)
    cam=base.camera((-.15,-.62,.105),(0,.65,.43),lens=26,focus=(0,1.2,.99),fstop=11)
    return cam


def godmask_lift(c):
    stage_board(c)
    native_actor(c,'brute',(0,.17,.267),height=.22,yaw=15)
    base.import_asset(c,'Head/Mask_2.fbx',base.mask_mat(c,2),'Exact Grimhorn looming',(0,1.22,.92),euler=(13,0,-5),scale=3.4)
    grip_hand(c)
    base.import_asset(c,'Head/Mask_1.fbx',base.mask_mat(c,1),'Exact Runeveil joining',(1.12,.97,1.01),euler=(8,0,18),scale=3.25)
    base.import_asset(c,'Hands/VRHandArcane_L_rig.fbx',base.hand_mat(c,'VRHandArcane'),'Exact Runeveil left',(.69,.17,.25),euler=(90,10,15),scale=1.8,curl=.16)
    base.import_asset(c,'Hands/VRHandArcane_R_rig.fbx',base.hand_mat(c,'VRHandArcane'),'Exact Runeveil right',(.97,.26,.28),euler=(85,-10,-20),scale=1.8,curl=.16)
    base.area('Giant silhouette amber rim',(-.9,1.7,1.6),(0,1.2,.95),(1,.24,.06),90,.8)
    base.area('Cyan god-mask edge',(.8,1.30,1.4),(0,1.2,.95),(.16,.50,1),70,.65)
    base.area('Soft revelation face',(0,-.1,1.25),(0,1.2,.99),(1,.66,.37),25,.9)
    base.area('Ground texture skim',(-.5,-.4,.25),(0,.3,.03),(1,.5,.15),4,.25)
    base.area('Runeveil edge',(1.8,.9,1.5),(1.1,.9,.95),(.18,.55,1),65,.8)
    base.area('Held original hero softbox',(0,-.9,.8),(0,.17,.40),(1,.64,.36),12,.6)
    cam=base.camera((.40,-2.70,1.68),(.40,.60,.61),lens=38,focus=(0,.17,.40),fstop=8)
    return cam


def dungeon_intro(c):
    # The entire godmask world remains present. Tight figure-height framing puts
    # Grimhorn above the frame and the descending glove beyond its right edge.
    cam=godmask(c)
    cam.location=(-.10,-.58,.12)
    base.look_at(cam,(-.14,.17,.13))
    cam.data.lens=55
    cam.data.dof.focus_distance=(cam.location-Vector((0,.17,.125))).length
    cam.data.dof.aperture_fstop=8
    return cam


def board_reveal(c):
    stage_board(c)
    native_actor(c,'brute',(-.10,-.07,.005),height=.20,yaw=25)
    native_actor(c,'livingbones',(.18,.19,.005),height=.19,yaw=-150)
    base.import_asset(c,'Head/Mask_2.fbx',base.mask_mat(c,2),'Exact Grimhorn',(-.54,.53,.77),euler=(9,0,-18),scale=1.85)
    base.import_asset(c,'Head/Mask_1.fbx',base.mask_mat(c,1),'Exact Runeveil',(.53,.50,.76),euler=(7,0,18),scale=1.85)
    for style,side,location,euler in [('VRHand','R',(-.44,.06,.25),(95,-15,-12)),('VRHand','L',(-.70,.16,.21),(88,8,15)),('VRHandArcane','L',(.46,.04,.22),(92,10,15)),('VRHandArcane','R',(.68,.16,.23),(82,-8,-20))]:
        base.import_asset(c,f'Hands/{style}_{side}_rig.fbx',base.hand_mat(c,style),f'Exact {style} {side}',location,euler=euler,scale=1.3,curl=.16)
    base.area('Amber Grimhorn',(-1,-.5,1.1),(-.5,.4,.65),(1,.57,.26),70,.8)
    base.area('Cool Runeveil',(1,-.2,1.15),(.5,.4,.65),(.18,.58,1),80,.8)
    base.area('Shared world overhead',(0,.6,1.5),(0,.15,0),(1,.62,.32),85,1.1)
    cam=base.camera((.08,-2.65,1.32),(0,.25,.40),lens=44,focus=(0,.30,.57),fstop=8)
    return cam


def main():
    c=arguments();outputs=[]
    for shot in c.shots:
        s=reset(c,shot)
        if shot.startswith('npc_'):npc_plate(c,shot[4:])
        else:globals()[shot](c)
        s.frame_set(c.frame)
        if c.save_blend:
            bpy.data.orphans_purge(do_local_ids=True,do_linked_ids=False,do_recursive=True)
            bpy.ops.file.pack_all()
            p=c.output/f'{shot}.blend';bpy.ops.wm.save_as_mainfile(filepath=str(p),compress=True);outputs.append(p)
        s.render.filepath=str(c.output/f'{shot}.png');bpy.ops.render.render(write_still=True);outputs.append(Path(s.render.filepath));print(f'PLATE_READY {s.render.filepath}',flush=True)
        if c.frames:
            for i in range(c.frames):
                frame=round(1+i*95/max(c.frames-1,1));s.frame_set(frame);s.render.filepath=str(c.output/f'{shot}-camera-{frame:03d}.png');bpy.ops.render.render(write_still=True);outputs.append(Path(s.render.filepath))
    manifest={'presentation':'Exact mod character assets with cinematic staging and adapted PBR; not gameplay capture','blender_version':bpy.app.version_string,'tool':str(Path(__file__).resolve()),'tool_sha256':base.sha(__file__),'render_assets_sha256':base.sha(Path(__file__).with_name('render_assets.py')),'resolution':[c.width,round(c.width*9/16)],'samples':c.samples,'sources':[{'path':str(p),'bytes':p.stat().st_size,'sha256':base.sha(p)} for p in sorted(base.USED)],'asset_instances':base.ASSET_REPORT,'outputs':[{'path':str(p),'sha256':base.sha(p)} for p in outputs]}
    serialized=json.dumps(manifest,indent=2)+'\n'
    (c.output/'opener-manifest.json').write_text(serialized)
    (c.output/f"opener-manifest-{'-'.join(c.shots)}.json").write_text(serialized)

if __name__=='__main__':main()
