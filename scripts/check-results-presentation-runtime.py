#!/usr/bin/env python3
"""Run original results render authoring through production snapshot/codec/inert playback.
Native controllers/bootstrap and animation clocks are explicit ports. Original static
rects, sprites/fonts and actual Unity final pixels are compared independently.
"""
import argparse,hashlib,importlib.util,json,os,re,shutil,signal,subprocess,tempfile,time
from pathlib import Path
from town_run_retention import cleanup_completed_run
ROOT=Path(__file__).resolve().parents[1]
def method(s,k):
 a=s.index(k);p=s.index('{',a)+1;d=1
 while d:d+=(s[p]=='{')-(s[p]=='}');p+=1
 return s[a:p]
def command(cmd,log,timeout=240):
 with log.open('w') as out:
  p=subprocess.Popen(cmd,stdout=out,stderr=subprocess.STDOUT,start_new_session=True)
  try:return p.wait(timeout=timeout)
  finally:
   try:os.killpg(p.pid,signal.SIGKILL)
   except ProcessLookupError:pass
   p.wait()
def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--case',action='append');parser.add_argument('--footer-only',action='store_true');parser.add_argument('--source-root',type=Path,default=ROOT);parser.add_argument('--bar-source-root',type=Path,default=ROOT);parser.add_argument('--codec-source-root',type=Path,default=ROOT);args=parser.parse_args()
 started=time.monotonic()
 if args.footer_only:os.environ['RESULTS_FOOTER_ONLY']='1'
 out=ROOT/'.planning/debug/results-presentation-runtime';out.mkdir(parents=True,exist_ok=True);folder=Path(tempfile.mkdtemp(prefix='run-',dir=out));print('Evidence: '+str(folder),flush=True);status=1;log=folder/'completion.log';log.write_text(__doc__+'\nEvidence: '+str(folder)+'\n')
 fixture=ROOT/'scripts/results-presentation-runtime';managed=ROOT/'ressources/GH_Data/Managed';unity=Path('/home/claw/unity-2021.3.5/Editor/Unity');base=args.source_root/'src/GloomhavenVR';pins=[]
 try:
  inputs=folder/'inputs';inputs.mkdir();assert command([str(Path.home()/'unitypy-venv/bin/python'),str(fixture/'export-native.py'),str(ROOT),str(inputs)],folder/'export.log')==0,'Original export failed'
  paths=[base/'Net/ScenarioResultsPresentation.cs',base/'WorldUI/Modal/ScenarioResultsNative.cs',base/'WorldUI/Modal/ScenarioResultsVisualLease.cs',base/'WorldUI/Composites/ScenarioResultsReadyPark.cs',base/'WorldUI/Composites/MovedSubtreeLayers.cs']+[base/'Net/TownServices'/name for name in ['TownServiceBinding.cs','TownServiceMaterial.cs','TownServiceFlameClock.cs','TownServiceNeutralize.cs','TownServiceAssets.cs','TownServiceAssets.PackedSprites.cs']]
  paths.append(base/'Net/ScenarioResultsOriginalTextures.cs')
  sources={p.name:p.read_text() for p in paths};pins+=paths
  expected_keys=[json.loads(line.strip().rstrip(',')) for line in sources['ScenarioResultsOriginalTextures.cs'].splitlines() if line.strip().startswith('"texture|')]
  sources['CatalogExpectations.cs']='using System;using System.Collections.Generic;internal static class CatalogExpectations {private static readonly HashSet<string> Keys=new(StringComparer.Ordinal){'+','.join(json.dumps(key) for key in expected_keys)+'};internal static bool Contains(string key)=>Keys.Contains(key);}'
  sources['TownServiceBinding.cs']=sources['TownServiceBinding.cs'].replace('    internal void Apply(TownServiceFrame frame, TownServiceAssets assets)\n    {','    internal void Apply(TownServiceFrame frame, TownServiceAssets assets)\n    {\n        ResultsApplyFault.BeforeApply(Root);')
  sources['ScenarioResultsPresentation.cs']=sources['ScenarioResultsPresentation.cs'].replace('Time.unscaledTime','ResultsClock.Now').replace('Time.frameCount','ResultsClock.Frame')
  dto=args.codec_source_root/'src/GloomhavenVR/Net/ScenarioResultsState.cs'
  sources['ScenarioResultsState.cs']=dto.read_text() if dto.exists() else subprocess.check_output(['git','show','e08f04ebb:src/GloomhavenVR/Net/ScenarioResultsState.cs'],cwd=ROOT,text=True)
  delta=args.codec_source_root/'src/GloomhavenVR/Net/ScenarioResultsDelta.cs';sources[delta.name]=delta.read_text();sources['TownServiceDelta.cs']=(base/'Net/TownServices/TownServiceDelta.cs').read_text();pins += [dto,delta,base/'Net/TownServices/TownServiceDelta.cs'];
  frame=base/'Net/TownServices/TownServiceFrame.cs';s=frame.read_text();sources['Frame.cs']='#pragma warning disable CS0649\nusing System;using System.Collections.Generic;namespace GloomhavenVR.Net.TownServices {'+method(s,'internal sealed class TownServiceFrame')+'}' ;sources['Values.cs']='using System;using System.Collections.Generic; namespace GloomhavenVR.Net.TownServices {'+'\n'.join(method(s,k) for k in ['internal sealed class TownServiceNode','internal sealed class TownServiceValue\n','internal sealed class TownServiceValueComparer','internal static class TownServiceProperty'])+'\n}'
  native=ROOT/'decompiled/GH.Runtime/UINewAdventureResultsManager.cs';sources['NativeReady.cs']='using System;using FFSNet;using GLOOM;using UnityEngine;using UnityEngine.Events; public partial class UINewAdventureResultsManager {\n'+method(native.read_text(),'private void ShowReadyUpToggles(string buttonText, Action callback, bool autoReadyUp')+'\n}'
  toggle=native.parent/'UIReadyToggle.cs';sources['NativeReady.cs']+='\npublic partial class UIReadyToggle {\n'+'\n'.join(method(toggle.read_text(),k) for k in ['public bool ShouldBeVisible','private void OnReadiedPlayersChanged(','private void UpdateVisiblity('])+'\n}'
  options=native.parent;sources['NativeControls.cs']='using System;using UnityEngine;using UnityEngine.EventSystems;public abstract partial class UIResultsOption {\n'+'\n'.join(method((options/'UIResultsOption.cs').read_text(),k) for k in ['public virtual void Register(','protected void InvokeAction('])+'\n}public partial class UIResultsButtonOption {\n'+method((options/'UIResultsButtonOption.cs').read_text(),'public override void Register(')+'\n}'
  for component in ['ExtendedButton','ExtendedToggle']:sources['NativeControls.cs']+='\npublic partial class '+component+'{\n'+method((options/(component+'.cs')).read_text(),'public override void OnPointerClick(')+'\n}'
  bridge=base/'WorldUI/Modal/SharedWindowScrollBridge.cs';sources['ResultsNestedLease.cs']='using System;using System.Collections.Generic;using TMPro;using UnityEngine;using UnityEngine.UI;using UnityEngine.EventSystems;namespace GloomhavenVR.WorldUI {'+method(bridge.read_text(),'internal sealed class SharedWindowScrollCanvasLease')+method(bridge.read_text(),'internal sealed class SharedResultsHeaderGraphic')+'}';pins+=[toggle,bridge,*[options/(n+'.cs') for n in ['UIResultsOption','UIResultsButtonOption','ExtendedButton','ExtendedToggle']]]
  bar=args.bar_source_root/'src/GloomhavenVR/WorldUI/Grab/GrabbableModal.cs';sources['ActualResultsBar.cs']='using UnityEngine;using GloomhavenVR.Core;namespace GloomhavenVR.WorldUI {internal sealed partial class GrabbableModal {'+method(bar.read_text(),'    private void SyncBar(')+'}}';pins.append(bar)
  for rel in ['WorldUI/Grab/GrabBarLayout.cs','WorldUI/Grab/BarSizeSettle.cs','WorldUI/Grab/GrabBarTween.cs','Core/GrabBar.cs','Core/GrabBarVisual.cs','Core/GrabBarTexture.cs','Core/EmbeddedTexture.cs']:
   path=base/rel;sources[path.name]=path.read_text();pins.append(path)
  lifecycle=ROOT/'scripts/results-window-runtime/Boundary.cs';lifecycle_text=lifecycle.read_text().replace('internal static class WorldUIConfig','internal static partial class WorldUIConfig');pins.append(lifecycle)
  roles=json.loads((inputs/'roles.json').read_text());sources['NativePaths.cs']='internal static class NativePaths {'+'\n'.join('internal const string '+key+'='+json.dumps(roles['resultsPaths'][str(roles[key])])+';' for key in ['header','resultsPanel','statsPanel','buttonsContainer'])+'}'
  cases=[('production',None,None,None,None),
   ('old-native-image-import','Program.cs','if(c.HasAttribute("nativeMaterial"))image.material=NativeMaterials.Load(c.GetAttribute("nativeMaterial"));','','original custom image materials survive native results and ready import'),
   ('no-inactive-preparation','ScenarioResultsPresentation.cs','''            foreach (ScenarioResultsPart part in state.Parts)
            {
                Copy copy = copies[part.Id];
                copy.Binding.Apply(Frame(part), _assets);
                RootPose(copy.Binding.Root, part.Nodes[0]);
            }
''','','post-validation widget failure keeps the previous complete final pixels'),
   ('obsolete-author-close-lease','ScenarioResultsPresentation.cs','(_closedAuthor != state.Author || _closedKey == state.Key)','(_closedAuthor == state.Author && _closedKey == state.Key)','a newly elected content author replaces an obsolete accepted-close lease without native reopening'),
   ('early-close-restores-native','ScenarioResultsPresentation.cs','        BeginClosedLease(state.Author, state.Key);','','accepted public close suppresses private native results and wait-help until local closure'),
   ('missing-row-prefab','ScenarioResultsPresentation.cs','row = manager != null ? (field?.GetValue(manager) as Component)?.transform : null;','row = null;','a whole native picture becomes visible atomically in the converted owner'),
   ('mutable-capture','ScenarioResultsPresentation.cs','(TownServiceNode[])sampled.Clone()','sampled','published prior node arrays remain immutable across native sampler reuse'),
   ('root-pose-skipped','ScenarioResultsPresentation.cs','copy.Binding.Apply(frame, _assets); RootPose(copy.Binding.Root, nodes[0]);','copy.Binding.Apply(frame, _assets);','native transform animation advances smoothly between complete network samples'),
   ('controllers-live','ScenarioResultsPresentation.cs','                TownServiceNeutralize.Apply(clone);','','observer clones contain no native gameplay or readiness controller'),
   ('future-opening-discarded','ScenarioResultsPresentation.cs','if (_pending != null && _pending.Author == author && _pending.Key == key) _pending = null;','if (_pending != null) _pending = null;','early future opening survives native retirement of the previous results'),
   ('no-smooth-output','ScenarioResultsPresentation.cs','float amount = _blendDuration <= 0f ? 1f : Mathf.Clamp01((ResultsClock.Now - _receivedAt) / _blendDuration);','float amount = 1f;','native transform animation advances smoothly between complete network samples'),
   ('no-atomic-retention','ScenarioResultsPresentation.cs','                    // A late original/asset never replaces a valid picture with a partial one.','                    RetirePicture();','a failed complete candidate retains the entire previously valid public picture')]
  cases.append(('results-native-catalog-disabled','ScenarioResultsPresentation.cs','private readonly TownServiceAssets _assets = new(ScenarioResultsOriginalTextures.Contains);','private readonly TownServiceAssets _assets = new();','runtime GUI noise dependency does not freeze the persistent observer after Victory; failure: Ambiguous native town-service asset: texture|noise_01|256|256|3|9'))
  cases.append(('results-original-root-alias-disabled','ScenarioResultsPresentation.cs','private void RegisterRootImageSprite(Transform root)\n    {','private void RegisterRootImageSprite(Transform root)\n    {\n        if (bool.Parse("true")) return;','cold Results observer retains its exact native root backdrop despite conflicting original discovery'))
  cases.append(('results-late-root-refresh-disabled','ScenarioResultsPresentation.cs','        RegisterRootImageSprite(panel.Target);','','late original root sprite resolves on the existing observer without a structural rebuild'))
  cases.append(('results-native-catalog-admits-unknown','ScenarioResultsOriginalTextures.cs','=> Keys.Contains(descriptorKey);','=> Keys.Contains(descriptorKey) || descriptorKey.StartsWith("texture|", StringComparison.Ordinal);','Results-only catalog keeps conflicting and mutable original groups excluded'))
  cases.append(('results-bar-uses-private-ink','ActualResultsBar.cs','bool inkUsable = !resultsFrame && _inkValid && unit > 1e-9f;','bool inkUsable = _inkValid && unit > 1e-9f;','Results author and observer bars ignore divergent private ink width centre and lower extent'))
  cases.append(('missing-public-continue-surface','ScenarioResultsPresentation.cs','        AttachContinuationInputs(state);','','the visible common original Continue carries its own metadata-only input surface'))
  cases.append(('old-fitted-footer','ScenarioResultsReadyPark.cs','        Footer.Tick(p, control);','','the complete final camera image really contains the drawn Results bar'))
  cases.append(('footer-mount-lost','ScenarioResultsPresentation.cs','if (!source.Binding.Root.IsChildOf(parent)) nodes[0] = RelativeRoot(nodes[0], source.Binding.Root, parent);','','initial complete common Results pixels agree before a second wrapper exists'))
  cases.append(('footer-sibling-lost','ScenarioResultsPresentation.cs','copy.Values[TownServiceProperty.Sibling] = new TownServiceValue\n                { Numbers = new[] { (float)parent.childCount }, Text = sibling.Text };','copy.Values[TownServiceProperty.Sibling] = sibling;','initial complete common Results pixels agree before a second wrapper exists'))
  cases.append(('footer-takeover-receipt','ScenarioResultsPresentation.cs','        ScenarioResultsReadyPark.BeforeCommonPicture(panel);','','common picture takeover restores the pre-footer fit receipt'))
  cases.append(('footer-overwrites-successor','ScenarioResultsReadyPark.cs','bool ownsFrame = ReferenceEquals(_panel.FitContentRoot, _fitRoot)','bool ownsFrame = bool.Parse("true") || ReferenceEquals(_panel.FitContentRoot, _fitRoot)','footer retirement cannot overwrite a successor frame fit root or target pose'))
  if args.footer_only:cases=[x for x in cases if x[0] in {'production','old-fitted-footer','footer-mount-lost','footer-sibling-lost','footer-takeover-receipt','footer-overwrites-successor'}]
  if args.case:assert set(args.case)<={x[0] for x in cases};cases=[x for x in cases if x[0] in args.case]
  project=folder/'unity'
  for d in ['Assets/Editor','Assets/Plugins','Assets/Resources/OriginalShaders','Packages','ProjectSettings']:(project/d).mkdir(parents=True,exist_ok=True)
  manifest={'result':str(folder/'results.txt'),'evidence':str(folder/'captures'),'cases':[]}
  for name,file,before,after,expected in cases:
   case=folder/name;prod=case/'production';prod.mkdir(parents=True)
   for filename,text in {**sources, 'Program.cs':(fixture/'Program.cs').read_text()}.items():
    if filename==file:assert text.count(before)==1;text=text.replace(before,after)
    (prod/filename).write_text(text)
   (case/'LifecycleBoundary.cs').write_text(lifecycle_text)
   csproj=case/'Presentation.csproj';shutil.copy2(fixture/'Presentation.csproj',csproj);assembly='ResultsPresentation_'+name.replace('-','_')
   code=command([str(Path.home()/'.dotnet/dotnet'),'build',str(csproj),'-c','Release','--nologo','--verbosity','quiet','-p:UseSharedCompilation=false','-nodeReuse:false','-p:CaseName='+assembly,'-p:FixtureDir='+str(fixture),'-p:ProductionDir='+str(prod),'-p:LifecycleBoundary='+str(case/'LifecycleBoundary.cs'),'-p:UnityManaged='+str(unity.parent/'Data/Managed'),'-p:GameManaged='+str(managed)],case/'build.log')
   assert code==0,(case/'build.log').read_text()
   dll=case/'bin/Release/netstandard2.1'/(assembly+'.dll');target=project/'Assets/Plugins'/dll.name;shutil.copy2(dll,target);manifest['cases'].append({'name':name,'dll':str(target),'expected':expected or ''})
  spec=importlib.util.spec_from_file_location('native_material_ports',fixture/'native-material-ports.py');ports=importlib.util.module_from_spec(spec);spec.loader.exec_module(ports);ports.write_ports(inputs,project/'Assets/Resources/OriginalShaders')
  runner=ROOT/'scripts/multiplayer-progression-runtime/Editor/ProgressionRunner.cs'
  runner_text=runner.read_text();cleanup='foreach (var obj in UnityEngine.Object.FindObjectsOfType<GameObject>()) UnityEngine.Object.DestroyImmediate(obj);';assert runner_text.count(cleanup)==1
  (project/'Assets/Editor/ProgressionRunner.cs').write_text(runner_text.replace(cleanup,cleanup+'\n                    Assembly.LoadFile(entry.dll).GetType("ProgressionTests").GetMethod("Cleanup").Invoke(null, null);'))
  for file in ['UnityEngine.UI.dll','Unity.TextMeshPro.dll']:shutil.copy2(managed/file,project/'Assets/Plugins'/file)
  shaders=ROOT/'unity/GloomhavenVR.Assets/Assets/Bundle/HealthBars';source=(shaders/'ActorBarSDF.shader').read_text().replace('Shader "GloomhavenVR/ActorBarSDF"','Shader "TextMeshPro/Distance Field"',1);source=source.replace('\n\t\t\t#include "ActorBarForeground.cginc"','',1);source=re.sub(r'\n\t\t\tGHVRActorBarForegroundClip\([^;]*\);','',source);source=re.sub(r'\n\t\t\tfloat4 ghvrScreenPosition : TEXCOORD7;','',source);source=re.sub(r'\n\t\t\tOUT.ghvrScreenPosition = ComputeScreenPos\(OUT.vertex\);','',source);source=source.replace('\n\t\t\t\tComputeScreenPos(vPosition),','');source=source.replace('Fallback "GloomhavenVR/ActorBarMobileSDF"','Fallback "TextMeshPro/Mobile/Distance Field"')
  (project/'Assets/Resources/OriginalShaders/NativeSDF.shader').write_text(source)
  overlay=base.parent.parent/'unity/GloomhavenVR.Assets/Assets/Bundle/Table/Overlay.shader';shutil.copy2(overlay,project/'Assets/Resources/OriginalShaders/GrabBarOverlay.shader');pins += [overlay,base/'Assets/grabbar_generic.png']
  for name in ['TMPro.cginc','TMPro_Properties.cginc']:shutil.copy2(shaders/name,project/'Assets/Resources/OriginalShaders'/name)
  (project/'Packages/manifest.json').write_text(json.dumps({'dependencies':{k:'1.0.0' for k in ['com.unity.modules.ui','com.unity.modules.imgui','com.unity.modules.imageconversion','com.unity.modules.physics','com.unity.modules.jsonserialize']}}));(project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n');mf=folder/'manifest.json';mf.write_text(json.dumps(manifest,indent=2))
  pins += [frame,native,runner,Path(__file__),ROOT/'scripts/results-window-runtime/Boundary.cs',*fixture.glob('*'),*shaders.glob('*.cginc'),shaders/'ActorBarSDF.shader']
  (folder/'source-pins.json').write_text(json.dumps({str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in pins if p.is_file()},indent=2));(folder/'source-content-hashes.json').write_text(json.dumps({k:hashlib.sha256(v.encode()).hexdigest() for k,v in sources.items()},indent=2))
  cmd=[str(unity),'-batchmode','-force-glcore','-projectPath',str(project),'-executeMethod','ProgressionRunner.Start','-progressionManifest',str(mf),'-logFile',str(folder/'unity.log')]
  if not os.environ.get('DISPLAY'):cmd=['xvfb-run','-a']+cmd
  code=command(cmd,folder/'launcher.log');result=folder/'results.txt'
  if result.exists():print(result.read_text(),end='')
  assert code==0 and result.exists(),'Unity failed: '+str(folder)
  status=0
 finally:
  (folder/'result.json').write_text(json.dumps({'exit_code':status,'limits':__doc__,'partial':bool(args.case) or args.footer_only,'elapsed_seconds':time.monotonic()-started},indent=2));(folder/'cleanup.json').write_text(json.dumps(cleanup_completed_run('results-presentation-runtime',status,log,ROOT,process_reaped=True),indent=2))
if __name__=='__main__':main()
