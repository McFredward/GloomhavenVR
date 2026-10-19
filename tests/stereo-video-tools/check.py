#!/usr/bin/env python3
"""Check delivered calibration pixels, seeks, hashes and causal negative controls.

The decoded witness controls retain correct dimensions/frame counts unless that
specific property is the deliberate fault. An additional real MP4 remux changes
only the clock. Generated controls are deleted after all children are reaped.
"""
from __future__ import annotations

import argparse
import copy
import json
from pathlib import Path
import shutil
import signal
import sys

sys.dont_write_bytecode = True
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts/stereo-video"))
import calibration as c


def interrupted(_signum, _frame):
    raise KeyboardInterrupt


def check(media: Path, directory: Path) -> dict:
    manifest = json.loads((media / "calibration-manifest.json").read_text())
    if (manifest["frame_count"], manifest["frame_rate"], manifest["per_eye_size"], manifest["eye_order"]) != (
            540, 60, [1920, 1080], "left-right"):
        raise ValueError("Delivery manifest contract differs")
    ffmpeg, ffprobe = shutil.which("ffmpeg"), shutil.which("ffprobe")
    if not ffmpeg or not ffprobe:
        raise ValueError("ffmpeg and ffprobe are required")
    observations, positives = {}, {}
    for mode in ("sbs", "mono"):
        item = manifest["artifacts"][mode]
        path = media / f"calibration-{mode}.mp4"
        if item["file"] != path.name or c.digest(path) != item["sha256"] or path.stat().st_size != item["bytes"]:
            raise ValueError(f"{mode} asset bytes differ from manifest")
        observation = c.observe(path, mode, directory, ffmpeg, ffprobe)
        positives[mode] = c.validate(observation, mode)
        positives[mode]["seeks"] = c.seek_checks(path, mode, directory, ffmpeg)
        observations[mode] = observation

    controls = []

    def reject(name, expected, mutate):
        observation = copy.deepcopy(observations["sbs"])
        mutate(observation)
        try:
            c.validate(observation, "sbs")
        except ValueError as error:
            if not str(error).startswith(expected + ":"):
                raise ValueError(f"{name} failed for unrelated reason: {error}") from error
            controls.append({"name": name, "detected_by": expected, "error": str(error)})
        else:
            raise ValueError(f"Negative control was accepted: {name}")

    def remap_eyes(observation, mapping):
        # Mutate the actual decoded rows, not expected values or copied labels.
        width = c.WIDTH * 2
        for name in ("header", "depth"):
            data = observation[name]
            observation[name] = b"".join(
                data[frame * width + eye * c.WIDTH:frame * width + (eye + 1) * c.WIDTH]
                for frame in range(c.FRAMES) for eye in mapping)

    reject("swapped-eyes", "eye-clock", lambda o: remap_eyes(o, (1, 0)))
    reject("duplicated-left-eye", "eye-clock", lambda o: remap_eyes(o, (0, 0)))
    reject("duplicated-right-eye", "eye-clock", lambda o: remap_eyes(o, (1, 1)))

    def stale_right(o):
        data = bytearray(o["header"])
        # One eye is exactly one old frame at the first cut; all other pixels/PTS survive.
        first = 180 * c.WIDTH * 2 + c.WIDTH
        data[first:first + c.WIDTH] = o["header"][first - c.WIDTH * 2:first - c.WIDTH]
        o["header"] = bytes(data)

    reject("right-eye-old-frame-at-cut", "eye-clock", stale_right)
    reject("30fps-metadata", "clock", lambda o: o["probe"]["streams"][0].update(avg_frame_rate="30/1"))
    reject("half-sbs-width", "size", lambda o: o["probe"]["streams"][0].update(width=1920))
    reject("non-square-pixels", "aspect", lambda o: o["probe"]["streams"][0].update(sample_aspect_ratio="2:1"))
    reject("missing-cut-keyframe", "seek", lambda o: o["probe"]["frames"][180].update(key_frame=0))
    reject("one-wrong-pts-at-cut", "clock", lambda o: o["probe"]["frames"][360].update(best_effort_timestamp_time="6.02"))
    reject("missing-decoded-frame", "clock", lambda o: o.update(header=o["header"][:-c.WIDTH * 2]))

    def reverse_depth(o):
        # Keep correct per-eye header; copy only right near-marker pixels onto the
        # left eye, so labels and dimensions cannot hide an inverted disparity.
        data = bytearray(o["depth"])
        start = 20 * c.WIDTH * 2
        data[start + 340:start + 620] = o["depth"][start + c.WIDTH + 340:start + c.WIDTH + 620]
        o["depth"] = bytes(data)

    reject("wrong-near-disparity-with-correct-labels", "depth", reverse_depth)

    control_path = directory / "clock-control.mp4"
    c.run([ffmpeg, "-v", "error", "-nostdin", "-n", "-itsscale", "2", "-i",
           str(media / "calibration-sbs.mp4"), "-map", "0:v:0", "-c", "copy", str(control_path)],
          directory / "clock-control-remux.log")
    bad_clock = c.observe(control_path, "clock-control", directory, ffmpeg, ffprobe)
    try:
        c.validate(bad_clock, "sbs")
    except ValueError as error:
        if not str(error).startswith("clock:"):
            raise ValueError(f"Real clock control failed for unrelated reason: {error}") from error
        controls.append({"name": "actual-mp4-2x-timestamp-remux", "detected_by": "clock", "error": str(error),
                         "sha256": c.digest(control_path)})
    else:
        raise ValueError("Actual bad-clock MP4 was accepted")
    return {"status": "passed", "positive_deliveries": positives, "causal_negative_controls": controls,
            "negative_control_count": len(controls), "source_manifest_sha256": c.digest(media / "calibration-manifest.json"),
            "scope": "Offline encoded media, decoded pixel witnesses and actual seeks; no Unity/XR or headset proof"}


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--media-dir", type=Path, required=True)
    parser.add_argument("--out-dir", type=Path, required=True, help="Fresh private evidence directory")
    parser.add_argument("--retention-checkout", type=Path, default=ROOT)
    args = parser.parse_args()
    directory = args.out_dir.absolute()
    directory.mkdir(parents=True, exist_ok=False)
    signal.signal(signal.SIGTERM, interrupted)
    code, result = 1, {"status": "failed"}
    try:
        result = check(args.media_dir.absolute(), directory)
        code = 0
    except (Exception, KeyboardInterrupt) as error:
        code = 130 if isinstance(error, KeyboardInterrupt) else 1
        result = {"status": "failed", "error": str(error), "processes_reaped": True}
    finally:
        control = directory / "clock-control.mp4"
        if control.is_file() and not control.is_symlink():
            control.unlink()
        c.save(directory / "result.json", result)
        c.completion(directory, code, args.retention_checkout.absolute())
    print(json.dumps({"status": result["status"], "controls": result.get("negative_control_count", 0),
                      "evidence": str(directory), "exit_code": code}))
    return code


if __name__ == "__main__":
    raise SystemExit(main())
