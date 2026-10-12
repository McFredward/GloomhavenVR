#!/usr/bin/env python3
"""Render source-faithful character identity references, never new trailer footage.

Native face geometry, UVs, original textures and horn placement are unchanged.
Neutral reference lighting is intentionally separate from cinematic art direction.
"""
import argparse
import importlib.util
import json
import math
import sys
from pathlib import Path

import bpy
from bpy_extras.object_utils import world_to_camera_view
from mathutils import Vector


def module(name,filename):
    path=Path(__file__).with_name(filename)
    spec=importlib.util.spec_from_file_location(name,path);result=importlib.util.module_from_spec(spec);spec.loader.exec_module(result)
    return result


assets=module('reference_assets','render_assets.py')
opener=module('reference_opener','render_opener.py')
film=module('reference_film','render_film_world.py')
SHOTS=('brute_front','brute_threequarter','grimhorn_glove','runeveil_arcane','livingbones_front')


def arguments():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--assets',type=Path,default=Path(__file__).resolve().parents[2]/'unity/GloomhavenVR.Assets/Assets/Bundle')
    p.add_argument('--native-scene',type=Path,default=Path('/home/claw/gloomhaven_vr/.planning/debug/trailer/native-scene'))
    p.add_argument('--motion',type=Path,default=Path('/home/claw/gloomhaven_vr/.planning/debug/trailer/motion/brute'))
    p.add_argument('--output',type=Path,required=True)
    p.add_argument('--shots',default=','.join(SHOTS))
    p.add_argument('--width',type=int,default=1536)
    p.add_argument('--samples',type=int,default=48)
    p.add_argument('--save-blend',action='store_true')
    c=p.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
    c.shots=c.shots.split(',');c.assets=c.assets.resolve();c.output=c.output.resolve()
    c.native=c.native_scene;c.pose='idle';c.pose_frame=0
    if any(s not in SHOTS for s in c.shots):p.error('unknown reference')
    c.output.mkdir(parents=True,exist_ok=True)
    return c


def reset(c,shot):
    scene=assets.reset(c,shot)
    scene.render.resolution_y=c.width
    scene.render.film_transparent=False
    scene.render.image_settings.color_mode='RGB'
    scene.view_settings.view_transform='AgX'
    scene.view_settings.look='AgX - Medium High Contrast'
    scene.view_settings.exposure=0
    background=scene.world.node_tree.nodes['Background']
    background.inputs['Color'].default_value=(.085,.095,.11,1)
    background.inputs['Strength'].default_value=.45
    return scene


def light(target,scale=1):
    center=Vector(target)
    for name,offset,energy,color,size in [('Broad neutral key',(-1.6,-2,1.9),310,(1,.90,.80),2.0),('Soft face/eye fill',(1.6,-1.5,.4),155,(.77,.87,1),1.7),('Neutral silhouette rim',(.9,.9,1.4),230,(.78,.86,1),1.4)]:
        assets.area(name,center+Vector(offset)*scale,center,color,energy*scale*scale,size*scale)


def brute(c,threequarter=False):
    obj=film.original_actor(c)
    obj.rotation_euler.z=0
    # A diffuse-only presentation keeps painted facial features legible. It
    # changes the reference light response, never source colors, UVs or topology.
    for mat in obj.data.materials:
        shader=mat.node_tree.nodes['Principled BSDF']
        for key,value in [('Metallic',0),('Roughness',.82),('Specular IOR Level',.15)]:
            for link in list(shader.inputs[key].links):mat.node_tree.links.remove(link)
            shader.inputs[key].default_value=value
    center=(0,-.05,1.74)
    light(center)
    camera=assets.camera((.90,-1.66,1.80) if threequarter else (0,-1.80,1.76),center,lens=68)
    camera.data.dof.use_dof=False
    return {'actor':'original Brute','pose':'native idle frame0','reference':'three-quarter' if threequarter else 'front','focus_metres':center,'identity_priority':'Original snout, brow, cheek volume, eye placement, jaw/tusks and complete horn arrangement'}


def grimhorn(c):
    assets.import_asset(c,'Head/Mask_2.fbx',assets.mask_mat(c,2),'Original Grimhorn identity',(0,0,.18),scale=1.2)
    assets.import_asset(c,'Hands/VRHand_R_rig.fbx',assets.hand_mat(c,'VRHand'),'Original fingerless leather glove',(.015,0,-.11),euler=(0,0,-25),scale=.8,curl=.10)
    light((0,0,.07),.26)
    camera=assets.camera((.055,-.94,.10),(0,0,.065),lens=60);camera.data.dof.use_dof=False
    return {'actor':'Original Grimhorn Mask_2 and fingerless leather glove','identity_priority':'Preserve skull silhouette, two curved mask horns, painted source marks, original exposed fingertips and glove cuffs'}


def runeveil(c):
    assets.import_asset(c,'Head/Mask_1.fbx',assets.mask_mat(c,1),'Original Runeveil identity',(0,0,.19),scale=1.2)
    for side,x in [('L',-.12),('R',.12)]:
        assets.import_asset(c,f'Hands/VRHandArcane_{side}_rig.fbx',assets.hand_mat(c,'VRHandArcane'),f'Original arcane hand {side}',(x,0,-.10),euler=(0,0,198 if side=='L' else 162),scale=.65,curl=.10)
    light((0,0,.07),.28)
    camera=assets.camera((.035,-.96,.11),(0,0,.065),lens=60);camera.data.dof.use_dof=False
    return {'actor':'Original Runeveil Mask_1 and arcane hands','identity_priority':'Original pale mask, eyebrow/eye geometry, source purple rune markings and original blue/cyan hand glyphs'}


def livingbones(c):
    objects=opener.native_actor(c,'livingbones',(0,0,0),height=2.1,yaw=0)
    light((0,0,1.2),1.3)
    camera=assets.camera((.45,-3.8,1.4),(0,0,1.10),lens=58);camera.data.dof.use_dof=False
    bpy.context.view_layer.update()
    # Uniformly widen the camera until all original feet, sword and skull remain
    # visible. Reference framing must not hide source limb/silhouette evidence.
    for _ in range(24):
        points=[world_to_camera_view(bpy.context.scene,camera,o.matrix_world@v.co) for o in objects for v in o.data.vertices]
        if all(.035<=p.x<=.965 and .035<=p.y<=.965 for p in points):break
        camera.data.lens*=.975
    else:raise ValueError('Original Living Bones does not fit reference framing')
    return {'actor':'Original Living Bones','pose':'native idle frame0','identity_priority':'Original skull, skeletal anatomy, armor, swords and limb count'}


def main():
    c=arguments();records=[]
    tool_digest=assets.sha(__file__)
    capture=c.output/f'render-identity-source-{tool_digest[:12]}.py';capture.write_bytes(Path(__file__).read_bytes())
    helper_hashes={name:assets.sha(Path(__file__).with_name(name)) for name in ('render_assets.py','render_opener.py','render_film_world.py')}
    for shot in c.shots:
        scene=reset(c,shot)
        if shot.startswith('brute'):identity=brute(c,shot.endswith('threequarter'))
        elif shot=='grimhorn_glove':identity=grimhorn(c)
        elif shot=='runeveil_arcane':identity=runeveil(c)
        else:identity=livingbones(c)
        outputs=[]
        if c.save_blend:
            bpy.data.orphans_purge(do_local_ids=True,do_linked_ids=False,do_recursive=True);bpy.ops.file.pack_all()
            blend=c.output/f'{shot}.blend';bpy.ops.wm.save_as_mainfile(filepath=str(blend),compress=True);outputs.append(blend)
        scene.render.filepath=str(c.output/f'{shot}.png');bpy.ops.render.render(write_still=True);outputs.append(Path(scene.render.filepath))
        used=assets.USED|film.base.USED|opener.base.USED
        record={'reference':shot,**identity,'representation':'Source-faithful static identity reference; not trailer footage or gameplay capture','resolution':[c.width,c.width],'samples':c.samples,'blender':bpy.app.version_string,'tool_sha256':tool_digest,'tool_source_capture':str(capture),'helper_sha256':helper_hashes,'sources':[{'path':str(p),'sha256':assets.sha(p)} for p in sorted(used)],'outputs':[{'path':str(p),'sha256':assets.sha(p)} for p in outputs]}
        (c.output/f'{shot}-manifest.json').write_text(json.dumps(record,indent=2)+'\n');records.append(record)
        print('IDENTITY_REFERENCE_READY',scene.render.filepath,flush=True)
    aggregate=c.output/'identity-reference-manifest.json'
    previous=json.loads(aggregate.read_text()).get('references',[]) if aggregate.exists() else []
    merged={r['reference']:r for r in previous}
    merged.update({r['reference']:r for r in records})
    aggregate.write_text(json.dumps({'references':list(merged.values())},indent=2)+'\n')

if __name__=='__main__':main()
