#!/usr/bin/env python3
"""Focused licensed hero/enemy Android LOD producer, resume and provenance proof.

No complete game import/player build. Uses the actual production orchestrator,
native mesh reader and Android AssetBundle compiler in a private project.
"""
import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import struct
import sys
import tempfile
import time

from town_run_retention import cleanup_completed_run

ROOT = Path(__file__).resolve().parents[1]


def worker(run, broken_container=None):
    sys.path.insert(0, str(ROOT / 'tools/quest-builder'))
    import builder
    import figure_banks
    import UnityPy
    from types import SimpleNamespace
    game = (ROOT / 'ressources/GH_Data').resolve()
    rows = [builder.record_file(game / figure_banks.SOURCE_ROOT / name, figure_banks.SOURCE_ROOT + name)
            for name in ('hero_berserker_assets_all.bundle', 'npc_spittingdrake_assets_all.bundle')]
    args = SimpleNamespace(dotnet=str(Path.home() / '.dotnet/dotnet'))
    editor = Path('/home/claw/unity-2021.3.5/Editor/Unity')
    ordinary_command = builder.command
    def actual_identity_command(argv, *a, **kw):
        argv = list(argv)
        if 'build' in argv and any('Generator.csproj' in value for value in argv):
            argv.append('-p:IdentityProbe=' + str(ROOT / 'tests/QuestFigureGenerator.Tests/IdentityProbe.cs'))
        if 'QuestFigureMeshGenerator.Build' in argv:
            argv[argv.index('QuestFigureMeshGenerator.Build')] = 'QuestFigureIdentityProbe.Build'
            if broken_container: argv += ['-figureBrokenControl', str(broken_container)]
        return ordinary_command(argv, *a, **kw)
    builder.command = actual_identity_command
    started = time.monotonic()
    cache, manifest = figure_banks.produce(builder, args, ROOT, game, run, {'files': rows}, editor)
    cold = time.monotonic() - started
    assert {row['path'] for row in manifest['files']} >= {figure_banks.INDEX}
    assert manifest['target'] == 'Android'
    counts = {}
    root_types = {}
    for row in manifest['files']:
        if row['path'] == figure_banks.INDEX: continue
        environment = UnityPy.load(str(cache / 'banks' / row['path']))
        root_types[row['path']] = [obj.type.name for obj in environment.objects]
        assert root_types[row['path']].count('AssetBundle') == 1, 'actual native AssetBundle registration required'
        for asset in environment.objects:
            if asset.type.name != 'Mesh': continue
            mesh = asset.read()
            assert int(asset.assets_file.target_platform) == 13, 'actual Android target required'
            tier = int(mesh.m_Name.rsplit('-', 1)[1]); counts[tier] = counts.get(tier, 0) + 1
            assert mesh.m_VertexData.m_VertexCount > 0 and mesh.m_BindPose
    assert set(counts) == {75, 45, 20, 5}, counts
    def native_key(mesh):
        hashed = 14695981039346656037
        def byte(value):
            nonlocal hashed
            hashed = ((hashed ^ value) * 1099511628211) & 0xffffffffffffffff
        for codepoint in mesh.m_Name: byte(ord(codepoint))
        values = [int(mesh.m_IsReadable), mesh.m_VertexData.m_VertexCount, len(mesh.m_SubMeshes), *(sub.indexCount for sub in mesh.m_SubMeshes)]
        center, extent = mesh.m_LocalAABB.m_Center, mesh.m_LocalAABB.m_Extent
        values += [struct.unpack('<I', struct.pack('<f', value))[0] for value in (center.x, center.y, center.z, extent.x, extent.y, extent.z)]
        values += [len(mesh.m_BindPose)]
        values += [struct.unpack('<I', struct.pack('<f', getattr(pose, 'e' + str(r) + str(c))))[0]
                   for pose in mesh.m_BindPose for r in range(4) for c in range(4)]
        for value in values:
            for item in struct.pack('<I', value): byte(item)
        return 'figure-' + format(hashed, '016x')
    native = UnityPy.load(str(cache / 'identity-native/native-body.bundle'))
    actual_keys = {(obj.read().m_Name, bool(obj.read().m_IsReadable), native_key(obj.read())) for obj in native.objects if obj.type.name == 'Mesh'}
    expected_keys = json.loads((cache / 'identity-native/native-keys.json').read_text())['meshes']
    assert actual_keys == {(row['name'], row['readable'], row['key']) for row in expected_keys}, (actual_keys, expected_keys)
    assert {row['readable'] for row in expected_keys} == {True, False}
    # A corrupt mutable Unity input is repaired from exactly its durable source
    # asset. Completed derivatives/bank parts remain reusable.
    for log in (run / 'logs').glob('figure-*'): shutil.copyfile(log, log.with_name('cold-' + log.name))
    original_asset = next((cache / 'project/Assets/QuestFigures').glob('*.asset'))
    expected_asset = builder.digest(original_asset); original_asset.write_bytes(b'corrupt generated Unity input')
    derivative_times = {path.name: path.stat().st_mtime_ns for path in (cache / 'derivatives').glob('*.json')}
    (cache / 'completion.json').unlink()
    repaired, repaired_manifest = figure_banks.produce(builder, args, ROOT, game, run, {'files': rows}, editor)
    assert repaired == cache and repaired_manifest == manifest
    assert builder.digest(original_asset) == expected_asset
    assert derivative_times == {path.name: path.stat().st_mtime_ns for path in (cache / 'derivatives').glob('*.json')}
    # A completed ordinary retry never invokes either compiler or exporter.
    def forbidden(*a, **kw): raise AssertionError('cached producer repeated expensive work')
    original_command, original_export = builder.command, figure_banks._export
    builder.command, figure_banks._export = forbidden, forbidden
    started = time.monotonic()
    retained, again = figure_banks.produce(builder, args, ROOT, game, run, {'files': rows}, editor)
    warm = time.monotonic() - started
    assert cache == retained and manifest == again
    builder.command, figure_banks._export = original_command, original_export
    # Unrelated host/runtime diagnostics do not appear in the recipe identity;
    # a changed original bundle does. No compiler is needed for either control.
    altered = [dict(row) for row in rows]; altered[0]['sha256'] = 'f' * 64
    assert figure_banks.plan(ROOT, {'files': altered})['producerKey'] != manifest['producerKey']
    staging = run / 'delivery'
    figure_banks.stage(staging, cache, manifest)
    assert json.loads((staging / 'Assets/StreamingAssets/Quest/figure-meshes.json').read_text()) == manifest
    for row in manifest['files']:
        assert builder.digest(staging / 'Assets/StreamingAssets/QuestFigureMeshes' / row['path']) == row['sha256']
    result = {'scope': 'actual owned two-bundle extraction, Frame simplifiers, Unity Android AssetBundles; no headset FPS claim',
              'nativeBundleContainers': root_types, 'nativeUnityLoadVerified': True,
              'legacyNativeContainerRejected': bool(broken_container),
              'coldSeconds': cold, 'warmSeconds': warm, 'tiers': counts, 'manifest': manifest,
              'gameInputs': rows, 'producerKey': manifest['producerKey'], 'nativeAndroidKeys': expected_keys,
              'targetedProjectAssetRepair': True, 'wholeGameOrWindowsVerified': False}
    (run / 'verification.json').write_text(json.dumps(result, indent=2) + '\n')
    print('Evidence: ' + str(run), flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__); parser.add_argument('--worker', type=Path)
    parser.add_argument('--broken-container', type=Path, help='Known former Mesh-only Android file for a native loader negative control')
    args = parser.parse_args()
    if args.worker: worker(args.worker, args.broken_container); return
    ctypes.CDLL(None).prctl(36, 1, 0, 0, 0)
    base = ROOT / '.planning/debug/quest-figure-banks'; base.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=base)); log = run / 'runner.log'
    print('Quest figure bank evidence: ' + str(run), flush=True)
    source = run / 'sources'; source.mkdir()
    files = ['tools/quest-builder/figure_banks.py', 'tools/figure-mesh/QuestFigureMeshGenerator.cs',
             'tools/figure-mesh/export-native.py', 'tools/figure-mesh/NativeFigureMeshStream.cs',
             'tools/figure-mesh/ScenarioFigureMeshTopology.cs', 'src/GloomhavenVR/Core/Perf/ScenarioFigureMeshBank.cs']
    hashes = {}
    for name in files:
        target = source / name; target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(ROOT / name, target)
        hashes[name] = hashlib.sha256(target.read_bytes()).hexdigest()
    (run / 'source-hashes.json').write_text(json.dumps(hashes, indent=2) + '\n')
    process = None; code = 1
    try:
        with log.open('w') as output:
            output.write('Quest figure bank evidence: ' + str(run) + '\n'); output.flush()
            control = ['--broken-container', str(args.broken_container.resolve())] if args.broken_container else []
            process = subprocess.Popen(['/home/claw/unitypy-venv/bin/python', __file__, '--worker', str(run), *control],
                stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
            code = process.wait(timeout=1800)
    finally:
        helper = __import__('quest-card-alpha-content-tests')
        process_receipt = helper.reap_owned(process.pid if process else None)
        (run / 'process-cleanup.json').write_text(json.dumps(process_receipt, indent=2) + '\n')
        receipt = cleanup_completed_run('quest-figure-banks', code, log, ROOT, process_reaped=True)
        (run / 'cache-cleanup.json').write_text(json.dumps(receipt, indent=2) + '\n')
    print(log.read_text()[-5000:], end='', flush=True)
    raise SystemExit(code)


if __name__ == '__main__': main()
