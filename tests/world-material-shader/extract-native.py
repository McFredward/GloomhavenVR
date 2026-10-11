#!/usr/bin/env python3
"""Read-only extraction of native Windows programs used to derive the GL surrogate.

Shader names are never path-ID identities; every object is pinned to its source,
serialized file, raw object hash, table record and compiled program digest.
The output is private evidence, not redistributable original shader payload.
"""
import argparse, hashlib, json, struct
from pathlib import Path
import UnityPy
from UnityPy.export.ShaderConverter import ShaderSubProgram
from UnityPy.streams import EndianBinaryReader
from UnityPy.helpers.CompressionHelper import decompress_lz4

FAMILIES = {
    'Amp_Basic_N_MRAO': 1, 'Amp_Low/Amp_Basic_N_MRAO_Low': 2,
    'Amp_Basic_WallFade': 3, 'Amp_Low/Amp_Basic_WallFade_Low': 4,
    'Amp_Basic': 5, 'Amp_Low/Amp_Basic_Low': 6,
    'Amp_Basic_Prop_Shader': 7, 'Amp_Low/Amp_Basic_Prop_Shader_Low': 8,
    'Standard': 9, 'Legacy Shaders/Diffuse': 10,
}
SUPPORTED_KEYWORDS = {
    'DIRECTIONAL', 'LIGHTPROBE_SH', '_WORLDSPACE_ON', '_DIFUSE_ALPHA_ON_ON',
    '_WALLFADE_ON_ON', '_DESATURATION_ON', '_TOGGLEWALLFADE_ON',
    '_TOGGLEWALLFADEOFF_ON', '_ALPHATEST_ON', 'SHADOWS_DEPTH', 'SHADOWS_CUBE',
    'UNITY_PASS_SHADOWCASTER', '_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A',
}

def digest(data): return hashlib.sha256(data).hexdigest()

def constant_buffers(tail):
    """Decode bounded Unity 2021 constant-buffer descriptors, including byte offsets.

    The final group can be a texture table rather than a constant buffer. Stop at
    that explicit non-string record; never infer a property offset from a register.
    Payload/program digests still bind the complete descriptor tail.
    """
    if len(tail)<28: return []
    header=struct.unpack_from('<7I',tail); position=28; buffers=[]
    def string_at(at):
        length=struct.unpack_from('<I',tail,at)[0]
        assert 0<length<256 and at+4+length<=len(tail)
        value=tail[at+4:at+4+length].decode('ascii')
        assert all(32<=ord(c)<127 for c in value)
        return value,(at+4+length+3)&~3
    for index in range(header[2]):
        try: name,p=string_at(position)
        except (AssertionError,UnicodeError,struct.error):break
        size,count=struct.unpack_from('<II',tail,p);p+=8;variables=[]
        assert count<256
        for _ in range(count):
            variable,p=string_at(p)
            kind,rows,dimension,matrix,array,offset=struct.unpack_from('<6I',tail,p);p+=24
            variables.append({'name':variable,'byteOffset':offset,'kind':kind,'rows':rows,'dimension':dimension,'matrix':matrix,'array':array})
        structs=struct.unpack_from('<I',tail,p)[0];p+=4
        assert structs==0,'Nested native descriptors require independent review'
        buffers.append({'index':index,'name':name,'bytes':size,'variables':variables});position=p
    return buffers

def extract(root, output):
    output.mkdir(parents=True, exist_ok=True)
    paths = [root/'ressources/GH_Data/StreamingAssets/aa/StandaloneWindows64'/n for n in
             ['misc_high_shaders_assets_all.bundle', 'misc_shaders_assets_all.bundle',
              '7c79e4fec988dbd61eedc6188355414e_unitybuiltinshaders.bundle']]
    paths += [root/'ressources/GH_Data'/n for n in ['resources.assets', 'sharedassets6.assets', 'sharedassets9.assets',
              'Resources/unity_builtin_extra']]
    result = []
    for path in paths:
        if not path.exists(): continue
        source_hash = digest(path.read_bytes())
        for obj in UnityPy.load(str(path)).objects:
            if obj.type.name != 'Shader': continue
            shader = obj.read(); name = shader.m_ParsedForm.m_Name
            if name not in FAMILIES: continue
            tree = obj.read_typetree()['m_ParsedForm']
            identity = path.relative_to(root/'ressources/GH_Data').as_posix()+'::'+obj.assets_file.name+':'+str(obj.path_id)
            safe = name.replace('/', '_')+'-'+str(obj.path_id)
            record = {'identity': identity, 'family': name, 'route': FAMILIES[name],
                      'sourceSha256': source_hash, 'rawSha256': digest(obj.get_raw_data()),
                      'properties': tree['m_PropInfo']['m_Props'], 'programs': []}
            if not shader.compressedBlob:
                result.append(record); continue
            compressed = bytes(shader.compressedBlob)
            segments = [decompress_lz4(compressed[a:a+c],d) if c!=d else compressed[a:a+c]
                        for a,c,d in zip(shader.offsets[0],shader.compressedLengths[0],shader.decompressedLengths[0])]
            table = segments[0]
            for pass_index, native_pass in enumerate(shader.m_ParsedForm.m_SubShaders[0].m_Passes):
                tags = dict(native_pass.m_State.m_Tags.tags)
                caster = tags.get('LIGHTMODE', '').upper() == 'SHADOWCASTER'
                if pass_index != 0 and not caster: continue
                for stage in ['progVertex','progFragment']:
                    program = getattr(native_pass, stage)
                    for sub in program.m_SubPrograms:
                        keywords = [shader.m_ParsedForm.m_KeywordNames[k] for k in sub.m_KeywordIndices]
                        if ('DIRECTIONAL' not in keywords and not caster) or set(keywords)-SUPPORTED_KEYWORDS: continue
                        if sub.m_ShaderHardwareTier != 0: continue
                        offset, length, segment = struct.unpack_from('<III', table, 4+sub.m_BlobIndex*12)
                        data=segments[segment];reader = EndianBinaryReader(data, endian='<')
                        assert offset+length<=len(data), 'Compiled program must stay within its addressed segment'
                        reader.Position = offset; code = ShaderSubProgram(reader); end = reader.Position
                        compiled = bytes(code.m_ProgramCode); magic = compiled.find(b'DXBC')
                        assert magic >= 0, 'Native program must be Windows DXBC, not guessed API metadata'
                        total = struct.unpack_from('<I',compiled,magic+24)[0]
                        dxbc = compiled[magic:magic+total]; tail = data[end:offset+length]
                        tag = safe+'-'+stage+'-'+str(sub.m_BlobIndex)
                        (output/(tag+'.dxbc')).write_bytes(dxbc)
                        (output/(tag+'.tail')).write_bytes(tail)
                        record['programs'].append({'pass':pass_index,'stage':stage,'blob':sub.m_BlobIndex,
                            'keywords':keywords,'segment':segment,'offset':offset,'length':length,'tailOffset':end-offset,
                            'dxbcSha256':digest(dxbc),'tailSha256':digest(tail),'file':tag})
                        record['programs'][-1]['constantBuffers']=constant_buffers(tail)
            (output/(safe+'-parsed.json')).write_text(json.dumps(tree,indent=2)+'\n')
            result.append(record)
    (output/'native-programs.json').write_text(json.dumps(result,indent=2)+'\n')
    print('Extracted '+str(len(result))+' independently addressed native shader objects; '
          +str(sum(len(r['programs']) for r in result))+' bounded Windows programs')
    return result

if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root',type=Path,required=True)
    parser.add_argument('--output-dir',type=Path,required=True)
    args=parser.parse_args();extract(args.source_root.resolve(),args.output_dir.resolve())
