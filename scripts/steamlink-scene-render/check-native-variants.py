#!/usr/bin/env python3
"""Verify serialized native-program preservation and reject state/program drift."""
import argparse
import copy
import hashlib
import importlib.util
import json
from pathlib import Path
import sys
sys.dont_write_bytecode = True
HERE = Path(__file__).resolve().parent
spec = importlib.util.spec_from_file_location('native_variants', HERE/'native_variants.py')
native = importlib.util.module_from_spec(spec); spec.loader.exec_module(native)
import UnityPy


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, required=True)
    parser.add_argument('--bank', type=Path, required=True)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    manifest = json.loads((HERE/'native-variants.json').read_text())
    records = native.load_sources(args.root, manifest)
    env = UnityPy.load(str(args.bank))
    inventory = {o.read_typetree()['m_ParsedForm']['m_Name']: o.read_typetree()
                 for o in env.objects if o.type.name == 'Shader'}
    assert len(inventory) == 23 and len(env.container) == 23
    checks, controls = 2, []
    for item, obj, source in records:
        candidate = inventory[item['name'] + native.SUFFIX]
        native.verify_derivative(source, candidate); checks += 1
        for field in ['srcBlend', 'destBlend', 'destBlendAlpha', 'blendOpAlpha', 'colMask']:
            changed = copy.deepcopy(candidate)
            blend = changed['m_ParsedForm']['m_SubShaders'][0]['m_Passes'][0]['m_State']['rtBlend0']
            blend[field]['val'] += 1
            try: native.verify_derivative(source, changed)
            except AssertionError: controls.append(item['name'] + ':' + field)
            else: raise AssertionError('Non-alpha native blend mutation escaped: ' + field)
        for field in ['zWrite', 'zTest', 'culling']:
            changed = copy.deepcopy(candidate)
            state = changed['m_ParsedForm']['m_SubShaders'][0]['m_Passes'][0]['m_State']
            state[field]['val'] += 1
            try: native.verify_derivative(source, changed)
            except AssertionError: controls.append(item['name'] + ':' + field)
            else: raise AssertionError('Native depth/cull mutation escaped: ' + field)
        changed = copy.deepcopy(candidate)
        program = list(changed['compressedBlob']); program[0] ^= 1
        changed['compressedBlob'] = program
        try: native.verify_derivative(source, changed)
        except AssertionError: controls.append(item['name'] + ':native-program')
        else: raise AssertionError('Native GPU program mutation escaped')
        changed = copy.deepcopy(candidate)
        changed['m_ParsedForm']['m_SubShaders'][0]['m_Passes'][0]['m_State']['rtBlend0']['srcBlendAlpha']['name'] = '_ForeignAlpha'
        try: native.verify_derivative(source, changed)
        except AssertionError: controls.append(item['name'] + ':alpha-binding')
        else: raise AssertionError('Wrong alpha global escaped')
    report = {'schema':'native-alpha-preservation-proof-v1', 'passed':True,
              'positive_assertions':checks, 'rejected_mutations':len(controls), 'controls':controls,
              'bank_sha256':hashlib.sha256(args.bank.read_bytes()).hexdigest(),
              'source_pins':{str(p):hashlib.sha256(p.read_bytes()).hexdigest() for p in [Path(__file__),HERE/'native_variants.py',HERE/'native-variants.json']},
              'limits':'Full original native D3D11 GPU program and non-alpha serialized state preservation, not D3D11 execution or HMD proof.'}
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report,indent=2)+'\n')
    print('PASS native derivatives: '+str(checks)+' assertions; '+str(len(controls))+' rejected metadata/program mutations')


if __name__ == '__main__': main()
