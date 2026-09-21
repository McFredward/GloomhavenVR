#!/usr/bin/env python3
"""Join a separately reviewed head to the unchanged station costume (Blender 4.2).

This is explicit per-character authoring, not a general anatomical repair filter.
Original provider assets stay untouched. The derived head receives a separate texture
atlas and a protected LOD budget; no artificial facial animation readiness is claimed.
"""
import argparse
import hashlib
import json
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
            return (z > 1.535 and abs(x) < .168) or (z > 1.474 and abs(x) < .105 and y < .020)
        if name == 'priestess':
            # Keep the original hood and coif, including the white forehead band.
            width = np.interp(z, [1.447, 1.49, 1.54, 1.60, 1.665, 1.686], [.045,.052,.077,.080,.070,.052])
            return 1.447 < z < 1.686 and abs(x) < width and y < -.010 and not (z > 1.642 and y < -.155)
        width = np.interp(z, [1.478,1.51,1.56,1.615,1.67], [.028,.045,.063,.063,.043])
        return 1.478 < z < 1.67 and abs(x) < width and y < -.040 and not (z > 1.62 and y < -.185)
    # Preserve the actual hood border through its authored diffuse colour. A
    # rectangular face cut alone would notch the cloth rim above the forehead.
    material = obj.data.materials[0]
    shader = next(n for n in material.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
    image = shader.inputs['Base Color'].links[0].from_node.image
    pixels = np.array(image.pixels[:]).reshape(image.size[1], image.size[0], 4)
    uv = bm.loops.layers.uv.active
    def clothing(face):
        centre = face.calc_center_median()
        if name == 'merchant' or centre.z < 1.625: return False
        point = sum((loop[uv].uv for loop in face.loops), Vector((0, 0))) / len(face.loops)
        r,g,b = pixels[min(image.size[1]-1,max(0,int(point.y*image.size[1]))),
                       min(image.size[0]-1,max(0,int(point.x*image.size[0]))),:3]
        if name == 'enchantress': return g > r * 1.10 or b > g * 1.18
        return centre.z > 1.64 and r > g * 1.35 and g > b * 1.3 and b < .24
    faces = [f for f in bm.faces if replaced(f.calc_center_median()) and not clothing(f)]
    count = len(faces)
    bmesh.ops.delete(bm, geom=faces, context='FACES')
    bm.to_mesh(obj.data); bm.free(); obj.data.update()
    return count


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
        bottom = [f for f in bm.faces if f.calc_center_median().z < .007 and f.normal.z < -.15]
        bmesh.ops.delete(bm, geom=bottom, context='FACES')
        for vertex in bm.verts:
            z = vertex.co.z
            if z < .045:
                vertex.co.z -= .045 * (1 - max(0, z) / .045)
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
