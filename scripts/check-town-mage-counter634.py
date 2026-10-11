#!/usr/bin/env python3
"""Render the original mage points heading and its inert TLV110 observer."""
import argparse, hashlib, importlib.util, json, os, shutil, subprocess, tempfile
from pathlib import Path
ROOT = Path(__file__).resolve().parents[1]

def main():
    p=argparse.ArgumentParser(description=__doc__)
    p.add_argument('--source-root',type=Path,default=ROOT)
    p.add_argument('--output-dir',type=Path,default=ROOT/'.planning/debug/town-mage-counter634')
    args=p.parse_args();root=args.source_root.resolve();args.output_dir.mkdir(parents=True,exist_ok=True)
    run=Path(tempfile.mkdtemp(prefix='run-',dir=args.output_dir.resolve()))
    spec=importlib.util.spec_from_file_location('counter634_binding',root/'scripts/check-town-service-mirror.py')
    loader=importlib.util.module_from_spec(spec);spec.loader.exec_module(loader)
    bound,_=loader.sources(root)
    surface=(root/'src/GloomhavenVR/WorldUI/TownServices/TownServiceSurface.cs').read_text()
    # Alias only the class name to coexist with unrelated publisher adapters.
    bound['NativeSurface634.cs']=surface.replace('TownServiceSurface','NativeSurface634')
    adopt=(root/'src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.2.Adopt.cs').read_text()
    constants='\n'.join(loader.expression(adopt,signature) for signature in (
        'private const int CanvasSweepIntervalFrames', 'private const float BackgroundCoverFraction',
        'private const float BackgroundOpaqueAlpha'))
    bound['NativeBackground634.cs']='''using System.Collections.Generic;using UnityEngine;using UnityEngine.UI;using GloomhavenVR.Core;
namespace GloomhavenVR.WorldUI;internal static partial class CanvasConversion {
private static readonly List<Graphic> BgGraphicScratch=new();private static readonly Vector3[] BgCornerScratch=new Vector3[4];
'''+constants+'\n'+loader.method(adopt,'private static void HideFullScreenBackground(ConvertedPanel panel, bool initial)')+'\n}'
    core=(root/'src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.1.Core.cs').read_text()
    first=core.index('        if (transparentBackground)');last=core.index('\n\n',first)
    lifecycle=(root/'src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.4.Lifecycle.cs').read_text()
    first_restore=lifecycle.index('        if (panel.KeepBackgroundHidden)')
    last_restore=lifecycle.index('        panel.HiddenBackgrounds.Clear();',first_restore)+len('        panel.HiddenBackgrounds.Clear();')
    seam=(ROOT/'scripts/town-mage-counter634-runtime/Boundaries.cs').read_text()
    seam=seam.replace('/* ORIGINAL_BACKGROUND_CONVERT */',core[first:last])
    seam=seam.replace('/* ORIGINAL_BACKGROUND_RESTORE */',lifecycle[first_restore:last_restore])
    fixture=run/'fixture';shutil.copytree(root/'scripts/town-service-mirror-runtime',fixture)
    (fixture/'MageCounter634Boundaries.cs').write_text(seam)
    shutil.copy2(ROOT/'scripts/town-mage-counter634-runtime/MageCounter634.cs',fixture/'MageCounter634.cs')
    importer=(root/'scripts/town-first-picture632-runtime/FirstPicture632.cs').read_text()
    importer=importer[:importer.index('    private static IEnumerator FirstPicture632()')]+'\n}\n'
    importer=importer.replace('transforms[0].gameObject.SetActive(true);', 'transforms[0].gameObject.SetActive(true); RestoreCounterLayout634(transforms);',1)
    (fixture/'OriginalCounterImporter634.cs').write_text(importer)
    native_base=(root/'ressources/GH_Data').resolve().parents[1]/'decompiled/GH.Runtime/UnityEngine.UI'
    native_layout_hashes={}
    for name in ('HorizontalLayoutGroupExtended.cs','HorizontalOrVerticalLayoutGroupExtended.cs',
                 'LayoutUtilityExtended.cs','LayoutElementExtended.cs','ILayoutElementExtended.cs'):
        source=native_base/name
        (fixture/('NativeCounter'+name)).write_text(source.read_text())
        native_layout_hashes[name]=hashlib.sha256(source.read_bytes()).hexdigest()
    for path,anchor in [('Publisher.cs','internal static class TownServicePresentation'),('CabinetAudioBoundaries.cs','internal static class WorldUIConfig')]:
        file=fixture/path;txt=file.read_text();assert txt.count(anchor)==1
        file.write_text(txt.replace(anchor,anchor.replace('static class','static partial class'),1))
    program=fixture/'Program.cs';txt=program.read_text();anchor='            if (variant == "production") PublisherNoCloth();'
    assert txt.count(anchor)==1
    branch='''\n            if(suite=="mage-counter634") {
                var proof=MageCounter634();while(proof.MoveNext())yield return proof.Current;
                File.WriteAllText(Path.Combine(_output,"assertions.txt"),_assertions+" assertions\\n");yield break;
            }\n'''
    program.write_text(txt.replace(anchor,anchor+branch,1))
    variants=[('production',None,None,None,''),('white-header','NativeSurface634.cs',
        'transparentBackground: id == 13','transparentBackground: false',
        'original native points heading suppresses only its opaque full-cover backing')]
    unity=Path(os.environ.get('UNITY_PATH','/home/claw/unity-2021.3.5/Editor/Unity'));dotnet=shutil.which('dotnet')or str(Path.home()/'.dotnet/dotnet')
    manifest={'suite':'mage-counter634','result':str(run/'results.txt'),'evidence':str(run),'cases':[]}
    for name,filename,before,after,expected in variants:
        build=run/name;production=build/'production';production.mkdir(parents=True)
        for path,txt in bound.items():
            if path==filename:
                assert txt.count(before)==1;txt=txt.replace(before,after,1)
            (production/path).write_text(txt)
        project=build/'Mirror.csproj';shutil.copy2(fixture/'Mirror.csproj',project);assembly='TownMageCounter634_'+name.replace('-','_')
        result=subprocess.run([dotnet,'build',str(project),'-c','Release','--nologo','--verbosity','quiet',
            '-p:CaseName='+assembly,'-p:FixtureDir='+str(fixture),'-p:ProductionDir='+str(production),
            '-p:UnityManaged='+str(unity.parent/'Data/Managed'),'-p:UnityUi='+str(root/'ressources/GH_Data/Managed/UnityEngine.UI.dll'),
            '-p:UnityTmp='+str(root/'ressources/GH_Data/Managed/Unity.TextMeshPro.dll')],capture_output=True,text=True)
        (build/'build.log').write_text(result.stdout+result.stderr)
        if result.returncode:raise SystemExit(result.stdout+result.stderr)
        manifest['cases'].append(dict(name=name,dll=str(build/'bin/Release/netstandard2.1'/(assembly+'.dll')),expected=expected))
    project=run/'unity';(project/'Assets/Editor').mkdir(parents=True);(project/'Packages').mkdir();(project/'ProjectSettings').mkdir()
    shutil.copy2(fixture/'Editor/MirrorRunner.cs',project/'Assets/Editor/MirrorRunner.cs')
    (project/'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.physics":"1.0.0"}}\n')
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    exporter=ROOT/'scripts/town-mage-counter634-runtime/export-native.py'
    subprocess.run([os.environ.get('UNITYPY_PYTHON',str(Path.home()/'unitypy-venv/bin/python')),str(exporter),str(root),str(project/'Assets/NativeFirstPicture632')],check=True)
    (run/'native-layout-source-hashes.json').write_text(json.dumps(native_layout_hashes,indent=2)+'\n')
    (run/'source-hashes.json').write_text(json.dumps({name:hashlib.sha256(txt.encode()).hexdigest()for name,txt in bound.items()},indent=2)+'\n')
    (run/'conversion-source-hashes.json').write_text(json.dumps({str(path.relative_to(root)):hashlib.sha256(path.read_bytes()).hexdigest()for path in [
        root/'src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.1.Core.cs',root/'src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.2.Adopt.cs',
        root/'src/GloomhavenVR/WorldUI/Conversion/CanvasConversion.4.Lifecycle.cs',root/'src/GloomhavenVR/WorldUI/TownServices/TownServiceSurface.cs']},indent=2)+'\n')
    path=run/'manifest.json';path.write_text(json.dumps(manifest,indent=2)+'\n');print('Evidence: '+str(run),flush=True)
    result=subprocess.run(['xvfb-run','-a',str(unity),'-batchmode','-force-glcore','-projectPath',str(project),'-executeMethod','MirrorRunner.Start','-mirrorManifest',str(path),'-logFile',str(run/'unity.log')],timeout=300)
    if Path(manifest['result']).exists():print(Path(manifest['result']).read_text(),end='')
    if result.returncode or not Path(manifest['result']).exists():raise SystemExit('FAIL; see '+str(run/'unity.log'))
    print('PASS actual original counter and TLV110 observer')

if __name__=='__main__':main()
