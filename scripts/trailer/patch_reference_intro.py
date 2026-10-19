#!/usr/bin/env python3
"""Create local, source-preserving name and native-logo patches for the reference film.

No network/model calls occur here. Audio uses an existing generated name suffix
and locally separated original speech; logo rendering uses exact native artwork.
Use the trailer Python environment (NumPy and OpenCV) for logo rendering.
"""

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys

import numpy as np

FFMPEG = "/usr/bin/ffmpeg"


def sha256(path):
    h = hashlib.sha256()
    with Path(path).open("rb") as stream:
        while chunk := stream.read(1024 * 1024):
            h.update(chunk)
    return h.hexdigest()


def write_json(path, value):
    Path(path).write_text(json.dumps(value, indent=2) + "\n")


def decode_audio(path, rate=48000, channels=2, *, start=None, duration=None, tempo=None):
    command = [FFMPEG, "-v", "error", "-nostdin"]
    if start is not None:
        command += ["-ss", str(start)]
    command += ["-i", str(path), "-map", "0:a:0"]
    if duration is not None:
        command += ["-t", str(duration)]
    if tempo is not None:
        command += ["-af", f"atempo={tempo:.12f}"]
    command += ["-ar", str(rate), "-ac", str(channels), "-f", "f32le", "pipe:1"]
    result = subprocess.run(command, capture_output=True, check=True)
    return np.frombuffer(result.stdout, dtype="<f4").reshape(-1, channels).copy()


def save_audio(path, values, rate):
    subprocess.run([FFMPEG, "-v", "error", "-nostdin", "-n", "-f", "f32le",
                    "-ar", str(rate), "-ac", str(values.shape[1]), "-i", "pipe:0",
                    "-c:a", "pcm_f32le", str(path)],
                   input=values.astype("<f4").tobytes(), check=True)


def taper(length, fade):
    value = np.ones(length, dtype=np.float32)
    n = min(fade, length // 2)
    if n:
        ramp = np.sin(np.linspace(0, np.pi / 2, n, endpoint=True)) ** 2
        value[:n] = ramp
        value[-n:] = ramp[::-1]
    return value


def audio_patch(config_path, output):
    config = json.loads(config_path.read_text())
    rate = int(config["sample_rate"])
    if rate != 48000:
        raise ValueError("The reference audio patch requires its native48kHz rate")
    original = decode_audio(config["source"], rate)
    vocals = decode_audio(config["original_vocals"], rate)
    start = round(float(config["patch_start"]) * rate)
    end = round(float(config["patch_end"]) * rate)
    old_start = round(float(config["original_recruit_start"]) * rate)
    old_end = round(float(config["original_recruit_end"]) * rate)
    shift = round(float(config["recruit_shift"]) * rate)
    fade = round(float(config["edge_fade_seconds"]) * rate)
    if not (0 <= start <= old_start < old_end <= end <= len(original)):
        raise ValueError("Invalid original/patch bounds")
    if old_end > len(vocals) or old_end + shift > end:
        raise ValueError("Separated speech or patch does not cover moved original word")
    duration = float(config["generated_vr_end"]) - float(config["generated_vr_start"])
    desired = float(config["generated_vr_target_seconds"])
    suffix = decode_audio(config["generated"], rate, 1,
                          start=config["generated_vr_start"], duration=duration,
                          tempo=duration / desired)
    # atempo can leave a small filter tail; fit the explicitly reviewed word window.
    target = round(desired * rate)
    suffix = suffix[:target]
    if len(suffix) < target:
        suffix = np.pad(suffix, ((0, target - len(suffix)), (0, 0)))
    suffix *= float(config["vr_gain"]) * taper(target, fade)[:, None]
    suffix_start = round(float(config["vr_timeline_start"]) * rate)
    if not (start <= suffix_start and suffix_start + target <= end):
        raise ValueError("Generated suffix exceeds patch window")
    mixed = original.copy()
    word = vocals[old_start:old_end] * taper(old_end - old_start, fade)[:, None]
    mixed[old_start:old_end] -= word
    mixed[old_start + shift:old_end + shift] += word
    mixed[suffix_start:suffix_start + target] += suffix
    outside = np.r_[np.arange(start), np.arange(end, len(original))]
    outside_changes = int(np.count_nonzero(mixed[outside].view(np.uint32) !=
                                           original[outside].view(np.uint32)))
    if outside_changes:
        raise ValueError("PCM changed outside the authorized name-patch window")
    if not np.isfinite(mixed).all() or np.max(np.abs(mixed)) >= 1:
        raise ValueError("Patched audio contains a nonfinite or full-scale sample")
    master = output / "patched-master-48k-stereo.wav"
    save_audio(master, mixed, rate)
    save_audio(output / "patched-opening-12s.wav", mixed[:12 * rate], rate)
    save_audio(output / "generated-vr-suffix.wav", np.repeat(suffix, 2, axis=1), rate)
    save_audio(output / "moved-original-recruit.wav", word, rate)
    # Verify the actual saved delivery, not only the working array.
    delivery = decode_audio(master, rate)
    if not np.array_equal(delivery.view(np.uint32), mixed.view(np.uint32)):
        raise ValueError("Float32 WAV delivery did not retain exact PCM")
    receipt = {"config": config, "method": "Keep the source Gloomhaven word; append only generated V R and move the original recruit word. Subtract/add the same separated original word; retain the source music residual.",
               "sample_rate": rate, "channels": 2, "sample_frames": len(original),
               "decoded_duration_seconds": len(original) / rate,
               "patch_sample_bounds": [start, end],
               "original_pcm_unchanged_outside_patch": outside_changes == 0,
               "outside_changed_float_channels": outside_changes,
               "delivery_pcm_matches_working_array": True,
               "peak_dbfs": float(20 * np.log10(np.max(np.abs(mixed)))),
               "limitations": ["Local source separation is an estimate; small music/speech residuals inside the bounded word window are possible.", "A subsequent AAC mux is lossy and cannot claim unchanged decoded PCM outside the patch.", "Voice performance and the title-burst balance still need listener review."],
               "files": [{"path": str(p), "bytes": p.stat().st_size, "sha256": sha256(p)}
                         for p in [master, output / "patched-opening-12s.wav",
                                   output / "generated-vr-suffix.wav", output / "moved-original-recruit.wav"]]}
    write_json(output / "audio-receipt.json", receipt)
    print(json.dumps({"master": str(master), "duration": receipt["decoded_duration_seconds"],
                      "patch": [start / rate, end / rate], "outside_changed_channels": outside_changes}))


def logo_patch(source, logo_path, output):
    import cv2
    first, count, fps, width, height = 420, 123, 60, 1920, 1080
    logo = cv2.imread(str(logo_path), cv2.IMREAD_UNCHANGED)
    if logo is None or logo.ndim != 3 or logo.shape[2] != 4:
        raise ValueError("Native logo must be an RGBA image")
    sift = cv2.SIFT_create()
    native_keys, native_desc = sift.detectAndCompute(logo[:, :820, :3], None)
    matcher = cv2.BFMatcher()
    frames, matrices, registrations = [], [], []
    # The OpenCV wheel cannot decode the reference AV1 stream on this machine.
    # Decode the exact original frame window through the verified system ffmpeg.
    command = [FFMPEG, "-v", "error", "-nostdin", "-ss", str(first / fps),
               "-i", str(source), "-an", "-frames:v", str(count),
               "-vf", f"scale={width}:{height}:flags=area", "-pix_fmt", "bgr24",
               "-f", "rawvideo", "pipe:1"]
    result = subprocess.run(command, capture_output=True, check=True)
    if len(result.stdout) != count * width * height * 3:
        raise ValueError("Original title video ended early")
    source_frames = np.frombuffer(result.stdout, np.uint8).reshape(count, height, width, 3)
    # The title is stationary after its fire reveal. Repeated O/E features can
    # produce competing per-frame fits, visibly shaking an otherwise still VR.
    # Register one clear native/source frame and use its exact pose throughout.
    anchor_index = 48  # Original7.8s; native letters are clear of the fire burst.
    for index, frame in enumerate(source_frames):
        frames.append(frame)
        if index != anchor_index:
            matrices.append(None)
            registrations.append({"frame": first + index, "anchor": False})
            continue
        keys, desc = sift.detectAndCompute(frame, None)
        good = [a for a, b in matcher.knnMatch(native_desc, desc, k=2)
                if a.distance < .7 * b.distance] if desc is not None else []
        matrix, inliers = None, None
        if len(good) >= 20:
            x = np.float32([native_keys[m.queryIdx].pt for m in good])
            y = np.float32([keys[m.trainIdx].pt for m in good])
            matrix, inliers = cv2.estimateAffinePartial2D(
                x, y, method=cv2.RANSAC, ransacReprojThreshold=3)
        accepted = matrix is not None and int(inliers.sum()) >= 28
        if accepted:
            scale = float(np.hypot(matrix[0, 0], matrix[0, 1]))
            accepted = (1.15 < scale < 1.5 and 360 < matrix[0, 2] < 460
                        and 330 < matrix[1, 2] < 430)
        matrices.append(matrix if accepted else None)
        registrations.append({"frame": first + index, "matches": len(good),
                              "inliers": int(inliers.sum()) if inliers is not None else 0,
                              "accepted": bool(accepted), "anchor": True,
                              "matrix": matrix.tolist() if accepted else None})
    valid = [i for i, matrix in enumerate(matrices) if matrix is not None]
    if not valid:
        raise ValueError("Exact native artwork did not register to the original logo")
    # Only the native suffix is added; existing letters and publisher marks stay original.
    suffix_alpha = logo[:, :, 3].astype(np.float32) / 255
    suffix_alpha[:, :809] = 0
    suffix_alpha[:, 809:820] *= np.linspace(0, 1, 11)[None]
    rgb = logo[:, :, :3].astype(np.float32)
    processes = []
    logs = []
    definitions = [("registered-logo.mkv", "bgr24", "bgr0", "ffv1"),
                   ("logo-matte.mkv", "gray", "gray", "ffv1"),
                   ("title-review.mp4", "bgr24", "yuv420p", "libx264")]
    try:
        for name, raw_format, target_format, codec in definitions:
            log = (output / (name + ".ffmpeg.log")).open("wb")
            logs.append(log)
            command = [FFMPEG, "-v", "warning", "-nostdin", "-n", "-f", "rawvideo",
                       "-pix_fmt", raw_format, "-s", f"{width}x{height}",
                       "-r", str(fps), "-i", "pipe:0", "-an", "-c:v", codec,
                       "-pix_fmt", target_format, "-threads", "2"]
            if codec == "libx264":
                command += ["-preset", "fast", "-crf", "15", "-movflags", "+faststart"]
            command += [str(output / name)]
            processes.append(subprocess.Popen(command, stdin=subprocess.PIPE,
                                              stdout=subprocess.DEVNULL, stderr=log))
        outside_changed_channels = 0
        for index, frame in enumerate(frames):
            matrix = matrices[index]
            if matrix is None:
                before = [v for v in valid if v <= index]
                after = [v for v in valid if v >= index]
                a = before[-1] if before else valid[0]
                b = after[0] if after else valid[-1]
                fraction = (index - a) / (b - a) if a != b else 0
                matrix = matrices[a] * (1 - fraction) + matrices[b] * fraction
            time = (first + index) / fps
            # Source letters emerge through the retained original fire burst at7.30s.
            reveal = float(np.clip((time - 7.30) / .22, 0, 1))
            reveal = reveal * reveal * (3 - 2 * reveal)
            alpha = cv2.warpAffine(suffix_alpha, matrix, (width, height),
                                    flags=cv2.INTER_LINEAR) * reveal
            plate = cv2.warpAffine(rgb, matrix, (width, height), flags=cv2.INTER_LINEAR)
            # Follow the bright source reveal while keeping the native printed texture.
            warmth = max(0., (7.62 - time) / .32) if time >= 7.30 else 0.
            plate = np.clip(plate + warmth * np.array([22, 46, 62]), 0, 255)
            matte = np.rint(alpha * 255).astype(np.uint8)
            plate = np.rint(plate).astype(np.uint8)
            # Quantized matte is also used for the exact verification witness.
            a = matte.astype(np.float32) / 255
            composite = np.rint(frame * (1 - a[:, :, None]) + plate * a[:, :, None]).astype(np.uint8)
            outside_changed_channels += int(np.count_nonzero(composite[matte == 0] != frame[matte == 0]))
            for process, values in zip(processes, [plate, matte, composite]):
                process.stdin.write(values.tobytes())
            if index in [0, 17, 20, 26, 35, 48, 75, 122]:
                cv2.imwrite(str(output / f"title-witness-{index:03d}.png"), composite)
        for process in processes:
            process.stdin.close()
        for process in processes:
            if process.wait() != 0:
                raise RuntimeError("Logo media encoder failed; inspect retained log")
    finally:
        for process in processes:
            if process.poll() is None:
                process.terminate()
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill(); process.wait()
        for log in logs:
            log.close()
    if outside_changed_channels:
        raise ValueError("Original title pixels changed outside the native suffix matte")
    write_json(output / "logo-receipt.json", {
        "source": str(source), "native_logo": str(logo_path),
        "native_logo_sha256": sha256(logo_path), "start_frame": first,
        "frame_count": count, "fps": fps, "window": [first / fps, (first + count) / fps],
        "method": "SIFT-register one validated7.8s native/source title anchor; retain its stationary pose for all123frames. Add only the VR suffix using its native alpha and the original title reveal timing. Retain original letters, fire burst, background and publisher marks.",
        "registrations": registrations, "registered_frames": len(valid),
        "original_pixel_channels_changed_outside_suffix_matte": outside_changed_channels,
        "files": [{"path": str(output / name), "bytes": (output / name).stat().st_size,
                   "sha256": sha256(output / name)} for name, *_ in definitions],
        "assembly_clip": {"id": "native-logo-v2", "path": str(output / "registered-logo.mkv"),
                          "mask_path": str(output / "logo-matte.mkv"),
                          "start_seconds": first / fps,
                          "end_seconds": (first + count) / fps, "timing": "trim"}})
    print(json.dumps({"title_window": [first / fps, (first + count) / fps],
                      "registered_frames": len(valid), "outside_changed_channels": outside_changed_channels}))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="action", required=True)
    speech = sub.add_parser("audio")
    speech.add_argument("--config", type=Path, required=True)
    title = sub.add_parser("logo")
    title.add_argument("--source", type=Path, required=True)
    title.add_argument("--logo", type=Path, required=True)
    for child in [speech, title]:
        child.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    if output.exists():
        raise ValueError("Refusing to overwrite an existing delivery directory")
    output.mkdir(parents=True)
    if args.action == "audio":
        audio_patch(args.config.resolve(strict=True), output)
    else:
        logo_patch(args.source.resolve(strict=True), args.logo.resolve(strict=True), output)


if __name__ == "__main__":
    try:
        main()
    except (ValueError, RuntimeError, subprocess.CalledProcessError) as error:
        print(str(error), file=sys.stderr)
        sys.exit(2)
