#!/usr/bin/env python3
"""Generate and measure a 9s full-SBS H.264 calibration clip plus a mono companion."""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import shutil
import signal
import sys

sys.dont_write_bytecode = True
import PIL
import calibration as c


def interrupted(_signum, _frame):
    raise KeyboardInterrupt


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out-dir", type=Path, required=True, help="New output directory; never overwritten")
    parser.add_argument("--retention-checkout", type=Path, default=c.ROOT)
    args = parser.parse_args()
    directory = args.out_dir.absolute()
    directory.mkdir(parents=True, exist_ok=False)
    signal.signal(signal.SIGTERM, interrupted)
    code, result = 1, {"status": "failed"}
    try:
        ffmpeg, ffprobe = shutil.which("ffmpeg"), shutil.which("ffprobe")
        if not ffmpeg or not ffprobe:
            raise ValueError("ffmpeg, ffprobe and Pillow are required")
        tools = {"pillow": PIL.__version__, "ffmpeg": c.run([ffmpeg, "-version"], directory / "ffmpeg-version.log").decode().splitlines()[0],
                 "ffprobe": c.run([ffprobe, "-version"], directory / "ffprobe-version.log").decode().splitlines()[0]}
        artifacts = {}
        for mode in ("sbs", "mono"):
            target = directory / f"calibration-{mode}.mp4"
            command = c.encode(mode, target, ffmpeg, directory)
            observation = c.observe(target, mode, directory, ffmpeg, ffprobe)
            proof = c.validate(observation, mode)
            proof["seeks"] = c.seek_checks(target, mode, directory, ffmpeg)
            c.save(directory / f"probe-{mode}.json", observation["probe"])
            artifacts[mode] = {"file": target.name, "sha256": c.digest(target), "bytes": target.stat().st_size,
                               "width": c.WIDTH * (2 if mode == "sbs" else 1), "height": c.HEIGHT,
                               "proof": proof, "encode_command": command}
        result = {"schema_version": 1, "profile": "ghvr-stereo-calibration-v1", "status": "measured-awaiting-headset-review",
                  "eye_order": "left-right", "packing": "full-side-by-side", "per_eye_size": [c.WIDTH, c.HEIGHT],
                  "frame_rate": c.FPS, "frame_count": c.FRAMES, "duration_seconds": c.FRAMES / c.FPS,
                  "cut_frames": list(c.CUTS), "audio": "none", "tools": tools,
                  "source_sha256": {name: c.digest(Path(__file__).with_name(name)) for name in ("build.py", "calibration.py")},
                  "disparity_convention": "x_left minus x_right; near +12px, screen 0px, far -12px",
                  "depth_origin": "Explicit code-drawn geometry; no inferred or generated scene depth",
                  "comfort_limit": "12px is 0.625% of eye width, not a universal angular or comfort bound; physical screen width, viewing distance and IPD still matter.",
                  "artifacts": artifacts}
        c.save(directory / "calibration-manifest.json", result)
        code = 0
    except (Exception, KeyboardInterrupt) as error:
        code = 130 if isinstance(error, KeyboardInterrupt) else 1
        result = {"status": "failed", "error": str(error), "processes_reaped": True}
    finally:
        c.save(directory / "result.json", result)
        c.completion(directory, code, args.retention_checkout.absolute())
    print(json.dumps({"status": result["status"], "evidence": str(directory), "exit_code": code}))
    return code


if __name__ == "__main__":
    raise SystemExit(main())
