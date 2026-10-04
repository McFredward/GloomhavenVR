#!/usr/bin/env python3
"""Production legacy shader recovery checks, with no redistributed source assets."""
import argparse
import copy
import hashlib
import io
import json
from pathlib import Path
import shutil
import sys
import tarfile
import tempfile
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "tools/quest-builder"))
import shaders
from storage import BuildError

CHECKS = 0


def check(condition, description):
    global CHECKS
    CHECKS += 1
    if not condition:
        raise AssertionError(description)


def reject(action, description):
    try:
        action()
    except (BuildError, ValueError, KeyError):
        check(True, description)
    else:
        check(False, description)


def sha(content):
    return hashlib.sha256(content).hexdigest()


def synthetic_sources():
    specs = copy.deepcopy(shaders.SHADERS)
    sources, dummies = {}, {}
    for name, spec in specs.items():
        # This is test-owned syntax, not any downloaded Unity shader source.
        source = ('Shader "Hidden/' + name + '" {\n#include "UnityCG.cginc"\n' +
                  '\n'.join('float4 ' + field + ';' for field in spec['uniforms']) + '\n' +
                  '\n'.join('float4 fixtureVertex' + str(index) + ' = mul(UNITY_MATRIX_MVP, v.vertex);'
                            for index in range(spec['objectToClipPosReplacements'])) + '\n' +
                  '\n'.join('Pass {\n#pragma fragment ' + frag + '\n}' for frag in spec['fragments']) + '\n}').encode()
        dummy = ('//DummyShaderTextExporter\n//fixture ' + name).encode()
        spec['sourceSha256'], spec['sourceBytes'], spec['dummySha256'] = sha(source), len(source), sha(dummy)
        upgrade = (b"// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'\n\n" +
                   source.replace(b'mul(UNITY_MATRIX_MVP, v.vertex)', b'UnityObjectToClipPos(v.vertex)'))
        spec['importUpgradeSha256'] = sha(upgrade)
        sources[name], dummies[name] = source, dummy
    return specs, sources, dummies


def project_fixture(root, dummies):
    (root / 'Assets/Quest').mkdir(parents=True)
    for name, spec in shaders.SHADERS.items():
        target = root / ('Assets/Shader/Hidden_' + name + '.shader')
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(dummies[name])
        Path(str(target) + '.meta').write_text('fileFormatVersion: 2\nguid: ' + spec['guid'] +
                                            '\nShaderImporter:\n  userData: retained-test-data\n')


def fingerprint(root):
    return {path.relative_to(root).as_posix(): sha(path.read_bytes())
            for path in root.rglob('*') if path.is_file()}


def tar_package(sources, duplicate=False, missing=False, symbolic=False):
    out = io.BytesIO()
    with tarfile.open(fileobj=out, mode='w:gz') as archive:
        for index, (name, source) in enumerate(sources.items()):
            if missing and index == 0:
                continue
            base = 'record' + str(index) + '/'
            for relative, content in [('pathname', (shaders.PACKAGE_PREFIX + name + '.shader').encode()), ('asset', source)]:
                item = tarfile.TarInfo(base + relative)
                item.size = len(content)
                archive.addfile(item, io.BytesIO(content))
            if duplicate and index == 0:
                item = tarfile.TarInfo(base + 'asset')
                item.size = len(source)
                archive.addfile(item, io.BytesIO(source))
        if symbolic:
            item = tarfile.TarInfo('bad-link')
            item.type = tarfile.SYMTYPE
            item.linkname = '/outside'
            archive.addfile(item)
    return out.getvalue()


def run_tests(root):
    specs, sources, dummies = synthetic_sources()
    with patch.object(shaders, 'SHADERS', specs):
        for name, source in sources.items():
            shaders._validate_source(name, source)
            check(True, 'synthetic source contract ' + name)
            reject(lambda: shaders._validate_source(name, source + b'changed'), 'changed source rejected')
        cache = root / 'cache' / shaders.CACHE_NAME
        cache.mkdir(parents=True)
        for name, source in sources.items():
            (cache / (name + '.shader')).write_bytes(source)
        cache_before = fingerprint(cache)
        project = root / 'project'
        project_fixture(project, dummies)
        meta_before = {name: Path(str(project / ('Assets/Shader/Hidden_' + name + '.shader')) + '.meta').read_bytes()
                       for name in specs}
        with patch.object(shaders.urllib.request, 'urlopen', side_effect=AssertionError('cache should work offline')):
            receipt = shaders.restore_post_effects(project, root / 'cache')
            check(shaders.restore_post_effects(project, root / 'empty-cache') == receipt, 'idempotent verified receipt offline')
        check(fingerprint(cache) == cache_before, 'small cache remains read-only')
        check([row['passCount'] for row in receipt['shaders']] == [11, 2, 5], 'all original pass indexes retained')
        for name, content in sources.items():
            target = project / ('Assets/Shader/Hidden_' + name + '.shader')
            check(target.read_bytes() == content, 'selected source bytes retained')
            check(Path(str(target) + '.meta').read_bytes() == meta_before[name], 'metadata byte-for-byte retained')
        # Unity's pinned automatic API upgrade may occur after first import.
        # A header alone or arbitrary edits must never authorize changed payloads.
        for name, content in sources.items():
            target = project / ('Assets/Shader/Hidden_' + name + '.shader')
            upgraded = (b"// Upgrade NOTE: replaced 'mul(UNITY_MATRIX_MVP,*)' with 'UnityObjectToClipPos(*)'\n\n" +
                        content.replace(b'mul(UNITY_MATRIX_MVP, v.vertex)', b'UnityObjectToClipPos(v.vertex)'))
            target.write_bytes(upgraded)
            check(shaders.restore_post_effects(project, root / 'offline') == receipt,
                  'exact upgraded payload accepted offline: ' + name)
            check(target.read_bytes() == upgraded, 'upgraded payload reuse remains read-only: ' + name)
            check(Path(str(target) + '.meta').read_bytes() == meta_before[name], 'upgrade retains original metadata: ' + name)
            for bad in [upgraded + b'\n// unknown edit', upgraded.replace(b'UnityObjectToClipPos(v.vertex)', b'float4(0,0,0,1)', 1)]:
                target.write_bytes(bad)
                before = fingerprint(project)
                reject(lambda: shaders.restore_post_effects(project, root / 'offline'),
                       'upgrade header cannot authorize unknown or semantic changes: ' + name)
                check(fingerprint(project) == before, 'rejected upgraded change remains untouched: ' + name)
            target.write_bytes(upgraded)
        check(fingerprint(cache) == cache_before, 'upgrader handling never changes pinned official cache')
        with patch.object(shaders.urllib.request, 'urlopen', return_value=io.BytesIO(b'wrong official source')):
            reject(lambda: shaders.acquire(root / 'bad-download'), 'download SHA/length mismatch rejected')
        check(not list((root / 'bad-download').rglob('*.download')), 'failed download temporary file removed')
        check(not list((root / 'bad-download').rglob('*.shader')), 'failed download publishes no sources')
        bad_cache = root / 'bad-cache' / shaders.CACHE_NAME
        shutil.copytree(cache, bad_cache)
        (bad_cache / 'BlendForBloom.shader').write_bytes(b'bad')
        reject(lambda: shaders.acquire(root / 'bad-cache'), 'corrupt cache rejects instead of redownloading')
        for defect in ['dummy', 'guid', 'missing', 'receipt', 'source']:
            broken = root / ('broken-' + defect)
            project_fixture(broken, dummies)
            target = broken / 'Assets/Shader/Hidden_BlendForBloom.shader'
            if defect == 'dummy': target.write_bytes(b'unknown original')
            elif defect == 'guid': Path(str(target) + '.meta').write_text('guid: ' + '0' * 32 + '\nShaderImporter:\n')
            elif defect == 'missing': target.unlink()
            elif defect == 'receipt':
                (broken / shaders.RECEIPT).parent.mkdir(parents=True)
                (broken / shaders.RECEIPT).write_text('{}')
            before = fingerprint(broken)
            chosen_cache = root / ('bad-cache' if defect == 'source' else 'cache')
            reject(lambda: shaders.restore_post_effects(broken, chosen_cache), 'invalid ' + defect + ' rejected')
            check(fingerprint(broken) == before, 'invalid ' + defect + ' does not mutate project')
        rollback = root / 'rollback'
        project_fixture(rollback, dummies)
        before = fingerprint(rollback)
        with patch.object(shaders, 'write_json', side_effect=OSError('injected receipt write failure')):
            try: shaders.restore_post_effects(rollback, root / 'cache')
            except OSError: pass
            else: raise AssertionError('failure did not propagate')
        check(fingerprint(rollback) == before, 'receipt failure rolls back all sources')
        linked = root / 'linked'
        try:
            linked.symlink_to(project, target_is_directory=True)
        except OSError:
            pass  # Windows may not grant ordinary processes symlink creation.
        else:
            reject(lambda: shaders.restore_post_effects(linked, root / 'cache'), 'symlink project rejected')
        for label, options in [('good', {}), ('duplicate', {'duplicate': True}),
                               ('missing', {'missing': True}), ('symbolic', {'symbolic': True})]:
            package = root / (label + '.unitypackage')
            package.write_bytes(tar_package(sources, **options))
            if label == 'good':
                check(shaders._extract_sources(package) == sources, 'exact selected package payload')
            else:
                reject(lambda: shaders._extract_sources(package), label + ' package rejected')
        # Header negative controls use real parser and need no copyrighted bytes.
        invalid = root / 'invalid.pkg'
        invalid.write_bytes(b'x' * 28)
        reject(lambda: shaders._extract_effects(invalid, root / 'unwritten'), 'foreign XAR header rejected')
        check(not (root / 'unwritten').exists(), 'invalid archive writes no payload')
    return {'assertions': CHECKS, 'sourceAssetsRedistributed': False, 'unityLaunched': False}


def run_private(root, installer, recovered):
    shaders._require(installer, shaders.SOURCE_SHA256, shaders.SOURCE_BYTES)
    package = root / 'Effects.unitypackage'
    shaders._extract_effects(installer, package)
    sources = shaders._extract_sources(package)
    cache = root / 'official-cache' / shaders.CACHE_NAME
    cache.mkdir(parents=True)
    # Feed the exact official input through the real streaming acquisition path
    # without another network download; neither file is executed.
    with patch.object(shaders.urllib.request, 'urlopen', side_effect=lambda *a, **k: installer.open('rb')):
        paths = shaders.acquire(cache.parent)
    shaders._require(cache / 'StandardAssets.pkg', shaders.SOURCE_SHA256, shaders.SOURCE_BYTES)
    check(not list(cache.glob('*.download')), 'actual streamed download publishes atomically')
    project = root / 'official-project'
    (project / 'Assets/Quest').mkdir(parents=True)
    for name in shaders.SHADERS:
        relative = 'Assets/Shader/Hidden_' + name + '.shader'
        for suffix in ['', '.meta']:
            target = project / (relative + suffix)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(recovered / (relative + suffix), target)
    before = fingerprint(recovered / 'Assets/Shader')
    receipt = shaders.restore_post_effects(project, cache.parent)
    for name, source in sources.items():
        check(paths[name].read_bytes() == source, 'actual acquired source exact')
        check((project / ('Assets/Shader/Hidden_' + name + '.shader')).read_bytes() == source, 'actual staged source exact')
    check(fingerprint(recovered / 'Assets/Shader') == before, 'recovered reference remains read-only')
    check(shaders.restore_post_effects(project, root / 'offline') == receipt, 'actual stage reuse offline')
    return receipt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--official-installer', type=Path)
    parser.add_argument('--recovered-project', type=Path)
    parser.add_argument('--proof', type=Path)
    args = parser.parse_args()
    if bool(args.official_installer) != bool(args.recovered_project):
        parser.error('the two private proof inputs must be supplied together')
    with tempfile.TemporaryDirectory(prefix='quest-post-effects-') as temporary:
        root = Path(temporary)
        proof = run_tests(root)
        if args.official_installer:
            proof['officialSourceProof'] = run_private(root, args.official_installer, args.recovered_project)
        if args.proof:
            args.proof.parent.mkdir(parents=True, exist_ok=True)
            args.proof.write_text(json.dumps(proof, indent=2, sort_keys=True) + '\n')
        print(json.dumps({'passed': True, 'assertions': CHECKS, 'officialProof': bool(args.official_installer)}))


if __name__ == '__main__':
    main()
