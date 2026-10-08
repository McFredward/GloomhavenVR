#!/usr/bin/env python3
"""Original merchant row caption layout, repeated native overwrite and observer codec/render parity."""
import argparse, hashlib, importlib.util, json, os, shutil, subprocess, tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]

def load(name,path):
    spec=importlib.util.spec_from_file_location(name,path);module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module);return module

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--source-root',type=Path,default=ROOT)
    p.add_argument('--output-dir',type=Path,default=ROOT/'.planning/debug/merchant-caption646')
    p.add_argument('--no-negative-controls',action='store_true');a=p.parse_args();root=a.source_root.resolve()
    a.output_dir.mkdir(parents=True,exist_ok=True);run=Path(tempfile.mkdtemp(prefix='run-',dir=a.output_dir.resolve()))
    loader=load('caption646_mirror_sources',root/'scripts/check-town-service-mirror.py');bound,hashes=loader.sources(root)
    helper=root/'src/GloomhavenVR/WorldUI/TownServices/TownServiceMerchantCaption.cs';bound[helper.name]=helper.read_text()
    catalog=(root/'src/GloomhavenVR/WorldUI/TownServices/TownServiceCatalog.cs').read_text()
    start=catalog.index('        private void SuppressNativeBacking(')
    end=catalog.index('\n        }',start)+len('\n        }')
    method=catalog[start:end]
    bridge='''using System.Collections.Generic; using GloomhavenVR.Net; using GloomhavenVR.WorldUI; using UnityEngine;
internal sealed class Caption646Driver {
 private readonly RemoteWidgetMirror _row; private readonly TownServiceMerchantCaption _caption;
 private readonly List<Transform> _rowBackgrounds=new(), _backgroundClones=new();
 private Transform? _tooltipClone; private int _suppressionStamp=-1;
 private readonly Owner _owner=new(); private sealed class Owner { internal readonly Inventory _inventory=new(); }
 private sealed class Inventory { internal Component? itemTooltip; }
 internal Caption646Driver(RemoteWidgetMirror row,Transform original) { _row=row;_caption=new(row,original);
 foreach(var image in original.GetComponentsInChildren<UnityEngine.UI.RawImage>(true))_rowBackgrounds.Add(image.transform); }
 internal void Present()=>SuppressNativeBacking();
'''+method+'\n}\n'
    bound['Caption646Driver.cs']=bridge
    fixture=run/'fixture';shutil.copytree(root/'scripts/town-service-mirror-runtime',fixture)
    for name in ['Caption646.cs','CaptionPort.cs']:shutil.copyfile(root/'scripts/merchant-caption646-runtime'/name,fixture/name)
    native=(root/'scripts/town-first-picture632-runtime/FirstPicture632.cs').read_text()
    native=native[:native.index('    private static IEnumerator FirstPicture632()')]+"}\n"
    # A serialized Sprite PPtr is a shared object in the real prefab. The reusable
    # loader creates separate Sprite instances for repeated PPtrs; retain exact
    # shared provenance here instead of turning the two native gold references
    # into ambiguous unnamed clones in the original-asset bank.
    native=native.replace('    private static readonly Dictionary<string,Texture2D> GameTextures632 = new();',
        '    private static readonly Dictionary<string,Texture2D> GameTextures632 = new();\n    private static readonly Dictionary<string,Sprite> GameSprites646 = new();')
    before='image.sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),Vector2.one*.5f);'
    after='if(!GameSprites646.TryGetValue(visual.image,out var sprite)){sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),Vector2.one*.5f);sprite.name=visual.image;GameSprites646.Add(visual.image,sprite);}image.sprite=sprite;'
    assert native.count(before)==1
    native=native.replace(before,after,1)
    (fixture/'NativeRowLoader.cs').write_text(native)
    program=fixture/'Program.cs';text=program.read_text();anchor='            if (variant == "production") PublisherNoCloth();';assert text.count(anchor)==1
    program.write_text(text.replace(anchor,anchor+'\n            if (suite == "caption646") { var proof=Caption646(); while(proof.MoveNext()) yield return proof.Current; File.WriteAllText(Path.Combine(_output,"assertions.txt"),_assertions+" assertions\\n"); yield break; }'))
    variants=[('production',None,None,None,'')]
    if not a.no_negative_controls:
        variants.extend([
            ('small-native-price',helper.name,'private const float FontSize = 36f;','private const float FontSize = 20f;','legible native caption uses the enlarged price and quantity'),
            ('duplicate-name-and-icon',helper.name,'_clones[0]!.gameObject.SetActive(false);','_clones[0]!.gameObject.SetActive(true);','duplicate native name and item icon are absent from the caption'),
            ('catalog-skips-caption','Caption646Driver.cs','            _caption.Apply();','            // Deliberate missing final presentation.','duplicate native name and item icon are absent from the caption'),
        ])
    unity=Path(os.environ.get('UNITY_PATH','/home/claw/unity-2021.3.5/Editor/Unity'))
    hashes[helper.name]=hashlib.sha256(helper.read_bytes()).hexdigest();hashes['TownServiceCatalog.cs']=hashlib.sha256(catalog.encode()).hexdigest()
    case_hashes={};manifest={'suite':'caption646','result':str(run/'results.txt'),'evidence':str(run),'cases':[]}
    for name,file,before,after,expected in variants:
        build=run/name;production=build/'production';production.mkdir(parents=True)
        texts={}
        for filename,text in bound.items():
            if filename==file:assert text.count(before)==1;text=text.replace(before,after,1)
            (production/filename).write_text(text);texts[filename]=hashlib.sha256(text.encode()).hexdigest()
        case_hashes[name]=texts
        csproj=build/'Mirror.csproj';shutil.copyfile(fixture/'Mirror.csproj',csproj);assembly='MerchantCaption646_'+name.replace('-','_')
        r=subprocess.run([str(Path.home()/'.dotnet/dotnet'),'build',str(csproj),'-c','Release','--nologo','--verbosity','quiet',
            '-p:CaseName='+assembly,'-p:FixtureDir='+str(fixture),'-p:ProductionDir='+str(production),
            '-p:UnityManaged='+str(unity.parent/'Data/Managed'),'-p:UnityUi='+str(root/'ressources/GH_Data/Managed/UnityEngine.UI.dll'),
            '-p:UnityTmp='+str(root/'ressources/GH_Data/Managed/Unity.TextMeshPro.dll')],capture_output=True,text=True)
        (build/'build.log').write_text(r.stdout+r.stderr)
        if r.returncode:raise SystemExit(r.stdout+r.stderr)
        manifest['cases'].append({'name':name,'dll':str(build/'bin/Release/netstandard2.1'/(assembly+'.dll')),'expected':expected})
    project=run/'unity';(project/'Assets/Editor').mkdir(parents=True);(project/'Packages').mkdir();(project/'ProjectSettings').mkdir()
    shutil.copyfile(fixture/'Editor/MirrorRunner.cs',project/'Assets/Editor/MirrorRunner.cs')
    (project/'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6"}}\n')
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    subprocess.run([os.environ.get('UNITYPY_PYTHON',str(Path.home()/'unitypy-venv/bin/python')),str(root/'scripts/merchant-caption646-runtime/export-native.py'),str(root/'ressources/GH_Data'),str(project/'Assets/NativeFirstPicture632')],check=True)
    (run/'source-hashes.json').write_text(json.dumps({'production':hashes,'cases':case_hashes,'actual_catalog_method':hashlib.sha256(method.encode()).hexdigest(),'native_loader':hashlib.sha256(native.encode()).hexdigest()},indent=2)+'\n')
    path=run/'manifest.json';path.write_text(json.dumps(manifest,indent=2));print('Evidence: '+str(run),flush=True)
    r=subprocess.run(['xvfb-run','-a',str(unity),'-batchmode','-force-glcore','-projectPath',str(project),'-executeMethod','MirrorRunner.Start','-mirrorManifest',str(path),'-logFile',str(run/'unity.log')],timeout=240)
    if Path(manifest['result']).exists():print(Path(manifest['result']).read_text(),end='')
    if r.returncode or not Path(manifest['result']).exists():raise SystemExit('FAIL actual native merchant caption; '+str(run))
    print('PASS actual native merchant caption; '+str(run))
if __name__=='__main__':main()
