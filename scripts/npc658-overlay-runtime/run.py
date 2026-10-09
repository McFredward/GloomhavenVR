#!/usr/bin/env python3
"""Exercise exact offered-print paint affinity through native capture/playback.

Imports the serialized original highlighter and native OnHovered method, the
production furniture ladder, capture, packed numeric stream and avatar receiver.
The real D3D11 FlexFrame pass depth/blend states are read from game assets; the GL
fragment adapter demonstrates occlusion without claiming original effect pixels.
"""
import argparse, hashlib, importlib.util, json, os, shutil, subprocess, tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
def module(name,path):
 s=importlib.util.spec_from_file_location(name,path);m=importlib.util.module_from_spec(s);s.loader.exec_module(m);return m

def main():
 p=argparse.ArgumentParser(description=__doc__)
 p.add_argument('--source-root',type=Path,default=ROOT)
 p.add_argument('--output-dir',type=Path,default=ROOT/'.planning/debug/npc658-overlay/proof')
 p.add_argument('--old-depth-source',action='store_true',help='Compile exact published656 depth/offerings source in the same native paint challenge')
 p.add_argument('--expect-incomplete',action='store_true',help='Require the exact native frame-above-paper failure from published656')
 args=p.parse_args();root=args.source_root.resolve();args.output_dir.mkdir(parents=True,exist_ok=True)
 if args.old_depth_source != args.expect_incomplete:p.error('Published656 is a designated runtime negative control: use --old-depth-source --expect-incomplete together')
 run=Path(tempfile.mkdtemp(prefix='run-',dir=args.output_dir.resolve()))
 python=os.environ.get('UNITYPY_PYTHON',str(Path.home()/'unitypy-venv/bin/python'))
 subprocess.run([python,str(Path(__file__).with_name('export-pass.py')),str(root),str(run/'native-flexframe-pass.json')],check=True)
 loader=module('mirror639',root/'scripts/check-town-service-mirror.py')
 delivery=module('delivery639',root/'scripts/check-town-native-state623.py')
 receiver=module('receive639',root/'scripts/npc-first-picture638-runtime/run.py')
 bound,_=loader.sources(root);delivery.bind_delivery_transport(root,bound,loader)
 actual,actual_hash=receiver.receiver_sources(root,loader);bound['ActualReceiver638.cs']=actual
 depth=module('depth655',root/'scripts/check-town-depth-order.py')
 real_depth,depth_hashes=depth.sources(root);bound.update(real_depth)
 if args.old_depth_source:
  bound['TownServiceDepthOrder.cs']=subprocess.run(['git','-C',str(root),'show','bf3444cb502e1eff52bcf0e5194a255109879d36:src/GloomhavenVR/WorldUI/TownServices/TownServiceDepthOrder.cs'],check=True,capture_output=True,text=True).stdout
 if args.old_depth_source:
  bound['TownServiceMirror.Offerings.cs']=subprocess.run(['git','-C',str(root),'show','bf3444cb502e1eff52bcf0e5194a255109879d36:src/GloomhavenVR/Net/TownServices/TownServiceMirror.Offerings.cs'],check=True,capture_output=True,text=True).stdout
  bound['TownServiceMirror.Motion.cs']=bound['TownServiceMirror.Motion.cs'].replace('ClearOfferedFrames();','OfferedFrames.Clear();')
 # Existing unused suite methods reference this registration instrumentation.
 bound['TownServiceDepthOrder.cs']=bound['TownServiceDepthOrder.cs'].replace('internal static class TownServiceDepthOrder\n{','internal static class TownServiceDepthOrder\n{\n    internal static readonly System.Collections.Generic.HashSet<Transform> Bound = new();',1)
 bound['TownServiceDepthOrder.cs']=bound['TownServiceDepthOrder.cs'].replace('if (root == null) return;','if (root == null) return; Bound.Add(root);',1)
 runtime=root/'ressources/GH_Data/Managed/GH.Runtime.dll'
 env=dict(os.environ,DOTNET_ROOT=str(Path.home()/'.dotnet'))
 native_controller=subprocess.run([str(Path.home()/'.dotnet/tools/ilspycmd'),'-t','UIEnhancementButtonHighlight',str(runtime)],check=True,capture_output=True,text=True,env=env).stdout
 opening=native_controller.index('public void OnHovered(bool hovered)');end=native_controller.index('\n\tpublic void SetMode',opening)
 hover=native_controller[opening:end]
 bound['ActualNativeHover655.cs']='using UnityEngine; namespace GloomhavenVR.WorldUI; internal sealed partial class UIEnhancementButtonHighlight {\nprivate enum HighlightState { PREVIEW, SELECTED, SELECTABLE, INVALID }\nprivate CanvasGroup fillImage=null!; private HighlightState state; private float opacitySelected=.7f, opacityHovered=.2f;\ninternal void NativeInput655(CanvasGroup fill, int value) { fillImage=fill; state=(HighlightState)value; }\n'+hover+'\n}\n'
 (run/'native-hover-provenance.json').write_text(json.dumps({'GH.Runtime.dll':hashlib.sha256(runtime.read_bytes()).hexdigest(),'decompiled_controller':hashlib.sha256(native_controller.encode()).hexdigest(),'OnHovered':hashlib.sha256(hover.encode()).hexdigest(),**depth_hashes},indent=2)+'\n')
 # New asset registry helpers are part of the real resolver, not stand-ins.
 for path in (root/'src/GloomhavenVR/Net/TownServices').glob('TownServiceAssets*.cs'):bound[path.name]=path.read_text()
 fixture=run/'fixture';shutil.copytree(root/'scripts/town-service-mirror-runtime',fixture)
 for name in ['NativeState623.cs','NativeDelivery629.cs']:
  shutil.copyfile(root/'scripts/town-native-state623-runtime'/name,fixture/name)
 warm=loader.method((root/'src/GloomhavenVR/WorldUI/TownServices/NativeTemplates.EnhancementPreparation.cs').read_text(),'private static void WarmEnhancementBasis(string key)')
 bound['AdmissionWarm646.cs']='using System; using UnityEngine; using GloomhavenVR.Net.TownServices; namespace GloomhavenVR.WorldUI; internal static partial class LazyTemplateProbe {\n'+warm+'\n}\n'
 shutil.copyfile(root/'scripts/npc-admission646-runtime/Admission646.cs',fixture/'Admission646.cs')
 shutil.copyfile(Path(__file__).with_name('Overlay658.cs'),fixture/'Overlay658.cs')
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
 (fixture/'FirstPicture639.cs').write_text(case_source)
 boundaries=fixture/'Boundaries.cs';text=boundaries.read_text().replace('internal static bool WantsDebug => false;','internal static bool WantsDebug => true;',1)
 text=text.replace('internal static void Info(string channel, string message) { }','internal static void Info(string channel, string message) => Messages.Add(channel + ": " + message);',1)
 begin=text.index('    // The converted-window distance ladder has its own production harness.')
 end=text.index('\n\n}',begin)
 text=text[:begin]+text[end:]
 text=text.replace('    internal static class VRLog\n', '    internal enum VRLogLevel { Debug }\n    internal static class VRLog\n',1)
 text=text.replace('        internal static bool WantsDebug => true;', '        internal static bool Wants(VRLogLevel level) => false;\n        internal static bool WantsDebug => true;',1)
 publisher=fixture/'Publisher.cs';publisher.write_text(publisher.read_text().replace('internal sealed class UIEnhancementButtonHighlight : MonoBehaviour { }','internal sealed partial class UIEnhancementButtonHighlight : MonoBehaviour { }',1))
 boundaries.write_text(text)
 program=fixture/'Program.cs';text=program.read_text();anchor='            if (variant == "production") PublisherNoCloth();'
 if text.count(anchor)!=1:raise RuntimeError('Native fixture entry drift')
 text=text.replace(anchor, '''
            if (suite == "npc658-overlay") {
                var proof=NativeOverlay658();while(proof.MoveNext())yield return proof.Current;
                File.WriteAllText(Path.Combine(_output,"assertions.txt"),_assertions+" assertions\\n");yield break;
            }
'''+anchor,1)
 program.write_text(text)
 production=run/'production';production.mkdir()
 for name,text in bound.items():(production/name).write_text(text)
 project=run/'Mirror.csproj';shutil.copyfile(fixture/'Mirror.csproj',project)
 dotnet=shutil.which('dotnet')or str(Path.home()/'.dotnet/dotnet');unity=Path(os.environ.get('UNITY_PATH','/home/claw/unity-2021.3.5/Editor/Unity'))
 command=[dotnet,'build',str(project),'-c','Release','--nologo','--verbosity','quiet','-p:CaseName=NativeOverlay658','-p:FixtureDir='+str(fixture),'-p:ProductionDir='+str(production),'-p:UnityManaged='+str(unity.parent/'Data/Managed'),'-p:UnityUi='+str(root/'ressources/GH_Data/Managed/UnityEngine.UI.dll'),'-p:UnityTmp='+str(root/'ressources/GH_Data/Managed/Unity.TextMeshPro.dll')]
 result=subprocess.run(command,capture_output=True,text=True);(run/'build.log').write_text(result.stdout+result.stderr)
 if result.returncode:raise SystemExit(result.stdout+result.stderr)
 editor=run/'unity';(editor/'Assets/Editor').mkdir(parents=True);(editor/'Packages').mkdir();(editor/'ProjectSettings').mkdir()
 shutil.copyfile(fixture/'Editor/MirrorRunner.cs',editor/'Assets/Editor/MirrorRunner.cs')
 shutil.copyfile(Path(__file__).with_name('NativePass658.shader'),editor/'Assets/NativePass658.shader')
 (editor/'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.physics":"1.0.0"}}\n')
 (editor/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
 (editor/'ProjectSettings/TagManager.asset').write_text("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!78 &1\nTagManager:\n  serializedVersion: 2\n  tags: []\n  layers: []\n  m_SortingLayers:\n  - name: Default\n    uniqueID: 0\n    locked: 0\n  - name: Native658Alternate\n    uniqueID: 1536\n    locked: 0\n")
 native=editor/'Assets/NativeFirstPicture639'
 subprocess.run([python,str(root/'scripts/npc-first-picture639-runtime/export-native.py'),str(root),str(native)],check=True)
 hashes={name:hashlib.sha256(text.encode()).hexdigest()for name,text in bound.items()}
 hashes['NetAvatarDriver.TownServices fullsource']=actual_hash
 hashes['runner']=hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
 hashes['GL fragment/draw-state adapter']=hashlib.sha256(Path(__file__).with_name('NativePass658.shader').read_bytes()).hexdigest()
 hashes['native D3D11 pass exporter']=hashlib.sha256(Path(__file__).with_name('export-pass.py').read_bytes()).hexdigest()
 for file in fixture.glob('*.cs'):hashes['fixture/'+file.name]=hashlib.sha256(file.read_bytes()).hexdigest()
 (run/'source-hashes.json').write_text(json.dumps(hashes,indent=2)+'\n')
 manifest={'suite':'npc658-overlay','evidence':str(run),'result':str(run/'results.txt'),'cases':[{'name':'production','dll':str(run/'bin/Release/netstandard2.1/NativeOverlay658.dll'),'expected':'original native frame remains above its physical print across actual panel ranks'if args.expect_incomplete else ''}]}
 path=run/'manifest.json';path.write_text(json.dumps(manifest,indent=2)+'\n');print('Evidence: '+str(run),flush=True)
 result=subprocess.run(['xvfb-run','-a',str(unity),'-batchmode','-force-glcore','-projectPath',str(editor),'-executeMethod','MirrorRunner.Start','-mirrorManifest',str(path),'-logFile',str(run/'unity.log')],timeout=240)
 if Path(manifest['result']).exists():print(Path(manifest['result']).read_text(),end='')
 if result.returncode or not Path(manifest['result']).exists():raise SystemExit('FAIL658: '+str(run/'unity.log'))
 print('PASS658 exact original frame affinity, native draw-state paint and lifecycle; retained proof: '+str(run))

if __name__=='__main__':main()
