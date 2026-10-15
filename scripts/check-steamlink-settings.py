#!/usr/bin/env python3
"""Exercise the integrated Steam Link request with real BepInEx config persistence.

This tests settings lifetime/boot safety, not active XR rendering or receiver passthrough.
Each fresh process executes the production binder; causal controls remove its reset or
bind-once guard. Evidence is retained and generated build caches are cleaned on completion.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile

from town_run_retention import cleanup_completed_run

ROOT = Path(__file__).resolve().parents[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output-dir", type=Path, default=ROOT / ".planning/debug/steamlink-settings")
    parser.add_argument("--bepinex-assembly", type=Path, default=Path.home() / ".nuget/packages/bepinex.baselib/5.4.20/lib/netstandard2.0/BepInEx.dll")
    parser.add_argument("--game-managed", type=Path, help="managed references; defaults to local game or committed CI metadata")
    parser.add_argument("--self-test", action="store_true", help="also execute two causal failure controls")
    args = parser.parse_args()
    managed = args.game_managed or ROOT / "ressources/GH_Data/Managed"
    if not (managed / "UnityEngine.CoreModule.dll").is_file():
        managed = ROOT / "libs/RefAsm"
    if not args.bepinex_assembly.is_file():
        parser.error("BepInEx runtime assembly is missing; build GloomhavenVR once to restore its existing dependencies, or pass --bepinex-assembly.")
    if not (managed / "UnityEngine.CoreModule.dll").is_file():
        parser.error("Unity metadata is missing from both the local game and libs/RefAsm.")
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    completion = run / "completion.log"
    fixture = ROOT / "tests/GloomhavenVR.SteamLinkSettingsTests"
    config_path = ROOT / "src/GloomhavenVR/Core/SteamLink/SteamLinkMaskConfig.cs"
    defaults_path = ROOT / "src/GloomhavenVR/Defaults/Defaults.SteamLink.cs"
    config = config_path.read_text()
    variants = [("production", config, None)]
    if args.self_test:
        for name, old, new, failure in [
            ("no-boot-reset", "        SetRequested(false);", "", "first bind clears saved true before output can start"),
            ("reset-every-bind", "        if (_file != null)\n            return;", "", "catalog rebind does not discard a fresh request or create another config"),
        ]:
            if config.count(old) != 1:
                raise ValueError("Causal settings binding drift: " + name)
            variants.append((name, config.replace(old, new, 1), failure))
    inputs = (config_path, defaults_path, fixture / "Program.cs", fixture / "GloomhavenVR.SteamLinkSettingsTests.csproj", args.bepinex_assembly, managed / "UnityEngine.CoreModule.dll")
    result = {"evidence": str(run),
              "source_sha256": {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in inputs},
              "cases": []}
    status = 1
    try:
        dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
        for name, source, expected_failure in variants:
            case = run / name
            case.mkdir()
            (case / "SteamLinkMaskConfig.cs").write_text(source)
            shutil.copyfile(defaults_path, case / defaults_path.name)
            project = case / "GloomhavenVR.SteamLinkSettingsTests.csproj"
            shutil.copyfile(fixture / project.name, project)
            build = subprocess.run([dotnet, "build", str(project), "--disable-build-servers", "--nologo", "-v:quiet", "-c", "Release",
                                    "-p:UseSharedCompilation=false",
                                    "-p:ProductionDir=" + str(case), "-p:FixtureDir=" + str(fixture),
                                    "-p:BepInExAssembly=" + str(args.bepinex_assembly.resolve()),
                                    "-p:GameManaged=" + str(managed.resolve())],
                                   capture_output=True, text=True, timeout=90)
            (case / "build.log").write_text(build.stdout + build.stderr)
            if build.returncode:
                raise ValueError("Settings fixture build failed: " + name + "\n" + build.stdout + build.stderr)
            modes = ["fresh", "saved-on"] if expected_failure is None else ["saved-on"]
            for mode in modes:
                scenario = case / mode
                scenario.mkdir()
                executed = subprocess.run([dotnet, str(case / "bin/Release/net8.0/GloomhavenVR.SteamLinkSettingsTests.dll"), str(scenario), mode], capture_output=True, text=True, timeout=30)
                output = executed.stdout + executed.stderr
                (scenario / "result.log").write_text(output)
                if expected_failure is None and executed.returncode:
                    raise ValueError(output)
                if expected_failure is not None and (executed.returncode == 0 or expected_failure not in output):
                    raise ValueError("Causal settings failure was not detected: " + name + "\n" + output)
                print(output.strip() if expected_failure is None else "PASS causal settings control: " + name)
                result["cases"].append({"name": name, "mode": mode, "exit_code": executed.returncode, "expected_failure": expected_failure})
        status = 0
        return 0
    finally:
        result["exit_code"] = status
        (run / "results.json").write_text(json.dumps(result, indent=2))
        completion.write_text("Evidence: " + str(run) + "\n")
        cleanup = cleanup_completed_run("steamlink-settings", status, completion, ROOT, process_reaped=True)
        (run / "cleanup.json").write_text(json.dumps(cleanup, indent=2))
        print("Evidence: " + str(run))


if __name__ == "__main__":
    raise SystemExit(main())
