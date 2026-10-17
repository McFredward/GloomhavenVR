#!/usr/bin/env python3
"""Independently decode every intro frame/PTS and compare the retained left eye."""
from __future__ import annotations

import argparse
import json
import math
import os
from pathlib import Path
import re
import signal
import subprocess

import cv2
import numpy as np

from intro_depth import INTRO_SHA256, digest, save, validate_model
from intro_stereo import FRAMES, FPS, audio_packets, disparity_field, forward_warp, read_depth_receipt, validate_shots


def run(command: list[str], log: Path, timeout: int = 1200) -> bytes:
    with log.open("wb") as error:
        process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=error, start_new_session=True)
        try:
            output, _ = process.communicate(timeout=timeout)
        except BaseException:
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            finally:
                process.wait()
            raise
    if process.returncode:
        raise RuntimeError(f"Media verification failed; see {log}")
    return output


def validate_probe(probe: dict) -> dict:
    videos = [item for item in probe["streams"] if item["codec_type"] == "video"]
    audio = [item for item in probe["streams"] if item["codec_type"] == "audio"]
    if len(videos) != 1 or len(audio) != 1 or len(probe["streams"]) != 2:
        raise ValueError("Expected exactly one video and the original audio stream")
    video = videos[0]
    if (video["width"], video["height"], video["codec_name"], video["pix_fmt"], video["sample_aspect_ratio"]
            ) != (3840, 1080, "h264", "yuv420p", "1:1"):
        raise ValueError("Wrong stereo packing, codec or pixel aspect")
    if video["r_frame_rate"] != "60/1" or video["avg_frame_rate"] != "60/1":
        raise ValueError("Frame rate changed")
    if int(video["nb_read_frames"]) != FRAMES or abs(float(video["duration"]) - FRAMES / FPS) > .000001:
        raise ValueError("Native video count/duration changed")
    frames = [item for item in probe["frames"] if item.get("media_type") == "video"]
    if len(frames) != FRAMES or any(abs(float(frame["best_effort_timestamp_time"]) - index / FPS) > .000001
                                    for index, frame in enumerate(frames)):
        raise ValueError("Original frame addresses/timestamps changed")
    if (audio[0]["codec_name"], int(audio[0]["sample_rate"]), audio[0]["channels"]) != ("aac", 48000, 2):
        raise ValueError("Original audio format changed")
    return {"decoded_frames": len(frames), "fps": FPS, "duration_seconds": FRAMES / FPS,
            "packed_size": [3840, 1080], "per_eye_size": [1920, 1080], "audio": "AAC stereo 48kHz"}


def verify_right_frames(args, manifest: dict) -> list[dict]:
    """Decode the actual packed movie once, verify both sides of cuts and each midpoint.

    Pure geometry tests cannot detect an encoder wired to the left input twice.
    This check compares the encoded right picture against its same-clock actual
    reprojection, and records its distance from a duplicated original picture.
    """
    cv2.setNumThreads(1)
    logo_depth = None
    if getattr(args, "logo_depth", None) is not None:
        from intro_logo_depth import IntroLogoDepth
        logo_depth = IntroLogoDepth(args.logo_depth.resolve(), args.logo.resolve())
    owned, logs, results = [], [], []
    try:
        for name, path in (("source", args.source), ("packed", args.stereo)):
            command = ["ffmpeg", "-hide_banner", "-v", "error", "-nostdin", "-threads", "2",
                       "-i", str(path), "-map", "0:v:0", "-an"]
            if name == "source":
                command += ["-vf", "scale=in_color_matrix=bt709:in_range=tv", "-pix_fmt", "bgr24"]
            else:
                command += ["-pix_fmt", "yuv420p"]
            command += ["-fps_mode", "passthrough", "-f", "rawvideo", "-"]
            error = (args.out / f"right-{name}-decode.log").open("wb")
            logs.append(error)
            owned.append(subprocess.Popen(command, stdout=subprocess.PIPE, stderr=error, start_new_session=True))
        source, packed = owned
        for shot in manifest["shots"]:
            witnesses = {shot["start"], (shot["start"] + shot["end"]) // 2, shot["end"] - 1}
            depth, info = None, None
            if shot["mode"] == "depth":
                info = read_depth_receipt(args.depths / (shot["id"] + ".json"), shot, False)
                with np.load(args.depths / (shot["id"] + ".npz")) as archive:
                    depth = archive["depths"]
            for frame in range(shot["start"], shot["end"]):
                original_bytes = source.stdout.read(1920 * 1080 * 3)
                actual_bytes = packed.stdout.read(3840 * 1080 * 3 // 2)
                if len(original_bytes) != 1920 * 1080 * 3 or len(actual_bytes) != 3840 * 1080 * 3 // 2:
                    raise ValueError("Decoded right-eye clock lost an original frame")
                if frame not in witnesses:
                    continue
                original = np.frombuffer(original_bytes, np.uint8).reshape(1080, 1920, 3)
                planes = np.frombuffer(actual_bytes, np.uint8)
                y_size, uv_size = 3840 * 1080, 1920 * 540
                actual = np.concatenate((planes[:y_size].reshape(1080, 3840)[:, 1920:].ravel(),
                    planes[y_size:y_size + uv_size].reshape(540, 1920)[:, 960:].ravel(),
                    planes[y_size + uv_size:].reshape(540, 1920)[:, 960:].ravel()))
                expected = original
                if depth is not None:
                    field = disparity_field(depth[frame - shot["start"]], original, *info["normalization_p02_p98"])
                    field *= min(1.0, float(original[124:962].mean()) / 8.0)
                    expected, _ = forward_warp(original, field)
                elif logo_depth is not None:
                    field = logo_depth.field(shot["id"], frame)
                    if field is not None:
                        expected, _ = forward_warp(original, field)
                # Compare in the declared delivery format, exactly as the full
                # left-eye PSNR filter does. RGB includes a second chroma upsample
                # and rounding error which is not H264 encoding distortion.
                conversion = ["ffmpeg", "-v", "error", "-threads", "1", "-filter_threads", "1",
                    "-f", "rawvideo", "-pixel_format", "bgr24", "-video_size", "1920x1080", "-i", "-",
                    "-vf", "scale=flags=bicubic+accurate_rnd+full_chroma_int+full_chroma_inp:"
                    "out_color_matrix=bt709,format=yuv420p", "-frames:v", "2", "-pix_fmt", "yuv420p",
                    "-f", "rawvideo", "-"]
                converted = subprocess.check_output(conversion, input=expected.tobytes() + original.tobytes())
                frame_size = 1920 * 1080 * 3 // 2
                if len(converted) != 2 * frame_size:
                    raise ValueError("Independent YUV420 reference conversion lost a witness")
                reference = np.frombuffer(converted, np.uint8)[:frame_size].astype(np.float32)
                duplicate = np.frombuffer(converted, np.uint8)[frame_size:].astype(np.float32)
                actual_float = actual.astype(np.float32)
                mse = float(np.mean((actual_float - reference) ** 2))
                duplicate_mse = float(np.mean((actual_float - duplicate) ** 2))
                expected_difference = float(np.mean((reference - duplicate) ** 2))
                psnr = 10 * math.log10(255 ** 2 / mse) if mse else 999.0
                if psnr < 34:
                    raise ValueError(f"Encoded right eye lost reprojection/frame correspondence at {frame}: {psnr:.2f}dB")
                # Only require separation when the source itself provides a strong
                # stereo witness. Near-black/title frames intentionally match.
                if expected_difference > 20 and duplicate_mse <= mse * 2:
                    raise ValueError(f"Encoded right eye is consistent with a duplicated left at {frame}")
                results.append({"frame": frame, "shot": shot["id"], "metric_space": "native-YUV420",
                                "right_reference_psnr_db": psnr,
                                "actual_right_vs_duplicate_left_mse": duplicate_mse,
                                "expected_stereo_difference_mse": expected_difference})
        for process in owned:
            if process.stdout.read(1) or process.wait(timeout=60):
                raise ValueError("Unexpected decoded frame or right-eye decoder failure")
        return results
    finally:
        for process in owned:
            if process.poll() is None:
                try:
                    os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
                finally:
                    process.wait()
        for error in logs:
            error.close()


def main() -> int:
    def interrupted(_signum, _frame):
        raise KeyboardInterrupt

    signal.signal(signal.SIGTERM, interrupted)
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source", type=Path, required=True)
    parser.add_argument("--stereo", type=Path, required=True)
    parser.add_argument("--depths", type=Path, required=True)
    parser.add_argument("--model-root", type=Path, required=True)
    parser.add_argument("--shots", type=Path, default=Path(__file__).with_name("intro-shots.json"))
    parser.add_argument("--out", type=Path, required=True)
    parser.add_argument("--logo-depth", type=Path, help="Validate the approved foreground-wordmark revision")
    parser.add_argument("--logo", type=Path, default=Path(__file__).resolve().parents[2] /
                        "src/GloomhavenVR/Assets/GloomhavenVR_logo.png")
    args = parser.parse_args()
    args.out.mkdir(parents=True, exist_ok=False)
    code = 1
    try:
        code = verify(args)
        return code
    finally:
        root = Path(__file__).resolve().parents[2]
        import sys
        sys.path.insert(0, str(root / "scripts"))
        from town_run_retention import cleanup_completed_run
        log = args.out / "completion.log"
        log.write_text(f"All owned media processes stopped and reaped; exit={code}.\nevidence: {args.out.resolve()}\n")
        save(args.out / "cleanup.json", cleanup_completed_run("stereo-video-intro", code, log.resolve(), root,
                                                              process_reaped=True))


def verify(args) -> int:
    if args.stereo.stat().st_size > 100 * 1024 ** 2:
        raise ValueError("Stereo movie exceeds the repository's single-file delivery limit")
    if digest(args.source) != INTRO_SHA256:
        raise ValueError("Source is not the approved, already replaced GloomhavenVR intro")
    validate_model(args.model_root.resolve())
    manifest = json.loads(args.shots.read_text())
    validate_shots(manifest)
    depth_proof = []
    for shot in manifest["shots"]:
        if shot["mode"] == "screen":
            continue
        path = args.depths / (shot["id"] + ".npz")
        receipt = args.depths / (shot["id"] + ".json")
        info = read_depth_receipt(receipt, shot, False)
        if digest(path) != info["depth_sha256"]:
            raise ValueError("Consumed depth bytes differ")
        with np.load(path) as archive:
            depth = archive["depths"]
            if depth.dtype != np.float16 or depth.shape != tuple(info["depth_shape"]) or not np.isfinite(depth).all():
                raise ValueError("Invalid consumed depth values")
        depth_proof.append({"id": shot["id"], "depth_sha256": info["depth_sha256"],
                            "receipt_sha256": digest(receipt)})
    save(args.out / "depth-validation.json", depth_proof)
    command = ["ffprobe", "-v", "error", "-count_frames", "-show_streams", "-show_frames",
               "-show_entries", "frame=media_type,best_effort_timestamp_time", "-of", "json", str(args.stereo)]
    probe = json.loads(run(command, args.out / "probe.log"))
    save(args.out / "probe.json", probe)
    video_proof = validate_probe(probe)
    original, stereo = audio_packets(args.source), audio_packets(args.stereo)
    if original != stereo or len(original) != 2291:
        raise ValueError("AAC payloads/timestamps/durations differ")
    save(args.out / "audio-packets.json", stereo)
    stats = args.out.resolve() / "left-psnr.log"
    command = ["ffmpeg", "-hide_banner", "-v", "info", "-nostdin", "-threads", "2", "-i", str(args.stereo),
               "-threads", "2", "-i", str(args.source), "-filter_complex_threads", "1", "-filter_complex",
               f"[0:v]crop=1920:1080:0:0[left];[left][1:v]psnr=stats_file={stats}[check]",
               "-map", "[check]", "-an", "-f", "null", "-"]
    run(command, args.out / "left-compare.log")
    values = [float(value) for value in re.findall(r"psnr_avg:([0-9.inf]+)", stats.read_text())]
    if len(values) != FRAMES or min(values) < 34:
        raise ValueError("Left-eye original frame correspondence/encoding quality failed")
    finite = [value for value in values if math.isfinite(value)]
    right = verify_right_frames(args, manifest)
    save(args.out / "right-frame-validation.json", right)
    proof = {"status": "passed", "source_sha256": INTRO_SHA256, "stereo_sha256": digest(args.stereo),
             "video": video_proof, "identical_aac_packets": len(stereo),
             "left_comparison": {"frames": len(values), "minimum_psnr_db": min(values),
                                 "mean_finite_psnr_db": sum(finite) / len(finite)},
             "depth_shots_validated": len(depth_proof), "verification_source_sha256": digest(Path(__file__)),
             "encoded_right_witness_frames": len(right),
             "hardware_result": "unverified"}
    save(args.out / "result.json", proof)
    print(json.dumps(proof))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
