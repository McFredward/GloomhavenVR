#!/usr/bin/env python3
"""Execute the exact publisher card-restore UI and candidate Harmony patch in Unity."""
import argparse
import hashlib
import json
import shutil
import subprocess
import tempfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/native-bugfix-cards-runtime')
    parser.add_argument('--reuse-project', type=Path)
    args = parser.parse_args()
    root = args.source_root.resolve()
    game = root / 'ressources/GH_Data/Managed'
    unity = Path('/home/claw/unity-2021.3.5/Editor/Unity')
    packages = Path.home() / '.nuget/packages'
    deps = [('harmonyx', '2.7.0', 'net45', '0Harmony'),
            ('monomod.runtimedetour', '21.12.13.1', 'net452', 'MonoMod.RuntimeDetour'),
            ('monomod.utils', '21.12.13.1', 'net452', 'MonoMod.Utils'),
            ('mono.cecil', '0.11.4', 'net40', 'Mono.Cecil')]
    for required in (unity, game / 'GH.Runtime.dll', game / 'ScenarioRuleLibrary.dll'):
        if not required.is_file(): parser.error('Missing original runtime dependency: ' + str(required))
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    production_file = root / 'src/GloomhavenVR/Compat/NativeBugFixes.Cards.cs'
    original = production_file.read_text()
    fixture = root / 'scripts/native-bugfix-cards-runtime'
    manifest = {'result': str(run / 'results.txt'), 'cases': []}
    hashes = {str(production_file.relative_to(root)): hashlib.sha256(production_file.read_bytes()).hexdigest()}
    for file in fixture.rglob('*'):
        if file.is_file(): hashes[str(file.relative_to(root))] = hashlib.sha256(file.read_bytes()).hexdigest()
    for filename in ('GH.Runtime.dll', 'GH.Runtime.FirstPass.dll', 'ScenarioRuleLibrary.dll', 'ThirdParty.dll'):
        hashes['publisher/' + filename] = hashlib.sha256((game / filename).read_bytes()).hexdigest()
    (run / 'source-hashes.json').write_text(json.dumps(hashes, indent=2) + '\n')
    variants = [
        ('production', '', '', ''),
        ('stale-turn-type', '___extraTurnType = actor.TakingExtraTurnOfType;', '',
         'legal restored card half exactly follows original native restrictions'),
        ('stale-owner-promoted', 'if (topOwner == null && bottomOwner == null\n                || ___cachedTopCard != null && !ReferenceEquals(topOwner, actor)\n                || ___cachedBottomCard != null && !ReferenceEquals(bottomOwner, actor))',
         'if (!VRSession.IsRunning)', 'split-pair: stale/missing/observer identity does not rewrite UI type'),
        ('owner-member-fault', 'AccessTools.Field(typeof(FullAbilityCard), "playerActor")',
         'AccessTools.Field(typeof(CActor), "m_Class")', ''),
        ('owner-member-missing', 'AccessTools.Field(typeof(FullAbilityCard), "playerActor")',
         'AccessTools.Field(typeof(FullAbilityCard), "missingFixtureOwnerMember")', ''),
    ]
    for name, before, after, expected in variants:
        build = run / name
        production = build / 'production'; production.mkdir(parents=True)
        code = original
        if before:
            if code.count(before) != 1: raise SystemExit('Mutation seam drift: ' + name)
            code = code.replace(before, after, 1)
        (production / production_file.name).write_text('#nullable enable\n' + code)
        shutil.copyfile(fixture / 'CardsRestore.csproj', build / 'CardsRestore.csproj')
        assembly = 'NativeBugFixCards_' + name.replace('-', '_')
        harmony = packages / 'harmonyx/2.7.0/lib/net45/0Harmony.dll'
        command = [str(Path.home() / '.dotnet/dotnet'), 'build', str(build / 'CardsRestore.csproj'), '-c', 'Release',
                   '--nologo', '--verbosity', 'quiet', '-p:CaseName=' + assembly,
                   '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production),
                   '-p:UnityManaged=' + str(unity.parent / 'Data/Managed'), '-p:GameManaged=' + str(game),
                   '-p:HarmonyPath=' + str(harmony)]
        result = subprocess.run(command, capture_output=True, text=True)
        (build / 'build.log').write_text(result.stdout + result.stderr)
        if result.returncode: raise SystemExit('Compilation is not a passing control:\n' + result.stdout + result.stderr)
        manifest['cases'].append({'name': name, 'dll': str(build / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected})
    manifest_path = run / 'manifest.json'; manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
    project = args.reuse_project.resolve() if args.reuse_project else run / 'unity'
    for directory in ('Assets/Editor', 'Assets/Plugins', 'Packages', 'ProjectSettings'):
        (project / directory).mkdir(parents=True, exist_ok=True)
    for case in manifest['cases']:
        shutil.copyfile(case['dll'], project / 'Assets/Plugins' / Path(case['dll']).name)
    for dll in game.glob('*.dll'):
        if (dll.name.startswith(('UnityEngine.', 'System.')) and dll.name not in ('UnityEngine.UI.dll', 'System.Runtime.CompilerServices.Unsafe.dll')) or dll.name in ('UnityEngine.dll', 'mscorlib.dll', 'netstandard.dll'):
            continue
        shutil.copyfile(dll, project / 'Assets/Plugins' / dll.name)
    for package, version, framework, name in deps:
        shutil.copyfile(packages / package / version / 'lib' / framework / (name + '.dll'), project / 'Assets/Plugins' / (name + '.dll'))
    shutil.copyfile(fixture / 'Editor/CardsRunner.cs', project / 'Assets/Editor/CardsRunner.cs')
    (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {'com.unity.modules.' + name: '1.0.0'
        for name in ('ui', 'imgui', 'animation', 'assetbundle', 'physics', 'audio', 'particlesystem')}}))
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    command = ['xvfb-run', '-a', str(unity), '-batchmode', '-force-glcore', '-projectPath', str(project),
               '-executeMethod', 'CardsRunner.Start', '-interactionManifest', str(manifest_path),
               '-nativeGameManaged', str(game), '-nativeHarmony', str(project / 'Assets/Plugins/0Harmony.dll'),
               '-logFile', str(run / 'unity.log')]
    try:
        result = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=480)
    finally:
        # Receipts and source inputs stay available. A one-off native proof must
        # not retain a second Unity import cache after success, failure or timeout.
        # Reuse is explicit: only its caller-selected project retains the cache.
        cleanup_generated_cache(project, keep_cache=args.reuse_project is not None)
    receipt = Path(manifest['result'])
    if receipt.is_file(): print(receipt.read_text(), end='')
    if result.returncode or not receipt.is_file(): raise SystemExit('FAIL original-native card restore; see ' + str(run / 'unity.log'))
    if production_file.read_text() != original: raise SystemExit('Production source changed during proof')
    print('PASS actual original Unity native card-restore, two causal negatives and two fail-open controls; evidence: ' + str(run))


def cleanup_generated_cache(project, keep_cache=False):
    if keep_cache:
        return
    for name in ('Library', 'Temp'):
        shutil.rmtree(project / name, ignore_errors=True)


if __name__ == '__main__': main()
