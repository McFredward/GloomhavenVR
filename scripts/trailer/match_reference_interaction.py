#!/usr/bin/env python3
"""Normalize an edited I01 shot and join it to retained original handles.

Offline only: no inference, credentials, soundtrack or runtime changes. The
interior remains generated, including intentional VR contact and actor motion.
Original handles and motion-aligned boundary morphs restore the source clock.
Pixel-exact endpoints apply to the FFV1 replacement, before a lossy final encode.
Numerical receipts do not establish natural anatomy or an invisible transition.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import signal
import subprocess
import sys

import cv2
import numpy as np

WIDTH, HEIGHT, FPS, COUNT = 1920, 1080, 60, 345
TOP, BOTTOM = 134, 946


def write_json(path, value):
    with path.open("x", encoding="utf-8") as stream:
        json.dump(value, stream, indent=2, allow_nan=False)
        stream.write("\n")


def sha256(path):
    result = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            result.update(block)
    return result.hexdigest()


def reap(process):
    if process.poll() is None:
        try:
            os.killpg(process.pid, signal.SIGTERM)
        except ProcessLookupError:
            pass
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
    process.wait()


def run(command, log):
    with log.open("x", encoding="utf-8") as stream:
        stream.write(json.dumps(command) + "\n")
        stream.flush()
        process = subprocess.Popen(command, stdout=stream, stderr=subprocess.STDOUT,
                                   start_new_session=True)
        try:
            code = process.wait()
        finally:
            reap(process)
    if code:
        raise ValueError(f"Command failed ({code}); see {log}")


def verify_media(path):
    command = ["ffprobe", "-v", "error", "-count_frames", "-show_entries",
               "stream=codec_type,width,height,r_frame_rate,nb_read_frames:format=duration",
               "-of", "json", str(path)]
    process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                               text=True, start_new_session=True)
    try:
        stdout, stderr = process.communicate(timeout=120)
    finally:
        reap(process)
    if process.returncode:
        raise ValueError(f"ffprobe failed: {stderr[-2000:]}")
    metadata = json.loads(stdout)
    streams = metadata.get("streams", [])
    if len(streams) != 1 or streams[0].get("codec_type") != "video":
        raise ValueError("Replacement/review must have exactly one silent video stream")
    stream = streams[0]
    if (stream["width"], stream["height"], stream["r_frame_rate"],
            int(stream["nb_read_frames"])) != (WIDTH, HEIGHT, "60/1", COUNT):
        raise ValueError(f"Decoded dimensions/fps/count are wrong: {stream}")
    if abs(float(metadata["format"]["duration"]) - COUNT / FPS) > .002:
        raise ValueError("Replacement/review duration differs from the original window")
    return metadata


def smooth(value):
    value = min(1., max(0., float(value)))
    return value * value * (3. - 2. * value)


def measure_motion(a, b):
    """One bounded bidirectional flow pair, reusable during frame interpolation."""
    small = (640, round(a.shape[0] * 640 / a.shape[1]))
    ga = cv2.cvtColor(cv2.resize(a, small), cv2.COLOR_BGR2GRAY)
    gb = cv2.cvtColor(cv2.resize(b, small), cv2.COLOR_BGR2GRAY)
    solver = cv2.DISOpticalFlow_create(cv2.DISOPTICAL_FLOW_PRESET_MEDIUM)
    forward = solver.calc(ga, gb, None)
    backward = solver.calc(gb, ga, None)
    forward = cv2.resize(forward, (a.shape[1], a.shape[0]))
    backward = cv2.resize(backward, (a.shape[1], a.shape[0]))
    scale = np.array([a.shape[1] / small[0], a.shape[0] / small[1]], np.float32)
    forward *= scale
    backward *= scale
    return forward, backward


def aligned_morph(a, b, weight, motion=None):
    """Move both images toward one pose before mixing their colors.

    Source/generated boundaries are intentionally bounded to handles where the
    player has not entered or has already withdrawn. Flow cannot infer a proper
    physical exit for a hand that the model leaves in the final composition.
    """
    if weight <= 0:
        return a.copy(), {}
    if weight >= 1:
        return b.copy(), {}
    forward, backward = motion if motion is not None else measure_motion(a, b)
    yy, xx = np.mgrid[:a.shape[0], :a.shape[1]].astype(np.float32)
    wa = cv2.remap(a, xx - weight * forward[..., 0], yy - weight * forward[..., 1],
                   cv2.INTER_LINEAR, borderMode=cv2.BORDER_REFLECT_101)
    wb = cv2.remap(b, xx - (1 - weight) * backward[..., 0],
                   yy - (1 - weight) * backward[..., 1],
                   cv2.INTER_LINEAR, borderMode=cv2.BORDER_REFLECT_101)
    joined = np.rint(wa.astype(np.float32) * (1 - weight) +
                     wb.astype(np.float32) * weight).astype(np.uint8)
    delta = np.abs(wa.astype(np.float32) - wb.astype(np.float32))
    return joined, {"aligned_rgb_residual_mean": float(delta.mean()),
                    "aligned_rgb_residual_p95": float(np.percentile(delta, 95))}


class Encoder:
    def __init__(self, path):
        self.log = path.with_suffix(".encode.log").open("x", encoding="utf-8")
        command = ["ffmpeg", "-hide_banner", "-nostdin", "-n", "-v", "error",
                   "-threads", "2", "-f", "rawvideo", "-pix_fmt", "bgr24",
                   "-s", f"{WIDTH}x{HEIGHT}", "-r", str(FPS), "-i", "pipe:0",
                   "-an", "-c:v", "ffv1", "-level", "3", "-pix_fmt", "bgr0",
                   "-threads", "2", str(path)]
        self.log.write(json.dumps(command) + "\n")
        self.log.flush()
        self.process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=self.log,
                                        stderr=subprocess.STDOUT, start_new_session=True)

    def write(self, frame):
        self.process.stdin.write(frame.tobytes())

    def finish(self):
        self.process.stdin.close()
        code = self.process.wait()
        self.log.close()
        if code:
            raise ValueError(f"FFV1 encoder failed: {code}")

    def abort(self):
        reap(self.process)
        self.log.close()


def prepare_output(path):
    if path.exists():
        raise ValueError(f"Refusing to overwrite existing evidence directory: {path}")
    path.mkdir(parents=True)


def main():
    cv2.setNumThreads(2)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True, help="Native original 1080p60 FFV1")
    parser.add_argument("--generated", type=Path, required=True)
    parser.add_argument("--first", type=Path, required=True)
    parser.add_argument("--last", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--entry-handle", type=int, default=24)
    parser.add_argument("--entry-join", type=int, default=30)
    parser.add_argument("--exit-handle", type=int, default=12)
    parser.add_argument("--exit-join", type=int, default=24)
    parser.add_argument("--rejection-reason", action="append", default=[],
                        help="Retain a diagnosed model failure; repeat for multiple reasons")
    args = parser.parse_args()
    for name in ("source", "generated", "first", "last"):
        path = getattr(args, name).resolve()
        if not path.is_file():
            raise ValueError(f"Missing {name}: {path}")
        setattr(args, name, path)
    lengths = [args.entry_handle, args.entry_join, args.exit_handle, args.exit_join]
    if any(v < 0 for v in lengths) or args.entry_handle < 1 or args.exit_handle < 1:
        raise ValueError("Handles must retain at least one frame; join lengths must be nonnegative")
    if sum(lengths) >= COUNT:
        raise ValueError("Handles/joins leave no generated interior")
    args.output = args.output.resolve()
    prepare_output(args.output)
    output = args.output / "matched-interaction.mkv"
    capture = None
    model = None
    encoder = None
    code = 1
    try:
        write_json(args.output / "inputs.json", {name: {"path": str(getattr(args, name)),
                    "sha256": sha256(getattr(args, name))} for name in ("source", "generated", "first", "last")})
        capture = cv2.VideoCapture(str(args.source))
        model = cv2.VideoCapture(str(args.generated))
        raw = []
        while True:
            ok, image = model.read()
            if not ok:
                break
            raw.append(image)
        model.release()
        model = None
        if len(raw) < 2:
            raise ValueError("Generated input has fewer than two frames")
        first, last = [cv2.imread(str(p)) for p in (args.first, args.last)]
        if any(im is None or im.shape != (HEIGHT, WIDTH, 3) for im in (first, last)):
            raise ValueError("Native endpoints must be 1920x1080 RGB images")
        encoder = Encoder(output)
        notes = []
        normalized = None
        cached_pair = None
        cached_motion = None
        for index in range(COUNT):
            ok, source = capture.read()
            if not ok or source.shape != (HEIGHT, WIDTH, 3):
                raise ValueError(f"Native source ended or changed dimensions at frame {index}")
            if index == 0:
                source = first.copy()
            elif index == COUNT - 1:
                source = last.copy()
            phase = index * (len(raw) - 1) / (COUNT - 1)
            lower = int(phase)
            upper = min(len(raw) - 1, lower + 1)
            fraction = phase - lower
            pair = (lower, upper)
            weight = 1.
            if index < args.entry_handle:
                weight = 0.
            elif index < args.entry_handle + args.entry_join:
                weight = smooth((index - args.entry_handle + 1) / (args.entry_join + 1))
            if index >= COUNT - args.exit_handle:
                weight = 0.
            elif index >= COUNT - args.exit_handle - args.exit_join:
                weight = smooth((COUNT - args.exit_handle - index) / (args.exit_join + 1))
            interpolation, join = {}, {}
            if weight == 0:
                result = source.copy()
            else:
                if pair != cached_pair:
                    normalized = [cv2.resize(raw[n], (WIDTH, HEIGHT), interpolation=cv2.INTER_LANCZOS4)
                                  for n in pair]
                    cached_pair = pair
                    cached_motion = measure_motion(*normalized) if lower != upper else None
                generated, interpolation = aligned_morph(*normalized, fraction, motion=cached_motion)
                result, join = aligned_morph(source, generated, weight)
            # Native black bands are source-owned even when the interior changes.
            result[:TOP] = source[:TOP]
            result[BOTTOM:] = source[BOTTOM:]
            encoder.write(result)
            notes.append({"frame": index, "source_seconds": index / FPS,
                          "model_phase": phase, "generated_weight": weight,
                          "interpolation": interpolation, "join": join})
            if index % 15 == 0 or index in (1, 2, 3, 340, 341, 342, 343, 344):
                cv2.imwrite(str(args.output / f"witness-{index:03d}.png"), result)
            if index % 30 == 0:
                print(f"Matching {index}/{COUNT}: generated weight {weight:.3f}", flush=True)
        if capture.read()[0]:
            raise ValueError("Native source exceeds the exact 345-frame window")
        capture.release()
        capture = None
        encoder.finish()
        encoder = None
        run(["ffmpeg", "-hide_banner", "-nostdin", "-n", "-v", "error", "-threads", "2",
             "-i", str(output), "-an", "-c:v", "libx264", "-preset", "fast", "-crf", "16",
             "-pix_fmt", "yuv420p", "-threads", "2", "-movflags", "+faststart",
             str(args.output / "interaction-review.mp4")], args.output / "review-encode.log")
        decoded = cv2.VideoCapture(str(output))
        count = 0
        exact = []
        try:
            while True:
                ok, image = decoded.read()
                if not ok:
                    break
                if count in (0, COUNT - 1):
                    expected = first if count == 0 else last
                    exact.append({"frame": count, "changed_channels": int(np.count_nonzero(image != expected))})
                count += 1
        finally:
            decoded.release()
        if count != COUNT or any(row["changed_channels"] for row in exact) or len(exact) != 2:
            raise ValueError(f"Decoded frame count/endpoints failed: {count}, {exact}")
        media_qc = {}
        for path in (output, args.output / "interaction-review.mp4"):
            run(["ffmpeg", "-hide_banner", "-nostdin", "-v", "error", "-xerror", "-threads", "2",
                 "-i", str(path), "-map", "0:v:0", "-an", "-f", "null", "-"],
                args.output / f"{path.stem}-decode.log")
            media_qc[path.name] = verify_media(path)
        write_json(args.output / "receipt.json", {"frames": count, "fps": FPS,
                   "duration_seconds": count / FPS, "dimensions": [WIDTH, HEIGHT],
                   "lossless_endpoints": exact, "paid_calls": 0,
                   "output_sha256": sha256(output), "handles_and_joins": lengths,
                   "helper_sha256": sha256(Path(__file__)),
                   "generated_frames": len(raw), "frame_notes": notes,
                   "decoded_media": media_qc,
                   "approved_for_master": False, "failure_reasons": args.rejection_reason,
                   "visual_approval": "Not established by endpoint or decoding checks; inspect contact and dense joins.",
                   "assembly_clip": {"id": "ironwatch-interaction-v3", "path": str(output),
                                     "start_seconds": 139.95, "end_seconds": 145.7, "timing": "trim"}})
        code = 0
    finally:
        if capture is not None:
            capture.release()
        if model is not None:
            model.release()
        if encoder is not None:
            encoder.abort()
        sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
        from town_run_retention import cleanup_completed_run
        completion = args.output / "completion.log"
        completion.write_text("Owned encoders and captures closed and processes reaped.\n")
        write_json(args.output / "cleanup.json", cleanup_completed_run(
                   "trailer-interaction-match", code, completion,
                   Path(__file__).resolve().parents[2], process_reaped=True))


if __name__ == "__main__":
    main()
