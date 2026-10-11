#!/usr/bin/env python3
"""Execute both complete held-figure cloth drivers and physical OFF controls in Unity 2021.3.5."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root',type=Path,default=ROOT)
    parser.add_argument('--output-dir',type=Path,default=ROOT/'.planning/debug/figure-cloth-off-runtime')
    parser.add_argument('--unity',type=Path,default=Path(os.environ.get('UNITY_PATH','/home/claw/unity-2021.3.5/Editor/Unity')))
    parser.add_argument('--no-negative-controls',action='store_true')
    args=parser.parse_args()
    if not args.unity.is_file():parser.error('Actual Unity 2021.3.5 is required; physical cloth evidence cannot silently skip')
    args.output_dir.mkdir(parents=True,exist_ok=True)
    run=Path(tempfile.mkdtemp(prefix='run-',dir=args.output_dir.resolve()))
    fixture=ROOT/'scripts/figure-cloth-off-runtime'
    base=args.source_root/'src/GloomhavenVR/Board/FigureGrab'
    sources={name:(base/name).read_text() for name in ('FigureCloth.cs','FigureClothHands.cs')}
    (run/'source-hashes.json').write_text(json.dumps({'root':str(args.source_root.resolve()),'sha256':{name:hashlib.sha256(text.encode()).hexdigest() for name,text in sources.items()}},indent=2)+'\n')
    variants=[('production','','','','')]
    if not args.no_negative_controls:
        variants += [
            ('discover-while-off','FigureCloth.cs','internal static void Note(GameObject? root, float factor)\n    {\n        if (SimulationDisabled) { StopForDisabledSimulation(); return; }','internal static void Note(GameObject? root, float factor)\n    {','cold OFF skips hierarchy discovery'),
            ('cook-while-off','FigureCloth.cs','internal static void Tick()\n    {\n        if (SimulationDisabled) { StopForDisabledSimulation(); return; }','internal static void Tick()\n    {','OFF abandons both live and settled cook'),
            ('enable-managed-off','FigureCloth.cs','_disabledManaged[c] = t.Root;\n                c.enabled = false;','_disabledManaged[c] = t.Root;\n                c.enabled = true;','OFF abandons both live and settled cook'),
            ('lose-pristine-coefficients','FigureCloth.cs','c.coefficients = t.Pristine[i];','if (PerfConfig.FigureClothSimulationEnabled) c.coefficients = t.Pristine[i];','OFF restores pristine per-vertex coefficients'),
            ('lose-native-stiffness','FigureCloth.cs','c.stretchingStiffness = t.PristineStretch[i];','if (PerfConfig.FigureClothSimulationEnabled) c.stretchingStiffness = t.PristineStretch[i];','OFF restores authored stretching stiffness'),
            ('retain-rest-scale-records','FigureCloth.cs','FigureClothHands.Clear();\n        _tracked.Clear();','FigureClothHands.Clear();\n        if (PerfConfig.FigureClothSimulationEnabled) _tracked.Clear();','OFF restores native sphere pairs'),
            ('lose-enabled-ownership','FigureCloth.cs','_disabledManaged[c] = t.Root;','if (PerfConfig.FigureClothSimulationEnabled) _disabledManaged[c] = t.Root;','OFF passes original simulating claim'),
            ('clear-reenables-off','FigureCloth.cs','if (SimulationDisabled)\n        {\n            StopForDisabledSimulation();','if (SimulationDisabled && PerfConfig.FigureClothSimulationEnabled)\n        {\n            StopForDisabledSimulation();','Clear during OFF never drains cook'),
            ('sample-hands-off','FigureClothHands.cs','if (VRSession.IsRunning && !PerfConfig.FigureClothSimulationEnabled)','if (VRSession.IsRunning && PerfConfig.FigureClothSimulationEnabled && !PerfConfig.FigureClothSimulationEnabled)','cold OFF skips local held and free-hand sampling'),
            ('retain-invisible-probe','FigureClothHands.cs','Object.Destroy(_probe);','if (PerfConfig.FigureClothSimulationEnabled) Object.Destroy(_probe);','OFF destroys owned hand probe'),
            ('cancel-native-reset','FigureCloth.cs','if (!forcing) c.enabled = true;','if (actor != null || !forcing) c.enabled = true;','ON never cancels native two-frame forced-position reset'),
            ('miss-late-on-capture','FigureCloth.cs','else if (t.Suspended && t.Cloths.Count == 0 && t.FoundCloths > 0','else if (t.Suspended && t.Cloths.Count == 0 && t.FoundCloths < 0','ON late handover retries unchanged held factor'),
        ]
    manifest={'result':str(run/'results.txt'),'cases':[]}
    dotnet=shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    for name,filename,before,after,expected in variants:
        build=run/name;production=build/'production';production.mkdir(parents=True)
        for path,text in sources.items():
            if path==filename:
                # Probe destruction also occurs when replacing a broken probe. Mutate the first
                # occurrence in Clear only, keeping the helper's failure recovery independent.
                expected_count=2 if name=='retain-invisible-probe' else 1
                if text.count(before)!=expected_count:raise SystemExit('Production mutation binding drift: '+name)
                text=text.replace(before,after,1)
            (production/path).write_text(text)
        project=build/'Cloth.csproj';shutil.copyfile(fixture/'Cloth.csproj',project)
        assembly='FigureClothOff_'+name.replace('-','_')
        result=subprocess.run([dotnet,'build',str(project),'-c','Release','--nologo','--verbosity','quiet','-p:CaseName='+assembly,'-p:FixtureDir='+str(fixture),'-p:ProductionDir='+str(production),'-p:UnityManaged='+str(args.unity.parent/'Data/Managed')],capture_output=True,text=True)
        (build/'build.log').write_text(result.stdout+result.stderr)
        if result.returncode:raise SystemExit(result.stdout+result.stderr+'\nCompilation failure is not a passing negative control')
        manifest['cases'].append({'name':name,'dll':str(build/'bin/Release/netstandard2.1'/(assembly+'.dll')),'expected':expected})
    manifest_path=run/'manifest.json';manifest_path.write_text(json.dumps(manifest,indent=2))
    project=run/'unity';(project/'Assets/Editor').mkdir(parents=True);(project/'Packages').mkdir();(project/'ProjectSettings').mkdir()
    shutil.copyfile(fixture/'Editor/ClothRunner.cs',project/'Assets/Editor/ClothRunner.cs')
    (project/'Packages/manifest.json').write_text('{"dependencies":{"com.unity.modules.physics":"1.0.0","com.unity.modules.cloth":"1.0.0","com.unity.modules.animation":"1.0.0"}}\n')
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    result=subprocess.run([str(args.unity),'-batchmode','-nographics','-projectPath',str(project),'-executeMethod','ClothRunner.Start','-interactionManifest',str(manifest_path),'-logFile',str(run/'unity.log')],stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT,timeout=300)
    report=Path(manifest['result'])
    if report.is_file():print(report.read_text(),end='')
    if result.returncode or not report.is_file():raise SystemExit('FAIL: Unity physical cloth run; see '+str(run/'unity.log'))
    print('PASS: '+str(len(variants))+' complete production/negative variants; evidence: '+str(run))

if __name__=='__main__':main()
