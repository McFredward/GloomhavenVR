#!/usr/bin/env python3
"""Execute production remote-stage receive/parent/settle kernels with a portable transform boundary."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run


def block(source, signature):
    start = source.index(signature)
    opening = source.index('{', start)
    depth, end = 1, opening + 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


def execute(command, log):
    with log.open('w') as output:
        child = subprocess.Popen(command, stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            return child.wait(timeout=120)
        finally:
            if child.poll() is None:
                os.killpg(child.pid, signal.SIGKILL)
            child.wait()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/active-dismiss-remote-runtime')
    args = parser.parse_args()
    root = args.source_root.resolve()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir)).resolve()
    completion = run / 'completion.log'
    completion.write_text('Evidence: ' + str(run) + '\n')
    status, results = 1, []
    inputs = [root / 'src/GloomhavenVR/Net/Remote' / name for name in
              ('RemoteBoardCard.cs', 'RemoteAvatar.cs', 'RemoteActiveCards.cs')]
    hashes = {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in inputs}
    try:
        card, receiver, active = (path.read_text() for path in inputs)
        methods = '\n'.join(block(card, signature) for signature in (
            'internal void SetPresentationParent(', 'public void Move(', 'internal void TickGlide('))
        alias = 'using System; using GloomhavenVR.Core; using Vector3 = FixtureVector; using Quaternion = FixtureRotation; using Transform = FixtureTransform; using Mathf = FixtureMath; using Time = FixtureClock;\n'
        fields = '''private readonly FixtureObject _root = new(); private Vector3 _glideTo; private float _glideScale = 1f; private bool _gliding, _returningPose; private float _glideSpeed = Defaults.CardLerpSpeed; private const float GlideEpsilonSq = 1e-8f;
internal Transform PresentationTransform => _root.transform;
internal RemoteBoardCard(Transform parent, Vector3 position) { _root.transform.parent = parent; _root.transform.localPosition = position; }
'''
        begin = receiver.index('        ActiveDismissBerthActive = p.HasActiveDismissBerth;')
        end = receiver.index('\n\n', begin)
        apply = 'internal void Apply(in PresenceState p) {\n' + receiver[begin:end] + '\n}\n'
        seat = re.search(r'        int dismissSeat = _owner.ActiveDismissBerthActive[\s\S]*?;', active)[0]
        seat = seat.replace('_owner.', '').replace('Count', 'count').replace('ActiveDismissListcount', 'ActiveDismissListCount')
        seat = 'internal int ResolveDismissSeat(int count, Transform? dismissBerth) {\n' + seat + '\nreturn dismissSeat; }\n'
        sources = {'Card.cs': alias + 'namespace GloomhavenVR.Net { internal sealed class RemoteBoardCard {\n' + fields + methods + '\n}}',
                   'Receiver.cs': alias + 'namespace GloomhavenVR.Net { internal sealed partial class RemoteAvatar {\n' + apply + seat + '\n}}'}
        mutations = [
            ('production', '', '', '', ''),
            ('parent-teleports', 'Card.cs', 'worldPositionStays: true', 'worldPositionStays: false', 'reparent preserves the complete current world pose'),
            ('animated-wrong-scale', 'Card.cs', 'Vector3 targetScale = Vector3.one * _glideScale;', 'Vector3 targetScale = Vector3.one;', 'ability slab settles to the exact owner-authored berth fit'),
            ('seed-wrong-scale', 'Card.cs', '_root.transform.localScale = Vector3.one * _glideScale;', '_root.transform.localScale = Vector3.one;', 'freshly seeded stage uses the same fit'),
            ('receiver-latches-absence', 'Receiver.cs', 'ActiveDismissBerthActive = p.HasActiveDismissBerth;', 'ActiveDismissBerthActive |= p.HasActiveDismissBerth;', 'absent state clears the previous remote reservation'),
            ('wrong-public-list', 'Receiver.cs', 'ActiveDismissListCount == count', 'true', 'active-list count mismatch cannot stage a different card'),
        ]
        dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
        for name, filename, before, after, expected in mutations:
            case = run / name
            case.mkdir()
            for source_name, text in sources.items():
                if filename == source_name:
                    if text.count(before) != 1:
                        raise AssertionError('Causal-control source binding drift: ' + name)
                    text = text.replace(before, after, 1)
                (case / source_name).write_text(text)
            for path in (ROOT / 'scripts/active-dismiss-remote-runtime').glob('*.cs'):
                shutil.copyfile(path, case / path.name)
            project = case / 'RemoteDismiss.csproj'
            project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>\n')
            code = execute([dotnet, 'build', '--disable-build-servers', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet', '-p:UseSharedCompilation=false'], case / 'build.log')
            if code:
                raise RuntimeError('Compilation is not a passing control: ' + str(case / 'build.log'))
            code = execute([dotnet, str(case / 'bin/Release/net8.0/RemoteDismiss.dll')], case / 'runtime.log')
            output = (case / 'runtime.log').read_text()
            passed = code == 0 if not expected else code != 0 and expected in output
            results.append({'name': name, 'passed': passed, 'exit_code': code, 'expected': expected, 'output': output})
            print(('PASS ' if passed else 'FAIL ') + name + ': ' + output.strip(), flush=True)
            if not passed:
                raise RuntimeError('Production/control failure: ' + str(case))
        if any(hashlib.sha256(path.read_bytes()).hexdigest() != hashes[str(path)] for path in inputs):
            raise RuntimeError('Production sources changed during test')
        status = 0
        return 0
    finally:
        (run / 'source-hashes.json').write_text(json.dumps(hashes, indent=2) + '\n')
        (run / 'result.json').write_text(json.dumps({'exit_code': status, 'cases': results}, indent=2) + '\n')
        (run / 'cleanup.json').write_text(json.dumps(cleanup_completed_run('active-dismiss-remote-runtime', status, completion, root, process_reaped=True), indent=2) + '\n')
        print('Evidence: ' + str(run), flush=True)


if __name__ == '__main__':
    raise SystemExit(main())
