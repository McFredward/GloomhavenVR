#!/usr/bin/env python3
"""Clock a complete original NPC picture through actual capture and receive code.

The deadline is actual wall time, including deserialize/apply/render. It is not a
scheduler clock or an admitted/active flag. Native original hierarchy exports,
packed-atlas availability and all adapter limitations are retained with hashes.
"""
import argparse, hashlib, importlib.util, json, os, shutil, subprocess, tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
def module(name,path):
 s=importlib.util.spec_from_file_location(name,path);m=importlib.util.module_from_spec(s);s.loader.exec_module(m);return m

def main():
 p=argparse.ArgumentParser(description=__doc__)
 p.add_argument('--source-root',type=Path,default=ROOT)
 p.add_argument('--output-dir',type=Path,default=ROOT/'.planning/debug/npc-admission646/proof')
 p.add_argument('--native-dir',type=Path)
 p.add_argument('--atlas-bundle',type=Path,help='Read-only native BattleOverlayCanvas SpriteAtlas bundle')
 p.add_argument('--asset-source-root',type=Path,help='Explicit pending asset worker sources; exact hashes retained')
 p.add_argument('--pre-offer-growth',action='store_true',help='Native13-module pre-offer followed by33-module offer at431ms')
 p.add_argument('--required35',action='store_true',help='Concrete Build645 worst-case census:35 visible originals and66 prepared hierarchies')
 p.add_argument('--live-hover-census',action='store_true',help='Continue owner15Hz capture and real native33→34 hover visibility while first originals are fragmented')
 p.add_argument('--withdraw-hover',action='store_true',help='Also retire/re-register the actual hover original from prepared membership before first delivery')
 p.add_argument('--census-only',action='store_true',help='Exercise actual mounted native children across manifest retirement and delayed owner reparenting')
 p.add_argument('--old-census',action='store_true',help='Exact published645 manifest retirement causal control')
 p.add_argument('--old-source',action='store_true',help='Compile exact unmodified published645 transport against the same fixture')
 p.add_argument('--old-hover-source',action='store_true',help='Compile exact published654 mirror/queue against live-hover655 challenge')
 p.add_argument('--service',choices=['merchant','mage'],default='mage')
 p.add_argument('--cold-observer',action='store_true',help='Omit the production loading-phase exact-asset scan for a named causal control')
 p.add_argument('--without-receipts',action='store_true',help='Re-offer the real current picture before original receipts arrive')
 p.add_argument('--prior-inflight',action='store_true',help='Replace a real older complete original whose first packet is already in flight')
 p.add_argument('--without-opening-reservation',action='store_true',help='Named control: disable only the finite first-picture page reservation')
 p.add_argument('--expect-incomplete',action='store_true',help='Causal control must compile and fail the exact complete-picture deadline')
 args=p.parse_args();root=args.source_root.resolve();args.output_dir.mkdir(parents=True,exist_ok=True)
 if args.live_hover_census and args.service!='mage':p.error('--live-hover-census exercises the native enhancement picture')
 if args.withdraw_hover and not args.live_hover_census:p.error('--withdraw-hover requires --live-hover-census')
 if args.service=='mage' and args.atlas_bundle is None:
  native_bank=args.output_dir.resolve()/'native-original-atlas'
  exporter=root/'scripts/npc639-assets-runtime/export-native.py'
  if not exporter.exists():raise SystemExit('Actual native atlas exporter missing: '+str(exporter))
  inputs={str(file):hashlib.sha256(file.read_bytes()).hexdigest()for file in
      [exporter,root/'ressources/GH_Data/resources.assets',root/'ressources/GH_Data/resources.assets.resS']}
  cache_receipt=native_bank/'proof-input-hashes.json'
  valid=cache_receipt.exists()and json.loads(cache_receipt.read_text())==inputs
  valid=valid and(native_bank/'original-battle-atlas.bundle').exists()and(native_bank/'provenance.json').exists()
  if valid:
   provenance=json.loads((native_bank/'provenance.json').read_text())
   valid=hashlib.sha256((native_bank/'original-battle-atlas.bundle').read_bytes()).hexdigest()==provenance['generated_bundle_sha256']
  if not valid:
   python=os.environ.get('UNITYPY_PYTHON',str(Path.home()/'unitypy-venv/bin/python'))
   subprocess.run([python,str(exporter),str(root/'ressources/GH_Data'),str(native_bank)],check=True)
   cache_receipt.write_text(json.dumps(inputs,indent=2)+'\n')
  args.atlas_bundle=native_bank/'original-battle-atlas.bundle'
 run=Path(tempfile.mkdtemp(prefix='run-',dir=args.output_dir.resolve()))
 loader=module('mirror639',root/'scripts/check-town-service-mirror.py')
 delivery=module('delivery639',root/'scripts/check-town-native-state623.py')
 receiver=module('receive639',root/'scripts/npc-first-picture638-runtime/run.py')
 bound,_=loader.sources(root);delivery.bind_delivery_transport(root,bound,loader)
 if args.without_opening_reservation:
  key='ActualScheduler629.cs';anchor='result = _town.NextOpening(now);'
  if bound[key].count(anchor)!=2:raise RuntimeError('Finite opening reservation control anchor drift')
  bound[key]=bound[key].replace(anchor,'result = null; /*639 named causal control: finite opening reservation omitted*/')
  (run/'controlled-mutation639.txt').write_text('Both existing NextOpening calls omitted; all native assets, complete originals, ordinary queues, receipts and byte/event budgets unchanged.\n')
 actual,actual_hash=receiver.receiver_sources(root,loader);bound['ActualReceiver638.cs']=actual
 if args.old_source:
  for name, path in (('ExtrasSendQueue.cs','src/GloomhavenVR/Net/ExtrasSendQueue.cs'), ('TownServiceSendQueue.cs','src/GloomhavenVR/Net/TownServices/TownServiceSendQueue.cs')):
   original=subprocess.run(['git','-C',str(root),'show','2d77ffb65:'+path],check=True,capture_output=True,text=True).stdout
   if name=='ExtrasSendQueue.cs':
    original=original[:original.index('\n/// <summary>',original.index('internal sealed class ExtrasSendQueue'))]
    start=original.index('        internal static bool Same(Pending a, Pending b) =>');end=original.index(';',start)+1
    branch='a.Identity is TownServices.TownServiceFrame v && b.Identity is TownServices.TownServiceFrame u && TownServices.TownServiceSendQueue.SameIdentity(v,u)'
    original=original[:start]+'        internal static bool Same(Pending a, Pending b) => '+branch+';'+original[end:]
    import re
    protocol=(root/'src/GloomhavenVR/Net/NetProtocol.cs').read_text()
    for constant,value in re.findall(r'public const byte (\w+) = (\d+);',protocol):original=re.sub(r'\bNetProtocol\.'+constant+r'\b',value,original)
   bound[name]=original
  (run/'exact-old-source.txt').write_text('Published dev2d77ffb65 Build645 ExtrasSendQueue + TownServiceSendQueue; unchanged fixture, deadline, codecs, frozen native asset path.\n')
 if args.old_census:
  bound['TownServiceMirror.cs']=subprocess.run(['git','-C',str(root),'show','2d77ffb65:src/GloomhavenVR/Net/TownServices/TownServiceMirror.cs'],check=True,capture_output=True,text=True).stdout
 if args.old_hover_source:
  for name in ['TownServiceMirror.cs','TownServiceSendQueue.cs']:
   bound[name]=subprocess.run(['git','-C',str(root),'show','deb989570:src/GloomhavenVR/Net/TownServices/'+name],check=True,capture_output=True,text=True).stdout
  (run/'exact-old-source.txt').write_text('Published deb989570 Build654 mirror/queue; same real owner capture and native hover census, unchanged deadline and transport budgets.\n')
 # New asset registry helpers are part of the real resolver, not stand-ins.
 asset_root=(args.asset_source_root or root).resolve()
 for path in (asset_root/'src/GloomhavenVR/Net/TownServices').glob('TownServiceAssets*.cs'):bound[path.name]=path.read_text()
 fixture=run/'fixture';shutil.copytree(root/'scripts/town-service-mirror-runtime',fixture)
 for name in ['NativeState623.cs','NativeDelivery629.cs']:
  shutil.copyfile(root/'scripts/town-native-state623-runtime'/name,fixture/name)
 warm=loader.method((root/'src/GloomhavenVR/WorldUI/TownServices/NativeTemplates.EnhancementPreparation.cs').read_text(),'private static void WarmEnhancementBasis(string key)')
 bound['AdmissionWarm646.cs']='using System; using UnityEngine; using GloomhavenVR.Net.TownServices; namespace GloomhavenVR.WorldUI; internal static partial class LazyTemplateProbe {\n'+warm+'\n}\n'
 shutil.copyfile(Path(__file__).with_name('Admission646.cs'),fixture/'Admission646.cs')
 reader_source=root/'scripts/town-first-picture632-runtime/FirstPicture632.cs'
 reader=reader_source.read_text().split('    private static IEnumerator FirstPicture632()')[0]+'}\n'
 reader=reader.replace('NativeRow632(Transform parent,string localized)', 'NativeRow632(Transform parent,string localized,string panel = "row")').replace('"NativeFirstPicture632"','"NativeFirstPicture639", panel')
 # The game uses the full distance-field shader. The old reader copied the
 # editor's mobile material and silently omitted many native properties.
 reader=reader.replace('new Material(TMP_Settings.defaultFontAsset.material)',
     'new Material(Shader.Find("TextMeshPro/Distance Field") ?? throw new InvalidOperationException("Native TMP shader absent"))')
 reader=reader.replace('GameMaterials632.Add(material.key,restored);',
     'restored.SetTexture("_MainTex",TMP_Settings.defaultFontAsset.atlasTextures[0]); GameMaterials632.Add(material.key,restored);')
 (fixture/'NativeReader639.cs').write_text(reader)
 case_source=(root/'scripts/npc-first-picture639-runtime/FirstPicture639.cs').read_text()
 case_source=patch646(case_source,args.pre_offer_growth,args.required35)
 if args.live_hover_census:case_source=patch655(case_source,args.withdraw_hover)
 if args.cold_observer:case_source=case_source.replace('private static readonly bool PrewarmObserver639 = true;', 'private static readonly bool PrewarmObserver639 = false;')
 if args.without_receipts:case_source=case_source.replace('private static readonly bool AcknowledgeObserver639 = true;', 'private static readonly bool AcknowledgeObserver639 = false;')
 if args.prior_inflight:case_source=case_source.replace('private static readonly bool PriorInFlight639 = false;', 'private static readonly bool PriorInFlight639 = true;')
 (fixture/'FirstPicture639.cs').write_text(case_source)
 boundaries=fixture/'Boundaries.cs';text=boundaries.read_text().replace('internal static bool WantsDebug => false;','internal static bool WantsDebug => true;',1)
 text=text.replace('internal static void Info(string channel, string message) { }','internal static void Info(string channel, string message) => Messages.Add(channel + ": " + message);',1)
 boundaries.write_text(text)
 program=fixture/'Program.cs';text=program.read_text();anchor='            if (variant == "production") PublisherNoCloth();'
 if text.count(anchor)!=1:raise RuntimeError('Native fixture entry drift')
 text=text.replace(anchor, '''
            if (suite == "admission646-census") {
                var proof=AdmissionCensus646();while(proof.MoveNext())yield return proof.Current;
                File.WriteAllText(Path.Combine(_output,"assertions.txt"),_assertions+" assertions\\n");yield break;
            }            if (suite == "first-picture639-merchant" || suite == "first-picture639-mage") {
                var proof=FirstPicture639(suite=="first-picture639-mage"); while(proof.MoveNext()) yield return proof.Current;
                File.WriteAllText(Path.Combine(_output,"assertions.txt"),_assertions+" assertions\\n");yield break;
            }
'''+anchor,1);program.write_text(text)
 production=run/'production';production.mkdir()
 for name,text in bound.items():(production/name).write_text(text)
 project=run/'Mirror.csproj';shutil.copyfile(fixture/'Mirror.csproj',project)
 dotnet=shutil.which('dotnet')or str(Path.home()/'.dotnet/dotnet');unity=Path(os.environ.get('UNITY_PATH','/home/claw/unity-2021.3.5/Editor/Unity'))
 command=[dotnet,'build',str(project),'-c','Release','--nologo','--verbosity','quiet','-p:CaseName=FirstPicture639','-p:FixtureDir='+str(fixture),'-p:ProductionDir='+str(production),'-p:UnityManaged='+str(unity.parent/'Data/Managed'),'-p:UnityUi='+str(root/'ressources/GH_Data/Managed/UnityEngine.UI.dll'),'-p:UnityTmp='+str(root/'ressources/GH_Data/Managed/Unity.TextMeshPro.dll')]
 result=subprocess.run(command,capture_output=True,text=True);(run/'build.log').write_text(result.stdout+result.stderr)
 if result.returncode:raise SystemExit(result.stdout+result.stderr)
 editor=run/'unity';(editor/'Assets/Editor').mkdir(parents=True);(editor/'Packages').mkdir();(editor/'ProjectSettings').mkdir()
 shutil.copyfile(fixture/'Editor/MirrorRunner.cs',editor/'Assets/Editor/MirrorRunner.cs')
 (editor/'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.physics":"1.0.0"}}\n')
 (editor/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
 native=editor/'Assets/NativeFirstPicture639'
 if args.native_dir:shutil.copytree(args.native_dir.resolve(),native)
 else:
  python=os.environ.get('UNITYPY_PYTHON',str(Path.home()/'unitypy-venv/bin/python'))
  subprocess.run([python,str(root/'scripts/npc-first-picture639-runtime/export-native.py'),str(root),str(native)],check=True)
 if args.atlas_bundle:
  shutil.copyfile(args.atlas_bundle.resolve(),run/'original-atlas.bundle')
  # The byte-exact native Texture2D still names the game's original resS path.
  # Actual rendering (unlike descriptor-only admission) needs this read-only stream.
  stream=args.atlas_bundle.resolve().parent/'resources.assets.resS'
  if not stream.exists():stream=root/'ressources/GH_Data/resources.assets.resS'
  (editor/'resources.assets.resS').symlink_to(stream.resolve())
  (run/'native-stream-sha256.txt').write_text(hashlib.sha256(stream.read_bytes()).hexdigest()+'  '+str(stream.resolve())+'\n')
 hashes={name:hashlib.sha256(text.encode()).hexdigest()for name,text in bound.items()}
 hashes['NetAvatarDriver.TownServices fullsource']=actual_hash
 hashes['runner']=hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
 for file in fixture.glob('*.cs'):hashes['fixture/'+file.name]=hashlib.sha256(file.read_bytes()).hexdigest()
 (run/'source-hashes.json').write_text(json.dumps(hashes,indent=2)+'\n')
 manifest={'suite':'first-picture639-'+args.service,'evidence':str(run),'result':str(run/'results.txt'),'cases':[{'name':'production','dll':str(run/'bin/Release/netstandard2.1/FirstPicture639.dll'),'expected':'all exact visible originals render within1s wall clock'if args.expect_incomplete else ''}]}
 if args.census_only:
  manifest['suite']='admission646-census'
  manifest['cases'][0]['expected']='retained native child survives retired mount on every render' if args.old_census else ''
 path=run/'manifest.json';path.write_text(json.dumps(manifest,indent=2)+'\n');print('Evidence: '+str(run),flush=True)
 result=subprocess.run(['xvfb-run','-a',str(unity),'-batchmode','-force-glcore','-projectPath',str(editor),'-executeMethod','MirrorRunner.Start','-mirrorManifest',str(path),'-logFile',str(run/'unity.log')],timeout=240)
 if Path(manifest['result']).exists():print(Path(manifest['result']).read_text(),end='')
 if result.returncode or not Path(manifest['result']).exists():raise SystemExit('FAIL639: '+str(run/'unity.log'))
 print('PASS646 '+('retained native census' if args.census_only else 'actual complete-picture deadline (or named causal control)')+'; retained proof: '+str(run))
def patch646(case_source,pre_offer_growth=False,required35=False):
 anchor='            SetTransaction639(service,false);capture();SetTransaction639(service,true);'
 assert case_source.count(anchor)==1
 case_source=case_source.replace(anchor,'''            // Actual cumulative artwork is queued while the old complete native
            // original remains fragmented. A later current complete original
            // supersedes both; this is the missing pre-offer handoff case.
            title.text="Previous native title delta still queued";
            if(rows.Count!=0)rows[0].GetComponentsInChildren<TMP_Text>(true).First(x=>x.name=="Name").text="Previous option title delta";
            capture();
            Check(captured.Any(frame=>frame.BaseSequence!=0&&frame.TemplateAddress.StartsWith("face.",StringComparison.Ordinal)),
                "actual pre-offer cumulative delta is queued behind its unfinished complete native original");
'''+anchor)

 bank='        var originals=new List<PictureOriginal639>();'
 assert case_source.count(bank)==1
 case_source=case_source.replace(bank,bank+'\n        GameObject frozenBank646=Go("646 actual frozen provenance");frozenBank646.SetActive(false);LazyTemplateProbe.Close();LazyTemplateProbe.Open(frozenBank646);')
 register='            TownServiceMirror.RegisterTemplate(service,id,nativeObserver,address:address);'
 assert case_source.count(register)==1
 case_source=case_source.replace(register,'            Transform frozen646=LazyTemplateProbe.FreezeOriginal646(nativeObserver,"proof646."+id);\n            TownServiceMirror.RegisterTemplate(service,id,frozen646,address:address);')
 if pre_offer_growth:
  point='            capture();var priorClock=System.Diagnostics.Stopwatch.StartNew();bool originalStarted=false;'
  assert case_source.count(point)==1
  case_source=case_source.replace(point,'            foreach(var original in originals.Where(x=>x.Required&&(x.Address.StartsWith("enchant.row|",StringComparison.Ordinal)||x.Address.StartsWith("enhance.confirm.",StringComparison.Ordinal))))original.Source.gameObject.SetActive(false);\n            capture();\n            Check(captured.Last(x=>x.Module==TownServiceFrame.ManifestModule).RequiredVisibleModules!.Length==13,"actual pre-offer census has13 originals before native UI/rows mount");\n            var preOfferClock646=System.Diagnostics.Stopwatch.StartNew();\n            var priorClock=System.Diagnostics.Stopwatch.StartNew();bool originalStarted=false;')
  point='            SetTransaction639(service,false);capture();SetTransaction639(service,true);'
  assert case_source.count(point)==1
  case_source=case_source.replace(point,'            while(preOfferClock646.Elapsed.TotalSeconds<.431)yield return null;\n            SetTransaction639(service,false);capture();\n            foreach(var original in originals.Where(x=>x.Required))original.Source.gameObject.SetActive(true);\n            SetTransaction639(service,true);')
 if required35:
  for before,after in [('for(int i=0;i<14;i++)','for(int i=0;i<16;i++)'),('string option=optionNames[i];','string option=optionNames[i%optionNames.Length];'),('if(mage)for(int i=0;i<26;i++)','if(mage)for(int i=0;i<31;i++)'),('originals.Count==59&&originals.Count(x=>x.Required)==33','originals.Count==66&&originals.Count(x=>x.Required)==35'),('contains33 actual required originals and59 prepared','contains35 actual required originals and66 prepared')]:
   assert case_source.count(before)==1
   case_source=case_source.replace(before,after)
 return case_source
def patch655(case_source,withdraw=False):
 # The old fixture destroyed live owner atlas wrappers and called only motion
 # capture during arrival. Retain independent real owner wrappers for normal
 # capture; the observer's production asset bank is independently prepared.
 anchor='''            poison!.sprite=null;
            foreach(var sprite in _ownerAtlasWrappers639)if(sprite!=null)Object.DestroyImmediate(sprite);
            _ownerAtlasWrappers639=Array.Empty<Sprite>();
            if(!PrewarmObserver639)TownServiceMirror.Assets.Clear();
            actualColdSprite=true;'''
 assert case_source.count(anchor)==1
 case_source=case_source.replace(anchor,'''            // This challenge exercises the real continuously capturing owner.
            // Its Sprite wrappers must remain valid. The independent observer
            // registry was prepared before timing; cold-alias behavior inherits
            // the separate unmodified639/646 exact-asset challenge.
            actualColdSprite=true;
''')
 anchor='                if(!actualColdSprite)capture();\n                else typeof(TownServiceMirror).GetMethod("CaptureMotion",PrivateStatic)!.Invoke(null,new object[]{publish});'
 assert case_source.count(anchor)==1
 case_source=case_source.replace(anchor,'''                // A native hover original joins/leaves the required census while
                // the real complete first offer remains fragmented in the queue.
                PictureOriginal639 hover655=originals.First(x=>!x.Required);
                hover655.Source.gameObject.SetActive(now-began>=.20f&&now-began<1.2f
                    && ((int)((now-began-.20f)/.13f)&1)==0);
                capture();
''')
 if withdraw:
  anchor='        double clock=0;int events=0,members=0,totalBytes=0;float ready=-1;'
  assert case_source.count(anchor)==1
  case_source=case_source.replace(anchor,anchor+'\n        bool hoverRegistered655=true;')
  anchor='''                capture();

'''
  # Keep the wrapper's exact source lifetime, not only a synthesized census.
  replacement='''                bool hoverVisible655=hover655.Source.gameObject.activeSelf;
                if(hoverRegistered655!=hoverVisible655)
                {
                    NetPlayerActors.Peer=2;SetNativeSenderActive629(true);
                    try
                    {
                        if(hoverVisible655)
                        { TownServiceMirror.RegisterModule(hover655.Id,hover655.Id,hover655.Source,hover655.Exclude,hover655.Address);
                          TownServiceMirror.SetPriority(hover655.Id,true); }
                        else TownServiceMirror.UnregisterModule(hover655.Id);
                        hoverRegistered655=hoverVisible655;
                    }
                    finally{SetNativeSenderActive629(false);NetPlayerActors.Peer=10;}
                }
                capture();

'''
  assert case_source.count(anchor)==1, 'Live owner capture timing anchor changed'
  case_source=case_source.replace(anchor,replacement)
 return case_source
if __name__=='__main__':main()
