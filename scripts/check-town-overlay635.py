#!/usr/bin/env python3
"""Render and raycast the shipped enchantress effect on the actual original layout.

Uses actual UIEnchantressEffect, LoopAnimator and LeanTween DLL implementations,
serialized native11-node highlighter/images/layout and source-bound pool/Highlight
transform writes. The physical print is a diagnostic rectangle, UI area input uses
an equivalent Button adapter, and editor UI shaders are an explicit visual boundary.
Remote transport is covered by the independent original-offer parity fixture.
"""
import argparse, hashlib, importlib.util, json, os, re, shutil, subprocess, tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root',type=Path,default=ROOT)
    parser.add_argument('--output-dir',type=Path,default=ROOT/'.planning/debug/town-overlay635')
    parser.add_argument('--no-negative-controls',action='store_true')
    args=parser.parse_args(); root=args.source_root.resolve(); args.output_dir.mkdir(parents=True,exist_ok=True)
    run=Path(tempfile.mkdtemp(prefix='run-',dir=args.output_dir.resolve()))
    spec=importlib.util.spec_from_file_location('overlay635_binding',root/'scripts/check-town-service-mirror.py')
    bind=importlib.util.module_from_spec(spec); spec.loader.exec_module(bind); bound,_=bind.sources(root)
    fixture=run/'fixture';shutil.copytree(root/'scripts/town-service-mirror-runtime',fixture)
    shutil.copyfile(ROOT/'scripts/town-overlay635-runtime/NativeOverlay635.cs',fixture/'NativeOverlay635.cs')
    for filename in ['OfferedOrientation629.cs']:
        file=fixture/filename;file.write_text(re.sub(r'\bAbilityCardUI\b','GloomhavenVR.WorldUI.AbilityCardUI',file.read_text()))
    importer=(root/'scripts/town-first-picture632-runtime/FirstPicture632.cs').read_text()
    importer=importer[:importer.index('    private static IEnumerator FirstPicture632()')]+'\n}\n'
    (fixture/'OriginalHighlighter635.cs').write_text(importer)
    native_base=(root/'ressources/GH_Data').resolve().parents[1]/'decompiled/GH.Runtime'
    window=(native_base/'UINewEnhancementWindow.cs').read_text(); area=(native_base/'UIEnhancementButtonHighlight.cs').read_text()
    pool='highlightAbilityPool[num].transform.SetParent(item2.Key.transform);'
    assert window.count(pool)==1
    writes=['rectTransform.pivot = target.pivot;', 'rectTransform.sizeDelta = target.rect.size;', 'base.transform.position = target.transform.position;']
    assert all(area.count(w)==1 for w in writes)
    source='''using UnityEngine;public static partial class MirrorProgram {
private static void NativePoolPlace635(Transform original,RectTransform target){
'''+pool.replace('highlightAbilityPool[num].transform','original').replace('item2.Key.transform','target.transform')+'\noriginal.gameObject.SetActive(true);\nRectTransform rectTransform=(RectTransform)original;\n'+ '\n'.join(w.replace('base.transform','original')for w in writes)+'\n}\n}'
    (fixture/'ActualNativePoolWrites635.cs').write_text(source)
    program=fixture/'Program.cs'; txt=program.read_text();anchor='            if (variant == "production") PublisherNoCloth();';assert txt.count(anchor)==1
    txt=txt.replace(anchor,anchor+'''\n            if(suite=="native-overlay635") {var proof=NativeOverlay635();while(proof.MoveNext())yield return proof.Current;File.WriteAllText(Path.Combine(_output,"assertions.txt"),_assertions+" assertions\\n");yield break;}\n''',1);program.write_text(txt)
    managed=root/'ressources/GH_Data/Managed';unity=Path(os.environ.get('UNITY_PATH','/home/claw/unity-2021.3.5/Editor/Unity'));dotnet=shutil.which('dotnet')or str(Path.home()/'.dotnet/dotnet')
    project_text=(fixture/'Mirror.csproj').read_text().replace('CS0649;','CS0436;CS0649;')
    project_text=project_text.replace('</ItemGroup>','\n'+''.join('<Reference Include="'+name+'"><HintPath>'+str(managed/(name+'.dll'))+'</HintPath><Private>false</Private></Reference>'for name in ['GH.Runtime','GH.Runtime.FirstPass','ThirdParty'])+'\n</ItemGroup>')
    (fixture/'Mirror.csproj').write_text(project_text)
    native_export=run/'native-export'
    subprocess.run([os.environ.get('UNITYPY_PYTHON',str(Path.home()/'unitypy-venv/bin/python')),str(ROOT/'scripts/town-overlay635-runtime/export-native.py'),str(root/'ressources/GH_Data'),str(native_export)],check=True)
    config=json.loads((native_export/'native-effect635.json').read_text());effect=config['effect'];pulse=config['loop']['effects'][0]
    assert effect['enchantressEffect']['m_PathID']==2098 and effect['idleAnimator']['m_PathID']==11055
    assert effect['rotationTime']==20 and effect['rotationSpeed']==1
    assert pulse['Transform']['m_PathID']==7358 and pulse['Animation']==24 and pulse['Duration']==2
    assert pulse['FromUniqueValue']==1 and abs(pulse['ToUniqueValue']-.6)<1e-6 and pulse['AnimationType']==0
    (run/'native-effect-source-verified.json').write_text(json.dumps(config,indent=2)+'\n')
    variants=[('production',None,None,'')]
    if not args.no_negative_controls:
        variants.extend([('retained-flat-area-basis','        AlignNativeAreas();','        /* omit original pooled basis correction */','native world-preserving pooled area matches'),
                         ('flat-world-z-clock','_aura.rotation.eulerAngles.z','_aura.localRotation.eulerAngles.z','actual native textured ink advances continuously')])
    manifest={'suite':'native-overlay635','result':str(run/'results.txt'),'evidence':str(run),'managed':str(managed.resolve()),'cases':[]}
    for name,before,after,expected in variants:
        build=run/name;production=build/'production';production.mkdir(parents=True)
        for filename,value in bound.items():
            if filename=='TownServiceNativeEnhancementCardMask.cs'and before:
                assert value.count(before)==1;value=value.replace(before,after,1)
            (production/filename).write_text(value)
        project=build/'Mirror.csproj';shutil.copyfile(fixture/'Mirror.csproj',project);assembly='TownOverlay635_'+name.replace('-','_')
        result=subprocess.run([dotnet,'build',str(project),'-c','Release','--nologo','--verbosity','quiet','-p:CaseName='+assembly,'-p:FixtureDir='+str(fixture),'-p:ProductionDir='+str(production),'-p:UnityManaged='+str(unity.parent/'Data/Managed'),'-p:UnityUi='+str(managed/'UnityEngine.UI.dll'),'-p:UnityTmp='+str(managed/'Unity.TextMeshPro.dll')],capture_output=True,text=True)
        (build/'build.log').write_text(result.stdout+result.stderr)
        if result.returncode:raise SystemExit(result.stdout+result.stderr)
        manifest['cases'].append(dict(name=name,dll=str(build/'bin/Release/netstandard2.1'/(assembly+'.dll')),expected=expected))
    project=run/'unity';(project/'Assets/Editor').mkdir(parents=True);(project/'Packages').mkdir();(project/'ProjectSettings').mkdir()
    runner=(fixture/'Editor/MirrorRunner.cs').read_text().replace('public string result, evidence, suite;','public string result, evidence, suite, managed;')
    anchor='        output = new StreamWriter(manifest.result);';assert runner.count(anchor)==1
    runner=runner.replace(anchor,'''        AppDomain.CurrentDomain.AssemblyResolve += (_, e) => {
            string name = new AssemblyName(e.Name).Name + ".dll";
            string path = Path.Combine(manifest.managed, name);
            return File.Exists(path) && !name.StartsWith("UnityEngine") && !name.StartsWith("System.") ? Assembly.LoadFrom(path) : null;
        };
        foreach(string name in new[]{"GH.Runtime.dll","GH.Runtime.FirstPass.dll","ThirdParty.dll"}) Assembly.LoadFrom(Path.Combine(manifest.managed,name));
'''+anchor,1)
    (project/'Assets/Editor/MirrorRunner.cs').write_text(runner)
    (project/'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.physics":"1.0.0"}}\n')
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    shutil.copytree(native_export,project/'Assets/NativeFirstPicture632')
    hashes={name:hashlib.sha256(value.encode()).hexdigest()for name,value in bound.items()}
    hashes.update({str(p):hashlib.sha256(p.read_bytes()).hexdigest()for p in [native_base/'UINewEnhancementWindow.cs',native_base/'UIEnhancementButtonHighlight.cs',managed/'GH.Runtime.dll',managed/'GH.Runtime.FirstPass.dll',ROOT/'scripts/check-town-overlay635.py',ROOT/'scripts/town-overlay635-runtime/NativeOverlay635.cs',ROOT/'scripts/town-overlay635-runtime/export-native.py']})
    (run/'source-hashes.json').write_text(json.dumps({'sha256':hashes,'boundaries':__doc__},indent=2)+'\n')
    path=run/'manifest.json';path.write_text(json.dumps(manifest,indent=2)+'\n');print('Evidence: '+str(run),flush=True)
    result=subprocess.run(['xvfb-run','-a',str(unity),'-batchmode','-force-glcore','-projectPath',str(project),'-executeMethod','MirrorRunner.Start','-mirrorManifest',str(path),'-logFile',str(run/'unity.log')],timeout=300)
    if (run/'results.txt').exists():print((run/'results.txt').read_text(),end='')
    if result.returncode or not (run/'results.txt').exists():raise SystemExit('FAIL; see '+str(run/'unity.log'))
    print('PASS native effect geometry, clock, and pointer635; '+str(len(variants))+' variants')
if __name__=='__main__':main()
