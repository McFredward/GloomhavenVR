#!/usr/bin/env python3
"""Offline GLB -> normalized artist source and static Unity LOD candidates.

Run with Python (launches Blender) or blender --background --python this.py -- ...
No credentials, network, source anatomy edits, rigging or animation generation.
"""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import re
import shutil
import struct
import subprocess
import sys


def arguments():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', required=True, type=Path)
    parser.add_argument('--output-dir', required=True, type=Path)
    parser.add_argument('--name', required=True)
    parser.add_argument('--yaw-degrees', type=float, default=0,
                        help='Rotation about Blender Z after glTF import; inspect facing visually.')
    parser.add_argument('--height', type=float, default=1.75)
    parser.add_argument('--source-only', action='store_true', help='Normalize and preserve source only; skip LOD candidate authoring.')
    parser.add_argument('--roughness-floor', type=float, default=0.0,
                        help='Optional derived Unity material: max(roughness, floor * (1-metallic)). Source GLB stays unchanged.')
    parser.add_argument('--blender', default=os.environ.get('BLENDER', 'blender'))
    return parser.parse_args(sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else None)


def sha256(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def glb_contents(path):
    data = path.read_bytes()
    if data[:4] != b'glTF' or struct.unpack_from('<I', data, 4)[0] != 2:
        raise ValueError('Expected a glTF 2.0 binary file')
    document, binary = None, None
    offset = 12
    while offset < len(data):
        length, kind = struct.unpack_from('<II', data, offset)
        chunk = data[offset + 8:offset + 8 + length]
        if kind == 0x4E4F534A:
            document = json.loads(chunk)
        elif kind == 0x004E4942:
            binary = chunk
        offset += 8 + length
    if document is None or binary is None:
        raise ValueError('Expected JSON and embedded binary GLB chunks')
    if any('uri' in b for b in document.get('buffers', [])):
        raise ValueError('External buffers are unsupported; use a self-contained GLB')
    return document, binary


def weld_lod_source(obj):
    """Connect coincident UV seam vertices only on a derivative before decimation.

    UVs and imported normals belong to face corners and remain distinct across seams.
    Decimating disconnected UV islands independently creates real silhouette cracks.
    """
    import bmesh
    import numpy as np
    mesh = obj.data
    uv_before = []
    for layer in mesh.uv_layers:
        values = np.empty(len(layer.data) * 2, dtype=np.float32)
        layer.data.foreach_get('uv', values)
        uv_before.append(values)
    normals = [tuple(n.vector) for n in mesh.corner_normals]
    materials = np.empty(len(mesh.polygons), dtype=np.int32)
    mesh.polygons.foreach_get('material_index', materials)
    vertex_count, face_count, loop_count = len(mesh.vertices), len(mesh.polygons), len(mesh.loops)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=1e-6)
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    if len(mesh.polygons) != face_count or len(mesh.loops) != loop_count or len(mesh.uv_layers) != len(uv_before):
        raise RuntimeError('Derivative weld changed face/loop/UV-layer count; individual topology review required')
    after_materials = np.empty(len(mesh.polygons), dtype=np.int32)
    mesh.polygons.foreach_get('material_index', after_materials)
    if not np.array_equal(materials, after_materials):
        raise RuntimeError('Derivative weld changed material ordering')
    for before, layer in zip(uv_before, mesh.uv_layers):
        after = np.empty(len(layer.data) * 2, dtype=np.float32)
        layer.data.foreach_get('uv', after)
        if not np.array_equal(before, after):
            raise RuntimeError('Derivative weld changed UV corner ordering or values')
    mesh.normals_split_custom_set(normals)
    return {'sourceObject': obj.name, 'toleranceMetres': 1e-6,
            'verticesBefore': vertex_count, 'verticesAfter': len(mesh.vertices),
            'facesUnchanged': face_count, 'uvCornersUnchanged': loop_count,
            'materialAssignmentsUnchanged': True, 'importedCornerNormalsRestored': True,
            'uvHashes': [hashlib.sha256(v.tobytes()).hexdigest() for v in uv_before]}


def prepare(args):
    import bpy
    import numpy as np
    from mathutils import Matrix, Vector

    source = args.input.resolve(strict=True)
    output = args.output_dir.resolve()
    if not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_-]*', args.name):
        raise ValueError('--name must contain only letters, digits, underscore and hyphen')
    if not 0 <= args.roughness_floor <= 1:
        raise ValueError('Roughness floor must be between zero and one')
    if not math.isfinite(args.height) or args.height <= 0 or not math.isfinite(args.yaw_degrees):
        raise ValueError('Height must be positive and orientation finite')
    if output.exists() and any(output.iterdir()):
        raise ValueError('Output directory must be empty; existing assets are never overwritten')
    doc, blob = glb_contents(source)
    # This pipeline intentionally handles static meshes only, without dropping an input rig.
    if doc.get('skins') or doc.get('animations'):
        raise ValueError('Rigged/animated input requires a dedicated preserving pipeline')
    output.mkdir(parents=True, exist_ok=True)
    for folder in ('raw', 'textures', 'meshes'):
        (output / folder).mkdir()
    shutil.copy2(source, output / 'raw' / 'original.glb')
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(source))
    objects = [obj for obj in bpy.context.scene.objects if obj.type == 'MESH']
    if not objects:
        raise ValueError('No mesh objects found')

    def bounds(items):
        points = [obj.matrix_world @ Vector(corner) for obj in items for corner in obj.bound_box]
        return [[min(p[i] for p in points) for i in range(3)],
                [max(p[i] for p in points) for i in range(3)]]

    def triangles(items):
        total = 0
        for obj in items:
            obj.data.calc_loop_triangles()
            total += len(obj.data.loop_triangles)
        return total

    before = bounds(objects)
    # Bake the import hierarchy once. Blender's glTF importer already converts Y-up to Z-up.
    yaw = Matrix.Rotation(math.radians(args.yaw_degrees), 4, 'Z')
    for obj in objects:
        world = obj.matrix_world.copy()
        obj.parent = None
        obj.data = obj.data.copy()
        obj.data.transform(yaw @ world)
        obj.matrix_world = Matrix.Identity(4)
    bpy.context.view_layer.update()
    rotated = bounds(objects)
    height = rotated[1][2] - rotated[0][2]
    if height <= 1e-8:
        raise ValueError('Input has no usable vertical extent')
    scale = args.height / height
    centre = Vector(((rotated[0][0] + rotated[1][0]) / 2,
                     (rotated[0][1] + rotated[1][1]) / 2, rotated[0][2]))
    transform = Matrix.Scale(scale, 4) @ Matrix.Translation(-centre)
    for obj in objects:
        obj.data.transform(transform)
    bpy.context.view_layer.update()
    for obj in list(bpy.context.scene.objects):
        if obj not in objects:
            bpy.data.objects.remove(obj, do_unlink=True)
    bpy.context.scene.unit_settings.system = 'METRIC'
    bpy.context.scene.unit_settings.scale_length = 1.0

    report = {'schemaVersion': 1, 'name': args.name, 'blender': bpy.app.version_string,
              'inputSha256': sha256(source), 'rawFile': 'raw/original.glb',
              'sourceBoundsBlender': before, 'normalizedBoundsBlender': bounds(objects),
              'heightMetres': args.height, 'unityRoughnessFloor': args.roughness_floor, 'yawDegrees': args.yaw_degrees,
              'uniformScale': scale, 'translationBeforeScale': list(-centre),
              'axisConvention': 'Blender Z-up; GLB and FBX exports Y-up; floor at zero',
              'sourceTriangles': triangles(objects), 'materials': [], 'textures': [], 'derivatives': [],
              'limitations': ['Static generated mesh: no rigging or animation readiness claim.',
                             'LOD candidates require close-range visual review; no anatomy repair.',
                             'LOD copies weld coincident vertices within 1e-6 m before reduction; source is untouched. No generic smoothing, UV unwrap or texture baking.']}
    warnings = report['warnings'] = []

    def texture(info, label, noncolor=False):
        if not info:
            return ''
        if info.get('texCoord', 0) != 0 or info.get('extensions'):
            warnings.append(label + ': nondefault UV/texture transform needs manual Unity review')
        tex = doc['textures'][info['index']]
        source_index = tex.get('source', tex.get('extensions', {}).get('EXT_texture_webp', {}).get('source'))
        if source_index is None: raise ValueError('Unsupported GLB texture source')
        img = doc['images'][source_index]
        if 'bufferView' not in img:
            raise ValueError('Only embedded GLB images are supported')
        view = doc['bufferViews'][img['bufferView']]
        start = view.get('byteOffset', 0)
        extension = {'image/png': '.png', 'image/jpeg': '.jpg', 'image/webp': '.webp'}.get(img.get('mimeType'))
        if extension is None: raise ValueError('Unsupported embedded texture MIME type')
        raw = output / 'raw' / (label + extension)
        raw.write_bytes(blob[start:start + view['byteLength']])
        image = bpy.data.images.load(str(raw), check_existing=False)
        image.colorspace_settings.name = 'Non-Color' if noncolor else 'sRGB'
        result = 'textures/' + label + '.png'
        image.pixels[0]  # Load before replacing the lazy source path.
        image.filepath_raw = str(output / result)
        image.file_format = 'PNG'
        image.save()
        report['textures'].append({'path': result, 'width': image.size[0], 'height': image.size[1],
                                   'colorSpace': 'Non-Color' if noncolor else 'sRGB',
                                   'rawPayload': str(raw.relative_to(output))})
        bpy.data.images.remove(image)
        return result

    for index, mat in enumerate(doc.get('materials', [])):
        pbr = mat.get('pbrMetallicRoughness', {})
        stem = 'material_%02d' % index
        item = {'name': mat.get('name', 'Material_%d' % index),
                'baseColor': texture(pbr.get('baseColorTexture'), stem + '_basecolor'),
                'normal': texture(mat.get('normalTexture'), stem + '_normal', True),
                'metallicRoughness': texture(pbr.get('metallicRoughnessTexture'), stem + '_metallicroughness', True),
                'baseColorFactor': pbr.get('baseColorFactor', [1, 1, 1, 1]),
                'metallicFactor': pbr.get('metallicFactor', 1.0),
                'roughnessFactor': pbr.get('roughnessFactor', 1.0),
                'normalScale': mat.get('normalTexture', {}).get('scale', 1.0),
                'alphaMode': mat.get('alphaMode', 'OPAQUE'),
                'alphaCutoff': mat.get('alphaCutoff', 0.5),
                'doubleSided': mat.get('doubleSided', False), 'unityMetallicSmoothness': ''}
        if mat.get('extensions') or mat.get('occlusionTexture') or mat.get('emissiveTexture') or any(mat.get('emissiveFactor', [])):
            warnings.append(item['name'] + ': additional material channels/extensions remain in source GLB; review Unity approximation')
        if item['metallicRoughness']:
            image = bpy.data.images.load(str(output / item['metallicRoughness']), check_existing=False)
            image.colorspace_settings.name = 'Non-Color'
            pixels = np.empty(len(image.pixels), dtype=np.float32)
            image.pixels.foreach_get(pixels)
            pixels = pixels.reshape((-1, 4))
            converted = np.zeros_like(pixels)
            converted[:, 0] = pixels[:, 2] * item['metallicFactor']
            roughness = pixels[:, 1] * item['roughnessFactor']
            if args.roughness_floor:
                roughness = np.maximum(roughness, args.roughness_floor * (1 - converted[:, 0]))
            converted[:, 3] = 1 - roughness
            target = bpy.data.images.new(stem + '_unity_masks', width=image.size[0], height=image.size[1], alpha=True)
            target.colorspace_settings.name = 'Non-Color'
            target.alpha_mode = 'CHANNEL_PACKED'
            target.pixels.foreach_set(converted.ravel())
            item['unityMetallicSmoothness'] = 'textures/' + stem + '_metallic_smoothness.png'
            target.filepath_raw = str(output / item['unityMetallicSmoothness'])
            target.file_format = 'PNG'
            target.save()
            report['textures'].append({'path': item['unityMetallicSmoothness'],
                                       'width': target.size[0], 'height': target.size[1],
                                       'colorSpace': 'Non-Color',
                                       'channels': 'R = metallic; A = smoothness; G/B = 0'})
            bpy.data.images.remove(target)
            bpy.data.images.remove(image)
        item['originalGltfMaterial'] = mat
        report['materials'].append(item)

    # Pack the un-decimated, normalized source for an artist; save before adding any LODs.
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(output / 'source.blend'))

    def select(items):
        bpy.ops.object.select_all(action='DESELECT')
        for obj in items:
            obj.select_set(True)
        bpy.context.view_layer.objects.active = items[0]

    def export(items, label, target, welding=None):
        select(items)
        glb, fbx = 'meshes/' + label + '.glb', 'meshes/' + label + '.fbx'
        bpy.ops.export_scene.gltf(filepath=str(output / glb), export_format='GLB',
                                  use_selection=True, export_animations=False)
        bpy.ops.export_scene.fbx(filepath=str(output / fbx), use_selection=True,
                                 object_types={'MESH'}, bake_anim=False, add_leaf_bones=False,
                                 axis_forward='-Z', axis_up='Y', path_mode='STRIP', use_mesh_modifiers=True)
        report['derivatives'].append({'name': label, 'targetTriangles': target,
                                      'actualTriangles': triangles(items), 'glb': glb, 'fbx': fbx,
                                      'boundsBlender': bounds(items), 'derivedSourceWelding': welding or []})

    export(objects, args.name + '_source', report['sourceTriangles'])
    for level, budget in enumerate(() if args.source_only else (80000, 30000, 10000)):
        lod = []
        welding = []
        ratio = min(1.0, budget / report['sourceTriangles'])
        for obj in objects:
            clone = obj.copy()
            clone.data = obj.data.copy()
            clone.name = obj.name + '_LOD%d' % level
            bpy.context.collection.objects.link(clone)
            lod.append(clone)
            welding.append(weld_lod_source(clone))
            if ratio < 1:
                select([clone])
                modifier = clone.modifiers.new('Candidate triangle reduction', 'DECIMATE')
                modifier.ratio = ratio
                modifier.use_collapse_triangulate = True
                bpy.ops.object.modifier_apply(modifier=modifier.name)
        export(lod, args.name + '_lod%d' % level, budget, welding)
        for obj in lod:
            bpy.data.objects.remove(obj, do_unlink=True)
    report['files'] = [{'path': str(p.relative_to(output)), 'sha256': sha256(p), 'bytes': p.stat().st_size}
                       for p in sorted(output.rglob('*')) if p.is_file()]
    (output / 'manifest.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps({'output': str(output), 'triangles': [(d['name'], d['actualTriangles']) for d in report['derivatives']]}))


def main():
    args = arguments()
    try:
        import bpy  # noqa: F401
    except ImportError:
        blender = shutil.which(args.blender)
        if not blender:
            raise SystemExit('Blender not found; use --blender /path/to/blender (tested with 4.2)')
        command = [blender, '--background', '--factory-startup', '--python-exit-code', '1',
                   '--python', str(Path(__file__).resolve()), '--', '--input', str(args.input.resolve()),
                   '--output-dir', str(args.output_dir.resolve()), '--name', args.name,
                   '--yaw-degrees', str(args.yaw_degrees), '--height', str(args.height),
                   '--roughness-floor', str(args.roughness_floor)]
        raise SystemExit(subprocess.call(command))
    prepare(args)


if __name__ == '__main__':
    main()
