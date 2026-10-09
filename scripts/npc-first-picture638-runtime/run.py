#!/usr/bin/env python3
"""Exercise exact captured native originals through the actual avatar receive queue.

This focused challenge differs from632: the owner is already warm while a new
observer lacks its named complete baseline; continuous fast capture runs while
fragment delivery is in flight. Clocked native source fields and the exact
receiver coalescer compile from production. Equivalent editor TMP atlas remains
an explicit limitation. --expect-baseline-stall records the unmodified637 defect
without misreporting it as a fixed latency result.
"""
import argparse, hashlib, importlib.util, json, os, shutil, subprocess, tempfile
from pathlib import Path
ROOT = Path(__file__).resolve().parents[2]

def module(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    obj = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(obj)
    return obj

def receiver_sources(root, loader):
    path = root / 'src/GloomhavenVR/Net/Avatar/NetAvatarDriver.TownServices.cs'
    text = path.read_text()
    # Retain actual packet fields and bounded dictionaries verbatim. The test only
    # exposes entry/flush methods; their sorting/coalescing/admission logic stays real.
    start = text.index('    private sealed class TownPacket')
    end = text.index('    private readonly byte[] _activityBuffer', start)
    scaffold = ('using System; using UnityEngine; using GloomhavenVR.WorldUI; '
        'using GloomhavenVR.Core; using System.Collections.Generic; '
        'using GloomhavenVR.Net.TownServices; namespace GloomhavenVR.Net; '
        'internal sealed partial class NetAvatarDriver {\n' + text[start:end] + '\n')
    for signature in ['private bool QueueTownService(int sender, byte[] bytes, int length)',
                      'private bool QueueTownMotion(int sender, byte[] bytes, int length)',
                      'private void ApplyTownServices()']:
        scaffold += loader.method(text, signature) + '\n'
    scaffold += ('internal bool FixtureQueue638(int peer, byte[] bytes) => QueueTownService(peer, bytes, bytes.Length);\n'
                 'internal bool FixtureQueueMotion638(int peer, byte[] bytes) => QueueTownMotion(peer, bytes, bytes.Length);\n'
                 'internal void FixtureApply638() => ApplyTownServices();\n}\n')
    return scaffold, hashlib.sha256(text.encode()).hexdigest()

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--source-root', type=Path, default=ROOT)
    p.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/npc638-first-picture')
    p.add_argument('--expect-baseline-stall', action='store_true')
    p.add_argument('--case', choices=['dependency','lifecycle','template'], default='dependency')
    args = p.parse_args(); root = args.source_root.resolve(); args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    loader = module('mirror638', root / 'scripts/check-town-service-mirror.py')
    delivery = module('delivery638', root / 'scripts/check-town-native-state623.py')
    bound, _ = loader.sources(root); delivery.bind_delivery_transport(root, bound, loader)
    if args.case == 'lifecycle' and args.expect_baseline_stall:
        current='if (!sourceEntry.Complete && !prewarm && cloneOf == null'
        historical='if (!prewarm && cloneOf == null'
        if bound['PublisherNative.cs'].count(current)!=1: raise RuntimeError('Historical hidden-original guard binding drift')
        bound['PublisherNative.cs']=bound['PublisherNative.cs'].replace(current,historical,1)
        (run/'controlled-hidden-retirement.txt').write_text('Only the historical pre638 hidden-source guard is restored; all current receiver types and reveal dependency assertions remain.\n')
    receiver, receiver_hash = receiver_sources(root, loader)
    bound['ActualReceiver638.cs'] = receiver
    fixture = run / 'fixture'; shutil.copytree(root / 'scripts/town-service-mirror-runtime', fixture)
    for name in ['NativeState623.cs', 'NativeDelivery629.cs']:
        shutil.copyfile(root / 'scripts/town-native-state623-runtime' / name, fixture / name)
    shutil.copyfile(root / 'scripts/town-first-picture632-runtime/FirstPicture632.cs', fixture / 'FirstPicture632.cs')
    shutil.copyfile(Path(__file__).with_name('FirstPicture638.cs'), fixture / 'FirstPicture638.cs')
    boundaries = fixture / 'Boundaries.cs'; text = boundaries.read_text()
    text = text.replace('internal static bool WantsDebug => false;', 'internal static bool WantsDebug => true;', 1)
    text = text.replace('internal static void Info(string channel, string message) { }',
        'internal static void Info(string channel, string message) => Messages.Add(channel + ": " + message);', 1)
    boundaries.write_text(text)
    program = fixture / 'Program.cs'; text = program.read_text()
    anchor = '            if (variant == "production") PublisherNoCloth();'
    if text.count(anchor) != 1: raise RuntimeError('Runtime fixture entry drift')
    text = text.replace(anchor, '''
            if (suite == "first-picture638-lifecycle") {
                FirstPictureLifecycle638();
                File.WriteAllText(Path.Combine(_output,"assertions.txt"),_assertions+" assertions\\n"); yield break;
            }
            if (suite == "first-picture638" || suite == "first-picture638-template") {
                var proof = FirstPicture638(suite == "first-picture638-template"); while (proof.MoveNext()) yield return proof.Current;
                File.WriteAllText(Path.Combine(_output,"assertions.txt"),_assertions+" assertions\\n"); yield break;
            }
''' + anchor, 1)
    program.write_text(text)
    production = run / 'production'; production.mkdir()
    for name, text in bound.items(): (production / name).write_text(text)
    project = run / 'Mirror.csproj'; shutil.copyfile(fixture / 'Mirror.csproj', project)
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    unity = Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity'))
    command = [dotnet, 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet',
        '-p:CaseName=FirstPicture638', '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production),
        '-p:UnityManaged=' + str(unity.parent / 'Data/Managed'),
        '-p:UnityUi=' + str(root / 'ressources/GH_Data/Managed/UnityEngine.UI.dll'),
        '-p:UnityTmp=' + str(root / 'ressources/GH_Data/Managed/Unity.TextMeshPro.dll')]
    result = subprocess.run(command, capture_output=True, text=True)
    (run / 'build.log').write_text(result.stdout + result.stderr)
    if result.returncode: raise SystemExit(result.stdout + result.stderr)
    editor = run / 'unity'; (editor / 'Assets/Editor').mkdir(parents=True)
    shutil.copyfile(fixture / 'Editor/MirrorRunner.cs', editor / 'Assets/Editor/MirrorRunner.cs')
    (editor / 'Packages').mkdir(); (editor / 'ProjectSettings').mkdir()
    (editor / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.physics":"1.0.0"}}\n')
    (editor / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    python = os.environ.get('UNITYPY_PYTHON', str(Path.home() / 'unitypy-venv/bin/python'))
    subprocess.run([python, str(root / 'scripts/town-first-picture632-runtime/export-native.py'),
        str(root / 'ressources/GH_Data'), str(editor / 'Assets/NativeFirstPicture632')], check=True)
    hashes = {name: hashlib.sha256(text.encode()).hexdigest() for name, text in bound.items()}
    hashes['NetAvatarDriver.TownServices.cs (full source)'] = receiver_hash
    hashes['638 fixture runner'] = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
    for name in ['FirstPicture638.cs','FirstPicture632.cs','NativeState623.cs','NativeDelivery629.cs','Boundaries.cs','Program.cs']:
        hashes['fixture/' + name] = hashlib.sha256((fixture / name).read_bytes()).hexdigest()
    (run / 'source-hashes.json').write_text(json.dumps(hashes, indent=2) + '\n')
    manifest = {'suite': 'first-picture638-' + args.case if args.case != 'dependency' else 'first-picture638', 'evidence': str(run), 'result': str(run / 'results.txt'),
        'cases': [{'name': 'production', 'dll': str(run / 'bin/Release/netstandard2.1/FirstPicture638.dll'),
            'expected': ('hidden published dynamic original retains its registered module and named baseline' if args.case == 'lifecycle' else 'cold observer gets all named original dependencies within1s') if args.expect_baseline_stall else ''}]}
    path = run / 'manifest.json'; path.write_text(json.dumps(manifest, indent=2) + '\n')
    print('Evidence: ' + str(run), flush=True)
    result = subprocess.run(['xvfb-run', '-a', str(unity), '-batchmode', '-force-glcore',
        '-projectPath', str(editor), '-executeMethod', 'MirrorRunner.Start', '-mirrorManifest', str(path),
        '-logFile', str(run / 'unity.log')], timeout=240)
    if Path(manifest['result']).exists(): print(Path(manifest['result']).read_text(), end='')
    if result.returncode or not Path(manifest['result']).exists(): raise SystemExit('FAIL638 challenge: ' + str(run / 'unity.log'))
    print('PASS638 focused challenge; retained proof: ' + str(run))
if __name__ == '__main__': main()
