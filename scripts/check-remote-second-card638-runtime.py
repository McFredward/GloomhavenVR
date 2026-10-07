#!/usr/bin/env python3
"""Exercise real compact rig codec and both production card pose owners in Unity.

This receipt measures delivery authority, activation, scaling and motion rather
than native card artwork. A delayed Presence snapshot is injected between compact
rig samples, including a stale release/regrab. Negative controls must reach an
assertion; a compile error is never counted as causal evidence.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]

def method(source, signature):
    if source.count(signature) != 1:
        raise RuntimeError('Source binding drift: ' + signature)
    start = source.index(signature); opening = source.index('{', start)
    depth = 1; at = opening + 1
    while depth:
        depth += (source[at] == '{') - (source[at] == '}'); at += 1
    return source[start:at]

def bind(root):
    remote = (root / 'src/GloomhavenVR/Net/Remote/RemoteAvatar.cs').read_text()
    methods = [method(remote, marker) for marker in (
        '    private void AcceptSecondHeldCardRig(', '    private void AcceptSecondHeldCardExtras(',
        '    private static void UpdatePart(', '    private void UpdateCardSlab(', '    private float HeldSlabScale(')]
    # Call-site checks anchor the complete bound owners in their production receive/frame paths.
    assert 'AcceptSecondHeldCardRig(in state);' in method(remote, '    public void SetTarget(')
    assert 'AcceptSecondHeldCardExtras(in p);' in method(remote, '    public void SetExtras(')
    assert 'in _secondHeldCardPose, k,' in method(remote, '    public void Tick(')
    sampler = (root / 'src/GloomhavenVR/Net/Avatar/LocalRigSampler.cs').read_text()
    assert 'state.HasSecondHeldCardState = true;' in method(sampler, '    public static bool TrySample(')
    bound = {name: (root / ('src/GloomhavenVR/Net/' + path)).read_text() for name, path in (
        ('AvatarSerializer.cs', 'Avatar/AvatarSerializer.cs'), ('AvatarState.cs', 'Avatar/AvatarState.cs'),
        ('TownItemHeldSource.cs', 'TownItemHeldSource.cs'))}
    bound['AvatarSerializer637.cs'] = (ROOT / 'scripts/remote-second-card638-runtime/AvatarSerializer637.fixture').read_text().replace('class AvatarSerializer', 'class AvatarSerializer637')
    bound['RemoteAvatar.Motion.cs'] = 'using UnityEngine; using GloomhavenVR.Core; namespace GloomhavenVR.Net; internal sealed partial class RemoteAvatar {\n' + '\n'.join(methods) + '\n}'
    protocol = (root / 'src/GloomhavenVR/Net/NetProtocol.cs').read_text()
    names = sorted(set(re.findall(r'NetProtocol\.(\w+)', '\n'.join(bound.values()))) | {'InterpolationSharpness', 'HeldCardGripFirstBit', 'HeldCardGripSecondBit'})
    constants = []
    for name in names:
        if name == 'HeldFaceList':
            value = re.search(r'public static byte HeldFaceList\([^\n]+', protocol)
        else:
            value = re.search(r'public const (?:uint|byte|int|float) ' + re.escape(name) + r'\s*=\s*[^;]+;', protocol)
        if not value:
            raise RuntimeError('Missing bound wire member ' + name)
        constants.append(value[0])
    # HELD FACE decoding has its original shift dependency.
    for name in ('HeldFaceListShift',):
        if not any(' ' + name + ' ' in value for value in constants):
            constants.append(re.search(r'public const (?:byte|int) ' + name + r'\s*=\s*[^;]+;', protocol)[0])
    bound['ProtocolConstants.cs'] = 'namespace GloomhavenVR.Net; internal static class NetProtocol {\n' + '\n'.join(constants) + '\n}'
    return bound

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--no-negative-controls', action='store_true')
    args = parser.parse_args()
    parent = ROOT / '.planning/debug/remote-second-card638'; parent.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=parent)); bound = bind(args.source_root)
    (run / 'source-hashes.json').write_text(json.dumps({name: hashlib.sha256(text.encode()).hexdigest() for name,text in bound.items()}, indent=2) + '\n')
    variants = [('production', None, None, '')]
    if not args.no_negative_controls:
        variants += [
            ('secondary-still-extras', 'if (!state.HasSecondHeldCardState) return;', 'if (state.HasSecondHeldCardState) return;', 'same rig delivery keeps both cards equally smooth despite delayed extras'),
            ('stale-extras-overwrite', 'if (_hasAtomicSecondHeldCardState) return;', '// old extras overwrites compact rig authority', 'same rig delivery keeps both cards equally smooth despite delayed extras')]
    fixture = ROOT / 'scripts/remote-second-card638-runtime'
    unity = Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity'))
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    manifest = {'result': str(run / 'results.txt'), 'cases': []}
    for name, before, after, expected in variants:
        case = run / name; production = case / 'production'; production.mkdir(parents=True)
        for filename,text in bound.items():
            if filename == 'RemoteAvatar.Motion.cs' and before:
                if text.count(before) != 1: raise RuntimeError('Control binding drift: ' + name)
                text = text.replace(before, after, 1)
            (production / filename).write_text(text)
        shutil.copyfile(fixture / 'Hand.csproj', case / 'Hand.csproj')
        built = subprocess.run([dotnet, 'build', str(case / 'Hand.csproj'), '-c', 'Release', '--nologo', '-v', 'quiet',
            '-p:CaseName=Second_' + name.replace('-', '_'), '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production),
            '-p:UnityManaged=' + str(unity.parent / 'Data/Managed')], capture_output=True, text=True)
        (case / 'build.log').write_text(built.stdout + built.stderr)
        if built.returncode: raise SystemExit(built.stdout + built.stderr)
        manifest['cases'].append({'name': name, 'dll': str(case / 'bin/Release/netstandard2.1' / ('Second_' + name.replace('-', '_') + '.dll')), 'expected': expected})
    project = run / 'unity'; (project / 'Assets/Editor').mkdir(parents=True)
    (project / 'Packages').mkdir(); (project / 'ProjectSettings').mkdir()
    shutil.copyfile(fixture / 'Editor/InteractionRunner.cs', project / 'Assets/Editor/InteractionRunner.cs')
    (project / 'Packages/manifest.json').write_text('{"dependencies":{}}\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    manifest_path = run / 'manifest.json'; manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
    result = subprocess.run([str(unity), '-batchmode', '-nographics', '-projectPath', str(project),
        '-executeMethod', 'InteractionRunner.Start', '-interactionManifest', str(manifest_path), '-logFile', str(run / 'unity.log')], timeout=240)
    receipt = Path(manifest['result'])
    if receipt.is_file(): print(receipt.read_text(), end='')
    if result.returncode or not receipt.is_file(): raise SystemExit('FAIL Unity; see ' + str(run))
    shutil.rmtree(project)
    for name,*_ in variants:
        for directory in ('bin','obj'): shutil.rmtree(run / name / directory, ignore_errors=True)
    print('PASS: complete production motion owners and ' + str(len(variants)-1) + ' causal controls; ' + str(run))

if __name__ == '__main__': main()
