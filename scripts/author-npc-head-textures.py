#!/usr/bin/env python3
"""Author head geometry and camera-projected albedo from approved head references.

Blender-only offline derivative. It never repaints the input images or replaces raw
provider files. The three reference cameras are baked into a fresh UV atlas; this
avoids relying on the provider's visibly broken texture/UV correspondence.
"""
import argparse,json,math,sys,hashlib,runpy
from pathlib import Path
import bpy,bmesh,numpy as np
from mathutils import Vector

BOUNDS={
 'merchant':[(132,18,592,703),(66,12,633,699),(116,13,607,679)],
 'priestess':[(156,26,559,699),(111,25,610,700),(156,29,561,692)],
 'enchantress':[(77,12,651,708),(34,6,638,717),(77,13,649,710)],
}

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--input',type=Path,required=True);p.add_argument('--references',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--name',choices=BOUNDS,required=True);p.add_argument('--surface',type=Path)
 a=p.parse_args(sys.argv[sys.argv.index('--')+1:]);a.output.mkdir(parents=True,exist_ok=False)
 (a.output/'textures').mkdir();(a.output/'meshes').mkdir()
 d=json.loads((a.input/'manifest.json').read_text());bpy.ops.wm.open_mainfile(filepath=str(a.input/'source.blend'))
 obj=next(o for o in bpy.context.scene.objects if o.type=='MESH');bpy.context.view_layer.objects.active=obj;obj.select_set(True)
 if a.surface:
  bpy.data.objects.remove(obj,do_unlink=True)
  bpy.ops.wm.ply_import(filepath=str(a.surface),forward_axis='Y',up_axis='Z')
  obj=bpy.context.object
  bpy.context.view_layer.objects.active=obj;obj.select_set(True)
 bm=bmesh.new();bm.from_mesh(obj.data)
 bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.000001)
 bmesh.ops.dissolve_degenerate(bm,edges=list(bm.edges),dist=.0000001)
 bm.to_mesh(obj.data);bm.free()
 # Seal open generated hair shells before volumetric cleanup. The merchant
 # replacement uses Trellis instead of the rejected Hunyuan lip-cavity source.
 bm=bmesh.new();bm.from_mesh(obj.data)
 bmesh.ops.holes_fill(bm,edges=[e for e in bm.edges if e.is_boundary],sides=0)
 bm.to_mesh(obj.data);bm.free()
 bm=bmesh.new();bm.from_mesh(obj.data);bmesh.ops.recalc_face_normals(bm,faces=list(bm.faces));bm.to_mesh(obj.data);bm.free()
 # The provider's tiny hair cavities and duplicate inner shells are not useful
 # close-view detail. Voxel reconstruction keeps the outer anatomy continuous.
 obj.data.remesh_voxel_size=.00065;obj.data.remesh_voxel_adaptivity=0
 if not a.surface: bpy.ops.object.voxel_remesh()
 for face in obj.data.polygons:face.use_smooth=True
 smooth=obj.modifiers.new('Submillimetre surface cleanup','SMOOTH');smooth.factor=.35;smooth.iterations=3
 bpy.ops.object.modifier_apply(modifier=smooth.name)
 dec=obj.modifiers.new('Head authoring surface','DECIMATE');dec.ratio=min(1,90000/len(obj.data.polygons));dec.use_collapse_triangulate=True
 bpy.ops.object.modifier_apply(modifier=dec.name)
 if a.name == 'merchant':
  smooth=obj.modifiers.new('Closed beard surface cleanup','SMOOTH');smooth.factor=.5;smooth.iterations=6
  bpy.ops.object.modifier_apply(modifier=smooth.name)
 obj.data.normals_split_custom_set([(0,0,0)]*len(obj.data.loops))
 bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT');bpy.ops.uv.smart_project(angle_limit=math.radians(70),island_margin=.006);bpy.ops.object.mode_set(mode='OBJECT')
 obj.data.uv_layers.active.name='HeadAtlas'
 atlas=obj.data.uv_layers.active
 xyz=np.array([v.co[:] for v in obj.data.vertices]);lo=xyz.min(0);hi=xyz.max(0);span=hi-lo
 vertex_weights=[]
 for v in obj.data.vertices:
  x,y,z=v.co
  # Blend using the head's cylindrical position, not fine facial normals. A nose
  # sidewall must not suddenly project a profile eye over the frontal face.
  angle=abs(math.atan2(x,-(y-(lo[1]+hi[1])*.5)))
  front=max(0,min(1,(math.radians(75)-angle)/math.radians(25)))
  back=max(0,min(1,(angle-math.radians(110))/math.radians(30)))
  if a.name == 'priestess' and z < .06 and angle < math.radians(105): front,back=1,0
  vertex_weights.append((front,back,1-front-back,1))
 colour=obj.data.color_attributes.new(name='ReferenceWeights',type='FLOAT_COLOR',domain='CORNER')
 for loop in obj.data.loops:colour.data[loop.index].color=vertex_weights[loop.vertex_index]
 for k,label in enumerate(('front','left','back')):
  layer=obj.data.uv_layers.new(name='Reference_'+label);x0,y0,x1,y1=BOUNDS[a.name][k]
  for loop in obj.data.loops:
   x,y,z=xyz[loop.vertex_index];u=(x-lo[0])/span[0] if k!=1 else 1-(y-lo[1])/span[1]
   if k==2:u=1-u
   mapped_z=z
   if a.name=='priestess' and k==0:
    mapped_z=float(np.interp(z,[0,.055,.094,.133,.164,.191,.28],[0,.055,.102,.141,.175,.200,.28]))
   if a.name=='enchantress' and k==0:
    mapped_z=float(np.interp(z,[0,.065,.092,.128,.165,.205,.27],[0,.065,.095,.130,.169,.205,.27]))
   if a.name == 'priestess' and z < .060:
    # Extend the existing neck colour down the tucked collar section; projecting
    # beyond the photographed cut neck would bake its background onto the skin.
    mapped_z=.010+max(0,z)*(.050/.060)
    u=.5+(u-.5)*.65
   v=(mapped_z-lo[2])/span[2]
   layer.data[loop.index].uv=((x0+u*(x1-x0))/718,(718-y1+v*(y1-y0))/718)
 obj.data.uv_layers.active=obj.data.uv_layers['HeadAtlas']
 material=bpy.data.materials.new('TownFace');material.use_nodes=True;nodes=material.node_tree.nodes;links=material.node_tree.links;nodes.clear()
 output=nodes.new('ShaderNodeOutputMaterial');emission=nodes.new('ShaderNodeEmission');links.new(emission.outputs[0],output.inputs[0])
 images=[];textures=[]
 for label in ('front','left','back'):
  image=bpy.data.images.load(str(a.references/(label+'.png')),check_existing=False);images.append(image)
  tex=nodes.new('ShaderNodeTexImage');tex.image=image;tex.extension='EXTEND';uv=nodes.new('ShaderNodeUVMap');uv.uv_map='Reference_'+label;links.new(uv.outputs[0],tex.inputs[0]);textures.append(tex)
 color=nodes.new('ShaderNodeVertexColor');color.layer_name='ReferenceWeights';split=nodes.new('ShaderNodeSeparateColor');links.new(color.outputs[0],split.inputs[0])
 mix=nodes.new('ShaderNodeMixRGB');links.new(split.outputs[0],mix.inputs[0]);links.new(textures[1].outputs[0],mix.inputs[1]);links.new(textures[0].outputs[0],mix.inputs[2])
 mixback=nodes.new('ShaderNodeMixRGB');links.new(split.outputs[1],mixback.inputs[0]);links.new(mix.outputs[0],mixback.inputs[1]);links.new(textures[2].outputs[0],mixback.inputs[2]);links.new(mixback.outputs[0],emission.inputs[0])
 obj.data.materials.clear();obj.data.materials.append(material)
 target=bpy.data.images.new('head_albedo',width=4096,height=4096,alpha=False);target.colorspace_settings.name='sRGB';bake=nodes.new('ShaderNodeTexImage');bake.image=target;nodes.active=bake
 scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=1;scene.render.bake.margin=16;scene.render.bake.use_clear=True
 bpy.ops.object.bake(type='EMIT')
 target.filepath_raw=str(a.output/'textures/head_basecolor.png');target.file_format='PNG';target.save()
 # This deliberately matte dielectric replaces the generated glossy mask. The
 # detail comes from the reviewed geometry and reference albedo, not false metal.
 nodes.clear();bsdf=nodes.new('ShaderNodeBsdfPrincipled');output=nodes.new('ShaderNodeOutputMaterial');tex=nodes.new('ShaderNodeTexImage');tex.image=target;links.new(tex.outputs[0],bsdf.inputs['Base Color']);bsdf.inputs['Roughness'].default_value=.68;bsdf.inputs['Metallic'].default_value=0;bsdf.inputs['Specular IOR Level'].default_value=.3;links.new(bsdf.outputs[0],output.inputs[0])
 for layer_name in [layer.name for layer in obj.data.uv_layers]:
  if layer_name!='HeadAtlas':obj.data.uv_layers.remove(obj.data.uv_layers[layer_name])
 bpy.ops.file.pack_all();bpy.ops.wm.save_as_mainfile(filepath=str(a.output/'source.blend'))
 glb='meshes/'+a.name+'_source.glb';bpy.ops.export_scene.gltf(filepath=str(a.output/glb),export_format='GLB',use_selection=True,export_animations=False)
 d['materials']=[{'name':'TownFace','baseColor':'textures/head_basecolor.png','normal':'','unityMetallicSmoothness':'','baseColorFactor':[1,1,1,1],'metallicFactor':0,'roughnessFactor':.68,'normalScale':0}]
 d['derivatives']=[{'name':a.name+'_source','glb':glb}];d['sourceTriangles']=len(obj.data.polygons)
 d['referenceProjection']={'surfaceSha256':hashlib.sha256(a.surface.read_bytes()).hexdigest() if a.surface else None,'bounds':BOUNDS[a.name],'headOnlyAlbedo':4096,'voxelMetres':.00065,'sourceReferences':{l:hashlib.sha256((a.references/(l+'.png')).read_bytes()).hexdigest() for l in ('front','left','back')},'noFacialAnimationClaim':True}
 (a.output/'manifest.json').write_text(json.dumps(d,indent=2)+'\n');print('HEAD_PROJECTION_READY',a.name,len(obj.data.polygons))

if __name__=='__main__':main()
