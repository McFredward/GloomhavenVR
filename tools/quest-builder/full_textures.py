"""Preserve complete native Cubemap mip chains in portable Unity textures."""
import hashlib
import json
from pathlib import Path
import re
import sys

from storage import BuildError, write_json, ImmutableFileHashes


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
    files = ['QuestRecovery/original-asset-identities.json']
    for folder in ('QuestOriginalStartup', 'QuestOriginalCampaign'):
        for filename in ('startup-addressables.json', 'campaign-addressables.json', 'script-bindings.json'):
            files.append('Assets/' + folder + '/' + filename)
    for relative in files:
        path = project / relative
        if not path.is_file(): continue
        original = path.read_text(); document = json.loads(original); modified = False
        if relative == files[0]:
            for row in document['identities']:
                if row['path'] in path_map:
                    row['path'] = path_map[row['path']]; modified = True
        elif relative.endswith('-addressables.json'):
            for row in document['entries']:
                if row.get('assetPath') in path_map:
                    row['assetPath'] = path_map[row['assetPath']]; modified = True
        else:
            for index, value in enumerate(document['assetPaths']):
                if value in path_map:
                    document['assetPaths'][index] = path_map[value]; modified = True
        if modified:
            write_json(path,document); text = path.read_text()
            changed.append({'path': relative, 'beforeSha256': hashlib.sha256(original.encode()).hexdigest(),
                            'sha256': hashlib.sha256(text.encode()).hexdigest()})
    return changed


def restore_native_texture_pointer_types(project):
    """Retarget genuine PPtr nodes after an imported PNG becomes a native asset.

    Unity resolves type3 imported-image references differently from type2 native
    serialized assets even when their GUID/local ID agrees. Only the witnessed
    native texture target identities change; scalar names and source hashes do not.
    """
    recovery=Path(__file__).resolve().parents[1]/'quest-recovery'
    if str(recovery) not in sys.path:sys.path.append(str(recovery))
    from export_identity import POINTER
    from recover import is_unity_yaml,serialized_pointer_tokens,sha256
    project=Path(project);targets={};owners=[];changes={}
    for filename in ('native-cubemaps.json','native-texture2d.json'):
        path=project/'Assets/QuestOriginalCampaign'/filename
        if not path.is_file():continue
        for row in json.loads(path.read_text())['assets']:
            key=(row['guid'],int(row['fileId']))
            if key in targets:raise BuildError('Native texture reference target is not unique.')
            meta=Path(str(project/row['assetPath'])+'.meta').read_text()
            if not re.search(r'^guid: '+re.escape(row['guid'])+r'\s*$',meta,re.M) or 'NativeFormatImporter:' not in meta or not re.search(r'^\s+mainObjectFileID: '+str(row['fileId'])+r'\s*$',meta,re.M):
                raise BuildError('Native texture reference target is not a witnessed native importer.')
            targets[key]={'assetPath':row['assetPath'],'guid':row['guid'],'fileId':int(row['fileId']),'type':2}
    for path in (project/'Assets').rglob('*'):
        if not path.is_file() or not is_unity_yaml(path):continue
        with path.open('rb') as source:prefix=source.read(128)
        if re.search(rb'--- !u!(?:28|89) &',prefix):continue # Source-proven texture payloads have no external texture PPtrs.
        text=path.read_text(encoding='utf-8');matches=[m for m in POINTER.finditer(text) if (m[2],int(m[1])) in targets]
        if not matches:continue
        spans={(left,right) for guid,left,right in serialized_pointer_tokens(text) if any(key[0]==guid for key in targets)}
        actual=[m for m in matches if (m.start(2),m.end(2)) in spans]
        if {(m.start(2),m.end(2)) for m in actual}!=spans:
            raise BuildError('Native texture reference has an unwitnessed PPtr representation.')
        if not actual:continue
        replacements=[];references=[]
        for match in actual:
            if int(match[3]) not in (2,3):raise BuildError('Native texture PPtr has an unwitnessed source reference type.')
            if int(match[3])==3:replacements.append((match.start(3),match.end(3),'2'))
            references.append({'guid':match[2],'fileId':int(match[1]),'type':2})
        before=hashlib.sha256(text.encode()).hexdigest()
        for left,right,value in reversed(replacements):text=text[:left]+value+text[right:]
        if replacements:path.write_text(text)
        relative=path.relative_to(project).as_posix();after=sha256(path)
        if replacements:changes[relative]=(before,after)
        owners.append({'assetPath':relative,'beforeSha256':before,'sha256':after,
                       'changedReferenceCount':len(replacements),'references':references})
    refreshed=[]
    manifests=list((project/'QuestRecovery').glob('packed-*.json'))
    for filename in ('native-sprites.json','native-cubemaps.json','native-texture2d.json','native-platform-images.json','bundled-audio.json'):
        manifests.append(project/'Assets/QuestOriginalCampaign'/filename)
    for path in manifests:
        if not path.is_file():continue
        document=json.loads(path.read_text());modified=False
        for row in document.get('assets',[])+document.get('restoredMembers',[]):
            if row.get('assetPath') in changes:
                before,after=changes[row['assetPath']]
                if row['sha256']!=before:raise BuildError('Native texture owner receipt hash changed before reference repair.')
                row['sha256']=after;row['nativeTextureReferenceTypesRestored']=True;modified=True
        if modified:write_json(path,document);refreshed.append(path.relative_to(project).as_posix())
    receipt={'schema':1,'nativeTextureTargetCount':len(targets),'ownerCount':len(owners),
             'referenceCount':sum(len(row['references']) for row in owners),
             'changedReferenceCount':sum(row['changedReferenceCount'] for row in owners),
             'targets':list(targets.values()),'owners':owners,'refreshedManifests':refreshed,
             'unityConsumingReferencesVerified':False}
    write_json(project/'Assets/QuestOriginalCampaign/native-texture-references.json',receipt)
    return receipt


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
    source_hashes = ImmutableFileHashes()
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
              'sourceContainerSha256':source_hashes.digest(game_data/container),'originalImageSha256':hashlib.sha256(raw).hexdigest(),
              'beforeSha256':before,'sha256':sha256(replacement),'sourceFormat':source_format,'textureFormat':destination_format,
              'width':width,'mipCount':count,'faceCount':6,'isReadable':fields['m_IsReadable'],'pixelByteCount':len(pixels),
              'pixelSha256':hashlib.sha256(pixels).hexdigest(),'mips':mip_receipts,'sourceMipChainPreserved':True})
    manifests = remap_manifests(project,path_map)
    receipt={'schema':1,'nativeCubemapCount':len(assets),'assets':assets,'pathMap':path_map,'updatedManifests':manifests,
             'source':'original-native-CAB-pathID-all-six-faces-all-original-mip-levels',
             'unityImportVerified':False,'originalGpuParityVerified':False,'headsetGpuVerified':False}
    write_json(project/'Assets/QuestOriginalCampaign/native-cubemaps.json',receipt)
    receipt['platformImageAudit'] = audit_platform_images(project,game_data,objects,owners, source_hashes=source_hashes)
    receipt['nativeTextureReferences']=restore_native_texture_pointer_types(project)
    return receipt


def audit_platform_images(project,game_data,objects,owners, *, source_hashes=None):
    """Audit other native image containers rather than guessing from extensions."""
    from pointer_recovery import load_native
    from recover import sha256
    import UnityPy
    unsupported=[row for row in objects.values() if row['classId'] in (117,187,188)]
    if unsupported:
        raise BuildError('Native 3D/array images require exact format/mip recovery before importing this game version.')
    targets=[row for row in objects.values() if row['classId']==84 or
             row['classId']==28 and Path(row['path']).suffix.casefold()=='.texture2d']
    assets=[]
    source_hashes = source_hashes if source_hashes is not None else ImmutableFileHashes()
    environments = {}
    for target in targets:
        container=owners.get(target['collection'],target['collection'])
        if container not in environments: environments[container] = load_native(UnityPy,game_data/container)
        env = environments[container]
        matches=[obj for obj in env.objects if (obj.assets_file.name.casefold(),int(obj.path_id))==(target['collection'],target['pathId'])]
        if len(matches)!=1:raise BuildError('Native platform-sensitive image identity is unresolved.')
        fields=matches[0].read_typetree()
        row={'assetPath':target['path'],'guid':target['guid'],'fileId':target['fileId'],'classId':target['classId'],
             'originalCollection':target['collection'],'originalPathId':target['pathId'],
             'sourceContainer':container,'sourceContainerSha256':source_hashes.digest(game_data/container),
             'sha256':sha256(project/target['path']),'width':fields['m_Width'],'height':fields['m_Height']}
        if target['classId']==28:
            if fields['m_TextureFormat']!=1 or fields.get('m_PlatformBlob') or fields['m_MipCount']!=1:
                raise BuildError('Native font texture encoding changed; audit before this game version is ported.')
            row.update(textureFormat=1,mipCount=1,nativeEncoding='uncompressed-Alpha8-no-platform-blob')
        else:
            if fields['m_ColorFormat']!=8 or fields['m_DepthStencilFormat'] not in (90,92) or not fields['m_EnableCompatibleFormat']:
                raise BuildError('Native RenderTexture format/fallback contract changed.')
            row.update(colorFormat=fields['m_ColorFormat'],depthStencilFormat=fields['m_DepthStencilFormat'],
                       compatibleFormatFallback=fields['m_EnableCompatibleFormat'],dimension=fields['m_Dimension'],
                       antiAliasing=fields['m_AntiAliasing'],nativeEncoding='runtime-render-target-no-serialized-PC-texels')
        assets.append(row)
    receipt={'schema':1,'nativePlatformImageCount':len(assets),'unsupportedImageClassCount':0,'assets':assets,
             'androidGpuFormatsVerified':False,'headsetPictureVerified':False}
    write_json(project/'Assets/QuestOriginalCampaign/native-platform-images.json',receipt)
    return receipt
