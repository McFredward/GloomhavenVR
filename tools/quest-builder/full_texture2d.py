"""Audit native Texture2D import contracts by exact original object identity."""
from collections import Counter,defaultdict
from dataclasses import dataclass
import gc
import hashlib
import json
from pathlib import Path
import struct
import sys

from storage import BuildError,write_json, build_progress
import asset_jobs
import host_resources

NORMAL_USAGE={3,4,10} # Pinned AssetRipper TextureUsageMode enum, read from actual PE metadata.
IMAGE_SUFFIXES={'.png','.tga','.jpg','.jpeg'}


def image_dimensions(path):
    with path.open('rb') as stream:header=stream.read(32)
    if header[:8]==b'\x89PNG\r\n\x1a\n' and header[12:16]==b'IHDR':return struct.unpack_from('>II',header,16)
    if path.suffix.casefold()=='.tga' and len(header)>=18:return struct.unpack_from('<HH',header,12)
    raise BuildError('Ordinary recovered texture has an unwitnessed image container.')


def native_contract(fields):
    settings=fields['m_TextureSettings']
    return {'width':fields['m_Width'],'height':fields['m_Height'],'mipCount':fields['m_MipCount'],
            'mipsStripped':fields['m_MipsStripped'],'nativeFormat':fields['m_TextureFormat'],
            'colorSpace':fields['m_ColorSpace'],'srgb':fields['m_ColorSpace']==1,
            'usageMode':fields['m_LightmapFormat'],'normalMap':fields['m_LightmapFormat'] in NORMAL_USAGE,
            'filterMode':settings['m_FilterMode'],'aniso':settings['m_Aniso'],'mipBias':settings['m_MipBias'],
            'wrapU':settings['m_WrapU'],'wrapV':settings['m_WrapV'],'wrapW':settings['m_WrapW'],
            'isReadable':fields['m_IsReadable'],'streamingMipmaps':fields['m_StreamingMipmaps'],
            'streamingMipmapsPriority':fields['m_StreamingMipmapsPriority']}


def importer_differences(native,document,dimensions):
    importer=document.get('TextureImporter')
    if not isinstance(importer,dict):raise BuildError('Recovered ordinary texture lacks its original importer.')
    mip=importer['mipmaps'];settings=importer['textureSettings'];differences=[]
    checks=[('imageDimensions',tuple(dimensions),(native['width'],native['height'])),
            ('srgb',bool(mip['sRGBTexture']),native['srgb']),
            ('mipmapEnabled',bool(mip['enableMipMap']),native['mipCount']>1),
            ('normalMapClassification',int(importer['textureType'])==1,native['normalMap']),
            ('filterMode',settings['filterMode'],native['filterMode']),('aniso',settings['aniso'],native['aniso']),
            ('mipBias',settings['mipBias'],native['mipBias']),
            ('wrapU',settings['wrapU'],native['wrapU']),('wrapV',settings['wrapV'],native['wrapV']),('wrapW',settings['wrapW'],native['wrapW']),
            ('isReadable',bool(importer['isReadable']),native['isReadable']),
            ('streamingMipmaps',bool(importer['streamingMipmaps']),native['streamingMipmaps']),
            ('streamingMipmapsPriority',importer['streamingMipmapsPriority'],native['streamingMipmapsPriority'])]
    for field,current,expected in checks:
        if current!=expected:differences.append({'field':field,'current':current,'native':expected})
    required=max(native['width'],native['height'])
    if importer['maxTextureSize']<required:differences.append({'field':'maxTextureSize','current':importer['maxTextureSize'],'nativeMinimum':required})
    for platform in importer['platformSettings']:
        if platform['overridden'] and platform['maxTextureSize']<required:
            differences.append({'field':'platformMaxTextureSize','platform':platform['buildTarget'],'current':platform['maxTextureSize'],'nativeMinimum':required})
    return differences


def audit(project,game_data,*,cab_bundles,output=None):
    """Read-only bounded source/metadata audit. Return genuine mismatches."""
    recovery=Path(__file__).resolve().parents[1]/'quest-recovery'
    if str(recovery) not in sys.path:sys.path.append(str(recovery))
    from export_identity import object_index
    from pointer_recovery import load_native
    from recover import sha256
    import UnityPy,yaml
    project,game_data=Path(project),Path(game_data)
    objects=object_index(json.loads((project/'QuestRecovery/original-asset-identities.json').read_text())['identities'])
    owners=json.loads(Path(cab_bundles).read_text()) if isinstance(cab_bundles,(str,Path)) else cab_bundles
    owners={key.casefold():value for key,value in owners.items()}
    groups=defaultdict(list);embedded=[]
    for key,target in objects.items():
        if target['classId']!=28:continue
        if Path(target['path']).suffix.casefold() not in IMAGE_SUFFIXES:
            embedded.append({'assetPath':target['path'],'guid':target['guid'],'originalCollection':key[0],'originalPathId':key[1]});continue
        groups[owners.get(key[0],key[0])].append((key,target))
    counter = build_progress.Counter('prepare-items:ordinary-texture-audit', sum(len(targets) for targets in groups.values()), "items")
    try:
        assets,containers=[],[]
        for container,targets in sorted(groups.items()):
            env=load_native(UnityPy,game_data/container)
            native_objects={(obj.assets_file.name.casefold(),int(obj.path_id)):obj for obj in env.objects}
            container_sha=sha256(game_data/container);containers.append({'path':container,'sha256':container_sha})
            for key,target in targets:
                original=native_objects.get(key)
                if original is None:raise BuildError('Original ordinary Texture2D identity is unresolved.')
                contract=native_contract(original.read_typetree());path=project/target['path'];meta=Path(str(path)+'.meta')
                document=yaml.load(meta.read_text(),Loader=getattr(yaml,'CSafeLoader',yaml.SafeLoader))
                differences=importer_differences(contract,document,image_dimensions(path))
                full_mips=max(contract['width'],contract['height']).bit_length()
                assets.append({'assetPath':target['path'],'guid':target['guid'],'fileId':target['fileId'],
                    'originalCollection':key[0],'originalPathId':key[1],'sourceContainer':container,
                    'originalObjectSha256':hashlib.sha256(original.get_raw_data()).hexdigest(),
                    'metaSha256':sha256(meta),'native':contract,'differences':differences,
                    'incompleteNativeMipChain':contract['mipCount'] not in (1,full_mips) or contract['mipsStripped']!=0,
                    'nativeFloatOrHdr':contract['nativeFormat'] in (9,15,16,17,18,19,20,22,24)})
                counter.add(1, target['path'])
            native_objects.clear()
            del original,env
            gc.collect()
        report={'schema':1,'ordinaryTexture2DCount':len(assets),'embeddedNativeTextureCount':len(embedded),
                'mismatchedTextureCount':sum(bool(row['differences']) for row in assets),
                'incompleteMipChainCount':sum(row['incompleteNativeMipChain'] for row in assets),
                'floatOrHdrTextureCount':sum(row['nativeFloatOrHdr'] for row in assets),
                'nativeFormatCounts':dict(sorted(Counter(row['native']['nativeFormat'] for row in assets).items())),
                'nativeUsageCounts':dict(sorted(Counter(row['native']['usageMode'] for row in assets).items())),
                'normalMapCount':sum(row['native']['normalMap'] for row in assets),
                'sourceContainers':containers,'assets':assets,'embeddedNativeTextures':embedded,
                'unityImportedTexturesVerified':False,'headsetGraphicsVerified':False}
        if output:write_json(Path(output),report)
        counter.finish()
        return report
    except BaseException as error:
        counter.fail(error)
        raise


def half_mip_sizes(width,height,count):
    if min(width,height,count)<=0 or max(width,height)>16384 or count not in (1,max(width,height).bit_length()):
        raise BuildError('Native Texture2D has an unwitnessed mip layout.')
    return [max(1,width>>mip)*max(1,height>>mip)*8 for mip in range(count)]


def native_yaml(fields,pixels,file_id):
    """Keep original half pixels and native sampler state without PNG quantization."""
    if fields['m_ImageCount']!=1 or fields['m_TextureDimension']!=2 or fields['m_MipsStripped'] or fields.get('m_PlatformBlob'):
        raise BuildError('Native Texture2D has an unwitnessed image/platform layout.')
    if len(pixels)!=sum(half_mip_sizes(fields['m_Width'],fields['m_Height'],fields['m_MipCount'])):
        raise BuildError('Portable Texture2D did not retain all native pixels/mips.')
    names=('m_ForcedFallbackFormat','m_DownscaleFallback','m_IsAlphaChannelOptional',
           'm_Width','m_Height','m_MipsStripped','m_MipCount','m_IsReadable','m_IsPreProcessed',
           'm_IgnoreMasterTextureLimit','m_StreamingMipmaps','m_StreamingMipmapsPriority',
           'm_ImageCount','m_LightmapFormat','m_ColorSpace')
    output='%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!28 &'+str(file_id)+'\nTexture2D:\n'
    output+='  m_ObjectHideFlags: 0\n  m_CorrespondingSourceObject: {fileID: 0}\n  m_PrefabInstance: {fileID: 0}\n  m_PrefabAsset: {fileID: 0}\n'
    output+='  m_Name: '+json.dumps(fields['m_Name'],ensure_ascii=False)+'\n'
    output+='  m_ImageContentsHash:\n    serializedVersion: 2\n    Hash: 00000000000000000000000000000000\n  serializedVersion: 2\n'
    for name in names:
        value=fields[name];output+='  '+name+': '+str(int(value) if isinstance(value,bool) else value)+'\n'
    output+='  m_CompleteImageSize: '+str(len(pixels))+'\n  m_TextureFormat: 17\n  m_TextureDimension: 2\n  m_VTOnly: 0\n  m_AlphaIsTransparency: 0\n'
    output+='  m_TextureSettings:\n    serializedVersion: 2\n'
    for name,value in fields['m_TextureSettings'].items():output+='    '+name+': '+format(value,'.9g')+'\n'
    output+='  m_PlatformBlob: \n  image data: '+str(len(pixels))+'\n  _typelessdata: '+pixels.hex()+'\n'
    return output+'  m_StreamData:\n    serializedVersion: 2\n    offset: 0\n    size: 0\n    path: \n'


def half_range_witness(pixels):
    """Record original range and precision facts rather than assume VAT encoding."""
    negative,above_one,unorm8_loss=None,None,None
    for index,(value,) in enumerate(struct.iter_unpack('<e',pixels)):
        if value<0 and negative is None:negative={'componentIndex':index,'value':value}
        if value>1 and above_one is None:above_one={'componentIndex':index,'value':value}
        if unorm8_loss is None and 0<=value<=1 and value!=round(value*255)/255:
            unorm8_loss={'componentIndex':index,'value':value,'nearestUnorm8':round(value*255)/255}
        if negative is not None and above_one is not None and unorm8_loss is not None:break
    return {'negativeComponent':negative,'aboveOneComponent':above_one,'unorm8PrecisionLossComponent':unorm8_loss}


def native_pixels(original,fields,game_data,container):
    try:return original.read().get_image_data()
    except FileNotFoundError:
        # Core serialized files keep texels in an owned .resS sidecar. Bundle
        # resources must resolve through their actual embedded CAB environment.
        if original.assets_file.name.casefold()!=Path(container).name.casefold():raise
        stream=fields['m_StreamData'];name=stream['path'].replace('\\','/').rsplit('/',1)[-1]
        path=game_data/name;offset,size=int(stream['offset']),int(stream['size'])
        if not name or not size or offset<0 or not path.is_file() or offset+size>path.stat().st_size:
            raise BuildError('Original core Texture2D streaming extent is unresolved.')
        with path.open('rb') as source:source.seek(offset);pixels=source.read(size)
        if len(pixels)!=size:raise BuildError('Original core Texture2D streaming extent is truncated.')
        return pixels


@dataclass
class _TextureJob:
    row: dict
    fields: dict
    before: str
    original_image_sha: str
    direct_pixels: bytes | None
    codec: object
    memory_bytes: int


def restore_float_textures(project,game_data,source_audit,*,dotnet,tool_cache,cab_bundles,jobs=None):
    """Run independent BC6H codecs, retaining single-author native publication."""
    recovery=Path(__file__).resolve().parents[1]/'quest-recovery'
    if str(recovery) not in sys.path:sys.path.append(str(recovery))
    from pointer_recovery import load_native
    from recover import sha256
    from full_textures import remap_manifests,restore_native_texture_pointer_types
    import portable_decoder,UnityPy
    project,game_data=Path(project),Path(game_data)
    if source_audit['schema']!=1 or source_audit['mismatchedTextureCount'] or source_audit['incompleteMipChainCount']:
        raise BuildError('Native ordinary Texture2D contracts require repair before float recovery.')
    selected=[row for row in source_audit['assets'] if row['nativeFloatOrHdr']]
    if len(selected)!=source_audit['floatOrHdrTextureCount'] or len({row['assetPath'] for row in selected})!=len(selected):
        raise BuildError('Native float Texture2D inventory is incomplete or repeated.')
    groups=defaultdict(list)
    for row in selected:groups[row['sourceContainer']].append(row)
    counter = build_progress.Counter('prepare-items:native-texture2d', len(selected), "items")
    decoded = False
    try:
        command=portable_decoder.build(tool_cache,dotnet)
        policy=host_resources.phase_budget('asset-codec',tool_cache)
        workers=min(policy['jobs'],host_resources.parse_jobs(jobs) or policy['jobs'])
        containers={row['path']:row['sha256'] for row in source_audit['sourceContainers']}
        assets,path_map=[],{}
        # Native readers use mutable resource positions. They stay in this
        # parent closure; only independent closed payloads reach codec threads.
        native_objects={};current_container=None;environment=None
        with asset_jobs.CodecCache(tool_cache,asset_jobs.codec_fingerprint(command)) as cache:
            def prepare(row):
                nonlocal native_objects,current_container,environment
                container=row['sourceContainer']
                if container!=current_container:
                    native_objects.clear();environment=None;gc.collect()
                    if sha256(game_data/container)!=containers[container]:raise BuildError('Original audited Texture2D container changed.')
                    environment=load_native(UnityPy,game_data/container)
                    native_objects={(obj.assets_file.name.casefold(),int(obj.path_id)):obj for obj in environment.objects}
                    current_container=container
                original=native_objects.get((row['originalCollection'],row['originalPathId']))
                if original is None or hashlib.sha256(original.get_raw_data()).hexdigest()!=row['originalObjectSha256']:
                    raise BuildError('Original audited Texture2D identity changed.')
                fields=original.read_typetree();path=project/row['assetPath'];meta=Path(str(path)+'.meta')
                if sha256(meta)!=row['metaSha256'] or native_contract(fields)!=row['native']:
                    raise BuildError('Audited ordinary Texture2D source/importer contract changed.')
                replacement=path.with_suffix('.texture2D')
                if replacement.exists():raise BuildError('Portable floating Texture2D output already exists.')
                raw=native_pixels(original,fields,game_data,container);source_format=fields['m_TextureFormat']
                expected=sum(half_mip_sizes(fields['m_Width'],fields['m_Height'],fields['m_MipCount']))
                codec=None;direct=None
                if source_format==17:direct=raw
                elif source_format==24:
                    codec=cache.prepare(row['guid'],raw,('bc6h-2d',fields['m_Width'],fields['m_Height'],fields['m_MipCount']),expected)
                else:raise BuildError('Original floating Texture2D requires a witnessed decoder: '+str(source_format))
                memory=codec.memory_bytes if codec else max(128*1048576,4*len(raw))
                return _TextureJob(row,fields,sha256(path),hashlib.sha256(raw).hexdigest(),direct,codec,memory)

            def execute(job,cancellation):
                return job.direct_pixels if job.codec is None else cache.execute(job.codec,command,cancellation)

            def publish(job,result):
                row,fields=job.row,job.fields
                if job.codec is None:pixels=result;stamp=None
                else:stamp=cache.witnesses.current(result);pixels=result.read_bytes()
                # The original layout/pixel guards run before accepting a cache
                # output or writing anything to the generated Unity project.
                text=native_yaml(fields,pixels,row['fileId'])
                pixel_sha=hashlib.sha256(pixels).hexdigest()
                if job.codec is not None:cache.accept(job.codec,pixel_sha,stamp)
                path=project/row['assetPath'];meta=Path(str(path)+'.meta');replacement=path.with_suffix('.texture2D')
                replacement.write_text(text)
                Path(str(replacement)+'.meta').write_text('fileFormatVersion: 2\nguid: '+row['guid']+'\nNativeFormatImporter:\n  externalObjects: {}\n  mainObjectFileID: '+str(row['fileId'])+'\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n')
                path.unlink();meta.unlink();new_path=replacement.relative_to(project).as_posix();path_map[row['assetPath']]=new_path
                cursor,mips=0,[]
                for mip,size in enumerate(half_mip_sizes(fields['m_Width'],fields['m_Height'],fields['m_MipCount'])):
                    data=pixels[cursor:cursor+size];cursor+=size
                    mips.append({'mip':mip,'size':size,'sha256':hashlib.sha256(data).hexdigest()})
                assets.append({'assetPath':new_path,'originalRecoveredPath':row['assetPath'],'guid':row['guid'],'fileId':row['fileId'],
                    'originalCollection':row['originalCollection'],'originalPathId':row['originalPathId'],'sourceContainer':row['sourceContainer'],
                    'sourceContainerSha256':containers[row['sourceContainer']],'originalObjectSha256':row['originalObjectSha256'],
                    'originalStreamData':fields['m_StreamData'],
                    'originalImageSha256':job.original_image_sha,'beforeSha256':job.before,'sha256':sha256(replacement),
                    'sourceFormat':fields['m_TextureFormat'],'textureFormat':17,'native':row['native'],
                    'width':fields['m_Width'],'height':fields['m_Height'],'mipCount':fields['m_MipCount'],
                    'isReadable':fields['m_IsReadable'],'pixelByteCount':len(pixels),'pixelSha256':pixel_sha,
                    'originalHalfBytesPreserved':fields['m_TextureFormat']==17,'sourceMipChainPreserved':True,'mips':mips,
                    'originalHalfValueWitness':half_range_witness(pixels) if fields['m_TextureFormat']==17 else None})
                counter.add(1,row['assetPath'])

            ordered=[row for _,rows in sorted(groups.items()) for row in rows]
            asset_jobs.ordered_pipeline(ordered,prepare,execute,publish,jobs=workers,byte_budget=policy['memoryBudgetBytes'])
            counter.finish()
            decoded = True
            print('asset-codecs: '+json.dumps({'phase':'native-texture2d','workers':workers,'items':len(selected),**cache.stats}),flush=True)
        native_objects.clear();environment=None;gc.collect()
        receipt={'schema':1,'nativeTexture2DCount':len(assets),'originalHalfTextureCount':sum(row['sourceFormat']==17 for row in assets),
                 'originalBc6hTextureCount':sum(row['sourceFormat']==24 for row in assets),'assets':assets,'pathMap':path_map,
                 'updatedManifests':remap_manifests(project,path_map,progress_scope='native-texture2d'),'unityImportVerified':False,
                 'originalBc6GpuParityVerified':False,'headsetGpuVerified':False}
        write_json(project/'Assets/QuestOriginalCampaign/native-texture2d.json',receipt)
        receipt['nativeTextureReferences']=restore_native_texture_pointer_types(project,progress_scope='native-texture2d')
        return receipt
    except BaseException as error:
        if not decoded: counter.fail(error)
        raise


def stage(project,game_data,*,dotnet,tool_cache,cab_bundles):
    source_audit=audit(project,game_data,cab_bundles=cab_bundles,
        output=Path(project)/'Assets/QuestOriginalCampaign/ordinary-texture2d-audit.json')
    return restore_float_textures(project,game_data,source_audit,dotnet=dotnet,tool_cache=tool_cache,cab_bundles=cab_bundles)
