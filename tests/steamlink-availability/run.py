#!/usr/bin/env python3
"""Compile bounded production request/menu predicates; no HMD, helper or receiver is simulated."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent


def method(source, signature):
    start = source.index(signature)
    opening = source.index('{', start)
    end, depth = opening + 1, 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output.resolve()))
    completion = run / 'completion.log'
    completion.write_text('Evidence: ' + str(run) + '\n')
    inputs = [ROOT / 'src/GloomhavenVR/Core/SteamLink/SteamLinkSceneRuntime.cs',
        ROOT / 'src/GloomhavenVR/WorldUI/Options/VROptionsTab.8.Dependencies.cs',
        ROOT / 'src/GloomhavenVR/WorldUI/Options/VROptionsTab.MixedReality.cs',
        ROOT / 'src/GloomhavenVR/Defaults/Defaults.Core.cs', HERE / 'Program.cs',
        HERE / 'Fixture.csproj', Path(__file__)]
    pins = {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs}
    (run / 'source-pins.json').write_text(json.dumps(pins, indent=2) + '\n')
    copies = run / 'sources'; copies.mkdir()
    for p in inputs: shutil.copyfile(p, copies / p.name)
    owner, dependencies, menu, defaults = [p.read_text() for p in inputs[:4]]
    requested = re.search(r'internal static bool Requested =>.*?;', owner, re.S)[0]
    visible = re.search(r'private static bool IsRowVisible\(.*?;', dependencies, re.S)[0]
    predicate = re.search(r'\["MixedReality/KeyColor"\] = new\("MixedReality", "SteamLinkNative",\s*(.*?)\),', dependencies, re.S)[1]
    edge_predicate = re.search(r'\["MixedReality/SteamLinkEdgeQuality"\] = new\("MixedReality", "SteamLinkNative",\s*(.*?)\),', dependencies, re.S)[1]
    width_predicate = re.search(r'\["MixedReality/SteamLinkEncodeWidth"\] = new\("MixedReality", "SteamLinkEdgeQuality",\s*(.*?)\),', dependencies, re.S)[1]
    refresh = method(menu, 'private static void RefreshMixedRealityAvailability()')
    clear = method(menu, 'private static void ClearMixedRealityAvailabilityRefreshers()')
    assert re.search(r'internal const bool SteamLinkNative = true;', defaults)
    # A counterexample that restores each actual defect must be rejected at its
    # semantic assertion; compilation failure alone is never counted as proof.
    variants = {
        'production': (requested, visible, predicate, refresh),
        'request-gate': (requested.replace('&& SteamLinkConnection.Available', ''), visible, predicate, refresh),
        'row-gate': (requested, visible.replace('&& !Core.SteamLink.SteamLinkConnection.Available', '&& false'), predicate, refresh),
        'fallback': (requested, visible, 'static v => Off(v)', refresh),
        'live-refresh': (requested, visible, predicate, refresh.replace('if (changed)', 'if (changed && false)')),
    }
    variants = {name: (*values, edge_predicate, width_predicate) for name, values in variants.items()}
    variants['quality-parent'] = (requested, visible, predicate, refresh, 'static v => true', width_predicate)
    variants['width-parent'] = (requested, visible, predicate, refresh, edge_predicate,
        'static v => MixedReality.SteamLinkNative.Value')
    project = run / 'owned-project'; project.mkdir()
    shutil.copyfile(HERE / 'Fixture.csproj', project / 'Fixture.csproj')
    shutil.copyfile(HERE / 'Program.cs', project / 'Program.cs')
    env = dict(os.environ); env['PATH'] = str(Path.home() / '.dotnet') + os.pathsep + env.get('PATH', '')
    code = 1
    results = []
    expected = {'request-gate': 'only proven Frame/vrlink plus preference',
        'row-gate': 'native control requires current Frame/vrlink',
        'fallback': 'unsupported persisted-on preference must retain chromakey controls',
        'live-refresh': 'resolved capability refreshes an already open pane',
        'quality-parent': 'edge quality requires proven streaming plus native preference',
        'width-parent': 'encoding width requires proven native streaming plus edge quality'}
    try:
        for name, (request, row, pred, update, edge_pred, width_pred) in variants.items():
            fixture = ('using System; using System.Collections.Generic; using GloomhavenVR.Core;\n'
                + 'namespace GloomhavenVR.Core.SteamLink { internal static partial class SteamLinkSceneRuntime {' + request + '} }\n'
                + 'namespace GloomhavenVR.WorldUI { internal static partial class VROptionsTab {'
                + 'private static Func<object?, bool> KeyPredicate = ' + pred + ';\n'
                + 'private static Func<object?, bool> EdgeQualityPredicate = ' + edge_pred + ';\n'
                + 'private static Func<object?, bool> EncodeWidthPredicate = ' + width_pred + ';\n'
                + row + '\n' + update + '\n' + clear + '} }')
            (project / 'Source.cs').write_text(fixture)
            (copies / (name + '.cs')).write_text(fixture)
            command = ['dotnet', 'run', '--project', str(project / 'Fixture.csproj'), '-c', 'Release']
            with (run / (name + '.log')).open('w') as stream:
                process = subprocess.run(command, env=env, stdout=stream, stderr=subprocess.STDOUT, timeout=120)
            output = (run / (name + '.log')).read_text()
            if name == 'production':
                assert process.returncode == 0 and '69 assertions' in output, output
            else:
                assert process.returncode != 0 and expected[name] in output, 'Wrong refusal: ' + name + '\n' + output
                assert 'error CS' not in output, output
            results.append({'case': name, 'exit_code': process.returncode, 'accepted': name == 'production'})
        assert all(hashlib.sha256(Path(p).read_bytes()).hexdigest() == h for p, h in pins.items())
        code = 0
    finally:
        sys.path.insert(0, str(ROOT / 'scripts'))
        from town_run_retention import cleanup_completed_run
        cleanup = cleanup_completed_run('steamlink-availability', code, completion, ROOT, process_reaped=True)
        if project.is_dir() and not project.is_symlink(): shutil.rmtree(project)
        (run / 'result.json').write_text(json.dumps({'exit_code': code, 'cases': results,
            'assertions': 69 if results and results[0]['accepted'] else None,
            'cleanup': cleanup, 'scope': 'Production predicates; HMD identity and actual image output are external boundaries.'}, indent=2) + '\n')
        print('Evidence: ' + str(run))
    return code


if __name__ == '__main__': raise SystemExit(main())
