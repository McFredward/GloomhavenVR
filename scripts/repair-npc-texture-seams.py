#!/usr/bin/env python3
"""Attenuate normal-map UV seams on A-pose hands in a separate GLB candidate.

Blender 4.2 -b --python-exit-code 1 --python this-script -- --input model.glb
--output-dir repaired-candidate. External Python needs NumPy/Pillow, offline only.
"""

import argparse
import hashlib
import json
from pathlib import Path
import struct
import subprocess
import sys

import numpy as np


def expand(mask):
    h, w = mask.shape
    padded = np.pad(mask, 1, constant_values=False)
    result = np.zeros_like(mask)
    for dy in range(3):
        for dx in range(3):
            result |= padded[dy:dy + h, dx:dx + w]
    return result


def rasterize_mask(source, destination):
    from PIL import Image, ImageDraw

    with np.load(source) as archive:
        triangles, hands = archive['triangles'], archive['hands']
        width, height = archive['dimensions'].tolist()
        seam_width, falloff = archive['settings'].tolist()
    hand_image = Image.new('L', (width, height))
    draw = ImageDraw.Draw(hand_image)
    for triangle in triangles[hands] * [width, height] - .5:
        draw.polygon([tuple(vertex) for vertex in triangle], fill=255)
    hand_mask = np.asarray(hand_image) > 0
    # Count UV edges, not atlas coverage boundaries: tightly packed UV islands
    # can touch in a rasterized union mask and still require separate seam treatment.
    edges = np.concatenate([triangles[:, [0, 1]], triangles[:, [1, 2]], triangles[:, [2, 0]]])
    swap = ((edges[:, 0, 0] > edges[:, 1, 0]) |
            ((edges[:, 0, 0] == edges[:, 1, 0]) & (edges[:, 0, 1] > edges[:, 1, 1])))
    edges[swap] = edges[swap, ::-1, :]
    unique, counts = np.unique(np.round(edges.reshape(-1, 4), 6), axis=0, return_counts=True)
    boundaries = unique[counts == 1].reshape(-1, 2, 2)
    seam_image = Image.new('L', (width, height))
    draw = ImageDraw.Draw(seam_image)
    for edge in boundaries * [width, height] - .5:
        draw.line([tuple(vertex) for vertex in edge], fill=255, width=1)
    seam = (np.asarray(seam_image) > 0) & expand(hand_mask)
    weight = np.ones((height, width), dtype=np.float32)
    for _ in range(seam_width):
        seam = expand(seam)
    weight[seam] = 0
    for step in range(1, falloff + 1):
        outer = expand(seam)
        weight[outer & ~seam] = step / falloff
        seam = outer
    # Do not attenuate an unrelated atlas island merely because it is packed
    # near a hand island. One exterior texel retains bilinear edge coverage.
    weight[~expand(hand_mask)] = 1
    np.savez(destination, weight=weight, hand_mask=hand_mask,
             boundary_edges=np.array(len(boundaries)))


def read_glb(path):
    data = path.read_bytes()
    magic, version, length = struct.unpack_from('<III', data)
    if magic != 0x46546C67 or version != 2 or length != len(data):
        raise ValueError('Expected a valid glTF 2 binary container')
    json_length, json_type = struct.unpack_from('<II', data, 12)
    if json_type != 0x4E4F534A:
        raise ValueError('Missing GLB JSON chunk')
    document = json.loads(data[20:20 + json_length])
    if len(document.get('buffers', [])) != 1 or 'uri' in document['buffers'][0]:
        raise ValueError('Expected exactly one embedded GLB buffer')
    offset = 20 + json_length
    bin_length, bin_type = struct.unpack_from('<II', data, offset)
    if bin_type != 0x004E4942 or offset + 8 + bin_length != len(data):
        raise ValueError('Expected one embedded binary chunk')
    return document, data[offset + 8:offset + 8 + bin_length]


def append_replacement_images(document, binary, replacements, output):
    original = binary[:document['buffers'][0]['byteLength']]
    buffer = bytearray(original)
    found = set()
    for image in document.get('images', []):
        if 'bufferView' not in image:
            raise ValueError('External images are unsupported')
        view = document['bufferViews'][image['bufferView']]
        start = view.get('byteOffset', 0)
        digest = hashlib.sha256(original[start:start + view['byteLength']]).hexdigest()
        if digest not in replacements:
            continue
        buffer.extend(b'\0' * (-len(buffer) % 4))
        payload = replacements[digest].read_bytes()
        image['bufferView'] = len(document['bufferViews'])
        image['mimeType'] = 'image/png'
        document['bufferViews'].append({'buffer': 0, 'byteOffset': len(buffer), 'byteLength': len(payload)})
        buffer.extend(payload)
        found.add(digest)
    if found != set(replacements):
        raise ValueError('Could not match every imported normal map to its original GLB image')
    # Preserve original geometry, accessors, materials and image bytes verbatim.
    # Old normal bytes remain unreferenced in the buffer; no geometry re-export occurs.
    assert buffer[:len(original)] == original
    document['buffers'][0]['byteLength'] = len(buffer)
    encoded = json.dumps(document, separators=(',', ':')).encode()
    encoded += b' ' * (-len(encoded) % 4)
    buffer.extend(b'\0' * (-len(buffer) % 4))
    chunks = (struct.pack('<II', len(encoded), 0x4E4F534A) + encoded +
              struct.pack('<II', len(buffer), 0x004E4942) + buffer)
    output.write_bytes(struct.pack('<III', 0x46546C67, 2, 12 + len(chunks)) + chunks)


def main():
    import bpy
    from mathutils import Vector

    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--input', type=Path, required=True)
    parser.add_argument('--output-dir', type=Path, required=True)
    parser.add_argument('--seam-width', type=int, default=4)
    parser.add_argument('--falloff', type=int, default=8)
    parser.add_argument('--hand-min-height', type=float, default=.30)
    parser.add_argument('--hand-max-height', type=float, default=.67)
    parser.add_argument('--hand-min-abs-x', type=float, default=.30,
                        help='Minimum absolute distance from centre, as fraction of whole figure width.')
    parser.add_argument('--mask-python', default='python3')
    args = parser.parse_args(sys.argv[sys.argv.index('--') + 1:])
    args.input, args.output_dir = args.input.resolve(), args.output_dir.resolve()
    output_glb = args.output_dir / 'model.glb'
    if not args.input.is_file() or args.input.suffix.lower() != '.glb':
        parser.error('Input must be an existing GLB')
    if output_glb == args.input:
        parser.error('Choose a new output directory; input overwrite is forbidden')
    if args.seam_width < 0 or args.falloff < 1:
        parser.error('Seam width must be nonnegative and falloff positive')
    args.output_dir.mkdir(parents=True, exist_ok=True)
    source_hash = hashlib.sha256(args.input.read_bytes()).hexdigest()
    document, binary = read_glb(args.input)
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=str(args.input))
    meshes = [obj for obj in bpy.context.scene.objects if obj.type == 'MESH']
    points = [obj.matrix_world @ Vector(c) for obj in meshes for c in obj.bound_box]
    lo = np.array([min(p[i] for p in points) for i in range(3)])
    hi = np.array([max(p[i] for p in points) for i in range(3)])
    materials = {slot.material for obj in meshes for slot in obj.material_slots if slot.material}
    bindings = {}
    for material in materials:
        for normal in material.node_tree.nodes:
            if normal.type != 'NORMAL_MAP' or not normal.inputs['Color'].is_linked:
                continue
            if normal.space != 'TANGENT':
                raise ValueError('Only tangent-space normal maps are supported')
            texture = normal.inputs['Color'].links[0].from_node
            if texture.type != 'TEX_IMAGE' or texture.image is None:
                raise ValueError('Normal map must use a direct image input')
            if texture.inputs['Vector'].is_linked:
                coordinate = texture.inputs['Vector'].links[0].from_node
                if coordinate.type != 'UVMAP':
                    raise ValueError('Transformed normal-map coordinates are unsupported')
                uv_name = coordinate.uv_map
            else:
                uv_name = normal.uv_map
            bindings.setdefault(texture.image, []).append((material, texture, uv_name))
    if not bindings:
        raise ValueError('No supported normal map found')
    report = {'input': str(args.input), 'input_sha256': source_hash,
              'blender_version': bpy.app.version_string, 'seam_width_texels': args.seam_width,
              'falloff_texels': args.falloff, 'normal_bit_depth': 16, 'normal_maps': [],
              'hand_region': {'height_min': args.hand_min_height, 'height_max': args.hand_max_height,
                              'absolute_x_min_fraction_of_width': args.hand_min_abs_x},
              'tradeoff': 'Reduced normal microdetail only around UV seams in the selected hand/wrist region.'}
    replacements = {}
    for index, (image, users) in enumerate(sorted(bindings.items(), key=lambda pair: pair[0].name)):
        triangles, hands = [], []
        for obj in meshes:
            mesh = obj.data
            mesh.calc_loop_triangles()
            world = np.array([tuple(obj.matrix_world @ v.co) for v in mesh.vertices])
            for material, _, uv_name in users:
                slots = {i for i, slot in enumerate(obj.material_slots) if slot.material == material}
                if not slots:
                    continue
                layer = mesh.uv_layers.get(uv_name) if uv_name else mesh.uv_layers.active
                if layer is None:
                    raise ValueError('Normal map has no matching mesh UV layer')
                uv = np.empty(len(layer.data) * 2, dtype=np.float32)
                layer.data.foreach_get('uv', uv)
                uv = uv.reshape(-1, 2)
                selected = [tri for tri in mesh.loop_triangles if tri.material_index in slots]
                loops = np.array([tri.loops[:] for tri in selected], dtype=np.int32)
                vertices = np.array([tri.vertices[:] for tri in selected], dtype=np.int32)
                if not len(loops):
                    continue
                centres = world[vertices].mean(axis=1)
                height = (centres[:, 2] - lo[2]) / (hi[2] - lo[2])
                lateral = np.abs(centres[:, 0] - (hi[0] + lo[0]) / 2) / (hi[0] - lo[0])
                triangles.append(uv[loops])
                hands.append((height >= args.hand_min_height) & (height <= args.hand_max_height) &
                             (lateral >= args.hand_min_abs_x))
        triangles, hands = np.concatenate(triangles), np.concatenate(hands)
        if not hands.any() or np.min(triangles) < -1e-5 or np.max(triangles) > 1.00001:
            raise ValueError('Empty hand region or unsupported tiled UVs')
        width, height = image.size[:]
        uv_path, mask_path = args.output_dir / f'uv-{index}.npz', args.output_dir / f'mask-{index}.npz'
        np.savez(uv_path, triangles=triangles, hands=hands, dimensions=np.array([width, height]),
                 settings=np.array([args.seam_width, args.falloff]))
        subprocess.run([args.mask_python, str(Path(__file__).resolve()), '--rasterize-mask',
                        str(uv_path), str(mask_path)], check=True)
        with np.load(mask_path) as archive:
            weight, hand_mask = archive['weight'], archive['hand_mask']
        pixels = np.empty(width * height * 4, dtype=np.float32)
        image.pixels.foreach_get(pixels)
        pixels = pixels.reshape(height, width, 4)
        repaired = pixels.copy()
        neutral = np.array([.5, .5, 1], dtype=np.float32)
        affected = weight < 1
        repaired[affected, :3] = neutral + (pixels[affected, :3] - neutral) * weight[affected, None]
        assert np.array_equal(repaired[~affected], pixels[~affected])
        assert np.array_equal(repaired[:, :, 3], pixels[:, :, 3])
        replacement = bpy.data.images.new(image.name + '_hand_seams', width=width, height=height,
                                          alpha=True, float_buffer=True)
        replacement.colorspace_settings.name = 'Non-Color'
        replacement.pixels.foreach_set(repaired.reshape(-1))
        replacement.update()
        normal_path = args.output_dir / f'normal-{index}-hand-seams.png'
        scene = bpy.context.scene
        scene.render.image_settings.file_format = 'PNG'
        scene.render.image_settings.color_mode = 'RGBA'
        scene.render.image_settings.color_depth = '16'
        scene.view_settings.view_transform = 'Raw'
        replacement.save_render(str(normal_path), scene=scene)
        reloaded = bpy.data.images.load(str(normal_path))
        reloaded.colorspace_settings.name = 'Non-Color'
        reloaded.pack()
        replacements[hashlib.sha256(image.packed_file.data).hexdigest()] = normal_path
        for _, texture, _ in users:
            texture.image = reloaded
        report['normal_maps'].append({'source_image': image.name, 'dimensions': [width, height],
                                      'hand_triangles': int(hands.sum()),
                                      'hand_covered_pixels': int(hand_mask.sum()),
                                      'affected_pixels': int(affected.sum()),
                                      'fully_neutral_pixels': int((weight == 0).sum())})
        print('NPC_HAND_NORMAL_SEAMS', json.dumps(report['normal_maps'][-1]), flush=True)
    append_replacement_images(document, binary, replacements, output_glb)
    bpy.context.scene.view_settings.view_transform = 'AgX'
    bpy.context.scene.render.image_settings.color_depth = '8'
    bpy.ops.file.pack_all()
    bpy.ops.wm.save_as_mainfile(filepath=str(args.output_dir / 'repaired.blend'))
    assert hashlib.sha256(args.input.read_bytes()).hexdigest() == source_hash
    report['output_glb'] = str(output_glb)
    report['output_sha256'] = hashlib.sha256(output_glb.read_bytes()).hexdigest()
    report['source_geometry_buffer_preserved_verbatim'] = True
    (args.output_dir / 'repair-report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print('NPC_HAND_NORMAL_SEAMS_COMPLETE', str(output_glb), flush=True)


if __name__ == '__main__':
    if len(sys.argv) > 1 and sys.argv[1] == '--rasterize-mask':
        rasterize_mask(sys.argv[2], sys.argv[3])
    else:
        main()
