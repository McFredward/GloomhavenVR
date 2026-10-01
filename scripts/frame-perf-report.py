#!/usr/bin/env python3
"""Summarize Steam Frame logs without guessing scene, tracking, or GPU busy time.

Usage:
    python3 scripts/frame-perf-report.py --format table LogOutput.log
    python3 scripts/frame-perf-report.py --format json old.log new.log --compare 4:7

Window indices are one-based, in file order. A comparison is deliberately explicit:
the operator selects a window from each of two logs. The report rejects mismatched
scene, tracking, eye target, quality, or camera pose and flags sparse evidence.
LogOutput.log has no per-line timestamps. Line numbers and window durations are
reported instead of invented wall-clock times. The XR ``gpu`` field is a frame
interval/wait on this runtime and is never reported as GPU busy time.
"""

from __future__ import annotations

import argparse
import json
import re
from pathlib import Path


BUILD = re.compile(r"\[Core\] GloomhavenVR ModBuild (\d+)")
COMMIT = re.compile(r"v[\d.]+ build ([0-9a-f]+) \[[^]]+\].* loaded")
SCENE = re.compile(r"\[Rig\] LIGHT STABILISER released — scene changed to '([^']+)'")
SCENE_AUDIT = re.compile(r"\[WallSegmentFade\] WALL-PATH AUDIT scene='([^']+)'")
HEARTBEAT = re.compile(r"\[Core\] Heartbeat #\d+:.*?\bhmd=(tracked|UNTRACKED|invalid)")
EYE = re.compile(r"\[Rig\] EYE-TARGET DIAG .*?eyeTextureDesc (\d+)x(\d+).*?resolutionScale=([\d.]+).*?stereo=([A-Za-z]+)")
QUALITY = re.compile(r"\[Perf\] GFX .*?quality level \d+ '([^']+)'.*?shadows=([^ ]+).*?antiAliasing=(\d+)")
FRAME = re.compile(r"\[Perf\] FRAME ([\d.]+)s n=(\d+).*?display ([\d.]+)Hz budget ([\d.]+)ms.*?frametime mean ([\d.]+) p50 ([\d.]+) p95 ([\d.]+) p99 ([\d.]+) max ([\d.]+)ms.*?mod ([\d.]+)ms/frame avg")
SPLIT = re.compile(r"\[Perf\] SPLIT .*?logic \(Update→LateUpdate\) ([\d.]+).*?render loop \(cull\+submit\) ([\d.]+).*?blocked \(waiting on GPU/compositor\) ([\d.]+)ms")
POSE = re.compile(r"view height p50 ([\d.]+) \(([-\d.]+)\.\.([-\d.]+)\) dist p50 ([\d.]+) \(([-\d.]+)\.\.([-\d.]+)\)")
VISIBLE = re.compile(r"visible p50 (\d+) renderer")


def _state(values: list[str | tuple], unknown: str = "unknown") -> str | tuple:
    """Return a single observed state or mark a transition/absence explicitly."""
    unique = set(values)
    if not unique:
        return unknown
    if len(unique) != 1:
        return "mixed"
    return next(iter(unique))


def read_log(path: Path) -> dict:
    build = None
    commit = None
    scene_state = None
    eye_state = None
    quality_state = None
    scene_values: list[str] = ["unknown"]
    eye_values: list[tuple | str] = ["unknown"]
    quality_values: list[tuple] = []
    tracking_values: list[str] = []
    tracking_events: list[dict] = []
    scene_events: list[dict] = []
    eye_events: list[dict] = []
    transitions: list[dict] = []
    windows: list[dict] = []
    current: dict | None = None

    with path.open(encoding="utf-8", errors="replace") as source:
        for line_no, line in enumerate(source, 1):
            if match := BUILD.search(line):
                build = int(match.group(1))
            if match := COMMIT.search(line):
                commit = match.group(1)
            if match := SCENE.search(line):
                scene_state = match.group(1)
                scene_values.append(scene_state)
                event = {"line": line_no, "scene": scene_state}
                scene_events.append(event)
                transitions.append(event)
            elif scene_state is None and (match := SCENE_AUDIT.search(line)):
                # An audit identifies the active Unity scene, but is not an event.
                scene_state = match.group(1)
                scene_values.append(scene_state)
                scene_events.append({"line": line_no, "scene": scene_state, "kind": "audit"})
            if match := EYE.search(line):
                eye_state = (int(match.group(1)), int(match.group(2)), float(match.group(3)), match.group(4))
                eye_values.append(eye_state)
                eye_events.append({"line": line_no, "width": eye_state[0], "height": eye_state[1],
                                   "scale": eye_state[2], "stereo": eye_state[3]})
            if match := QUALITY.search(line):
                quality_state = (match.group(1), match.group(2), int(match.group(3)))
                quality_values.append(quality_state)
            if match := HEARTBEAT.search(line):
                tracking_values.append(match.group(1).lower())
                tracking_events.append({"line": line_no, "hmd": match.group(1).lower()})

            if match := FRAME.search(line):
                seconds, count, hz, budget, mean, p50, p95, p99, maximum, mod = match.groups()
                # The values accumulated since the preceding FRAME line describe this
                # completed window. SPLIT/GFX lines after FRAME belong to it instead.
                scene = _state(scene_values)
                eye = _state(eye_values)
                quality = _state(quality_values)
                tracking = _state(tracking_values)
                pose_match = POSE.search(line)
                pose = None
                if pose_match:
                    h, hmin, hmax, d, dmin, dmax = map(float, pose_match.groups())
                    pose = {"height_p50": h, "height_min": hmin, "height_max": hmax,
                            "distance_p50": d, "distance_min": dmin, "distance_max": dmax}
                visible_match = VISIBLE.search(line)
                current = {
                    "index": len(windows) + 1, "line": line_no, "duration_s": float(seconds),
                    "frames": int(count), "display_hz": float(hz), "budget_ms": float(budget),
                    "mean_ms": float(mean), "p50_ms": float(p50), "p95_ms": float(p95),
                    "p99_ms": float(p99), "max_ms": float(maximum), "mod_ms": float(mod),
                    "logic_ms": None, "render_ms": None, "blocked_ms": None,
                    "scene": scene, "tracking": tracking, "tracking_samples": len(tracking_values),
                    "scene_events": scene_events, "eye_events": eye_events,
                    "tracking_events": tracking_events,
                    "eye": None if isinstance(eye, str) else {"width": eye[0], "height": eye[1],
                          "scale": eye[2], "stereo": eye[3]},
                    "eye_status": eye if isinstance(eye, str) else "known",
                    "quality": None if isinstance(quality, str) else {"level": quality[0],
                               "shadows": quality[1], "msaa": quality[2]},
                    "quality_status": quality if isinstance(quality, str) else "known",
                    "pose": pose, "visible_p50": int(visible_match.group(1)) if visible_match else None,
                    "gpu_busy_ms": None,
                }
                windows.append(current)
                scene_values = [scene_state] if scene_state else []
                eye_values = [eye_state] if eye_state else []
                quality_values = [quality_state] if quality_state else []
                tracking_values = []
                tracking_events = []
                scene_events = []
                eye_events = []
                continue

            if current and (match := SPLIT.search(line)):
                current["logic_ms"], current["render_ms"], current["blocked_ms"] = map(float, match.groups())
            # GFX is emitted after FRAME and is only a snapshot at the window's
            # end. It seeds later windows but cannot prove the preceding window
            # ran at that setting throughout.

    return {"path": str(path), "mod_build": build, "commit": commit,
            "scene_transitions": transitions,
            "gpu_busy_available": False, "windows": windows}


def compare(a: dict, b: dict, same_file: bool = False) -> dict:
    reject: list[str] = []
    flags: list[str] = []
    for key in ("scene", "tracking"):
        if a[key] in ("unknown", "mixed") or b[key] in ("unknown", "mixed"):
            reject.append(f"{key}_unknown_or_mixed")
        elif a[key] != b[key]:
            reject.append(f"{key}_mismatch")
    if a["tracking"] != "tracked" or b["tracking"] != "tracked":
        reject.append("hmd_not_tracked")
    if min(a["tracking_samples"], b["tracking_samples"]) < 2:
        flags.append("sparse_tracking_samples")
    for key in ("eye", "quality"):
        if a[key] is None or b[key] is None or a[f"{key}_status"] == "mixed" or b[f"{key}_status"] == "mixed":
            reject.append(f"{key}_unknown_or_mixed")
        elif a[key] != b[key]:
            reject.append(f"{key}_mismatch")
    if a["scene"] == "ProcGen" and b["scene"] == "ProcGen" and not same_file:
        flags.append("scenario_identity_unverified_across_logs")
    if a["pose"] is None or b["pose"] is None:
        reject.append("pose_unavailable")
    else:
        dh = abs(a["pose"]["height_p50"] - b["pose"]["height_p50"])
        dd = abs(a["pose"]["distance_p50"] - b["pose"]["distance_p50"])
        if dh > 3 or dd > 5:
            reject.append("pose_mismatch")
        for window in (a, b):
            pose = window["pose"]
            if pose["height_max"] - pose["height_min"] > 5 or pose["distance_max"] - pose["distance_min"] > 5:
                reject.append("pose_not_stable")
                break
    if a["display_hz"] != b["display_hz"]:
        reject.append("refresh_rate_differs")
    if a["logic_ms"] is None or b["logic_ms"] is None:
        flags.append("split_missing")
    return {"baseline_window": a["index"], "candidate_window": b["index"],
            "comparable": not reject, "reject": sorted(set(reject)), "flags": sorted(set(flags)),
            "mean_delta_ms": round(b["mean_ms"] - a["mean_ms"], 2) if not reject else None,
            "p95_delta_ms": round(b["p95_ms"] - a["p95_ms"], 2) if not reject else None}


def _table(report: dict) -> str:
    lines = [f"{report['path']}: ModBuild {report['mod_build'] or '?'} commit {report['commit'] or '?'}",
             "# line scene tracking(n) eye scale stereo Hz mean p50 p95 mod logic render blocked visible"]
    for w in report["windows"]:
        eye = w["eye"]
        eye_text = f"{eye['width']}x{eye['height']} {eye['scale']:.2f} {eye['stereo']}" if eye else w["eye_status"]
        number = lambda value: "?" if value is None else f"{value:.2f}"
        lines.append(f"{w['index']} {w['line']} {w['scene']} {w['tracking']}({w['tracking_samples']}) "
                     f"{eye_text} {w['display_hz']:.0f} {w['mean_ms']:.2f} {w['p50_ms']:.2f} "
                     f"{w['p95_ms']:.2f} {w['mod_ms']:.2f} {number(w['logic_ms'])} "
                     f"{number(w['render_ms'])} {number(w['blocked_ms'])} "
                     f"{w['visible_p50'] if w['visible_p50'] is not None else '?'}")
    return "\n".join(lines)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("logs", nargs="+", type=Path, help="one or two LogOutput.log files")
    parser.add_argument("--format", choices=("table", "json"), default="table")
    parser.add_argument("--compare", metavar="BASE:CANDIDATE", help="one-based window indices, requires two logs")
    args = parser.parse_args()
    if len(args.logs) > 2 or (args.compare and len(args.logs) != 2):
        parser.error("provide one or two logs; --compare requires exactly two")
    reports = [read_log(path) for path in args.logs]
    result = {"reports": reports, "comparison": None,
              "note": "XR gpu is not GPU busy time; no per-line timestamps exist in LogOutput.log."}
    if args.compare:
        try:
            ai, bi = (int(value) - 1 for value in args.compare.split(":"))
            if ai < 0 or bi < 0:
                raise ValueError
            result["comparison"] = compare(reports[0]["windows"][ai], reports[1]["windows"][bi],
                                           args.logs[0].resolve() == args.logs[1].resolve())
        except (ValueError, IndexError):
            parser.error("--compare needs valid one-based BASE:CANDIDATE window indices")
    if args.format == "json":
        print(json.dumps(result, indent=2, ensure_ascii=False))
    else:
        print("\n\n".join(_table(report) for report in reports))
        if result["comparison"]:
            print("\ncomparison: " + json.dumps(result["comparison"], ensure_ascii=False))
        print("\nXR gpu is not GPU busy time; LogOutput.log has no per-line timestamps.")


if __name__ == "__main__":
    main()
