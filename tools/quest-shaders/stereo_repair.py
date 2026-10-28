"""Transactional provenance for the witnessed native FrontFace stereo adapter."""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re

from manifest import ValidationError, sha256

SCOPE = 'native-fragment-stereo-input-order'
DIRECTORY = 'QuestCampaignEvidence'
RECEIPT = DIRECTORY + '/fragment-stereo-input-order.json'
IDENTITIES = DIRECTORY + '/fragment-stereo-input-order-identities.json'
WITNESS = DIRECTORY + '/fragment-stereo-input-order-witness.json'
INPUT = DIRECTORY + '/fragment-stereo-input-order-input.json'
DRIVER = DIRECTORY + '/fragment-stereo-input-order-driver.json'
MACRO = b'    UNITY_VERTEX_OUTPUT_STEREO\n'


def digest(data):
    return hashlib.sha256(data).hexdigest()


def encoded(value):
    return (json.dumps(value, sort_keys=True, indent=2) + '\n').encode()


def asset(root, relative):
    if not isinstance(relative, str) or any(character in relative for character in ('\\', ':', '\x00')):
        raise ValidationError('Stereo repair requires an exact portable relative path.')
    parts = PurePosixPath(relative)
    if parts.is_absolute() or not parts.parts or any(p in ('..', '.', '') for p in relative.split('/')):
        raise ValidationError('Stereo repair path escapes its private project.')
    result = root.joinpath(*parts.parts)
    if result.resolve() != result.absolute() or not result.resolve().is_relative_to(root):
        raise ValidationError('Stereo repair cannot follow a symlink outside its owned bytes.')
    return result


def atomic(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = path.with_name(path.name + '.stereo-order-pending')
    with temporary.open('xb') as stream:
        stream.write(data)
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temporary, path)


def identity(root, row, guid=False):
    path = asset(root, row['assetPath'])
    value = {'assetPath': row['assetPath'], 'sha256': sha256(path), 'metaSha256': sha256(Path(str(path) + '.meta'))}
    if guid:
        actual = re.search(r'(?m)^guid: ([0-9a-f]{32})$', Path(str(path) + '.meta').read_text())
        if actual is None or actual[1] != row['guid']:
            raise ValidationError('Original shader/material GUID differs from its native identity.')
        value['guid'] = row['guid']
    return value


def verify_ledger(root, ledger):
    for kind in ('shaders', 'materials', 'programs'):
        for row in ledger[kind]:
            if identity(root, row, kind != 'programs') != row:
                raise ValidationError('Unchanged original shader/material/program identity drifted during stereo repair.')


def read_completed(root, manifest, manifest_path):
    marker = manifest.get('fragmentStereoInputOrderRepair')
    if not isinstance(marker, dict) or marker != {'path': RECEIPT, 'scope': SCOPE, 'programCount': marker.get('programCount')}:
        raise ValidationError('Stereo repair manifest marker is incomplete or unknown.')
    receipt = json.loads(asset(root, RECEIPT).read_bytes())
    if receipt.get('schema') != 1 or receipt.get('scope') != SCOPE or receipt['manifest']['path'] != manifest_path or receipt['manifest']['afterSha256'] != sha256(asset(root, manifest_path)):
        raise ValidationError('Completed stereo repair manifest provenance differs.')
    if marker['programCount'] != receipt['programCount'] or receipt['changedProgramCount'] != receipt['programCount'] or len(receipt['programs']) != receipt['programCount']:
        raise ValidationError('Completed stereo repair program census differs.')
    for role in ('identities', 'nativeCompilerWitness', 'nativeDriverWitness', 'nativeCompilerInput'):
        if sha256(asset(root, receipt[role]['path'])) != receipt[role]['sha256']:
            raise ValidationError('Completed stereo repair evidence bytes differ.')
    for row in receipt['programs']:
        path = asset(root, row['assetPath'])
        if sha256(path) != row['afterSha256'] or sha256(Path(str(path) + '.meta')) != row['metaSha256'] or digest(path.read_bytes().replace(MACRO, b'')) != row['restBytesSha256']:
            raise ValidationError('Completed stereo repair original program bytes differ.')
        candidates = [entry for entry in manifest['programs'] if entry['assetPath'] == row['assetPath']]
        if len(candidates) != 1 or candidates[0]['sourceSha256'] != row['afterSha256'] or any(candidates[0][key] != row[key] for key in ('originalDxbcSha256', 'originalInterfaceSha256')):
            raise ValidationError('Completed stereo repair native manifest identity differs.')
    verify_ledger(root, json.loads(asset(root, receipt['identities']['path']).read_bytes()))
    return receipt


def repair(project, manifest_path=None, compiler_witness=None):
    from produce import fragment_stereo_input_order
    root = Path(project).resolve()
    manifest_path = manifest_path or 'Assets/QuestOriginalCampaign/campaign-shaders.json'
    manifest_file = asset(root, manifest_path)
    before_manifest = manifest_file.read_bytes()
    manifest = json.loads(before_manifest)
    if manifest.get('schema') != 1 or manifest.get('scope') != 'campaign-compiler' or manifest.get('graphicsApi') != 'Vulkan':
        raise ValidationError('Stereo repair requires the actual original Vulkan Campaign manifest.')
    if manifest.get('fragmentStereoInputOrderRepair'):
        return read_completed(root, manifest, manifest_path)
    if asset(root, RECEIPT).exists():
        raise ValidationError('Unregistered stereo repair receipt exists; inspect the stopped project before repair.')
    transaction = asset(root, DIRECTORY + '/fragment-stereo-input-order.transaction')
    if transaction.exists():
        raise ValidationError('Interrupted stereo repair detected; restore its recorded originals before retrying.')
    if compiler_witness is None:
        raise ValidationError('A completed actual same-version native compiler/bundle witness is required.')
    witness_root = Path(compiler_witness).resolve()
    witness_bytes = (witness_root / 'FoliageWitness/results.json').read_bytes()
    input_bytes = (witness_root / 'WitnessInput.json').read_bytes()
    driver_bytes = (witness_root.parent / 'actual-driver/actual-vulkan-pipelines.json').read_bytes()
    witness, inputs, driver = map(json.loads, (witness_bytes, input_bytes, driver_bytes))
    if witness.get('schema') != 1 or witness.get('unityVersion') != '2021.3.5f1' or witness.get('inputSha256') != digest(input_bytes) or any(witness.get(key) is not True for key in ('originalDeclarationsAndMathPreserved', 'originalShaderAndMetaPreserved', 'actualNativeBundleBuilt')):
        raise ValidationError('Native stereo declaration witness is not a completed original-preserving bundle PASS.')
    if witness.get('headsetPictureVerified') is not False or witness.get('originalPixelParityVerified') is not False:
        raise ValidationError('Native compiler evidence cannot claim headset or original pixel parity.')
    if witness.get('baselineRejectedCount') != len(inputs['failures']) or witness.get('positiveCompilerCount') != len(inputs['failures']) + len(inputs['nativeControls']) or witness.get('monoExactStageCount') != len(inputs['nativeControls']) or not inputs['failures'] or not inputs['nativeControls']:
        raise ValidationError('Native stereo compiler negative/positive/mono census is incomplete.')
    banks = witness['banks']
    if [row['variant'] for row in banks] != inputs['failures'] + inputs['nativeControls']:
        raise ValidationError('Native stereo compiler keyword/pass identities differ.')
    for row in banks:
        for stage in ('vertex', 'fragment'):
            path = asset(witness_root / 'FoliageWitness', row[stage + 'File'])
            if sha256(path) != row[stage + 'Sha256']:
                raise ValidationError('Actual native stereo compiler output bytes differ.')
    driver_aliases = [{'variant': row['variant'], 'vertexSha256': row['vertexSha256'], 'fragmentSha256': row['fragmentSha256']} for row in banks]
    if driver.get('schema') != 1 or driver.get('graphicsApi') != 'Vulkan' or driver.get('sourceWitnessSha256') != digest(witness_bytes) or driver.get('aliases') != driver_aliases or driver.get('actualNativePipelineAliasCount') != len(banks) or driver.get('actualDistinctNativePipelineCount') != len({(row['vertexSha256'], row['fragmentSha256']) for row in banks}) or driver.get('missingNativeEntryRejected') is not True:
        raise ValidationError('Actual native Vulkan driver pipeline witness is incomplete.')
    if driver.get('headsetPictureVerified') is not False or driver.get('originalWindowsPixelParityVerified') is not False:
        raise ValidationError('Actual Vulkan driver compilation cannot establish headset/original pixel parity.')
    if len(manifest['shaders']) != manifest['requiredShaderCount'] or len(manifest['materials']) != manifest['requiredMaterialCount'] or sum(row['coverageKind'] == 'original-native' for shader in manifest['shaders'] for row in shader['variants']) != manifest['requiredOriginalNativeAliasCount']:
        raise ValidationError('Original Campaign shader/material/alias census differs.')
    proof_shader = next((row for row in manifest['shaders'] if row['guid'] == witness['shaderGuid']), None)
    if proof_shader is None or proof_shader['assetPath'] != inputs['shaderPath'] or sha256(asset(root, proof_shader['assetPath'])) != witness['shaderSha256'] or sha256(asset(root, proof_shader['assetPath'] + '.meta')) != witness['shaderMetaSha256']:
        raise ValidationError('Actual witnessed native Shader identity differs from the retained project.')
    input_rewrites = {row['assetPath']: row for row in inputs['rewrites']}
    if len(input_rewrites) != len(inputs['rewrites']):
        raise ValidationError('Native witness duplicates an original program identity.')
    rewritten, untouched, replacements = [], [], []
    native_pairs = set()
    for row in manifest['programs']:
        native_pair = row['originalDxbcSha256'], row['originalInterfaceSha256']
        if native_pair in native_pairs:
            raise ValidationError('Original native program identity is duplicated.')
        native_pairs.add(native_pair)
        path = asset(root, row['assetPath'])
        source = path.read_bytes()
        if digest(source) != row['sourceSha256']:
            raise ValidationError('Original native include source differs before declaration repair.')
        signature = row['originalInputSignature']
        if not any(field['systemValue'] == 9 for field in signature):
            untouched.append(identity(root, row)); continue
        if not any(variant.get('fragmentOriginalDxbcSha256') == native_pair[0] for variant in proof_shader['variants']):
            raise ValidationError('Native front-face program lacks its actual witnessed Shader ownership.')
        corrected, proof = fragment_stereo_input_order(source.decode('utf-8'), 'fragment', signature)
        corrected = corrected.encode('utf-8')
        if source == corrected or source.count(MACRO) != 1 or corrected.count(MACRO) != 1 or source.replace(MACRO, b'') != corrected.replace(MACRO, b''):
            raise ValidationError('Native stereo repair changed more than the one generated declaration order.')
        changed = {'assetPath': row['assetPath'], 'originalDxbcSha256': native_pair[0], 'originalInterfaceSha256': native_pair[1],
                   'beforeSha256': digest(source), 'afterSha256': digest(corrected), 'metaSha256': sha256(Path(str(path) + '.meta')),
                   'restBytesSha256': digest(source.replace(MACRO, b'')), 'originalDeclarationsAndMathPreserved': True}
        witnessed = input_rewrites.get(row['assetPath'])
        if witnessed is None or any(witnessed[key] != changed[key] for key in ('beforeSha256', 'afterSha256', 'metaSha256', 'originalDxbcSha256', 'originalInterfaceSha256')):
            raise ValidationError('Native program repair differs from its exact actual compiler witness.')
        rewritten.append(changed); replacements.append((path, source, corrected))
        row['sourceSha256'] = changed['afterSha256']; row['fragmentStereoInputOrderProofs'] = proof
    if not rewritten or set(input_rewrites) != {row['assetPath'] for row in rewritten} or witness['originalInputRewriteCount'] != len(rewritten):
        raise ValidationError('Actual native stereo program repair census differs.')
    ledger = {'schema': 1, 'scope': SCOPE + '-identities', 'originalAliasCount': manifest['requiredOriginalNativeAliasCount'],
              'shaders': [identity(root, row, True) for row in manifest['shaders']],
              'materials': [identity(root, row, True) for row in manifest['materials']], 'programs': untouched}
    ledger_bytes = encoded(ledger)
    marker = {'path': RECEIPT, 'scope': SCOPE, 'programCount': len(rewritten)}
    manifest['fragmentStereoInputOrderRepair'] = marker
    after_manifest = encoded(manifest)
    generator = Path(__file__).with_name('produce.py')
    receipt = {'schema': 1, 'scope': SCOPE, 'graphicsApi': 'Vulkan', 'applied': True,
               'generator': {'path': 'tools/quest-shaders/produce.py', 'sha256': sha256(generator)},
               'repairHelper': {'path': 'tools/quest-shaders/stereo_repair.py', 'sha256': sha256(Path(__file__))},
               'manifest': {'path': manifest_path, 'beforeSha256': digest(before_manifest), 'afterSha256': digest(after_manifest)},
               'programCount': len(rewritten), 'changedProgramCount': len(rewritten), 'programs': rewritten,
               'identities': {'path': IDENTITIES, 'sha256': digest(ledger_bytes), 'shaderCount': len(ledger['shaders']), 'materialCount': len(ledger['materials']), 'originalAliasCount': ledger['originalAliasCount']},
               'nativeCompilerWitness': {'path': WITNESS, 'sha256': digest(witness_bytes), **{key: witness[key] for key in ('baselineRejectedCount', 'positiveCompilerCount', 'monoExactStageCount', 'actualNativeBundleBuilt')}},
               'nativeCompilerInput': {'path': INPUT, 'sha256': digest(input_bytes)},
               'nativeDriverWitness': {'path': DRIVER, 'sha256': digest(driver_bytes), **{key: driver[key] for key in ('actualNativePipelineAliasCount', 'actualDistinctNativePipelineCount', 'missingNativeEntryRejected')}},
               'headsetPictureVerified': False, 'originalPixelParityVerified': False}
    # Verify stable input closure immediately before committing any output.
    if manifest_file.read_bytes() != before_manifest or sha256(generator) != receipt['generator']['sha256'] or sha256(Path(__file__)) != receipt['repairHelper']['sha256']:
        raise ValidationError('Stereo repair manifest/generator changed while planning.')
    verify_ledger(root, ledger)
    for path, source, _ in replacements:
        if path.read_bytes() != source:
            raise ValidationError('Native program changed while planning exact declaration repair.')
    evidence = [(asset(root, IDENTITIES), ledger_bytes), (asset(root, WITNESS), witness_bytes),
                (asset(root, INPUT), input_bytes), (asset(root, DRIVER), driver_bytes)]
    if any(path.exists() for path, _ in evidence):
        raise ValidationError('Unregistered stereo repair evidence already exists.')
    # A complete receipt is the final commit record. Each file is atomically
    # replaced; caught errors restore original bytes. A process-killed partial
    # operation keeps a transaction marker and fails closed on the next call.
    atomic(transaction, encoded({'schema': 1, 'scope': SCOPE, 'manifestBeforeSha256': digest(before_manifest), 'programs': rewritten}))
    try:
        for path, source, corrected in replacements:
            if path.read_bytes() != source:
                raise ValidationError('Native include changed during declaration repair.')
            atomic(path, corrected)
        for path, data in evidence:
            atomic(path, data)
        verify_ledger(root, ledger)
        for row in rewritten:
            if sha256(asset(root, row['assetPath'])) != row['afterSha256'] or sha256(asset(root, row['assetPath'] + '.meta')) != row['metaSha256']:
                raise ValidationError('Native include/meta changed during declaration repair.')
        if manifest_file.read_bytes() != before_manifest:
            raise ValidationError('Native manifest changed during declaration repair.')
        if sha256(generator) != receipt['generator']['sha256'] or sha256(Path(__file__)) != receipt['repairHelper']['sha256']:
            raise ValidationError('Stereo repair generator changed during declaration repair.')
        atomic(manifest_file, after_manifest)
        atomic(asset(root, RECEIPT), encoded(receipt))
    except Exception:
        for path, source, corrected in replacements:
            if path.read_bytes() == corrected:
                atomic(path, source)
        if manifest_file.read_bytes() == after_manifest:
            atomic(manifest_file, before_manifest)
        for path, data in evidence:
            if path.is_file() and path.read_bytes() == data:
                path.unlink()
        raise
    else:
        transaction.unlink()
    return receipt
