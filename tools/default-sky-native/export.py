#!/usr/bin/env python3
"""Package the original render-only default sky without loading a game scene.

No source asset is written. Native shader programs, material properties, encoded
texture mips and mesh vertex/index data are retained. Only local dependency IDs
and streamed-data addresses change in the isolated archive.
"""
import argparse
import copy
import hashlib
import json
from pathlib import Path


def sha(data):
    return hashlib.sha256(data).hexdigest()


def export(game, output):
    import UnityPy
    from UnityPy.enums import ClassIDType
    from UnityPy.files.ObjectReader import ObjectReader
    from UnityPy.files import SerializedFile
    from UnityPy.streams import EndianBinaryReader

    source = game / 'sharedassets8.assets'
    scene = game / 'level8'
    builtin = game / 'Resources/unity_builtin_extra'
    template = game / 'StreamingAssets/aa/StandaloneWindows64/itemconfigs_backgrounds_assets_poisondagger.bundle'
    inputs = {str(p.relative_to(game)): sha(p.read_bytes()) for p in (source, scene, builtin, template, source.with_suffix('.assets.resS'))}
    authored = UnityPy.load(str(scene)).assets[0]
    placement = {str(i): authored.objects[i].read_typetree() for i in (16, 18)}
    assert placement['16']['m_LocalScale']['x'] == 1.0629240274429321
    assert placement['18']['m_LocalPosition']['y'] == 14.749992370605469
    assert placement['18']['m_LocalScale']['y'] == 0.06696362793445587
    original = UnityPy.load(str(source)).assets[0]
    fallbacks = UnityPy.load(str(builtin)).assets[0]
    wrapper = next(iter(UnityPy.load(str(template)).files.values()))
    wrapper_file = next(f for f in wrapper.files.values() if isinstance(f, SerializedFile))
    container = next(o for o in wrapper_file.objects.values() if o.type.name == 'AssetBundle')
    native_ids = (4, 6, 8, 12, 18)
    objects = {i: copy.copy(original.objects[i]) for i in native_ids}
    sky_shader = objects[18].read_typetree()
    assert sky_shader['m_ParsedForm']['m_Name'] == 'AMP_SkyShader'
    assert sky_shader['platforms'] == [4]
    assert sky_shader['m_Dependencies'] == [{'m_FileID': 2, 'm_PathID': 7}, {'m_FileID': 2, 'm_PathID': 6}]
    assert objects[4].read_typetree()['m_Name'] == 'BG_SkyClouds 2'
    assert objects[12].read_typetree()['m_Name'] == 'GH_SkySphere'
    cab = 'CAB-' + sha(b'GHVR_NATIVE_DEFAULT_SKY/1' + source.read_bytes())[:32]
    resource_name = cab + '.resS'
    records = []
    streamed = bytearray()
    for path_id in native_ids:
        obj = objects[path_id]
        tree = obj.read_typetree()
        before = copy.deepcopy(tree)
        record = {'source': source.name, 'path_id': path_id, 'bank_path_id': path_id,
                  'type': obj.type.name, 'original_object_sha256': sha(obj.get_raw_data())}
        stream = tree.get('m_StreamData')
        if stream and stream['size']:
            raw = (game / stream['path']).read_bytes()[stream['offset']:stream['offset'] + stream['size']]
            assert len(raw) == stream['size']
            stream['offset'] = len(streamed)
            stream['path'] = 'archive:/' + cab + '/' + resource_name
            streamed.extend(raw)
            record['encoded_stream_sha256'] = sha(raw)
            record['encoded_stream_bytes'] = len(raw)
            obj.save_typetree(tree)
            restored = copy.deepcopy(tree)
            restored['m_StreamData'] = before['m_StreamData']
            assert restored == before
        if path_id == 18:
            tree['m_Dependencies'] = [{'m_FileID': 0, 'm_PathID': 1007}, {'m_FileID': 0, 'm_PathID': 1006}]
            obj.save_typetree(tree)
            restored = copy.deepcopy(tree)
            restored['m_Dependencies'] = before['m_Dependencies']
            assert restored == before
            record['compiled_blob_sha256'] = sha(bytes(tree['compressedBlob']))
            record['platforms'] = tree['platforms']
        records.append(record)

    # Preserve both original fallback programs as well. They are never replaced
    # by a different shader family and require no external game-file dependency.
    for old_id, new_id in ((6, 1006), (7, 1007)):
        original_object = fallbacks.objects[old_id]
        tree = original_object.read_typetree()
        before = copy.deepcopy(tree)
        if old_id == 7:
            assert tree['m_Dependencies'] == [{'m_FileID': 0, 'm_PathID': 6}]
            tree['m_Dependencies'] = [{'m_FileID': 0, 'm_PathID': 1006}]
        else:
            assert not tree['m_Dependencies']
        asset_type = copy.copy(original_object.serialized_type)
        original.types.append(asset_type)
        raw = original_object.get_raw_data()
        obj = ObjectReader(original, original_object.reader, new_id, len(original.types) - 1,
            asset_type, original_object.class_id, ClassIDType.Shader, 0, len(raw), None, None, raw)
        obj.save_typetree(tree)
        objects[new_id] = obj
        restored = copy.deepcopy(tree)
        restored['m_Dependencies'] = before['m_Dependencies']
        assert restored == before
        records.append({'source': 'Resources/unity_builtin_extra', 'path_id': old_id, 'bank_path_id': new_id,
            'type': 'Shader', 'name': tree['m_ParsedForm']['m_Name'], 'original_object_sha256': sha(raw),
            'compiled_blob_sha256': sha(bytes(tree['compressedBlob'])), 'platforms': tree['platforms']})

    tree = container.read_typetree()
    tree['m_Name'] = tree['m_AssetBundleName'] = 'ghvr-native-default-sky'
    tree['m_Dependencies'] = []
    tree['m_IsStreamedSceneAssetBundle'] = False
    tree['m_ExplicitDataLayout'] = 0
    tree['m_PreloadTable'] = [{'m_FileID': 0, 'm_PathID': i} for i in objects]
    tree['m_Container'] = [(name, {'preloadIndex': 0, 'preloadSize': len(objects),
        'asset': {'m_FileID': 0, 'm_PathID': i}}) for name, i in
        (('native/default-sky/material', 4), ('native/default-sky/mesh', 12), ('native/default-sky/shader', 18))]
    tree['m_MainAsset'] = {'preloadIndex': 0, 'preloadSize': 0, 'asset': {'m_FileID': 0, 'm_PathID': 0}}
    container.save_typetree(tree)
    asset_type = copy.copy(container.serialized_type)
    original.types.append(asset_type)
    raw = container.get_raw_data()
    objects[1] = ObjectReader(original, container.reader, 1, len(original.types) - 1,
        asset_type, container.class_id, ClassIDType.AssetBundle, 0, len(raw), None, None, raw)
    original.objects = objects
    original.script_types = []
    original.externals = []
    used = sorted({o.type_id for o in objects.values()})
    remap = {old: new for new, old in enumerate(used)}
    original.types = [original.types[i] for i in used]
    for obj in objects.values():
        obj.type_id = remap[obj.type_id]
        if original.types[obj.type_id].node is None:
            original.types[obj.type_id].node = obj._get_typetree_node(None)
    for typ in original.types:
        if typ.type_dependencies is None:
            typ.type_dependencies = []
        for node in typ.node.traverse():
            if node.m_TypeFlags is None:
                node.m_TypeFlags = 1 if node.m_Type == 'Array' else 0
            if node.m_RefTypeHash is None:
                node.m_RefTypeHash = 0
    original._enable_type_tree = True
    original.name = cab
    original.flags = wrapper_file.flags
    resource = EndianBinaryReader(bytes(streamed))
    resource.flags = 0
    wrapper.files = {cab: original, resource_name: resource}
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_bytes(wrapper.save('lz4'))
    actual = UnityPy.load(str(output)).assets[0]
    assert not actual.externals and len(actual.objects) == 8
    for path_id, expected in objects.items():
        left = actual.objects[path_id].get_raw_data()
        right = expected.data if expected.data is not None else expected.get_raw_data()
        assert left == right, (path_id, len(left), len(right))
    for path, digest in inputs.items():
        assert sha((game / path).read_bytes()) == digest
    provenance = {'schema': 'native-default-sky-v1', 'unity': '2021.3.5f1', 'sources': inputs,
        'authored_placement': {'scene': 'level8', 'transforms': placement,
            'observer_origin': 'Original GH_SkySphere_PR prefab origin; no mesh-bounds recentering.'},
        'assets': records, 'bundle_sha256': sha(output.read_bytes()), 'bundle_bytes': output.stat().st_size,
        'stream_bytes': len(streamed), 'native_platforms': ['Direct3D11'],
        'changes': ['Isolated container/CAB identity', 'Local dependency pointer IDs', 'Stream offsets and archive addresses'],
        'preserved': ['All compiled shader programs/properties/states', 'All material values',
                      'Original encoded texture mips', 'Original encoded mesh vertex/index data'],
        'limits': 'Original Windows shader pixels are not verified by serialization audit or a GL asset-loader test.'}
    output.with_suffix('.provenance.json').write_text(json.dumps(provenance, indent=2) + '\n')
    print('Packaged original native default sky:', output.stat().st_size, 'bytes; seven render assets; source files unchanged.')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--game-data', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    export(args.game_data, args.output)
