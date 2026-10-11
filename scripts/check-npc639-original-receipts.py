#!/usr/bin/env python3
"""Compile the real bounded receipt codec/state with explicit owner-storage seams.

This is a focused receipt proof, not the complete wire gate or evidence of headset
timing. Four executable causal controls must compile and reach their intended
semantic failure. Engine capture, native prefab generation and Photon delivery
remain outside this metadata proof and need the integration wire/Unity checks.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]


def declaration(text, signature):
    """Keep an actual declaration, including its complete cleanup body."""
    match = re.search(r'^([ \t]*)' + re.escape(signature), text, re.MULTILINE)
    if match is None:
        raise RuntimeError('Production lifecycle binding drift: ' + signature)
    line_end = text.index('\n', match.end())
    if ';' in text[match.end():line_end]:
        return text[match.start():line_end]
    end = re.search(r'^' + re.escape(match.group(1)) + r'}\s*$', text[match.end():], re.MULTILINE)
    if end is None:
        raise RuntimeError('Production lifecycle end drift: ' + signature)
    return text[match.start():match.end() + end.end()]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-root', type=Path, default=ROOT)
    parser.add_argument('--protocol-root', type=Path)
    parser.add_argument('--output-dir', type=Path, default=ROOT / '.planning/debug/npc639/receipts')
    parser.add_argument('--case', action='append', choices=('production', 'any-peer-inference',
        'unstored-original-inference', 'old-object-inference', 'unknown-owner-publication'),
        help='Rerun only an affected case; unchanged evidence may be retained.')
    args = parser.parse_args()
    protocol_root = args.protocol_root or args.source_root
    paths = {
        'TownServiceOriginalReceiptCodec.cs': args.source_root / 'src/GloomhavenVR/Net/TownServices/TownServiceOriginalReceiptCodec.cs',
        'TownServiceMirror.OriginalReceipts.cs': args.source_root / 'src/GloomhavenVR/Net/TownServices/TownServiceMirror.OriginalReceipts.cs',
        'TownServiceFrame.cs': args.source_root / 'src/GloomhavenVR/Net/TownServices/TownServiceFrame.cs',
        'TownServiceCodec.ReturnOrigin.cs': args.source_root / 'src/GloomhavenVR/Net/TownServices/TownServiceCodec.ReturnOrigin.cs',
        'TownServiceCodec.OfferedHover.cs': args.source_root / 'src/GloomhavenVR/Net/TownServices/TownServiceCodec.OfferedHover.cs',
        'NetPacket.cs': args.source_root / 'src/GloomhavenVR/Net/NetPacket.cs',
        'NetProtocol.cs': protocol_root / 'src/GloomhavenVR/Net/NetProtocol.cs',
        'TownServiceMirror.OriginalRequests.cs': args.source_root / 'src/GloomhavenVR/Net/TownServices/TownServiceMirror.OriginalRequests.cs',
        'TownServiceMirror.NativePublication.cs': args.source_root / 'src/GloomhavenVR/Net/TownServices/TownServiceMirror.NativePublication.cs',
        'TownServiceLaneSendQueue.OriginalRepairs.cs': args.source_root / 'src/GloomhavenVR/Net/TownServices/TownServiceLaneSendQueue.OriginalRepairs.cs',
    }
    sources = {name: path.read_text() for name, path in paths.items()}
    if 'MsgTownOriginalReceipt = 28' not in sources['NetProtocol.cs'] or 'ExtIdTownOriginalReceipt = 112' not in sources['NetProtocol.cs']:
        raise SystemExit('Missing reserved production receipt constants; integrate the protocol or supply --protocol-root.')
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix='run-', dir=args.output_dir))
    (run / 'source-hashes.json').write_text(json.dumps({str(paths[name]): hashlib.sha256(text.encode()).hexdigest()
        for name, text in sources.items()}, indent=2) + '\n')
    # Frame retention carries this immutable production type. Bind its exact
    # declaration without importing the unrelated original-artwork codec into
    # a receipt-state proof; no replacement origin implementation is supplied.
    origin_source = sources['TownServiceCodec.ReturnOrigin.cs']
    origin_declaration = declaration(origin_source, 'internal sealed class TownCardReturnOrigin')
    sources['TownServiceCodec.ReturnOrigin.cs'] = (
        'using System; namespace GloomhavenVR.Net.TownServices;\n' + origin_declaration + '\n')
    hover_source = sources['TownServiceCodec.OfferedHover.cs']
    hover_declaration = declaration(hover_source, 'internal sealed class TownOfferedHover')
    sources['TownServiceCodec.OfferedHover.cs'] = (
        'using System; namespace GloomhavenVR.Net.TownServices;\n' + hover_declaration + '\n')
    (run / 'bound-declarations.json').write_text(json.dumps({
        'TownCardReturnOrigin': {
            'source': str(paths['TownServiceCodec.ReturnOrigin.cs']),
            'source_sha256': hashlib.sha256(origin_source.encode()).hexdigest(),
            'signature': 'internal sealed class TownCardReturnOrigin',
            'declaration_sha256': hashlib.sha256(origin_declaration.encode()).hexdigest(),
            'boundary': 'Exact immutable Frame metadata declaration; return-origin wire encoding and validation are outside this receipt-only proof.',
        },
        'TownOfferedHover': {
            'source': str(paths['TownServiceCodec.OfferedHover.cs']),
            'source_sha256': hashlib.sha256(hover_source.encode()).hexdigest(),
            'signature': 'internal sealed class TownOfferedHover',
            'declaration_sha256': hashlib.sha256(hover_declaration.encode()).hexdigest(),
            'boundary': 'Exact immutable Frame metadata declaration; hover wire/playback are outside this receipt-only proof.',
        },
    }, indent=2) + '\n')
    # Receipt disconnect/reset now also retires missing-original requests. Bind
    # the real request storage and cleanup, rather than replacing those calls
    # with empty fixture methods. Admission, encoding and scheduling of requests
    # remain outside this receipt-only proof and have their separate full checks.
    requests = sources.pop('TownServiceMirror.OriginalRequests.cs')
    publication = sources.pop('TownServiceMirror.NativePublication.cs')
    queue = sources.pop('TownServiceLaneSendQueue.OriginalRepairs.cs')
    storage_start = requests.index('    private sealed class OriginalRequest\n')
    storage_end = requests.index('    private static void RecordOriginalRequest(', storage_start)
    cleanup = '\n'.join(declaration(requests, signature) for signature in (
        'private static void RemoveOriginalRequestsPeer(int peer)',
        'private static void ResetOriginalRequests()',
    ))
    repair_storage = '\n'.join(declaration(publication, signature) for signature in (
        'private static readonly System.Collections.Generic.List<LocalModule> RequestedRepairCandidates',
        'private static int _requestedRepairCursor',
    ))
    sources['OriginalRequestCleanup.cs'] = (
        'using System; using System.Collections.Generic; namespace GloomhavenVR.Net.TownServices;\n'
        + 'internal static partial class TownServiceMirror {\n'
        + requests[storage_start:storage_end] + cleanup + '\n' + repair_storage + '\n}\n'
        + declaration(queue, 'internal static class TownRequestedOriginalRepair') + '\n'
    )
    # NetProtocol also contains unrelated engine board-tuning helpers. Compile its
    # required actual declarations verbatim, without mocking their numeric values or
    # importing those unrelated runtime helpers into this receipt-only proof.
    constants = []
    for name in ('Magic', 'Version', 'MsgRig', 'MsgExtras', 'MsgTownOriginalReceipt', 'ExtIdTownOriginalReceipt'):
        found = re.findall(r'public const (?:uint|byte) ' + name + r'\s*=\s*[^;]+;', sources['NetProtocol.cs'])
        if len(found) != 1:
            raise SystemExit('Production protocol constant binding drift: ' + name)
        constants.append(found[0])
    sources['NetProtocol.cs'] = 'namespace GloomhavenVR.Net; internal static class NetProtocol {' + '\n'.join(constants) + '}\n'
    variants = (
        ('production', '', '', ''),
        ('any-peer-inference', 'if (!retained.Peers.Contains(peer)) return false;',
         'if (!retained.Peers.Contains(peer)) continue;', 'every compatible peer must explicitly acknowledge'),
        ('unstored-original-inference', '|| !ReceivedBaselines.TryGetValue(peer, out var retained)',
         '|| false && !ReceivedBaselines.TryGetValue(peer, out var retained)', 'unstored original cannot create receipt'),
        ('old-object-inference', '!ReferenceEquals(retained.Baseline, baseline)',
         '(retained.Baseline.Sequence != baseline.Sequence || retained.Baseline.Service != baseline.Service || retained.Baseline.Session != baseline.Session)',
         'exact retained baseline identity required'),
        ('unknown-owner-publication', 'if (!OriginalReceiptPeers.Contains(owner)) continue;',
         '', 'unknown owner keeps pending receipt until compatibility handshake'),
    )
    fixture = ROOT / 'scripts/npc639-original-receipts'
    dotnet = shutil.which('dotnet') or str(Path.home() / '.dotnet/dotnet')
    results = []
    for name, before, after, expected in variants:
        if args.case and name not in args.case:
            continue
        case = run / name
        production = case / 'production'
        production.mkdir(parents=True)
        for filename, text in sources.items():
            if filename == 'TownServiceMirror.OriginalReceipts.cs' and before:
                if before not in text:
                    raise SystemExit('Causal control binding drift: ' + name)
                if name == 'unstored-original-inference':
                    # Remove only receipt admission's retained-full gate. Capture
                    # still performs its independent retained-baseline check.
                    start = text.index(before)
                    end = text.index(') return;', start) + 1
                    text = text[:start] + ')' + text[end:]
                else:
                    text = text.replace(before, after)
            (production / filename).write_text(text)
        shutil.copyfile(fixture / 'Receipts.csproj', case / 'Receipts.csproj')
        built = subprocess.run([dotnet, 'build', str(case / 'Receipts.csproj'), '-c', 'Release',
            '--nologo', '-v', 'quiet', '-p:FixtureDir=' + str(fixture), '-p:ProductionDir=' + str(production),
            '-p:SourceRoot=' + str(args.source_root)],
            capture_output=True, text=True, timeout=90)
        (case / 'build.log').write_text(built.stdout + built.stderr)
        if built.returncode:
            raise SystemExit('Compilation failure is not a control pass:\n' + built.stdout + built.stderr)
        executed = subprocess.run([dotnet, str(case / 'bin/Release/net8.0/Receipts.dll')],
            capture_output=True, text=True, timeout=30)
        output = executed.stdout + executed.stderr
        (case / 'result.log').write_text(output)
        print(name + ': ' + output.strip())
        passed = (executed.returncode == 0) if name == 'production' else (executed.returncode != 0 and expected in output)
        results.append({'case': name, 'success': passed, 'exit': executed.returncode, 'expected': expected})
        if not passed:
            (run / 'results.json').write_text(json.dumps(results, indent=2) + '\n')
            raise SystemExit('FAIL case ' + name + '; see ' + str(run))
    (run / 'results.json').write_text(json.dumps(results, indent=2) + '\n')
    scope = 'selected focused receipt cases' if args.case else 'focused receipt proof with four causal controls'
    print('PASS ' + scope + '; ' + str(run))


if __name__ == '__main__':
    main()
