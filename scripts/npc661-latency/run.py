#!/usr/bin/env python3
"""Measure native first-picture delivery without preparing its bank before the clock.

The inherited native exporter and actual capture/queue/receive/render bindings are
reused unchanged. This extension defers each real partition freeze/registration
until the complete owner artwork is present, then starts its clock before the
first owner Camera.Render and all native bank, material-basis and capture work.
It preserves the original one-second assertion and all native output assertions.
"""
import importlib.util
import hashlib
import json
import re
import sys
from types import SimpleNamespace
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
def option(flag):
    present = flag in sys.argv
    if present: sys.argv.remove(flag)
    return present

REQUEST_ONLY = option('--request-only')
EXPIRED_BUDGET = option('--expire-request-budget')
OLD_BUDGET = option('--old-budget-source')
NUMERIC_ORIGINAL = option('--numeric-original')
OLD_NUMERIC = option('--old-numeric-source')
OLD_REPAIR_PRIORITY = option('--old-repair-priority')
OLD_REPAIR_FAIRNESS = option('--old-repair-fairness')
OLD_RECOVERY = option('--old-recovery-source')
OLD_REQUEST_BANK = option('--old-request-bank')
OLD_REPAIR_COMPLETION = option('--old-repair-completion')
OLD_OPENING_RESET = option('--old-opening-reset')
ACTUAL_STOCK = option('--actual-stock')
PREPARED_CONTROL = '--prepared-control' in sys.argv
if PREPARED_CONTROL:
    sys.argv.remove('--prepared-control')


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def replace_once(text, before, after):
    if text.count(before) != 1:
        raise RuntimeError('NPC661 source binding anchor drift: ' + before[:90])
    return text.replace(before, after, 1)


def extend(case_source, cold=False, mismatch=False):
    text = ORIGINAL_PATCH(case_source, cold=False, mismatch=mismatch and not NUMERIC_ORIGINAL)
    if REQUEST_ONLY:
        opening = text.index('    private static IEnumerator FirstPicture639(bool mage)')
        end = text.index('\n    }', opening) + 6
        return text[:opening] + '    private static IEnumerator FirstPicture639(bool mage) => OriginalRequests661();' + text[end:]
    text = CASE_PATCH.shared_original_bank(text)
    text = CASE_PATCH.numeric_original(text) if NUMERIC_ORIGINAL else CASE_PATCH.material_refusal(text) if mismatch else text
    if not PREPARED_CONTROL:
        text = CASE_PATCH.timed_cold_bank(text)
    else:
        text = replace_once(text, '        var watch=System.Diagnostics.Stopwatch.StartNew();float began=Time.unscaledTime,nextCapture=began;',
                            '        var watch=System.Diagnostics.Stopwatch.StartNew();float began=Time.unscaledTime,nextCapture=began;\n'
                            '        double ownerFirstDraw661=-1,bankFrozen661=-1;')
    if ACTUAL_STOCK:
        text = STOCK_BINDING.case(text)
    text = replace_once(text, '        if(PrewarmObserver639)TownServiceMirror.Assets.Scan();',
        '        if(PrewarmObserver639)TownServiceMirror.Assets.Scan();\n'
        '        double assetScan661=assetPreparation.Elapsed.TotalSeconds;')
    text = replace_once(text, '        assetPreparation.Stop();',
        '        assetPreparation.Stop();\n'
        '        File.WriteAllText(Path.Combine(_output,"source-preparation661.txt"),'
        '"assetScanCpu="+assetScan661+" basisCpu="+(assetPreparation.Elapsed.TotalSeconds-assetScan661)+"\\n");')
    text = CASE_PATCH.request_transport(text)
    report = '        Check(ready>=0&&ready<=1.000,"all exact visible originals render within1s wall clock");'
    origin = 'secondary prepared-bank control; cold preparation excluded explicitly' if PREPARED_CONTROL else 'complete owner original source, before first Camera.Render and any native-bank freeze'
    text = replace_once(text, report, '''        File.WriteAllText(Path.Combine(_output,"cold-lifecycle661.txt"),
            "clockOrigin=''' + origin + '''\\n"
            +"ownerFirstDraw="+ownerFirstDraw661+" nativeBankFrozen="+bankFrozen661+" fullObserverRender="+ready+"\\n"
            +"nativeControllers=not executed; serialized native card model/container population remains the declared inherited constructor port\\n"
            +"fullHUDInitialize=not executed; actual selected-original Freeze/Partition/RegisterTemplate/NativeBasis/capture/transport/receive/render are timed\\n");
''' + report)
    return text




CASE_PATCH = load('case_patch661', Path(__file__).with_name('case-patch.py'))
STOCK_BINDING = load('stock_binding661', Path(__file__).with_name('stock-binding.py'))
RUNNER = load('npc658_native_latency', ROOT / 'scripts/npc658-latency-runtime/run.py')
ORIGINAL_PATCH = RUNNER.patch658
RUNNER.patch658 = extend
ORIGINAL_MODULE = RUNNER.module
WITHOUT_REQUEST = '--without-rejection-request' in sys.argv
if WITHOUT_REQUEST:
    sys.argv.remove('--without-rejection-request')


def bound_module(name, path):
    result = ORIGINAL_MODULE(name, path)
    if path.name == 'check-town-service-mirror.py':
        original_sources = result.sources

        def sources(root):
            bound, hashes = original_sources(root)
            base = root / 'src/GloomhavenVR/Net/TownServices'
            for name in ('TownServiceOriginalRequestCodec.cs', 'TownServiceMirror.OriginalRequests.cs'):
                content = (base / name).read_text()
                if name.endswith('Codec.cs'):
                    protocol = (root / 'src/GloomhavenVR/Net/NetProtocol.cs').read_text()
                    for constant, value in re.findall(r'public const byte (\w+) = (\d+);', protocol):
                        content = re.sub(r'\bNetProtocol\.' + constant + r'\b', value, content)
                bound[name] = content
            if OLD_NUMERIC:
                key='TownServiceMirror.NativeTemplateState.cs'
                bound[key] = replace_once(bound[key], ' || NativeModelGeometry(complete, property.Key)', '')
            if OLD_REPAIR_PRIORITY:
                key='TownServiceSendQueue.cs'
                bound[key] = replace_once(bound[key], 'if (urgent && TakeRequestedOriginal(now) is byte[] requested) return requested;',
                    '// NPC661 causal control: leave exact requests behind speculative repair bundles.')
            if OLD_REPAIR_FAIRNESS:
                key='TownServiceMirror.NativePublication.cs'
                bound[key]=replace_once(bound[key], 'int start = candidates == 0 ? 0 : _requestedRepairCursor % candidates;', 'int start = 0;')
                bound[key]=replace_once(bound[key], '            if (now < repair.After) { RequestedOriginalRepairPending = true; continue; }', '')
            if OLD_RECOVERY:
                old=Path(__file__).with_name('OldRequestRetention661.cs').read_text()
                for key,signature in (
                    ('TownServiceMirror.NativeTemplateState.cs','private static bool RetainUnpreparedNativeTemplate(int peer, TownServiceFrame frame)'),
                    ('TownServiceMirror.OriginalRequests.cs','private static void RecordOriginalRequest(int peer, TownServiceFrame frame)')):
                    bound[key]=replace_once(bound[key],result.method(bound[key],signature),result.method(old,signature))
            if OLD_REQUEST_BANK:
                key='TownServiceMirror.OriginalRequests.cs'
                bound[key]=replace_once(bound[key],'            if (frame.Sequence <= owner.Sequence) return;',
                    '            // Causal control: discard future source-bank sequence dominance.')
            if OLD_REPAIR_COMPLETION:
                key='TownServiceLaneSendQueue.OriginalRepairs.cs'
                bound[key]=replace_once(bound[key],'            bool fullPending = queue.TryPeekPending(out _, out object? next)\n                && ReferenceEquals(next, original);\n','')
                bound[key]=replace_once(bound[key],' && !fullPending','')
            if WITHOUT_REQUEST:
                key = 'TownServiceMirror.NativeTemplateState.cs'
                bound[key] = replace_once(bound[key], '        RecordOriginalRequest(peer, frame);',
                                          '        // NPC661 causal control: retain old passive full-repair behavior.')
            if REQUEST_ONLY:
                binding = load('request_binding661', Path(__file__).with_name('request-binding.py'))
                hashes.update(binding.bind(root, bound, result, EXPIRED_BUDGET, OLD_BUDGET))
            else:
                bound['NativeBasisInvalidation661.cs'] = '''namespace GloomhavenVR.Net.TownServices;internal static partial class TownServiceMirror {
internal static void InvalidateOnlyBasis661(string key) { if(NativeTemplateBases.TryGetValue(key,out var basis)) { basis.Binding.Dispose();NativeTemplateBases.Remove(key); } }
}'''
                if ACTUAL_STOCK:
                    hashes.update(STOCK_BINDING.bind(root, bound, result))
            return bound, hashes

        result.sources = sources
    if path.name == 'check-town-native-state623.py' and OLD_OPENING_RESET:
        original_delivery = result.bind_delivery_transport
        def delivery_sources(root, bound, loader):
            value = original_delivery(root, bound, loader)
            key = 'ActualScheduler629.cs'
            bound[key] = replace_once(bound[key], '_openingTownTurns = result != null ? 1 : 0;',
                'if (result != null) _openingTownTurns = 1;')
            return value
        result.bind_delivery_transport = delivery_sources
    return result


RUNNER.module = bound_module
ORIGINAL_COPY = RUNNER.shutil.copyfile
def fixture_copy(source, target, *args, **kwargs):
    value = ORIGINAL_COPY(source, target, *args, **kwargs)
    if REQUEST_ONLY and Path(source).name == 'NativeRepair658.cs':
        ORIGINAL_COPY(Path(__file__).with_name('Requests661.cs'), Path(target).with_name('Requests661.cs'))
    if Path(source).name == 'NativeRepair658.cs':
        if ACTUAL_STOCK:
            STOCK_BINDING.adapt_fixture(Path(target).parent, None)
        (Path(target).parent.parent / 'npc661-boundary.json').write_text(json.dumps({
            'clock': 'secondary prepared control' if PREPARED_CONTROL else 'before owner first draw and preparation',
            'request_only': REQUEST_ONLY, 'expired_budget_clock_port': EXPIRED_BUDGET,
            'old_budget_control': OLD_BUDGET, 'passive_repair_control': WITHOUT_REQUEST,
            'actual_stock': ACTUAL_STOCK,
            'old_repair_fairness': OLD_REPAIR_FAIRNESS, 'old_recovery_source': OLD_RECOVERY,
            'old_request_bank': OLD_REQUEST_BANK,
            'old_repair_completion': OLD_REPAIR_COMPLETION,
            'old_opening_reset': OLD_OPENING_RESET,
            'omitted_gameplay': 'HUD.Initialize, MakeAbilityAction and model/container population are declared inherited constructor ports',
        }, indent=2) + '\n')
    return value
RUNNER.shutil.copyfile = fixture_copy
ORIGINAL_DUMPS = json.dumps
def evidence_json(value, *args, **kwargs):
    if isinstance(value, dict) and 'runner' in value:
        value = dict(value)
        for path in Path(__file__).parent.iterdir():
            if path.suffix in ('.py', '.cs'):
                value['npc661/' + path.name] = hashlib.sha256(path.read_bytes()).hexdigest()
    if isinstance(value, dict) and 'cases' in value:
        if REQUEST_ONLY and OLD_BUDGET:
            value['cases'][0]['expected'] = 'expired pre-encode CPU budget still performs at least one requested repair'
        elif OLD_NUMERIC:
            value['cases'][0]['expected'] = 'every actual native model partition child carries owner-authored numeric geometry'
        elif OLD_REPAIR_FAIRNESS:
            value['cases'][0]['expected'] = 'persistently failing first two originals cannot starve the next valid requested source during retry cooldown'
        elif OLD_RECOVERY:
            value['cases'][0]['expected'] = 'late rejected old-session compact cannot erase the current complete request batch'
        elif OLD_REQUEST_BANK:
            value['cases'][0]['expected'] = 'future-census source identity survives a late older module with the same peer/lane/module key'
        elif OLD_REPAIR_COMPLETION:
            value['cases'][0]['expected'] = 'old compact completion leaves the requested matching full source marked at its pending head'
        elif OLD_OPENING_RESET:
            value['cases'][0]['expected'] = 'prior queued dependent revision survives the requested full repair'
    return ORIGINAL_DUMPS(value, *args, **kwargs)
RUNNER.json = SimpleNamespace(dumps=evidence_json, loads=json.loads)
if __name__ == '__main__':
    if '--output-dir' not in sys.argv:
        sys.argv += ['--output-dir', str(ROOT / '.planning/debug/npc661-latency/cold-lifecycle')]
    if not any(flag in sys.argv for flag in ('--latency658-native', '--latency658-startup', '--latency658-mismatch')):
        sys.argv += ['--latency658-native']
    RUNNER.main()
