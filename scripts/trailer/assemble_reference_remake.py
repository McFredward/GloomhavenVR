#!/usr/bin/env python3
"""Offline, frame-addressed reference-trailer assembly; no inference or network.

Usage: python3 scripts/trailer/assemble_reference_remake.py manifest.json --plan
       python3 scripts/trailer/assemble_reference_remake.py manifest.json --render

Manifest paths resolve relative to the manifest file. Canonical schema:
  {"source":"original.mp4", "output":"review-v1.mp4", "clips":[
    {"id":"forest", "start_seconds":217.2, "end_seconds":223.783333,
     "path":"forest-edit.mp4", "mask_path":"forest-matte.mp4"}],
   "intro_graphic":{"path":"intro.png", "start_seconds":0,"end_seconds":3},
   "end_graphic":{"path":"end.png", "start_seconds":232.5,
                  "end_seconds":246.833333}, "footer_height":0}

White matte pixels replace source; black retains source. A still matte is allowed.
Moving mattes use the replacement's local clock, with matching source duration.
Clips default to timing="retime": their complete available video fits the fixed
window. timing="trim" keeps speed, trims excess and holds the last frame if short.
Optional in_seconds / mask_in_seconds select a local starting point. Graphics
are full-frame images; RGBA preserves the original wherever alpha is transparent.

All original video cuts keep their frame addresses (nearest 60fps boundary).
Source audio alone is copied unchanged, including a tail longer than the video.
--aac-audio explicitly transcodes that audio to AAC320k for compatibility.
The last graph operation burns local text in the existing lower black band.
footer_height>=64 instead reserves a band by fitting the complete picture above
it, without cropping or stretching. No output/evidence directory is overwritten.
--plan is read-only. --render saves manifest, hashes, probes, graph, commands,
logs and QC beside the output in <output filename>.assembly/.
"""

from __future__ import annotations

import argparse
from decimal import Decimal, ROUND_HALF_UP
import hashlib
import json
import math
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import time


ROOT = Path(__file__).resolve().parents[2]
WIDTH, HEIGHT, FPS = 1920, 1080, 60
LABEL = "UNOFFICIAL · FAN-MADE MOD"
STILLS = {".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tif", ".tiff"}


class AssemblyError(ValueError):
    pass


def write_json(path: Path, value: object) -> None:
    with path.open("x", encoding="utf-8") as stream:
        json.dump(value, stream, indent=2, ensure_ascii=False, allow_nan=False)
        stream.write("\n")


def digest(path: Path) -> str:
    hasher = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            hasher.update(chunk)
    return hasher.hexdigest()


def fingerprint(path: Path) -> tuple[int, int, int, int]:
    info = path.stat()
    return info.st_dev, info.st_ino, info.st_size, info.st_mtime_ns


def number(value: object, name: str, minimum: float = 0) -> float:
    if isinstance(value, bool) or not isinstance(value, (int, float)):
        raise AssemblyError(f"{name} must be a finite number")
    result = float(value)
    if not math.isfinite(result) or result < minimum:
        raise AssemblyError(f"{name} must be finite and >= {minimum}")
    return result


def frame(seconds: float) -> int:
    return int((Decimal(str(seconds)) * FPS).to_integral_value(rounding=ROUND_HALF_UP))


def kill_and_reap(process: subprocess.Popen) -> None:
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


def capture(command: list[str], timeout: float = 60) -> str:
    process = subprocess.Popen(command, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
                               start_new_session=True, text=True)
    try:
        stdout, stderr = process.communicate(timeout=timeout)
    except BaseException:
        kill_and_reap(process)
        raise
    if process.returncode:
        raise AssemblyError(f"Command failed ({process.returncode}): {stderr[-4000:].strip()}")
    return stdout


def execute(command: list[str], log: Path, cwd: Path) -> None:
    with log.open("x", encoding="utf-8") as stream:
        stream.write(json.dumps(command, ensure_ascii=False) + "\n")
        stream.flush()
        process = subprocess.Popen(command, stdout=stream, stderr=subprocess.STDOUT,
                                   cwd=cwd, start_new_session=True)
        try:
            code = process.wait()
        except BaseException:
            kill_and_reap(process)
            raise
    if code:
        raise AssemblyError(f"FFmpeg failed ({code}); retained log: {log}")


def tools(args: argparse.Namespace) -> tuple[str, str, str]:
    candidates = [args.ffmpeg] if args.ffmpeg else [shutil.which("ffmpeg"), "/usr/bin/ffmpeg"]
    seen = set()
    for candidate in candidates:
        if not candidate or candidate in seen or not Path(candidate).is_file():
            continue
        seen.add(candidate)
        filters = capture([candidate, "-hide_banner", "-filters"])
        encoders = capture([candidate, "-hide_banner", "-encoders"])
        if re.search(r"\bdrawtext\b", filters) and re.search(r"\blibx264\b", encoders):
            ffprobe = args.ffprobe or str(Path(candidate).with_name("ffprobe"))
            if not Path(ffprobe).is_file():
                ffprobe = shutil.which("ffprobe")
            if not ffprobe:
                raise AssemblyError("ffprobe is not available")
            version = capture([candidate, "-version"]).splitlines()[0]
            return str(Path(candidate).resolve()), str(Path(ffprobe).resolve()), version
    raise AssemblyError("A local FFmpeg with drawtext and libx264 is required; use --ffmpeg")


def probe(path: Path, ffprobe: str) -> dict:
    return json.loads(capture([ffprobe, "-v", "error", "-protocol_whitelist", "file,pipe",
                               "-show_streams", "-show_format", "-of", "json", str(path)]))


def video_info(metadata: dict, name: str, still: bool = False) -> dict:
    streams = [s for s in metadata.get("streams", []) if s.get("codec_type") == "video"]
    if not streams:
        raise AssemblyError(f"{name}: no video stream")
    stream = streams[0]
    duration = stream.get("duration", metadata.get("format", {}).get("duration"))
    try:
        duration = float(duration) if duration not in (None, "N/A") else None
    except (TypeError, ValueError):
        raise AssemblyError(f"{name}: invalid duration metadata") from None
    if not still and (duration is None or not math.isfinite(duration) or duration <= 0):
        raise AssemblyError(f"{name}: positive video duration is required")
    if not stream.get("width") or not stream.get("height"):
        raise AssemblyError(f"{name}: video dimensions are missing")
    return {"stream_index": stream["index"], "duration_seconds": duration,
            "width": stream["width"], "height": stream["height"], "stream": stream}


def path_value(value: object, base: Path, name: str) -> Path:
    if not isinstance(value, str) or not value or "\n" in value or "\r" in value:
        raise AssemblyError(f"{name}: expected a local file path")
    path = Path(value).expanduser()
    if not path.is_absolute():
        path = base / path
    path = path.resolve()
    if not path.is_file():
        raise AssemblyError(f"{name}: file not found: {path}")
    return path


def interval(item: dict, video_seconds: float, total_frames: int, name: str) -> dict:
    start = number(item.get("start_seconds"), f"{name}.start_seconds")
    end = number(item.get("end_seconds"), f"{name}.end_seconds")
    if end <= start or end > video_seconds + 0.000001:
        raise AssemblyError(f"{name}: window must lie inside source video [0, {video_seconds}]")
    first, last = frame(start), frame(end)
    if first >= last or last > total_frames:
        raise AssemblyError(f"{name}: empty or out-of-range 60fps window")
    return {"requested_start_seconds": start, "requested_end_seconds": end,
            "start_frame": first, "end_frame_exclusive": last, "frame_count": last - first,
            "start_seconds": first / FPS, "end_seconds": last / FPS,
            "duration_seconds": (last - first) / FPS}


def build_plan(manifest_path: Path, args: argparse.Namespace) -> tuple[dict, dict]:
    manifest_path = manifest_path.resolve(strict=True)
    raw = manifest_path.read_text(encoding="utf-8")
    manifest = json.loads(raw)
    if not isinstance(manifest, dict):
        raise AssemblyError("Manifest must be an object")
    base = manifest_path.parent
    ffmpeg, ffprobe, version = tools(args)
    source = path_value(manifest.get("source"), base, "source")
    output_value = manifest.get("output")
    if not isinstance(output_value, str) or not output_value:
        raise AssemblyError("output must be a new .mp4 path")
    output = Path(output_value).expanduser()
    output = (output if output.is_absolute() else base / output).resolve()
    evidence = output.with_name(output.name + ".assembly")
    if output.suffix.lower() != ".mp4" or output.exists() or evidence.exists():
        raise AssemblyError("Output must be a new .mp4, and its .assembly directory must not exist")
    entries = {}

    def media(path: Path) -> dict:
        key = str(path)
        if key not in entries:
            before = fingerprint(path)
            metadata = probe(path, ffprobe)
            sha = digest(path)
            if before != fingerprint(path):
                raise AssemblyError(f"Input changed during preflight: {path}")
            entries[key] = {"sha256": sha, "metadata": metadata, "fingerprint": list(before)}
        return entries[key]

    source_media = media(source)
    source_video = video_info(source_media["metadata"], "source")
    source_stream = source_video["stream"]
    if abs(float(source_stream.get("start_time", 0))) > 1 / FPS:
        raise AssemblyError("Source video must start at zero; normalize its A/V timeline explicitly first")
    audio = [s for s in source_media["metadata"].get("streams", []) if s.get("codec_type") == "audio"]
    if not audio:
        raise AssemblyError("Original master audio is required")
    seconds = source_video["duration_seconds"]
    total_frames = frame(seconds)
    if total_frames <= 0:
        raise AssemblyError("Source video has no output frames")
    footer = manifest.get("footer_height", 0)
    if type(footer) is not int or footer != 0 and not 64 <= footer <= 200:
        raise AssemblyError("footer_height must be 0 (existing band) or an integer 64..200")
    font_value = manifest.get("font_path")
    if font_value:
        font = path_value(font_value, base, "font_path")
    else:
        font = next((p for p in [Path("/usr/share/fonts/truetype/lato/Lato-Semibold.ttf"),
                                   Path("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf")] if p.is_file()), None)
        if font is None:
            raise AssemblyError("Provide a local font_path")
    clips = manifest.get("clips", [])
    if not isinstance(clips, list):
        raise AssemblyError("clips must be a list")
    normalized = []
    ids = set()
    for item in clips:
        if not isinstance(item, dict) or not isinstance(item.get("id"), str) or not item["id"]:
            raise AssemblyError("Every clip needs a nonempty string id")
        if item["id"] in ids:
            raise AssemblyError(f"Duplicate clip id: {item['id']}")
        ids.add(item["id"])
        edit = interval(item, seconds, total_frames, item["id"])
        path = path_value(item.get("path"), base, item["id"] + ".path")
        info = video_info(media(path)["metadata"], item["id"])
        local_start = number(item.get("in_seconds", 0), item["id"] + ".in_seconds")
        available = info["duration_seconds"] - local_start
        if available <= 0:
            raise AssemblyError(f"{item['id']}: in_seconds is beyond replacement video")
        timing = item.get("timing", "retime")
        if timing not in ("retime", "trim"):
            raise AssemblyError(f"{item['id']}: timing must be retime or trim")
        span = available if timing == "retime" else min(available, edit["duration_seconds"])
        edit.update({"id": item["id"], "path": str(path), "video": info,
                     "in_seconds": local_start, "input_span_seconds": span, "timing": timing,
                     "pts_multiplier": edit["duration_seconds"] / span if timing == "retime" else 1,
                     "held_tail_seconds": max(0, edit["duration_seconds"] - span) if timing == "trim" else 0})
        if item.get("mask_path") is not None:
            mask = path_value(item["mask_path"], base, item["id"] + ".mask_path")
            still = mask.suffix.lower() in STILLS
            mask_info = video_info(media(mask)["metadata"], item["id"] + ".mask", still)
            if abs((mask_info["width"] / mask_info["height"]) / (info["width"] / info["height"]) - 1) > .01:
                raise AssemblyError(f"{item['id']}: matte and replacement must have the same aspect ratio")
            mask_start = number(item.get("mask_in_seconds", local_start), item["id"] + ".mask_in_seconds")
            if not still and mask_info["duration_seconds"] - mask_start + .000001 < span:
                raise AssemblyError(f"{item['id']}: moving matte is shorter than selected replacement span")
            edit.update({"mask_path": str(mask), "mask_still": still, "mask_video": mask_info,
                         "mask_in_seconds": 0 if still else mask_start})
        normalized.append(edit)
    normalized.sort(key=lambda e: (e["start_frame"], e["end_frame_exclusive"]))
    for left, right in zip(normalized, normalized[1:]):
        if (left["requested_end_seconds"] > right["requested_start_seconds"] + .000000001
                or left["end_frame_exclusive"] > right["start_frame"]):
            raise AssemblyError(f"Overlapping clips: {left['id']} / {right['id']}")
    graphics = []
    for name in ("intro_graphic", "end_graphic"):
        item = manifest.get(name)
        if item is None:
            continue
        if not isinstance(item, dict):
            raise AssemblyError(f"{name} must have path/start_seconds/end_seconds")
        edit = interval(item, seconds, total_frames, name)
        path = path_value(item.get("path"), base, name + ".path")
        if path.suffix.lower() not in STILLS:
            raise AssemblyError(f"{name}: a full-frame still image is required")
        edit.update({"id": name, "path": str(path), "video": video_info(media(path)["metadata"], name, True)})
        graphics.append(edit)
    threads = args.threads
    if not 1 <= threads <= 4:
        raise AssemblyError("--threads must be between 1 and 4")
    crf = manifest.get("crf", 18)
    if type(crf) is not int or not 0 <= crf <= 35:
        raise AssemblyError("crf must be an integer 0..35")
    preset = manifest.get("preset", "medium")
    if preset not in ("ultrafast", "superfast", "veryfast", "faster", "fast", "medium", "slow"):
        raise AssemblyError("Unsupported x264 preset")
    plan = {"schema_version": 1, "manifest_path": str(manifest_path),
            "manifest_sha256": hashlib.sha256(raw.encode()).hexdigest(), "source": str(source),
            "output": str(output), "evidence_directory": str(evidence), "ffmpeg": ffmpeg,
            "ffprobe": ffprobe, "ffmpeg_version": version, "output_size": [WIDTH, HEIGHT],
            "output_fps": FPS, "expected_video_frames": total_frames,
            "expected_video_duration_seconds": total_frames / FPS,
            "original_format_duration_seconds": float(source_media["metadata"]["format"]["duration"]),
            "original_audio": audio[0], "audio_mode": "aac320k" if args.aac_audio else "copy",
            "clips": normalized, "graphics": graphics, "footer_height": footer,
            "font_path": str(font), "font_sha256": digest(font), "branding_text": LABEL,
            "threads": threads, "filter_threads": 1, "crf": crf, "preset": preset,
            "inputs": entries, "notes": ["Single source-video decode and one final encode.",
                "Only original first audio stream is mapped; replacement/model audio is never mapped.",
                "Windows use nearest 60fps frames; source timeline/cuts do not slide.",
                "No -shortest or video-duration trim of the original audio tail.",
                "Outside mattes retains the normalized original, apart from final branding/graphics and lossy encoding."]}
    graph, command = build_command(plan)
    plan.update({"filter_graph": graph, "render_command": command})
    return plan, manifest


def build_command(plan: dict) -> tuple[str, list[str]]:
    command = [plan["ffmpeg"], "-nostdin", "-hide_banner", "-loglevel", "warning", "-nostats", "-n",
               "-filter_threads", "1", "-filter_complex_threads", "1",
               "-threads", str(plan["threads"]), "-protocol_whitelist", "file,pipe", "-i", plan["source"]]
    graph = [f"[0:v:0]setpts=PTS-STARTPTS,fps={FPS},scale={WIDTH}:{HEIGHT}:flags=lanczos,"
             f"setsar=1,format=yuv420p,trim=end_frame={plan['expected_video_frames']},"
             f"settb=1/{FPS},setpts=N[base0]"]
    current, index, node = "base0", 1, 0

    def add_input(path: str, still: bool = False) -> int:
        nonlocal index
        result = index
        if still:
            command.extend(["-loop", "1", "-framerate", str(FPS)])
        command.extend(["-threads", "1", "-protocol_whitelist", "file,pipe", "-an", "-i", path])
        index += 1
        return result

    def normalize(input_index: int, edit: dict, output: str, mask: bool = False, still: bool = False) -> None:
        start = 0 if still else edit.get("mask_in_seconds" if mask else "in_seconds", 0)
        span = edit["duration_seconds"] if still else edit.get("input_span_seconds", edit["duration_seconds"])
        multiplier = 1 if still else edit.get("pts_multiplier", 1)
        pixel_format = "gray" if mask else "rgba"
        flags = "bilinear" if mask else "lanczos"
        padding = "black" if mask or not still else "black@0"
        graph.append(f"[{input_index}:v:0]trim=start={start:.9f}:duration={span:.9f},"
                     f"setpts=(PTS-STARTPTS)*{multiplier:.12f},fps={FPS}:round=near,"
                     f"scale={WIDTH}:{HEIGHT}:force_original_aspect_ratio=decrease:flags={flags},"
                     f"format={pixel_format},pad={WIDTH}:{HEIGHT}:(ow-iw)/2:(oh-ih)/2:color={padding},setsar=1,"
                     f"tpad=stop_mode=clone:stop_duration={edit['duration_seconds'] + 1:.9f},"
                     f"trim=end_frame={edit['frame_count']},settb=1/{FPS},setpts=N[{output}]")

    for edit in plan["clips"]:
        node += 1
        replacement = add_input(edit["path"])
        normalize(replacement, edit, f"replace{node}")
        foreground = f"replace{node}"
        if edit.get("mask_path"):
            matte = add_input(edit["mask_path"], edit["mask_still"])
            normalize(matte, edit, f"matte{node}", mask=True, still=edit["mask_still"])
            graph.append(f"[{foreground}][matte{node}]alphamerge[masked{node}]")
            foreground = f"masked{node}"
        graph.append(f"[{foreground}]setpts=PTS+{edit['start_frame']}[placed{node}]")
        # Generic multi-input timeline n can count secondary frames too. Use
        # the normalized main clock and rational frame boundaries instead.
        graph.append(f"[{current}][placed{node}]overlay=eof_action=pass:repeatlast=0:"
                     f"enable='gte(t,{edit['start_frame']}/{FPS})*lt(t,{edit['end_frame_exclusive']}/{FPS})'[base{node}]")
        current = f"base{node}"
    for edit in plan["graphics"]:
        node += 1
        image = add_input(edit["path"], True)
        normalize(image, edit, f"graphic{node}", still=True)
        graph.append(f"[graphic{node}]setpts=PTS+{edit['start_frame']}[placed{node}]")
        graph.append(f"[{current}][placed{node}]overlay=eof_action=pass:repeatlast=0:"
                     f"enable='gte(t,{edit['start_frame']}/{FPS})*lt(t,{edit['end_frame_exclusive']}/{FPS})'[base{node}]")
        current = f"base{node}"
    if plan["footer_height"]:
        height = HEIGHT - plan["footer_height"]
        graph.append(f"[{current}]scale={WIDTH}:{height}:force_original_aspect_ratio=decrease,"
                     f"pad={WIDTH}:{HEIGHT}:(ow-iw)/2:0:color=black[footer]")
        current = "footer"
    graph.append(f"[{current}]drawtext=fontfile=font.ttf:textfile=branding.txt:expansion=none:"
                 f"fontcolor=0xE6DDD0:fontsize=23:borderw=2:bordercolor=black@0.95:"
                 f"shadowcolor=black@0.9:shadowx=1:shadowy=1:x=w-tw-38:y=h-th-30,format=yuv420p[outv]")
    command.extend(["-filter_complex_script", "filter.ffgraph", "-map", "[outv]", "-map", "0:a:0",
                    "-map_metadata", "0", "-c:v", "libx264", "-threads", str(plan["threads"]),
                    "-preset", plan["preset"], "-crf", str(plan["crf"]), "-pix_fmt", "yuv420p"])
    if plan["audio_mode"] == "copy":
        command.extend(["-c:a", "copy"])
    else:
        command.extend(["-c:a", "aac", "-b:a", "320k"])
    command.extend(["-movflags", "+faststart", "render.partial.mp4", "-map", "0:a:0", "-c:a", "copy",
                    "-f", "streamhash", "-hash", "sha256", "original-audio.streamhash"])
    return ";\n".join(graph) + "\n", command


def audio_hash(path: Path) -> str:
    rows = [line.strip() for line in path.read_text().splitlines() if line.strip() and not line.startswith("#")]
    if len(rows) != 1 or not re.search(r"[0-9a-fA-F]{64}$", rows[0]):
        raise AssemblyError(f"Invalid audio streamhash: {path}")
    return re.search(r"[0-9a-fA-F]{64}$", rows[0]).group(0).lower()


def verify(plan: dict, evidence: Path) -> dict:
    partial = evidence / "render.partial.mp4"
    metadata = probe(partial, plan["ffprobe"])
    info = video_info(metadata, "rendered output")
    video = info["stream"]
    audio = [s for s in metadata["streams"] if s["codec_type"] == "audio"]
    checks = {"video_codec": video.get("codec_name") == "h264",
              "size": [info["width"], info["height"]] == [WIDTH, HEIGHT],
              "fps": video.get("avg_frame_rate") == "60/1",
              "frames": int(video.get("nb_frames", -1)) == plan["expected_video_frames"],
              "video_duration": abs(info["duration_seconds"] - plan["expected_video_duration_seconds"]) < 1 / FPS,
              "one_audio_stream": len(audio) == 1}
    command = [plan["ffmpeg"], "-nostdin", "-hide_banner", "-loglevel", "error", "-nostats", "-n",
               "-xerror", "-filter_threads", "1", "-filter_complex_threads", "1", "-err_detect", "explode",
               "-threads", str(plan["threads"]), "-protocol_whitelist", "file,pipe", "-i", str(partial),
               "-map", "0:a:0", "-c:a", "copy", "-f", "streamhash", "-hash", "sha256",
               "rendered-audio.streamhash", "-map", "0:v:0", "-map", "0:a:0", "-threads",
               str(plan["threads"]), "-f", "null", "-"]
    write_json(evidence / "qc-command.json", command)
    execute(command, evidence / "qc-decode.log", evidence)
    checks["complete_decode"] = True
    original_hash = audio_hash(evidence / "original-audio.streamhash")
    rendered_hash = audio_hash(evidence / "rendered-audio.streamhash")
    if audio:
        checks["audio_channels"] = audio[0].get("channels") == plan["original_audio"].get("channels")
        checks["audio_sample_rate"] = audio[0].get("sample_rate") == plan["original_audio"].get("sample_rate")
        original_duration = plan["original_audio"].get("duration")
        if original_duration is not None and audio[0].get("duration") is not None:
            tolerance = .001 if plan["audio_mode"] == "copy" else .06
            checks["audio_duration"] = abs(float(audio[0]["duration"]) - float(original_duration)) < tolerance
        checks["audio_start_time"] = abs(float(audio[0].get("start_time", 0)) -
                                         float(plan["original_audio"].get("start_time", 0))) < .001
        if plan["audio_mode"] == "copy":
            checks["original_audio_packets_identical"] = rendered_hash == original_hash
            checks["audio_codec"] = audio[0].get("codec_name") == plan["original_audio"].get("codec_name")
        else:
            checks["audio_codec"] = audio[0].get("codec_name") == "aac"
    result = {"passed": all(checks.values()), "checks": checks, "output_metadata": metadata,
              "original_audio_packet_sha256": original_hash, "output_audio_packet_sha256": rendered_hash,
              "audio_transcoded": plan["audio_mode"] != "copy", "audio_sources": [plan["source"]],
              "limits": "Technical decode/timing/audio QC only; no claim of visual or mechanical parity."}
    write_json(evidence / "qc.json", result)
    if not result["passed"]:
        raise AssemblyError("Output QC failed; unpublished partial and receipts are retained")
    return result


def render(plan: dict, manifest: dict) -> dict:
    output, evidence = Path(plan["output"]), Path(plan["evidence_directory"])
    output.parent.mkdir(parents=True, exist_ok=True)
    evidence.mkdir()  # Exclusive, before FFmpeg; never reuse another run's evidence.
    started = time.monotonic()
    code = 1
    try:
        write_json(evidence / "manifest.json", manifest)
        write_json(evidence / "plan.json", plan)
        write_json(evidence / "command.json", plan["render_command"])
        (evidence / "filter.ffgraph").write_text(plan["filter_graph"], encoding="utf-8")
        (evidence / "branding.txt").write_text(LABEL, encoding="utf-8")
        shutil.copyfile(plan["font_path"], evidence / "font.ttf")
        execute(plan["render_command"], evidence / "ffmpeg.log", evidence)
        qc = verify(plan, evidence)
        for path, entry in plan["inputs"].items():
            if list(fingerprint(Path(path))) != entry["fingerprint"]:
                raise AssemblyError(f"Input changed during render: {path}")
        partial = evidence / "render.partial.mp4"
        sha = digest(partial)
        os.link(partial, output)  # Exclusive publication; EEXIST never overwrites.
        partial.unlink()
        receipt = {"status": "complete", "output": str(output), "output_sha256": sha,
                   "manifest_sha256": plan["manifest_sha256"], "elapsed_seconds": time.monotonic() - started,
                   "evidence_directory": str(evidence), "qc_passed": qc["passed"],
                   "original_video_frames": plan["expected_video_frames"],
                   "original_audio_tail_preserved": plan["audio_mode"] == "copy",
                   "audio_mode": plan["audio_mode"], "replacement_audio_used": False}
        write_json(evidence / "receipt.json", receipt)
        code = 0
        return receipt
    except BaseException as error:
        code = 130 if isinstance(error, KeyboardInterrupt) else 1
        write_json(evidence / "failure.json", {"status": "interrupted" if isinstance(error, KeyboardInterrupt) else "failed",
                                              "error": str(error), "elapsed_seconds": time.monotonic() - started,
                                              "processes_reaped": True})
        raise
    finally:
        # This assembler creates no regenerable project caches. The central API
        # still audits only this completed, reaped run; it fails closed outside a
        # recognized .planning/debug/run-* and preserves media/source receipts.
        sys.dont_write_bytecode = True
        sys.path.insert(0, str(ROOT / "scripts"))
        from town_run_retention import cleanup_completed_run
        completion = evidence / "completion.log"
        completion.write_text(f"evidence: {evidence}\nexit_code: {code}\nprocesses_reaped: true\n")
        cleanup = cleanup_completed_run("trailer-reference-assembly", code, completion, ROOT, process_reaped=True)
        write_json(evidence / "cleanup.json", cleanup)


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("manifest", type=Path)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--plan", action="store_true")
    mode.add_argument("--render", action="store_true")
    parser.add_argument("--ffmpeg")
    parser.add_argument("--ffprobe")
    parser.add_argument("--threads", type=int, default=2)
    parser.add_argument("--aac-audio", action="store_true", help="Explicit AAC320k compatibility transcode; default copies master audio")
    args = parser.parse_args()
    def interrupt(_signum: int, _frame: object) -> None:
        raise KeyboardInterrupt
    signal.signal(signal.SIGTERM, interrupt)
    try:
        plan, manifest = build_plan(args.manifest, args)
        result = plan if args.plan else render(plan, manifest)
        print(json.dumps(result, indent=2, ensure_ascii=False, allow_nan=False))
        return 0
    except KeyboardInterrupt:
        print("Assembly interrupted; child processes reaped and evidence retained", file=sys.stderr)
        return 130
    except (AssemblyError, OSError, json.JSONDecodeError) as error:
        print(f"Assembly rejected: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
