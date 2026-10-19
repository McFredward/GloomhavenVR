#!/usr/bin/env python3
"""Stage native Gloomhaven characters in a continuous cinematic world.

The revision deliberately begins at character scale. Only the later reveal may
expose a tabletop. Original actor/mask/hand assets remain unchanged and read-only.
"""
import argparse
import hashlib
import importlib.util
import json
import math
import sys
from types import SimpleNamespace
from pathlib import Path

import bpy
import numpy as np
from bpy_extras.object_utils import world_to_camera_view
from mathutils import Matrix, Vector

_spec=importlib.util.spec_from_file_location('trailer_assets',Path(__file__).with_name('render_assets.py'))
base=importlib.util.module_from_spec(_spec);_spec.loader.exec_module(base)
SHOTS=('opening','god_presence','companion_lift')
INSTANCES=[]


def arguments():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--assets',type=Path,default=Path(__file__).resolve().parents[2]/'unity/GloomhavenVR.Assets/Assets/Bundle')
    p.add_argument('--native',type=Path,default=Path('/home/claw/gloomhaven_vr/.planning/debug/trailer/native-scene'))
    p.add_argument('--motion',type=Path,default=Path('/home/claw/gloomhaven_vr/.planning/debug/trailer/motion/brute'))
    p.add_argument('--output',type=Path,required=True)
    p.add_argument('--shots',default='opening,god_presence')
    p.add_argument('--width',type=int,default=960)
    p.add_argument('--samples',type=int,default=32)
    p.add_argument('--pose',choices=('idle','melee','heavy'),default='melee')
    p.add_argument('--pose-frame',type=int,default=6)
    p.add_argument('--save-blend',action='store_true')
    p.add_argument('--save-base',action='store_true',help='Pack an unanimated God scene for the single reviewed motion driver')
    p.add_argument('--player-motion-helper',type=Path,help='Reviewed source-rig motion module for natural reach/lift reference poses')
    c=p.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    c.shots=c.shots.split(',');c.assets=c.assets.resolve();c.output=c.output.resolve()
    if any(s not in SHOTS for s in c.shots):p.error('unknown shot')
    if 'companion_lift' in c.shots and not c.player_motion_helper:p.error('companion endpoint requires the reviewed source-rig motion helper')
    c.output.mkdir(parents=True,exist_ok=True)
    return c


def source(path):
    path=Path(path).resolve();base.USED.add(path);return path


def surface(c,name,albedo,normal=None,roughness=.8,scale=.5):
    mat=base.material(name,albedo,normal=normal,roughness=roughness,box=True)
    tree=mat.node_tree
    coordinates=tree.nodes.new('ShaderNodeTexCoord')
    vector=tree.nodes.new('ShaderNodeVectorMath');vector.operation='SCALE';vector.inputs[3].default_value=scale
    tree.links.new(coordinates.outputs['Object'],vector.inputs[0])
    for n in tree.nodes:
        if n.type=='TEX_IMAGE':
            n.projection='BOX';n.projection_blend=.16;tree.links.new(vector.outputs['Vector'],n.inputs['Vector'])
    tree.nodes['Principled BSDF'].inputs['Emission Strength'].default_value=0
    return mat


def original_actor(c):
    stem={'idle':'HE_Brute_Idle_v001','melee':'HE_Brute_Melee_Attack_v001','heavy':'HE_Brute_Heavy_Melee_Attack_v001'}[c.pose]
    path=source(c.motion/f'{stem}.npz');source(path.with_suffix('.json'))
    data=np.load(path,allow_pickle=False)
    frame=min(c.pose_frame,len(data['positions'])-1)
    vertices=data['positions'][frame]
    mesh=bpy.data.meshes.new('Original animated Brute native topology')
    mesh.from_pydata(vertices.tolist(),[],data['triangles'].tolist());mesh.update()
    obj=bpy.data.objects.new('Original Brute living native pose',mesh);bpy.context.scene.collection.objects.link(obj)
    meta_path=source(c.native/'brute/manifest.json');metadata=json.loads(meta_path.read_text())
    slots={m['obj_name']:m for m in metadata['materials']}
    for name in data['material_names'].tolist():
        entry=slots[name];textures=entry['textures'];diffuse=source(textures['_Diffuse']['output'])
        normal=source(textures['_NormalMap']['output']) if '_NormalMap' in textures else None
        packed=source(textures['_MRAO']['output']) if '_MRAO' in textures else None
        mat=base.material(f'Original Brute {name}',diffuse,normal=normal,mrs=packed,roughness=.65)
        tree=mat.node_tree;shader=tree.nodes['Principled BSDF'];shader.inputs['Emission Strength'].default_value=0
        # Retain source PBR masks while bounding the film-light specular response.
        if shader.inputs['Roughness'].is_linked:
            link=shader.inputs['Roughness'].links[0];output=link.from_socket;tree.links.remove(link)
            remap=tree.nodes.new('ShaderNodeMapRange');remap.inputs['From Min'].default_value=0;remap.inputs['From Max'].default_value=1
            remap.inputs['To Min'].default_value=.42;remap.inputs['To Max'].default_value=.86
            tree.links.new(output,remap.inputs['Value']);tree.links.new(remap.outputs['Result'],shader.inputs['Roughness'])
        if 'Fur' in name:
            tex=next(n for n in tree.nodes if n.type=='TEX_IMAGE' and n.image and Path(n.image.filepath).resolve()==diffuse)
            tree.links.new(tex.outputs['Alpha'],shader.inputs['Alpha']);mat.surface_render_method='DITHERED'
        mesh.materials.append(mat)
    for poly,index in zip(mesh.polygons,data['triangle_materials']):poly.material_index=int(index);poly.use_smooth=True
    layer=mesh.uv_layers.new(name='Original native UV')
    loop_indices=np.zeros(len(mesh.loops),dtype=np.int32);mesh.loops.foreach_get('vertex_index',loop_indices)
    layer.data.foreach_set('uv',data['uv'][loop_indices].astype(np.float32).reshape(-1))
    obj.rotation_euler.z=math.radians(-10)
    INSTANCES.append({'kind':'original native animated actor','source':str(path),'pose':stem,'frame':frame,'time':float(data['times'][frame]),'vertices':len(vertices),'triangles':len(mesh.polygons),'world_scale':1,'yaw_degrees':-10})
    return obj


def prop(c,name,position,height,yaw=0):
    root=c.assets/'Environments/Imported';src=source(root/f'Models/{name}.obj')
    before=set(bpy.data.objects);bpy.ops.wm.obj_import(filepath=str(src),forward_axis='NEGATIVE_Z',up_axis='Y')
    meshes=[o for o in set(bpy.data.objects)-before if o.type=='MESH']
    points=[o.matrix_world@v.co for o in meshes for v in o.data.vertices]
    low=Vector(tuple(min(p[i] for p in points) for i in range(3)));high=Vector(tuple(max(p[i] for p in points) for i in range(3)))
    pivot=Vector(((low.x+high.x)/2,(low.y+high.y)/2,low.z));gain=height/(high.z-low.z)
    mat=base.material(f'Original film prop {name}',root/f'Textures/{name}_alb.jpg',normal=root/f'Textures/{name}_nrm.jpg',roughness=.78)
    mat.node_tree.nodes['Principled BSDF'].inputs['Emission Strength'].default_value=0
    matrix=Matrix.Translation(Vector(position))@Matrix.Rotation(math.radians(yaw),4,'Z')@Matrix.Scale(gain,4)@Matrix.Translation(-pivot)
    for o in meshes:o.matrix_world=matrix@o.matrix_world;o.data.materials.clear();o.data.materials.append(mat)
    INSTANCES.append({'kind':'original mod environment prop','source':str(src),'height':height,'position':position,'yaw_degrees':yaw})


def arch(stone):
    # A complete wall surrounds a walkable archway. Its joined volumes have no
    # stage-panel gaps or board-scale cutoffs anywhere inside the camera frustum.
    y=4.9;thickness=.8;radius=1.25;spring=2.4;top=6.4;outside=6.4
    base.cube('Continuous back wall left',(-3.84,y,top/2),(5.16,thickness,top),stone,.035)
    base.cube('Continuous back wall right',(3.84,y,top/2),(5.16,thickness,top),stone,.035)
    base.cube('Stone above arch crown',(0,y,(top+spring+radius)/2),(2.56,thickness,top-spring-radius),stone,.025)
    # Fill the two upper arch haunches with actual contiguous wedge polygons.
    for side in (-1,1):
        for i in range(12):
            a=i*math.pi/24;b=(i+1)*math.pi/24
            x1=side*radius*math.cos(a);x2=side*radius*math.cos(b)
            z1=spring+radius*math.sin(a);z2=spring+radius*math.sin(b)
            points=[(x1,y-thickness/2,z1),(x2,y-thickness/2,z2),(x2,y-thickness/2,spring+radius+.035),(x1,y-thickness/2,spring+radius+.035)]
            points+= [(x,y+thickness/2,z) for x,_,z in points]
            mesh=bpy.data.meshes.new('Arch haunch solid');mesh.from_pydata(points,[],[(0,1,2,3),(7,6,5,4),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)]);mesh.materials.append(stone)
            o=bpy.data.objects.new('Joined ancient arch masonry',mesh);bpy.context.scene.collection.objects.link(o)
    # Individually worn voussoirs make the arch read as structural stonework.
    trim=base.material('Ancient arch edge stone',color=(.17,.12,.075,1),roughness=.88)
    for i in range(17):
        a=math.pi*i/17;b=math.pi*(i+1)/17
        pts=[]
        for yy in (y-thickness/2-.07,y+thickness/2+.02):
            pts += [(r*math.cos(t),yy,spring+r*math.sin(t)) for r,t in [(radius,a),(radius,b),(radius+.24,b),(radius+.24,a)]]
        mesh=bpy.data.meshes.new('Arch ring voussoir');mesh.from_pydata(pts,[],[(0,1,2,3),(7,6,5,4),(0,4,5,1),(1,5,6,2),(2,6,7,3),(3,7,4,0)]);mesh.materials.append(trim)
        o=bpy.data.objects.new('Arch structural stone ring',mesh);bpy.context.scene.collection.objects.link(o)
    for x in (-1.37,1.37):base.cube('Arch jamb continuous support',(x,y-.08,spring/2),(.25,1.04,spring+.04),trim,.035)
    base.cube('Joined left courtyard wall',(-6.03,-.8,3.2),(.8,24.0,6.4),stone,.04)
    base.cube('Joined right courtyard wall',(6.03,-.8,3.2),(.8,24.0,6.4),stone,.04)
    # The door opens into an inhabited corridor with floor, far wall and light.
    base.cube('Corridor left return',(-1.62,8.0,2.7),(.65,6.0,5.4),stone,.025)
    base.cube('Corridor right return',(1.62,8.0,2.7),(.65,6.0,5.4),stone,.025)
    base.cube('Far passage wall',(0,11.0,2.7),(3.9,.6,5.4),stone,.025)


def torch(position, energy=130):
    x,y,z=position
    iron=base.material('Cinematic torch forged iron',color=(.035,.027,.021,1),metallic=.75,roughness=.7)
    base.cube('Torch wall mounting',(x,y+.07,z-.08),(.14,.18,.36),iron,.018)
    bpy.ops.mesh.primitive_cylinder_add(vertices=16,radius=.055,depth=.55,location=(x,y-.15,z-.15));bpy.context.object.data.materials.append(iron)
    glow=base.material('Motivated amber torch flame',color=(1,.10,.004,1));shader=glow.node_tree.nodes['Principled BSDF'];shader.inputs['Emission Color'].default_value=(1,.105,.008,1);shader.inputs['Emission Strength'].default_value=4.5
    # Tapered, asymmetrical tongues replace the overlarge uniformly white bulb.
    for dx,dy,dz,r,h in ((0,0,0,.050,.23),(.035,.015,-.025,.026,.17),(-.020,-.015,.035,.022,.22)):
        bpy.ops.mesh.primitive_cone_add(vertices=9,radius1=r,radius2=.003,depth=h,location=(x+dx,y-.15+dy,z+.12+dz+h/2))
        o=bpy.context.object;o.rotation_euler=(.04,.12+dx*2,.1);o.data.materials.append(glow)
    d=bpy.data.lights.new('Torch practical warm pool','POINT');d.energy=energy;d.color=(1,.25,.055);d.shadow_soft_size=.10
    o=bpy.data.objects.new(d.name,d);bpy.context.scene.collection.objects.link(o);o.location=(x,y-.32,z+.2)


def atmosphere():
    volume=bpy.data.materials.new('Fine cinematic courtyard dust');volume.use_nodes=True;tree=volume.node_tree;tree.nodes.clear()
    scatter=tree.nodes.new('ShaderNodeVolumePrincipled');scatter.inputs['Density'].default_value=.007;scatter.inputs['Color'].default_value=(.32,.40,.56,1);scatter.inputs['Anisotropy'].default_value=.32
    out=tree.nodes.new('ShaderNodeOutputMaterial');tree.links.new(scatter.outputs['Volume'],out.inputs['Volume'])
    base.cube('Airborne dust light volume',(0,3,5),(20,20,12),volume)


def world(c):
    env=c.assets/'Environments/Imported/Textures'
    stone=surface(c,'Original stone on continuous ancient walls',env/'monastery_stone_floor_alb.jpg',env/'monastery_stone_floor_nrm.jpg',scale=.63)
    floor=surface(c,'Continuous worn courtyard flagstone',env/'monastery_stone_floor_alb.jpg',env/'monastery_stone_floor_nrm.jpg',roughness=.88,scale=.44)
    base.cube('Continuous human scale stone ground',(0,-.5,-.16),(13.1,25,.32),floor,.025)
    # The floor ends under its joined walls. Its wooden support is outside all
    # early framing and becomes legible only at the final camera pullback.
    wood=surface(c,'Original wood tabletop revealed late',c.assets/'TownServices/Textures/dark_wooden_planks_alb.jpg',roughness=.74,scale=.18)
    base.cube('Late visible wooden game table',(0,-.5,-.70),(21,32,.70),wood,.16)
    arch(stone)
    # Heavy plinths join the chamber walls and floor rather than standing loose.
    for x in (-5.25,5.25):
        base.cube('Continuous wall foot plinth',(x,.0,.25),(.65,10.1,.5),stone,.025)
    prop(c,'wine_barrel_01',(-2.7,3.8,0),1.05,-10)
    prop(c,'wooden_crate_01',(-3.3,3.2,0),.72,15)
    prop(c,'wooden_crate_01',(-2.55,3.2,0),.55,-8)
    prop(c,'rock_moss_set_02',(-2.6,-.35,0),.24,38)
    prop(c,'rock_moss_set_01',(3.7,3.4,0),.20,-24)
    torch((-2.4,4.45,2.0));torch((2.4,4.45,2.0));torch((0,10.65,1.8),energy=55)
    base.area('Cold open roof light',(1.6,2.2,8.5),(0,0,1.3),(.24,.45,1),1350,4.0)
    base.area('Warm motivated foreground key',(-3.6,-1.2,3.2),(0,0,1.15),(1,.47,.19),550,1.5)
    base.area('Subtle cold face lift',(1.0,-3.0,2.3),(0,0,1.35),(.31,.45,.7),125,2.5)
    base.area('Cool subdued passage depth',(0,8.6,3.7),(0,5.2,1.4),(.19,.40,.75),180,1.4)
    base.area('Cold hero shoulder contour',(-2,1.4,4.2),(0,0,1.4),(.18,.42,1),450,1.2)
    atmosphere()
    actor=original_actor(c)
    INSTANCES.append({'kind':'cinematic continuous architecture','authority':'user revision requests film-world courtyard, no visible grid/table/void initially','dimensions_metres':[12,24,6.4],'arch_opening_metres':[2.5,3.65],'ground_metres':[13.1,25],'late_table_metres':[21,32]})
    return actor


def god(c, gaze=(0,0,1.4)):
    position=Vector((-1.1,2.2,6.90));target=Vector(gaze)
    rotation=(target-position).to_track_quat('-Y','Z').to_euler()
    meshes=base.import_asset(c,'Head/Mask_2.fbx',base.mask_mat(c,2),'Original Grimhorn focused on Brute',position,scale=26)
    for o in meshes:o.rotation_euler=rotation
    INSTANCES.append({'kind':'original Grimhorn god gaze','position':list(position),'gaze_target':list(target),'gaze_local_axis':'-Y','scale':26})
    base.area('God warm face bounce',(-3,-2,6.8),position,(1,.56,.28),1350,4.5)
    base.area('God cold sky rim',(3.5,3.0,10.5),position,(.20,.42,1),1800,4.0)


def source_player_pose(c,actor,frame):
    path=source(c.player_motion_helper)
    spec=importlib.util.spec_from_file_location('source_player_motion',path)
    driver=importlib.util.module_from_spec(spec);spec.loader.exec_module(driver)
    # The source-rig motion lane owns these poses. Use its reviewed trajectory
    # rather than the rejected pointing/palm-claw placeholder.
    report=driver.create_player_motion(bpy.context.scene,c.assets,actor,c.motion/'HE_Brute_Idle_v001.npz',duration=8.0,grimhorn_position=(-1.1,2.2,6.9))
    base.USED.update(driver.base.USED)
    for p in (path,path.with_name('render_gameplay.py'),path.with_name('render_assets.py')):
        if p.exists():source(p)
    bpy.context.scene.frame_set(frame);bpy.context.view_layer.update()
    (c.output/f'player-pose-trace-{bpy.context.scene.name}.json').write_text(json.dumps(report,indent=2)+'\n')
    INSTANCES.append({'kind':'source-rig human intervention','motion_helper':str(path),'duration_seconds':8,'reference_frame':frame,'timing':report['timing'],'head_contact_source_vertices':report['head_contact_cohort_vertices']})


def camera_identity_safe(cam,objects,margin=.025):
    scene=bpy.context.scene
    for _ in range(48):
        points=[world_to_camera_view(scene,cam,o.matrix_world@Vector(corner)) for o in objects for corner in o.bound_box]
        if all(margin<=p.x<=1-margin and margin<=p.y<=1-margin for p in points if p.z>0):return
        cam.data.lens*=.986
    raise ValueError('Cannot retain original mask silhouette with the specified camera')


def companion_lift(c,actor):
    # A close two-player reference supports the single next review. The final
    # motion camera owns the approved framing; a full table reveal is deferred.
    god(c);source_player_pose(c,actor,184)
    base.area('Late table warm light',(-10,-8,11),(0,0,0),(1,.52,.24),2300,12)
    base.area('Late companion/table cool lift',(10,-6,10),(6,0,1),(.23,.49,1),1500,10)


def reset(c,shot):
    scene=base.reset(c,shot);scene.render.film_transparent=False;scene.render.image_settings.color_mode='RGB';scene.view_settings.exposure=-.35
    tree=scene.world.node_tree;background=tree.nodes['Background'];background.inputs['Strength'].default_value=.38
    coordinates=tree.nodes.new('ShaderNodeTexCoord');cloud=tree.nodes.new('ShaderNodeTexNoise');cloud.inputs['Scale'].default_value=3.1;cloud.inputs['Detail'].default_value=3.4;cloud.inputs['Roughness'].default_value=.67
    tree.links.new(coordinates.outputs['Normal'],cloud.inputs['Vector'])
    ramp=tree.nodes.new('ShaderNodeValToRGB');ramp.color_ramp.elements[0].position=.34;ramp.color_ramp.elements[0].color=(.025,.055,.13,1);ramp.color_ramp.elements[1].position=.68;ramp.color_ramp.elements[1].color=(.12,.17,.24,1)
    tree.links.new(cloud.outputs['Fac'],ramp.inputs['Fac']);tree.links.new(ramp.outputs['Color'],background.inputs['Color'])
    scene.eevee.volumetric_samples=64
    return scene


def main():
    c=arguments();outputs=[]
    code=Path(__file__).read_bytes();digest=hashlib.sha256(code).hexdigest()
    capture=c.output/f'render-film-world-source-{digest[:12]}.py';capture.write_bytes(code)
    for shot in c.shots:
        scene=reset(c,shot)
        shot_config=SimpleNamespace(**vars(c))
        if shot!='opening':shot_config.pose='idle';shot_config.pose_frame=0
        actor=world(shot_config)
        if shot=='god_presence':
            god(c)
            cam=base.camera((1.4,-10.5,1.4),(-.15,2.0,4.2),lens=24,focus=(0,1.9,6.9),fstop=8)
            camera_identity_safe(cam,[o for o in bpy.data.objects if o.name.startswith('Original Grimhorn focused on Brute')])
            if c.save_base:
                bpy.data.orphans_purge(do_local_ids=True,do_linked_ids=False,do_recursive=True);bpy.ops.file.pack_all()
                p=c.output/'god_presence_base.blend';bpy.ops.wm.save_as_mainfile(filepath=str(p),compress=True);outputs.append(p)
                print('FILM_MOTION_BASE_READY',p,flush=True)
            if c.player_motion_helper:source_player_pose(c,actor,40)
            camera_identity_safe(cam,[o for o in bpy.data.objects if o.name.startswith('Original Grimhorn focused on Brute')])
        elif shot=='companion_lift':
            companion_lift(c,actor);cam=base.camera((-2.394,-18.370,8.099),(-.089,.789,4.039),lens=27.018,focus=(0,0,3.6),fstop=8)
            camera_identity_safe(cam,[o for o in bpy.data.objects if o.name.startswith(('Original Grimhorn focused on Brute','Original Runeveil'))])
        else:base.camera((.95,-4.4,1.43),(0,0,1.23),lens=48,focus=(0,0,1.25),fstop=4)
        if c.save_blend:
            bpy.data.orphans_purge(do_local_ids=True,do_linked_ids=False,do_recursive=True);bpy.ops.file.pack_all();p=c.output/f'{shot}.blend';bpy.ops.wm.save_as_mainfile(filepath=str(p),compress=True);outputs.append(p)
        scene.render.filepath=str(c.output/f'{shot}.png');bpy.ops.render.render(write_still=True);outputs.append(Path(scene.render.filepath));print('FILM_PLATE_READY',scene.render.filepath,flush=True)
    manifest={'representation':'Cinematic film-world staging using unchanged original actor and mod character assets; not gameplay capture','tool_sha256':digest,'tool_source_capture':str(capture),'blender':bpy.app.version_string,'resolution':[c.width,round(c.width*9/16)],'samples':c.samples,'native_motion_pose':c.pose,'native_motion_frame':c.pose_frame,'sources':[{'path':str(p),'sha256':base.sha(p)} for p in sorted(base.USED)],'instances':INSTANCES,'outputs':[{'path':str(p),'sha256':base.sha(p)} for p in outputs]}
    (c.output/f"film-world-manifest-{'-'.join(c.shots)}.json").write_text(json.dumps(manifest,indent=2)+'\n')

if __name__=='__main__':main()
