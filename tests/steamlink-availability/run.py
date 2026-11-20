#!/usr/bin/env python3
"""Compile bounded production menu predicates with explicit connection and XR capability inputs."""
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
    options = ROOT / 'src/GloomhavenVR/WorldUI/Options'
    dependencies_path = options / 'VROptionsTab.8.Dependencies.cs'
    menu_path = options / 'VROptionsTab.MixedReality.cs'
    content_path = options / 'VROptionsTab.3.Content.cs'
    curated_path = options / 'VROptionsTab.4.Curated.cs'
    catalog_path = options / 'ConfigCatalog.cs'
    inputs = [dependencies_path, menu_path, HERE / 'Program.cs',
        HERE / 'Fixture.csproj', Path(__file__), curated_path, content_path, catalog_path]
    pins = {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs}
    (run / 'source-pins.json').write_text(json.dumps(pins, indent=2) + '\n')
    copies = run / 'sources'; copies.mkdir()
    for p in inputs: shutil.copyfile(p, copies / p.name)
    dependencies, menu = dependencies_path.read_text(), menu_path.read_text()
    visible = re.search(r'private static bool IsRowVisible\(.*?;', dependencies, re.S)[0]
    fields = menu[menu.index('private static readonly List<Action>'):
        menu.index('private static bool IsEnvironmentChoice')]
    refresh = method(menu, 'private static void RefreshMixedRealityAvailability()')
    clear = method(menu, 'private static void ClearMixedRealityAvailabilityRefreshers()')
    capture = re.search(r'private static void CaptureMixedRealityRowVisibility\(.*?;', menu, re.S)[0]
    source = '\n'.join([fields, visible, refresh, clear, capture])
    rebuild = method(content_path.read_text(), 'private static void Rebuild()')
    assert rebuild.index('ClearRows();') < rebuild.index('CaptureMixedRealityRowVisibility();') < rebuild.index('BuildAdvancedIndex()')
    topic = method(content_path.read_text(), 'private static int BuildTopic(')
    assert 'ConfigCatalog.Groups(topic)' in topic and 'IsRowVisible(entry)' in topic and 'BuildItem(entry,' in topic
    categorization = method(catalog_path.read_text(), 'private static ConfigTopic TopicOf(')
    assert re.search(r'case "MixedReality":(?:\s*case "[^"]+":)*\s*return ConfigTopic.Environment;', categorization)
    assert not re.search(r'new\("MixedReality", "(?:SteamLinkNative|SteamLinkCompanion|SteamLinkEncodeWidth|SteamLinkEdgeQuality)",',
        curated_path.read_text())
    # Each counterexample must fail its semantic assertion after compiling.
    mutations = {
        'companion-row': ('!FrameNativePassthrough.Standalone && Core.SteamLink.SteamLinkConnection.Available',
            '!FrameNativePassthrough.Standalone'),
        'chroma-native': ('!FrameNativePassthrough.Required && !SteamLinkCompanionVisible',
            '!SteamLinkCompanionVisible'),
        'chroma-companion': ('!FrameNativePassthrough.Required && !SteamLinkCompanionVisible',
            '!FrameNativePassthrough.Required'),
        'live-refresh': ('if (changed)', 'if (changed && false)'),
        'early-refresh': ('_mixedRealityRowVisibility = MixedRealityRowVisibility;', '_mixedRealityRowVisibility = null;'),
        'retired-options': ('"SteamLinkNative" or "SteamLinkEdgeQuality" or "SteamLinkEncodeWidth" => true',
            '"SteamLinkNative" or "SteamLinkEdgeQuality" or "SteamLinkEncodeWidth" => false'),
    }
    variants = {'production': source}
    for name, (original, mutated) in mutations.items():
        assert source.count(original) == 1, name
        variants[name] = source.replace(original, mutated)
    project = run / 'owned-project'; project.mkdir()
    shutil.copyfile(HERE / 'Fixture.csproj', project / 'Fixture.csproj')
    shutil.copyfile(HERE / 'Program.cs', project / 'Program.cs')
    env = dict(os.environ); env['PATH'] = str(Path.home() / '.dotnet') + os.pathsep + env.get('PATH', '')
    code = 1
    results = []
    expected = {
        'companion-row': 'legacy control is limited to identified Frame streaming',
        'chroma-native': 'chroma colour is reserved for generic PC streaming',
        'chroma-companion': 'chroma colour is reserved for generic PC streaming',
        'live-refresh': 'identified connection updates an already open pane',
        'early-refresh': 'first tick sees capability change since building rows',
        'retired-options': 'retired companion and transmission options stay hidden',
    }
    assertions = None
    try:
        for name, variant in variants.items():
            fixture = ('using System; using System.Collections.Generic; using GloomhavenVR.Core;\n'
                + 'namespace GloomhavenVR.WorldUI { internal static partial class VROptionsTab {'
                + variant + '} }')
            (project / 'Source.cs').write_text(fixture)
            (copies / (name + '.cs')).write_text(fixture)
            command = ['dotnet', 'run', '--project', str(project / 'Fixture.csproj'), '-c', 'Release']
            with (run / (name + '.log')).open('w') as stream:
                process = subprocess.run(command, env=env, stdout=stream, stderr=subprocess.STDOUT, timeout=120)
            output = (run / (name + '.log')).read_text()
            if name == 'production':
                assert process.returncode == 0 and '57 assertions' in output, output
                assertions = 57
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
            'assertions': assertions,
            'cleanup': cleanup, 'scope': 'Production predicates; HMD identity and actual image output are external boundaries.'}, indent=2) + '\n')
        print('Evidence: ' + str(run))
    return code


if __name__ == '__main__': raise SystemExit(main())
