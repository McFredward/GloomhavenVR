"""Exact Unity2021 import upgrades of three pinned original Bloom sources."""
from pathlib import Path
import json

from manifest import ValidationError, sha256

# Original native GUID/recipe/pathID and both source hashes are already pinned
# by the legacy source recovery. An arbitrary importer change is not accepted.
PINS = {
    '30881e480b10c1b46a3d99ec13496f5e': ('Hidden/BlendForBloom', 135,
        '84f4f797c3f77fca2797806b388d50bdbac7492118212ded78370a6d9d636ae5',
        'd22461e93e8d3bd6801fe12a8ea8a12632d870fd54afc4d34dca839337838abf',
        '263f263605bc9ce88c366f33e895b696b32faf4aa812979f41e508e23fe48653', 2),
    '93f40d5ea0c0a7945a5782e2dcd23833': ('Hidden/BrightPassFilter2', 132,
        '26e81ea437fbb5ffb45cb8fc4556bdb2ab6c1c50d5396af5c2c8bc1562b8e424',
        '8a19269566f8fd692a44eb607c44f114abbc0a555d11c06995de22e2112e8dc1',
        'feba6bb83a31a7dc0f7388eb0aa7c9af897f76f1c17b1b5750f2e1710ebe854a', 1),
    '29d4384c2ae952c4597a9d894d381163': ('Hidden/BlurAndFlares', 137,
        '63283e8f5e60b6a7c2771b175c1c82fe62813648306ccf185c56379f75b196eb',
        '343d875aed4f5f221ce7d5e33df24ffc90459d9533448d7ba9edca80fa40d390',
        '4c6e89d84f8d16d060162186390ead698e0e528c805dfd1e4de75359a457cb17', 4),
}


def source_matches(row, actual, project):
    if actual == row['sourceSha256']: return True
    pin = PINS.get(row['guid'])
    if pin is None or row.get('sourceRestoration') != 'retained-source-contract': return False
    name, native_id, original, upgraded, recipe, count = pin
    if (row['originalName'], row['originalPathId'], row['sourceSha256'], actual) != (name, native_id, original, upgraded): return False
    contract = row.get('retainedSourceContract') or {}
    proof = contract.get('originalProvenance') or {}
    shader = proof.get('shader') or {}
    if contract.get('sourceSha256') != original or proof.get('receipt') != 'QuestStartupEvidence/legacy-post-effects.json': return False
    if any(shader.get(key) != value for key, value in dict(guid=row['guid'], name=name, originalPathId=native_id,
            sourceSha256=original, canonicalRecipeSha256=recipe, assetPath=row['assetPath']).items()): return False
    if shader.get('importUpgrade') != dict(kind='UnityObjectToClipPos', replacements=count, sha256=upgraded): return False
    receipt = Path(project) / proof['receipt']
    if not receipt.is_file() or sha256(receipt) != proof.get('receiptSha256'): return False
    candidates = [value for value in json.loads(receipt.read_text()).get('shaders', []) if value.get('guid') == row['guid']]
    return candidates == [shader]
