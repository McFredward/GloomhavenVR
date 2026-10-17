#!/usr/bin/env python3
"""Physical image witnesses for the actual offline intro reprojection.

Run with the isolated intro-conversion Python environment. No model or movie is
needed. Mutations demonstrate that duplicated eyes, reversed depth and a global
picture shift do not satisfy the spatial witness.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import sys

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts/stereo-video"))
from intro_stereo import forward_warp, validate_shots


def scene() -> tuple[np.ndarray, np.ndarray]:
    image = np.zeros((48, 96, 3), np.uint8)
    image[:, :, 0] = 180  # Far blue background.
    image[:, :, 1] = np.arange(48, dtype=np.uint8)[:, None]  # Vertical address witness.
    image[12:36, 34:54, 0] = 0
    image[12:36, 34:54, 2] = 240  # Near red rectangle.
    image[4:8, 70:74, 1] = 220  # Stationary background marker.
    field = np.zeros((48, 96), np.float32)
    field[12:36, 34:54] = 6
    return image, field


def spatial_witness(right: np.ndarray) -> None:
    # x_left - x_right = +6 for the foreground, zero for the background.
    red = np.where(right[20, :, 2] > 200)[0]
    assert red.tolist() == list(range(28, 48)), "foreground eye order/disparity"
    assert np.all(right[4:8, 70:74, 1] == 220), "background must not follow foreground shift"
    assert np.all(right[12:36, 48:54, 0] == 180), "disocclusion uses background, not foreground"
    assert np.all(right[20, 28:34, 2] == 240), "nearer foreground wins occlusion"
    assert np.all(right[12:36, :, 1] == np.arange(12, 36, dtype=np.uint8)[:, None]), "vertical address"


def check() -> dict:
    image, field = scene()
    right, holes = forward_warp(image, field)
    spatial_witness(right)
    assert holes == 6 * 24
    assert np.array_equal(image, scene()[0]), "source/left eye was modified"
    identity, identity_holes = forward_warp(image, np.zeros_like(field))
    assert np.array_equal(identity, image) and identity_holes == 0
    rng = np.random.default_rng(412)
    random = rng.integers(0, 256, image.shape, dtype=np.uint8)
    identity, _ = forward_warp(random, np.zeros_like(field))
    assert np.array_equal(identity, random), "zero disparity must preserve every channel"
    # A separated far object has negative disparity and therefore moves right.
    far = np.zeros_like(image)
    far[20:25, 40:45, 2] = 250
    shifted, _ = forward_warp(far, np.full_like(field, -4))
    assert np.where(shifted[22, :, 2] > 200)[0].tolist() == list(range(44, 49))
    controls = []
    reversed_eye, _ = forward_warp(image, -field)
    global_shift, _ = forward_warp(image, np.full_like(field, 6))
    for name, mutant in (("duplicate-right", image), ("reversed-eye-depth", reversed_eye),
                         ("whole-picture-shift", global_shift), ("vertical-flip", right[::-1])):
        try:
            spatial_witness(mutant)
        except AssertionError:
            controls.append(name)
        else:
            raise AssertionError(f"Control survived: {name}")
    manifest = json.loads((ROOT / "scripts/stereo-video/intro-shots.json").read_text())
    validate_shots(manifest)
    for name, change in (("gap", lambda m: m["shots"][2].update(start=421)),
                         ("lost-ending", lambda m: m["shots"][-1].update(end=2930)),
                         ("wrong-source", lambda m: m.update(source_sha256="0" * 64))):
        mutant = json.loads(json.dumps(manifest))
        change(mutant)
        try:
            validate_shots(mutant)
        except ValueError:
            controls.append(name)
        else:
            raise AssertionError(f"Control survived: {name}")
    return {"status": "passed", "physical_witness_checks": 10, "negative_controls": controls,
            "processes_reaped": True, "scope": "actual offline geometry; no inferred-depth or headset quality claim"}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=False)
    code = 1
    try:
        result = check()
        code = 0
        (args.out / "result.json").write_text(json.dumps(result, indent=2) + "\n")
        print(json.dumps(result))
    finally:
        sys.path.insert(0, str(ROOT / "scripts"))
        from town_run_retention import cleanup_completed_run
        log = args.out / "completion.log"
        log.write_text(f"No subprocesses started.\nevidence: {args.out.resolve()}\n")
        receipt = cleanup_completed_run("stereo-video-intro", code, log.resolve(), ROOT, process_reaped=True)
        (args.out / "cleanup.json").write_text(json.dumps(receipt, indent=2) + "\n")
    return code


if __name__ == "__main__":
    raise SystemExit(main())
