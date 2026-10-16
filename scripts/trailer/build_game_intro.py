#!/usr/bin/env python3
"""Build the approved trailer opening as a mod-local campaign movie, offline.

The native game's title-only opening differs from the trailer. Replace PEGI
with the exact mod wordmark, keep every approved opening shot at source speed,
and use the native end fade instead of the trailer's following combat section.
Input originals are never modified. All inputs and frame ranges are receipted.
"""

from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys

import numpy as np
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
FPS, WIDTH, HEIGHT, FRAMES, RATE = 60, 1920, 1080, 2931, 48000
LABEL = "UNOFFICIAL · FAN-MADE MOD"


def digest(path: Path) -> str:
    with path.open("rb") as stream:
        return hashlib.file_digest(stream, "sha256").hexdigest()


def save(path: Path, value: object) -> None:
    path.write_text(json.dumps(value, indent=2, ensure_ascii=False) + "\n")


def audio(path: Path, seconds: float) -> np.ndarray:
    raw = subprocess.check_output([
        "/usr/bin/ffmpeg", "-v", "error", "-nostdin", "-i", str(path),
        "-t", str(seconds), "-vn", "-ar", str(RATE), "-ac", "2",
        "-f", "f32le", "pipe:1",
    ])
    return np.frombuffer(raw, dtype="<f4").reshape(-1, 2).copy()


def build(manifest: Path) -> None:
    spec = json.loads(manifest.read_text())
    inputs = {key: Path(spec[key]).resolve(strict=True)
              for key in ("native_intro", "approved_trailer", "logo", "font")}
    hashes = {key: digest(path) for key, path in inputs.items()}
    for key, expected in spec["input_sha256"].items():
        if hashes[key] != expected:
            raise ValueError(f"Input changed: {key}")
    out = Path(spec["output_directory"]).resolve()
    out.relative_to(ROOT / ".planning/debug/trailer")
    out.mkdir(parents=True, exist_ok=True)
    movie = out / "GH_CP_Intro.mov"
    if movie.exists() or (out / "receipt.json").exists():
        raise ValueError("Use a fresh output directory")

    # Full existing raster artwork, with its aspect intact and both VR letters
    # inside the title safe area. Native title-band vertical centre is y451.
    logo = Image.open(inputs["logo"]).convert("RGBA")
    logo_width = 1730
    scale = logo_width / logo.width
    logo = logo.resize((logo_width, round(logo.height * scale)), Image.Resampling.LANCZOS)
    card = Image.new("RGB", (WIDTH, HEIGHT), "black")
    card.paste(logo, ((WIDTH - logo_width) // 2, round(451 - 64.5 * scale)), logo)
    card.save(out / "opening-logo.png")
    (out / "branding.txt").write_text(LABEL)

    # Preserve approved speech, the cloned name suffix and music. The original
    # campaign movie's final sting replaces the trailer continuation. Picture
    # alignment is +2s; decoded native audio is +2.012s (measured576 samples).
    approved = audio(inputs["approved_trailer"], FRAMES / FPS)
    native = audio(inputs["native_intro"], FRAMES / FPS)
    count = round(FRAMES / FPS * RATE)
    if len(approved) < count or len(native) < count:
        raise ValueError("Input audio does not cover the native movie clock")
    pcm = approved[:count].copy()
    first, last = round(45.58 * RATE), round(45.78 * RATE)
    shift = round(2.012 * RATE)
    # The native export has an over-full-scale final sting (peak1.51).
    # Bound only that inherited tail; the approved dialogue keeps its gain.
    tail = native[first - shift:count - shift] * .60
    ramp = np.ones(len(tail), np.float32)
    ramp[:last - first] = np.linspace(0, 1, last - first)
    pcm[first:] = pcm[first:] * (1 - ramp[:, None]) + tail * ramp[:, None]
    if np.max(np.abs(pcm)) >= 1:
        raise ValueError("Audio would clip")
    subprocess.run([
        "/usr/bin/ffmpeg", "-v", "error", "-nostdin", "-n", "-f", "f32le",
        "-ar", str(RATE), "-ac", "2", "-i", "pipe:0", "-c:a", "pcm_f32le",
        str(out / "soundtrack.wav"),
    ], input=pcm.astype("<f4").tobytes(), check=True)

    # The first three seconds replace the trailer's existing opening card.
    # Keeping the longer native logo
    # interval would conceal approved O01 (3..7s); do not sacrifice that shot.
    # End at the native fade: native frame2626 maps to trailer frame2746.
    # The final two seconds removed here are already silent black in the game.
    footer = (
        f"drawtext=fontfile='{inputs['font']}':textfile=branding.txt:expansion=none:"
        "fontcolor=0xE6DDD0:fontsize=23:borderw=2:bordercolor=black@0.95:"
        "shadowcolor=black@0.9:shadowx=1:shadowy=1:x=w-tw-38:y=h-th-30"
    )
    graph = (
        "[2:v]trim=end_frame=180,setpts=PTS-STARTPTS,"
        "fade=t=in:st=0:d=0.20,fade=t=out:st=2.80:d=0.20,"
        f"{footer},setsar=1,format=yuv420p[title];\n"
        "[0:v]trim=start_frame=180:end_frame=2746,setpts=PTS-STARTPTS,"
        "setsar=1,format=yuv420p[body];\n"
        f"[1:v]trim=end_frame=185,setpts=PTS-STARTPTS,{footer},"
        "setsar=1,format=yuv420p[ending];\n"
        "[title][body][ending]concat=n=3:v=1:a=0,format=yuv420p[outv]\n"
    )
    (out / "filter.ffgraph").write_text(graph)
    command = [
        "/usr/bin/ffmpeg", "-v", "warning", "-nostdin", "-n", "-threads", "2",
        "-i", str(inputs["approved_trailer"]), "-threads", "2", "-ss", "43.766666666666667",
        "-i", str(inputs["native_intro"]), "-loop", "1", "-framerate", str(FPS),
        "-i", str(out / "opening-logo.png"), "-i", str(out / "soundtrack.wav"),
        "-filter_complex_threads", "2", "-filter_complex_script", str(out / "filter.ffgraph"),
        "-map", "[outv]", "-map", "3:a:0", "-frames:v", str(FRAMES),
        "-t", "48.85", "-c:v", "libx264", "-crf", "17", "-preset", "fast",
        "-threads", "4", "-pix_fmt", "yuv420p", "-color_primaries", "bt709",
        "-color_trc", "bt709", "-colorspace", "bt709", "-c:a", "aac", "-b:a", "320k",
        "-metadata", "title=GloomhavenVR - Unofficial Fan-Made Campaign Intro",
        "-movflags", "+faststart", str(movie),
    ]
    save(out / "command.json", command)
    with (out / "render.log").open("wb") as log:
        subprocess.run(command, cwd=out, stdout=log, stderr=subprocess.STDOUT, check=True)
    probe = json.loads(subprocess.check_output([
        "/usr/bin/ffprobe", "-v", "error", "-show_streams", "-show_format", "-of", "json", str(movie),
    ]))
    video = next(s for s in probe["streams"] if s["codec_type"] == "video")
    assert int(video["nb_frames"]) == FRAMES and video["r_frame_rate"] == "60/1"
    assert (video["width"], video["height"]) == (WIDTH, HEIGHT)
    assert abs(float(probe["format"]["duration"]) - 48.85) < .001
    subprocess.run(["/usr/bin/ffmpeg", "-v", "error", "-nostdin", "-i", str(movie),
                    "-f", "null", "-"], check=True, stdout=subprocess.DEVNULL)
    assert hashes == {key: digest(path) for key, path in inputs.items()}
    save(out / "receipt.json", {
        "status": "rendered-and-decoded-awaiting-visual-review", "movie": str(movie),
        "sha256": digest(movie), "bytes": movie.stat().st_size, "inputs": {k: str(v) for k, v in inputs.items()},
        "input_sha256": hashes, "frames": FRAMES, "fps": FPS, "duration_seconds": 48.85,
        "segments": [{"kind": "native-style-mod-logo", "frames": [0, 180]},
                     {"kind": "approved-trailer-at-source-speed", "frames": [180, 2746]},
                     {"kind": "native-fade-and-black-tail", "frames": [2746, 2931],
                      "native_source_frames": [2626, 2811]}],
        "audio": {"approved_unchanged_pcm_until_seconds": 45.58,
                  "native_tail_crossfade_seconds": [45.58, 45.78], "native_tail_shift_seconds": 2.012,
                  "native_tail_gain": .60, "pcm_peak": float(np.max(np.abs(pcm)))},
        "opening_approved_AI_preserved": ["O01", "E01", "E02"], "new_paid_calls": 0,
        "native_callbacks_or_game_data_changed": False, "probe": probe,
    })
    print(json.dumps({"movie": str(movie), "sha256": digest(movie), "frames": FRAMES}))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("manifest", type=Path)
    args = parser.parse_args()
    out = Path(json.loads(args.manifest.read_text())["output_directory"]).resolve()
    out.relative_to(ROOT / ".planning/debug/trailer")
    out.mkdir(parents=True, exist_ok=True)
    code = 1
    try:
        build(args.manifest)
        code = 0
    finally:
        sys.path.insert(0, str(ROOT / "scripts"))
        from town_run_retention import cleanup_completed_run
        log = out / "completion.log"
        log.write_text(f"Media subprocesses exited and were reaped; exit {code}.\n")
        save(out / "cleanup.json", cleanup_completed_run(
            "approved-campaign-intro", code, log, ROOT, process_reaped=True))


if __name__ == "__main__":
    main()
