#!/usr/bin/env python3
"""Actual serialized enhancement-row hover under independent parent-fit motion."""
import argparse, hashlib, importlib.util, json, os, shutil, subprocess, tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def load(name,path):
    spec=importlib.util.spec_from_file_location(name,path);m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--source-root',type=Path,default=ROOT)
    p.add_argument('--output-dir',type=Path,default=ROOT/'.planning/debug/town-hover645')
    p.add_argument('--no-negative-controls',action='store_true');a=p.parse_args();root=a.source_root.resolve()
    a.output_dir.mkdir(parents=True,exist_ok=True);run=Path(tempfile.mkdtemp(prefix='run-',dir=a.output_dir.resolve()))
    loader=load('hover645_mirror_sources',root/'scripts/check-town-service-mirror.py');bound,hashes=loader.sources(root)
    fixture=run/'fixture';shutil.copytree(root/'scripts/town-service-mirror-runtime',fixture)
    shutil.copyfile(root/'scripts/town-hover645-runtime/Hover645.cs',fixture/'Hover645.cs')
    native=(root/'scripts/town-first-picture632-runtime/FirstPicture632.cs').read_text()
    native=native[:native.index('    private static IEnumerator FirstPicture632()')]+"}\n"
    (fixture/'NativeRowLoader.cs').write_text(native)
    program=fixture/'Program.cs';text=program.read_text();anchor='            if (variant == "production") PublisherNoCloth();';assert text.count(anchor)==1
    program.write_text(text.replace(anchor,anchor+'\n            if (suite == "hover645") { var hover=Hover645(); while(hover.MoveNext()) yield return hover.Current; File.WriteAllText(Path.Combine(_output,"assertions.txt"),_assertions+" assertions\\n"); yield break; }'))
    variants=[('production',None,None,None,'')]
    if not a.no_negative_controls:
        current = bound['TownServiceBinding.cs']
        old = current.replace('        Vector3 position = rect.localPosition;\n','',1).replace('        if (!rect.localPosition.Equals(position)) rect.localPosition = position;\n','',1)
        old = old.replace('        ApplyRootLayout(root, detached: false);',
            '        if (Root is RectTransform rr && root.Length == 18)\n'
            '        { rr.anchorMin = rr.anchorMax = new Vector2(.5f, .5f); rr.pivot = new Vector2(root[14], root[15]); rr.sizeDelta = new Vector2(root[16], root[17]); }',1)
        assert old != current
        variants.append(('root-layout-translates-header','TownServiceBinding.cs',current,old,'hover after an original artwork refresh cannot move a stationary native row vertically'))
    unity=Path(os.environ.get('UNITY_PATH','/home/claw/unity-2021.3.5/Editor/Unity'))
    case_hashes={}
    manifest={'suite':'hover645','result':str(run/'results.txt'),'evidence':str(run),'cases':[]}
    for name,file,before,after,expected in variants:
        build=run/name;production=build/'production';production.mkdir(parents=True)
        for filename,text in bound.items():
            if filename==file: assert text==before;text=after
            (production/filename).write_text(text)
        case_hashes[name]={filename:hashlib.sha256(text.encode()).hexdigest() for filename,text in bound.items()}
        if file: case_hashes[name][file]=hashlib.sha256(after.encode()).hexdigest()
        csproj=build/'Mirror.csproj';shutil.copyfile(fixture/'Mirror.csproj',csproj);assembly='TownHover645_'+name.replace('-','_')
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
    subprocess.run([os.environ.get('UNITYPY_PYTHON',str(Path.home()/'unitypy-venv/bin/python')),str(root/'scripts/town-first-picture632-runtime/export-native.py'),str(root/'ressources/GH_Data'),str(project/'Assets/NativeFirstPicture632')],check=True)
    (run/'source-hashes.json').write_text(json.dumps({'production':hashes,'cases':case_hashes,'native_loader':hashlib.sha256(native.encode()).hexdigest()},indent=2)+'\n')
    path=run/'manifest.json';path.write_text(json.dumps(manifest,indent=2));print('Evidence: '+str(run),flush=True)
    r=subprocess.run(['xvfb-run','-a',str(unity),'-batchmode','-force-glcore','-projectPath',str(project),'-executeMethod','MirrorRunner.Start','-mirrorManifest',str(path),'-logFile',str(run/'unity.log')],timeout=240)
    if Path(manifest['result']).exists():print(Path(manifest['result']).read_text(),end='')
    if r.returncode or not Path(manifest['result']).exists():raise SystemExit('FAIL actual native hover; '+str(run))
    print('PASS actual native hover coordinates; '+str(run))
if __name__=='__main__':main()
