#!/usr/bin/env python3
"""Qualify exact original ping particle streams, native pixels and reversible pooling."""
import argparse
import ctypes
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--owned-project', type=Path, required=True)
    parser.add_argument('--shader-overlay', type=Path)
    parser.add_argument('--control', choices=('no-upload', 'no-restore', 'no-shader-gate', 'no-live-contract', 'baked-native-visibility'))
    args = parser.parse_args()
    base = ROOT / '.planning/debug/quest-ping-streams'; base.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=base)); project = run / 'unity'
    resources = project / 'Assets/Resources'; resources.mkdir(parents=True)
    source = args.owned_project.resolve(strict=True); inputs = {}
    def copy(path, target=None):
        target = target or project / path.relative_to(source)
        target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(path, target)
        inputs[str(path)] = hashlib.sha256(path.read_bytes()).hexdigest()
    recovered = source / 'Assets/QuestRecoveredBundles'
    if recovered.is_dir():
        for guid in ('68b6eb6a1e6d0694b8b8e02cd7c882eb', '8f570964c3a36d84ab3a5c616adf9c15', '402fffc2f53be6a4381dae7a5094c920', '78d5c8105b91ef14582e250040e703e5'):
            for path in (recovered / guid).iterdir(): copy(path)
        shader = source / 'Assets/QuestRecoveredBundles/402fffc2f53be6a4381dae7a5094c920/PriorityParticleAdd.shader'
        prefab = recovered / '48e6b1e92b4109f4cb3486950a7557b3/Ping Highlight.prefab'
        overlay = source
    else:
        # A retained original export has the same native fields under normal
        # asset paths. Pair it with its recovered Android shader bank explicitly.
        if args.shader_overlay is None: parser.error('--shader-overlay required for original export')
        overlay = args.shader_overlay.resolve(strict=True)
        for relative in ('Material/ParticleGlow.mat', 'Material/Ping Hex 3D Highlight.mat', 'Texture2D/AirCreating.png'):
            path = source / 'Assets' / relative; copy(path); copy(Path(str(path) + '.meta'))
        shader = overlay / 'Assets/QuestRecoveredBundles/402fffc2f53be6a4381dae7a5094c920/PriorityParticleAdd.shader'
        target = project / 'Assets/Shader/Particles_Priority Additive.shader'
        copy(shader, target)
        copy(source / 'Assets/Shader/Particles_Priority Additive.shader.meta', Path(str(target) + '.meta'))
        prefab = source / 'Assets/GameObject/Ping Highlight.prefab'
    for include in re.findall(r'#include "(Assets/QuestOriginalCampaign/ShaderPrograms/[^"]+)"', shader.read_text()):
        copy(overlay / include, project / include)
    text = prefab.read_text(); inputs[str(prefab)] = hashlib.sha256(prefab.read_bytes()).hexdigest()
    # Its Chronos Timeline callback belongs to game simulation, outside this
    # paused particle rendering witness. Preserve every native particle field.
    scripts = re.findall(r'^--- !u!114 &(\d+)\n.*?(?=^--- !u!|\Z)', text, re.M | re.S)
    for key in scripts:
        text = re.sub(r'^--- !u!114 &' + key + r'\n.*?(?=^--- !u!|\Z)', '', text, flags=re.M | re.S)
        text = text.replace('  - component: {fileID: ' + key + '}\n', '')
    (resources / 'NativePing.prefab').write_text(text)
    production = ROOT / 'src/GloomhavenVR/Board/Patches/QuestPingStreams.cs'; code = production.read_text(); source_hashes = {str(production): hashlib.sha256(production.read_bytes()).hexdigest()}
    mutations = {
        'no-upload': ('binding.Renderer.SetActiveVertexStreams(Packed);', 'binding.Renderer.SetActiveVertexStreams(binding.Original);'),
        'no-restore': ('if (Equal(_scratch, Packed)) binding.Renderer.SetActiveVertexStreams(binding.Original);', ';'),
        'no-shader-gate': ('|| (shader != "Particles/Priority Additive"', '|| (false && shader != "Particles/Priority Additive"'),
        'no-live-contract': (' || binding.Material.shader != binding.Shader', ' || false'),
    }
    if args.control in mutations:
        before, after = mutations[args.control]
        if code.count(before) != 1: raise RuntimeError('Ping causal control binding drift: ' + args.control)
        code = code.replace(before, after, 1)
    elif args.control == 'baked-native-visibility':
        # Exact B712 production source, not an invented renderer surrogate.
        # That revision passed paused/manual pixel probes while its visibility
        # lease freezes both original Pause-culling simulation clocks on device.
        code = subprocess.check_output(['git', 'show', '17cab93ae:src/GloomhavenVR/Board/Patches/QuestPingStreams.cs'], cwd=ROOT, text=True)
    source_hashes['emittedProduction'] = hashlib.sha256(code.encode()).hexdigest()
    (project / 'Assets/QuestPingStreams.cs').write_text('#nullable enable\n' + code.replace('namespace GloomhavenVR.Board.Patches;', 'namespace GloomhavenVR.Board.Patches {') + '\n}\n')
    (project / 'Assets/Boundaries.cs').write_text('''using System.Collections.Generic;
using UnityEngine;
internal class PingManager : MonoBehaviour {
    private class Ping { public GameObject highlight; public Ping(GameObject target) { highlight=target; } }
    private readonly List<Ping> assigned = new List<Ping>();
    internal void Assigned(GameObject target) { assigned.Add(new Ping(target)); }
}
namespace GloomhavenVR.Core { internal static class QuestStandalonePlatform { internal static bool Enabled=true; } internal static class VRSession { internal static bool IsRunning=true; } internal enum VRLogLevel { Debug } internal static class VRCameraPolicy { internal static Camera AllowedHead; } internal static class VRLog { internal static bool Wants(VRLogLevel level) => true; internal static void Warn(string category,string message) { Debug.LogWarning(message); } } }
''')
    for probe in (ROOT / 'tests/QuestPingStreams.Tests').glob('*.cs'):
        shutil.copyfile(probe, project / 'Assets' / probe.name); source_hashes[str(probe)] = hashlib.sha256(probe.read_bytes()).hexdigest()
    (project / 'Packages').mkdir(); (project / 'ProjectSettings').mkdir()
    (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.modules.particlesystem":"1.0.0","com.unity.modules.imageconversion":"1.0.0"}}\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    (project / 'ProjectSettings/ProjectSettings.asset').write_text('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\nPlayerSettings:\n  m_ObjectHideFlags: 0\n  serializedVersion: 23\n  m_ActiveColorSpace: 1\n')
    log = run / 'runner.log'; log.write_text('Quest native ping streams evidence: ' + str(run) + '\n'); print(log.read_text(), end='', flush=True)
    spec = importlib.util.spec_from_file_location('artwork_fixture', ROOT / 'scripts/quest-artwork-tests.py')
    fixture = importlib.util.module_from_spec(spec); spec.loader.exec_module(fixture)
    if sys.platform.startswith('linux') and ctypes.CDLL(None, use_errno=True).prctl(36, 1, 0, 0, 0) != 0: raise OSError('Could not own fixture children')
    command = ['/home/claw/unity-2021.3.5/Editor/Unity', '-batchmode', '-force-vulkan', '-projectPath', str(project), '-executeMethod', 'QuestPingStreamsProbe.Run', '-logFile', str(run / 'unity.log')]
    if not os.environ.get('DISPLAY'): command = ['xvfb-run', '-a'] + command
    environment = os.environ.copy()
    if args.control == 'baked-native-visibility': environment['QUEST_PING_AUTOPLAY_ONLY'] = '1'
    process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, start_new_session=True, env=environment); code = 1
    try:
        code = process.wait(timeout=180)
        if args.control:
            expected = {'no-upload': 'Original ping retains its exact consumed native streams', 'no-restore': 'Pooling restores exact original ping streams', 'no-shader-gate': 'Foreign particle materials retain their authored streams', 'no-live-contract': 'Changed native contracts retain authored streams', 'baked-native-visibility': 'AUTO_CLOCK_RESULT same=False'}[args.control]
            if code == 0 or expected not in (run / 'unity.log').read_text(): raise RuntimeError('Native ping control did not detect its targeted defect')
            (run / 'control.json').write_text(json.dumps({'control': args.control, 'expectedFailure': expected, 'observedExit': code, 'hardwareVerified': False}, indent=2) + '\n')
            print('PASS native ping causal control: ' + args.control, flush=True); code = 0; return
        if code: raise RuntimeError('Native ping fixture failed: ' + str(run / 'unity.log'))
        report = json.loads((project / 'ProbeOutput/results.json').read_text())
        if report['assertions'] < 30 or report['visiblePixels'] < 50: raise RuntimeError('Incomplete native ping evidence')
        clock = json.loads((project / 'ProbeOutput/auto-clock.json').read_text())
        if not clock['sameNativeClock'] or clock['samples'] < 5 or clock['nativeClock'] < 1: raise RuntimeError('Incomplete native autoplay evidence')
        (run / 'qualification.json').write_text(json.dumps({'sources': source_hashes, 'nativeInputs': inputs, 'results': report, 'autoPlay': clock, 'hardwareVerified': False}, indent=2) + '\n')
        print('PASS native ping streams: ' + str(report['assertions']) + ' assertions and ' + str(clock['samples']) + ' actual autoplay observations; headset tile pixels unverified', flush=True)
    finally:
        if process.poll() is None: process.kill(); process.wait()
        pixels = project / 'ProbeOutput'
        if pixels.is_dir(): shutil.copytree(pixels, run / 'native-pixels', dirs_exist_ok=True)
        reaped = fixture.reap_fixture_group(process.pid)
        (run / 'process-cleanup.json').write_text(json.dumps(reaped, indent=2) + '\n')
        for name, wanted in inputs.items(): assert hashlib.sha256(Path(name).read_bytes()).hexdigest() == wanted, 'Read-only native input changed'
        (run / 'cleanup.json').write_text(json.dumps(cleanup_completed_run('quest-ping-streams', code, log, ROOT, process_reaped=True), indent=2) + '\n')


if __name__ == '__main__': main()
