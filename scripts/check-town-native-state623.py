#!/usr/bin/env python3
"""Prove sparse native metadata, exact original reconstruction and atomic mage exposure in Unity."""
import argparse
import hashlib
import importlib.util
import json
import os
import re
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def bind_delivery_transport(root, bound, loader):
    # Bind the actual complete scheduler, not the older per-town-only queue seam.
    # Non-town encoders are outside this UI proof; their existing stream queues
    # receive bounded noise directly, with actual production capacity constants.
    raw = (root / 'src/GloomhavenVR/Net/ExtrasSendQueue.cs').read_text()
    declaration = re.search(r'internal sealed (?:partial )?class ExtrasSendScheduler', raw)
    if declaration is None: raise RuntimeError('Production scheduler declaration binding drift')
    scheduler = raw[declaration.start():]
    enqueue = loader.method(scheduler, 'internal void Enqueue(byte[] snapshot, int length, int nativeSlot = -1, object? identity = null)')
    town_end = enqueue.index('        else if (type == _animationType)')
    scheduler = scheduler.replace(enqueue, enqueue[:town_end] + '\n    }', 1)
    capacities = {
        'CardPlumeCodec': ('CardPlumeCodec.cs', 'MaxSize'),
        'NativeBoardCodec': ('NativeBoardCodec.cs', 'MaxSize'),
        'CardAppearanceCodec': ('CardAppearanceCodec.cs', 'MaxSize'),
        'NativeDecisionPromptCodec': ('NativeDecisionPromptState.cs', 'MaxSize'),
        'ItemAppearanceCodec': ('ItemAppearanceCodec.cs', 'MaxSize'),
    }
    for cls, (filename, field) in capacities.items():
        source = (root / 'src/GloomhavenVR/Net' / filename).read_text()
        value = re.search(r'\b' + field + r' = (\d+);', source)
        if not value: raise RuntimeError('Production queue capacity drift: ' + cls)
        scheduler = scheduler.replace(cls + '.' + field, value[1])
        bound['Capacity-' + cls + '.cs'] = '// Actual production capacity source\n/*' + source + '*/'
    native_source = (root / 'src/GloomhavenVR/Net/NativeUseBarCodec.cs').read_text()
    native_size = re.search(r'\bMaxSize = (\d+);', native_source)[1]
    packet_source = (root / 'src/GloomhavenVR/Net/NativeUseBarPacket.cs').read_text()
    if 'MaxSize = 6 + NativeUseBarCodec.MaxSize;' not in packet_source: raise RuntimeError('Native capacity binding drift')
    scheduler = scheduler.replace('NativeUseBarPacket.MaxSize', '(6 + ' + native_size + ')')
    tooltip_source = (root / 'src/GloomhavenVR/Net/MapButtonTooltipCodec.cs').read_text()
    tooltip_caps = {name: re.search(r'\b' + name + r' = (\d+);', tooltip_source)[1] for name in ['MaxPayload', 'PageBytes']}
    tooltip_size = re.search(r'\bMaxSize = (.*);', tooltip_source)[1]
    for name, value in tooltip_caps.items(): tooltip_size = re.sub(r'\b' + name + r'\b', value, tooltip_size)
    scheduler = scheduler.replace('MapButtonTooltipCodec.MaxSize', '(' + tooltip_size + ')')
    batch = (root / 'src/GloomhavenVR/Net/PresentationBatch.cs').read_text()
    protocol = (root / 'src/GloomhavenVR/Net/NetProtocol.cs').read_text()
    for constant, value in re.findall(r'public const byte (\w+) = (\d+);', protocol):
        scheduler = re.sub(r'\bNetProtocol\.' + constant + r'\b', value, scheduler)
        batch = re.sub(r'\bNetProtocol\.' + constant + r'\b', value, batch)
    bound['ActualScheduler629.cs'] = 'using System; namespace GloomhavenVR.Net;\n' + scheduler
    receipt_scheduler = root / 'src/GloomhavenVR/Net/ExtrasSendScheduler.OriginalReceipts.cs'
    if receipt_scheduler.exists():
        bound[receipt_scheduler.name] = receipt_scheduler.read_text()
    serializer = (root / 'src/GloomhavenVR/Net/Avatar/AvatarSerializer.cs').read_text()
    bound['BatchSerializer629.cs'] = 'namespace GloomhavenVR.Net; internal static class AvatarSerializer {\n' + loader.method(serializer, 'internal static void WriteU32(byte[] b, ref int i, uint v)') + '\n}'
    bound['PresentationBatch.cs'] = batch
    bound['SchedulerOriginal629.cs'] = '// Exact original scheduler source before compile seams\n/*' + raw + '*/'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/town-native-state623')
    parser.add_argument('--no-negative-controls', action='store_true')
    parser.add_argument('--delivery-only', action='store_true', help='Measure real first-visible native transport and publication failure recovery')
    parser.add_argument('--bank-split-only', action='store_true')
    parser.add_argument('--lifecycle-only', action='store_true', help='Run only cold-template disconnect and existing-bank reset preparation proofs')
    parser.add_argument('--preparation658-only', action='store_true', help='Run first-map and immersive-off bounded original preparation')
    parser.add_argument('--negative-control', action='append', choices=['inert-artwork', 'owner-text', 'complete-picture', 'split-headers', 'owner-state', 'overridden-basis', 'omitted-numeric-basis', 'cold-original', 'retained-canvas', 'eager-bank', 'departed-cold-peer', 'reset-preparation', 'repeated-native-baseline', 'fragile-mage-keyframe', 'queued-native-repair', 'ordinary-native-repair', 'published-baseline', 'all-prepared-picture', 'hidden-publication', 'warm-priority', 'structural-bound', 'retained-visible-census'])
    args = parser.parse_args()
    if args.preparation658_only: args.lifecycle_only = True
    root = args.source_root.resolve()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir.resolve()))
    spec = importlib.util.spec_from_file_location('mirror_source_binding', root / 'scripts/check-town-service-mirror.py')
    loader = importlib.util.module_from_spec(spec); spec.loader.exec_module(loader)
    bound, hashes = loader.sources(root)
    bind_delivery_transport(root, bound, loader)
    fixture = run / 'fixture'; shutil.copytree(root / 'scripts/town-service-mirror-runtime', fixture)
    shutil.copyfile(Path(__file__).resolve().parent / 'town-native-state623-runtime/NativeState623.cs', fixture / 'NativeState623.cs')
    shutil.copyfile(Path(__file__).resolve().parent / 'town-native-state623-runtime/NativeDelivery629.cs', fixture / 'NativeDelivery629.cs')
    shutil.copyfile(root / 'scripts/npc658-latency-runtime/NativeRepair658.cs', fixture / 'NativeRepair658.cs')
    if args.lifecycle_only:
        if args.bank_split_only: parser.error('--lifecycle-only and --bank-split-only are mutually exclusive')
        preparation = root / 'src/GloomhavenVR/WorldUI/TownServices/NativeTemplates.EnhancementPreparation.cs'
        bound[preparation.name] = preparation.read_text()
        shutil.copyfile(Path(__file__).resolve().parent / 'town-native-state623-runtime/NativeTemplateDelivery625.cs', fixture / 'NativeTemplateDelivery625.cs')
        publisher = fixture / 'Publisher.cs'
        text = publisher.read_text()
        for before, after in [('internal sealed class Part {', 'internal sealed partial class Part {'),
                              ('internal static class TownServiceNativeAssets {', 'internal static partial class TownServiceNativeAssets {')]:
            if text.count(before) != 1: raise RuntimeError('Lifecycle adapter binding drift: ' + before)
            text = text.replace(before, after, 1)
        publisher.write_text(text)
        if args.preparation658_only:
            shutil.copyfile(root / 'scripts/npc658-latency-runtime/NativePreparation658.cs', fixture / 'NativePreparation658.cs')
            raw_sync = (root / 'src/GloomhavenVR/WorldUI/TownServices/TownServiceSync.cs').read_text()
            bound['PrepareCore658.cs'] = 'using System; using UnityEngine; using GloomhavenVR.Net; using GloomhavenVR.WorldUI.MapRoom; namespace GloomhavenVR.WorldUI; internal sealed partial class TownServiceSync {\n' + loader.method(raw_sync, 'private void PrepareCore()') + '\n}'
            raw_templates = (root / 'src/GloomhavenVR/WorldUI/TownServices/NativeTemplates.cs').read_text()
            initialize = loader.method(raw_templates, 'internal static bool Initialize()')
            tail = initialize[initialize.rindex('_ready = true;'):]
            if tail.count('PrepareEnhancementOriginals();') != 1 or tail.count('return true;') != 1: raise RuntimeError('First Initialize return binding drift')
            bound['NativeInitialReturn658.cs'] = 'namespace GloomhavenVR.WorldUI; internal static partial class NativeTemplates { internal static bool FixtureInitialReturn658() {\n' + tail + '\n}'
            text = publisher.read_text().replace('private void PrepareCore() { }', '', 1)
            publisher.write_text(text)
            for filename, before in [('Boundaries.cs', 'internal static class TownServicePopulation'), ('CabinetAudioBoundaries.cs', 'internal static class WorldUIConfig')]:
                target = fixture / filename; text = target.read_text()
                if text.count(before) != 1: raise RuntimeError('Preparation availability port drift: ' + filename)
                target.write_text(text.replace(before, before.replace('static class', 'static partial class'), 1))
    program = fixture / 'Program.cs'
    anchor = '            if (variant == "production") PublisherNoCloth();'
    branch = '''            if (suite == "native-state623") {
                var state = NativeState623(); while (state.MoveNext()) yield return state.Current;
                var repair = NativeRepair658(); while (repair.MoveNext()) yield return repair.Current;
                File.WriteAllText(Path.Combine(_output,"assertions.txt"),_assertions+" assertions\\n"); yield break;
            }
            if (suite == "native-bank-split623") {
                var bank = NativeBankSplit623(); while (bank.MoveNext()) yield return bank.Current;
                File.WriteAllText(Path.Combine(_output,"assertions.txt"),_assertions+" assertions\\n"); yield break;
            }
'''
    if args.delivery_only:
        branch += '''            if (suite == "native-delivery629") {
                var delivery = NativeDelivery629(); while (delivery.MoveNext()) yield return delivery.Current;
                File.WriteAllText(Path.Combine(_output,"assertions.txt"),_assertions+" assertions\\n"); yield break;
            }
'''
    text = program.read_text()
    if text.count(anchor) != 1: raise RuntimeError('Fixture entry binding drift')
    program.write_text(text.replace(anchor, anchor + '\n' + branch, 1))
    if args.lifecycle_only:
        anchor = '            DelayedCensusRace();'
        text = program.read_text()
        if text.count(anchor) != 1: raise RuntimeError('Lifecycle-only entry binding drift')
        branch = '''            if (suite == "native-template-lifecycle625") {
                var state = NativeTemplateLifecycle625(); while (state.MoveNext()) yield return state.Current;
                File.WriteAllText(Path.Combine(_output,"assertions.txt"),_assertions+" assertions\\n"); yield break;
            }
'''
        program.write_text(text.replace(anchor, branch + anchor, 1))
        if args.preparation658_only:
            text = program.read_text()
            program.write_text(text.replace('var state = NativeTemplateLifecycle625();', 'var state = NativePreparation658();', 1))
    variants = [('production', None, None, None, '')]
    if args.lifecycle_only and not args.no_negative_controls:
        variants += ([
            ('late-visitor-preparation658', 'PrepareCore658.cs', 'if (!MapRoomDriver.Active || Time.unscaledTime < _prepareAfter) return;', 'if (!MapRoomDriver.Active || Time.unscaledTime < _prepareAfter) return;\n        if (!WorldUIConfig.ImmersiveTownServices.Value && !TownServicePopulation.HasRemoteVisitors) return;', 'immersive-off first map prepares originals before any remote visitor'),
            ('initial-return-preparation658', 'NativeInitialReturn658.cs', 'PrepareEnhancementOriginals();', '// omit first Initialize preparation', 'first Initialize return starts bounded native original preparation immediately'),
        ] if args.preparation658_only else [
            ('departed-cold-peer', 'TownServiceMirror.cs', 'ForgetUnpreparedNativePeer(peer);', '// omit departed deferred admission lifetime', 'departed cold metadata cannot recreate pending or baseline state'),
            ('reset-preparation', 'NativeTemplates.EnhancementPreparation.cs', '|| _enhancementBasisRevision != TownServiceMirror.NativeTemplatePreparationRevision)', '|| false)', 'network reset rewarms existing frozen originals without changing asset identities'),
        ])
    if not args.no_negative_controls and not args.lifecycle_only and not args.delivery_only:
        variants += [
            ('eager-bank', 'TownServiceMirror.CatalogBank.cs',
             'if (frame.CatalogBank.Prepared)', 'if (false && frame.CatalogBank.Prepared)',
             'prepared page clock excludes synchronous full-bank compression'),
            ('retained-canvas', 'TownCatalogBank.cs',
             'original.CanvasSortingLayer != current.CanvasSortingLayer || original.CanvasSortingOrder != current.CanvasSortingOrder',
             'false', 'cached original key cannot authorize a changed canonical canvas header'),
            ('split-headers', 'TownServiceMirror.CatalogBank.cs',
             'TownServiceFrame reference = TownCatalogClock.Create(frame, NoCatalogPatchBases);',
             'TownServiceFrame reference = TownServiceDelta.Retain(frame); reference.CatalogBank = new TownCatalogBank { Prepared = frame.CatalogBank.Prepared, Members = frame.CatalogBank.Members };',
             'split reference retains every current original header for atomic dependency admission'),
        ]
    if not args.no_negative_controls and not args.bank_split_only and not args.lifecycle_only and not args.delivery_only:
        variants += [
            ('queued-native-repair', 'TownServiceSendQueue.cs', 'if (retainedNativeOriginal) queue.PrependTownOriginal(bytes, frame, preserveInFlight: true);', 'if (false && retainedNativeOriginal) queue.PrependTownOriginal(bytes, frame, preserveInFlight: true);', 'actual retained full repair survives continuous same-baseline coalescing'),
            ('ordinary-native-repair', 'TownServiceSendQueue.cs', '(frame.HighPriority || frame.Service == 3)', 'frame.HighPriority', 'ordinary original retained full repair survives continuous same-baseline coalescing'),
            ('fragile-mage-keyframe', 'TownServiceMirror.NativePublication.cs',
             'if (now < repair.After) return;',
             'if (true) return; // omit retained full-original repair\n#pragma warning disable CS0162',
             'mismatched originals receive their retained full source without periodic repair debt'),
            ('repeated-native-baseline', 'TownServiceMirror.cs',
             'bool completeOriginal = openingOriginal || frame.CatalogBank != null || module.Baseline == null || now >= module.NextBaseline || !TownServiceDelta.Compatible(module.Baseline, frame);',
             'bool completeOriginal = UsesNativeTemplateState(frame) || openingOriginal || frame.CatalogBank != null || module.Baseline == null || now >= module.NextBaseline || !TownServiceDelta.Compatible(module.Baseline, frame);',
             'after the exact native original actual hover capture uses the existing cumulative owner delta'),
            ('inert-artwork', 'TownServiceMirror.NativeTemplateState.cs', 'binding.Read(Assets, includeInactiveGraphics: true)', 'binding.Read(Assets)', 'actual original prefab produces compact native metadata without a prior network baseline'),
            ('owner-text', 'TownServiceMirror.NativeTemplateState.cs', 'NativeTextProperty(key)\n        || index == 0 && !TownServiceFastNumbers.IsMaterial(key);', 'index == 0 && !TownServiceFastNumbers.IsMaterial(key);', 'localized observer defaults never replace exact owner text or font'),
            ('owner-state', 'TownServiceMirror.NativeTemplateState.cs', '|| index == 0 && !TownServiceFastNumbers.IsMaterial(key);', '|| index == 0 && key == TownServiceProperty.Sibling;', 'every owner root pose and state remains explicit in native metadata'),
            ('omitted-numeric-basis', 'TownServiceMirror.NativeTemplateState.cs', 'writer.Write((ushort)value.Numbers.Length);\n                    foreach (float number in value.Numbers) writer.Write(number == 0f ? 0f : number);', '// omit native numeric basis from exact hash', 'changed omitted native numeric state rejects before any observer paint'),
            ('overridden-basis', 'TownServiceMirror.NativeTemplateState.cs', 'if (OwnerProperty(index, key) || basis.Coverage.Length != 0\n                        && (basis.Coverage[index] & (1u << key)) != 0) continue;', 'if (OwnerProperty(index, key)) continue;', 'a fully transmitted owner material replaces a different observer native default immediately'),
            ('cold-original', 'TownServiceMirror.NativeTemplateState.cs', 'bool ready = attempt && TryExpandNativeTemplateState(received, out _);', 'bool ready = false;', 'a retained first sparse offer replays immediately after its original becomes available'),
            ('complete-picture', 'TownServiceMirror.NativeTemplateState.cs', 'if (peer <= 0 || session.Service != 3 || session.Modules.Length == 0) return true;', 'return true;\n#pragma warning disable CS0162', 'a missing offered card prevents a partial ring or options picture'),
        ]
    if not args.no_negative_controls and not args.lifecycle_only and not args.bank_split_only:
        variants += [
            ('published-baseline', 'TownServiceMirror.cs',
             'byte[] packet = WriteCatalogPacket(emitted, send);',
             'if (completeOriginal) { module.Baseline = emitted; module.NextBaseline = float.PositiveInfinity; }\n                    byte[] packet = WriteCatalogPacket(emitted, send);',
             'rejected publication cannot invent an undelivered original baseline'),
            ('all-prepared-picture', 'TownServiceMirror.NativeTemplateState.cs',
             'foreach (ushort id in session.RequiredVisibleModules ?? session.Modules)',
             'foreach (ushort id in session.Modules)',
             'complete visible native picture arrives without a hidden-module repair wait'),
            ('hidden-publication', 'TownServiceMirror.cs',
             '&& module.Last == null && !CurrentModuleVisible(module)',
             '&& false && module.Last == null && !CurrentModuleVisible(module)',
             'hidden never-presented originals do not consume first-visible capture or wire work'),
            ('warm-priority', 'TownServiceSendQueue.cs',
             '_priority.Count > 0 && (_urgentBundle.HasInFlight || _urgentBundle.HasPending);\n        foreach (ushort id in _priority)',
             '_coldPriority.Count > 0 && (_urgentBundle.HasInFlight || _urgentBundle.HasPending);\n        foreach (ushort id in _coldPriority.Keys)',
             'a warm current original control can borrow an existing bounded urgent town turn'),
            ('structural-bound', 'LazyNativeTemplates.cs',
             'i >= 0 && remaining > 32;', 'i >= 0 && remaining > 128;',
             'An original town widget partition exceeds the bounded presentation packet.'),
            # The atomic original-value bundle retains its manifest before delivery.
            # Losing the census now correctly fails the real first-picture proof
            # before the later direct retained-array lifetime assertion is reached.
            ('retained-visible-census', 'TownServiceDelta.cs',
             'RequiredVisibleModules = f.RequiredVisibleModules == null ? null : (ushort[])f.RequiredVisibleModules.Clone()',
             'RequiredVisibleModules = null',
             'complete visible native picture arrives without a hidden-module repair wait'),
        ]
    if args.negative_control:
        variants = [case for case in variants if case[0] == 'production' or case[0] in args.negative_control]
    suite = 'native-delivery629' if args.delivery_only else 'native-template-lifecycle625' if args.lifecycle_only else 'native-bank-split623' if args.bank_split_only else 'native-state623'
    manifest = {'suite': suite, 'result': str(run / 'results.txt'), 'evidence': str(run), 'cases': []}
    unity = Path(os.environ.get('UNITY_PATH', '/home/claw/unity-2021.3.5/Editor/Unity'))
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    for name, filename, before, after, expected in variants:
        build = run / name; production = build / 'production'; production.mkdir(parents=True)
        for path, text in bound.items():
            if path == filename:
                if text.count(before) != 1: raise RuntimeError('Mutation binding drift: ' + name)
                text = text.replace(before, after, 1)
            (production / path).write_text(text)
        project = build / 'Mirror.csproj'; shutil.copyfile(fixture / 'Mirror.csproj', project)
        assembly = 'TownNativeState623_' + name.replace('-', '_')
        result = subprocess.run([dotnet, 'build', str(project), '-c', 'Release', '--nologo', '--verbosity', 'quiet',
            '-p:CaseName=' + assembly, '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production),
            '-p:UnityManaged=' + str(unity.parent / 'Data/Managed'),
            '-p:UnityUi=' + str(root / 'ressources/GH_Data/Managed/UnityEngine.UI.dll'),
            '-p:UnityTmp=' + str(root / 'ressources/GH_Data/Managed/Unity.TextMeshPro.dll')], capture_output=True, text=True)
        (build / 'build.log').write_text(result.stdout + result.stderr)
        if result.returncode: raise SystemExit(result.stdout + result.stderr)
        manifest['cases'].append({'name': name, 'dll': str(build / 'bin/Release/netstandard2.1' / (assembly + '.dll')), 'expected': expected})
        print('Compiled ' + name, flush=True)
    (run / 'source-hashes.json').write_text(json.dumps({name: hashlib.sha256(text.encode()).hexdigest() for name, text in bound.items()}, indent=2) + '\n')
    project = run / 'unity'; (project / 'Assets/Editor').mkdir(parents=True)
    if not args.lifecycle_only and not args.delivery_only:
        shutil.copytree(root / 'unity/GloomhavenVR.Assets/Assets/Bundle/TownServices', project / 'Assets/Resources/TownServices')
    shutil.copyfile(fixture / 'Editor/MirrorRunner.cs', project / 'Assets/Editor/MirrorRunner.cs')
    (project / 'Packages').mkdir(); (project / 'ProjectSettings').mkdir()
    (project / 'Packages/manifest.json').write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6","com.unity.modules.physics":"1.0.0"}}\n')
    (project / 'ProjectSettings/ProjectVersion.txt').write_text('m_EditorVersion: 2021.3.5f1\n')
    path = run / 'manifest.json'; path.write_text(json.dumps(manifest, indent=2))
    print('Evidence: ' + str(run), flush=True)
    result = subprocess.run(['xvfb-run', '-a', str(unity), '-batchmode', '-force-glcore', '-projectPath', str(project),
        '-executeMethod', 'MirrorRunner.Start', '-mirrorManifest', str(path), '-logFile', str(run / 'unity.log')], timeout=300)
    report = Path(manifest['result'])
    if report.exists(): print(report.read_text(), end='')
    if result.returncode or not report.exists(): raise SystemExit('FAIL native metadata proof; see ' + str(run / 'unity.log'))
    print('PASS native metadata source/runtime; evidence: ' + str(run))


if __name__ == '__main__': main()
