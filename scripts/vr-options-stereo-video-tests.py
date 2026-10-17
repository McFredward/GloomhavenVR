#!/usr/bin/env python3
"""Execute the direct Advanced stereo action and its live feedback binding.

Part of the existing vr-options suite. Runtime behavior and Unity row artwork
helpers are named API boundaries; the actual index, BuildLinkRow/Button callback
and complete feedback partial are compiled. No decoder or headset is exercised.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile

sys.dont_write_bytecode = True
from town_run_retention import cleanup_completed_run

ROOT = Path(__file__).resolve().parents[1]


def method(source, signature):
    start = source.index("    " + signature)
    return source[start:source.index("\n    }", start) + len("\n    }")]


def execute(command, log):
    with log.open("wb") as output:
        child = subprocess.Popen(command, stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            child.wait(timeout=90)
        except BaseException:
            try:
                os.killpg(child.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            child.wait()
            raise
    return child.returncode, log.read_text(errors="replace")


def check(args, run):
    base = args.source_root / "src/GloomhavenVR/WorldUI/Options"
    fixture = ROOT / "tests/GloomhavenVR.VROptionsStereoVideoTests"
    paths = [base / name for name in ("VROptionsTab.3.Content.cs", "VROptionsTab.2.Rows.cs", "VROptionsTab.StereoVideo.cs")]
    inputs = {str(path): path.read_bytes() for path in [*paths, *fixture.glob("*.cs"), fixture / "Menu.csproj", Path(__file__)]}
    content, rows, partial = (inputs[str(path)].decode() for path in paths)
    extracted = "using System;\nusing GloomhavenVR.Core;\nusing TMPro;\nusing UnityEngine;\nusing UnityEngine.UI;\nnamespace GloomhavenVR.WorldUI;\ninternal static partial class VROptionsTab {\n"
    extracted += method(content, "private static int BuildAdvancedIndex()") + "\n"
    extracted += method(rows, "private static GameObject BuildLinkRow(") + "\n}\n"
    variants = [("production", partial, extracted, None)]
    if args.self_test:
        for name, old, new, expected, target in [
            ("index-hook", "int rows = BuildStereoVideoCalibrationRow();", "int rows = 0;", "Advanced directly exposes stereo action with both feedback rows", "extracted"),
            ("action-kind", "}, asAction: true);", "}, asAction: false);", "Advanced directly exposes stereo action with both feedback rows", "partial"),
            ("request-callback", "StereoVideoCalibration.RequestStart,", "() => { },", "press calls actual runtime request and repaints status in place", "partial"),
            ("immediate-feedback", "            feedback?.Refresh();", "", "press calls actual runtime request and repaints status in place", "partial"),
            ("async-feedback", "private void Update() => Refresh();", "private void Update() { }", "asynchronous preparation refreshes visible status", "partial"),
            ("localized-action", "_action.text = Loc.StereoVideoCalibrationStart;", '_action.text = "stale";', "language change updates action hint and status without rebuilding", "partial"),
            ("note-height", "layout.minHeight = layout.preferredHeight = height;", "layout.minHeight = layout.preferredHeight = minimum;", "long asynchronous status grows its note instead of overlapping later rows", "partial"),
            ("menu-rebuild", "            feedback?.Refresh();", "            Rebuild();\n            feedback?.Refresh();", "press never rebuilds or navigates the menu", "partial"),
        ]:
            source = extracted if target == "extracted" else partial
            if source.count(old) != 1:
                raise ValueError("Mutation binding drift: " + name)
            changed = source.replace(old, new, 1)
            variants.append((name, partial if target == "extracted" else changed,
                             changed if target == "extracted" else extracted, expected))
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    result = {"source_sha256": {path: hashlib.sha256(value).hexdigest() for path, value in inputs.items()},
              "cases": [], "scope": __doc__.strip()}
    for name, partial_source, projected_source, expected in variants:
        case = run / name
        source = case / "source"
        source.mkdir(parents=True)
        (source / "VROptionsTab.StereoVideo.cs").write_text(partial_source)
        (source / "Options.fixture").write_text(projected_source)
        project = case / "Menu.csproj"
        shutil.copyfile(fixture / project.name, project)
        code, text = execute([dotnet, "build", str(project), "--disable-build-servers", "--nologo", "-v:quiet", "-c", "Release",
                              "-p:UseSharedCompilation=false", "-p:FixtureDir=" + str(fixture), "-p:ProductionDir=" + str(source)], case / "build.log")
        if code:
            raise ValueError("Build failed: " + name + "\n" + text)
        code, text = execute([dotnet, str(case / "bin/Release/net8.0/Menu.dll")], case / "test.log")
        if expected is None:
            if code:
                raise ValueError(text)
            result["assertions"] = json.loads(text)["assertions"]
        elif code == 0 or expected not in text:
            raise ValueError("Control escaped or failed for unrelated reason: " + name + "\n" + text)
        result["cases"].append({"name": name, "exit_code": code, "expected_failure": expected})
    if any(Path(path).read_bytes() != value for path, value in inputs.items()):
        raise ValueError("Source changed during the proof; no stable certification")
    result.update(status="passed", causal_controls=len(variants) - 1)
    return result


def interrupted(_signum, _frame):
    raise KeyboardInterrupt


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=ROOT)
    parser.add_argument("--output-dir", type=Path, default=ROOT / ".planning/debug/vr-options-stereo-video")
    parser.add_argument("--retention-checkout", type=Path, default=ROOT)
    parser.add_argument("--self-test", action="store_true")
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.absolute()))
    signal.signal(signal.SIGTERM, interrupted)
    code, result = 1, {"status": "failed"}
    try:
        result = check(args, run)
        code = 0
    except (Exception, KeyboardInterrupt) as error:
        code = 130 if isinstance(error, KeyboardInterrupt) else 1
        result = {"status": "failed", "error": str(error), "processes_reaped": True}
    finally:
        result["exit_code"] = code
        (run / "result.json").write_text(json.dumps(result, indent=2) + "\n")
        completion = run / "completion.log"
        completion.write_text("All owned build and test processes exited and were reaped.\nevidence: " + str(run) + "\n")
        cleanup = cleanup_completed_run("vr-options-stereo-video", code, completion, args.retention_checkout.absolute(), process_reaped=True)
        (run / "cleanup.json").write_text(json.dumps(cleanup, indent=2) + "\n")
    print(json.dumps({"status": result["status"], "assertions": result.get("assertions", 0),
                      "controls": result.get("causal_controls", 0), "evidence": str(run)}))
    return code


if __name__ == "__main__":
    raise SystemExit(main())
