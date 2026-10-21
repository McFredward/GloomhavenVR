"""Partition native compiler evidence without reducing the original graph.

Each shard uses its own Unity project/Library. Its native shader and material
identities are disjoint. The merged receipt is accepted only if every row from
the authoritative manifest occurs exactly once with unchanged provenance.
"""
from __future__ import annotations
import argparse
import copy
import json
from pathlib import Path
import shutil

from manifest import ValidationError, sha256, validate_receipt


def partition(source: Path, output: Path, ends: list[int]):
    original = json.loads(source.read_text())
    total = len(original['shaders'])
    if ends != sorted(set(ends)) or not ends or ends[-1] != total or ends[0] < 1:
        raise ValidationError('Shard boundaries must partition every original shader identity.')
    output.mkdir(parents=True, exist_ok=True)
    paths = []
    begin = 0
    for index, end in enumerate(ends):
        shard = copy.deepcopy(original)
        shard['shaders'] = original['shaders'][begin:end]
        owned = {row['guid'] for row in shard['shaders']}
        shard['materials'] = [row for row in original['materials'] if row['shaderGuid'] in owned or
                              index == 0 and (row.get('originalEngineBuiltinShader') or row.get('originalShaderNull'))]
        shard['requiredShaderCount'] = len(shard['shaders'])
        shard['requiredMaterialCount'] = len(shard['materials'])
        shard['partition'] = dict(index=index, fullManifestSha256=sha256(source), begin=begin, end=end)
        path = output / f'native-shard-{index}.json'
        path.write_text(json.dumps(shard, indent=2) + '\n')
        paths.append(path)
        begin = end
    return paths


def merge(source: Path, shards: list[Path], receipts: list[Path], output: Path):
    if len(shards) != len(receipts):
        raise ValidationError('Every native shard requires one actual compiler receipt.')
    original = json.loads(source.read_text())
    rows, materials, programs = {}, {}, []
    output.mkdir(parents=True, exist_ok=True)
    source_hash = sha256(source)
    backend = original['graphicsApi']
    inputs = []
    for manifest_path, receipt_path in zip(shards, receipts):
        shard, receipt = json.loads(manifest_path.read_text()), json.loads(receipt_path.read_text())
        if shard.get('partition', {}).get('fullManifestSha256') != source_hash:
            raise ValidationError('Shard derives from a different authoritative source manifest.')
        if shard['programs'] != original['programs'] or shard['graphicsApi'] != backend:
            raise ValidationError('Shard changed native instruction provenance or backend.')
        validate_receipt(shard, sha256(manifest_path), receipt)
        if receipt['unityVersion'] != '2021.3.5f1':
            raise ValidationError('Native shard was compiled by a different Unity version.')
        for row in shard['shaders']:
            if row['guid'] in rows: raise ValidationError('Duplicate native shader partition.')
            rows[row['guid']] = row
        for row in shard['materials']:
            if row['guid'] in materials: raise ValidationError('Duplicate native material partition.')
            materials[row['guid']] = row
        for row in receipt['programs']:
            for field, hash_field in (('file', 'bankSha256'), ('vertexFile', 'vertexSha256'), ('fragmentFile', 'fragmentSha256')):
                name = row[field]
                if Path(name).name != name: raise ValidationError('Native shard bank path escapes its evidence directory.')
                path = receipt_path.parent / name
                if sha256(path) != row[hash_field]: raise ValidationError('Actual shard bank bytes changed.')
                target = output / name
                if target.exists():
                    if sha256(target) != row[hash_field]: raise ValidationError('Native shard bank name collision.')
                else: shutil.copy2(path, target)
            programs.append(row)
        inputs.append(dict(manifestSha256=sha256(manifest_path), receiptSha256=sha256(receipt_path)))
    if rows != {r['guid']: r for r in original['shaders']} or materials != {r['guid']: r for r in original['materials']}:
        raise ValidationError('Native shard union changes or omits an original identity.')
    result = dict(schema=1, materialCount=len(materials), unityVersion='2021.3.5f1', graphicsApi=backend,
                  compilerPlatform=original['compilerPlatform'], sourceManifestSha256=source_hash,
                  originalPixelParityVerified=False, headsetPictureVerified=False, programs=programs, partitions=inputs)
    validate_receipt(original, source_hash, result)
    (output / 'android-compiler.json').write_text(json.dumps(result, indent=2) + '\n')
    return result


def main():
    p = argparse.ArgumentParser(description=__doc__)
    commands = p.add_subparsers(dest='command', required=True)
    split = commands.add_parser('partition'); split.add_argument('--manifest', type=Path, required=True)
    split.add_argument('--output', type=Path, required=True); split.add_argument('--ends', type=int, nargs='+', required=True)
    join = commands.add_parser('merge'); join.add_argument('--manifest', type=Path, required=True)
    join.add_argument('--output', type=Path, required=True); join.add_argument('--shards', type=Path, nargs='+', required=True)
    join.add_argument('--receipts', type=Path, nargs='+', required=True)
    a = p.parse_args()
    if a.command == 'partition':
        for path in partition(a.manifest, a.output, a.ends): print(path)
    else: print('Merged actual native shader aliases:', len(merge(a.manifest, a.shards, a.receipts, a.output)['programs']))


if __name__ == '__main__': main()
