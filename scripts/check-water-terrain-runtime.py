#!/usr/bin/env python3
"""Run complete production water adoption against real Unity renderer/material lifetimes."""
import argparse
from concurrent.futures import ThreadPoolExecutor
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run


def method(source, signature):
    start = source.index(signature); body = source.index('{', start); end = body + 1; depth = 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}'); end += 1
    return source[start:end]


def integration_source(root):
    core_path = root / 'src/GloomhavenVR/Core/CoreModule.cs'
    env_path = root / 'src/GloomhavenVR/Core/Perf/ScenarioEnvironmentBudget.cs'
    core = core_path.read_text(); env = env_path.read_text()
    start = core.index('ScenarioEnvironmentBudget.ConfigureWorldMaterialIntegration(')
    ready = method(core[start:], 'renderer =>')
    assert 'WaterTerrainVR.MaterialReady(renderer);' in ready, 'actual CoreModule callback is missing water publication'
    assert ready.index('WaterTerrainVR.MaterialReady(renderer);') < ready.index('WorldMaterialBudget.MaterialReady(renderer);'), 'water completion must precede generic material substitution'
    native_ready = method(env, 'internal static void MaterialReady(Renderer renderer)')
    ready_patch = method(env, 'internal static class MaterialLoaderData_Ready_EnvironmentBudgetPatch')
    wrapped = """using System; using HarmonyLib; using UnityEngine;
namespace GloomhavenVR.Core {
internal static class NativePublicationIntegration {
 public static void Configure() {
  VRSession.Harmony = new Harmony("water.native." + typeof(VRSession).Assembly.GetName().Name);
  ScenarioEnvironmentBudget.Bind(READY_LAMBDA);
  VRSession.Harmony.PatchAll(typeof(MaterialLoaderData_Ready_EnvironmentBudgetPatch));
 }
 public static void Shutdown() => VRSession.Harmony?.UnpatchSelf();
}
internal static class WorldMaterialBudget { internal static int Calls; internal static void MaterialReady(Renderer renderer) { Calls++; } }
internal sealed class NativeEnvironmentDriver { internal void MaterialReady(Renderer renderer) { } }
internal static class ScenarioEnvironmentBudget {
 private static Action<Renderer>? _worldReady, _terrainReady = null, _wallRendererReady = null;
 private static bool _failed = false; private static NativeEnvironmentDriver? _driver = null;
 internal static void Bind(Action<Renderer> ready) => _worldReady = ready;
 internal static void BeforeNativeContentChange() { }
 internal static void BeforeNativeRendererWrite(Renderer renderer) { }
 internal static void StopAfterFailure(Exception error) => throw new InvalidOperationException("native publication failed", error);
 READY_METHOD
}
[HarmonyPatch(typeof(MaterialLoaderData), "CheckAllMaterialLoaded")]
READY_PATCH
}
"""
    return [core_path, env_path], wrapped.replace('READY_LAMBDA', ready).replace('READY_METHOD', native_ready).replace('READY_PATCH', ready_patch)


def coverage_source(path):
    coverage = json.loads(path.read_text()); materials = coverage['materials']; surfaces = coverage['surfaces']
    assert len(materials) == 14 and len(surfaces) == 45
    literal = lambda value: json.dumps(value, ensure_ascii=True)
    number = lambda value: repr(float(value)) + 'f'
    vector = lambda value: ','.join(number(value[k]) for k in ('r','g','b','a'))
    material_rows = []
    for sample in materials:
        saved = sample['savedNative']; declared = sample['declaredConsumedProperties']
        strength = saved.get('_DetailOpacityBaseNormalStr') or dict(r=0,g=0,b=0,a=0)
        material_rows.append('new NativeWaterMaterial(' + ','.join((literal(sample['name']), literal(sample['shader']), str(sample['customRenderQueue']),
            'new Color(' + vector(saved['_Color_Tint']) + ')', 'new Vector4(' + vector(saved['_NormalTilings']) + ')', number(saved['_Smoothness']),
            'true' if '_DetailOpacityBaseNormalStr' in declared else 'false', 'new Vector4(' + vector(strength) + ')')) + ')')
    index = {sample['id']: i for i, sample in enumerate(materials)}
    surface_rows = []
    for sample in surfaces:
        assert sample['replaceWaterFilm'] and sample['rendererType'] == 'MeshRenderer' and sample['slot'] == 0
        surface_rows.append('new NativeWaterSurface(' + literal(sample['source'] + ':' + str(sample['rendererPathId'])) + ',new string[]{'
            + ','.join(literal(n) for n in sample['ancestry']) + '},' + str(index[sample['material']]) + ')')
    return """using UnityEngine;
public readonly struct NativeWaterMaterial {
 public readonly string Name, ShaderName; private readonly int Queue; private readonly Color Tint;
 private readonly Vector4 Tilings, Strength; private readonly float Smoothness; private readonly bool HasStrength;
 public NativeWaterMaterial(string name,string shader,int queue,Color tint,Vector4 tilings,float smoothness,bool hasStrength,Vector4 strength)
 {Name=name;ShaderName=shader;Queue=queue;Tint=tint;Tilings=tilings;Smoothness=smoothness;HasStrength=hasStrength;Strength=strength;}
 public Material Create() { var m=new Material(Shader.Find(ShaderName));m.name=Name;m.renderQueue=Queue;
  m.SetColor("_Color_Tint",Tint);m.SetVector("_NormalTilings",Tilings);m.SetFloat("_Smoothness",Smoothness);
  m.SetTexture("_Normal_Map",Texture2D.normalTexture);if(HasStrength)m.SetVector("_DetailOpacityBaseNormalStr",Strength);return m; }
}
public readonly struct NativeWaterSurface { public readonly string Name;public readonly string[] Ancestry;public readonly int Material;
 public NativeWaterSurface(string name,string[] ancestry,int material){Name=name;Ancestry=ancestry;Material=material;} }
public static class NativeWaterCoverage {
 public static readonly NativeWaterMaterial[] Materials = {MATERIAL_ROWS};
 public static readonly NativeWaterSurface[] Surfaces = {SURFACE_ROWS};
}
""".replace('MATERIAL_ROWS', ','.join(material_rows)).replace('SURFACE_ROWS', ','.join(surface_rows))

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/water-terrain-runtime')
    parser.add_argument('--unity', type=Path, default=Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity')))
    parser.add_argument('--case', action='append')
    parser.add_argument('--integration-root', type=Path, help='Read actual current CoreModule/native-ready source integration')
    parser.add_argument('--coverage', type=Path, help='Read-only native full-game surface catalog; defaults to source-root/tools/water-audit/water-surface-coverage.json')
    args = parser.parse_args(); root = args.source_root.resolve()
    fixture = ROOT / 'tests/water-terrain-runtime'
    paths = sorted((root / 'src/GloomhavenVR/Core/Water').glob('*.cs'))
    sources = {p.name: p.read_text() for p in paths}
    bridge_paths, bridge = integration_source((args.integration_root or root).resolve())
    coverage = (args.coverage or root / 'tools/water-audit/water-surface-coverage.json').resolve()
    sources['NativePublicationIntegration.cs'] = bridge
    sources['NativeCoverage.cs'] = coverage_source(coverage)
    provenance_path = fixture / 'native-publication-provenance.json'
    provenance = json.loads(provenance_path.read_text())
    native_fixture = fixture / 'NativeMaterialPublication.cs'
    native_method = method(native_fixture.read_text(), '\tprivate void CheckAllMaterialLoaded()')
    assert hashlib.sha256(native_method.encode()).hexdigest() == provenance['methodSha256'], 'native method fixture drift'
    native_source = (args.integration_root or root) / 'decompiled/GH.Runtime/MaterialLoaderData.cs'
    native_verified = native_source.is_file()
    if native_verified:
        actual_method = method(native_source.read_text(), '\tprivate void CheckAllMaterialLoaded()')
        assert actual_method == native_method, 'actual installed native material publication differs'
        bridge_paths.append(native_source)
    variants = [('production', sources, ''), ('queue-pixels', sources, '')]
    changes = [
        ('async-ready-lost', '_readyMaterials.Add(renderer);', '/* lost ready event */', 'asynchronous publication after empty seed adopts in the same frame'),
        ('inactive-ready-lost', 'if (renderer == null || !Want || !_owned.ContainsKey(renderer)', 'if (renderer == null || !renderer.gameObject.activeInHierarchy || !Want || !_owned.ContainsKey(renderer)', 'inactive preview water receives the material before first visible frame'),
        ('terrain-family-missed', 'node.GetComponent<ProceduralBase>() != null || IsTerrainWaterName(node.name)', 'IsTerrainWaterName(node.name)', 'verified DLC terrain ancestry: DLC_SC11_WaterPlane_MAT'),
        ('mixed-slots-replaced', 'if (material == null || isFilm && !IsNativeWaterMaterial(material)) continue;', 'if (material == null) continue;', 'unrelated material slot remains the original shared material'),
        ('later-slot-overwrite-missed', 'for (int slot = 0; unchanged && slot < o.Instances.Length; slot++)', 'for (int slot = 0; unchanged && slot < Math.Min(1, o.Instances.Length); slot++)', 'later-slot native quality replacement re-adopts without slot-zero guard'),
        ('native-restore-overwritten', 'if (changed) r.sharedMaterials = _enforceScratch.ToArray();', 'if (changed || bool.Parse("true")) r.sharedMaterials = o.Shared;', 'restoration retains later native unrelated material writes'),
        ('ready-overflow-dropped', 'DrainReadyMaterials();\n            }\n            _readyMaterials.Add(renderer);', 'return;\n            }\n            _readyMaterials.Add(renderer);', 'bounded ready burst preserves every renderer'),
        ('reset-seed-retained', '            _seenTiles.Clear();\n            _activeTiles.Clear();', '            _activeTiles.Clear();', 'VR re-entry seeds prior tiles after ownership reset'),
    ]
    for name, before, after, expected in changes:
        text = sources['WaterTerrainVR.cs']
        assert text.count(before) == 1, (name, text.count(before))
        changed = dict(sources); changed['WaterTerrainVR.cs'] = text.replace(before, after, 1)
        variants.append((name, changed, expected))
    bad_queue = dict(sources)
    assert sources['WaterTerrainVR.cs'].count('inst.renderQueue = FilmRenderQueue(shared.renderQueue);') == 1
    bad_queue['WaterTerrainVR.cs'] = sources['WaterTerrainVR.cs'].replace('inst.renderQueue = FilmRenderQueue(shared.renderQueue);', 'inst.renderQueue = shared.renderQueue;', 1)
    variants.append(('queue-old-opaque-order', bad_queue, 'opaque queue film is visible above native ground'))
    if args.case:
        selected = set(args.case); variants = [v for v in variants if v[0] in selected]
        assert len(variants) == len(selected), 'unknown selected case'
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    print('evidence: ' + str(run), flush=True)
    wrapper = run / 'completion.log'; wrapper.write_text('evidence: ' + str(run) + '\n')
    deps = [Path.home() / '.nuget/packages' / p for p in (
        'harmonyx/2.7.0/lib/net45/0Harmony.dll', 'monomod.runtimedetour/21.12.13.1/lib/net452/MonoMod.RuntimeDetour.dll',
        'monomod.utils/21.12.13.1/lib/net452/MonoMod.Utils.dll', 'mono.cecil/0.11.4/lib/net40/Mono.Cecil.dll')]
    assert all(p.is_file() for p in deps)
    shaders = [root / 'unity/GloomhavenVR.Assets/Assets/Bundle/Environments/WaterVR.shader', root / 'unity/GloomhavenVR.Assets/Assets/Bundle/Table/Overlay.shader']
    # The fallback need not run in positive cases; only the actual water shader is required.
    shaders = [p for p in shaders if p.is_file()]
    includes = list(shaders[0].parent.glob('WaterVR*.cginc'))
    inputs = paths + bridge_paths + [coverage] + shaders + includes + list(p for p in fixture.rglob('*') if p.is_file()) + [Path(__file__).resolve()]
    hashes = {str(p): hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs}
    (run / 'integration-bindings.json').write_text(json.dumps({'nativeMethodFreshlyVerified': native_verified, 'nativeMethodProvenance': provenance, 'currentCoreModule': str(bridge_paths[0]), 'currentNativeReady': str(bridge_paths[1]), 'nativeCoverage': str(coverage), 'boundary': 'Original method source extraction and real HarmonyX postfix; original game/controller assembly and Addressables callbacks remain outside this focused fixture.'}, indent=2) + '\n')
    (run / 'source-hashes.json').write_text(json.dumps({'sha256': hashes, 'cases': [v[0] for v in variants], 'boundary':
        'Complete production water driver and constants; actual Unity material slots, shader assets, inactive/destroyed objects, bounds and probes. Native loading/procedural callbacks, scene registry, VR state and bundle resolver are explicit API boundaries. No headset pixel/FPS or actual native Addressables execution claim.'}, indent=2) + '\n')
    process = None; code = 1
    try:
        dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
        def compile_case(variant):
            name, texts, expected = variant; case = run / name; production = case / 'production'; production.mkdir(parents=True)
            for name_, text in texts.items(): (production / name_).write_text(text)
            project = case / 'Water.csproj'; shutil.copyfile(fixture / 'Water.csproj', project)
            assembly = 'WaterTerrain_' + name.replace('-', '_')
            command = [dotnet, 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet', '-p:CaseName=' + assembly,
                '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production), '-p:UnityManaged=' + str(args.unity.parent / 'Data/Managed'), '-p:HarmonyPath=' + str(deps[0])]
            result = subprocess.run(command, capture_output=True, text=True, timeout=180)
            (case / 'build.log').write_text(result.stdout + result.stderr)
            if result.returncode: raise RuntimeError(result.stdout + result.stderr + '\nCompilation failure is not a negative control')
            return {'name': name, 'dll': str(case / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected}
        with ThreadPoolExecutor(max_workers=3) as pool: cases = list(pool.map(compile_case, variants))
        manifest = {'result': str(run / 'results.txt'), 'cases': cases}; (run / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
        unity = run / 'unity'; (unity / 'Assets/Editor').mkdir(parents=True); (unity / 'Packages').mkdir(); (unity / 'ProjectSettings').mkdir()
        shutil.copyfile(fixture / 'Editor/WaterRunner.cs', unity / 'Assets/Editor/WaterRunner.cs')
        native = (fixture / 'NativeWater.shader').read_text()
        for shader in ('VFX/Water_Shd', 'VFX/Water_Shd_Low', 'VFX/Water_Shd_Trans', 'VFX/Water_Shd_Trans_Low'):
            text = native.replace('VFX/Water_Shd_Trans', shader, 1)
            if shader == 'VFX/Water_Shd_Trans':
                text = text.replace('_Normal_Strength("Strength",Vector)=(1,1,1,1)', '_DetailOpacityBaseNormalStr("Strength",Vector)=(1,1,1,1)')
            else:
                text = text.replace('_Normal_Strength("Strength",Vector)=(1,1,1,1)', '')
            (unity / 'Assets' / (shader.replace('/', '_') + '.shader')).write_text(text)
        for shader in shaders + includes: shutil.copyfile(shader, unity / 'Assets' / shader.name)
        for dependency in deps: shutil.copyfile(dependency, unity / 'Assets' / dependency.name)
        (unity / 'Packages/manifest.json').write_text('{"dependencies":{}}\n'); (unity / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        command = [str(args.unity), '-batchmode', '-force-glcore', '-projectPath', str(unity), '-executeMethod', 'WaterRunner.Start', '-waterManifest', str(run / 'manifest.json'), '-logFile', str(run / 'unity.log')]
        if not os.environ.get('DISPLAY'): command = ['xvfb-run', '-a'] + command
        process = subprocess.Popen(command, start_new_session=True, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT)
        unity_code = process.wait(timeout=240)
        (run / 'unity-exit-code.txt').write_text(str(unity_code) + '\n')
        report = run / 'results.txt'
        if report.is_file(): print(report.read_text(), end='')
        changed = [str(p) for p in inputs if hashlib.sha256(p.read_bytes()).hexdigest() != hashes[str(p)]]
        (run / 'source-stability.json').write_text(json.dumps({'unchanged': not changed, 'changed': changed}, indent=2) + '\n')
        if changed or unity_code or not report.is_file(): raise RuntimeError('water runtime failed; inspect ' + str(run))
        if 'Shader error in ' in (run / 'unity.log').read_text(): raise RuntimeError('shader import error is not a passing control')
        code = 0; print('PASS: ' + str(len(variants)) + ' water runtime variants; evidence: ' + str(run))
    finally:
        if process is not None:
            try: os.killpg(process.pid, signal.SIGTERM)
            except ProcessLookupError: pass
            try: process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                os.killpg(process.pid, signal.SIGKILL); process.wait()
        cleanup = cleanup_completed_run('water-terrain-runtime', code, wrapper, root, process_reaped=True)
        (run / 'completion-cleanup.json').write_text(json.dumps(cleanup, indent=2) + '\n')


if __name__ == '__main__': main()
