#!/usr/bin/env python3
"""Exercise exact production card diagnostics/blackout in actual Unity play-mode frames."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def sources(root):
    base = root / 'src/GloomhavenVR/Cards/Art'
    half = (base / 'CardHalfTone.cs').read_text()
    face = (base / 'CardFace.cs').read_text()
    # Whole production diagnostic region, including native UI sampling, shader diff and verdict.
    # Only surrounding gameplay dependencies are boundaries; no rewritten scheduler/blackout model.
    prefix = half[half.index('    private static readonly HashSet<FullAbilityCard> CensusRegistry'):half.index('    /// <summary>True once the "what this fixed"')]
    assert prefix.count('Resources.FindObjectsOfTypeAll<FullAbilityCard>()') == 1
    prefix = prefix.replace('Resources.FindObjectsOfTypeAll<FullAbilityCard>()', 'CardDiagnosticProbe.FindAllCards()')
    census = half[half.index('    private struct Bucket'):half.rfind('\n}')]
    reset = half[half.index('    internal static void Reset()'):half.index('    // ------------------------------------------------------------------- the gate')]
    blackout = face[face.index('    private static class FaceBlackout'):face.index('    /// <summary>\n    /// (Re)compute the face-to-host')]
    # Inert work counters make the expensive geometry/probe/format paths observable. Some Unity
    # Mono builds return zero from GetAllocatedBytesForCurrentThread even after a known allocation,
    # so allocation data alone cannot establish that the old hidden inventory stopped running.
    for before, after in (
        ('img.rectTransform.GetWorldCorners(corners);', 'CardDiagnosticProbe.GeometryWalks++; img.rectTransform.GetWorldCorners(corners);'),
        ('probes++;', 'probes++; CardDiagnosticProbe.FootprintProbes++;'),
        ('if (report != null)\n                {', 'if (report != null)\n                {\n                    CardDiagnosticProbe.InventoryEntries++;'),
    ):
        if blackout.count(before) != 1: raise SystemExit('Production work-counter binding drift: ' + before)
        blackout = blackout.replace(before, after, 1)
    patches = (root / 'src/GloomhavenVR/Cards/Patches/CardArtPatches.cs').read_text()
    lifetime = patches[patches.index('[HarmonyPatch(typeof(FullAbilityCard), \"OnEnable\")]'):]
    assert 'RegisterCensusFace(face);' in half[half.index('internal static void Observe('):half.index('internal static void Observe(') + 350]
    return {
        'Lifetime.cs': 'using HarmonyLib;\nnamespace GloomhavenVR.Cards.Patches;\n' + lifetime,
        'Census.cs': 'using System.Collections.Generic;\nusing Stopwatch = System.Diagnostics.Stopwatch;\nusing GloomhavenVR.Core;\nusing UnityEngine;\nusing UnityEngine.UI;\nnamespace GloomhavenVR.Cards { internal static partial class CardHalfTone {\n' + prefix + reset + census + '\n} }\n',
        'Blackout.cs': 'using System.Collections.Generic;\nusing GloomhavenVR.Core;\nusing UnityEngine;\nusing UnityEngine.UI;\nnamespace GloomhavenVR.Cards { internal static partial class CardFace {\n' + blackout + '\n} }\n',
    }, {'CardHalfTone.cs': half, 'CardFace.cs': face, 'CardArtPatches.cs': patches}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/card-diagnostic-runtime')
    parser.add_argument('--unity', type=Path, default=Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity')))
    parser.add_argument('--no-negative-controls', action='store_true')
    parser.add_argument('--case', action='append', help='Run named variants only; repeat for focused follow-up')
    args = parser.parse_args()
    ui = args.source_root / 'ressources/GH_Data/Managed/UnityEngine.UI.dll'
    if not args.unity.is_file() or not ui.is_file(): parser.error('Real Unity and read-only native UI assembly required; no silent skip')
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    fixture = ROOT / 'scripts/card-diagnostic-runtime'
    production, originals = sources(args.source_root)
    (run / 'source-hashes.json').write_text(json.dumps({'root': str(args.source_root.resolve()), 'sha256': {name: hashlib.sha256(code.encode()).hexdigest() for name, code in originals.items()}}, indent=2) + '\n')
    variants = [('production', '', '', '', '')]
    if not args.no_negative_controls:
        variants += [
            ('missing-enable-registration', 'Lifetime.cs', 'internal static class FullAbilityCard_OnEnable_CensusLifetime\n{\n    private static void Postfix(FullAbilityCard __instance) => CardHalfTone.RegisterCensusFace(__instance);\n}', 'internal static class FullAbilityCard_OnEnable_CensusLifetime\n{\n    private static void Postfix(FullAbilityCard __instance) { }\n}', 'original Unity OnEnable registers newly created active cards'),
            ('recurring-resource-discovery', 'Census.cs', 'CensusRegistry.RemoveWhere(face => face == null || !face.gameObject.scene.IsValid());', 'SeedCensusRegistry(); CensusRegistry.RemoveWhere(face => face == null || !face.gameObject.scene.IsValid());', 'interactive diagnostics never query the native resource heap again'),
            ('normal-census', 'Census.cs', 'if (!VRLog.WantsDebug)', 'if (!VRLog.WantsDebug && Time.frameCount < 0)', 'normal logging never discovers or samples a census'),
            ('same-frame-census', 'Census.cs', 'if (s_censusFrame == Time.frameCount)', 'if (s_censusFrame == Time.frameCount && Time.frameCount < 0)', 'many card offers cannot multiply one frame census work'),
            ('whole-scene-census', 'Census.cs', 'private const int CensusFacesPerFrame = 8;', 'private const int CensusFacesPerFrame = 100000;', 'census samples at most eight faces per actual frame'),
            ('counter-dedup', 'Census.cs', 'string line = $"compared {compared}', 'string line = $"#{s_censuses}: compared {compared}', 'unchanged census does not log again merely because its counter changed'),
            ('reset-retains-snapshot', 'Census.cs', 's_nextCensus = 0f;\n        s_censusFaces = null;', 's_nextCensus = 0f;\n        // reset retained the old diagnostic snapshot', 'production scene Reset releases every diagnostic scene reference'),
            ('repeated-inventory', 'Blackout.cs', 'VRLog.WantsDebug && !s_logged[(int)kind];', 'VRLog.WantsDebug && (!s_logged[(int)kind] || !s_loggedMuted[(int)kind]);', 'zero-result maintenance never rebuilds a hidden full inventory'),
            ('bright-geometry', 'Blackout.cs', 'if (!opaqueDark && report == null)', 'if (!opaqueDark && report == null && Time.frameCount < 0)', 'bright and art-bearing graphics skip unnecessary geometry and footprint probes'),
            ('arrival-rate-limit', 'Blackout.cs', 'if (!artJustArrived && s_nextPass.TryGetValue', 'if (s_nextPass.TryGetValue', 'arrival corrects a newly loaded quad in the same frame'),
            ('invisible-arriving-art', 'Blackout.cs', 'img.color = tracked.Orig;', 'img.color = tracked.Muted;', 'newly arriving sprite art is immediately restored'),
            ('no-strip-correction', 'Blackout.cs', 'bool qualifies = backdropTier || stripTier;', 'bool qualifies = backdropTier;', 'thin edge strips retain tier B correction'),
        ]
    if args.case:
        names = set(args.case)
        unknown = names - {entry[0] for entry in variants}
        if unknown: parser.error('Unknown cases: ' + ', '.join(sorted(unknown)))
        variants = [entry for entry in variants if entry[0] in names]
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    manifest = {'result': str(run / 'results.txt'), 'cases': []}
    for name, filename, before, after, expected in variants:
        build = run / name
        generated = build / 'production'
        generated.mkdir(parents=True)
        for path, code in production.items():
            if path == filename:
                if code.count(before) != 1: raise SystemExit('Production mutation binding drift: ' + name)
                code = code.replace(before, after, 1)
                if name == 'whole-scene-census':
                    code = code.replace('private const double CensusBudgetSeconds = 0.001;', 'private const double CensusBudgetSeconds = 1.0;', 1)
            (generated / path).write_text(code)
        project = build / 'Diagnostic.csproj'
        shutil.copyfile(fixture / 'Diagnostic.csproj', project)
        assembly = 'CardDiagnostic_' + name.replace('-', '_')
        result = subprocess.run([dotnet, 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet', '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(generated), '-p:UnityManaged=' + str(args.unity.parent / 'Data/Managed'), '-p:UnityUi=' + str(ui)], capture_output=True, text=True)
        (build / 'build.log').write_text(result.stdout + result.stderr)
        if result.returncode: raise SystemExit(result.stdout + result.stderr + '\nCompilation failure is not a passing negative control')
        manifest['cases'].append({'name': name, 'dll': str(build / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected})
    manifest_path = run / 'manifest.json'
    project = run / 'unity'
    (project / 'Assets/Editor').mkdir(parents=True)
    (project / 'Assets/Plugins').mkdir()
    (project / 'Packages').mkdir()
    (project / 'ProjectSettings').mkdir()
    for entry in manifest['cases']:
        destination = project / 'Assets/Plugins' / Path(entry['dll']).name
        shutil.copyfile(entry['dll'], destination)
        for dependency in Path(entry['dll']).parent.glob('*.dll'):
            if dependency.name != Path(entry['dll']).name:
                shutil.copyfile(dependency, project / 'Assets/Plugins' / dependency.name)
        entry['dll'] = str(destination)
    manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
    shutil.copyfile(fixture / 'Editor/DiagnosticRunner.cs', project / 'Assets/Editor/DiagnosticRunner.cs')
    (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    result = subprocess.run(['xvfb-run', '-a', str(args.unity), '-batchmode', '-projectPath', str(project), '-executeMethod', 'DiagnosticRunner.Start', '-interactionManifest', str(manifest_path), '-logFile', str(run / 'unity.log')], stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=360)
    for cache in ('Library', 'Temp'):
        shutil.rmtree(project/cache, ignore_errors=True)
    report = Path(manifest['result'])
    if report.is_file(): print(report.read_text(), end='')
    if result.returncode or not report.is_file(): raise SystemExit('FAIL: Unity run; see ' + str(run / 'unity.log'))
    print('PASS: ' + str(len(variants)) + ' production/negative variants; evidence: ' + str(run))


if __name__ == '__main__': main()
