#!/usr/bin/env python3
"""One focused actual-Unity upload/blit/fence proof; never an XR/Steam Link proof."""
import argparse
import hashlib
import importlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[2]
FIXTURE = Path(__file__).resolve().parent
SOURCES = ("ProbeManifest.cs", "ProbePolicy.cs", "ProbeTextures.cs", "PayloadOutput.cs",
           "RegistrationSource.cs", "RegistrationPairPolicy.cs", "RegistrationTextures.cs")
WIDTH, HEIGHT = 688, 548


def digest(data):
    return hashlib.sha256(data).hexdigest()


def execute(command, log, cwd, timeout=240, environment=None):
    with log.open("w") as output:
        process = subprocess.Popen(command, cwd=cwd, stdout=output, stderr=subprocess.STDOUT,
                                   start_new_session=True, env=environment)
        try:
            return process.wait(timeout=timeout)
        except subprocess.TimeoutExpired:
            os.killpg(process.pid, signal.SIGKILL)
            process.wait()
            return 124
        except BaseException:
            if process.poll() is None:
                os.killpg(process.pid, signal.SIGKILL)
            process.wait()
            raise


def verify_pixels(evidence, probe):
    events = [json.loads(line) for line in (evidence / "runtime.jsonl").read_text().splitlines()]
    assert events[-1]["name"] == "complete", "Production fixture did not complete"
    reports, by_eye = [], {}
    for event in events:
        if event["name"] != "pixels":
            continue
        expected_path, actual_path = (evidence / event[key] for key in ("expected_file", "actual_file"))
        source = json.loads((evidence / event["source_file"]).read_text())
        expected, actual = expected_path.read_bytes(), actual_path.read_bytes()
        assert len(expected) == len(actual) == WIDTH * HEIGHT * 3
        assert digest(expected) == event["expected_sha256"] and digest(actual) == event["actual_sha256"]
        assert source["schema"] == "frame-app-registration-source-v1"
        assert source["session"] == event["session"] and source["pair_id"] == event["pair_id"]
        assert source["eye"] == event["eye"] and source["content_id_meaning"] == "app_owned_pair_id_not_native_video_frame"
        assert source["passthrough_authorized"] is False and source["source_frame_association_proven"] is False
        manifest = source["manifest"]
        clean, image = probe.Image(WIDTH, HEIGHT, expected), probe.Image(WIDTH, HEIGHT, actual)
        color = probe.extract(clean, manifest["placements"]["color"])
        coverage_rgb = probe.extract(clean, manifest["placements"]["coverage"])
        assert all(coverage_rgb[i] == coverage_rgb[i + 1] == coverage_rgb[i + 2]
                   for i in range(0, len(coverage_rgb), 3))
        coverage = coverage_rgb[::3]
        assert digest(color) == manifest["color_sha256"] and digest(coverage) == manifest["coverage_sha256"]
        assert digest(expected) == manifest["canvas_rgb_sha256"]
        wanted = probe.header_for(320, 192, color, coverage, source["session"], source["pair_id"], source["eye"], "straight")
        assert all(probe.sample_header(clean, copy) == wanted for copy in range(3))
        try:
            headers = [probe.sample_header(image, copy) for copy in range(3)]
            recovered = all(value == wanted for value in headers)
        except probe.ProbeError:
            recovered, headers = False, []
        if event["negative"]:
            assert not recovered, "Vertical-flip control escaped the independent header decoder"
        else:
            assert recovered, "All three exact source headers must survive the actual production GPU route"
            errors = [abs(a - b) for a, b in zip(actual, expected)]
            assert max(errors) <= 1 and max(errors) == event["max_error"]
            wrong_eye = "right" if source["eye"] == "left" else "left"
            assert wanted != probe.header_for(320, 192, color, coverage, source["session"], source["pair_id"], wrong_eye, "straight")
            previous = by_eye.get(source["eye"])
            if previous:
                assert previous["pair"] < source["pair_id"] and previous["header"] != wanted
            by_eye[source["eye"]] = {"pair": source["pair_id"], "header": wanted}
        # Lossless small artifacts retain original CPU/GPU bytes, not a screenshot
        # transformed through another graphics/color pipeline. Drop only owned raw
        # intermediates after exact round-trip validation of the retained PNGs.
        for path, data in ((expected_path, expected), (actual_path, actual)):
            encoded = probe.encode_png(probe.Image(WIDTH, HEIGHT, data))
            assert probe.decode_png(encoded).rgb == data
            path.with_suffix(".png").write_bytes(encoded)
            path.unlink()
        reports.append({"eye": source["eye"], "pair_id": source["pair_id"], "negative": event["negative"],
            "three_headers_recovered": recovered, "header_sha256": digest(wanted), "max_code_error": event["max_error"],
            "cpu_rgb_sha256": digest(expected), "gpu_rgb_sha256": digest(actual)})
    assert len([row for row in reports if not row["negative"]]) == 8
    assert set(by_eye) == {"left", "right"}
    first_completion = next(index for index, event in enumerate(events) if event["name"] == "actual_fence_passed")
    prior_textures = {event["texture_id"] for event in events[:first_completion] if event["name"] == "submitted"}
    reused = [event["texture_id"] for event in events[first_completion:] if event["name"] == "submitted"]
    assert len(reused) == 2 and all(identity in prior_textures for identity in reused), "Actual passed-fence texture reuse was not exercised"
    return {"events": events, "pixel_controls": reports, "production_source_headers_recovered": 24,
            "actual_passed_fence_texture_reuses": len(reused),
            "xr_callbacks_executed": False, "native_codec_or_mapping_proven": False, "passthrough_authorized": False}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=ROOT)
    parser.add_argument("--unity", type=Path, default=Path("/home/claw/unity-2021.3.5/Editor/Unity"))
    parser.add_argument("--output-root", type=Path, default=ROOT / ".planning/debug/steamlink-registration-gpu")
    parser.add_argument("--retention-checkout", type=Path, default=ROOT)
    parser.add_argument("--payload-tools", type=Path, required=True,
                        help="Read-only directory containing the existing strict app_payload_probe.py")
    parser.add_argument("--backend", choices=("vulkan", "glcore"), default="vulkan")
    parser.add_argument("--color-space", choices=("linear", "gamma", "both"), default="both")
    parser.add_argument("--reuse-build", type=Path, help="Reuse a prior passing exact-source fixture DLL for a remaining profile")
    args = parser.parse_args()
    args.source_root = args.source_root.resolve()
    assert args.unity.is_file(), "Actual Unity2021.3.5 is required"
    assert shutil.which("xvfb-run"), "The real graphics fixture requires xvfb-run"
    for name in SOURCES:
        assert (args.source_root / "src/GloomhavenVR/Core/SteamLink" / name).is_file(), "Production source not ready: " + name
    sys.path.insert(0, str(args.payload_tools.resolve()))
    probe = importlib.import_module("app_payload_probe")
    sys.path.insert(0, str(args.source_root / "scripts"))
    cleanup = importlib.import_module("town_run_retention").cleanup_completed_run
    args.output_root.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_root.resolve()))
    completion = run / "completion.log"
    completion.write_text("evidence: " + str(run) + "\n")
    production = run / "production"
    production.mkdir()
    hashes = {}
    for name in SOURCES:
        data = (args.source_root / "src/GloomhavenVR/Core/SteamLink" / name).read_bytes()
        (production / name).write_bytes(data)
        hashes[name] = digest(data)
    (run / "source-hashes.json").write_text(json.dumps({"production": hashes,
        "fixture": {str(path.relative_to(FIXTURE)): digest(path.read_bytes()) for path in FIXTURE.rglob("*") if path.is_file()},
        "payload_decoder": digest((args.payload_tools / "app_payload_probe.py").read_bytes())}, indent=2) + "\n")
    project = run / "RegistrationGpu.csproj"
    shutil.copyfile(FIXTURE / "RegistrationGpu.csproj", project)
    code, results = 1, {"scope": "actual Unity production-generator upload/blit/fence; no XR or Steam Link", "profiles": {}}
    try:
        dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
        assembly = run / "SteamLinkRegistrationGpu.dll"
        if args.reuse_build is None:
            command = [dotnet, "build", "--disable-build-servers", str(project), "-c", "Release", "--nologo", "--verbosity", "quiet",
                "--configfile", str(FIXTURE / "NuGet.Config"), "-p:ProductionDir=" + str(production),
                "-p:FixtureDir=" + str(FIXTURE), "-p:UnityManaged=" + str(args.unity.parent / "Data/Managed"), "-p:UseSharedCompilation=false"]
            code = execute(command, run / "build.log", run)
            if code:
                raise RuntimeError("Production fixture compilation failed; see " + str(run / "build.log"))
            shutil.copyfile(run / "bin/Release/netstandard2.1/SteamLinkRegistrationGpu.dll", assembly)
        else:
            previous = args.reuse_build.resolve()
            original = json.loads((previous / "source-hashes.json").read_text())
            assert json.loads((previous / "results.json").read_text())["exit_code"] == 0
            assert original["production"] == hashes, "Prior compile belongs to different production sources"
            assert all(original["fixture"][name] == digest((FIXTURE / name).read_bytes())
                       for name in ("Fixture.cs", "RegistrationGpu.csproj")), "Prior compiled fixture sources differ"
            assert (previous / assembly.name).stat().st_size < 8 * 1024 * 1024
            shutil.copyfile(previous / assembly.name, assembly)
            results["inherited_compile_evidence"] = str(previous)
            code = 0
        receipt = cleanup("steamlink-registration-gpu", code, completion, args.retention_checkout, process_reaped=True)
        (run / "cleanup-build.json").write_text(json.dumps(receipt, indent=2) + "\n")
        profiles = ("linear", "gamma") if args.color_space == "both" else (args.color_space,)
        for profile in profiles:
            evidence = run / profile
            evidence.mkdir()
            unity = run / ("unity-" + profile)
            (unity / "Assets/Editor").mkdir(parents=True)
            (unity / "Packages").mkdir()
            (unity / "ProjectSettings").mkdir()
            shutil.copyfile(FIXTURE / "Editor/RegistrationRunner.cs", unity / "Assets/Editor/RegistrationRunner.cs")
            (unity / "Packages/manifest.json").write_text('{"dependencies":{}}\n')
            (unity / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
            (unity / "ProjectSettings/ProjectSettings.asset").write_text("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!129 &1\nPlayerSettings:\n  m_ObjectHideFlags: 0\n  serializedVersion: 26\n  m_ActiveColorSpace: " + ("1" if profile == "linear" else "0") + "\n")
            command = ["xvfb-run", "-a", "-s", "-screen 0 800x600x24", str(args.unity), "-batchmode", "-force-" + args.backend,
                "-projectPath", str(unity), "-executeMethod", "RegistrationRunner.Start", "-registrationEvidence", str(evidence),
                "-registrationAssembly", str(assembly), "-logFile", str(evidence / "unity.log")]
            environment = os.environ.copy()
            if args.backend == "vulkan" and Path("/usr/share/vulkan/icd.d/intel_icd.json").is_file():
                environment["VK_DRIVER_FILES"] = "/usr/share/vulkan/icd.d/intel_icd.json"
            try:
                native_code = execute(command, evidence / "launcher.log", run, environment=environment)
                code = native_code or 1  # A later failed pixel/header assertion must never record success.
                result_path = evidence / "editor-result.json"
                assert native_code == 0 and result_path.is_file() and json.loads(result_path.read_text())["passed"], "Actual Unity failed: " + str(evidence)
                results["profiles"][profile] = verify_pixels(evidence, probe)
                actual_color = results["profiles"][profile]["events"][0]["detail"]
                assert ("color=Linear" if profile == "linear" else "color=Gamma") in actual_color
                code = 0
            finally:
                receipt = cleanup("steamlink-registration-gpu", code, completion, args.retention_checkout, process_reaped=True)
                (evidence / "cleanup.json").write_text(json.dumps(receipt, indent=2) + "\n")
        code = 0
    finally:
        results["exit_code"] = code
        results["validated_source_root"] = str(args.source_root)
        results["production_changed_during_run"] = [name for name in SOURCES
            if digest((args.source_root / "src/GloomhavenVR/Core/SteamLink" / name).read_bytes()) != hashes[name]]
        (run / "results.json").write_text(json.dumps(results, indent=2, sort_keys=True) + "\n")
        receipt = cleanup("steamlink-registration-gpu", code, completion, args.retention_checkout, process_reaped=True)
        (run / "cleanup.json").write_text(json.dumps(receipt, indent=2) + "\n")
        print("evidence: " + str(run))
    return code


if __name__ == "__main__":
    raise SystemExit(main())
