#!/usr/bin/env python3
"""Replay repeated campaign creation through unchanged methods from the installed game.

The original generation, network receiver, repeated-name lookup and personal-quest
completion coroutine are compiled against a deterministic domain boundary. This
checks native branch composition and random-work cardinality, not live Bolt or HMD.
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

from town_run_retention import cleanup_completed_run

ROOT = Path(__file__).resolve().parents[1]


def method(source, signature):
    start = source.index(signature)
    brace = source.index('{', start)
    depth, cursor = 1, brace + 1
    while depth:
        depth += (source[cursor] == '{') - (source[cursor] == '}')
        cursor += 1
    return source[start:cursor]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path)
    args = parser.parse_args()
    root = args.source_root.resolve()
    output = args.output_dir or root / '.planning/debug/campaign-creation756'
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=output))
    project = run / 'project'
    project.mkdir()
    source = run / 'sources'
    source.mkdir()
    log = run / 'console.log'
    log.write_text('Evidence: ' + str(run) + '\n')
    print('Evidence: ' + str(run), flush=True)
    status = 1
    env = os.environ.copy()
    env['DOTNET_ROOT'] = str(Path.home() / '.dotnet')
    env['PATH'] = env['DOTNET_ROOT'] + os.pathsep + env.get('PATH', '')
    env.update(UseSharedCompilation='false', MSBUILDDISABLENODEREUSE='1', DOTNET_CLI_USE_MSBUILD_SERVER='0')
    bindings = []
    def bind(original, target):
        shutil.copy2(original, target)
        bindings.append({'source': str(original), 'snapshot': str(target), 'sha256': hashlib.sha256(target.read_bytes()).hexdigest()})
    try:
        fixture = root / 'tests/GloomhavenVR.CampaignCreationTests'
        for item in fixture.iterdir():
            if item.is_file():
                bind(item, project / item.name)
        original = root / 'src/GloomhavenVR/Compat/NativeCampaignCreationGuard.cs'
        production = source / original.name
        bind(original, production)
        runtime = root / 'ressources/GH_Data/Managed/GH.Runtime.dll'
        runtime_sha = hashlib.sha256(runtime.read_bytes()).hexdigest()
        native = '#nullable disable\nusing System;using System.Linq;using System.Collections;using FFSNet;using MapRuleLibrary.Adventure;using MapRuleLibrary.Party;\n'
        specs = [
            ('CharacterCreatorService', 'public static CMapCharacter CreateCharacter(string classId, string characterName)',
             'CreateCharacter(', 'CreateCharacterOriginal('),
            ('UICharacterCreatorNameStep', 'public bool IsNameRepeated(string characterName)',
             'IsNameRepeated(', 'IsNameRepeatedOriginal('),
            ('UICharacterCreatorWindow', 'public void ProxyCreateCharacter(GameAction action)',
             'ProxyCreateCharacter(', 'ProxyCreateCharacterOriginal('),
            ('UICampaignAdventurePartyAssemblyWindow', 'private IEnumerator ProxyCreateCampaignCharacterCoroutine(CampaignPersonalQuestData data)',
             'private IEnumerator', 'public IEnumerator'),
        ]
        for kind, signature, before, after in specs:
            result = subprocess.run([str(Path.home() / '.dotnet/tools/ilspycmd'), '-t', kind, str(runtime)],
                                    check=True, text=True, capture_output=True, env=env)
            text = result.stdout
            (source / (kind + '.native.cs')).write_text(text)
            fragment = method(text, signature)
            if before is not None:
                assert fragment.count(before) == 1, (kind, 'translation boundary')
                fragment = fragment.replace(before, after, 1)
            native += 'public partial class ' + kind + ' {\n' + fragment + '\n}\n'
        (project / 'NativeMethods.cs').write_text(native)
        (run / 'input-bindings.json').write_text(json.dumps({'production': bindings, 'runtime': str(runtime), 'runtime_sha256': runtime_sha,
            'native_methods': [row[:2] for row in specs], 'ports': [
                'MapRuleLibrary state/generation boundary with deterministic successive quest IDs and work counters',
                'FFSNet player/action records, original creator window fields and presentation no-ops',
                'Harmony prefix/postfix/finalizer invocation adapter; native service, name lookup and proxy methods renamed only to call through adapter',
                'Private original completion coroutine made public for explicit test invocation; no live Bolt or HMD proof'],
            }, indent=2) + '\n')
        command = ['dotnet', 'run', '--project', str(project / 'CampaignCreation.csproj'), '-c', 'Release', '--property:CreationSource=' + str(production)]
        result = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, env=env)
        with log.open('a') as stream:
            stream.write(result.stdout)
        print(result.stdout, flush=True)
        if result.returncode:
            raise SystemExit(result.returncode)
        assertions = int(re.search(r'Campaign creation: (\d+) assertions', result.stdout).group(1))
        assembly = run / 'executed-assembly'
        shutil.copytree(project / 'bin/Release/net8.0', assembly)
        original_text = production.read_text()
        variants = [
            ('lost-reuse', 'if (!TryReuse(classId, characterName, out CMapCharacter? character))',
             'if (!TryReuse(classId, characterName, out CMapCharacter? character) || character != null)',
             'reopened native creator reuses exact provisional object'),
            ('old-player-context', ' || !ReferenceEquals(_player?.Target, player)', '',
             'rejoin with same save and numeric player id cannot reuse old session'),
            ('reuse-completed', 'if (!Provisional(prior))', 'if (ReferenceEquals(prior, null))',
             'completed prior character is never reused'),
            ('wrong-class-reuse', 'if (!string.Equals(prior.CharacterID, classId, StringComparison.Ordinal))',
             'if (ReferenceEquals(prior, null))', 'different class never reuses wrong native character'),
            ('lost-reservation', '__result = ReservedForOtherClass(__instance, characterName);', '__result = false;',
             'changed class retains native repeated-name rejection'),
            ('wrong-command-origin', 'return LocalCommand(player) && creation != null', 'return creation != null',
             'peer same-name command generates its own native object'),
            ('lost-origin-restore', 'if (scope.Entered) _commandOwner = scope.Previous;', 'if (scope.Entered) _commandOwner = -1;',
             'nested peer exception restores outer local origin'),
            ('lost-entered-origin', 'if (scope.Entered) _commandOwner = scope.Previous;', '_commandOwner = scope.Previous;',
             'unentered finalizer cannot clear outer local origin'),
            ('cross-thread-origin', '[ThreadStatic] private static int _commandOwner;', 'private static int _commandOwner;',
             'native origin cannot leak to another thread'),
            ('observer-name-reservation', '|| (_commandOwner != 0 && !LocalCommand(PlayerRegistry.MyPlayer))', '',
             'peer wrong-class command retains original observer name validation'),
        ]
        for label, needle, replacement, expected in variants:
            assert original_text.count(needle) == 1, (label, 'control binding')
            mutated = original_text.replace(needle, replacement)
            production.write_text(mutated)
            (run / (label + '.cs')).write_text(mutated)
            result = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, env=env)
            (run / (label + '.log')).write_text(result.stdout)
            if result.returncode == 0 or expected not in result.stdout:
                raise RuntimeError('Causal control failed at wrong boundary: ' + label + '\n' + result.stdout)
            print('Campaign creation control: ' + label + ' rejected.', flush=True)
        production.write_text(original_text)
        (run / 'result.json').write_text(json.dumps({'assertions': assertions, 'negative_controls': [v[0] for v in variants],
            'hardware_verified': False, 'live_bolt_verified': False, 'runtime_sha256': runtime_sha}, indent=2) + '\n')
        status = 0
    finally:
        receipt = cleanup_completed_run('campaign-creation', status, log, root, process_reaped=True)
        (run / 'cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')
    return status


if __name__ == '__main__':
    raise SystemExit(main())
