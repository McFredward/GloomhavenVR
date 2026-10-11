#!/usr/bin/env python3
"""Restore real lower-body articulation on the NPC station rig, preserving face/hands.

Run with Blender 4.2 --background --python this.py -- --input ... --output ...
Only weights below the pelvis change. Coincident UV-seam vertices share decisions.
"""
import argparse,json,sys
from pathlib import Path
import bpy
import numpy as np

def smooth(a,b,v):
    t=np.clip((v-a)/(b-a),0,1);return t*t*(3-2*t)

def main():
    p=argparse.ArgumentParser();p.add_argument('--input',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--name',required=True);a=p.parse_args(sys.argv[sys.argv.index('--')+1:]);a.output.mkdir(parents=True,exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=str(a.input));rig=next(o for o in bpy.data.objects if o.type=='ARMATURE');report=[]
    for obj in bpy.data.objects:
        if obj.type!='MESH' or not any(m.type=='ARMATURE' and m.object==rig for m in obj.modifiers):continue
        before=len(obj.data.vertices);changed=0; seams={}; protected={}
        lower={'Hips','Thigh.L','Thigh.R','Shin.L','Shin.R','Foot.L','Foot.R'}
        for v in obj.data.vertices:
            weights=tuple((obj.vertex_groups[g.group].name,g.weight) for g in v.groups)
            if any(n not in lower and w>1e-6 for n,w in weights):protected[v.index]=weights
        for v in obj.data.vertices:
            co=np.array(tuple(v.co));x,y,z=np.round(co,5)
            if z>=.96:continue
            # Neutral hands extend below the pelvis. A height-only mask would
            # rebind their skin to the legs and tear the wrists during arm IK.
            if any(obj.vertex_groups[g.group].name not in lower and g.weight>1e-6 for g in v.groups):continue
            # The imported generated rig formerly assigned every lower-body vertex to
            # Hips. Give actual legs/boots their chain and softly divide long fabric.
            hip=float(smooth(.76,.94,z));side=float(smooth(-.04,.04,x));knee=float(smooth(.40,.53,z));foot=float(1-smooth(.15,.24,z))
            raw={'Hips':hip}
            for suffix,w in [('L',side),('R',1-side)]:
                raw['Thigh.'+suffix]=(1-hip)*w*knee
                raw['Shin.'+suffix]=(1-hip)*w*(1-knee)*(1-foot)
                raw['Foot.'+suffix]=(1-hip)*w*(1-knee)*foot
            weights=sorted(raw.items(),key=lambda q:q[1],reverse=True)[:4];total=sum(w for _,w in weights)
            if total<=0:raise ValueError('Unweighted lower body')
            for group in obj.vertex_groups:group.remove([v.index])
            for name,w in weights:
                if w<1e-7:continue
                group=obj.vertex_groups.get(name) or obj.vertex_groups.new(name=name);group.add([v.index],w/total,'REPLACE')
            key=tuple(np.round(co,5));value=tuple((n,round(w/total,7)) for n,w in weights)
            if key in seams and seams[key]!=value:raise ValueError('UV seam lower-weight mismatch')
            seams[key]=value;changed+=1
        for index,original in protected.items():
            current=tuple((obj.vertex_groups[g.group].name,g.weight) for g in obj.data.vertices[index].groups)
            if current!=original:raise ValueError('Non-leg weights changed at '+obj.name+':'+str(index))
        report.append(dict(mesh=obj.name,vertices=before,changed=changed,protected_vertices=len(protected),non_leg_weights_unchanged=True))
    bpy.ops.wm.save_as_mainfile(filepath=str(a.output/'rig-source.blend'))
    bpy.ops.object.select_all(action='DESELECT')
    for obj in bpy.data.objects:
        if obj.type in ('MESH','ARMATURE','EMPTY'):obj.select_set(True)
    bpy.context.view_layer.objects.active=rig
    bpy.context.scene.frame_set(1)
    bpy.ops.export_scene.fbx(filepath=str(a.output/(a.name+'_rig.fbx')),use_selection=True,object_types={'MESH','ARMATURE','EMPTY'},add_leaf_bones=False,bake_anim=True,bake_anim_use_nla_strips=False,bake_anim_use_all_actions=True,bake_anim_force_startend_keying=True,bake_anim_simplify_factor=0,axis_forward='-Z',axis_up='Y',path_mode='STRIP')
    (a.output/'leg-weights.json').write_text(json.dumps(report,indent=2)+'\n')
    print('TOWN_LEG_WEIGHTS_OK',sum(x['changed'] for x in report))
if __name__=='__main__':main()
