#!/usr/bin/env python3
"""Blender authoring checks against measured reference landmarks, not headset proof."""
import importlib.util
import os
import subprocess
import sys
from pathlib import Path

# The authoring module imports bpy. Run the same measured geometry assertions in
# Blender when launched by the ordinary local test runner, instead of faking bpy.
if importlib.util.find_spec("bpy") is None:
    blender = os.environ.get("BLENDER_PATH", "/home/claw/blender-4.2/blender")
    raise SystemExit(subprocess.run([blender, "--background", "--python", str(Path(__file__).resolve())]).returncode)

import numpy as np


def main():
    path = Path(__file__).with_name('author-town-facial-topology.py')
    spec = importlib.util.spec_from_file_location('facial_authoring', path)
    source = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(source)
    # HM08's alar recess, independently identified on its anatomical topology.
    # Targets are pixel positions measured on the 718 px supplied reference.
    recess = np.array([[.139, 6.864, 1.55], [-.139, 6.864, 1.55]])
    targets = {'merchant': ((415, 349), (319, 349)),
               'priestess': ((380, 392), (335, 392)),
               'enchantress': ((394, 416), (355, 416))}
    assertions = 0
    def registered(actual, expected):
        return np.all(np.linalg.norm(actual - np.array(expected), axis=1) < 6)
    for name, expected in targets.items():
        actual = source.facial_texture_coordinates(recess, name)
        assert registered(actual, expected), (name, actual, expected)
        assertions += 1
        # Real expression loops retain finite geometry and consistent vertex
        # identity; UV registration must never move the anatomical surface.
        for orbital in (False, True):
            assert np.all(np.isfinite(source.fit(recess, name, orbital)))
            assertions += 1
        for corruption in (np.array([0, 14]), np.array([14, 0])):
            assert not registered(actual + corruption, expected)
            assertions += 1
    # The prior flat projection is a genuine negative control: its priestess
    # nostril image is 18 px above the cavity and its enchantress pair too wide.
    for name in ('priestess', 'enchantress'):
        assert not registered(source.portrait_coordinates(recess, source.PROFILES[name]), targets[name])
        assertions += 1
    # The original portrait has separated eyes above a compact full beard. These
    # checks catch a return to the accumulated narrow-eye/long-goatee fitting.
    eyes = source.fit(np.array([[.30775, 7.28415, 1.24535], [-.30775, 7.28415, 1.24535]]), 'merchant', orbital=False)
    assert .070 < abs(eyes[0, 0] - eyes[1, 0]) < .075
    assertions += 1
    axial = source.fit(np.array([[0, 8.4913, 0], [0, 6.16, .9], [0, 6.615, 1.5]]), 'merchant', orbital=False)
    assert .27 < axial[0, 2] - axial[1, 2] < .29
    assert .35 < (axial[2, 2] - axial[1, 2]) / (axial[0, 2] - axial[1, 2]) < .42
    assertions += 2
    # Profile helix and lobe must land on the same photographed ear. A global
    # depth projection put its dark concha on intact scalp behind the true ear.
    ear = np.array([[.86, 7.40, .35], [.78, 7.02, .63]])
    expected = np.array([[199.4, 227], [280.2, 395]])
    actual = source.side_texture_coordinates(ear, 'merchant')
    assert registered(actual, expected)
    assertions += 1
    old = np.column_stack((67 + (ear[:, 2] + .391) / 2.0717 * 543,
                           source.portrait_coordinates(ear, source.PROFILES['merchant'])[:, 1]))
    assert not registered(old, expected)
    assertions += 1
    print(f'PASS: {assertions} facial landmark assertions; 9 registration negative controls')


if __name__ == '__main__':
    main()
