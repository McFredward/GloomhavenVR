#!/usr/bin/env python3
"""Production native town binding across separate Unity owner/observer processes."""
import argparse, hashlib, importlib.util, json, os, shutil, subprocess, tempfile
from pathlib import Path

def main():
    root=Path(__file__).resolve().parent.parent
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--source-root',type=Path,default=root);p.add_argument('--output-dir',type=Path,default=root/'.planning/debug/town-native-peer');p.add_argument('--no-negative-controls',action='store_true');a=p.parse_args()
    root=a.source_root.resolve();a.output_dir.mkdir(parents=True,exist_ok=True);run=Path(tempfile.mkdtemp(prefix='run-',dir=a.output_dir.resolve()))
    fixture=root/'scripts/town-service-mirror-runtime';own=root/'scripts/town-native-peer-runtime'
    python=Path(os.environ.get('UNITYPY_PYTHON',str(Path.home()/'unitypy-venv/bin/python')))
    subprocess.run([str(python),str(own/'export-native.py'),str(root/'ressources/GH_Data'),str(run/'native')],check=True)
    spec=importlib.util.spec_from_file_location('town_mirror_sources',root/'scripts/check-town-service-mirror.py');module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    bound,hashes=module.sources(root)
    mesh=(root/'src/GloomhavenVR/Cards/Art/CardMesh.cs').read_text()
    constants='\n'.join(module.expression(mesh,x) for x in ('internal const float Thickness','internal const float CornerRadius','private const int CornerSegments','internal const float RimUvInset'))
    bound['OriginalCardBody.cs']='using UnityEngine;\ninternal static class OriginalCardBody {\n'+constants+'\ninternal static Mesh Create(float w,float h)=>Build(w,h);\n'+module.method(mesh,'private static Mesh Build(float width, float height)')+'\n'+module.method(mesh,'private static Vector3 OutwardNormal(')+'\n}\n'
    hashes['CardMesh.cs']=hashlib.sha256(mesh.encode()).hexdigest()
    cases=[('production',None,None,None,'')]
    if not a.no_negative_controls:cases += [
      ('frozen-root-rect','TownServiceBinding.cs','        if (n.Length != 18 || Root is not RectTransform rect) return;','        if (Root != null) return; if (n.Length != 18 || Root is not RectTransform rect) return;','baseline retains the actual native root rect instead of frozen prefab dimensions'),
      ('omitted-root-layout','TownServiceMirror.Motion.cs','&& (property.Value.Numbers.Length != 18 || SameRootLayout(before.Numbers, property.Value.Numbers))) continue;','&& (property.Value.Numbers.Length == 18 || SameRootLayout(before.Numbers, property.Value.Numbers))) continue;','numeric native root resize preserves the complete visible extent and pivot'),
      ('baked-sprite-provenance','TownServiceAssets.cs','        if (asset is Sprite spriteAsset) asset = CardFaceMipBake.OriginalFor(spriteAsset);','        // omit normalization before cached key lookup','baked sprite resolves the verified original before cached identity lookup'),
      ('drop-verified-alias','TownServiceAssets.cs','        _assets[key] = asset;\n','        // omit later verified alias\n','Original town-service asset is not loaded'),
      ('packed-descriptor','TownServiceTemplateAssets.cs','        Type type = catalog.GetType();','        if (catalog != null) return;\n        Type type = catalog.GetType();','Original town-service asset is not loaded'),
      ('double-root-pose','TownServiceMirror.cs','        root.localPosition = Vector3.zero; root.localRotation = Quaternion.identity; root.localScale = Vector3.one;','        // deliberately retain the template local pose beneath its absolute authored host','detached root normalized after native numeric transform'),
      ('missing-model-fx','TownServiceTemplateAssets.cs','        Visit(assets, root, "native-town|template|" + template, root, modelFace);','        if (modelFace) return;\n        Visit(assets, root, "native-town|template|" + template, root, modelFace);','Original town-service asset is not loaded')]
    unity=Path(os.environ.get('UNITY_PATH','/home/claw/unity-2021.3.5/Editor/Unity'));managed=root/'ressources/GH_Data/Managed';dotnet=str(Path.home()/'.dotnet/dotnet')
    project=run/'unity';(project/'Assets/Editor').mkdir(parents=True);(project/'Packages').mkdir();(project/'ProjectSettings').mkdir()
    shutil.copyfile(fixture/'Editor/MirrorRunner.cs',project/'Assets/Editor/MirrorRunner.cs')
    (project/'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6"}}\n');(project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    (run/'source-hashes.json').write_text(json.dumps(hashes,indent=2)+'\n')
    built=[]
    for name,file,before,after,expected in cases:
      build=run/name;production=build/'production';production.mkdir(parents=True);copy=build/'fixture';shutil.copytree(fixture,copy)
      boundary=(copy/'Boundaries.cs').read_text();anchor='        internal static bool LocalReady, PublishedReady;';assert boundary.count(anchor)==1
      if 'bool HasReadyVisitor(' not in boundary: boundary=boundary.replace(anchor,anchor+'\n        internal static bool HasReadyVisitor(byte service) => false;')
      if 'bool HasReadyMerchantVisitor' not in boundary: boundary=boundary.replace(anchor,anchor+'\n        internal static bool HasReadyMerchantVisitor => false;')
      anchor='internal static class CardFaceMipBake { internal static Sprite OriginalFor(Sprite sprite) => sprite; }'
      assert boundary.count(anchor)==1
      boundary=boundary.replace(anchor,'internal static class CardFaceMipBake { internal static readonly Dictionary<Sprite,Sprite> Originals=new(); internal static Sprite OriginalFor(Sprite sprite) => Originals.TryGetValue(sprite,out Sprite source)?source:sprite; }')
      (copy/'Boundaries.cs').write_text(boundary)
      program=(copy/'Program.cs').read_text();anchor='            DelayedCensusRace();';assert program.count(anchor)==1
      program=program.replace(anchor,'            if (suite.StartsWith("native-peer-", StringComparison.Ordinal))\n            { IEnumerator peer = NativePeer(suite); while (peer.MoveNext()) yield return peer.Current; File.WriteAllText(Path.Combine(_output,"assertions.txt"),_assertions+" assertions\\n"); yield break; }\n'+anchor)
      (copy/'Program.cs').write_text(program);shutil.copyfile(own/'NativePeer.cs',copy/'NativePeer.cs')
      for filename,text in bound.items():
        if filename==file:
          assert text.count(before)==1,(name,filename,'mutation anchor drift');text=text.replace(before,after,1)
        (production/filename).write_text(text)
      csproj=build/'Mirror.csproj';shutil.copyfile(fixture/'Mirror.csproj',csproj);assembly='TownNativePeer_'+name.replace('-','_')
      command=[dotnet,'build',str(csproj),'-c','Release','--nologo','--verbosity','quiet',f'-p:CaseName={assembly}',f'-p:FixtureDir={copy}',f'-p:ProductionDir={production}',f'-p:UnityManaged={unity.parent/"Data/Managed"}',f'-p:UnityUi={managed/"UnityEngine.UI.dll"}',f'-p:UnityTmp={managed/"Unity.TextMeshPro.dll"}']
      result=subprocess.run(command,text=True,stdout=subprocess.PIPE,stderr=subprocess.STDOUT);(build/'build.log').write_text(result.stdout)
      if result.returncode:print(result.stdout);raise SystemExit('FAIL compilation '+name)
      built.append((name,str(build/'bin/Release/netstandard2.1'/(assembly+'.dll')),expected))
    results=[]
    # No same-process registry or source object can leak from capture to playback.
    phases=[('owner',[{'name':'owner','dll':built[0][1],'expected':''}]),('observer',[{'name':n,'dll':dll,'expected':expected} for n,dll,expected in built])]
    for phase,entries in phases:
      manifest={'result':str(run/(phase+'-results.txt')),'evidence':str(run),'suite':'native-peer-'+phase,'cases':entries};path=run/(phase+'-manifest.json');path.write_text(json.dumps(manifest,indent=2))
      command=['xvfb-run','-a',str(unity),'-batchmode','-force-glcore','-projectPath',str(project),'-executeMethod','MirrorRunner.Start','-mirrorManifest',str(path),'-logFile',str(run/(phase+'-unity.log'))]
      environment=os.environ.copy(); environment['GHVR_NATIVE_PEER_SOURCE']=str(root)
      result=subprocess.run(command,stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT,timeout=240,env=environment)
      output=Path(manifest['result']);print(output.read_text() if output.exists() else 'No phase result',flush=True)
      if result.returncode:raise SystemExit('FAIL '+phase+'; see '+str(run/(phase+'-unity.log')))
      results.append({'phase':phase,'exit':result.returncode,'result':str(output)})
    (run/'results.json').write_text(json.dumps({'passed':True,'processes':results,'source':str(root),'sha256':hashes},indent=2)+'\n')
    print('PASS separate-process native town artwork and root geometry; evidence: '+str(run))
if __name__=='__main__':main()
