#!/usr/bin/env python3
"""Execute production movie trigger UI/Loc with explicit runtime doubles and causal controls."""
from __future__ import annotations

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

from town_run_retention import cleanup_completed_run

ROOT = Path(__file__).resolve().parent.parent


def method(source: str, signature: str) -> str:
    start = source.index('    ' + signature)
    end = source.index('\n    }', start) + len('\n    }')
    return source[start:end]


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--catalogue-source', type=Path, default=ROOT / 'src/GloomhavenVR/WorldUI/FlatScreen/NativeVideoMovies.cs')
    parser.add_argument('--control', action='append', help='Run only this causal control; repeat for a focused repair check.')
    args = parser.parse_args()
    fixture = ROOT / 'tests/GloomhavenVR.VideoReviewOptionsTests'
    options = ROOT / 'src/GloomhavenVR/WorldUI/Options'
    paths = {
        'ui': options / 'VROptionsTab.VideoReview.cs',
        'loc': ROOT / 'src/GloomhavenVR/Core/Loc/Loc.VideoReview.cs',
        'page': options / 'VROptionsTab.9.TestTriggers.cs',
        'inject': options / 'VROptionsTab.1.Inject.cs',
        'rows': options / 'VROptionsTab.2.Rows.cs',
        'catalogue': args.catalogue_source,
    }
    source = {key: path.read_text() for key, path in paths.items()}
    movies = re.search(r'private static readonly string\[\] Movies\s*=\s*\{(.*?)\};', source['catalogue'], re.S)
    assert movies, 'The production movie catalogue must be present; integrate the runtime lane first.'
    intro = re.search(r'private const string IntroName = "([^"]+)";', source['catalogue'])
    assert intro
    keys = [intro[1] + '.mov' if value == 'IntroKey' else value[1:-1]
            for value in re.findall(r'IntroKey|"[^"]+\.mov"', movies[1])]
    expected = json.loads((fixture / 'catalogue.json').read_text())
    assert len(keys) == len(set(keys)) and set(keys) == set(expected), 'Audited runtime/UI catalogue changed; review every new movie name/action.'
    localized = re.findall(r'^\s*"([^"]+\.mov)"\s*=>', source['loc'], re.M)
    assert set(localized) == set(keys), 'Every runtime movie needs its own localized player name.'
    assert 'RefreshVideoReviewRows();' in method(source['inject'], 'internal static void Tick()')
    assert 'ClearVideoReviewRows();' in method(source['inject'], 'private static void Forget()')
    assert 'ClearVideoReviewRows();' in method(source['rows'], 'private static void ClearRows()')
    assert 'private static GameObject BuildNote(' in source['rows'] and 'int minLines = 1)' in source['rows']
    assert "new string('\\n', minLines - 1)" in source['rows']
    assert 'Rebuild' not in source['ui'], 'Movie actions may repaint, never rebuild their own page.'

    evidence_root = ROOT / '.planning/debug/video-review-options'
    evidence_root.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=evidence_root))
    project_dir = run / 'project'
    project_dir.mkdir()
    for path in fixture.glob('*.cs*'):
        shutil.copy2(path, project_dir / path.name)
    (run / 'runtime-catalogue.json').write_text(json.dumps(keys, indent=2) + '\n')
    (run / 'source-hashes.json').write_text(json.dumps({key: {'path': str(paths[key]), 'sha256': hashlib.sha256(text.encode()).hexdigest()}
                                                       for key, text in source.items()}, indent=2) + '\n')
    page = ('using GloomhavenVR.Core;\nnamespace GloomhavenVR.WorldUI;\ninternal static partial class VROptionsTab {\n'
            + method(source['page'], 'private static int BuildTestTriggers()') + '\n}\n')
    originals = {'ui': source['ui'], 'loc': source['loc'], 'page': page}
    for key, text in originals.items():
        (run / (key + '.fixture')).write_text(text)
    project = project_dir / 'GloomhavenVR.VideoReviewOptionsTests.csproj'
    dotnet = str(Path(os.environ.get('DOTNET_ROOT', str(Path.home() / '.dotnet'))) / 'dotnet')
    if not Path(dotnet).is_file():
        dotnet = shutil.which('dotnet') or dotnet
    suite_log = run / 'suite.log'
    suite_log.write_text(f'evidence: {run}\n')
    print(f'Video review UI evidence: {run}', flush=True)
    results = []
    exit_code = 1

    def execute(name: str, variant: dict[str, str], expected_failure: str | None = None) -> None:
        for key, text in variant.items():
            (run / (key + '.current')).write_text(text)
        command = [dotnet, 'run', '--project', str(project), '--configuration', 'Release',
                   '-p:UseSharedCompilation=false',
                   '-p:ReviewUiSource=' + str(run / 'ui.current'),
                   '-p:ReviewLocSource=' + str(run / 'loc.current'),
                   '-p:TriggerPageSource=' + str(run / 'page.current'),
                   '--', str(run / 'runtime-catalogue.json')]
        log_path = run / (name + '.log')
        with log_path.open('w') as log:
            child = subprocess.Popen(command, cwd=ROOT, stdout=log, stderr=subprocess.STDOUT,
                                     start_new_session=True)
            try:
                code = child.wait()
            except BaseException:
                os.killpg(child.pid, signal.SIGTERM)
                try:
                    child.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    os.killpg(child.pid, signal.SIGKILL)
                    child.wait()
                raise
        output = log_path.read_text()
        if expected_failure is None:
            assert code == 0, output
        else:
            assert code != 0 and expected_failure in output, name + ' did not fail for its intended assertion:\n' + output
        results.append({'name': name, 'exit_code': code, 'expected_failure': expected_failure})
        with suite_log.open('a') as log:
            log.write(name + ': ' + ('production passed' if expected_failure is None else 'causal control rejected') + '\n')
        print(output.strip() if expected_failure is None else 'PASS video review UI negative control: ' + name, flush=True)

    try:
        execute('production', originals)
        mutations = [
            ('wrong-movie', 'ui', 'NativeVideoReview.TryStart(key);', 'NativeVideoReview.TryStart(NativeVideoReview.GetKey(0));',
             'Each movie requests its own exact canonical key'),
            ('lost-first', 'ui', 'int i = 0;', 'int i = 1;',
             'All audited movies, back, close and all-off actions remain reachable'),
            ('tab-action', 'ui', '}, asAction: true);\n            rows++;', '}, asAction: false);\n            rows++;',
             'Movie rows are one-shot actions, not menu toggles'),
            ('delayed-feedback', 'ui', '                RefreshVideoReviewRows();', '                // skipped immediate feedback',
             'Preparation feedback appears on the press'),
            ('lost-close', 'ui', '            NativeVideoReview.Stop();', '            // skipped close',
             'Explicit close releases the review and explains replay'),
            ('lost-stop-all', 'page', '            NativeVideoReview.Stop();', '            // skipped all-off close',
             'All-off closes the owned review movie'),
            ('lost-refusal', 'ui', '        if (NativeVideoReview.LastFailure != NativeVideoReviewFailure.None)',
             '        if (NativeVideoReview.LastFailure == (NativeVideoReviewFailure)255)', 'Busy feedback appears on the press'),
            ('wrong-context', 'ui', 'NativeVideoReviewFailure.InvalidContext => Loc.VideoReviewScenarioRequired,',
             'NativeVideoReviewFailure.InvalidContext => Loc.VideoReviewFailed,',
             'Invalid context explains that a scenario is required'),
            ('hidden-repaint', 'ui', '_videoReviewStatus == null || !IsOpen', '_videoReviewStatus == null',
             'Closing options leaves playback intact and does not repaint hidden controls'),
            ('stale-label', 'ui', '        _videoReviewStatus = null;', '        // skipped stale label release',
             'Destroyed status label was repainted'),
            ('status-overlap', 'ui', 'minLines: 3', 'minLines: 1',
             'Live status reserves localized feedback space'),
            ('no-german', 'loc', 'CurrentLanguage == "German" ? german : english;', 'CurrentLanguage == "__mutant__" ? german : english;',
             'German movie names are localized'),
            ('lost-native-name', 'loc', '                    return Game(hero.LocKey, fallback);',
             '                    return fallback;',
             'Hero title uses its own original class localization without an unlock gate'),
            ('per-frame-format', 'ui', 'if (_videoReviewSnapshot && state == _videoReviewState && failure == _videoReviewFailure',
             'if (_videoReviewSnapshot && Loc.CurrentLanguage == "__mutant__" && state == _videoReviewState && failure == _videoReviewFailure',
             'Unchanged open status does not allocate per frame'),
        ]
        if args.control:
            assert set(args.control) <= {item[0] for item in mutations}, 'Unknown causal control'
            mutations = [item for item in mutations if item[0] in args.control]
        for name, key, before, after, failure in mutations:
            assert originals[key].count(before) == 1, name
            variant = dict(originals)
            variant[key] = originals[key].replace(before, after)
            for source_key, text in variant.items():
                (run / (name + '-' + source_key + '.fixture')).write_text(text)
            execute(name, variant, failure)
        exit_code = 0
        return 0
    finally:
        (run / 'result.json').write_text(json.dumps({'exit_code': exit_code, 'results': results, 'selected_controls': args.control,
                                                   'scope': 'Production menu and localization; runtime and Unity doubles, no decoder or headset claim.'}, indent=2) + '\n')
        cleanup = cleanup_completed_run('video-review-options', exit_code, suite_log, ROOT, process_reaped=True)
        (run / 'cache-cleanup.json').write_text(json.dumps(cleanup, indent=2) + '\n')


if __name__ == '__main__':
    sys.exit(main())
