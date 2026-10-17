#!/usr/bin/env python3
"""Reproject the approved intro into left-first full SBS without changing its clock.

The decoded original is the left view. Only the right view is synthesized from
temporally estimated inverse depth. A foreground depth test resolves occlusions;
only uncovered samples use the farther adjoining background. No generative
picture, text, actor, audio or continuation changes are made here.
"""
from __future__ import annotations

import argparse
import json
import math
import os
from pathlib import Path
import signal
import subprocess
import sys
import time

import cv2
from numba import njit
import numpy as np

from intro_depth import CODE_REVISION, INTRO_SHA256, MODEL_REVISION, WEIGHTS_SHA256, digest, save

WIDTH, HEIGHT, FPS, FRAMES = 1920, 1080, 60, 2931


@njit(cache=False)
def forward_warp(image: np.ndarray, disparity: np.ndarray) -> tuple[np.ndarray, int]:
    """x_right=x_left-disparity; nearer samples win; vertical address never changes."""
    height, width = disparity.shape
    output = np.empty_like(image)
    holes = 0
    for y in range(height):
        front = np.full(width, -np.inf, dtype=np.float32)
        weights = np.zeros(width, dtype=np.float32)
        color = np.zeros((width, 3), dtype=np.float32)
        for x in range(width):
            target = x - disparity[y, x]
            first = math.floor(target)
            fraction = target - first
            for k in range(2):
                dest = first + k
                weight = 1.0 - fraction if k == 0 else fraction
                if 0 <= dest < width and weight > 0.00001:
                    front[dest] = max(front[dest], disparity[y, x])
        for x in range(width):
            target = x - disparity[y, x]
            first = math.floor(target)
            fraction = target - first
            for k in range(2):
                dest = first + k
                weight = 1.0 - fraction if k == 0 else fraction
                if (0 <= dest < width and weight > 0.00001
                        and disparity[y, x] >= front[dest] - 0.15):
                    weights[dest] += weight
                    for channel in range(3):
                        color[dest, channel] += image[y, x, channel] * weight
        for x in range(width):
            if weights[x] > 0:
                for channel in range(3):
                    output[y, x, channel] = min(255, max(0, round(color[x, channel] / weights[x])))
        x = 0
        while x < width:
            if weights[x] > 0:
                x += 1
                continue
            start = x
            while x < width and weights[x] == 0:
                x += 1
            end = x
            holes += end - start
            left, right = start - 1, end
            # Only disoccluded samples are filled. A closer foreground endpoint
            # must never be stretched across a newly visible background strip.
            if left < 0:
                selected = min(right, width - 1)
            elif right >= width:
                selected = left
            else:
                selected = left if front[left] < front[right] else right
            for missing in range(start, end):
                if left >= 0 and right < width and abs(front[left] - front[right]) < 0.25:
                    fraction = (missing - left) / (right - left)
                    for channel in range(3):
                        output[y, missing, channel] = round(output[y, left, channel] * (1 - fraction)
                                                            + output[y, right, channel] * fraction)
                else:
                    output[y, missing] = output[y, selected]
    return output, holes


def disparity_field(depth: np.ndarray, image: np.ndarray, low: float, high: float) -> np.ndarray:
    height, width = image.shape[:2]
    normalized = np.clip((depth.astype(np.float32) - low) / (high - low), 0, 1)
    field = cv2.resize(normalized, (width, height), interpolation=cv2.INTER_LINEAR)
    # A small luminance-guided filter aligns upsampled depth with source edges.
    guide = cv2.cvtColor(image, cv2.COLOR_BGR2GRAY).astype(np.float32) / 255.0
    mean = lambda value: cv2.boxFilter(value, -1, (9, 9), borderType=cv2.BORDER_REFLECT)
    mean_i, mean_p = mean(guide), mean(field)
    covariance = mean(guide * field) - mean_i * mean_p
    variance = mean(guide * guide) - mean_i * mean_i
    a = covariance / (variance + 0.0025)
    b = mean_p - a * mean_i
    field = np.clip(mean(a) * guide + mean(b), 0, 1) * 16.0 - 4.0
    # Keep original black letterbox geometry and the outer picture edge fixed.
    field[:124] = 0
    field[962:] = 0
    border = np.minimum(np.arange(width), np.arange(width)[::-1]).astype(np.float32)
    field *= np.minimum(border / 64.0, 1.0)[None, :]
    return np.ascontiguousarray(field, dtype=np.float32)


def audio_packets(path: Path) -> list[dict]:
    command = ["ffprobe", "-v", "error", "-select_streams", "a:0", "-show_packets",
               "-show_data_hash", "sha256", "-show_entries", "packet=pts,dts,duration,size,data_hash",
               "-of", "json", str(path)]
    return json.loads(subprocess.check_output(command))["packets"]


def validate_shots(manifest: dict) -> None:
    if manifest.get("source_sha256") != INTRO_SHA256 or manifest.get("frame_count") != FRAMES:
        raise ValueError("Shot manifest does not describe the approved intro")
    cursor = 0
    for shot in manifest["shots"]:
        if shot["start"] != cursor or not cursor < shot["end"] <= FRAMES:
            raise ValueError("Shot intervals must cover every original frame exactly once")
        if shot["mode"] not in ("screen", "depth"):
            raise ValueError("Unknown shot mode")
        cursor = shot["end"]
    if cursor != FRAMES:
        raise ValueError("Shot intervals lost the native ending")


def read_depth_receipt(path: Path, shot: dict, wait: bool) -> dict:
    deadline = time.monotonic() + (3600 if wait else 0)
    while True:
        try:
            info = json.loads(path.read_text())
            if (info["source_sha256"] != INTRO_SHA256 or info["code_revision"] != CODE_REVISION
                    or info["model_revision"] != MODEL_REVISION or info["checkpoint_sha256"] != WEIGHTS_SHA256
                    or info["device"] != "cpu" or info["dtype"] != "float32" or info["input_size"] != 392
                    or (info["start"], info["end"]) != (shot["start"], shot["end"])
                    or info["depth_shape"] != [shot["end"] - shot["start"], 360, 640]):
                raise ValueError("Depth provenance/frame address mismatch")
            low, high = info["normalization_p02_p98"]
            if not math.isfinite(low) or not math.isfinite(high) or high <= low:
                raise ValueError("Invalid shot-wide depth normalization")
            return info
        except (FileNotFoundError, json.JSONDecodeError, KeyError):
            # Also accepts a producer started before atomic publication existed.
            # A complete but invalid receipt is never retried as valid evidence.
            if time.monotonic() >= deadline:
                raise
            time.sleep(0.5)


def encode(source: Path, shots_path: Path, depths: Path, directory: Path, wait_depth: bool = False) -> Path:
    if digest(source) != INTRO_SHA256:
        raise ValueError("Only the already replaced GloomhavenVR intro can be converted")
    manifest = json.loads(shots_path.read_text())
    validate_shots(manifest)
    directory.mkdir(parents=True, exist_ok=False)
    target = directory / "GH_CP_Intro.sbs-v1.mov"
    command = ["ffmpeg", "-hide_banner", "-loglevel", "error", "-nostdin", "-n",
               "-f", "rawvideo", "-pixel_format", "bgr24", "-video_size", "1920x1080",
               "-framerate", "60", "-i", "pipe:0", "-threads", "2", "-i", str(source),
               "-filter_complex_threads", "1", "-filter_complex",
               "[0:v]scale=flags=bicubic+accurate_rnd+full_chroma_int+full_chroma_inp:"
               "out_color_matrix=bt709,format=yuv420p,setsar=1[right];"
               "[1:v][right]hstack=inputs=2,setsar=1[packed]",
               "-map", "[packed]", "-map", "1:a:0", "-frames:v", "2931",
               # Keep the single delivered movie within the repository's binary
               # file limit; full-frame left-eye comparison still gates quality.
               "-c:v", "libx264", "-threads", "3", "-preset", "fast", "-crf", "17",
               "-maxrate", "17M", "-bufsize", "34M",
               "-profile:v", "high", "-level:v", "5.1", "-g", "60", "-keyint_min", "1",
               "-video_track_timescale", "15360", "-color_primaries", "bt709",
               "-color_trc", "bt709", "-colorspace", "bt709", "-color_range", "tv",
               "-c:a", "copy", "-map_metadata", "-1", "-metadata",
               "title=GloomhavenVR - Campaign Intro (Full SBS left first)",
               "-movflags", "+faststart", str(target)]
    save(directory / "command.json", command)
    decode_command = ["ffmpeg", "-hide_banner", "-v", "error", "-nostdin", "-threads", "2",
                      "-i", str(source), "-map", "0:v:0", "-an", "-vf",
                      "scale=in_color_matrix=bt709:in_range=tv", "-pix_fmt", "bgr24", "-f", "rawvideo", "-"]
    save(directory / "decode-command.json", decode_command)
    process, decoder, decode_error = None, None, None
    began = time.monotonic()
    records = []
    cv2.setNumThreads(1)
    try:
        decode_error = (directory / "decode.log").open("wb")
        decoder = subprocess.Popen(decode_command, stdout=subprocess.PIPE, stderr=decode_error,
                                   start_new_session=True)
        with (directory / "encode.log").open("wb") as error:
            process = subprocess.Popen(command, stdin=subprocess.PIPE, stderr=error,
                                       stdout=subprocess.DEVNULL, start_new_session=True)
            packed = np.empty((HEIGHT, WIDTH * 2, 3), dtype=np.uint8)
            for shot in manifest["shots"]:
                data, info = None, None
                if shot["mode"] == "depth":
                    depth_file = depths / (shot["id"] + ".npz")
                    depth_receipt = depths / (shot["id"] + ".json")
                    info = read_depth_receipt(depth_receipt, shot, wait_depth)
                    if (info["source_sha256"] != INTRO_SHA256 or digest(depth_file) != info["depth_sha256"]
                            or (info["start"], info["end"]) != (shot["start"], shot["end"])):
                        raise ValueError("Depth provenance/frame address mismatch")
                    with np.load(depth_file) as archive:
                        data = archive["depths"]
                    if (data.dtype != np.float16 or data.shape != (shot["end"] - shot["start"], 360, 640)
                            or not np.isfinite(data).all()):
                        raise ValueError("Depth shape/values differ from the reviewed profile")
                record = {"id": shot["id"], "start": shot["start"], "end": shot["end"],
                          "mode": shot["mode"], "holes_filled": 0, "maximum_hole_fraction": 0.0,
                          "maximum_disparity_px": 0.0, "minimum_disparity_px": 0.0}
                for index in range(shot["start"], shot["end"]):
                    raw = decoder.stdout.read(HEIGHT * WIDTH * 3)
                    if len(raw) != HEIGHT * WIDTH * 3:
                        raise ValueError(f"Missing original frame {index}")
                    left = np.frombuffer(raw, dtype=np.uint8).reshape(HEIGHT, WIDTH, 3)
                    holes = 0
                    if data is None:
                        right = left
                    else:
                        low, high = info["normalization_p02_p98"]
                        field = disparity_field(data[index - shot["start"]], left, low, high)
                        # Actual near-black fades have no depth cue. Fade disparity
                        # with original picture luminance rather than inventing a cut.
                        luminance = float(left[124:962].mean())
                        field *= min(1.0, luminance / 8.0)
                        right, holes = forward_warp(left, field)
                        record["maximum_disparity_px"] = max(record["maximum_disparity_px"], float(field.max()))
                        record["minimum_disparity_px"] = min(record["minimum_disparity_px"], float(field.min()))
                    # The encoder takes left YUV pixels directly from the original
                    # stream. Only the synthesized right eye traverses RGB/YUV.
                    process.stdin.write(right.tobytes())
                    record["holes_filled"] += holes
                    record["maximum_hole_fraction"] = max(record["maximum_hole_fraction"], holes / (WIDTH * HEIGHT))
                    if index in (shot["start"], (shot["start"] + shot["end"]) // 2, shot["end"] - 1):
                        packed[:, :WIDTH] = left
                        packed[:, WIDTH:] = right
                        cv2.imwrite(str(directory / f"source-{index:04d}.jpg"), packed)
                records.append(record)
                save(directory / "geometry.json", records)
                print(json.dumps(record), flush=True)
                del data
            if decoder.stdout.read(1):
                raise ValueError("Unexpected extra original frame")
            if decoder.wait(timeout=60):
                raise RuntimeError("Original BT709 decoder failed; see decode.log")
            process.stdin.close()
            if process.wait(timeout=600):
                raise RuntimeError("SBS encoder failed; see encode.log")
    finally:
        for owned in (process, decoder):
            if owned is not None and owned.poll() is None:
                try:
                    os.killpg(owned.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
                finally:
                    owned.wait()
        if decode_error is not None:
            decode_error.close()
    original_audio, stereo_audio = audio_packets(source), audio_packets(target)
    if original_audio != stereo_audio or len(stereo_audio) != 2291:
        raise ValueError("Original AAC payloads or packet timestamps changed")
    save(directory / "receipt.json", {"source_sha256": INTRO_SHA256, "output_sha256": digest(target),
         "output_bytes": target.stat().st_size, "packing": "full-SBS-left-first", "per_eye_size": [WIDTH, HEIGHT],
         "frames": FRAMES, "fps": FPS, "duration_seconds": FRAMES / FPS,
         "audio": "2291 original AAC packet payloads, timestamps and durations identical",
         "left": "original decoded YUV pixels directly hstacked before final H264 encode",
         "right": "depth-based horizontal forward reprojection with foreground depth test",
         "disocclusion_fill": "farther adjoining source background; no generative inpainting",
         "depth_bounds_px": [-4, 12], "shot_manifest_sha256": digest(shots_path),
         "elapsed_seconds": time.monotonic() - began, "hardware_result": "unverified"})
    return target


def main() -> int:
    def interrupted(_signum, _frame):
        raise KeyboardInterrupt

    signal.signal(signal.SIGTERM, interrupted)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--shots", type=Path, default=Path(__file__).with_name("intro-shots.json"))
    parser.add_argument("--depths", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--wait-depth", action="store_true", help="Consume completed shots while inference finishes")
    args = parser.parse_args()
    print(encode(args.source.resolve(), args.shots.resolve(), args.depths.resolve(), args.out.resolve(), args.wait_depth))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
