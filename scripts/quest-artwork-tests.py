#!/usr/bin/env python3
"""Actual Unity texture-copy, region and changing portrait-cache qualification."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run


def method(source, signature):
    assert source.count(signature) == 1, signature
    start = source.index(signature)
    end = source.index('\n    }\n', start) + len('\n    }\n')
    return source[start:end]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--graphics', choices=('vulkan', 'glcore'), default='vulkan')
    parser.add_argument('--case', action='append')
    parser.add_argument('--color-space', choices=('gamma', 'linear'), default='linear')
    args = parser.parse_args()
    base = ROOT / '.planning/debug/quest-artwork'
    base.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=base))
    log = run / 'runner.log'
    log.write_text('Quest artwork evidence: ' + str(run) + '\n')
    print(log.read_text(), end='', flush=True)
    sources = {}
    for relative in ('Cards/Art/CardFaceMipBake.cs', 'Core/QuestTextureCopy.cs', 'Cards/Art/QuestArtworkEvidence.cs'):
        path = ROOT / 'src/GloomhavenVR' / relative
        sources[path.name] = path.read_text()
    panel = (ROOT / 'src/GloomhavenVR/WorldUI/Sharpness/PanelMipBake.cs').read_text()
    sources['RawPortrait.cs'] = '''using System.Collections.Generic; using GloomhavenVR.Cards; using GloomhavenVR.Core; using UnityEngine; using UnityEngine.UI;
namespace GloomhavenVR.WorldUI; internal static class PanelMipBake {
private static readonly Dictionary<int, Texture> s_rawOriginalByBaked = new(16);
internal static Texture OriginalFor(Texture texture) => s_rawOriginalByBaked.TryGetValue(texture.GetInstanceID(), out Texture original) && original != null ? original : texture;
''' + method(panel, '    internal static bool RefreshRawTexture(') + '\n}\n'
    variants = [
        ('production', '', '', '', ''),
        ('label-is-content', 'CardFaceMipBake.cs', 'return QuestStandalonePlatform.Enabled ? $"{label}|instance={tex.GetInstanceID()}|update={tex.updateCount}" : label;', 'return label;', 'same-name atlas regions do not alias unrelated content'),
        ('raw-update-stale', 'RawPortrait.cs', 'if (!QuestStandalonePlatform.Enabled || original is not Texture2D live || live == null) return false;', 'if (QuestStandalonePlatform.Enabled || original is not Texture2D live || live == null) return false;', 'already baked raw portrait refreshes changed original contents'),
        ('raw-source-current-copy', 'RawPortrait.cs', 'source = live;', 'source = displayed;', 'already baked raw portrait refreshes changed original contents'),
        ('sprite-update-stale', 'CardFaceMipBake.cs', '&& update == source.texture.updateCount;', '&& true;', 'same-object source update invalidates preparation without GPU hash'),
    ]
    if args.case:
        variants = [row for row in variants if row[0] in args.case]
        if len(variants) != len(set(args.case)): parser.error('Unknown/duplicate case')
    fixture = ROOT / 'tests/QuestArtwork.Tests'
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    unity = Path('/home/claw/unity-2021.3.5/Editor/Unity')
    manifest = {'result': str(run / 'results.txt'), 'cases': []}
    code = 1
    process = None
    try:
        for name, filename, before, after, expected in variants:
            case = run / name
            production = case / 'production'; production.mkdir(parents=True)
            for path, source in sources.items():
                if path == filename:
                    assert source.count(before) == 1, name
                    source = source.replace(before, after, 1)
                (production / path).write_text(source)
            project = case / 'Runtime.csproj'; shutil.copyfile(fixture / 'Runtime.csproj', project)
            assembly = 'QuestArtwork_' + name.replace('-', '_')
            build = subprocess.run([dotnet, 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet',
                '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production),
                '-p:UnityManaged=' + str(unity.parent / 'Data/Managed'), '-p:UnityUi=' + str(ROOT / 'ressources/GH_Data/Managed/UnityEngine.UI.dll')], capture_output=True, text=True)
            (case / 'build.log').write_text(build.stdout + build.stderr)
            if build.returncode: raise RuntimeError('Fixture compilation failed: ' + str(case / 'build.log'))
            manifest['cases'].append({'name': name, 'dll': str(case / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected})
        project = run / 'unity'
        for folder in ('Assets/Plugins', 'Assets/Editor', 'Assets/Resources', 'Packages', 'ProjectSettings'): (project / folder).mkdir(parents=True, exist_ok=True)
        for case in manifest['cases']:
            target = project / 'Assets/Plugins' / Path(case['dll']).name
            shutil.copyfile(case['dll'], target); case['dll'] = str(target)
        shutil.copyfile(ROOT / 'scripts/card-diagnostic-runtime/Editor/DiagnosticRunner.cs', project / 'Assets/Editor/DiagnosticRunner.cs')
        shutil.copyfile(ROOT / 'unity/GloomhavenVR.Quest/Assets/Quest/Resources/QuestTextureCopy.shader', project / 'Assets/Resources/QuestTextureCopy.shader')
        (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}\n')
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        (project / 'ProjectSettings/ProjectSettings.asset').write_text('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\nPlayerSettings:\n  m_ObjectHideFlags: 0\n  serializedVersion: 23\n  m_ActiveColorSpace: ' + ('1' if args.color_space == 'linear' else '0') + '\n')
        manifest_path = run / 'manifest.json'; manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
        (run / 'source-hashes.json').write_text(json.dumps({'sources': {name: hashlib.sha256(text.encode()).hexdigest() for name, text in sources.items()},
            'panelSource': hashlib.sha256(panel.encode()).hexdigest(), 'graphics': args.graphics, 'colorSpace': args.color_space,
            'hardwareVerified': False}, indent=2) + '\n')
        command = [str(unity), '-batchmode', '-force-' + args.graphics, '-projectPath', str(project), '-executeMethod', 'DiagnosticRunner.Start', '-interactionManifest', str(manifest_path), '-logFile', str(run / 'unity.log')]
        if not os.environ.get('DISPLAY'): command = ['xvfb-run', '-a'] + command
        process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
        code = process.wait(timeout=240)
        result = Path(manifest['result'])
        if result.is_file(): print(result.read_text(), end='', flush=True)
        if code or not result.is_file(): raise RuntimeError('Actual Unity artwork fixture failed: ' + str(run / 'unity.log'))
        print('PASS Quest artwork ' + args.graphics + ': ' + str(len(variants)) + ' production/causal variants', flush=True)
    finally:
        if process is not None and process.poll() is None: process.kill(); process.wait()
        cleanup = cleanup_completed_run('quest-artwork', code, log, ROOT, process_reaped=True)
        (run / 'cleanup.json').write_text(json.dumps(cleanup, indent=2) + '\n')


if __name__ == '__main__': main()
