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
 p.add_argument('--output-dir',type=Path,default=ROOT/'.planning/debug/npc639/proof')
 p.add_argument('--native-dir',type=Path)
 p.add_argument('--atlas-bundle',type=Path,help='Read-only native BattleOverlayCanvas SpriteAtlas bundle')
 p.add_argument('--asset-source-root',type=Path,help='Explicit pending asset worker sources; exact hashes retained')
 p.add_argument('--service',choices=['merchant','mage'],default='mage')
 p.add_argument('--cold-observer',action='store_true',help='Omit the production loading-phase exact-asset scan for a named causal control')
 p.add_argument('--expect-incomplete',action='store_true',help='Causal control must compile and fail the exact complete-picture deadline')
 args=p.parse_args();root=args.source_root.resolve();args.output_dir.mkdir(parents=True,exist_ok=True)
 run=Path(tempfile.mkdtemp(prefix='run-',dir=args.output_dir.resolve()))
 loader=module('mirror639',root/'scripts/check-town-service-mirror.py')
 delivery=module('delivery639',root/'scripts/check-town-native-state623.py')
 receiver=module('receive639',root/'scripts/npc-first-picture638-runtime/run.py')
 bound,_=loader.sources(root);delivery.bind_delivery_transport(root,bound,loader)
 actual,actual_hash=receiver.receiver_sources(root,loader);bound['ActualReceiver638.cs']=actual
 # New asset registry helpers are part of the real resolver, not stand-ins.
 asset_root=(args.asset_source_root or root).resolve()
 for path in (asset_root/'src/GloomhavenVR/Net/TownServices').glob('TownServiceAssets*.cs'):bound[path.name]=path.read_text()
 fixture=run/'fixture';shutil.copytree(root/'scripts/town-service-mirror-runtime',fixture)
 for name in ['NativeState623.cs','NativeDelivery629.cs']:
  shutil.copyfile(root/'scripts/town-native-state623-runtime'/name,fixture/name)
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
 case_source=Path(__file__).with_name('FirstPicture639.cs').read_text()
 if args.cold_observer:case_source=case_source.replace('private static readonly bool PrewarmObserver639 = true;', 'private static readonly bool PrewarmObserver639 = false;')
 (fixture/'FirstPicture639.cs').write_text(case_source)
 boundaries=fixture/'Boundaries.cs';text=boundaries.read_text().replace('internal static bool WantsDebug => false;','internal static bool WantsDebug => true;',1)
 text=text.replace('internal static void Info(string channel, string message) { }','internal static void Info(string channel, string message) => Messages.Add(channel + ": " + message);',1)
 boundaries.write_text(text)
 program=fixture/'Program.cs';text=program.read_text();anchor='            if (variant == "production") PublisherNoCloth();'
 if text.count(anchor)!=1:raise RuntimeError('Native fixture entry drift')
 text=text.replace(anchor, '''            if (suite == "first-picture639-merchant" || suite == "first-picture639-mage") {
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
  subprocess.run([python,str(Path(__file__).with_name('export-native.py')),str(root),str(native)],check=True)
 if args.atlas_bundle:
  shutil.copyfile(args.atlas_bundle.resolve(),run/'original-atlas.bundle')
  # The byte-exact native Texture2D still names the game's original resS path.
  # Actual rendering (unlike descriptor-only admission) needs this read-only stream.
  stream=root/'ressources/GH_Data/resources.assets.resS'
  (editor/'resources.assets.resS').symlink_to(stream.resolve())
  (run/'native-stream-sha256.txt').write_text(hashlib.sha256(stream.read_bytes()).hexdigest()+'  '+str(stream.resolve())+'\n')
 hashes={name:hashlib.sha256(text.encode()).hexdigest()for name,text in bound.items()}
 hashes['NetAvatarDriver.TownServices fullsource']=actual_hash
 hashes['runner']=hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
 for file in fixture.glob('*.cs'):hashes['fixture/'+file.name]=hashlib.sha256(file.read_bytes()).hexdigest()
 (run/'source-hashes.json').write_text(json.dumps(hashes,indent=2)+'\n')
 manifest={'suite':'first-picture639-'+args.service,'evidence':str(run),'result':str(run/'results.txt'),'cases':[{'name':'production','dll':str(run/'bin/Release/netstandard2.1/FirstPicture639.dll'),'expected':'all exact visible originals render within1s wall clock'if args.expect_incomplete else ''}]}
 path=run/'manifest.json';path.write_text(json.dumps(manifest,indent=2)+'\n');print('Evidence: '+str(run),flush=True)
 result=subprocess.run(['xvfb-run','-a',str(unity),'-batchmode','-force-glcore','-projectPath',str(editor),'-executeMethod','MirrorRunner.Start','-mirrorManifest',str(path),'-logFile',str(run/'unity.log')],timeout=240)
 if Path(manifest['result']).exists():print(Path(manifest['result']).read_text(),end='')
 if result.returncode or not Path(manifest['result']).exists():raise SystemExit('FAIL639: '+str(run/'unity.log'))
 print('PASS639 actual complete-picture deadline (or named causal control); retainedproof: '+str(run))
if __name__=='__main__':main()
