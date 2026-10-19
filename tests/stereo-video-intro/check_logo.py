#!/usr/bin/env python3
"""Independent foreground-plane and unchanged-artwork witnesses for the VR logo."""
from pathlib import Path
import argparse
import hashlib
import json
import sys

import cv2
import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts/stereo-video"))
from intro_logo_depth import IntroLogoDepth
from intro_stereo import forward_warp


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=False)
    cv2.setNumThreads(1)
    checks = []

    def require(condition, name):
        if not condition:
            raise AssertionError(name)
        checks.append(name)

    provider = IntroLogoDepth(ROOT / "scripts/stereo-video/intro-logo-depth.json",
                             ROOT / "src/GloomhavenVR/Assets/GloomhavenVR_logo.png")
    art = Image.open(ROOT / "src/GloomhavenVR/Assets/GloomhavenVR_logo.png").convert("RGBA")
    scale = 1730 / art.width
    art = art.resize((1730, round(art.height * scale)), Image.Resampling.LANCZOS)
    canvas = Image.new("RGB", (1920, 1080))
    canvas.paste(art, (95, round(451 - 64.5 * scale)), art)
    left = np.asarray(canvas)[:, :, ::-1].copy()
    before = hashlib.sha256(left.tobytes()).hexdigest()
    field = provider.field("opening-title", 90)
    right, _ = forward_warp(left, field)
    # This oracle is an integer translation of the original bitmap, independent
    # of the renderer's depth winner, matte, interpolation and hole-fill code.
    expected = np.zeros_like(left)
    expected[:, :-12] = left[:, 12:]
    require(np.array_equal(right, expected), "original printed artwork translates exactly12px left for right eye")
    require(hashlib.sha256(left.tobytes()).hexdigest() == before, "approved left pixels are never mutated")
    require(np.isfinite(field).all() and abs(float(field.max()) - 12) < .0001,
            "finite12px foreground bound")
    require(np.all(field[:124] == 0) and np.all(field[962:] == 0), "letterboxes remain screen plane")
    require(np.all(field[:, :64] == 0) and np.all(field[:, -64:] == 0), "outer cinema border remains screen plane")
    require(all(np.array_equal(field, provider.field("opening-title", frame)) for frame in [0, 179]),
            "stationary opening logo depth never pumps")
    require(provider.field("native-black-tail", 2900) is None, "native ending unchanged")
    require(provider.field("forest", 900) is None, "approved scenic depth untouched")
    require(float(provider.field("fire-title", 438).max()) == 0, "fire before text reveal is unchanged")
    levels = [float(provider.field("fire-title", frame).max()) for frame in [439, 442, 445, 448, 452]]
    require(all(a < b for a, b in zip(levels, levels[1:])) and abs(levels[-1] - 12) < .0001,
            "original fire reveal introduces foreground depth smoothly")
    controls = []
    for name, alternative in [("duplicated-eye", left),
                              ("behind-screen", np.pad(left[:, :-12], ((0, 0), (12, 0), (0, 0)))),
                              ("excess-depth", np.pad(left[:, 24:], ((0, 0), (0, 24), (0, 0))))]:
        require(not np.array_equal(alternative, expected), "reject " + name)
        controls.append(name)
    report = {"status": "passed", "checks": checks, "negative_controls": controls,
              "method": "Exact original-logo integer-translation oracle plus source/clock/border witnesses",
              "hardware_result": "unverified"}
    (args.out / "result.json").write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report))


if __name__ == "__main__":
    code = 1
    try:
        main()
        code = 0
    finally:
        # No child process is created by this pure geometry fixture.
        out = Path(sys.argv[sys.argv.index("--out") + 1]).resolve()
        out.mkdir(parents=True, exist_ok=True)
        sys.path.insert(0, str(ROOT / "scripts"))
        from town_run_retention import cleanup_completed_run
        log = out / "completion.log"
        log.write_text(f"Evidence: {out}\nGeometry fixture completed; exit={code}; no child processes.\n")
        (out / "cleanup.json").write_text(json.dumps(cleanup_completed_run(
            "stereo-video-intro-logo", code, log, ROOT, process_reaped=True), indent=2) + "\n")
