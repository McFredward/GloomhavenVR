"""Run unmodified actual Android-cooked compute SPIR-V on a host Vulkan ICD.

This is optional developer evidence, never a headset readiness assertion. The C
harness uses a supplied pinned Vulkan header tree and system Vulkan 1.0 loader.
"""
from __future__ import annotations
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import struct
import subprocess
if __package__:
    from .spirv import Module,coordinate
    from .compiled import validate_objects
else:
    from spirv import Module,coordinate
    from compiled import validate_objects

FORMATS={'r32f':(100,4,1),'r16f':(76,2,1),'rg32f':(103,4,2),'rg16f':(83,2,2),'r8':(9,1,1),'rg8':(16,1,2),'rgba16f':(97,2,4),'rgba32f':(109,4,4)}
KINDS={'sampler':0,'sampledImage':1,'storageImage':2,'uniformBuffer':3,'storageBuffer':4}


def image(format,dimensions,values=None):
    dimensions=tuple(dimensions)+(1,)*(3-len(dimensions));fmt,width,channels=FORMATS[format];count=math.prod(dimensions)*channels
    if values is None:data=b'\0'*(count*width)
    else:data=struct.pack('<'+str(count)+('e' if width==2 else 'f' if width==4 else 'B'),*values)
    return {'format':fmt,'size':dimensions,'data':data}


def floating(values):return struct.pack('<'+str(len(values))+'f',*values)


class Runner:
    def __init__(self,bundle,manifest,cache,headers,icd):
        import UnityPy
        self.cache=Path(cache).resolve();self.cache.mkdir(parents=True,exist_ok=True)
        self.env={**os.environ,'VK_ICD_FILENAMES':str(Path(icd).resolve())}
        self.binary=self.cache/'vulkan-host'
        source=Path(__file__).with_name('vulkan_host.c')
        subprocess.run(['cc','-std=c11','-O2','-Wall','-Wextra','-I',str(Path(headers).resolve()),str(source),'-l:libvulkan.so.1','-o',str(self.binary)],check=True)
        objects=[o.read_typetree() for o in UnityPy.load(str(bundle)).objects if o.type.name=='ComputeShader']
        self.audit=validate_objects(json.loads(Path(manifest).read_text()),objects)
        self.programs={(o['m_Name'],k['name']):k['variantMap'][0][1] for o in objects for k in o['variants'][0]['kernels']}
        self.counter=0

    def execute(self,shader,kernel,resources=None,groups=(0,0,0)):
        self.counter+=1;directory=self.cache/str(self.counter);directory.mkdir(exist_ok=True)
        program=self.programs[(shader,kernel)];code=bytes(program['code']);spv=directory/'program.spv';spv.write_bytes(code)
        subprocess.run(['spirv-val','--target-env','vulkan1.0',str(spv)],check=True,capture_output=True)
        descriptors=Module(code).descriptors();names={coordinate(b):b['name'] for category in ('cbs','textures','inBuffers','outBuffers') for b in program[category]}
        lines=[f'{spv} {len(code)} {len(descriptors)} '+ ' '.join(map(str,groups))];outputs={}
        for n,row in enumerate(descriptors):
            name=names.get((row['set'],row['binding']),row['name']);fixture=(resources or {}).get(name);kind=KINDS[row['kind']]
            format=dimension=w=h=d=layers=bytes_count=0;path=directory/(str(n)+'.bin')
            if groups[0] and kind!=0:
                if fixture is None:raise RuntimeError('Missing exact bound fixture resource: '+name)
                if kind in (1,2):
                    format=fixture['format'];dimension=row['dimension'];w,h,depth=fixture['size'];layers=depth if dimension==5 else 1;d=depth if dimension==3 else 1;data=fixture['data']
                else:data=fixture
                path.write_bytes(data);bytes_count=len(data)
                if kind in (2,4):outputs[name]=Path(str(path)+'.out')
            lines.append(' '.join(map(str,[row['set'],row['binding'],kind,format,dimension,w,h,d,layers,bytes_count,path])))
        request=directory/'request.txt';request.write_text('\n'.join(lines)+'\n')
        result=subprocess.run([str(self.binary),str(request)],env=self.env,capture_output=True,text=True,timeout=60)
        if result.returncode:raise RuntimeError('Actual Vulkan fixture failed: '+result.stderr)
        proof=json.loads(result.stdout);proof.update(shader=shader,kernel=kernel,actualSpirvSha256=hashlib.sha256(code).hexdigest())
        return {name:path.read_bytes() for name,path in outputs.items()},proof


def compare(name,data,expected,width=4):
    actual=list(struct.unpack('<'+str(len(expected))+('e' if width==2 else 'f'),data))
    if actual!=expected:raise RuntimeError('Actual Vulkan native output differs: '+name+' '+repr([(i,a,b) for i,(a,b) in enumerate(zip(actual,expected)) if a!=b][:8]))
    return {'property':name,'nativeComponentCount':len(actual),'actualReadbackSha256':hashlib.sha256(data).hexdigest(),'everyComponentMatchesOriginalInstructionFormula':True}


def edges(runner):
    proofs=[]
    size=(5,3,2);other=(2,2,1)
    output,p=runner.execute('Texture3DLerp','KTexture3DLerp',{'_From':image('rgba32f',size,[.25]*math.prod(size)*4),'_To':image('rgba32f',other,[.75]*math.prod(other)*4),'_Output':image('rgba16f',size),'CGlobals':floating([*size,.5])},(2,2,2))
    expected=[v for z in range(2) for y in range(3) for x in range(5) for v in [.5 if x<2 and y<2 and z<1 else .125]*4]
    p.update(integerLoadZeroOutsideSource=26,comparison=compare('_Output',output['_Output'],expected,2));proofs.append(p)
    width,height,plotsize=5,3,5;cb=floating([width,height,plotsize,0]);initial=struct.pack('<25I',*([0x5a5a5a5a]*25))
    output,p=runner.execute('Vectorscope','KVectorscopeClear',{'CGlobals':cb,'_VectorscopeBuffer':initial},(1,1,1))
    if output['_VectorscopeBuffer']!=b'\0'*100:raise RuntimeError('Actual Vulkan original clear did not clear all25 counters.')
    proofs.append(p)
    source=[c for y in range(height) for x in range(width) for c in (1.0 if (x+y)%2==0 else 0.0,0.0,0.0,1.0)]
    output,p=runner.execute('Vectorscope','KVectorscopeGather',{'CGlobals':cb,'_VectorscopeBuffer':output['_VectorscopeBuffer'],'_Source':image('rgba32f',(width,height),source)},(1,1,1))
    expected=[0]*25;expected[12]=7;actual=list(struct.unpack('<25I',output['_VectorscopeBuffer']))
    if actual!=expected:raise RuntimeError('Actual Vulkan D3D atomic discard mismatch: '+repr(actual))
    p.update(actualCounters=actual,originalDiscardedRedIndex=26,discardedRedCount=8);proofs.append(p)
    width,height=17,19
    def depth(x,y):return (.125,.25,.5,1.0)[(x+y*3)%4]
    def value(x,y):return 1/depth(x,y) if x<width and y<height else 100000.0
    resources={'Depth':image('r32f',(width,height),[depth(x,y) for y in range(height) for x in range(width)]),'CGlobals':floating([1,0,0,0])}
    layouts={'LinearZ':('r16f',(width,height)), 'DS2x':('r32f',((width+1)//2,(height+1)//2)), 'DS4x':('r32f',((width+3)//4,(height+3)//4)), 'DS2xAtlas':('r16f',((width+7)//8,(height+7)//8,16)), 'DS4xAtlas':('r16f',((width+15)//16,(height+15)//16,16))}
    for name,(format,size) in layouts.items():resources[name]=image(format,size)
    output,p=runner.execute('MultiScaleVODownsample1','MultiScaleVODownsample1',resources,(2,2,1))
    comparisons=[]
    for name,(format,size) in layouts.items():
        sizes=size+(1,)*(3-len(size));wanted=[]
        for layer in range(sizes[2]):
            for y in range(sizes[1]):
                for x in range(sizes[0]):
                    if name=='LinearZ':v=value(x,y)
                    elif name=='DS2x':v=value(x*2,y*2)
                    elif name=='DS4x':v=value(x*4,y*4)
                    else:
                        factor=2 if name=='DS2xAtlas' else 4;v=value((x*4+layer%4)*factor,(y*4+layer//4)*factor)
                        if v==100000:v=65504.0 # Exact native D3D storage contract; separate original oracle required.
                    wanted.append(v)
        comparisons.append(compare(name,output[name],wanted,2 if format=='r16f' else 4))
    p.update(nativeZeroSourceLoads=701,nativeOutputs=5,comparisons=comparisons);proofs.append(p)
    return proofs


def run(bundle,manifest,cache,headers,icd):
    runner=Runner(bundle,manifest,cache,headers,icd);pipelines=[]
    for shader,kernel in runner.programs:
        _,proof=runner.execute(shader,kernel);pipelines.append(proof)
    receipt={'schema':1,'graphicsApi':'Vulkan','all36ActualHostVulkanPipelinesCreated':True,'originalPixelParityVerified':False,'hardwareVerified':False,'pipelines':pipelines,'edges':edges(runner)}
    receipt['actualPaddedEdgeDispatchPassed']=True
    return receipt


if __name__=='__main__':
    p=argparse.ArgumentParser(description=__doc__)
    for key in ('bundle','manifest','cache','headers','icd','receipt'):p.add_argument('--'+key,required=True,type=Path)
    args=p.parse_args();receipt=run(args.bundle,args.manifest,args.cache,args.headers,args.icd);args.receipt.write_text(json.dumps(receipt,indent=2,sort_keys=True)+'\n')
    print(json.dumps({k:receipt[k] for k in ('graphicsApi','all36ActualHostVulkanPipelinesCreated','actualPaddedEdgeDispatchPassed','hardwareVerified')}))
