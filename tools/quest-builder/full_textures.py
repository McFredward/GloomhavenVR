"""Preserve complete native Cubemap mip chains in portable Unity textures."""
import hashlib
import json
from pathlib import Path
import sys

from storage import BuildError, write_json


def mip_sizes(width, count, bytes_per_pixel):
    if width <= 0 or width > 16384 or width & (width - 1) or count != width.bit_length():
        raise BuildError('Original Cubemap requires a complete witnessed square mip chain.')
    return [max(1, width >> mip) ** 2 * bytes_per_pixel for mip in range(count)]


def native_yaml(fields, pixels, texture_format, file_id):
    """Unity2021 class89 shape witnessed by an actual Editor-created Cubemap."""
    if fields['m_Width'] != fields['m_Height'] or fields['m_ImageCount'] != 6 or fields['m_MipsStripped'] != 0:
        raise BuildError('Native Cubemap face/dimension/mip layout changed.')
    if fields.get('m_PlatformBlob') or any(ref['m_PathID'] for ref in fields['m_SourceTextures']):
        raise BuildError('Native Cubemap has an unwitnessed platform blob/source reference.')
    width, count = fields['m_Width'], fields['m_MipCount']
    expected = 6 * sum(mip_sizes(width, count, 8 if texture_format == 17 else 4))
    if len(pixels) != expected:
        raise BuildError('Portable Cubemap did not retain all native faces/mips.')
    names = ('m_ForcedFallbackFormat', 'm_DownscaleFallback', 'm_IsAlphaChannelOptional',
             'm_Width', 'm_Height', 'm_MipsStripped', 'm_MipCount', 'm_IsReadable',
             'm_IsPreProcessed', 'm_IgnoreMasterTextureLimit', 'm_StreamingMipmaps',
             'm_StreamingMipmapsPriority', 'm_ImageCount', 'm_LightmapFormat', 'm_ColorSpace')
    output = '%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!89 &' + str(file_id) + '\nCubemap:\n'
    output += '  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n'
    output += '  m_Name: ' + json.dumps(fields['m_Name'], ensure_ascii=False) + '\n'
    output += '  m_ImageContentsHash:\n    serializedVersion: 2\n    Hash: 00000000000000000000000000000000\n  serializedVersion: 2\n'
    for name in names:
        value = fields[name]
        output += '  ' + name + ': ' + str(int(value) if isinstance(value, bool) else value) + '\n'
    output += '  m_CompleteImageSize: ' + str(len(pixels)//6) + '\n  m_TextureFormat: ' + str(texture_format) + '\n'
    output += '  m_TextureDimension: 4\n  m_VTOnly: 0\n  m_AlphaIsTransparency: 0\n  m_TextureSettings:\n    serializedVersion: 2\n'
    for name, value in fields['m_TextureSettings'].items():
        output += '    ' + name + ': ' + format(value, '.9g') + '\n'
    output += '  m_PlatformBlob: \n  image data: ' + str(len(pixels)) + '\n  _typelessdata: ' + pixels.hex() + '\n'
    output += '  m_StreamData:\n    serializedVersion: 2\n    offset: 0\n    size: 0\n    path: \n  m_SourceTextures:\n' + '  - {fileID: 0}\n'*6
    return output


def remap_manifests(project, path_map):
    """Retain captured native identities when their portable physical path changes."""
    changed = []
    for relative in ('QuestRecovery/original-asset-identities.json', 'Assets/QuestOriginalCampaign/campaign-addressables.json'):
        path = project / relative
        if not path.is_file(): continue
        original = path.read_text(); text = original
        for old, new in path_map.items():
            text = text.replace(json.dumps(old), json.dumps(new))
        if text != original:
            path.write_text(text)
            changed.append({'path': relative, 'beforeSha256': hashlib.sha256(original.encode()).hexdigest(),
                            'sha256': hashlib.sha256(text.encode()).hexdigest()})
    return changed


def stage(project, game_data, *, dotnet, tool_cache, cab_bundles):
    """Recover every original cube using exact CAB/pathID; change a generated copy."""
    recovery = Path(__file__).resolve().parents[1] / 'quest-recovery'
    if str(recovery) not in sys.path: sys.path.append(str(recovery))
    from export_identity import object_index
    from pointer_recovery import load_native
    from recover import sha256
    import portable_decoder
    import UnityPy
    project, game_data = Path(project), Path(game_data)
    objects = object_index(json.loads((project/'QuestRecovery/original-asset-identities.json').read_text())['identities'])
    owners = json.loads(Path(cab_bundles).read_text()) if isinstance(cab_bundles, (str, Path)) else cab_bundles
    owners = {key.casefold(): value for key, value in owners.items()}
    targets = [target for target in objects.values() if target['classId'] == 89]
    command = portable_decoder.build(tool_cache, dotnet)
    temporary = Path(tool_cache)/'original-cubemap-payloads'; temporary.mkdir(exist_ok=True)
    environments, assets, path_map = {}, [], {}
    for target in targets:
        container = owners[target['collection']] if target['collection'].startswith('cab-') else target['collection']
        if container not in environments: environments[container] = load_native(UnityPy, game_data/container)
        native = [obj for obj in environments[container].objects if (obj.assets_file.name.casefold(), int(obj.path_id)) == (target['collection'], target['pathId'])]
        if len(native) != 1: raise BuildError('Original Cubemap identity is not unique.')
        fields = native[0].read_typetree(); raw = native[0].read().get_image_data()
        source_format = fields['m_TextureFormat']; width, count = fields['m_Width'], fields['m_MipCount']
        if source_format == 5:
            pixels, destination_format = raw, 5
        elif source_format in (10, 24):
            input_path, output_path = temporary/(target['guid']+'.blocks'), temporary/(target['guid']+'.pixels')
            input_path.write_bytes(raw)
            portable_decoder.execute(command, ('bc6h' if source_format == 24 else 'bc1', input_path, output_path, width, count))
            pixels, destination_format = output_path.read_bytes(), 17 if source_format == 24 else 4
            input_path.unlink(); output_path.unlink()
        else:
            raise BuildError('Original Cubemap has an unwitnessed texture format: '+str(source_format))
        path = project/target['path']; replacement = path.with_suffix('.asset')
        if replacement != path and replacement.exists(): raise BuildError('Portable Cubemap output already exists.')
        before = sha256(path)
        replacement.write_text(native_yaml(fields, pixels, destination_format, target['fileId']))
        Path(str(replacement)+'.meta').write_text('fileFormatVersion: 2\nguid: '+target['guid']+'\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: '+str(target['fileId'])+'\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n')
        if replacement != path:
            path.unlink(); Path(str(path)+'.meta').unlink()
        new_path = replacement.relative_to(project).as_posix(); path_map[target['path']] = new_path
        sizes = mip_sizes(width, count, 8 if destination_format == 17 else 4)
        cursor, mip_receipts = 0, []
        for face in range(6):
            for mip, size in enumerate(sizes):
                data = pixels[cursor:cursor+size]; cursor += size
                mip_receipts.append({'face':face,'mip':mip,'size':size,'sha256':hashlib.sha256(data).hexdigest()})
        assets.append({'assetPath':new_path,'originalRecoveredPath':target['path'],'guid':target['guid'],'fileId':target['fileId'],
              'originalCollection':target['collection'],'originalPathId':target['pathId'],'sourceContainer':container,
              'sourceContainerSha256':sha256(game_data/container),'originalImageSha256':hashlib.sha256(raw).hexdigest(),
              'beforeSha256':before,'sha256':sha256(replacement),'sourceFormat':source_format,'textureFormat':destination_format,
              'width':width,'mipCount':count,'faceCount':6,'isReadable':fields['m_IsReadable'],'pixelByteCount':len(pixels),
              'pixelSha256':hashlib.sha256(pixels).hexdigest(),'mips':mip_receipts,'sourceMipChainPreserved':True})
    manifests = remap_manifests(project,path_map)
    receipt={'schema':1,'nativeCubemapCount':len(assets),'assets':assets,'pathMap':path_map,'updatedManifests':manifests,
             'source':'original-native-CAB-pathID-all-six-faces-all-original-mip-levels',
             'unityImportVerified':False,'originalGpuParityVerified':False,'headsetGpuVerified':False}
    write_json(project/'Assets/QuestOriginalCampaign/native-cubemaps.json',receipt)
    return receipt
