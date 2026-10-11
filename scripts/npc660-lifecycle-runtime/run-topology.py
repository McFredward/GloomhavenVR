#!/usr/bin/env python3
"""Challenge actual capture/receive/native replacement with a cold live hierarchy revision."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[2]
CONTROLS = {
    'structure': (
        '|| module.Binding.Structure != frame.Structure', '|| false',
        'pending same-identity native replacement preserves the continuously visible validated button panel'),
    'retention': (
        '|| preparingReplacement)', '|| preparingReplacement && false)',
        'pending same-identity native replacement preserves the continuously visible validated button panel'),
    'template': (
        'if (!Templates.TryGetValue(key, out GameObject? original) || original == null)',
        'if (!Templates.ContainsKey(key))',
        'prepared exact native original replaces the previous topology rather than retaining stale buttons'),
    'withdrawal': (
        '                bool preparingReplacement = module != null && needsCandidate;',
        '                if (RemoteRetry.TryGetValue(retryKey, out float delayed) && now < delayed) continue;\n'
        '                bool preparingReplacement = module != null && needsCandidate;',
        'owner withdrawal during native preparation hides immediately before the retry clock'),
    'identity': (
        '&& module.Address == frame.TemplateAddress', '&& true',
        'a changed original identity never retains preceding visible artwork during preparation'),
}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/npc660-lifecycle/topology')
    selected = parser.add_mutually_exclusive_group()
    selected.add_argument('--control', choices=CONTROLS)
    selected.add_argument('--controls', action='store_true', help='Run production and all five causal controls')
    args = parser.parse_args()
    root = args.source_root.resolve(); args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    fixture = run / 'fixture'
    shutil.copytree(root / 'scripts/town-service-mirror-runtime', fixture)
    (run / 'town-purse-runtime').symlink_to(root / 'scripts/town-purse-runtime', target_is_directory=True)
    topology = Path(__file__).with_name('Topology660.cs')
    shutil.copyfile(topology, fixture / topology.name)
    program = fixture / 'Program.cs'; text = program.read_text()
    anchor = '            if (variant == "production") PublisherNoCloth();'
    if text.count(anchor) != 1: raise RuntimeError('Production fixture entry drift')
    text = text.replace(anchor,
        '            if (suite == "lifecycle") { IEnumerator topology = Topology660(); '
        'while (topology.MoveNext()) yield return topology.Current; yield break; }\n' + anchor)
    program.write_text(text)
    checker = root / 'scripts/check-town-service-mirror.py'; source = checker.read_text()
    anchor = '    print(f"Production binding: {args.source_root.resolve()}; evidence: {run}", flush=True)'
    if source.count(anchor) != 1: raise RuntimeError('Production runner variant entry drift')
    variants = [('production', None, None, None, '')]
    controls = list(CONTROLS) if args.controls else [args.control] if args.control else []
    for control in controls:
        before, after, expected = CONTROLS[control]
        actual = (root / 'src/GloomhavenVR/Net/TownServices/TownServiceMirror.cs').read_text()
        if actual.count(before) != 1: raise RuntimeError('Control source binding drift: ' + control)
        variants.append(('control-' + control, 'TownServiceMirror.cs', before, after, expected))
    if args.control: variants = variants[1:]
    source = source.replace(anchor, '    variants = ' + repr(variants) + '\n' + anchor)
    bound_checker = run / 'bound-checker.py'; bound_checker.write_text(source)
    (run / 'variant.json').write_text(json.dumps({'control': args.control, 'all_controls': args.controls, 'variants': variants,
        'boundary': 'Production capture/codec/Receive/TickRemote and native neutralizer; ordinary Unity '
                    'Canvas/Image live hierarchy and delayed exact original are engine/source boundaries, not headset proof.',
        'sha256': {str(path): hashlib.sha256(path.read_bytes()).hexdigest()
                   for path in [Path(__file__), topology, checker, bound_checker]}}, indent=2) + '\n')
    command = ['python3', str(bound_checker), '--source-root', str(root), '--fixture-dir', str(fixture),
               '--suite', 'lifecycle', '--no-negative-controls', '--output-dir', str(run / 'proof')]
    print('Evidence: ' + str(run), flush=True)
    result = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (run / 'command.log').write_text(result.stdout)
    print(result.stdout, end='')
    compiled = {str(path.relative_to(run)): hashlib.sha256(path.read_bytes()).hexdigest()
                for path in (run / 'proof').glob('run-*/*/production/TownServiceMirror.cs')}
    (run / 'compiled-mirror-hashes.json').write_text(json.dumps(compiled, indent=2) + '\n')
    if result.returncode: raise SystemExit(result.returncode)


if __name__ == '__main__': main()
