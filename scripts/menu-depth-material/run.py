#!/usr/bin/env python3
"""Native story dissolve parity on shipping shader, actual uGUI and production clone sync."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import shutil
import signal
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run
sys.path.insert(0, str(ROOT / 'scripts/menu-depth-character'))
from project_compilers import cleanup_project_compilers

NATIVE_PIXELS = 'spatial dissolve preserves the exact native fragment and blend pixels'


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def replace_once(code, before, after):
    assert code.count(before) == 1, (before, code.count(before))
    return code.replace(before, after, 1)


def execute(argv, log, timeout=240):
    with log.open('w') as stream:
        child = subprocess.Popen(argv, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            return child.wait(timeout=timeout)
        finally:
            if child.poll() is None:
                os.killpg(child.pid, signal.SIGKILL); child.wait()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--case', action='append')
    parser.add_argument('--native-evidence', type=Path, default=Path('/home/claw/gloomhaven_vr/.planning/debug/menu-depth723/material-audit/run-ohye58v4'))
    args = parser.parse_args()
    output = ROOT / '.planning/debug/menu-depth-material'; output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=output)); completion = run / 'completion.log'
    completion.write_text('Evidence: ' + str(run) + '\n'); print('Evidence: ' + str(run), flush=True)
    status = 1
    try:
        evidence = args.native_evidence
        required = {
            'native-dissolve-progVertex.dxbc': '076c53f158c731b2703f8baa965b0b668ddd6df4f2f07875ca6e081db5592fbe',
            'native-dissolve-progFragment.dxbc': '6759a3905d6da9e7464b1c344227dd6e2ab902265e3c08d7b16af1b5a7fee65e',
            'native-dissolve-mask.bc1-mips.bin': 'b1ee5261a7bbcbe5098044d8dd152ded213b3df691657836385b9628673a9ceb'
        }
        for name, expected in required.items():
            if not (evidence / name).is_file() or digest(evidence / name) != expected:
                raise RuntimeError('Exact original shader/mask evidence is absent or changed: ' + str(evidence / name))
        retained = run / 'native-evidence'; retained.mkdir()
        for name in [*required, 'native-programs.json', 'native-fragment.dxbc.asm', 'native-vertex.dxbc.asm',
                     'native-signatures.json', 'shader-evidence.json', 'color-space-source.json', 'native-mask-payload.json']:
            shutil.copyfile(evidence / name, retained / name)
        native_pins = {name: digest(retained / name) for name in [p.name for p in retained.iterdir()]}
        (run / 'native-evidence-pins.json').write_text(json.dumps({'source': str(evidence), 'sha256': native_pins,
            'limit': 'Original native programs are D3D11-only. GL uses an independently compiled source reference transcribed from direct DXBC, with the original BC1 mip payload and an independent CPU scalar/blend oracle.'}, indent=2) + '\n')
        source = ROOT / 'src/GloomhavenVR/WorldUI/MenuDepth'
        names = ('IMenuDepthSource.cs', 'MenuDepthEye.cs', 'MenuDepthSources.cs', 'MenuDepthProjection.cs',
                 'MenuDepthDisplay.cs', 'MenuDepthHeadRaster.cs', 'MenuDepthHeadCarrier.cs', 'MenuDepthHeightfield.cs', 'MenuDepthGraphic.cs')
        sources = {name: (source / name).read_text() for name in names}
        fixture = ROOT / 'tests/GloomhavenVR.MenuDepthMaterialTests'
        cases = [
            ('production', '', '', '', ''),
            ('native-effect-disabled', 'MenuDepthGraphic.cs', '_depthMaterial.SetFloat(DissolveId, _nativeDissolve ? 1f : 0f);',
             '_depthMaterial.SetFloat(DissolveId, 0f);', NATIVE_PIXELS),
            ('native-blend-lost', 'MenuDepthGraphic.cs', '_depthMaterial.SetFloat(SourceBlendId, _nativeDissolve ? 5f : 1f);',
             '_depthMaterial.SetFloat(SourceBlendId, 1f);', NATIVE_PIXELS),
            ('native-clock-frozen', 'MenuDepthGraphic.cs', 'public void BeforeCapture(MenuDepthEye eye)\n    {\n        SyncNativeEffect();',
             'public void BeforeCapture(MenuDepthEye eye)\n    {\n        /* injected: original material clock is not polled */', NATIVE_PIXELS),
            ('native-first-copy-lost', 'MenuDepthGraphic.cs', 'SyncNativeEffect();\n        return _depthMaterial;',
             'return _depthMaterial;', NATIVE_PIXELS),
            ('native-mask-offset-frozen', 'MenuDepthGraphic.cs', '_depthMaterial.SetTextureOffset("_Mask", source.GetTextureOffset("_Mask"));',
             '/* injected: original mask transform clock is not polled */', NATIVE_PIXELS),
            ('native-main-offset-frozen', 'MenuDepthGraphic.cs', '_depthMaterial.SetTextureOffset("_MainTex", source.GetTextureOffset("_MainTex"));',
             '/* injected: original main transform clock is not polled */', NATIVE_PIXELS),
            ('native-mask-instance-frozen', 'MenuDepthGraphic.cs', '_depthMaterial.SetTexture(MaskId, source.GetTexture(MaskId));',
             '/* injected: original mask texture instance is not polled */', NATIVE_PIXELS),
            ('native-edge-frozen', 'MenuDepthGraphic.cs', '_depthMaterial.SetFloat(EdgeWeightId, source.GetFloat(EdgeWeightId));',
             '/* injected: original edge weight is not polled */', NATIVE_PIXELS),
        ]
        load = '_depthShader = _bundle.LoadAsset<Shader>(DepthShaderPath);'
        variants = [
            ('shader-premultiplied-native', 'NativePremultiplied', 'if (color.a <= 0) discard;\n                    return color;',
             'if (color.a <= 0) discard;\n                    color.rgb *= color.a;\n                    return color;', NATIVE_PIXELS),
            ('shader-saturated-native', 'NativeSaturated', 'color.a = alpha * _EdgeWeight;',
             'color.a = saturate(alpha * _EdgeWeight);', NATIVE_PIXELS),
            ('shader-mask-uses-main-uv', 'NativeMaskUsesMainUV', 'o.dissolveUV = TRANSFORM_TEX(v.uv, _Mask);',
             'o.dissolveUV = o.uv;', NATIVE_PIXELS),
            ('shader-native-ui-tint', 'NativeUsesUIColor', 'o.color = _GvrMenuDepthDissolve > 0.5 ? v.color : v.color * _Color;',
             'o.color = v.color * _Color;', NATIVE_PIXELS),
            ('shader-empty-depth-write', 'NativeEmptyDepth', 'if (color.a <= 0) discard;',
             '/* injected: invisible native pixels still write depth */',
             'native exactly-zero dissolve alpha cannot occlude later artwork through the depth buffer'),
        ]
        for name, shader_name, before, after, expected in variants:
            cases.append((name, 'MenuDepthDisplay.cs', load, '_depthShader = Shader.Find("GloomhavenVR/MenuDepthGraphic.' + shader_name + '");', expected))
        if args.case:
            selected = set(args.case); assert selected <= {c[0] for c in cases}
            cases = [c for c in cases if c[0] in selected]
        project = run / 'unity'
        for directory in ('Assets/Editor', 'Assets/Controls', 'Packages', 'ProjectSettings'): (project / directory).mkdir(parents=True)
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        (project / 'Packages/manifest.json').write_text(json.dumps({'dependencies': {'com.unity.ugui': '1.0.0'}}) + '\n')
        shutil.copyfile(Path(__file__).parent / 'Editor/Runner.cs', project / 'Assets/Editor/Runner.cs')
        shutil.copyfile(fixture / 'NativeDissolveReference.shader', project / 'Assets/NativeDissolveReference.shader')
        rendering = ROOT / 'src/GloomhavenVR/Assets/MenuDepth/Rendering'
        original_shader = (rendering / 'MenuDepthGraphic.shader').read_text()
        for name, shader_name, before, after, expected in variants:
            if name not in {c[0] for c in cases}: continue
            changed = replace_once(original_shader, 'Shader "GloomhavenVR/MenuDepthGraphic"', 'Shader "GloomhavenVR/MenuDepthGraphic.' + shader_name + '"')
            (project / 'Assets/Controls' / (shader_name + '.shader')).write_text(replace_once(changed, before, after))
        pins = [*(source / name for name in names), *fixture.glob('*'), Path(__file__), Path(__file__).parent / 'Editor/Runner.cs',
                ROOT / 'scripts/menu-depth-character/project_compilers.py', rendering / 'MenuDepthGraphic.shader',
                rendering / 'NativeDissolve.provenance.json', rendering / 'menu-depth.bundle', rendering / 'provenance.json']
        hashes = {str(path.relative_to(ROOT)): digest(path) for path in pins if path.is_file()}
        (run / 'source-pins.json').write_text(json.dumps({'sha256': hashes,
            'boundaries': 'Native material animation values are explicit property inputs on the same original material without SetMaterialDirty. Actual root MenuDepthGraphic BeforeCapture/GetModifiedMaterial sync, actual shipping shader bundle, actual UI sprite/mask/colour/CanvasGroup and camera rendering execute.',
            'limits': 'No native D3D11 program execution, original tween scheduling, full game prefab, XR/headset or multiplayer claim. Native equation/state are bound to direct original DXBC and the real BC1 mip chain; independent source shader and CPU oracle establish GL parity.'}, indent=2) + '\n')
        manifest = {'evidence': str(run), 'result': str(run / 'results.txt'), 'mask': str(retained / 'native-dissolve-mask.bc1-mips.bin'), 'cases': []}
        for name, filename, before, after, expected in cases:
            case = run / name; bound = case / 'production'; bound.mkdir(parents=True)
            for actual, code in sources.items():
                (bound / actual).write_text(replace_once(code, before, after) if filename == actual else code)
            csproj = case / 'MenuDepthMaterial.csproj'; shutil.copyfile(fixture / csproj.name, csproj)
            assembly = 'MenuDepthMaterial_' + name.replace('-', '_')
            command = ['/home/claw/.dotnet/dotnet', 'build', str(csproj), '-c', 'Release', '-v', 'quiet', '--nologo',
                       '--disable-build-servers', '-nr:false', '-p:UseSharedCompilation=false', '-p:CaseName=' + assembly,
                       '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(bound),
                       '-p:UnityManaged=/home/claw/unity-2021.3.5/Editor/Data/Managed',
                       '-p:GameManaged=' + str(ROOT / 'ressources/GH_Data/Managed'), '-p:DepthBundle=' + str(rendering / 'menu-depth.bundle')]
            if execute(command, case / 'build.log'): raise RuntimeError('Compilation failure is not a causal control pass: ' + name)
            dll = run / 'assemblies' / (assembly + '.dll'); dll.parent.mkdir(exist_ok=True)
            shutil.copyfile(case / 'bin/Release/net472' / dll.name, dll)
            manifest['cases'].append({'name': name, 'dll': str(dll), 'expected': expected})
        (run / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
        command = ['xvfb-run', '-a', '/home/claw/unity-2021.3.5/Editor/Unity', '-batchmode', '-force-glcore',
                   '-projectPath', str(project), '-executeMethod', 'MenuDepthMaterialRunner.Start',
                   '-menuDepthMaterialManifest', str(run / 'manifest.json'), '-logFile', str(run / 'unity.log')]
        (run / 'command.json').write_text(json.dumps(command, indent=2) + '\n')
        status = execute(command, run / 'launch.log', 420)
        (run / 'exit-code.txt').write_text(str(status) + '\n')
        print((run / 'results.txt').read_text() if (run / 'results.txt').exists() else 'No Unity results', flush=True)
        stable = all(digest(ROOT / path) == sha for path, sha in hashes.items())
        (run / 'source-stability.json').write_text(json.dumps({'unchanged': stable, 'inputs': len(hashes)}) + '\n')
        if not stable: status = 1; raise RuntimeError('Pinned sources changed during the proof')
        return status
    finally:
        compilers = cleanup_project_compilers(run / 'unity', editor_reaped=True)
        (run / 'compiler-cleanup.json').write_text(json.dumps(compilers, indent=2) + '\n')
        if compilers['remaining']: raise RuntimeError('Project compiler survived; its generated caches are retained.')
        cleanup = cleanup_completed_run('menu-depth-material', status, completion, ROOT, process_reaped=True)
        (run / 'cache-cleanup.json').write_text(json.dumps(cleanup, indent=2) + '\n')


if __name__ == '__main__': raise SystemExit(main())
