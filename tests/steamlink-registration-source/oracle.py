"""Independent unchanged v1 header/CRC oracle for the new C# registration source."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import struct
import subprocess
import sys
import tempfile
import zlib

sys.dont_write_bytecode = True


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--fixture', type=Path, required=True)
    parser.add_argument('--dotnet', type=Path, required=True)
    parser.add_argument('--probe-helper', type=Path, required=True)
    args = parser.parse_args()
    spec = importlib.util.spec_from_file_location('original_app_probe', args.probe_helper)
    probe = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = probe
    spec.loader.exec_module(probe)
    checks = 0

    def require(value, message):
        nonlocal checks
        checks += 1
        if not value:
            raise AssertionError(message)

    session = '0123456789abcdef0123456789abcdef'
    cases = [(session, 1, 'left'), (session, 1, 'right'), (session, 2, 'left'),
             (session, 129, 'left'), ('fedcba9876543210fedcba9876543210', 1, 'left'),
             (session, 2**64 - 1, 'right')]
    tuples = {}
    with tempfile.TemporaryDirectory(prefix='registration-source-oracle-') as temporary:
        root = Path(temporary)
        for index, (nonce, pair, eye) in enumerate(cases):
            directory = root / str(index)
            command = [str(args.dotnet), str(args.fixture), '--emit', str(directory),
                       '--session', nonce, '--pair', str(pair), '--eye', eye]
            result = subprocess.run(command, capture_output=True, text=True, timeout=30)
            require(result.returncode == 0, 'C# source emission failed: ' + result.stdout + result.stderr)
            record = json.loads((directory / 'registration.json').read_text())
            manifest = record['manifest']
            color, alpha, canvas, header = ((directory / name).read_bytes() for name in
                                           ('color.rgb8', 'coverage.r8', 'canvas.rgb8', 'header.bin'))
            require((len(color), len(alpha), len(canvas), len(header)) == (184320, 61440, 1131072, 132), 'fixed source sizes')
            require(record['schema'] == 'frame-app-registration-source-v1' and manifest['schema'] == 'frame-app-payload-reference-v1', 'new wrapper is distinct')
            require((record['session'], record['pair_id'], record['eye'], record['phase']) == (nonce, pair, eye, (pair - 1) % 128), 'actual tuple retained')
            require(record['content_id_meaning'] == 'app_owned_pair_id_not_native_video_frame' and manifest['content_id'] == pair, 'pair meaning is explicit')
            require(manifest['placements'] == probe.layout(320, 192), 'old fixed atlas geometry is preserved')
            require(header == probe.header_for(320, 192, color, alpha, nonce, pair, eye, 'straight'), 'exact unchanged independent132-byte header')
            require(zlib.crc32(header[:128]) == struct.unpack('>I', header[128:])[0], 'independent CRC32')
            image = probe.Image(688, 548, canvas)
            for copy in range(3):
                require(probe.sample_header(image, copy) == header, 'every complete old-decoder header agrees')
            require(probe.extract(image, manifest['placements']['color']) == color, 'exact RGB coordinates')
            expanded = probe.extract(image, manifest['placements']['coverage'])
            require(expanded[::3] == expanded[1::3] == expanded[2::3] == alpha, 'exact R8 grayscale placement')
            for field, payload in [('color_sha256', color), ('coverage_sha256', alpha), ('canvas_rgb_sha256', canvas)]:
                require(manifest[field] == hashlib.sha256(payload).hexdigest(), 'independent plane/canvas SHA256')
            require(set(alpha) == {0, 64, 128, 191, 192, 255}, 'all intended coverage codes')
            for field in ('physical_eye_proven', 'source_frame_association_proven', 'gpu_completion_proven', 'passthrough_authorized'):
                require(record[field] is False, 'source must not invent ' + field)
            require(record['opaque_fallback_required'] is True and record['native_frame_identity'] is None
                    and record['native_spatial_mapping'] is None, 'native authorities stay unavailable')
            old = probe.validate_reference(image, nonce, pair, eye, (320, 192),
                                           (manifest['color_sha256'], manifest['coverage_sha256']))
            require(old['reference_valid'] is False, 'new dense canvas cannot pass as the unchanged static-v1 artwork')
            # A mirrored or rotated source cannot retain either coordinate pattern.
            mirrored = b''.join(color[y * 960:(y + 1) * 960][x:x + 3] for y in range(192) for x in range(957, -1, -3))
            rotated = b''.join(color[i:i + 3] for i in range(len(color) - 3, -1, -3))
            require(hashlib.sha256(mirrored).digest() != hashlib.sha256(color).digest(), 'mirror trap')
            require(hashlib.sha256(rotated).digest() != hashlib.sha256(color).digest(), '180-degree trap')
            require(probe.header_for(320, 192, color, alpha, nonce, pair ^ 1, eye, 'straight') != header, 'wrong pair cannot satisfy header')
            require(probe.header_for(320, 192, color, alpha, nonce, pair, 'right' if eye == 'left' else 'left', 'straight') != header, 'wrong eye cannot satisfy header')
            tuples[(nonce, pair, eye)] = (color, alpha, canvas, header)
        first = tuples[cases[0]]
        repeated = tuples[cases[3]]
        require(first[:2] == repeated[:2] and first[2:] != repeated[2:], 'phase-cache reuse still carries fresh pair/header/digest')
        require(first[0] != tuples[cases[1]][0] and first[1] != tuples[cases[1]][1], 'asymmetric eyes')
        require(first[0] != tuples[cases[4]][0], 'session changes source coordinates')
        # Reconstruct the exact tuple again, never choose a latest receipt.
        destination = root / 'reconstructed'
        repeated_run = subprocess.run([str(args.dotnet), str(args.fixture), '--emit', str(destination), '--session', session,
                                       '--pair', '1', '--eye', 'left'], capture_output=True, text=True, timeout=30)
        require(repeated_run.returncode == 0, 'tuple reconstruction succeeds')
        require((destination / 'canvas.rgb8').read_bytes() == first[2], 'tuple reconstruction is exact')
    print(json.dumps({'oracle_checks': checks, 'tuple_fixtures': len(cases), 'unchanged_header_decoder': True,
                      'gpu_or_native_proof': False, 'passthrough_authorized': False}))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
