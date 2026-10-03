#!/usr/bin/env python3
"""Source-linked reversible GPU-instancing ownership in Unity, with native variant provenance.

The native Windows shader variants are metadata evidence, not a GL rendered substitute.
A source GL fixture proves API state, separate culling/MPBs and pixels on actual Unity.
Actual native shader pixels, headset draw calls and FPS require hardware validation.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT=Path(__file__).resolve().parents[1]


def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--source-root',type=Path,default=ROOT)
    p.add_argument('--output-dir',type=Path,default=ROOT/'.planning/debug/structural-instancing-runtime')
    p.add_argument('--unity',type=Path,default=Path('/home/claw/unity-2021.3.5/Editor/Unity'))
    p.add_argument('--case',action='append',help='Partial focused variant list')
    args=p.parse_args()
    args.output_dir.mkdir(parents=True,exist_ok=True)
    run=Path(tempfile.mkdtemp(prefix='run-',dir=args.output_dir.resolve()))
    proof_script=r'''
import UnityPy,hashlib,json,sys
from pathlib import Path
base=Path(sys.argv[1]);result=[]
wanted={'Amp_Basic_N_MRAO','Amp_Low/Amp_Basic_N_MRAO_Low','Amp_Basic_WallFade','Amp_Low/Amp_Basic_WallFade_Low'}
for filename in ('misc_shaders_assets_all.bundle','misc_high_shaders_assets_all.bundle'):
 file=base/filename
 for obj in UnityPy.load(str(file)).objects:
  if obj.type.name!='Shader':continue
  form=obj.read_typetree()['m_ParsedForm'];name=form['m_Name']
  if name not in wanted:continue
  keyword=form['m_KeywordNames'].index('INSTANCING_ON');passes=[]
  for sub in form['m_SubShaders']:
   for entry in sub['m_Passes']:
    count=sum(keyword in prog['m_KeywordIndices'] for prog in entry['progVertex']['m_SubPrograms'])
    passes.append({'instancing':entry['m_HasInstancingVariant'],'instancedVertexPrograms':count})
  assert passes[0]['instancing'] and passes[0]['instancedVertexPrograms']>0,name+' has no retained native forward instancing'
  result.append({'shader':name,'bundle':filename,'bundleSHA256':hashlib.file_digest(file.open('rb'),'sha256').hexdigest(),'pathID':obj.path_id,'passes':passes})
assert {x['shader'] for x in result}==wanted,'Missing original shader variant proof'
Path(sys.argv[2]).write_text(json.dumps(result,indent=2)+'\n')
'''
    python=Path(os.environ.get('UNITYPY_PYTHON','/home/claw/unitypy-venv/bin/python'))
    if not python.is_file():p.error('UnityPy and original game shader bundles required; no silent skip')
    subprocess.run([str(python),'-c',proof_script,str(args.source_root/'ressources/GH_Data/StreamingAssets/aa/StandaloneWindows64'),str(run/'native-shader-provenance.json')],check=True)
    path=args.source_root/'src/GloomhavenVR/Core/Perf/ScenarioStructuralInstancing.cs'
    source=path.read_text();fixture=ROOT/'tests/structural-instancing-runtime'
    changes=[
      ('omit-enable','group.Material.enableInstancing = true;','group.Material.enableInstancing = false;','repeated exact native mesh/material enables original GPU instancing flag'),
      ('omit-actor-veto','node.GetComponent<ActorBehaviour>() != null','false','native interaction boundary retains rendering: Actor'),
      ('omit-restore','owner.Material.enableInstancing = owner.Original;','owner.Material.enableInstancing = true;','owned material flag restores immediately on option off'),
      ('fight-foreign','_foreign.Contains(group.Material)','false','foreign native flag changes are never fought or re-enabled'),
      ('lose-original-on','&& owner.Material.shader == owner.Shader && owner.Material.enableInstancing','&& owner.Material.shader == owner.Shader','already enabled native flag retains its exact original state'),
    ]
    variants=[('production',source,'')]
    for name,needle,replacement,expected in changes:
        if name=='lose-original-on':continue  # no owned write is made for native already-on flags
        assert source.count(needle)==1,'Mutation binding drift: '+name
        variants.append((name,source.replace(needle,replacement,1),expected))
    if args.case:
        unknown=set(args.case)-{name for name,_,_ in variants}
        if unknown:p.error('Unknown cases '+str(unknown))
        variants=[v for v in variants if v[0] in args.case]
    (run/'source-hashes.json').write_text(json.dumps({'root':str(args.source_root),'sha256':{str(path):hashlib.sha256(source.encode()).hexdigest()},'partial':bool(args.case),'limits':['Native original compiled shader metadata verified independently.','GL fixture proves real Unity API/render pixels; game classes at explicitly inert boundaries.','Native game shader pixels, actual eye draw calls and headset milliseconds unmeasured.']},indent=2)+'\n')
    manifest={'result':str(run/'results.txt'),'cases':[]}
    dotnet=shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    for name,value,expected in variants:
        build=run/name;production=build/'production';production.mkdir(parents=True)
        (production/'ScenarioStructuralInstancing.cs').write_text(value)
        project=build/'Instancing.csproj';shutil.copyfile(fixture/'Instancing.csproj',project)
        assembly='StructuralInstancing_'+name.replace('-','_')
        result=subprocess.run([dotnet,'build',str(project),'-c','Release','--nologo','--verbosity','quiet','-p:CaseName='+assembly,'-p:FixtureDir='+str(fixture),'-p:ProductionDir='+str(production),'-p:UnityManaged='+str(args.unity.parent/'Data/Managed')],capture_output=True,text=True)
        (build/'build.log').write_text(result.stdout+result.stderr)
        if result.returncode:raise SystemExit(result.stdout+result.stderr+'\nA compilation failure is not a passing causal control')
        manifest['cases'].append({'name':name,'dll':str(build/'bin/Release/netstandard2.1'/(assembly+'.dll')),'expected':expected})
    file=run/'manifest.json';file.write_text(json.dumps(manifest,indent=2))
    project=run/'unity';(project/'Assets/Editor').mkdir(parents=True);(project/'Packages').mkdir();(project/'ProjectSettings').mkdir()
    shutil.copyfile(fixture/'Editor/InstancingRunner.cs',project/'Assets/Editor/InstancingRunner.cs')
    shutil.copyfile(fixture/'NativeInstancing.shader',project/'Assets/NativeInstancing.shader')
    (project/'Packages/manifest.json').write_text('{"dependencies":{"com.unity.modules.physics":"1.0.0"}}\n')
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    command=[str(args.unity),'-batchmode','-force-glcore','-projectPath',str(project),'-executeMethod','InstancingRunner.Start','-instancingManifest',str(file),'-logFile',str(run/'unity.log')]
    if not os.environ.get('DISPLAY'):command=['xvfb-run','-a']+command
    try:result=subprocess.run(command,stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT,timeout=240)
    finally:
        for cache in ('Library','Temp'):shutil.rmtree(project/cache,ignore_errors=True)
    report=Path(manifest['result'])
    if report.is_file():print(report.read_text(),end='')
    (run/'unity-exit-code.txt').write_text(str(result.returncode)+'\n')
    if result.returncode or not report.is_file():raise SystemExit('FAIL: Unity run; see '+str(run/'unity.log'))
    print('PASS: '+str(len(variants))+' production/causal variants; evidence: '+str(run))

if __name__=='__main__':main()
