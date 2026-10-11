#!/usr/bin/env python3
"""Build the separate original-actor derivative bank with the exact Unity 2021.3.5 editor."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT/'.planning/debug/frame606-figure-mesh')
    parser.add_argument('--unity', type=Path, default=Path('/home/claw/unity-2021.3.5/Editor/Unity'))
    parser.add_argument('--unitypy-python', type=Path, default=Path('/home/claw/unitypy-venv/bin/python'))
    parser.add_argument('--skip-extract', action='store_true')
    parser.add_argument('--validate', action='store_true', help='Verify all existing generated assets against immutable source streams and survivor maps')
    parser.add_argument('--repack', action='store_true', help='Only rebuild parts/index from already generated immutable meshes')
    args = parser.parse_args(); root = args.source_root.resolve(); out = args.output_dir.resolve()
    out.mkdir(parents=True, exist_ok=True)
    if not args.skip_extract and not args.repack and not args.validate:
        subprocess.run([str(args.unitypy_python), str(root/'tools/figure-mesh/export-native.py'),
                        '--source-root', str(root), '--output-dir', str(out/'native')], check=True)
    boundaries = out/'Boundaries.cs'
    boundaries.write_text('namespace GloomhavenVR.Core { internal static class VRLog { internal static bool WantsDebug => false; internal static void Debug(string a,string b) { } internal static void Note(string a,string b) { } } }\n')
    project = root/'unity/GloomhavenVR.FigureMeshes'
    (project/'Assets/Editor').mkdir(parents=True, exist_ok=True)
    subprocess.run([shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet'),'build',str(root/'tools/figure-mesh/Generator.csproj'),'-c','Release','--nologo','--verbosity','quiet',
        '-p:GeneratorSource='+str(root/'tools/figure-mesh'), '-p:RuntimeSource='+str(root/'src/GloomhavenVR/Core/Perf'),
        '-p:GeneratedBoundaries='+str(boundaries),'-p:UnityManaged='+str(args.unity.parent/'Data/Managed'),'-o',str(out/'generator')],check=True)
    shutil.copyfile(out/'generator/Generator.dll',project/'Assets/Editor/Generator.dll')
    command=[str(args.unity),'-batchmode','-nographics','-projectPath',str(project),'-executeMethod','FigureMeshGenerator.Validate' if args.validate else 'FigureMeshGenerator.Repack' if args.repack else 'FigureMeshGenerator.Build',
             '-nativeMeshInput',str(out/'native'),'-nativeMeshOutput',str(out/'bank'),'-logFile',str(out/'build.log')]
    subprocess.run(command,check=True,timeout=3600)
    if args.validate:
        print((out/'bank/validation.json').read_text(), end='')
        return
    bundles=sorted((out/'bank').glob('ghvr-figure-meshes-*.bundle'))
    if not bundles: raise SystemExit('No generated bundle: see build.log')
    sources=json.loads((out/'native/sources.json').read_text()); results=json.loads((out/'bank/results.json').read_text())
    manifest={'format':1,'editor':'2021.3.5f1','banks':{bank.name:hashlib.sha256(bank.read_bytes()).hexdigest() for bank in bundles},
              'source_meshes':len(sources['meshes']),'derivatives':len(results),'sources':sources['meshes'],'results':results}
    (out/'manifest.json').write_text(json.dumps(manifest,indent=2)+'\n')
    legacy=root/'prebuilt/ghvr-figure-meshes.bundle'
    if legacy.exists(): legacy.unlink()
    for old in (root/'prebuilt').glob('ghvr-figure-meshes-*.bundle'): old.unlink()
    for bank in bundles:
        if bank.stat().st_size>=100*1024*1024: raise SystemExit('Git single-object limit exceeded: '+bank.name)
        shutil.copyfile(bank,root/'prebuilt'/bank.name)
    shutil.copyfile(out/'bank/ghvr-figure-meshes-index.json',root/'prebuilt/ghvr-figure-meshes-index.json')
    # Small checked-in provenance is sufficient; source vertices remain in ignored local
    # evidence and are regenerated from the licensed game's read-only bundles.
    summary={k:v for k,v in manifest.items() if k not in ('sources','results')}
    summary['source_bundles']={m['bundle']:m['bundle_sha256'] for m in sources['meshes']}
    summary['meshes']=[{k:v for k,v in record.items() if k!='sourceFile'} for record in results]
    (root/'tools/figure-mesh/manifest.json').write_text(json.dumps(summary,indent=2)+'\n')
    print(json.dumps({'source_meshes':summary['source_meshes'],'derivatives':summary['derivatives'],'parts':len(summary['banks']),'total_bytes':sum(bank.stat().st_size for bank in bundles)},indent=2))

if __name__=='__main__': main()
