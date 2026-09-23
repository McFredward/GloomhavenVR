#!/usr/bin/env python3
"""Reconstruct a watertight head surface from reviewed oriented source vertices.

Offline PyMeshLab step, used only for the merchant's open generated beard shells.
The NPZ input contains Blender-space vertex positions `v` and normals `n` exported
from the normalized, otherwise unchanged source. Raw provider GLBs remain untouched.
No texture is generated or edited by this geometry-only process.
"""
import argparse
import hashlib
import json
from pathlib import Path
import numpy as np
import pymeshlab

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--points', type=Path, required=True)
parser.add_argument('--output', type=Path, required=True)
args = parser.parse_args()
if args.output.exists():
    raise FileExistsError('Use a fresh derivative output')
points = np.load(args.points)
meshset = pymeshlab.MeshSet()
meshset.add_mesh(pymeshlab.Mesh(vertex_matrix=points['v'], v_normals_matrix=points['n']))
parameters = dict(depth=10, pointweight=4, samplespernode=1.5, threads=4, preclean=True)
meshset.generate_surface_reconstruction_screened_poisson(**parameters)
meshset.save_current_mesh(str(args.output))
args.output.with_suffix('.json').write_text(json.dumps({
    'inputSha256': hashlib.sha256(args.points.read_bytes()).hexdigest(),
    'outputSha256': hashlib.sha256(args.output.read_bytes()).hexdigest(),
    'algorithm': 'Screened Poisson', 'parameters': parameters,
    'vertices': meshset.current_mesh().vertex_number(),
    'triangles': meshset.current_mesh().face_number(),
    'limitation': 'Neutral surface, not facial animation topology.'
}, indent=2) + '\n')
