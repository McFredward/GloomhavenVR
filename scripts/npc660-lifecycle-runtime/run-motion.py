#!/usr/bin/env python3
"""Run the exact production native interpolation in Unity, including anchor side effects."""
import argparse, hashlib, json, os, shutil, subprocess, tempfile
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2]

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/npc660-lifecycle/motion')
    parser.add_argument('--control', choices=['layout', 'parent'], help='Omit exactly one repaired production seam')
    args = parser.parse_args()
    root = args.source_root.resolve()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    unity = Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity'))
    production = run / 'production'; production.mkdir()
    source = root / 'src/GloomhavenVR/Net/TownServices/TownServiceMotion.cs'
    text = source.read_text()
    expected = ''
    if args.control == 'layout':
        anchor = 'if (from.Position != to.Position || layoutMovesPosition)'
        if text.count(anchor) != 1: raise RuntimeError('Layout control binding drift')
        text = text.replace(anchor, 'if (from.Position != to.Position)')
        expected = 'native layout interpolation preserves the separately authored root position on every render'
    elif args.control == 'parent':
        start = text.index('    internal void Reparent(Transform? parent)')
        end = text.index('    private static State ReparentState(', start)
        text = text[:start] + '''    internal void Reparent(Transform? parent)
    { _nodes[0].Transform.SetParent(parent, true); }
''' + text[end:]
        expected = 'retained original continues its exact authored world path after a census mount retires'
    (production / source.name).write_text(text)
    fixture = run / 'fixture'; fixture.mkdir()
    shutil.copyfile(Path(__file__).with_name('MotionLayout660.cs'), fixture / 'MotionLayout660.cs')
    project = run / 'Motion660.csproj'
    project.write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<TargetFramework>netstandard2.1</TargetFramework><LangVersion>latest</LangVersion><Nullable>enable</Nullable>
<NoWarn>CS8625</NoWarn>
<GenerateDocumentationFile>false</GenerateDocumentationFile>
<EnableDefaultCompileItems>false</EnableDefaultCompileItems><AssemblyName>Motion660</AssemblyName>
</PropertyGroup><ItemGroup><Compile Include="fixture/*.cs"/><Compile Include="production/*.cs"/>
<Reference Include="$(UnityManaged)/UnityEngine/*.dll"/>
<Reference Include="UnityEngine.UI"><HintPath>$(UnityUi)</HintPath><Private>false</Private></Reference>
</ItemGroup></Project>''')
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    command = [dotnet, 'build', str(project), '-c', 'Release', '--nologo', '-v:q',
        '-p:UnityManaged=' + str(unity.parent / 'Data/Managed'),
        '-p:UnityUi=' + str(root / 'ressources/GH_Data/Managed/UnityEngine.UI.dll')]
    result = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (run / 'build.log').write_text(result.stdout)
    if result.returncode: raise SystemExit(result.stdout)
    editor = run / 'unity'; (editor / 'Assets/Editor').mkdir(parents=True)
    (editor / 'Packages').mkdir(); (editor / 'ProjectSettings').mkdir()
    runner = Path(__file__).with_name('EditorRunner.cs')
    shutil.copyfile(runner, editor / 'Assets/Editor/EditorRunner.cs')
    (editor / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}\n')
    (editor / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    (run / 'source-hashes.json').write_text(json.dumps({str(path): hashlib.sha256(path.read_bytes()).hexdigest()
        for path in [source, Path(__file__).with_name('MotionLayout660.cs'), runner, Path(__file__)]}, indent=2) + '\n')
    (run / 'variant.json').write_text(json.dumps({'control': args.control, 'expected_failure': expected,
        'compiled_source_sha256': hashlib.sha256(text.encode()).hexdigest()}, indent=2) + '\n')
    print('Evidence: ' + str(run), flush=True)
    result = subprocess.run(['xvfb-run', '-a', str(unity), '-batchmode', '-force-glcore', '-projectPath', str(editor),
        '-executeMethod', 'LifecycleRunner660.Start', '-evidence660', str(run),
        '-expected660', expected,
        '-dll660', str(run / 'bin/Release/netstandard2.1/Motion660.dll'), '-logFile', str(run / 'unity.log')], timeout=180)
    receipt = run / 'result.txt'
    if receipt.exists(): print(receipt.read_text(), end='')
    if result.returncode or not receipt.exists(): raise SystemExit('FAIL Unity: ' + str(run / 'unity.log'))

if __name__ == '__main__': main()
