#!/usr/bin/env python3
"""Execute native arch attachment queries in real Unity, with bounded causal controls.

The production helper, original arch geometry and actual sticky-restitution branch
are compiled. Native gameplay markers and the restitution write are explicit inert
boundaries. This proves ownership and integration, not headset/shader pixels.
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

ROOT = Path(__file__).resolve().parents[1]


def block(source, signature):
    assert source.count(signature) == 1, 'Production binding drift: ' + signature
    start = source.index(signature)
    opening = source.index('{', start)
    depth = 1
    for end in range(opening + 1, len(source)):
        depth += (source[end] == '{') - (source[end] == '}')
        if depth == 0:
            return source[start:end + 1]
    raise AssertionError('Unclosed production binding: ' + signature)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/arch-mounted-runtime')
    parser.add_argument('--unity', type=Path, default=Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity')))
    parser.add_argument('--variant', action='append', help='Only the named production/control scope; partial evidence')
    args = parser.parse_args()
    if not args.unity.is_file(): parser.error('Real Unity 2021.3.5 is required')
    base = args.source_root / 'src/GloomhavenVR/Core/WallFade'
    helper = (base / 'WallSegmentFade.ArchMounted.cs').read_text()
    gate = (base / 'WallSegmentFade.Gate.cs').read_text()
    mounted = (base / 'WallSegmentFade.Mounted.cs').read_text()
    fixture = ROOT / 'scripts/arch-mounted-runtime'
    metadata = json.loads((fixture / 'NativeArchMounts.json').read_text())
    root_literals = set(re.findall(r'"([^"]+)"', block(helper, 'private static readonly HashSet<string> NativeArchEffectRoots')))
    expected_roots = {root for root in metadata['roots'] if not any(term in root for term in ('Entrance', 'EXIT'))}
    assert root_literals == expected_roots and len(expected_roots) == 29, 'The complete inspected primitive family set must reach production'
    assert len(metadata['roots']) == 33 and len(metadata['bundles']) == 17, 'Review whole-game original doorway audit scope before altering its coverage'
    assert 'Light' not in re.sub(r'//[^\n]*', '', helper), 'Attachment queries must not access native lights'
    old = 'IsArchProtected(archProbe, f.Name ?? c.name)'
    combined = old + ' || IsArchMountedEffect(c, f.Name)'
    assert mounted.count(combined) == 1, 'Native attachment protection must reach the real mounted admission gate'
    sticky = block(mounted, 'if (IsArchMountedEffect(p.Renderer))')
    assert 'RestoreProp(p);' in sticky and '_mountedOwned.Remove(p.Renderer);' in sticky and 'continue;' in sticky, 'Native reparenting must restitute an old sticky owner before continuing'
    sticky_pos = mounted.index('if (IsArchMountedEffect(p.Renderer))')
    assert sticky_pos < mounted.index('bool carriedWallBuilt = false;', sticky_pos), 'Arch attachment restitution precedes every sticky handover'
    geometry = []
    for signature in ('internal struct ArchRect', 'private static float ArchContainmentFraction(', 'private static bool IsArchRectPiece(', 'private bool IsArchProtected(Bounds b, string name, out float containment)'):
        geometry.append(block(gate, signature))
    constants = '\n'.join(re.findall(r'private const float (?:ArchNameExtraWU|ArchContainmentMin|ArchOverhangWU) = [^;]+;', gate))
    scaffold = '#nullable enable\nusing System; using UnityEngine;\nnamespace GloomhavenVR.Core { internal static partial class WallSegmentFade { private sealed partial class FadeDriver {\n'
    geometry_source = scaffold + constants + '\n' + '\n'.join(geometry) + '\nprivate bool IsArchProtected(Bounds b,string name)=>IsArchProtected(b,name,out _);\n} } }\n'
    carrier_source = scaffold + 'private bool ReleaseCarried(MountedProp p) { foreach(var ignored in new[]{0}) { ' + sticky + ' } return p.Restored; }\n} } }\n'
    native_fixture = 'internal static class NativeArchFixture { internal static readonly string[][] Pairs = { ' + ','.join('new[] {' + ','.join(json.dumps(n) for n in pair) + '}' for pair in metadata['primitive_frame_pairs']) + '}; internal static readonly string[] CompositeRoots = {' + ','.join(json.dumps(root) for root in sorted(set(metadata['roots']) - expected_roots)) + '}; }\n'
    sources = {'Attachment.cs': helper, 'Geometry.cs': geometry_source, 'Carrier.cs': carrier_source, 'NativeFixture.cs': native_fixture, 'Bounds.cs': (base / 'WallCommitGeometryReads.cs').read_text()}
    variants = [('production', '', '', '', '')]
    variants += [
        ('primitive-unrecognized', 'Attachment.cs', 'if (!NativeArchEffectRoots.Contains(NativeArchName(node.name)))', 'if (bool.Parse("true"))', 'actual split-frame sibling torch stays protected'),
        ('foreign-root-admitted', 'Attachment.cs', 'if (!NativeArchEffectRoots.Contains(NativeArchName(node.name)))', 'if (node.name.IndexOf("_FRAME", StringComparison.Ordinal) < 0)', 'unknown similarly named containers cannot inherit'),
        ('foreign-mount-admitted', 'Attachment.cs', '&& NativeArchMountMeshes.Contains(filter.sharedMesh.name)', '&& true', 'native torch mesh identity cannot be inferred'),
        ('foreign-frame-admitted', 'Attachment.cs', '&& NativeArchFrameMeshes.Contains(filter.sharedMesh.name)', '&& true', 'native frame mesh identity cannot be inferred'),
        ('frame-geometry-ignored', 'Attachment.cs', '&& IsArchProtected(WallCommitGeometryReads.Read(frame), frame.name)', '&& true', 'an unprotected frame cannot exempt'),
        ('candle-layer-ignored', 'Attachment.cs', 'bool candleLayer = NativeArchName(name ?? renderer.name) == "CandleFlame";', 'bool candleLayer = false;', 'original sibling candle flame inherits'),
        ('actor-veto-lost', 'Attachment.cs', 'if (renderer.GetComponentInParent<ActorBehaviour>(true) != null\n                || renderer.GetComponentInParent<CInteractableActor>(true) != null)', 'if (bool.Parse("false"))', 'a gameplay actor never acquires'),
        ('sticky-release-omitted', 'Carrier.cs', 'if (IsArchMountedEffect(p.Renderer))', 'if (bool.Parse("false"))', 'already hidden frame effect restitutes'),
        ('verm-nested-route-lost', 'Attachment.cs', 'if (NativeArchName(child.name) == "ST_Vermling_Door_Split")', 'if (bool.Parse("false"))', 'the original Vermling nested frame supplies'),
    ]
    if args.variant:
        names = {v[0] for v in variants}
        if set(args.variant) - names: parser.error('Unknown variant: ' + ', '.join(set(args.variant) - names))
        variants = [v for v in variants if v[0] in args.variant]
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    (run/'source-hashes.json').write_text(json.dumps({'scope': 'Native Unity hierarchy/bounds; original attachment and arch-query sources; inert native gameplay markers and restitution write boundary', 'sources': {str(base/name): hashlib.sha256((base/name).read_bytes()).hexdigest() for name in ('WallSegmentFade.ArchMounted.cs', 'WallSegmentFade.Gate.cs', 'WallSegmentFade.Mounted.cs', 'WallCommitGeometryReads.cs')}, 'metadata_sha256': hashlib.sha256((fixture/'NativeArchMounts.json').read_bytes()).hexdigest(), 'original_prefab_count': 53, 'original_family_count': 33, 'primitive_families': 29, 'actual_frame_pairs': len(metadata['primitive_frame_pairs'])}, indent=2)+'\n')
    manifest = {'result': str(run/'results.txt'), 'cases': []}
    dotnet = shutil.which('dotnet') or str(Path.home()/'.dotnet/dotnet')
    for name, filename, before, after, expected in variants:
        build = run/name; production = build/'production'; production.mkdir(parents=True)
        for path, text in sources.items():
            if path == filename:
                assert text.count(before) == 1, 'Mutation binding drift: ' + name
                text = text.replace(before, after, 1)
            (production/path).write_text(text)
        project = build/'ArchMounted.csproj'; shutil.copyfile(fixture/'ArchMounted.csproj',project)
        assembly = 'ArchMounted_'+name.replace('-', '_')
        result = subprocess.run([dotnet,'build',str(project),'-c','Release','--nologo','--verbosity','quiet','-p:CaseName='+assembly,'-p:FixtureDir='+str(fixture),'-p:ProductionDir='+str(production),'-p:UnityManaged='+str(args.unity.parent/'Data/Managed')],capture_output=True,text=True)
        (build/'build.log').write_text(result.stdout+result.stderr)
        if result.returncode: raise SystemExit(result.stdout+result.stderr+'\nCompilation failure cannot be a passing negative control')
        manifest['cases'].append({'name':name,'dll':str(build/'bin/Release/netstandard2.1'/(assembly+'.dll')),'expected':expected})
    manifest_path = run/'manifest.json'; manifest_path.write_text(json.dumps(manifest,indent=2))
    project = run/'unity'; (project/'Assets/Editor').mkdir(parents=True); (project/'Packages').mkdir(); (project/'ProjectSettings').mkdir()
    shutil.copyfile(fixture/'Runner.txt',project/'Assets/Editor/InteractionRunner.cs')
    (project/'Packages/manifest.json').write_text('{"dependencies":{"com.unity.modules.physics":"1.0.0","com.unity.modules.particlesystem":"1.0.0"}}\n')
    (project/'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    result = subprocess.run([str(args.unity),'-batchmode','-nographics','-projectPath',str(project),'-executeMethod','InteractionRunner.Start','-interactionManifest',str(manifest_path),'-logFile',str(run/'unity.log')],stdout=subprocess.DEVNULL,stderr=subprocess.STDOUT,timeout=240)
    report = Path(manifest['result'])
    if report.is_file(): print(report.read_text(),end='')
    (run/'unity-exit-code.txt').write_text(str(result.returncode)+'\n')
    if result.returncode or not report.is_file(): raise SystemExit('FAIL: Unity replay; see '+str(run/'unity.log'))
    print('PASS '+str(len(variants))+' selected production/control scopes; evidence '+str(run))


if __name__ == '__main__': main()
