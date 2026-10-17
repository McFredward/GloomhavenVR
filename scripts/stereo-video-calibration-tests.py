#!/usr/bin/env python3
"""Compile full calibration owner/policy/localization with narrow API substitutes.

This is an offline lifecycle proof, not GPU/Unity or native gameplay execution.
Environment.GetCommandLineArgs is real in each child. All owned build processes
are reaped before the repository's completion-cache cleanup API runs.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import signal
import subprocess
import sys
import tempfile

sys.dont_write_bytecode = True
from town_run_retention import cleanup_completed_run

ROOT = Path(__file__).resolve().parents[1]
FILES = {
    "StereoVideoCalibration.cs": "src/GloomhavenVR/WorldUI/FlatScreen/StereoVideoCalibration.cs",
    "StereoVideoCalibrationPolicy.cs": "src/GloomhavenVR/WorldUI/FlatScreen/StereoVideoCalibrationPolicy.cs",
    "Loc.StereoVideo.cs": "src/GloomhavenVR/Core/Loc/Loc.StereoVideo.cs",
}
FLAG = "--ghvr-stereo-video-calibration"
HOOKS = {
    "Tick": "src/GloomhavenVR/WorldUI/FlatScreen/FlatScreen.1.Core.cs",
    "Shutdown": "src/GloomhavenVR/WorldUI/FlatScreen/FlatScreen.4.Lifecycle.cs",
}


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def run(command, log, timeout=120):
    with log.open("wb") as output:
        process = subprocess.Popen(command, stdout=output, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            process.wait(timeout=timeout)
        except BaseException:
            try:
                os.killpg(process.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            process.wait()
            raise
    return process.returncode, log.read_text(errors="replace")


def interrupted(_signum, _frame):
    raise KeyboardInterrupt


def hook_binding(source, method):
    # One lexical pass masks comments, ordinary/verbatim strings and characters.
    # A copied call in documentation or a diagnostic literal is not an invocation.
    pattern = r'//[^\n]*|/\*.*?\*/|@?"(?:""|\\.|[^"\\])*"|\'(?:\\.|[^\'\\])*\''
    code = re.sub(pattern, lambda m: re.sub(r"[^\n]", " ", m.group()), source, flags=re.S)
    declaration = re.search(r"\bpublic\s+void\s+" + method + r"\s*\(\s*\)\s*\{", code)
    if declaration is None:
        raise ValueError("hook-method: " + method)
    depth, start = 1, declaration.end()
    end = start
    while end < len(code) and depth:
        depth += (code[end] == "{") - (code[end] == "}")
        end += 1
    if depth:
        raise ValueError("hook-method: unmatched braces in " + method)
    body = code[start:end - 1]
    target = "Tick" if method == "Tick" else "Stop"
    invocation = r"\bStereoVideoCalibration\s*\.\s*" + target + r"\s*\(\s*\)\s*;"
    if len(re.findall(invocation, body)) != 1:
        raise ValueError("hook-count: " + method)
    if re.match(r"\s*" + invocation, body) is None:
        raise ValueError("hook-position: " + method + " must enter before native continuation or early gates")
    return {"method": method, "calibration_call": target, "count": 1, "position": "first statement"}


def check_hooks(sources, self_test):
    positive = [hook_binding(source, method) for method, source in sources.items()]
    controls = []
    if self_test:
        for method, source in sources.items():
            call = "StereoVideoCalibration." + ("Tick" if method == "Tick" else "Stop") + "();"
            if source.count(call) != 1:
                raise ValueError("Hook control source drift: " + method)
            # An extra comment is a positive control for correct lexical masking.
            hook_binding(source.replace(call, call + " // " + call), method)
            for name, mutated, expected in [
                ("removed", source.replace(call, ""), "hook-count"),
                ("comment-only", source.replace(call, "// " + call), "hook-count"),
                ("string-only", source.replace(call, 'string fixtureHook = "' + call + '";'), "hook-count"),
                ("wrong-method", source.replace(call, "") + "\nprivate void Unrelated() { " + call + " }", "hook-count"),
                ("conditional", source.replace(call, "if (false) " + call), "hook-position"),
                ("duplicate", source.replace(call, call + " " + call), "hook-count"),
            ]:
                try:
                    hook_binding(mutated, method)
                except ValueError as error:
                    if not str(error).startswith(expected + ":"):
                        raise ValueError("Unrelated hook control failure: " + method + ":" + name) from error
                    controls.append({"method": method, "mutation": name, "detected_by": expected})
                else:
                    raise ValueError("Hook control escaped: " + method + ":" + name)
    return {"bindings": positive, "causal_controls": controls}


def check(args, directory):
    fixture = ROOT / "tests/GloomhavenVR.StereoVideoCalibrationTests"
    paths = {name: args.source_root / relative for name, relative in FILES.items()}
    sources = {name: path.read_text() for name, path in paths.items()}
    hook_paths = {method: args.source_root / relative for method, relative in HOOKS.items()}
    hook_sources = {method: path.read_text() for method, path in hook_paths.items()}
    hook_proof = check_hooks(hook_sources, args.self_test)
    for method, source in hook_sources.items():
        (directory / (method + ".hook-source.cs")).write_text(source)
    input_paths = [*paths.values(), *hook_paths.values(), *fixture.glob("*.cs"), fixture / "Calibration.csproj", Path(__file__)]
    hashes = {str(path): digest(path) for path in input_paths}
    cases = [("production", None, None, None)]
    if args.self_test:
        mutations = [
            ("flag-guard", "StereoVideoCalibration.cs", "if (!_requested || _finished) return;", "if (_finished) return;", "no-flag", [], "no exact flag performs no file, shader or decoder work"),
            ("case-insensitive-argv", "StereoVideoCalibrationPolicy.cs", "StringComparison.Ordinal)", "StringComparison.OrdinalIgnoreCase)", "no-flag", [FLAG.upper()], "no exact flag performs no file, shader or decoder work"),
            ("online-gate", "StereoVideoCalibrationPolicy.cs", "vr && conversion && !online &&", "vr && conversion &&", "gates", [FLAG], "leaving offline menu stops and detaches the decoder"),
            ("first-frame-gate", "StereoVideoCalibration.cs", " || _player.frame < 0", "", "stereo", [FLAG], "first decoded frame gates picture"),
            ("dimensions-gate", "StereoVideoCalibrationPolicy.cs", "width == EyeWidth * (stereo ? 2 : 1) && height == Height", "true", "dimensions", [FLAG], "invalid decoded dimensions refuse before picture creation"),
            ("mono-companion", "StereoVideoCalibrationPolicy.cs", 'stereo ? "calibration-sbs.mp4" : "calibration-mono.mp4"', '"calibration-sbs.mp4"', "mono", [FLAG], "missing shader selects the mono companion"),
            ("prepared-owner", "StereoVideoCalibration.cs", "if (_player != null && player != null && ReferenceEquals(player, _player)) player.Play();", "player.Play();", "errors", [FLAG], "callbacks ignore unowned decoder identity"),
            ("prepared-fake-null", "StereoVideoCalibration.cs", "if (_player != null && player != null && ReferenceEquals(player, _player)) player.Play();", "if (player == _player) player.Play();", "errors", [FLAG], "late queued callbacks cannot resurrect or report an ended decoder"),
            ("error-fake-null", "StereoVideoCalibration.cs", "if (_player != null && player != null && ReferenceEquals(player, _player)) Refuse", "if (player == _player) Refuse", "errors", [FLAG], "late queued callbacks cannot resurrect or report an ended decoder"),
            ("error-unsubscribe", "StereoVideoCalibration.cs", "            _player.errorReceived -= Failed;", "", "errors", [FLAG], "error teardown removes both event subscribers"),
            ("click-owner", "StereoVideoCalibration.cs", "if (StereoVideoCalibration.OwnsClick(gameObject)) StereoVideoCalibration.RequestClose();", "StereoVideoCalibration.Stop();", "stereo", [FLAG], "unowned click cannot close calibration"),
            ("instant-picture", "StereoVideoCalibration.cs", "_group.alpha = _closing ? _fadeFrom * (1f - progress) : progress;", "_group.alpha = 1f;", "stereo", [FLAG], "prepared picture starts at zero alpha"),
            ("surface-dispose", "StereoVideoCalibration.cs", "        _surface?.Dispose();", "", "stereo", [FLAG], "close disposes surface and unregisters pointer canvas"),
            ("external-destroy-cleanup", "StereoVideoCalibration.cs", "            Stop();\n            return;\n        }\n        try", "            return;\n        }\n        try", "external-destroy", [FLAG], "external Unity destruction still disposes managed eye surface"),
            ("premature-finished", "StereoVideoCalibration.cs", "if (_attempted) _finished = true;", "_finished = true;", "shutdown", [FLAG], "shutdown before any attempt still permits the later allowed menu"),
        ]
        for name, file, old, new, scenario, flags, failure in mutations:
            if sources[file].count(old) != 1:
                raise ValueError("Mutation source binding drift: " + name)
            cases.append((name, (file, sources[file].replace(old, new, 1)), (scenario, flags), failure))
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    result = {"source_root": str(args.source_root), "source_sha256": hashes, "cases": [], "assertions": 0,
              "hook_proof": hook_proof,
              "scope": "Full actual production source with API substitutes; no Unity GPU or native gameplay execution"}
    for name, changed, scenario, failure in cases:
        case = directory / name
        production = case / "source"
        production.mkdir(parents=True)
        for file, source in sources.items():
            (production / file).write_text(changed[1] if changed is not None and changed[0] == file else source)
        project = case / "Calibration.csproj"
        shutil.copyfile(fixture / project.name, project)
        command = [dotnet, "build", str(project), "--disable-build-servers", "--nologo", "-v:quiet", "-c", "Release",
                   "-p:UseSharedCompilation=false", "-p:FixtureDir=" + str(fixture), "-p:ProductionDir=" + str(production)]
        code, output = run(command, case / "build.log")
        if code:
            raise ValueError(f"Fixture build failed ({name}): {output}")
        scenarios = [scenario] if scenario is not None else [
            ("policy", []), ("no-flag", []), ("no-flag", [FLAG.upper()]),
            ("no-flag", [FLAG + "=true"]), ("no-flag", [FLAG + "-extra"]),
            *[(mode, [FLAG]) for mode in ("gates", "stereo", "mono", "errors", "dimensions", "shutdown", "external-destroy")],
        ]
        for index, (mode, flags) in enumerate(scenarios):
            log = case / f"{index:02d}-{mode}.log"
            code, output = run([dotnet, str(case / "bin/Release/net8.0/Calibration.dll"), mode, *flags], log, 30)
            entry = {"variant": name, "scenario": mode, "argv": flags, "exit_code": code, "expected_failure": failure}
            if failure is None:
                if code:
                    raise ValueError(f"Production scenario {mode} failed: {output}")
                measured = json.loads(output.strip())
                entry["assertions"] = measured["assertions"]
                result["assertions"] += measured["assertions"]
            elif code == 0 or failure not in output:
                raise ValueError(f"Causal control {name} failed for unrelated reason or escaped: {output}")
            result["cases"].append(entry)
    if hashes != {str(path): digest(path) for path in input_paths}:
        raise ValueError("Actual source changed during the proof; no stable-source certification")
    result.update(status="passed", causal_negative_controls=len(cases) - 1 + len(hook_proof["causal_controls"]),
                  runtime_mutation_controls=len(cases) - 1, hook_mutation_controls=len(hook_proof["causal_controls"]),
                  production_scenarios=sum(item["variant"] == "production" for item in result["cases"]))
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=ROOT)
    parser.add_argument("--output-dir", type=Path, default=ROOT / ".planning/debug/stereo-video/lifecycle")
    parser.add_argument("--retention-checkout", type=Path, default=ROOT)
    parser.add_argument("--self-test", action="store_true", help="Also run causal production-source mutations")
    args = parser.parse_args()
    args.source_root = args.source_root.absolute()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    directory = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.absolute()))
    signal.signal(signal.SIGTERM, interrupted)
    code, result = 1, {"status": "failed"}
    try:
        result = check(args, directory)
        code = 0
    except (Exception, KeyboardInterrupt) as error:
        code = 130 if isinstance(error, KeyboardInterrupt) else 1
        result = {"status": "failed", "error": str(error), "processes_reaped": True}
    finally:
        result["exit_code"] = code
        (directory / "result.json").write_text(json.dumps(result, indent=2) + "\n")
        log = directory / "completion.log"
        log.write_text("All owned fixture processes exited and were reaped.\nevidence: " + str(directory) + "\n")
        cleanup = cleanup_completed_run("stereo-video-calibration", code, log, args.retention_checkout.absolute(), process_reaped=True)
        (directory / "cleanup.json").write_text(json.dumps(cleanup, indent=2) + "\n")
    print(json.dumps({"status": result["status"], "assertions": result.get("assertions", 0),
                      "controls": result.get("causal_negative_controls", 0), "evidence": str(directory)}))
    return code


if __name__ == "__main__":
    raise SystemExit(main())
