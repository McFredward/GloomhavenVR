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
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
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
    text = ORIGINAL_PATCH(case_source, cold=False, mismatch=mismatch)
    if not PREPARED_CONTROL:
        text = timed_cold_bank(text)
    else:
        text = replace_once(text, '        var watch=System.Diagnostics.Stopwatch.StartNew();float began=Time.unscaledTime,nextCapture=began;',
                            '        var watch=System.Diagnostics.Stopwatch.StartNew();float began=Time.unscaledTime,nextCapture=began;\n'
                            '        double ownerFirstDraw661=-1,bankFrozen661=-1;')
    text = request_transport(text)
    report = '        Check(ready>=0&&ready<=1.000,"all exact visible originals render within1s wall clock");'
    origin = 'secondary prepared-bank control; cold preparation excluded explicitly' if PREPARED_CONTROL else 'complete owner original source, before first Camera.Render and any native-bank freeze'
    text = replace_once(text, report, '''        File.WriteAllText(Path.Combine(_output,"cold-lifecycle661.txt"),
            "clockOrigin=''' + origin + '''\\n"
            +"ownerFirstDraw="+ownerFirstDraw661+" nativeBankFrozen="+bankFrozen661+" fullObserverRender="+ready+"\\n"
            +"nativeControllers=not executed; serialized native card model/container population remains the declared inherited constructor port\\n"
            +"fullHUDInitialize=not executed; actual selected-original Freeze/Partition/RegisterTemplate/NativeBasis/capture/transport/receive/render are timed\\n");
''' + report)
    return text


def timed_cold_bank(text):
    beginning = text.index('            Transform nativeObserver=Object.Instantiate(source.gameObject,observer,false).transform;')
    end_marker = '            Object.DestroyImmediate(nativeObserver.gameObject);'
    ending = text.index(end_marker, beginning) + len(end_marker)
    freeze = text[beginning:ending]
    text = text[:beginning] + text[ending:]
    loop = '''        // The exact owner originals are now active. No observer partition has
        // been frozen, registered or based before this visible-source clock.
        var watch=System.Diagnostics.Stopwatch.StartNew();
        float began=Time.unscaledTime,nextCapture=began;
        Canvas.ForceUpdateCanvases();Render639(owner);
        double ownerFirstDraw661=watch.Elapsed.TotalSeconds;
        foreach(var original661 in originals)
        {
            Transform source=original661.Source;
            string address=original661.Address;
            Func<Transform,bool>? exclude=original661.Exclude;
            ushort id=original661.Id;
''' + freeze + '''
        }
        double bankFrozen661=watch.Elapsed.TotalSeconds;
'''
    text = replace_once(text, '        TownServiceMirror.BeginSession(service,639,owner,owner);',
                        loop + '        TownServiceMirror.BeginSession(service,639,owner,owner);')
    text = replace_once(text,
                        '        var watch=System.Diagnostics.Stopwatch.StartNew();float began=Time.unscaledTime,nextCapture=began;\n        double clock=',
                        '        // Continue the same clock through every preparation and delivery stage.\n        double clock=')
    text = replace_once(text,
                        '        var preparation=System.Diagnostics.Stopwatch.StartNew();\n        Canvas.ForceUpdateCanvases();Render639(owner);',
                        '        var preparation=System.Diagnostics.Stopwatch.StartNew();\n        Canvas.ForceUpdateCanvases();')
    text = replace_once(text, '        preparation.Stop();\n        yield return null;',
                        '        preparation.Stop();')
    return text


def request_transport(text):
    # The observer and owner run in separate declared role scopes, like the
    # inherited receipt proof. Both directions use the real saturated scheduler.
    text = replace_once(text,
                        '            TownServiceMirror.CaptureOriginalReceipts((bytes,length)=>receipts658.Enqueue(bytes,length));',
                        '            TownServiceMirror.CaptureOriginalRequests((bytes,length)=>receipts658.Enqueue(bytes,length));\n'
                        '            TownServiceMirror.CaptureOriginalReceipts((bytes,length)=>receipts658.Enqueue(bytes,length));')
    text = replace_once(text,
                        '                NetPlayerActors.Peer=2;Check(TownServiceMirror.ReceiveOriginalReceipt(10,page658,page658.Length),"actual first-picture original receipt uses bounded reverse transport");',
                        '''                NetPlayerActors.Peer=2;SetNativeSenderActive629(true);
                try
                {
                    bool request661=TownServiceOriginalRequestCodec.TryRead(page658,page658.Length,out _);
                    Check(request661?TownServiceMirror.ReceiveOriginalRequest(10,page658,page658.Length)
                        :TownServiceMirror.ReceiveOriginalReceipt(10,page658,page658.Length),
                        "actual first-picture receipt/request uses unchanged bounded reverse transport");
                    TownServiceMirror.CaptureRequestedOriginalRepairs(publish);
                }
                finally { SetNativeSenderActive629(false); }
''')
    return text


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
            if WITHOUT_REQUEST:
                key = 'TownServiceMirror.NativeTemplateState.cs'
                bound[key] = replace_once(bound[key], '        RecordOriginalRequest(peer, frame);',
                                          '        // NPC661 causal control: retain old passive full-repair behavior.')
            return bound, hashes

        result.sources = sources
    return result


RUNNER.module = bound_module
if __name__ == '__main__':
    if '--output-dir' not in sys.argv:
        sys.argv += ['--output-dir', str(ROOT / '.planning/debug/npc661-latency/cold-lifecycle')]
    if not any(flag in sys.argv for flag in ('--latency658-native', '--latency658-startup', '--latency658-mismatch')):
        sys.argv += ['--latency658-native']
    RUNNER.main()
