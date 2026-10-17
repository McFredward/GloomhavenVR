#!/usr/bin/env python3
"""Causal controls against actual owned compiled source/derivative bundles."""
import argparse
import copy
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
from unittest import mock
sys.dont_write_bytecode = True
TOOL = Path(__file__).resolve().parents[2] / 'tools/scene-coverage-shader-bank/derive.py'
spec = importlib.util.spec_from_file_location('owned_coverage_bank', TOOL)
bank = importlib.util.module_from_spec(spec)
spec.loader.exec_module(bank)


def run(source, derived, output):
    source, derived, output = Path(source), Path(derived), Path(output)
    pin = bank.digest(source.read_bytes())
    pristine = source.read_bytes()
    checks, controls = 0, []
    def require(ok, why):
        nonlocal checks
        checks += 1
        if not ok:
            raise AssertionError(why)
    def refuse(label, call):
        try:
            call()
        except ValueError as error:
            controls.append({'name': label, 'refusal': str(error)})
            require(True, label)
        else:
            raise AssertionError('Causal mutation admitted: ' + label)
    proof = bank.verify(source, derived, pin)
    require(proof['complete_programs_and_other_shader_state_equal'] and proof['source_immutable'], 'actual complete derivative passes')
    require(bank.canonical({'a': -0.0}) != bank.canonical({'a': 0.0}), 'complete state equality retains signed zero')
    refuse('wrong-source-pin', lambda: bank.verify(source, derived, '0' * 64))
    refuse('overwrite-source', lambda: bank.derive(source, source, pin))
    refuse('overwrite-existing-derived', lambda: bank.derive(source, derived, pin))
    with tempfile.TemporaryDirectory(prefix='mutants-', dir=output.parent) as folder:
        folder = Path(folder)
        def mutate(label, mutation, verify_source=False):
            loaded = bank.load(source if verify_source else derived)
            shader, metadata = bank.tree(loaded)
            mutation(loaded, shader, metadata)
            loaded[-2].save_typetree(shader)
            loaded[-1].save_typetree(metadata)
            path = folder / (label + '.bundle')
            path.write_bytes(loaded[1].save(packer='none'))
            if verify_source:
                return lambda: bank.derive(path, folder/(label+'-output.bundle'), pin)
            return lambda: bank.verify(source, path, pin)
        def state(shader):
            return shader['m_ParsedForm']['m_SubShaders'][0]['m_Passes'][0]['m_State']
        def program(_loaded, shader, _metadata):
            shader['compressedBlob'][0] ^= 1
        refuse('compiled-program-mutated', mutate('program', program))
        refuse('RGB-source-factor-mutated', mutate('rgb', lambda _,s,b: state(s)['rtBlend0']['srcBlend'].__setitem__('val',1.0)))
        refuse('depth-state-mutated', mutate('depth', lambda _,s,b: state(s)['zWrite'].__setitem__('val',1.0)))
        refuse('clip-program-metadata-mutated', mutate('metadata', lambda _,s,b: s['m_ParsedForm']['m_SubShaders'][0]['m_Passes'][0]['m_NameIndices'].append(('UNAPPROVED',99))))
        refuse('wrong-alpha-factor', mutate('alpha', lambda _,s,b: state(s)['rtBlend0']['srcBlendAlpha'].__setitem__('val',5.0)))
        refuse('shader-identity-mutated', mutate('identity', lambda _,s,b: s['m_ParsedForm'].__setitem__('m_Name','CoverageBank/Other')))
        refuse('bank-dependency-added', mutate('dependency', lambda _,s,b: b['m_Dependencies'].append('foreign.bundle')))
        def external(loaded, _shader, _metadata):
            from UnityPy.files.SerializedFile import FileIdentifier
            entry = object.__new__(FileIdentifier)
            entry.path, entry.temp_empty, entry.guid, entry.type = 'foreign.assets', '', bytes(16), 0
            loaded[2].externals.append(entry)
        refuse('serialized-external-added', mutate('external', external))
        def cab(loaded, _shader, _metadata):
            loaded[2].name = 'CAB-'+'0'*32
            loaded[1].files = {loaded[2].name: loaded[2]}
        refuse('CAB-collision-mutated', mutate('cab', cab))
        refuse('source-program-mutated', mutate('source', program, True))
        # Repinning a source is not enough to authorize property-controlled state.
        loaded = bank.load(source)
        shader, metadata = bank.tree(loaded)
        state(shader)['rtBlend0']['srcBlendAlpha']['name'] = '_UnobservedAlpha'
        loaded[-2].save_typetree(shader)
        path = folder/'property-controlled.bundle'
        path.write_bytes(loaded[1].save(packer='none'))
        refuse('property-state-not-literal', lambda: bank.derive(path, folder/'property-output.bundle', bank.digest(path.read_bytes())))
    require(source.read_bytes() == pristine, 'every original byte still intact after all controls')
    require(bank.digest(derived.read_bytes()) == proof['derived_sha256'], 'positive derivative remains immutable')
    report = {'passed': True, 'checks': checks, 'causal_controls': controls,
              'controls': len(controls), 'scope': 'OWNED_COMPILED_BUNDLE_ONLY',
              'source_immutable': True, 'game_shader_admitted': False, 'runtime_adoption': False}
    output.write_text(json.dumps(report, indent=2)+'\n')
    print(json.dumps(report))


def io_delta(source, derived, output):
    source, derived, output = Path(source), Path(derived), Path(output)
    pristine = source.read_bytes()
    pin = bank.digest(pristine)
    checks, rows = 0, []
    def require(ok, message):
        nonlocal checks
        checks += 1
        if not ok:
            raise AssertionError(message)
    with tempfile.TemporaryDirectory(prefix='io-controls-', dir=output.parent) as directory:
        directory = Path(directory)
        def cli(label, candidate, report, input_path=source, success=False):
            result = subprocess.run([sys.executable, str(TOOL), str(input_path), str(candidate),
                                     '--source-sha256', pin, '--report', str(report)],
                                    capture_output=True, text=True, timeout=30)
            rows.append({'name': label, 'exit_code': result.returncode,
                         'stderr': result.stderr[-1000:]})
            require((result.returncode == 0) == success, 'actual CLI expected result: '+label)
            require(source.read_bytes() == pristine, 'actual CLI never changes original: '+label)
            if not success:
                require(not candidate.exists(), 'rejected CLI never leaves candidate: '+label)
            return result
        cli('report-equals-source', directory/'alias-source.bundle', source)
        same = directory/'same-output-report'
        cli('report-equals-output', same, same)
        existing = directory/'existing-report.json'
        existing.write_bytes(b'KEEP ORIGINAL REPORT')
        cli('preexisting-report', directory/'existing-report.bundle', existing)
        require(existing.read_bytes() == b'KEEP ORIGINAL REPORT', 'preexisting report unchanged')
        linked = directory/'report-link'
        linked.symlink_to(source.resolve())
        cli('report-symlink-to-source', directory/'symlink.bundle', linked)
        require(linked.is_symlink(), 'report symlink never followed/replaced')
        # Sparse oversize is rejected BEFORE the first content read.
        oversized = directory/'oversized.bundle'
        with oversized.open('wb') as stream:
            stream.truncate(bank.MAXIMUM_BYTES+1)
        with mock.patch.object(bank.os, 'read', side_effect=AssertionError('oversize read attempted')):
            try:
                bank.read_bounded(oversized)
            except ValueError as error:
                require('extent' in str(error), 'stat rejects oversize before any read')
            else:
                raise AssertionError('oversized input admitted')
        cli('oversized-input', directory/'oversize-output.bundle', directory/'oversize-report.json', oversized)
        # A file growing after fstat is still read at most MAX+1, then refused.
        growing = directory/'growing.bundle'
        growing.write_bytes(b'seed')
        original_read = os.read
        requests, returned = [], []
        def grow(fd, count):
            if not requests:
                with growing.open('r+b') as stream:
                    stream.truncate(bank.MAXIMUM_BYTES+131072)
            requests.append(count)
            chunk = original_read(fd, count)
            returned.append(len(chunk))
            return chunk
        with mock.patch.object(bank.os, 'read', side_effect=grow):
            try:
                bank.read_bounded(growing)
            except ValueError as error:
                require('extent' in str(error), 'growth during bounded read refused')
            else:
                raise AssertionError('growing input admitted')
        require(sum(returned) <= bank.MAXIMUM_BYTES+1 and max(requests) <= 65536,
                'changing input content read remains bounded')
        rows.append({'name':'grow-after-stat','read_bytes':sum(returned),'maximum_request':max(requests)})
        candidate, report = directory/'positive.bundle', directory/'positive.json'
        cli('positive-exclusive-output-report', candidate, report, success=True)
        result = json.loads(report.read_text())
        require(result['source_immutable'] and result['complete_programs_and_other_shader_state_equal'], 'actual exclusive report complete')
        require(candidate.read_bytes() == derived.read_bytes(), 'IO repair preserves exact previously GPU-tested derivative bytes')
    result = {'passed': True, 'scope': 'OFFLINE_TOOL_IO_DELTA_ONLY', 'checks':checks,
              'cases':rows, 'source_immutable':True, 'gpu_repeated':False}
    output.write_text(json.dumps(result,indent=2)+'\n')
    print(json.dumps(result))


if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--io-delta',action='store_true');parser.add_argument('source',type=Path);parser.add_argument('derived',type=Path);parser.add_argument('output',type=Path)
    args=parser.parse_args();(io_delta if args.io_delta else run)(args.source,args.derived,args.output)
