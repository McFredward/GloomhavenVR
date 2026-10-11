#!/usr/bin/env python3
"""Measure cold native first-picture capture, exact pooling and actual Unity playback.

Uses the game's serialized26-node enhancement row, two owner languages and a
44-visible/66-prepared transaction. Other global streams retain their real
capacity and the unchanged864-byte/50ms scheduler. The game's source font atlas
is not imported into the editor: provenance explicitly records the equivalent
editor TMP atlas limitation. No headset or latency guarantee follows from this
lossless transport scheduling proof.
"""
import argparse, hashlib, importlib.util, json, os, shutil, subprocess, tempfile
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
def module(name,path):
    spec=importlib.util.spec_from_file_location(name,path);m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--source-root',type=Path,default=ROOT)
    p.add_argument('--output-dir',type=Path,default=ROOT/'.planning/debug/town-first-picture632')
    p.add_argument('--no-negative-controls',action='store_true');args=p.parse_args();root=args.source_root.resolve()
    args.output_dir.mkdir(parents=True,exist_ok=True);run=Path(tempfile.mkdtemp(prefix='run-',dir=args.output_dir.resolve()))
    loader=module('town632_loader',root/'scripts/check-town-service-mirror.py');delivery=module('town632_delivery',root/'scripts/check-town-native-state623.py')
    bound,hashes=loader.sources(root);delivery.bind_delivery_transport(root,bound,loader)
    value_pool=root/'src/GloomhavenVR/Net/TownServices/TownServiceCodec.OriginalValuePool.cs';bound[value_pool.name]=value_pool.read_text()
    fixture=run/'fixture';shutil.copytree(root/'scripts/town-service-mirror-runtime',fixture)
    boundaries=fixture/'Boundaries.cs';text=boundaries.read_text()
    if 'ExtIdTownOriginalValuePool' not in text:text=text.replace('internal const byte ExtIdTownVisibleCensus = 108;','internal const byte ExtIdTownVisibleCensus = 108;\n        internal const byte ExtIdTownOriginalValuePool = 110;')
    text=text.replace("internal static bool WantsDebug => false;", "internal static bool WantsDebug => true;",1)
    text=text.replace("internal static void Info(string channel, string message) { }", "internal static void Info(string channel, string message) => Messages.Add(channel + \": \" + message);",1)
    boundaries.write_text(text)
    for source in ['NativeState623.cs','NativeDelivery629.cs']:
        shutil.copyfile(root/'scripts/town-native-state623-runtime'/source,fixture/source)
    shutil.copyfile(root/'scripts/town-first-picture632-runtime/FirstPicture632.cs',fixture/'FirstPicture632.cs')
    program=fixture/'Program.cs';text=program.read_text();anchor='            if (variant == "production") PublisherNoCloth();'
    if text.count(anchor)!=1:raise RuntimeError('Actual mirror entry drift')
    branch='''\n            if (suite == "first-picture632") {
                var proof = FirstPicture632(); while (proof.MoveNext()) yield return proof.Current;
                File.WriteAllText(Path.Combine(_output,"assertions.txt"),_assertions+" assertions\\n"); yield break;
            }\n'''
    program.write_text(text.replace(anchor,anchor+branch,1))
    variants=[('production',None,None,None,'')]
    if not args.no_negative_controls:
        # Revert only the old actual queue method, not codec, transport budgets or
        # picture-ready rules. This reproduces independent12KiB value tables filling
        # each uncompressed58KiB envelope before transport compression.
        baseline=(root/'scripts/town-first-picture632-runtime/IndependentOriginalQueue629.cs.txt').read_text()
        current=loader.method(bound['TownServiceSendQueue.cs'],'private byte[]? TakeBundle(double now, bool urgent)')
        old=loader.method(baseline,'private byte[]? TakeBundle(double now, bool urgent)')
        variants.append(('independent-value-tables','TownServiceSendQueue.cs',current,old,'full original first picture arrives within1s without a template repair wait'))
        variants.append(('active-is-not-ink','TownServiceBinding.cs','    internal bool HasVisibleOutput()\n    {','    internal bool HasVisibleOutput()\n    {\n        return true;\n#pragma warning disable CS0162','active but disabled original graphics do not gate first picture'))
    dotnet=shutil.which('dotnet')or str(Path.home()/'.dotnet/dotnet');unity=Path(os.environ.get('UNITY_PATH','/home/claw/unity-2021.3.5/Editor/Unity'))
    manifest={'suite':'first-picture632','result':str(run/'results.txt'),'evidence':str(run),'cases':[]}
    for name,filename,before,after,expected in variants:
        build=run/name;production=build/'production';production.mkdir(parents=True)
        for path,text in bound.items():
            if path==filename:
                if text.count(before)!=1:raise RuntimeError('Negative control source drift: '+name)
                text=text.replace(before,after,1)
                if name == 'independent-value-tables':
                    # Restore the old separate census send as well: its old bundle
                    # method has no atomic-census consumer.
                    text=text.replace('byte[]? census = BundleColdManifest ? null : NextManifest(now);','byte[]? census = NextManifest(now);',1)
            (production/path).write_text(text)
        project=build/'Mirror.csproj';shutil.copyfile(fixture/'Mirror.csproj',project);assembly='TownFirstPicture632_'+name.replace('-','_')
        result=subprocess.run([dotnet,'build',str(project),'-c','Release','--nologo','--verbosity','quiet',
            '-p:CaseName='+assembly,'-p:FixtureDir='+str(fixture),'-p:ProductionDir='+str(production),
            '-p:UnityManaged='+str(unity.parent/'Data/Managed'),'-p:UnityUi='+str(root/'ressources/GH_Data/Managed/UnityEngine.UI.dll'),
            '-p:UnityTmp='+str(root/'ressources/GH_Data/Managed/Unity.TextMeshPro.dll')],capture_output=True,text=True)
        (build/'build.log').write_text(result.stdout+result.stderr)
        if result.returncode:raise SystemExit(result.stdout+result.stderr)
        manifest['cases'].append({'name':name,'dll':str(build/'bin/Release/netstandard2.1'/(assembly+'.dll')),'expected':expected})
        print('Compiled '+name,flush=True)
    project=run/'unity';(project/'Assets/Editor').mkdir(parents=True)
    shutil.copyfile(fixture/'Editor/MirrorRunner.cs',project/'Assets/Editor/MirrorRunner.cs')
    (project/'Packages').mkdir();(project/'ProjectSettings').mkdir()
    (project/'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.physics":"1.0.0"}}\n')
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    exporter=root/'scripts/town-first-picture632-runtime/export-native.py'
    exporter_python=os.environ.get('UNITYPY_PYTHON',str(Path.home()/'unitypy-venv/bin/python'))
    subprocess.run([exporter_python,str(exporter),str(root/'ressources/GH_Data'),str(project/'Assets/NativeFirstPicture632')],check=True)
    hashes={name:hashlib.sha256(text.encode()).hexdigest()for name,text in bound.items()}
    (run/'source-hashes.json').write_text(json.dumps(hashes,indent=2)+'\n')
    path=run/'manifest.json';path.write_text(json.dumps(manifest,indent=2));print('Evidence: '+str(run),flush=True)
    result=subprocess.run(['xvfb-run','-a',str(unity),'-batchmode','-force-glcore','-projectPath',str(project),
        '-executeMethod','MirrorRunner.Start','-mirrorManifest',str(path),'-logFile',str(run/'unity.log')],timeout=300)
    if Path(manifest['result']).exists():print(Path(manifest['result']).read_text(),end='')
    if result.returncode or not Path(manifest['result']).exists():raise SystemExit('FAIL native first picture; see '+str(run/'unity.log'))
    print('PASS actual cold original first-picture scheduling and visible publication: '+str(run))
if __name__=='__main__':main()
