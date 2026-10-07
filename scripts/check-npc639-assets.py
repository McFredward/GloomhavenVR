#!/usr/bin/env python3
"""Resolve all895 cold original atlas members through production TownServiceAssets.

The generated test bank retains each original Sprite/Atlas/Texture object byte
and the native compressed stream. Only an AssetBundle outer container changes.
No game controllers, addressables, sprite reimport or atlas repack runs. The old
638 control captures and resolves with that old code, reproducing actual missing
Poison/Disarm originals. Unity playmode binding and full packed UV/mesh comparisons
prevent an editmode/rect-only surrogate from passing.
"""
import argparse,hashlib,json,os,shutil,subprocess,tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--source-root',type=Path,default=ROOT);p.add_argument('--output-dir',type=Path,default=ROOT/'.planning/debug/npc639/assets');p.add_argument('--controls',action='store_true');p.add_argument('--case',choices=['production','old638','missing-packed-census','clone-name','missing-native-uv']);a=p.parse_args();root=a.source_root.resolve();a.output_dir.mkdir(parents=True,exist_ok=True)
 run=Path(tempfile.mkdtemp(prefix='run-',dir=a.output_dir.resolve()));native=run/'native';fixture=root/'scripts/npc639-assets-runtime'
 py=os.environ.get('UNITYPY_PYTHON',str(Path.home()/'unitypy-venv/bin/python'))
 subprocess.run([py,str(fixture/'export-native.py'),str(root/'ressources/GH_Data'),str(native)],check=True)
 sources={path.name:path.read_text()for path in (root/'src/GloomhavenVR/Net/TownServices').glob('TownServiceAssets*.cs')}
 source_hash={k:hashlib.sha256(v.encode()).hexdigest()for k,v in sources.items()}
 variants=[('production',sources,'')]
 if a.controls:
  old=subprocess.run(['git','-C',str(root),'show','9870bd802:src/GloomhavenVR/Net/TownServices/TownServiceAssets.cs'],capture_output=True,text=True,check=True).stdout
  variants.append(('old638',{'TownServiceAssets.cs':old},'Original town-service asset is not loaded'))
  for name,before,after,expected in [
    ('missing-packed-census','foreach (SpriteAtlas atlas in Resources.FindObjectsOfTypeAll<SpriteAtlas>())','foreach (SpriteAtlas atlas in new SpriteAtlas[0])','Original town-service asset is not loaded'),
    ('clone-name','if (name.EndsWith("(Clone)", StringComparison.Ordinal))','if (name.StartsWith("\\0", StringComparison.Ordinal))','Original town-service asset is not loaded'),
    ('missing-native-uv','return BitConverter.ToString(value).Replace("-", string.Empty);','return "omitted";','exact original packed geometry and UV match')]:
   variant=dict(sources);s=variant['TownServiceAssets.PackedSprites.cs'];assert s.count(before)==1,(name,'source mutation drift');variant['TownServiceAssets.PackedSprites.cs']=s.replace(before,after,1);variants.append((name,variant,expected))
 if a.case:variants=[v for v in variants if v[0]==a.case]
 if not variants:raise SystemExit('--case control requires --controls')
 unity=Path(os.environ.get('UNITY_PATH','/home/claw/unity-2021.3.5/Editor/Unity'));dotnet=shutil.which('dotnet')or str(Path.home()/'.dotnet/dotnet');project=run/'unity';(project/'Assets/Editor').mkdir(parents=True);(project/'Packages').mkdir();(project/'ProjectSettings').mkdir()
 (project/'resources.assets.resS').symlink_to(native/'resources.assets.resS')
 (project/'Packages/manifest.json').write_text('{"dependencies":{"com.unity.textmeshpro":"3.0.6","com.unity.ugui":"1.0.0"}}\n');(project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n');shutil.copyfile(fixture/'AssetsRunner.cs.txt',project/'Assets/Editor/AssetsRunner.cs')
 results=[]
 for name,source,expected in variants:
  case=run/name;prod=case/'production';prod.mkdir(parents=True)
  for f,text in source.items():(prod/f).write_text(text)
  shutil.copyfile(fixture/'Assets.csproj',case/'Assets.csproj');assembly='Assets639_'+name.replace('-','_')
  command=[dotnet,'build',str(case/'Assets.csproj'),'-c','Release','--nologo','--verbosity','quiet','-p:CaseName='+assembly,'-p:FixtureDir='+str(fixture),'-p:ProductionDir='+str(prod),'-p:UnityManaged='+str(unity.parent/'Data/Managed'),'-p:UnityTmp='+str(root/'ressources/GH_Data/Managed/Unity.TextMeshPro.dll')]
  built=subprocess.run(command,capture_output=True,text=True);(case/'build.log').write_text(built.stdout+built.stderr)
  if built.returncode:raise SystemExit('Build failed '+name+'\n'+built.stdout+built.stderr)
  command=['xvfb-run','-a',str(unity),'-batchmode','-force-glcore','-projectPath',str(project),'-executeMethod','AssetsRunner.Start','-assetsDll',str(case/'bin/Release/netstandard2.1'/(assembly+'.dll')),'-assetsBank',str(native/'original-battle-atlas.bundle'),'-assetsOutput',str(case/'assertions.txt'),'-logFile',str(case/'unity.log')]
  executed=subprocess.run(command,stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT,timeout=180);log=(case/'unity.log').read_text();passed=executed.returncode!=0 and expected in log if expected else executed.returncode==0 and (case/'assertions.txt').exists()
  result={'name':name,'passed':passed,'exit':executed.returncode,'expected':expected,'assertions':(case/'assertions.txt').read_text()if (case/'assertions.txt').exists()else None};results.append(result);print(('PASS'if passed else'FAIL')+' '+name+': '+str(result['assertions']or expected),flush=True)
  if not passed:raise SystemExit('Runtime failed '+name+'; see '+str(case/'unity.log'))
 (run/'results.json').write_text(json.dumps({'passed':True,'source':str(root),'source_sha256':source_hash,'native_provenance':str(native/'provenance.json'),'cases':results,'scope':'Real original895-member atlas cold resolution; actual mesh/UV/clone lifetime. Existing mip/bundle shader adapters are excluded; no headset/network claim.'},indent=2)+'\n');print('Evidence: '+str(run),flush=True)
if __name__=='__main__':main()
