#!/usr/bin/env python3
"""Audit excluded Bug Fixes v5 map patches against unchanged original game DLLs.

Runs original managed achievement methods, with explicit party/condition/stat
preparation seams. This is evidence about the native defect and incompatibility
hazards; it installs no production patch and is not a live multiplayer proof.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--game-root', type=Path)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/native-bugfix-map-audit')
    args = parser.parse_args()
    game = args.game_root or args.source_root / 'ressources/GH_Data'
    fixture = args.source_root / 'scripts/native-bugfix-map-runtime'
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    rules = game / 'StreamingAssets/Rulebase/DLC/DLC_JoTL/DLC_JoTL_Guildmaster.ruleset'
    with zipfile.ZipFile(rules) as archive:
        condition = archive.read('Achievement/Achievement_Demolitionist_2.yml').decode('utf-8-sig')
    for text in ('CharacterIDs: DemolitionistID', 'AbilityTypes: Attack', 'Filter: DealDamage',
                 'SameTarget: true', 'Times: 3', 'Amount: 15', 'SubFilter: RoundNoReset'):
        assert text in condition, 'Original combo condition changed: ' + text
    (run / 'original-combo.yml').write_text(condition)
    managed = game / 'Managed'
    paths = [fixture / 'Program.cs', fixture / 'MapAudit.csproj', Path(__file__).resolve(), rules]
    paths += [managed / name for name in ('GH.Runtime.dll', 'MapRuleLibrary.dll',
                                        'ScenarioRuleLibrary.dll', 'SharedLibrary.dll')]
    (run / 'provenance.json').write_text(json.dumps({
        'sha256': {str(path.resolve()): hashlib.sha256(path.read_bytes()).hexdigest() for path in paths},
        'coverage': 'Eight unchanged native achievement assertions; no adopted map runtime patch.',
        'boundaries': [
            'Native party list containers, satisfied unlock and synthetic battle logs are prepared explicitly.',
            'Exact shipped combo YAML matches the native target constructor arguments.',
            'Native CheckNonTrophyAchievements, CheckAchievement, CheckTarget and serialization execute original DLLs.',
            'The result-UI clear is invoked at the original list seam; full UI results controllers are not executed.',
            'No Unity scene, platform rewards, Bolt host snapshot reconciliation or hardware outcome is asserted.'
        ]}, indent=2) + '\n')
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    environment = dict(os.environ)
    environment['DOTNET_ROOT'] = str(Path(dotnet).resolve().parent)
    build = subprocess.run([
        dotnet, 'build', str(fixture / 'MapAudit.csproj'), '-c', 'Release',
        '-p:FixtureDir=' + str(fixture.resolve()), '-p:GameManaged=' + str(managed.resolve()),
        '-p:BaseIntermediateOutputPath=' + str(run / 'obj') + '/',
        '-p:OutputPath=' + str(run / 'bin') + '/', '-p:UseSharedCompilation=false'
    ], cwd=args.source_root, env=environment, capture_output=True, text=True)
    (run / 'build.log').write_text(build.stdout + build.stderr)
    if build.returncode:
        print(build.stdout + build.stderr, end='')
        raise SystemExit('FAIL native map audit compilation: ' + str(run))
    # dotnet run re-evaluates its launch path without the redirected OutputPath
    # on this SDK. Execute the successfully built isolated DLL explicitly.
    result = subprocess.run([dotnet, str(run / 'bin/net8.0/MapAudit.dll')],
        cwd=args.source_root, env=environment, capture_output=True, text=True)
    (run / 'execution.log').write_text(result.stdout + result.stderr)
    print(result.stdout + result.stderr, end='')
    if result.returncode:
        raise SystemExit('FAIL native map audit: ' + str(run))
    if 'PASS: 8 unchanged native achievement assertions' not in result.stdout:
        raise SystemExit('FAIL missing exact native assertion count: ' + str(run))
    print('Evidence: ' + str(run))


if __name__ == '__main__':
    main()
