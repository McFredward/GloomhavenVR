#!/usr/bin/env python3
"""Exercise actual native figure derivatives, topology invariants and extant ghost restoration."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT=Path(__file__).resolve().parents[1]

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root',type=Path,default=ROOT)
    parser.add_argument('--output-dir',type=Path,default=ROOT/'.planning/debug/figure-mesh-runtime')
    parser.add_argument('--unity',type=Path,default=Path(os.environ.get('UNITY_PATH','/home/claw/unity-2021.3.5/Editor/Unity')))
    parser.add_argument('--native-dir',type=Path)
    parser.add_argument('--no-negative-controls',action='store_true')
    args=parser.parse_args();root=args.source_root.resolve()
    if not args.unity.is_file():parser.error('Actual Unity 2021.3.5 is required')
    args.output_dir.mkdir(parents=True,exist_ok=True);run=Path(tempfile.mkdtemp(prefix='run-',dir=args.output_dir.resolve()))
    native=args.native_dir
    if native is None:
        native=run/'native'
        subprocess.run([str(Path.home()/'unitypy-venv/bin/python'),str(root/'tools/figure-mesh/export-native.py'),
            '--source-root',str(root),'--output-dir',str(native),'--only','npc_winddemon_assets_all','npc_sundemon_assets_all','hero_berserker_assets_all','npc_spittingdrake_assets_all'],check=True)
    sources={
      'Bank.cs':(root/'src/GloomhavenVR/Core/Perf/ScenarioFigureMeshBank.cs').read_text(),
      'Topology.cs':(root/'tools/figure-mesh/ScenarioFigureMeshTopology.cs').read_text(),
      'Stream.cs':(root/'tools/figure-mesh/NativeFigureMeshStream.cs').read_text(),
      'Mirror.cs':(root/'src/GloomhavenVR/Board/FigureGrab/FigureVisualMirror.cs').read_text(),
    }
    boundary="""
namespace GloomhavenVR.Core
{
 internal static class VRLog { internal static bool WantsDebug => true; internal static void Debug(string scope,string text) { UnityEngine.Debug.Log(scope+": "+text); } internal static void Note(string scope,string text) { UnityEngine.Debug.LogWarning(scope+": "+text); } }
}

namespace GloomhavenVR.Core { internal static class VRLayers { internal const string ModOwnedNamePrefix="VR_"; } }
namespace GloomhavenVR.Board.FigureGrab
{
 internal static class FigureOverlay
 {
  internal static void CopyBlendShapeWeights(UnityEngine.SkinnedMeshRenderer a,UnityEngine.SkinnedMeshRenderer b)
  { for(int i=0;i<a.sharedMesh.blendShapeCount;i++)b.SetBlendShapeWeight(i,a.GetBlendShapeWeight(i)); }
  internal static void MatchCloneWorldScale(UnityEngine.Transform a,UnityEngine.Transform parent,UnityEngine.Transform source)
  { a.localScale=source.lossyScale; }
 }
}
"""
    fixture=root/'scripts/scenario-figure-mesh-runtime'
    variants=[('production','','','','')]
    if not args.no_negative_controls:
        variants += [
          ('retain-all-geometry','Topology.cs','(int)Math.Ceiling(remaining * fraction)','(int)Math.Ceiling(remaining * 1f)','actual topology must materially reduce'),
          ('lose-skin-weights','Topology.cs','result.boneWeights = Copy(weights, originalVertices);','result.boneWeights = new BoneWeight[originalVertices.Length];','every survivor retains exact original'),
          ('delete-triangles','Topology.cs','if (!face.Removed && face.Material == sub)','if (!face.Removed && face.Material == sub && face.A % 7 != 0)','small closed limb shells may never collapse'),
          ('collapse-terminal-limbs','Topology.cs','if (!other.Removed && !other.Contains(remove) && other.Contains(ia)','if (other.Removed && !other.Contains(remove) && other.Contains(ia)','small closed limb shells may never collapse'),
          ('retain-stale-ghost','Mirror.cs','if (meshCopy.sharedMesh != meshSource.sharedMesh) meshCopy.sharedMesh = meshSource.sharedMesh;','if (meshCopy.sharedMesh != meshSource.sharedMesh) meshCopy.quality = meshSource.quality;','existing local home ghost follows exact original'),
          ('overwrite-foreign-slot','Bank.cs','if (current != (_applied ?? Original))','if (current == null)','foreign native shared-mesh changes are never overwritten'),
        ]
    manifest={'result':str(run/'results.txt'),'cases':[]}
    dotnet=shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    for name,filename,before,after,expected in variants:
        build=run/name;production=build/'production';production.mkdir(parents=True)
        (production/'Boundaries.cs').write_text(boundary)
        for path,text in sources.items():
            if path==filename:
                if text.count(before)!=1:raise SystemExit('Mutation binding drift: '+name)
                text=text.replace(before,after,1)
            (production/path).write_text(text)
        project=build/'Figures.csproj';shutil.copyfile(root/'scripts/scenario-figure-detail-runtime/Figures.csproj',project)
        assembly='NativeFigureMesh_'+name.replace('-','_')
        result=subprocess.run([dotnet,'build',str(project),'-c','Release','--nologo','--verbosity','quiet',
            '-p:CaseName='+assembly,'-p:FixtureDir='+str(fixture),'-p:ProductionDir='+str(production),
            '-p:UnityManaged='+str(args.unity.parent/'Data/Managed')],capture_output=True,text=True)
        (build/'build.log').write_text(result.stdout+result.stderr)
        if result.returncode:raise SystemExit(result.stdout+result.stderr+'Compilation failure is not a passing negative control')
        dll=build/'bin/Release/netstandard2.1'/(assembly+'.dll')
        for bank in (root/'prebuilt').glob('ghvr-figure-meshes-*'):
            if bank.suffix in ('.bundle','.json'):shutil.copyfile(bank,dll.parent/bank.name)
        manifest['cases'].append({'name':name,'dll':str(dll),'expected':expected})
    manifest_path=run/'manifest.json';manifest_path.write_text(json.dumps(manifest,indent=2))
    hashes={path:hashlib.sha256(text.encode()).hexdigest() for path,text in sources.items()}
    hashes['banks']={bank.name:hashlib.sha256(bank.read_bytes()).hexdigest() for bank in (root/'prebuilt').glob('ghvr-figure-meshes-*') if bank.suffix in ('.bundle','.json')}
    (run/'source-hashes.json').write_text(json.dumps(hashes,indent=2)+'\n')
    project=run/'unity';(project/'Assets/Editor').mkdir(parents=True);(project/'Packages').mkdir();(project/'ProjectSettings').mkdir()
    shutil.copyfile(root/'scripts/scenario-scenery-runtime/Editor/InteractionRunner.cs',project/'Assets/Editor/InteractionRunner.cs')
    (project/'Packages/manifest.json').write_text('{"dependencies":{"com.unity.modules.physics":"1.0.0","com.unity.modules.cloth":"1.0.0","com.unity.modules.animation":"1.0.0","com.unity.modules.particlesystem":"1.0.0","com.unity.modules.assetbundle":"1.0.0"}}\n')
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    command=[str(args.unity),'-batchmode','-force-glcore','-projectPath',str(project),'-executeMethod','InteractionRunner.Start',
             '-interactionManifest',str(manifest_path),'-figureNativeDir',str(native.resolve()),'-figureNativeBundles',str((root/'ressources/GH_Data/StreamingAssets/aa/StandaloneWindows64').resolve()),'-logFile',str(run/'unity.log')]
    if not os.environ.get('DISPLAY'):
        xvfb=shutil.which('xvfb-run')
        if not xvfb:parser.error('xvfb-run required: real mesh render cannot silently skip')
        command=[xvfb,'-a','-s','-screen 0 640x480x24']+command
    result=subprocess.run(command,stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT,timeout=300)
    report=Path(manifest['result'])
    if report.is_file():print(report.read_text(),end='')
    if result.returncode or not report.is_file():raise SystemExit('FAIL real mesh runtime; see '+str(run/'unity.log'))
    print('PASS: '+str(len(variants))+' production/negative variants; evidence: '+str(run))

if __name__=='__main__':main()
