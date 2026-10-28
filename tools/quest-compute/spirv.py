"""Strict reflection of Unity's actual raw Vulkan compute programs (SPIR-V 1.0).

This does not compile, translate or infer an executable from source. Descriptor
coordinates come from the cooked SPIR-V, then match Unity's packed binding table.
"""
from __future__ import annotations
import struct
if __package__:
    from .native import ComputeRecoveryError
else:
    from native import ComputeRecoveryError

IMAGE_FORMATS = {1:'rgba32f',2:'rgba16f',3:'r32f',4:'rgba8',6:'rg32f',7:'rg16f',9:'r16f',13:'rg8',15:'r8'}


def string(words):
    raw = struct.pack('<' + 'I'*len(words), *words)
    return raw.split(b'\0',1)[0].decode('utf-8')


class Module:
    def __init__(self, code: bytes):
        if len(code) < 20 or len(code)%4:
            raise ComputeRecoveryError('Vulkan program has an incomplete SPIR-V extent.')
        words = struct.unpack('<'+'I'*(len(code)//4),code)
        if words[0] != 0x07230203 or words[1] != 0x10000 or words[4] != 0:
            raise ComputeRecoveryError('Vulkan compute requires actual little-endian SPIR-V 1.0.')
        self.instructions=[]; self.names={}; self.members={}; self.decorations={}; self.member_decorations={}; self.types={}; self.variables={}; self.constants={}; self.capabilities=[]; self.groups=None; self.entry=None
        i=5
        while i<len(words):
            size,op=words[i]>>16,words[i]&65535
            if size<1 or i+size>len(words):raise ComputeRecoveryError('Malformed SPIR-V instruction extent.')
            a=tuple(words[i+1:i+size]);self.instructions.append((op,a))
            if op==5:self.names[a[0]]=string(a[1:])
            elif op==6:self.members[(a[0],a[1])]=string(a[2:])
            elif op==71:self.decorations.setdefault(a[0],{})[a[1]]=a[2:]
            elif op==72:self.member_decorations.setdefault((a[0],a[1]),{})[a[2]]=a[3:]
            elif op==17:self.capabilities.append(a[0])
            elif op==15:
                if a[0]!=5 or self.entry is not None or string(a[2:])!='main':raise ComputeRecoveryError('Vulkan program is not a unique original compute main.')
                self.entry=a[1]
            elif op==16 and a[1]==17:
                if self.groups is not None:raise ComputeRecoveryError('Duplicate Vulkan LocalSize.')
                self.groups=list(a[2:]);self.group_entry=a[0]
            elif op in range(19,34):self.types[a[0]]=(op,a[1:])
            elif op==43:self.constants[a[1]]=a[2:]
            elif op==59:self.variables[a[1]]=(a[0],a[2])
            i+=size
        if self.entry is None or self.groups is None or self.group_entry!=self.entry:raise ComputeRecoveryError('Vulkan compute LocalSize missing.')
        if not set(self.capabilities)<= {1,49,50}:raise ComputeRecoveryError('Unaudited Vulkan compute capability.')

    def descriptors(self):
        rows=[]
        for identity,(pointer,storage) in self.variables.items():
            dec=self.decorations.get(identity,{})
            if 33 not in dec and 34 not in dec:continue
            if set((33,34))-dec.keys():raise ComputeRecoveryError('Incomplete Vulkan descriptor coordinates.')
            p,a=self.types[pointer]
            if p!=32 or a[0]!=storage:raise ComputeRecoveryError('Unknown Vulkan descriptor pointer.')
            target=a[1];op,t=self.types[target]
            row={'id':identity,'name':self.names.get(identity,''),'set':dec[34][0],'binding':dec[33][0]}
            if op==25:
                # sampledType, Dim, Depth, Arrayed, MS, Sampled, Format
                if t[1] not in (1,2) or t[2]!=0 or t[4]!=0 or t[5] not in (1,2):raise ComputeRecoveryError('Unknown Vulkan original image shape.')
                row.update(kind='sampledImage' if t[5]==1 else 'storageImage',dimension=5 if t[1]==1 and t[3]==1 else 2 if t[1]==1 else 3,
                           imageFormat=IMAGE_FORMATS.get(t[6]) if t[5]==2 else None)
                if t[5]==2 and row['imageFormat'] is None:raise ComputeRecoveryError('Unaudited Vulkan native image format.')
            elif op==26:row['kind']='sampler'
            elif op==30:
                deco=self.decorations.get(target,{})
                if 2 in deco:row.update(kind='uniformBuffer',members=self.struct_members(target))
                elif 3 in deco:
                    if len(t)!=1:raise ComputeRecoveryError('Unknown Vulkan structured block shape.')
                    array=t[0];array_op,array_t=self.types[array]
                    if array_op!=29:raise ComputeRecoveryError('Structured compute buffer is not a runtime array.')
                    stride=self.decorations.get(array,{}).get(6)
                    if stride is None:raise ComputeRecoveryError('Structured Vulkan native stride missing.')
                    offset=self.member_decorations.get((target,0),{}).get(35)
                    if offset!=(0,):raise ComputeRecoveryError('Structured native buffer data starts at a nonzero offset.')
                    row.update(kind='storageBuffer',strideBytes=stride[0])
                else:raise ComputeRecoveryError('Unknown Vulkan native descriptor block.')
            else:raise ComputeRecoveryError('Unknown Vulkan descriptor resource type.')
            rows.append(row)
        if len({(r['set'],r['binding']) for r in rows})!=len(rows):raise ComputeRecoveryError('Aliased Vulkan descriptor coordinates.')
        return rows

    def struct_members(self,target):
        rows=[]
        for n,typ in enumerate(self.types[target][1]):
            name=self.members.get((target,n));offset=self.member_decorations.get((target,n),{}).get(35)
            if name is None or offset is None:raise ComputeRecoveryError('Original Vulkan uniform name/offset missing.')
            rows.append({'name':name,'byteOffset':offset[0],**self.shape(typ)})
        return rows

    def shape(self,identity):
        op,a=self.types[identity]
        if op==22 and a==(32,):return {'type':0,'rows':1,'columns':1,'arraySize':0}
        if op==21 and a[0]==32:return {'type':1 if a[1] else 4,'rows':1,'columns':1,'arraySize':0}
        if op==23:return {**self.shape(a[0]),'columns':a[1]}
        if op==28:
            count=self.constants.get(a[1])
            if count is None or len(count)!=1:raise ComputeRecoveryError('Unknown native uniform array length.')
            return {**self.shape(a[0]),'arraySize':count[0]}
        raise ComputeRecoveryError('Unaudited Vulkan uniform value shape.')


def coordinate(binding):
    packed=binding['bindPoint']
    if packed<0 or packed>>24 not in (0,2):raise ComputeRecoveryError('Unknown Unity Vulkan packed descriptor binding.')
    return ((packed>>16)&255,packed&65535)


def audit(program, expected, constant_buffers):
    module=Module(bytes(program['code']))
    if module.groups!=expected['threadGroups']:raise ComputeRecoveryError('Actual Vulkan LocalSize differs from original DXBC.')
    descriptors=module.descriptors();lookup={(r['set'],r['binding']):r for r in descriptors};used=set()
    def bound(binding,kind):
        key=coordinate(binding);row=lookup.get(key)
        if row is None or row['kind']!=kind:raise ComputeRecoveryError('Cooked Vulkan native descriptor kind/coordinates differ: '+binding['name'])
        if key in used:raise ComputeRecoveryError('Duplicated Unity Vulkan descriptor binding.')
        used.add(key)
        return row
    outputs={r['name']:r for r in program['outBuffers']}
    if outputs.keys()!={r['name'] for r in expected['outputBindings']}:raise ComputeRecoveryError('Actual Vulkan output property closure differs.')
    output_rows=[]
    for binding in expected['outputBindings']:
        actual=outputs[binding['name']];storage=binding.get('imageStorage')
        if actual['texDimension']!=binding['dimension']:raise ComputeRecoveryError('Actual Vulkan original output dimension differs.')
        row=bound(actual,'storageImage' if storage else 'storageBuffer')
        if storage:
            if row['dimension']!=binding['dimension'] or row['imageFormat']!=storage['glslImageQualifier']:raise ComputeRecoveryError('Actual Vulkan storage image format differs from original allocation.')
        elif row['strideBytes']!=binding['strideBytes']:raise ComputeRecoveryError('Actual Vulkan structured stride differs from original.')
        output_rows.append({'property':binding['name'],**row,'imageStorage':storage})
    names=set()
    for binding in program['textures']:
        row=bound(binding,'sampledImage');names.add(binding['name'])
        if row['dimension']!=binding['texDimension']:raise ComputeRecoveryError('Actual Vulkan sampled dimension differs from binding.')
        if binding['samplerBindPoint']!=-1:
            key=coordinate({'bindPoint':binding['samplerBindPoint']})
            if key not in used:bound({'name':'sampler'+binding['name'],'bindPoint':binding['samplerBindPoint']},'sampler')
    for binding in program['inBuffers']:
        row=bound(binding,'storageBuffer');names.add(binding['name'])
        if row['strideBytes']!=4:raise ComputeRecoveryError('Original histogram input buffer requires uint stride4.')
    expected_names={r['name'] for r in expected['interface']['bindings'] if r['kind'] in ('texture','buffer')}
    if names!=expected_names:raise ComputeRecoveryError('Actual Vulkan original input property closure differs.')
    originals={f['name']:f for b in expected['interface']['buffers'] for f in b['fields']}
    uniform_rows=[]
    if len(program['cbs'])!=len(program['cbVariantIndices']):raise ComputeRecoveryError('Incomplete Vulkan uniform table indices.')
    for binding,index in zip(program['cbs'],program['cbVariantIndices']):
        if not isinstance(index,int) or not 0<=index<len(constant_buffers):raise ComputeRecoveryError('Vulkan uniform table index outside actual bank.')
        row=bound(binding,'uniformBuffer');cb=constant_buffers[index]
        params={p['name']:p for p in cb['params']}
        if {m['name'] for m in row['members']}!=params.keys():raise ComputeRecoveryError('Actual Vulkan uniform property closure differs from Unity table.')
        for member in row['members']:
            source=originals.get(member['name']);param=params[member['name']]
            if source is None:raise ComputeRecoveryError('Actual Vulkan uniform absent in original DXBC interface.')
            if member['byteOffset']!=source['byteOffset'] or member['byteOffset']!=param['offset'] or member['type']!=source['type'] or member['columns']!=source['columns'] or member['rows']!=source['rows'] or member['arraySize']!=source['arraySize']:
                raise ComputeRecoveryError('Actual Vulkan uniform scalar/layout differs from original: '+member['name'])
        uniform_rows.append(row)
    for sampler in program['builtinSamplers']:
        bound({'name':'builtinSampler','bindPoint':sampler['bindPoint']},'sampler')
    if used!=lookup.keys():raise ComputeRecoveryError('Unbound actual Vulkan descriptor remains: '+str([r for k,r in lookup.items() if k not in used]))
    counts={op:sum(1 for kind,a in module.instructions if kind==op) for op in (95,99,103,104,68,234,250)}
    bounds=expected.get('integerTextureLoadBounds',[])
    if sum(r['originalLoadCount'] for r in bounds)!=expected.get('nativeResourceInstructions',{}).get('textureLoad',0):raise ComputeRecoveryError('Vulkan original integer-read bounds census differs.')
    if any(r['mipLevel']!=0 or r['outOfBoundsResult']!=[0,0,0,0] for r in bounds):raise ComputeRecoveryError('Unaudited original Vulkan texture edge result.')
    if bounds and (counts[103]<len(bounds) or counts[95]<len(bounds) or not counts[250]):raise ComputeRecoveryError('Actual Vulkan guarded integer texture query/fetch/branch missing.')
    atomic=expected.get('structuredAtomicBounds',[])
    if atomic and (counts[68]!=1 or counts[234]!=1 or not counts[250]):raise ComputeRecoveryError('Actual Vulkan atomic count/query/branch missing.')
    return {'threadGroups':module.groups,'capabilities':module.capabilities,'descriptors':descriptors,'outputs':output_rows,'uniformBuffers':uniform_rows,
            'instructionCounts':{str(k):v for k,v in counts.items()},'integerTextureLoadBounds':bounds,'structuredAtomicBounds':atomic}
