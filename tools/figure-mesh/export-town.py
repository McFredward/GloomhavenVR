#!/usr/bin/env python3
"""Append lossless original NPC body/expression streams from the shipped town bundle."""
import argparse
import hashlib
import importlib.util
import json
from pathlib import Path
import UnityPy

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--bundle', type=Path, required=True)
parser.add_argument('--output-dir', type=Path, required=True)
args = parser.parse_args()
spec = importlib.util.spec_from_file_location('native', Path(__file__).with_name('export-native.py'))
native = importlib.util.module_from_spec(spec)
spec.loader.exec_module(native)
index = args.output_dir / 'sources.json'
records = json.loads(index.read_text())
records['meshes'] = [entry for entry in records['meshes'] if not entry.get('npc')]
env = UnityPy.load(str(args.bundle)); seen = set()
for obj in env.objects:
    if obj.type.name != 'SkinnedMeshRenderer': continue
    skin = obj.read(); mesh = skin.m_Mesh.read()
    if mesh.m_VertexData.m_VertexCount < 1000 or skin.m_Mesh.path_id in seen: continue
    seen.add(skin.m_Mesh.path_id)
    protected = [i for i, bone in enumerate(skin.m_Bones)
                 if any(name in bone.read().m_GameObject.read().m_Name.lower() for name in ('head', 'jaw', 'eye'))]
    if not protected: raise SystemExit('NPC facial bone identity unavailable')
    target = args.output_dir / f'town-{len(seen):02d}.mesh'
    record = native.export_mesh(mesh, target, blend_shapes=True)
    record.update({'file': target.name, 'bundle': args.bundle.name, 'bundle_sha256': hashlib.sha256(args.bundle.read_bytes()).hexdigest(),
                   'npc': True, 'protectedBones': protected})
    records['meshes'].append(record)
index.write_text(json.dumps(records, indent=2) + '\n')
print('Appended', len(seen), 'NPC bodies with original expressions and protected facial bones')
