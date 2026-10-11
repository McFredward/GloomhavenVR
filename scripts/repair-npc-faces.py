#!/usr/bin/env python3
"""Join a separately reviewed head to the unchanged station costume (Blender 4.2).

This is explicit per-character authoring, not a general anatomical repair filter.
Original provider assets stay untouched. The derived head receives a separate texture
atlas and a protected LOD budget; no artificial facial animation readiness is claimed.
"""
import argparse
import hashlib
import json
import math
from pathlib import Path
import shutil
import sys
import bpy
import bmesh
import numpy as np
from mathutils import Matrix, Vector

PROFILES = {
    'merchant': {'height': .310, 'base': 1.440, 'depth': .010},
    'priestess': {'height': .280, 'base': 1.445, 'depth': -.030},
    'enchantress': {'height': .270, 'base': 1.450, 'depth': -.040},
}


def remove_old_face(obj, name):
    bm = bmesh.new(); bm.from_mesh(obj.data)
    def replaced(v):
        x, y, z = v
        if name == 'merchant':
            return (z > 1.535 and abs(x) < .168) or (z > 1.474 and abs(x) < .115 and y < .020)
        if name == 'priestess':
            # Keep the original hood and coif, including the white forehead band.
            width = np.interp(z, [1.395, 1.447, 1.49, 1.54, 1.60, 1.665, 1.686], [.092,.078,.065,.077,.080,.070,.052])
            return 1.395 < z < 1.686 and abs(x) < width and y < -.010 and not (z > 1.642 and y < -.155)
        width = np.interp(z, [1.478,1.51,1.56,1.615,1.67], [.028,.045,.063,.063,.043])
        return 1.478 < z < 1.67 and abs(x) < width and y < -.040
    # Preserve the actual hood border through its authored diffuse colour. A
    # rectangular face cut alone would notch the cloth rim above the forehead.
    material = obj.data.materials[0]
    shader = next(n for n in material.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    image = shader.inputs['Base Color'].links[0].from_node.image
    pixels = np.array(image.pixels[:]).reshape(image.size[1], image.size[0], 4)
    uv = bm.loops.layers.uv.active
    def clothing(face):
        centre = face.calc_center_median()
        if name == 'merchant':
            # Preserve the actual shirt collar and cloak behind the head, never the
            # red lips of the replaced source face.
            if centre.z > 1.535 and not (centre.y > .02 and centre.z < 1.56): return False
        if name == 'priestess' and 1.445 <= centre.z < 1.625: return False
        if name == 'enchantress' and (centre.z < 1.51 or (abs(centre.x) < .042 and centre.z < 1.606) or (abs(centre.x) < .061 and 1.575 < centre.z < 1.611 and centre.y > -.15)): return False
        point = sum((loop[uv].uv for loop in face.loops), Vector((0, 0))) / len(face.loops)
        r,g,b = pixels[min(image.size[1]-1,max(0,int(point.y*image.size[1]))),
                       min(image.size[0]-1,max(0,int(point.x*image.size[0]))),:3]
        if name == 'merchant':
            if max(r,g,b)-min(r,g,b) < .12 and max(r,g,b) < .45: return False
            if centre.y > .02 and centre.z < 1.56: return True
            if centre.z > 1.515: return False
            return centre.y > -.065 or (r > g * 1.45 and b > g * 1.05) or (min(r,g,b) > .36 and max(r,g,b)-min(r,g,b) < .20)
        if name == 'priestess' and centre.z < 1.445: return r < g * 1.10 or b > g * .9
        if name == 'enchantress': return (g > r * .96 and b > r * 1.15) or b > g * 1.18
        return centre.z > 1.64 and r > g * 1.35 and g > b * 1.3 and b < .24
    faces = [f for f in bm.faces if replaced(f.calc_center_median()) and not clothing(f)]
    count = len(faces)
    bmesh.ops.delete(bm, geom=faces, context='FACES')
    bm.to_mesh(obj.data); bm.free(); obj.data.update()
    return count



def continue_priestess_coif(body):
    """Continue the existing linen garment across the authored neck join."""
    # Reuse a small patch of the original robe atlas, including its normal map.
    mesh = body.data
    cloth_face = min(mesh.polygons, key=lambda f: (f.center - Vector((.14, -.13, 1.27))).length_squared)
    source_uv = mesh.uv_layers.active.data
    centre_uv = sum((source_uv[i].uv for i in cloth_face.loop_indices), Vector((0, 0))) / len(cloth_face.loop_indices)
    vertices, faces = [], []
    rings, segments = 20, 64
    for row in range(rings):
        t = row / (rings - 1)
        for column in range(segments):
            angle = 2 * math.pi * column / segments
            front = max(0, -math.sin(angle))
            height = (1-t) * (1.542 - .033 * front) + t * 1.375
            rx = .055 * (1-t) + .115 * t + .004*math.sin(math.pi*t)
            ry = .085 + .030*math.sin(math.pi*t) - .005*t
            fold = (.009 * math.sin(7 * angle + 2*t) + .006 * math.sin(4*math.pi*t+1.5*math.cos(angle))) * math.sin(math.pi*t)
            vertices.append(((rx+fold)*math.cos(angle), -.027 + (ry+fold)*math.sin(angle), height))
            if row:
                previous = (column+1) % segments
                faces.append(((row-1)*segments+column,row*segments+column,row*segments+previous,(row-1)*segments+previous))
    data = bpy.data.meshes.new('Linen coif continuation');data.from_pydata(vertices,[],faces);data.update()
    obj = bpy.data.objects.new('CoifContinuation',data);bpy.context.collection.objects.link(obj)
    data.materials.append(body.data.materials[0]);uv=data.uv_layers.new(name='UVMap')
    for face in data.polygons:
        face.use_smooth=True
        for index in face.loop_indices:
            vertex=data.loops[index].vertex_index;row,column=divmod(vertex,segments)
            uv.data[index].uv=centre_uv+Vector(((column/segments-.5)*.006,(row/(rings-1)-.5)*.008))
    return obj



def close_merchant_neckline(body):
    """Close the retained collar underneath the beard using original neck skin."""
    mesh = body.data
    skin_face = min(mesh.polygons, key=lambda f: (f.center - Vector((0, -.04, 1.46))).length_squared)
    source_uv = mesh.uv_layers.active.data
    centre_uv = sum((source_uv[i].uv for i in skin_face.loop_indices), Vector((0, 0))) / len(skin_face.loop_indices)
    vertices, faces = [], []
    rings, segments = 10, 48
    for row in range(rings):
        t = row/(rings-1)
        for column in range(segments):
            angle = 2*math.pi*column/segments
            vertices.append(((.061+.009*t)*math.cos(angle), .025+(.052+.008*t)*math.sin(angle), 1.44+.12*t))
            if row:
                next_column = (column+1)%segments
                faces.append(((row-1)*segments+column,(row-1)*segments+next_column,row*segments+next_column,row*segments+column))
    faces.append(tuple(reversed(range(segments))))
    faces.append(tuple((rings-1)*segments+i for i in range(segments)))
    data=bpy.data.meshes.new('Inner neck closure');data.from_pydata(vertices,[],faces);data.update()
    obj=bpy.data.objects.new('NeckClosure',data);bpy.context.collection.objects.link(obj)
    data.materials.append(body.data.materials[0]);uv=data.uv_layers.new(name='UVMap')
    for face in data.polygons:
        face.use_smooth=True
        for index in face.loop_indices:
            row,column=divmod(data.loops[index].vertex_index,segments)
            uv.data[index].uv=centre_uv+Vector(((column/segments-.5)*.004,(row/(rings-1)-.5)*.006))
    return obj


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--body',type=Path,required=True)
    parser.add_argument('--head',type=Path,required=True)
    parser.add_argument('--output',type=Path,required=True)
    parser.add_argument('--name',choices=PROFILES,required=True)
    a=parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    if a.output.exists(): raise ValueError('Use a new output directory')
    a.output.mkdir(parents=True); (a.output/'meshes').mkdir(); (a.output/'textures').mkdir()
    body=json.loads((a.body/'manifest.json').read_text()); head=json.loads((a.head/'manifest.json').read_text())
    bpy.ops.wm.open_mainfile(filepath=str(a.body/'source.blend'))
    bodies=[o for o in bpy.context.scene.objects if o.type=='MESH']
    removed={o.name:remove_old_face(o,a.name) for o in bodies}
    for obj in bodies:
        for material in obj.data.materials: material.name='TownBody'
    if a.name == 'priestess': bodies.append(continue_priestess_coif(bodies[0]))
    if a.name == 'merchant': bodies.append(close_merchant_neckline(bodies[0]))
    before=set(bpy.context.scene.objects)
    source=next(d for d in head['derivatives'] if d['name'].endswith('_source'))
    bpy.ops.import_scene.gltf(filepath=str(a.head/source['glb']))
    heads=[o for o in bpy.context.scene.objects if o not in before and o.type=='MESH']
    profile=PROFILES[a.name]
    for obj in heads:
        obj.data.transform(obj.matrix_world); obj.parent=None; obj.matrix_world=Matrix.Identity(4)
        obj.name='FaceSource'
        for material in obj.data.materials: material.name='TownFace'
        # Open the generated neck base and tuck the short stump inside the original
        # collar. A closed bust cap would otherwise become a visible horizontal shelf.
        bm = bmesh.new(); bm.from_mesh(obj.data)
        bottom = [f for f in bm.faces if f.calc_center_median().z < .014 or
                  (a.name != 'merchant' and f.calc_center_median().z < .085 and abs(f.calc_center_median().x) > (.050 if a.name == 'enchantress' else .060))]
        if a.name == 'enchantress':
            # The original purple hair is retained. The generated head's straight
            # side hair is not an anatomical cheek/ear and must stay inside it.
            bottom += [face for face in bm.faces if face not in bottom and
                       face.calc_center_median().z < .20 and abs(face.calc_center_median().x) >
                       float(np.interp(face.calc_center_median().z, [0,.08,.105,.155,.19,.20], [.043,.045,.057,.060,.055,.065]))]
        bmesh.ops.delete(bm, geom=bottom, context='FACES')
        for vertex in bm.verts:
            z = vertex.co.z
            if z < .060 and a.name != 'merchant':
                blend = 1 - max(0, z) / .060
                vertex.co.z -= (.085 if a.name == 'priestess' else .070) * blend
                if a.name == 'priestess':
                    vertex.co.x *= 1 + .30 * blend
                    vertex.co.y *= 1 + .20 * blend
            # The replacement scalp stays inside the retained native hood.
            if a.name != 'merchant' and z > .19:
                blend = min(1, (z - .19) / .065)
                vertex.co.x *= 1 - .14 * blend
                vertex.co.y *= 1 - .15 * blend
                vertex.co.z -= .012 * blend
        bm.to_mesh(obj.data); bm.free(); obj.data.update()
        obj.data.transform(Matrix.Translation((0,profile['depth'],profile['base'])))
    # Each atlas keeps its own material slot and UVs. No rebake/downsampling of the face.
    materials=[]
    for prefix,folder,manifest in [('body',a.body,body),('face',a.head,head)]:
        for source_record in manifest['materials']:
            record=dict(source_record)
            for key in ('baseColor','normal','unityMetallicSmoothness'):
                if record.get(key):
                    rel=Path('textures')/(prefix+'_'+Path(record[key]).name)
                    shutil.copy2(folder/record[key],a.output/rel);record[key]=str(rel)
            materials.append(record)
    bpy.ops.object.select_all(action='DESELECT')
    for obj in bodies+heads: obj.select_set(True)
    bpy.context.view_layer.objects.active=bodies[0]
    bpy.ops.object.join(); combined=bpy.context.object; combined.name='BodyAndFace'
    bpy.ops.file.pack_all(); bpy.ops.wm.save_as_mainfile(filepath=str(a.output/'source.blend'))
    glb='meshes/'+a.name+'_source.glb'
    bpy.ops.export_scene.gltf(filepath=str(a.output/glb),export_format='GLB',use_selection=True,export_animations=False)
    result=dict(body);result['materials']=materials
    result['sourceTriangles']=len(combined.data.polygons)
    result['derivatives']=[{'name':a.name+'_source','glb':glb}]
    result['faceAuthoring']={'bodyInputSha256':body['inputSha256'],'headInputSha256':head['inputSha256'],
        'profile':profile,'removedOriginalFaces':removed,'separateHeadAtlas':True,
        'limitations':['Neutral closed-mouth head; no mouth interior or facial animation is claimed.']}
    (a.output/'manifest.json').write_text(json.dumps(result,indent=2)+'\n')
    print('NPC_FACE_ASSEMBLED',a.name,result['sourceTriangles'],removed)

if __name__=='__main__': main()
