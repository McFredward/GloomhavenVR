#!/usr/bin/env python3
"""Run event-invalidated UI inventories and original depth/mip consumers in real Unity."""
import hashlib,json,os,shutil,subprocess,tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def method(text,signature):
 start=text.index(signature);opening=text.index('{',start);depth=1;end=opening+1
 while depth:
  depth+=(text[end]=='{')-(text[end]=='}');end+=1
 return text[start:end]
def main():
 output=ROOT/'.planning/debug/ui-inventory-runtime';output.mkdir(parents=True,exist_ok=True)
 run=Path(tempfile.mkdtemp(prefix='run-',dir=output));source=ROOT/'src/GloomhavenVR/WorldUI'
 fixture=ROOT/'scripts/ui-inventory-runtime'
 inventory=(source/'Conversion/UiHierarchyInventory.cs').read_text();mip=(source/'Sharpness/PanelMipBake.cs').read_text()
 depth=(source/'Surfaces/TablePanelSurfaces.cs').read_text();veil=(source/'Conversion/CanvasConversion.9e.HiddenWindowVeil.cs').read_text()
 read='using UnityEngine;using System.Collections.Generic;using GloomhavenVR.Core;namespace GloomhavenVR.WorldUI {internal sealed partial class InitiativeTrackSurface {\n'+method(depth,'private void NormalizeDepth()')+'\n'+method(depth,'private void RestoreDepth()')+'\n}internal static partial class CanvasConversion {\n'+method(veil,'private static void RefreshVeilWindowInventory(')+'\n}}'
 boundary=(fixture/'Boundary.cs').read_text().replace('internal sealed class InitiativeTrackSurface {','internal sealed partial class InitiativeTrackSurface {')
 sources={'SharedWindowReads.cs':(source/'Conversion/CanvasConversion.9e.SharedWindowReads.cs').read_text(),'UiHierarchyInventory.cs':inventory,'PanelMipBake.cs':mip,'Reads.cs':read,'Boundary.cs':boundary,'Program.cs':(fixture/'Program.cs').read_text()}
 variants=[('production',None,None,None,''),
  ('stale-insertion','UiHierarchyInventory.cs','private void OnTransformChildrenChanged() => Notify(true);','private void OnTransformChildrenChanged() { }','inactive parent insertion invalidates immediately'),
  ('uncached-stable','UiHierarchyInventory.cs',' || !_dirty','', 'stable inventory must not rebuild'),
  ('stale-activation','UiHierarchyInventory.cs','private void OnEnable() => Notify(_children != transform.childCount);','private void OnEnable() { }','activation wakes maintenance cadence'),
  ('late-mip-arrival','PanelMipBake.cs','watch.Inventory.IsDirty || frame >= watch.NextRecaptureFrame','frame >= watch.NextRecaptureFrame','pooled sprite arrival precedes next periodic recapture'),
  ('lost-disabled-window','Reads.cs','target.GetComponentsInChildren(includeInactive: true, state.Windows);','state.Windows.AddRange(UIWindow.GetWindows());','disabled native windows retained beyond active registry')]
 (run/'source-hashes.json').write_text(json.dumps({name:hashlib.sha256(code.encode()).hexdigest()for name,code in sources.items()},indent=2))
 manifest={'result':str(run/'results.txt'),'cases':[]};dotnet=os.environ.get('DOTNET_EXE','/home/claw/.dotnet/dotnet')
 unity=Path(os.environ.get('UNITY_PATH','/home/claw/unity-2021.3.5/Editor/Unity'));managed=unity.parent/'Data/Managed'
 deps=[Path.home()/'.nuget/packages'/path for path in ['harmonyx/2.7.0/lib/net45/0Harmony.dll','monomod.runtimedetour/21.12.13.1/lib/net452/MonoMod.RuntimeDetour.dll','monomod.utils/21.12.13.1/lib/net452/MonoMod.Utils.dll','mono.cecil/0.11.4/lib/net40/Mono.Cecil.dll']]
 for name,file,old,new,expected in variants:
  case=run/name;generated=case/'production';generated.mkdir(parents=True)
  for path,code in sources.items():
   if path==file:
    if old not in code:raise RuntimeError('mutation anchor missing: '+old)
    code=code.replace(old,new)
   (generated/path).write_text(code)
  assembly='UiInventory_'+name.replace('-','_');project=case/'Inventory.csproj';shutil.copyfile(fixture/'Inventory.csproj',project)
  r=subprocess.run([dotnet,'build',str(project),'-c','Release','--nologo','--verbosity','quiet','-p:CaseName='+assembly,'-p:FixtureDir='+str(case/'empty'),'-p:ProductionDir='+str(generated),'-p:UnityManaged='+str(managed),'-p:GameManaged='+str(ROOT/'ressources/GH_Data/Managed'),'-p:HarmonyPath='+str(deps[0])],capture_output=True,text=True)
  (case/'build.log').write_text(r.stdout+r.stderr)
  if r.returncode:raise RuntimeError(r.stdout+r.stderr)
  manifest['cases'].append({'name':name,'dll':str(case/'bin/Release/netstandard2.1'/(assembly+'.dll')),'expected':expected})
 project=run/'unity'
 for folder in ['Assets/Editor','Assets/Plugins','Packages','ProjectSettings']:(project/folder).mkdir(parents=True)
 for entry in manifest['cases']:
  dest=project/'Assets/Plugins'/Path(entry['dll']).name;shutil.copyfile(entry['dll'],dest);entry['dll']=str(dest)
 for dll in deps:shutil.copyfile(dll,project/'Assets/Plugins'/dll.name)
 shutil.copyfile(fixture/'Editor/InventoryRunner.cs',project/'Assets/Editor/InventoryRunner.cs')
 (project/'Packages/manifest.json').write_text(json.dumps({'dependencies':{'com.unity.ugui':'1.0.0'}}));(project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
 path=run/'manifest.json';path.write_text(json.dumps(manifest,indent=2))
 r=subprocess.run(['xvfb-run','-a',str(unity),'-batchmode','-force-glcore','-projectPath',str(project),'-executeMethod','InventoryRunner.Start','-interactionManifest',str(path),'-logFile',str(run/'unity.log')],timeout=180)
 if (run/'results.txt').exists():print((run/'results.txt').read_text())
 print('Evidence: '+str(run));raise SystemExit(r.returncode)
if __name__=='__main__':main()
