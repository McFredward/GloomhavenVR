#!/usr/bin/env python3
"""Blender authoring checks against measured reference landmarks, not headset proof."""
import importlib.util
from pathlib import Path

import numpy as np


def main():
    path = Path(__file__).with_name('author-town-facial-topology.py')
    spec = importlib.util.spec_from_file_location('facial_authoring', path)
    source = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(source)
    # HM08's alar recess, independently identified on its anatomical topology.
    # Targets are pixel positions measured on the 718 px supplied reference.
    recess = np.array([[.139, 6.864, 1.55], [-.139, 6.864, 1.55]])
    targets = {'merchant': ((393, 360), (331, 360)),
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
    # Eye spacing must match the same narrower merchant geometry as the orbit.
    eyes = source.fit(np.array([[.30775, 7.28415, 1.24535], [-.30775, 7.28415, 1.24535]]), 'merchant', orbital=False)
    assert .058 < abs(eyes[0, 0] - eyes[1, 0]) < .064
    assertions += 1
    print(f'PASS: {assertions} facial landmark assertions; 8 registration negative controls')


if __name__ == '__main__':
    main()
