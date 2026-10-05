"""Fail-closed original Shader/SVC retention in actual Android native bytes.

No compiler is invoked. Native Vulkan stores VS+FS together in progVertex.
The original coarse SVC projection is checked separately from pass/tier aliases.
"""
import hashlib
import struct
import re
import json
import gc
import zipfile
from pathlib import Path

from storage import BuildError, digest, write_json

class ClosureError(BuildError):
    pass

def require(condition,message):
    if not condition: raise ClosureError(message)

PASS_TYPES={'Normal':0,'Vertex':1,'VertexLM':2,'VertexLMRGBM':3,'ForwardBase':4,
 'ForwardAdd':5,'LightPrePassBase':6,'LightPrePassFinal':7,'ShadowCaster':8,
 'Deferred':10,'Meta':11,'MotionVectors':12,'ScriptableRenderPipelineDefaultUnlit':13,
 'ScriptableRenderPipeline':14}

def native_aliases(tree, expected):
    require(tree.get('platforms')==[18],'Expected actual Vulkan compiler platform18')
    require(tree.get('m_ShaderIsBaked') is True,'Native Shader is not player-baked')
    parsed=tree['m_ParsedForm'];require(parsed['m_Name']==expected['originalName'],'Native Shader name changed')
    names=parsed['m_KeywordNames'];require(len(names)==len(set(names)),'Duplicate native keyword names')
    aliases={}
    for si,sub in enumerate(parsed['m_SubShaders']):
        for pi,shader_pass in enumerate(sub['m_Passes']):
            # Unity Vulkan stores the actual VS+FS bank in progVertex; an empty
            # progFragment is normal and must not fabricate a missing stage.
            require(not shader_pass['progFragment']['m_SubPrograms'],'Unexpected separately serialized Vulkan fragment program')
            for value in shader_pass['progVertex']['m_SubPrograms']:
                require(value['m_GpuProgramType']==25,'Unexpected GPU program in Vulkan bank')
                tier=value['m_ShaderHardwareTier'];require(tier in (0,1,2),'Unknown native hardware tier')
                indices=value['m_KeywordIndices'];require(len(indices)==len(set(indices)),'Duplicate native keyword indices')
                require(all(type(i) is int and 0<=i<len(names) for i in indices),'Native keyword index outside its actual space')
                key=(si,pi,tier,tuple(sorted(names[i] for i in indices)))
                blob=value['m_BlobIndex'];require(type(blob) is int and blob>=0,'Invalid native program pointer')
                require(key not in aliases or aliases[key]==blob,'Conflicting native alias duplicate')
                aliases[key]=blob
    return aliases

def alias_report(tree, expected):
    actual=native_aliases(tree,expected)
    wanted={(v['subshader'],v['pass'],v['hardwareTier'],tuple(sorted(v['keywords']))) for v in expected['variants']}
    missing=sorted(wanted-actual.keys());instanced=sum('INSTANCING_ON' in a[3] for a in missing)
    return {'guid':expected['guid'],'name':expected['originalName'],'originalAliasCount':len(wanted),
      'actualCombinedVulkanAliasCount':len(actual),'originalAliasCountPresent':len(wanted)-len(missing),
      'missingOriginalAliasCount':len(missing),'missingInstancingAliasCount':instanced,
      'missingNonInstancingAliasCount':len(missing)-instanced,
      'missingSample':[{'subshader':s,'pass':p,'tier':t,'keywords':list(k)} for s,p,t,k in missing[:8]],
      'allOriginalAliasesRetained':not missing,'stagesSerializedTogether':True,
      'compilerExecuted':False,'hardwarePictureVerified':False}

def require_closure(tree,expected):
    report=alias_report(tree,expected)
    require(report['allOriginalAliasesRetained'],'Actual cooked shader strips original native keyword aliases: '+expected['guid'])
    return report

def stage_ranges(code):
    require(len(code)>=52,'Native Vulkan stage table truncated')
    ranges=[];stages=[]
    for stage in range(6):
        start,count=struct.unpack_from('<II',code,4+8*stage)
        if not count:
            require(start==0,'Empty native stage has an offset');continue
        require(52<=start<len(code) and count<=len(code)-start,'Native Vulkan stage extent invalid')
        require(not any(start<b and start+count>a for a,b in ranges),'Native stage payload overlap')
        payload=code[start:start+count];require(len(payload)>=24 and payload[:4]==b'LOMS','Native stage is not a SMOL-V payload')
        version,generator,bound,reserved,decoded=struct.unpack_from('<5I',payload,4)
        require(version>>24<=1 and 0x10000<=version&0x00ffffff<=0x10600 and bound>0 and reserved==0 and 20<=decoded<=64*1024*1024 and decoded%4==0,'Native SMOL-V header invalid')
        ranges.append((start,start+count));stages.append({'nativeStageIndex':stage,'compressedBytes':count,'decodedBytesDeclared':decoded,'compressedSha256':hashlib.sha256(payload).hexdigest()})
    require([s['nativeStageIndex'] for s in stages]==[0,1],'Native Vulkan bank does not contain only its vertex and fragment stages')
    return stages

def program_sample(tree,indices):
    from UnityPy.helpers import CompressionHelper
    from UnityPy.export.ShaderConverter import ShaderSubProgram
    from UnityPy.streams import EndianBinaryReader
    require(tree['platforms']==[18],'Program sample is not Vulkan')
    offsets,lengths,sizes=tree['offsets'][0],tree['compressedLengths'][0],tree['decompressedLengths'][0]
    require(len(offsets)==len(lengths)==len(sizes)>0,'Native compression metadata count differs')
    blob=bytes(tree['compressedBlob']);segments=[]
    for offset,count,size in zip(offsets,lengths,sizes):
        require(0<=offset<len(blob) and 0<count<=len(blob)-offset and 0<size<=256*1024*1024,'Native compressed segment invalid')
        raw=CompressionHelper.decompress_lz4(blob[offset:offset+count],size);require(len(raw)==size,'Native segment decompressed size differs');segments.append(raw)
    directory=segments[0];require(len(directory)>=4,'Native program directory truncated')
    count=struct.unpack_from('<i',directory)[0];require(count>=0 and count<1000000 and 4+count*12<=len(directory),'Native program directory malformed')
    all_indices = set(native_aliases(tree, {'originalName': tree['m_ParsedForm']['m_Name']}).values())
    for index in all_indices:
        require(type(index) is int and 0 <= index < count, 'Native alias points outside program directory')
        offset, length, segment = struct.unpack_from('<3i', directory, 4 + index * 12)
        require(0 <= segment < len(segments) and 0 <= offset < len(segments[segment])
            and 8 <= length <= len(segments[segment]) - offset
            and (segment != 0 or offset >= 4 + count * 12), 'Native program extent invalid')
        require(struct.unpack_from('<i', segments[segment], offset + 4)[0] == 25,
            'Native alias points to a non-Vulkan program body')
    output=[]
    for index in indices:
        require(type(index)is int and 0<=index<count,'Native alias points outside program directory')
        offset,length,segment=struct.unpack_from('<3i',directory,4+index*12)
        require(0<=segment<len(segments) and 0<=offset<len(segments[segment]) and 0<length<=len(segments[segment])-offset,'Native program extent invalid')
        value=ShaderSubProgram(EndianBinaryReader(segments[segment][offset:offset+length],endian='<'))
        require(int(value.m_ProgramType)==25,'Decoded native program is not Vulkan SPIR-V')
        stages=stage_ranges(value.m_ProgramCode)
        output.append({'blobIndex':index,'codeBytes':len(value.m_ProgramCode),'codeSha256':hashlib.sha256(value.m_ProgramCode).hexdigest(),'stages':stages})
    return output

def svc_map(tree,external_paths):
    result={}
    for pointer,value in tree['m_Shaders']:
        index=pointer['m_FileID'];require(type(index)is int and 1<=index<=len(external_paths) and pointer['m_PathID']==1,'Native SVC shader pointer is invalid')
        guid=external_paths[index-1];require(re.fullmatch('[0-9a-f]{32}',guid) is not None,'Native SVC pointer is not an exact original GUID-file')
        require(guid not in result,'Duplicate SVC shader reference')
        variants=[]
        for v in value['variants']:
            keywords=v['keywords'].split();require(len(keywords)==len(set(keywords)),'Duplicate SVC keyword')
            variants.append((v['passType'],tuple(sorted(keywords))))
        require(len(variants)==len(set(variants)),'Duplicate SVC variant')
        result[guid]=set(variants)
    return result

def svc_sample(collection,expected):
    require(expected['guid'] in collection,'Original SVC GUID absent')
    projected={(PASS_TYPES[v['passType']],tuple(sorted(v['keywords']))) for v in expected['variants']}
    actual=collection[expected['guid']];require(actual==projected,'Actual SVC projection differs from original alias projection')
    return {'guid':expected['guid'],'originalAliasCount':len(expected['variants']),'nativeSVCProjectedEntries':len(actual),'projectionExactlyPreserved':True,'nativeExecutableCoverageProven':False}


def material_shader_link(material,shader_origin):
    require(material['shaderGuid']==shader_origin['guid'],'Material links a different original shader GUID')
    actual=material['nativeShaderPPtr'];require(actual['pathId']==shader_origin['nativePathId'],'Material native Shader pathID changed')
    require(actual['externalPath'].rsplit('/',1)[-1].lower()==shader_origin['nativeFile'].lower(),'Material references a different native shader container')
    require(material['actualEnableInstancing']==material['sourceEnableInstancing'],'Original material instancing flag changed')
    require(material['actualKeywords']==material['sourceKeywords'],'Original material keywords changed')
    require(not material['actualInvalidKeywords'],'Native material has invalid original keywords')
    return {'materialGuid':material['materialGuid'],'shaderGuid':material['shaderGuid'],'exactNativeShaderPPtrVerified':True,'originalKeywordFlagsPreserved':True}


COLLECTION_PATH = 'Assets/Quest/CampaignShaders/QuestCampaignShaderVariants.shadervariants'
PLAYER_COLLECTION_PATH = 'Assets/Resources/QuestCampaignShaderVariants.shadervariants'
GUID_PATTERN = re.compile(r'[0-9a-f]{32}')


def contract_rows(manifest, full=False):
    require(manifest.get('schema') == 1 and manifest.get('scope') == 'campaign-compiler'
        and manifest.get('graphicsApi') == 'Vulkan' and manifest.get('compilerPlatform') == 'Vulkan',
        'Cooked Shader retention requires the original Vulkan compiler manifest')
    rows = manifest.get('shaders', [])
    require(rows and all(isinstance(row, dict) for row in rows), 'Missing original shader identities')
    by_guid = {}
    by_path = {}
    total = 0
    for row in rows:
        guid, path = row.get('guid'), row.get('assetPath')
        require(isinstance(guid, str) and GUID_PATTERN.fullmatch(guid), 'Invalid original Shader GUID')
        require(guid not in by_guid, 'Duplicate original Shader GUID')
        require(isinstance(path, str) and path.startswith('Assets/') and '\\' not in path
            and all(part not in ('', '.', '..') for part in path.split('/')), 'Invalid original Shader asset path')
        require(path.lower() not in by_path, 'Duplicate original Shader asset root')
        require(isinstance(row.get('originalName'), str) and row['originalName'], 'Missing original Shader name')
        variants = row.get('variants', [])
        require(variants, 'Original Shader has no native alias contract')
        for value in variants:
            require(value.get('coverageKind') == 'original-native', 'Invented Shader alias in original contract')
            require(value.get('passType') in PASS_TYPES, 'Unknown original Shader pass projection')
            require(type(value.get('subshader')) is int and value['subshader'] >= 0
                and type(value.get('pass')) is int and value['pass'] >= 0
                and value.get('hardwareTier') in (0, 1, 2), 'Malformed original Shader alias')
            keys = value.get('keywords')
            require(isinstance(keys, list) and all(isinstance(k, str) and k for k in keys)
                and len(keys) == len(set(keys)), 'Malformed original Shader keywords')
        by_guid[guid] = row
        by_path[path.lower()] = row
        total += len(variants)
    require(manifest.get('requiredSyntheticAliasCount', 0) == 0, 'Synthetic aliases cannot satisfy original retention')
    if full:
        require(len(rows) == manifest.get('requiredShaderCount') == 688
            and total == manifest.get('requiredOriginalNativeAliasCount') == 51564
            and manifest.get('requiredMaterialCount') == 9187, 'Original full-game Shader census changed')
    return by_guid, by_path


def pointer_key(file, pointer):
    index, path_id = pointer['m_FileID'], pointer['m_PathID']
    require(type(index) is int and 0 <= index <= len(file.externals)
        and type(path_id) is int and path_id != 0, 'Invalid cooked native asset pointer')
    name = file.name if index == 0 else file.externals[index - 1].path.rsplit('/', 1)[-1]
    require(isinstance(name, str) and name, 'Cooked pointer loses its serialized container')
    return name.lower(), path_id


def collection_projection(tree, file, shader_objects):
    result = {}
    for pointer, value in tree.get('m_Shaders', []):
        key = pointer_key(file, pointer)
        require(key in shader_objects, 'Native AA-SVC refers to an unowned or missing Shader object')
        guid = shader_objects[key]
        require(guid not in result, 'Native AA-SVC repeats an original Shader GUID')
        projected = []
        for variant in value['variants']:
            keywords = variant['keywords'].split()
            require(len(keywords) == len(set(keywords)), 'Duplicate cooked SVC keyword')
            projected.append((variant['passType'], tuple(sorted(keywords))))
        require(len(projected) == len(set(projected)), 'Duplicate cooked SVC alias')
        result[guid] = set(projected)
    return result


def require_collection(tree, file, shader_objects, contracts):
    collection = collection_projection(tree, file, shader_objects)
    require(collection.keys() == contracts.keys(), 'Cooked AA-SVC omits or adds original Shader GUID identities')
    receipts = [svc_sample(collection, row) for row in contracts.values()]
    return {'shaderCount': len(collection), 'projectedEntryCount': sum(len(value) for value in collection.values()),
        'allOriginalProjectionsRetained': True, 'originalAliases': sum(len(row['variants']) for row in contracts.values())}


def catalog_plan(decoded, contracts, material_rows=(), sample_count=16):
    require(type(sample_count) is int and 0 <= sample_count <= 64, 'Invalid bounded material sample count')
    paths = {row['assetPath'].lower(): row for row in contracts.values()}
    shader_locations, collection_locations, materials = {}, [], []
    material_paths = {row['assetPath'].lower(): row for row in material_rows
        if row.get('shaderGuid') in contracts}
    for location in decoded['locations']:
        name = location['resourceType']['m_ClassName']
        internal = location['internalId'].replace('\\', '/')
        if name == 'UnityEngine.Shader' and location['provider'].endswith('.BundledAssetProvider') and internal.lower() in paths:
            guid = paths[internal.lower()]['guid']
            require(guid not in shader_locations, 'Duplicate cooked original Shader catalog location')
            shader_locations[guid] = location
        if name == 'UnityEngine.ShaderVariantCollection' and location['provider'].endswith('.BundledAssetProvider') and internal.lower() == COLLECTION_PATH.lower():
            collection_locations.append(location)
        if name == 'UnityEngine.Material' and location['provider'].endswith('.BundledAssetProvider') and internal.lower() in material_paths:
            materials.append((material_paths[internal.lower()], location))
    require(shader_locations.keys() == contracts.keys(),
        'Actual native catalog omits explicit original Shader GUID asset roots')
    require(len(collection_locations) == 1, 'Actual native catalog omits or duplicates its original SVC build input')
    chosen, selected_guids = [], set()
    # One deterministic public native material per shader, bounded by sample_count.
    for row, location in sorted(materials, key=lambda item: item[0]['guid']):
        if len(chosen) >= sample_count: break
        if row['shaderGuid'] in selected_guids: continue
        chosen.append((row, location)); selected_guids.add(row['shaderGuid'])
    required_locations = list(shader_locations.values()) + collection_locations + [loc for _, loc in chosen]
    pending = [location['index'] for location in required_locations]
    visited, bundles = set(), set()
    while pending:
        index = pending.pop()
        require(type(index) is int and 0 <= index < len(decoded['locations']), 'Cooked catalog dependency outside location table')
        if index in visited: continue
        visited.add(index)
        location = decoded['locations'][index]
        pending.extend(location['dependencyEntries'])
        internal = location['internalId'].replace('\\', '/')
        if internal.endswith('.bundle'):
            prefix = '{UnityEngine.AddressableAssets.Addressables.RuntimePath}/'
            require(internal.startswith(prefix), 'Cooked Shader dependency is not a local native bundle')
            relative = internal[len(prefix):]
            require(relative and all(part not in ('', '.', '..') for part in relative.split('/')),
                'Cooked Shader dependency escapes its delivered bank')
            bundles.add('StreamingAssets/aa/' + relative)
    return {'bundles': sorted(bundles), 'shaderRoots': {row['assetPath'].lower(): row for row in contracts.values()},
        'materialRoots': {row['assetPath'].lower(): row for row, _ in chosen},
        'publicMaterialCount': len(materials), 'materialSampleCount': len(chosen)}


class NativeBundleAudit:
    """Resolve exact public roots and deferred CAB/pathID links, never shader names."""
    def __init__(self, contracts, plan, material_contract):
        self.contracts = contracts
        self.plan = plan
        self.material_contract = material_contract
        self.shaders, self.shader_objects, self.materials, self.collections = {}, {}, [], []
        self.origins = []

    def observe(self, env, entry, raw_sha256):
        bundles = [obj for obj in env.objects if obj.type.name == 'AssetBundle']
        require(len(bundles) == 1, 'Shader dependency has no exact native AssetBundle directory')
        bundle = bundles[0]; root_map = {}
        for path, record in bundle.read_typetree()['m_Container']:
            normalized = path.lower()
            if normalized not in self.plan['shaderRoots'] and normalized not in self.plan['materialRoots'] \
                and normalized != COLLECTION_PATH.lower(): continue
            require(normalized not in root_map, 'Conflicting original native public root')
            root_map[normalized] = pointer_key(bundle.assets_file, record['asset'])
        objects = {(obj.assets_file.name.lower(), obj.path_id): obj for obj in env.objects}
        for path, key in root_map.items():
            require(key in objects, 'Native public Shader/SVC/Material root refers outside its owner payload')
            obj = objects[key]
            if path in self.plan['shaderRoots']:
                row = self.plan['shaderRoots'][path]
                require(obj.type.name == 'Shader', 'Original public Shader root changed its native type')
                require(row['guid'] not in self.shaders, 'Original Shader root appears in two actual native owners')
                tree = obj.read_typetree(); receipt = require_closure(tree, row)
                aliases = native_aliases(tree, row)
                receipt['programSamples'] = program_sample(tree, sorted(set(aliases.values()))[:2])
                receipt.update(assetPath=row['assetPath'], entry=entry, entrySha256=raw_sha256,
                    nativeContainer=key[0], nativePathId=key[1])
                self.shaders[row['guid']] = receipt
                require(key not in self.shader_objects, 'Two original GUIDs alias the same cooked Shader identity')
                self.shader_objects[key] = row['guid']
            elif path in self.plan['materialRoots']:
                row = self.plan['materialRoots'][path]
                require(obj.type.name == 'Material', 'Original public Material root changed its native type')
                tree = obj.read_typetree(); source = self.material_contract(row)
                require(tree.get('m_EnableInstancingVariants') == source['enableInstancing'], 'Cooked original material instancing flag changed')
                actual_keys = tree.get('m_ValidKeywords', tree.get('m_ShaderKeywords', []))
                if isinstance(actual_keys, str): actual_keys = actual_keys.split()
                require(actual_keys == source['keywords'] and tree.get('m_InvalidKeywords', []) == source.get('invalidKeywords', []),
                    'Cooked original material keywords changed or became invalid')
                self.materials.append({'guid': row['guid'], 'shaderGuid': row['shaderGuid'],
                    'assetPath': row['assetPath'], 'nativeKey': pointer_key(obj.assets_file, tree['m_Shader']),
                    'enableInstancing': source['enableInstancing'], 'keywords': source['keywords'], 'invalidKeywords': source.get('invalidKeywords', []),
                    'sourceSha256': source['sha256'], 'entry': entry, 'entrySha256': raw_sha256})
            else:
                require(obj.type.name == 'ShaderVariantCollection', 'Cooked original SVC root changed its native type')
                from types import SimpleNamespace
                saved = SimpleNamespace(name=obj.assets_file.name, externals=[SimpleNamespace(path=e.path) for e in obj.assets_file.externals])
                self.collections.append((obj.read_typetree(), saved))
        self.origins.append({'entry': entry, 'sha256': raw_sha256, 'originalRootCount': len(root_map)})

    def finish(self):
        require(self.shaders.keys() == self.contracts.keys(), 'Actual native shader bank omits original Shader roots')
        require(len(self.collections) == 1, 'Actual native shader bank omits or duplicates its original collection')
        require(len(self.materials) == self.plan['materialSampleCount'], 'Actual native material sample closure incomplete')
        svc = require_collection(*self.collections[0], self.shader_objects, self.contracts)
        for material in self.materials:
            require(self.shader_objects.get(material['nativeKey']) == material['shaderGuid'],
                'Original material points to a missing or different cooked Shader GUID')
            material['exactNativeShaderPPtrVerified'] = True
        return {'schema': 1, 'scope': 'actual-native-original-shader-keyword-closure', 'graphicsApi': 'Vulkan',
            'shaderCount': len(self.shaders), 'originalNativeAliasCount': sum(len(row['variants']) for row in self.contracts.values()),
            'allOriginalAliasesRetained': True, 'actualNativeAliasMetadataVerified': True,
            'nativeVariantCollection': svc, 'shaders': list(self.shaders.values()),
            'nativeMaterialPublicRootCount': self.plan['publicMaterialCount'],
            'materialSampleCount': len(self.materials), 'materials': [{k: v for k, v in row.items() if k != 'nativeKey'} for row in self.materials],
            'all9187MaterialsAudited': False, 'nativePayloads': self.origins,
            'compilerQueries': 0, 'hardwarePictureVerified': False, 'originalPixelParityVerified': False}


def source_material(project, row):
    import yaml
    class MaterialLoader(yaml.SafeLoader): pass
    MaterialLoader.add_multi_constructor('tag:unity3d.com,2011:', lambda loader, tag, node: loader.construct_mapping(node, deep=True))
    path = Path(project) / row['assetPath']
    require(path.is_file() and not path.is_symlink(), 'Missing original source material for cooked binding audit')
    raw = path.read_bytes()
    material = yaml.load(raw.decode('utf-8-sig'), Loader=MaterialLoader)['Material']
    require(material['m_Shader']['guid'] == row['shaderGuid'], 'Original material manifest points at different Shader GUID')
    keywords = material.get('m_ValidKeywords', material.get('m_ShaderKeywords', []))
    if isinstance(keywords, str): keywords = keywords.split()
    return {'enableInstancing': bool(material['m_EnableInstancingVariants']), 'keywords': keywords, 'invalidKeywords': material.get('m_InvalidKeywords', []),
        'sha256': hashlib.sha256(raw).hexdigest()}


def unique_zip(archive):
    names = archive.namelist()
    require(len(names) == len(set(names)), 'Duplicate entry in actual native payload archive')
    return set(names)


def validate_delivered(source, project, apk, bank, evidence, *, material_sample_count=16):
    """Audit exact all-688 native roots; read only their catalog dependency closure."""
    import UnityPy
    import campaign_shaders
    project, apk, bank = map(Path, (project, apk, bank))
    manifest_path = project / 'Assets/QuestOriginalCampaign/campaign-shaders.json'
    manifest_bytes = manifest_path.read_bytes(); manifest = json.loads(manifest_bytes)
    artifacts_before = [(path.stat().st_ino, path.stat().st_size, path.stat().st_mtime_ns) for path in (apk, bank)]
    contracts, _ = contract_rows(manifest, full=True)
    decoder = campaign_shaders.load(Path(source), 'tools/quest-recovery/catalog.py', 'tools/quest-recovery')
    apk_shader_receipts = []
    with zipfile.ZipFile(apk) as player:
        player_names = unique_zip(player)
        metadata = (project / (PLAYER_COLLECTION_PATH + '.meta')).read_text()
        match = re.search(r'^guid: ([0-9a-f]{32})$', metadata, re.M)
        require(match is not None, 'Original source SVC has no exact generated GUID')
        svc_entry = 'assets/bin/Data/' + match[1]
        require(svc_entry in player_names, 'Actual signed player omits its original Resource SVC')
        raw = player.read(svc_entry); env = UnityPy.load(raw)
        objects = [obj for obj in env.objects if obj.type.name == 'ShaderVariantCollection']
        require(len(objects) == 1 and objects[0].path_id == 1, 'Actual player Resource SVC identity changed')
        obj = objects[0]
        collection = svc_map(obj.read_typetree(), [external.path for external in obj.assets_file.externals])
        require(collection.keys() == contracts.keys(), 'Actual player Resource SVC GUID closure differs')
        for row in contracts.values(): svc_sample(collection, row)
        player_svc = {'entry': svc_entry, 'sha256': hashlib.sha256(raw).hexdigest(), 'shaderCount': len(collection),
            'projectedEntryCount': sum(len(value) for value in collection.values()), 'allOriginalProjectionsRetained': True}
        del env, raw, obj, objects; gc.collect()
        for index, row in enumerate(contracts.values()):
            entry = 'assets/bin/Data/' + row['guid']
            require(entry in player_names, 'Signed native player omits original Shader GUID-file: ' + row['guid'])
            raw = player.read(entry); env = UnityPy.load(raw)
            shaders = [obj for obj in env.objects if obj.type.name == 'Shader']
            require(len(shaders) == 1 and shaders[0].path_id == 1, 'Original opaque Shader GUID-file changed its typed object')
            tree = shaders[0].read_typetree(); receipt = require_closure(tree, row)
            receipt.update(entry=entry, sha256=hashlib.sha256(raw).hexdigest())
            receipt['programSamples'] = program_sample(tree, sorted(set(native_aliases(tree, row).values()))[:2])
            apk_shader_receipts.append(receipt)
            del env, raw, shaders, tree
            if (index + 1) % 32 == 0: gc.collect()
    with zipfile.ZipFile(bank) as content:
        bank_names = unique_zip(content)
        catalog_raw = content.read('StreamingAssets/aa/catalog.json')
        decoded = decoder.decode_catalog(json.loads(catalog_raw))
        plan = catalog_plan(decoded, contracts, manifest.get('materials', []), material_sample_count)
        audit = NativeBundleAudit(contracts, plan, lambda row: source_material(project, row))
        for index, entry in enumerate(plan['bundles']):
            require(entry in bank_names, 'Original Shader dependency missing from actual delivered bank')
            raw = content.read(entry); env = UnityPy.load(raw)
            audit.observe(env, entry, hashlib.sha256(raw).hexdigest())
            del env, raw
            if (index + 1) % 16 == 0: gc.collect()
        result = audit.finish()
        result.update(originalManifestSha256=hashlib.sha256(manifest_bytes).hexdigest(),
            actualNativeCatalogSha256=hashlib.sha256(catalog_raw).hexdigest(),
            apkResourceCollection=player_svc, apkShaders=apk_shader_receipts,
            selectedNativeBundleCount=len(plan['bundles']))
    require(manifest_path.read_bytes() == manifest_bytes, 'Original Shader manifest changed during native delivery audit')
    require(artifacts_before == [(path.stat().st_ino, path.stat().st_size, path.stat().st_mtime_ns) for path in (apk, bank)],
        'Actual native artifact changed during Shader delivery audit')
    write_json(Path(evidence), result)
    return result
