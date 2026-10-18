#!/usr/bin/env python3
"""Qualify the exact owned native flame mask in Quest Linear color space."""
import argparse
import ctypes
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'scripts'))
from town_run_retention import cleanup_completed_run


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--native-project', type=Path, required=True)
    parser.add_argument('--case', action='append')
    args = parser.parse_args()
    files = {name: (ROOT / 'src/GloomhavenVR/Cards/Art' / name).read_text() for name in ('QuestCardFireMask.cs', 'AdoptedCardFxSpace.cs', 'QuestHostedItemFireMask.cs')}
    import re
    def block(text, signature):
        start = text.index(signature); brace = text.index('{', start); end = brace + 1; depth = 1
        while depth:
            depth += (text[end] == '{') - (text[end] == '}'); end += 1
        return text[start:end]
    net = ROOT / 'src/GloomhavenVR/Net'
    remote = (net / 'Remote/RemoteCardArt.cs').read_text()
    native = (net / 'Remote/RemoteCardArt.Native.cs').read_text()
    item_native = (net / 'Remote/RemoteCardArt.ItemNative.cs').read_text()
    item = (net / 'ItemAppearanceBindings.cs').read_text()
    writes = (net / 'Remote/NativePlaybackWrites.cs').read_text()
    bounds = (ROOT / 'src/GloomhavenVR/Cards/Art/CardFxBounds.cs').read_text()
    assert 'Destroy(_questFlameMasks.Release)' in item_native
    native_block = block(native, 'if (node.Role == 11 && (supported & NativePlaybackProperties.ParticleMask) != 0)')
    item_block = block(item, 'if (node.Role == 11)')
    assert native_block.index('NativePlaybackWrites.Texture(') < native_block.index('_questFlameMasks.Bind(')
    assert item_block.index('NativePlaybackWrites.Texture(') < item_block.index('flameQueue?.Invoke(')
    fields = '\n'.join(re.findall(r'^    private static readonly int \w+ = Shader[.]PropertyToID\([^;]+;', remote, re.M))
    files['ActualRemoteFlames.cs'] = ('using System; using UnityEngine; using Object=UnityEngine.Object; using UnityEngine.UI; using GloomhavenVR.Cards; using GloomhavenVR.Core;\nnamespace GloomhavenVR.Net;\ninternal sealed partial class RemoteCardArt {\n'
        + fields + '\n' + block(remote, 'private void WriteFlameConstants(') + '\n'
        + block(remote, 'private void ApplyQuestFlameMask(') + '\n' + block(remote, 'private static void SetFloatIfPresent(') + '\n'
        + block(remote, 'private void ReleaseOwnedMaterials(') + '\n' + block(item_native, 'private void ApplyItemFlameQueue(') + '\n'
        + 'internal void NativeFlame(Image graphic, Material material, bool ghost) { uint supported = uint.MaxValue; var node = new FixtureNode { Role=11, Flags=ghost?8:0 };\n' + native_block + '\n}\n'
        + 'internal void ItemFlame(Image graphic, Material material, bool ghost) { uint support = uint.MaxValue; var node = new FixtureNode { Role=11, Flags=ghost?8:0 }; ItemCardEffects Effects = _itemNativeSource!; Action<Image,Material> flameQueue=ApplyItemFlameQueue;\n' + item_block + '\n}\n}\n'
        + 'internal static class NativePlaybackWrites {\n' + block(writes, 'internal static void Texture(') + '\n}\n'
        + 'internal sealed class ActualItemMaterials { internal System.Collections.Generic.Dictionary<Graphic,Material> _materials = new();\n' + block(item, 'internal void Destroy(') + '\n}\n')
    files['ActualHostedBounds.cs'] = ('using UnityEngine; using GloomhavenVR.Core; using UnityEngine.UI;\nnamespace GloomhavenVR.Cards; internal static class CardFxBounds {\n'
        + 'private static readonly int PosAndBoundsId=Shader.PropertyToID("_PosAndBounds"), GreyOutId=Shader.PropertyToID("_GreyOut");\n'
        + block(bounds, 'internal static void Reseat(') + '\n' + bounds[bounds.index('private static bool Approximately('):bounds.index(';', bounds.index('private static bool Approximately(')) + 1]
        + '\nprivate static void ReportRefusalOnce(Vector2 size) {} private static void ReportOnce(ItemCardEffects fx,Vector4 was,Vector4 want,int written,int seen) {}\n}\n')
    remote_hashes = {str(path.relative_to(ROOT)): hashlib.sha256(path.read_bytes()).hexdigest() for path in [net / 'Remote/RemoteCardArt.cs', net / 'Remote/RemoteCardArt.Native.cs', net / 'Remote/RemoteCardArt.ItemNative.cs', net / 'ItemAppearanceBindings.cs', ROOT / 'src/GloomhavenVR/Cards/Art/CardFxBounds.cs']}
    variants = [
        ('production', '', '', '', ''),
        ('numerical-view-still-srgb', 'QuestCardFireMask.cs', 'source.mipmapCount, true)', 'source.mipmapCount, false)', 'numerical alias retains native format'),
        ('mask-blocks-not-copied', 'QuestCardFireMask.cs', 'copy.LoadRawTextureData(source.GetRawTextureData());', 'copy.LoadRawTextureData(new byte[source.GetRawTextureData().Length]);', 'exact native flame mask recovers original Gamma-space coverage'),
        ('desktop-mask-replaced', 'QuestCardFireMask.cs', '!QuestStandalonePlatform.Enabled || QualitySettings.activeColorSpace', 'QualitySettings.activeColorSpace', 'desktop keeps its exact native mask binding'),
        ('private-mask-binding-lost', 'QuestCardFireMask.cs', 'material.SetTexture(Particle, mask);', ';', 'actual adopted binding changes only numerical sampling'),
        ('release-restoration-lost', 'QuestCardFireMask.cs', 'entry.Key.SetTexture(Particle, entry.Value);', '{}', 'release restores native mask even after the Quest capability stops'),
        ('gpu-block-copy-lost', 'QuestCardFireMask.cs', 'Graphics.CopyTexture(source, copy);', ';', 'GPU-only native textures use an exact ordinary block copy'),
        ('last-lease-leaks', 'QuestCardFireMask.cs', 'UnityEngine.Object.Destroy(entry.Copy);', 'entry.Copy.name += ".leaked";', 'last mask lease releases private GPU textures'),
        ('native-clone-mask-not-bound', 'ActualRemoteFlames.cs', '_questFlameMasks.Bind(material, _nativeBindings.BurnTexture);', '{}', 'current native clone owns its numerical burn mask'),
        ('item-clone-mask-not-bound', 'ActualRemoteFlames.cs', '_questFlameMasks.Bind(material, _itemNativeSource?.overlayFrameBurn);', '{}', 'native item clone owns its numerical burn mask after the native texture write'),
        ('hosted-item-mask-not-bound', 'QuestHostedItemFireMask.cs', 'if (_masks.Bind(_flame.material, _source.overlayFrameBurn))', 'if (bool.Parse("false"))', 'original hosted item uses its exact fgFx burn mask'),
    ]
    if args.case:
        variants = [v for v in variants if v[0] in args.case]
        if len(variants) != len(set(args.case)): parser.error('Unknown or repeated case')
    base = ROOT / '.planning/debug/quest-card-fire-mask'; base.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=base)); log = run / 'runner.log'
    log.write_text('Quest card fire evidence: ' + str(run) + '\n'); print(log.read_text(), end='', flush=True)
    fixture = ROOT / 'tests/QuestCardFireMask.Tests'
    unity = Path('/home/claw/unity-2021.3.5/Editor/Unity')
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    manifest = {'result': str(run / 'results.txt'), 'cases': []}
    code = 1; process = None
    if sys.platform.startswith('linux'):
        if ctypes.CDLL(None, use_errno=True).prctl(36, 1, 0, 0, 0) != 0: raise OSError('Could not own fixture children')
    try:
        for name, changed, before, after, expected in variants:
            case = run / name; production = case / 'production'; production.mkdir(parents=True)
            for filename, original in files.items():
                source = original
                if filename == changed:
                    assert source.count(before) == 1, 'Causal mutation boundary drift: ' + name
                    source = source.replace(before, after, 1)
                (production / filename).write_text(source)
            project = case / 'Runtime.csproj'; shutil.copyfile(fixture / 'Runtime.csproj', project)
            assembly = 'QuestCardFireMask_' + name.replace('-', '_')
            build = subprocess.run([dotnet, 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet', '--disable-build-servers', '-p:UseSharedCompilation=false',
                '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production),
                '-p:UnityManaged=' + str(unity.parent / 'Data/Managed'), '-p:UnityUi=' + str(ROOT / 'ressources/GH_Data/Managed/UnityEngine.UI.dll')], capture_output=True, text=True)
            (case / 'build.log').write_text(build.stdout + build.stderr)
            if build.returncode: raise RuntimeError('Actual fire mask fixture compilation failed: ' + str(case / 'build.log'))
            manifest['cases'].append({'name': name, 'dll': str(case / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected})
        project = run / 'unity'
        for folder in ('Assets/Plugins', 'Assets/Resources', 'Assets/Editor', 'Packages', 'ProjectSettings'): (project / folder).mkdir(parents=True, exist_ok=True)
        for case in manifest['cases']:
            target = project / 'Assets/Plugins' / Path(case['dll']).name; shutil.copyfile(case['dll'], target); case['dll'] = str(target)
        shutil.copyfile(ROOT / 'scripts/card-diagnostic-runtime/Editor/DiagnosticRunner.cs', project / 'Assets/Editor/DiagnosticRunner.cs')
        native = args.native_project / 'Assets'
        assets = {
            'QuestRecoveredBundles/abd3f824ded15674f88a93d52188506e/GUI_GUI_Shine_Sphere_Blend_Shd.shader': 'QuestRecoveredBundles/abd3f824ded15674f88a93d52188506e/GUI_GUI_Shine_Sphere_Blend_Shd.shader',
            'QuestRecoveredBundles/033bf5afedf9da34083f506222f8d5e0/GUI_Card_Border_Bloom_Mat.mat': 'Resources/NativeFire.mat',
            'QuestRecoveredBundles/97c300f636d975f4f94a8257a1b6b821/T_rect_frame_mask_card_noisy.png': 'Resources/NativeBurn.png',
            'QuestRecoveredBundles/968cb426bbbc349478ace3c85be3380d/T_flowmap_outwards_spotty.png': 'Resources/NativeFlow.png',
            'QuestRecoveredBundles/617732aafd8a8174b9f76ab133689ee4/T_disc_ring.png': 'Resources/NativeMain.png',
            'QuestRecoveredBundles/aebf70bfe7bc33c48898c10fdd4d16a9/T_rect_frame_mask_card_wide.png': 'Resources/NativeGhost.png',
        }
        program_names = ('95dbbdd240b8b21866c3f4b10c1632b1943a147269ae80d73d7e790d56aee811-1115fbdd19f131001107df1d074da9b428792a48eb7dc8568c23047516e6af78.hlsl',
                         'dafe09414713ed2cbe52134fdd7a9e742b144b510e77fcca0145802a762020aa-014fad405e75a05b7e7bbc0719181e01dc7b533774bbcaf256fa5998c3342969.hlsl')
        assets.update({'QuestOriginalCampaign/ShaderPrograms/' + name: 'QuestOriginalCampaign/ShaderPrograms/' + name for name in program_names})
        native_hashes = {}
        for source_name, target_name in assets.items():
            for suffix in ('', '.meta'):
                source = native / (source_name + suffix); target = project / 'Assets' / (target_name + suffix)
                target.parent.mkdir(parents=True, exist_ok=True); shutil.copyfile(source, target)
                native_hashes[source_name + suffix] = hashlib.sha256(source.read_bytes()).hexdigest()
                if source_name.endswith(program_names[1]) and not suffix:
                    text = target.read_text(); before = 'float4(_Time.x, _Time.y, _Time.z, _Time.w)'
                    assert text.count(before) == 1
                    target.write_text(text.replace(before, 'float4(1.0, 20.0, 40.0, 60.0)'))
        (run / 'native-inputs.json').write_text(json.dumps({'ownedProject': str(args.native_project), 'sha256': native_hashes,
            'fixtureOnlyTime': 20, 'fixtureReadabilityPair': True, 'colorSpace': 'Linear', 'headsetVerified': False}, indent=2) + '\n')
        # A second import of the exact same native PNG supplies the genuinely
        # GPU-only/updateCount0 source. Only fixture readability/GUID differs.
        import re
        shutil.copyfile(project / 'Assets/Resources/NativeBurn.png', project / 'Assets/Resources/NativeBurnGpu.png')
        gpu_meta = (project / 'Assets/Resources/NativeBurn.png.meta').read_text()
        gpu_meta = re.sub(r'(?m)^guid: [a-f0-9]+$', 'guid: 42af8934f391465b8849f89f8f3e7f35', gpu_meta)
        gpu_meta = re.sub(r'(?m)^(  isReadable: )1$', r'\g<1>0', gpu_meta)
        (project / 'Assets/Resources/NativeBurnGpu.png.meta').write_text(gpu_meta)
        readable_meta = (project / 'Assets/Resources/NativeBurn.png.meta').read_text()
        readable_meta = re.sub(r'(?m)^(  isReadable: )[01]$', r'\g<1>1', readable_meta)
        (project / 'Assets/Resources/NativeBurn.png.meta').write_text(readable_meta)
        (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}\n')
        (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
        (project / 'ProjectSettings/ProjectSettings.asset').write_text('%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\nPlayerSettings:\n  m_ActiveColorSpace: 1\n')
        manifest_path = run / 'manifest.json'; manifest_path.write_text(json.dumps(manifest, indent=2) + '\n')
        (run / 'source-hashes.json').write_text(json.dumps({'runtime': {name: hashlib.sha256(text.encode()).hexdigest() for name, text in files.items()},
            'remoteSourceHashes': remote_hashes, 'fixtureFiles': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in fixture.iterdir() if p.is_file()}, 'headsetVerified': False}, indent=2) + '\n')
        command = [str(unity), '-batchmode', '-force-vulkan', '-projectPath', str(project), '-executeMethod', 'DiagnosticRunner.Start', '-interactionManifest', str(manifest_path), '-logFile', str(run / 'unity.log')]
        if not os.environ.get('DISPLAY'): command = ['xvfb-run', '-a'] + command
        process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, start_new_session=True)
        code = process.wait(timeout=180)
        result = Path(manifest['result'])
        if result.is_file(): print(result.read_text()[-8192:], end='', flush=True)
        if code or not result.is_file(): raise RuntimeError('Actual fire snapshot fixture failed: ' + str(run / 'unity.log'))
        if any((ROOT / 'src/GloomhavenVR/Cards/Art' / name).read_text() != text for name, text in files.items() if not name.startswith('Actual')) or any(hashlib.sha256((ROOT / name).read_bytes()).hexdigest() != digest for name,digest in remote_hashes.items()): raise RuntimeError('Runtime source changed during qualification')
        print('PASS Quest native numerical fire mask: ' + str(len(variants)) + ' production/causal cases; actual native Linear GPU output', flush=True)
    finally:
        if process is not None and process.poll() is None: process.kill(); process.wait()
        spec = importlib.util.spec_from_file_location('fire_fixture_cleanup', ROOT / 'scripts/quest-artwork-tests.py')
        helper = importlib.util.module_from_spec(spec); spec.loader.exec_module(helper)
        children = helper.reap_fixture_group(process.pid if process else None)
        (run / 'process-cleanup.json').write_text(json.dumps(children, indent=2) + '\n')
        (run / 'cleanup.json').write_text(json.dumps(cleanup_completed_run('quest-card-fire-mask', code, log, ROOT, process_reaped=True), indent=2) + '\n')


if __name__ == '__main__': main()
