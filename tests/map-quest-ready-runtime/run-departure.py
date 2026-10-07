#!/usr/bin/env python3
"""Portable native departure proof; optional actual HarmonyX registration under Mono."""
from pathlib import Path
import argparse
import hashlib
import json
import os
import re
import shutil
import subprocess
import tempfile


def extract(source, signature):
    start = source.index(signature)
    opening = source.index("{", start)
    depth, end = 1, opening + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--real-harmony", action="store_true",
                        help="Run the optional seven-target production registration proof under Mono.")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[2]
    fixture_dir = root / "tests/map-quest-ready-runtime"
    production = root / "src/GloomhavenVR/WorldUI/MapRoom/MapQuestDepartureValidation.cs"
    common = Path(subprocess.check_output(
        ["git", "-C", str(root), "rev-parse", "--path-format=absolute", "--git-common-dir"],
        text=True).strip())
    native_root = Path(os.environ.get("GHVR_NATIVE_SOURCE_ROOT", str(common.parent / "decompiled/GH.Runtime")))
    fixture = fixture_dir / "NativeDepartureFixture.cs"
    fixture_text = fixture.read_text()
    signatures = (
        "public bool ShouldBeVisible", "public void Initialize(", "public void ToggleVisibility(",
        "public void SetInteractable(", "private void UpdateVisiblity(", "private bool IsReadyUpForbidden(",
        "public void ReadyUp(bool toggledOn, bool autoValidateUnreadying)", "private bool ReadyUpPlayer(",
        "private bool UnreadyPlayer(", "private void OnReadiedPlayersChanged(", "private void OnPlayerLeft(",
        "private IEnumerator<float> WaitForStateSyncBeforeProceeding(", "public void ResetPlayerACKs(",
        "public void Reset(", "private void Proceed(", "public void WaitForPlayerBeforeProceeding(",
        "private void InputToggle(", "private bool UseProgressForInput(",
        "private void OnEndAnimationProgressBar(", "public void CancelProgress(",
        "public void ProxySetReadyState(", "public void ServerACKSyncedStateRevision(",
    )
    hashes = {str(path): hashlib.sha256(path.read_bytes()).hexdigest() for path in (fixture, production)}
    if native_root.exists():
        source_path = native_root / "UIReadyToggle.cs"
        source = source_path.read_text()
        for signature in signatures:
            assert extract(source, signature) in fixture_text, "Native departure fixture differs: " + signature
        hashes[str(source_path)] = hashlib.sha256(source_path.read_bytes()).hexdigest()
        print("Native departure fixture: 22 verbatim method/property bodies match the read-only game source.", flush=True)
    else:
        print("Native departure fixture: read-only source unavailable; executing the committed native bodies.", flush=True)

    env = os.environ.copy()
    env["PATH"] = str(Path(env.get("DOTNET_ROOT", str(Path.home() / ".dotnet")))) + os.pathsep + env["PATH"]
    mono = None
    if args.real_harmony:
        configured = env.get("GHVR_UNITY_MONO")
        if configured:
            mono = Path(configured)
            if not mono.is_file():
                raise SystemExit("GHVR_UNITY_MONO does not name an available Mono executable.")
        else:
            candidates = sorted(Path.home().glob("unity-*/Editor/Data/MonoBleedingEdge/bin/mono"))
            if candidates:
                mono = candidates[-1]
            elif shutil.which("mono", path=env["PATH"]):
                mono = Path(shutil.which("mono", path=env["PATH"]))
        if mono is None:
            print("SKIP: optional actual HarmonyX registration proof requires Mono; no runtime pass claimed.")
            return

    with tempfile.TemporaryDirectory(prefix="ghvr-native-departure-") as temporary:
        out = Path(temporary)
        (out / "MapQuestDepartureValidation.cs").write_bytes(production.read_bytes())
        (out / "Native.cs").write_text(fixture_text)
        (out / "source-hashes.json").write_text(json.dumps(hashes, indent=2) + "\n")
        properties = [f"-p:FixtureDir={fixture_dir}", f"-p:ProductionDir={out}",
                      f"-p:NativeSource={out / 'Native.cs'}", f"-p:BaseIntermediateOutputPath={out / 'obj'}/",
                      f"-p:OutputPath={out / 'bin'}/"]
        project = fixture_dir / "Departure.csproj"
        if args.real_harmony:
            subprocess.run(["dotnet", "build", str(project), "-c", "Release", "-v", "quiet",
                            "-p:RealHarmony=true", *properties], env=env, check=True)
            subprocess.run([str(mono), str(out / "bin/Departure.exe")], env=env, check=True)
            return

        # Rebuild and launch the exact private output, so rapid linked-source mutations
        # cannot satisfy a control by running an older binary.
        build = ["dotnet", "build", str(project), "-c", "Release", "-v", "quiet",
                 "--no-incremental", *properties]
        execute = ["dotnet", str(out / "bin/Departure.dll")]
        subprocess.run(build, env=env, check=True)
        subprocess.run(execute, env=env, check=True)
        original = production.read_text()
        controls = {
            "native-departure-off": (
                "            validateReadyUpOnPlayerLeft = true;",
                "            validateReadyUpOnPlayerLeft = false;",
                "native quest departure option starts original ACK coroutine after participant departure"),
            "unscoped-withdrawal": (
                "            if (toggledOn || _explicitInputDepth == 0)",
                "            if (toggledOn)",
                "non-input ReadyUp never inherits departure withdrawal authorization"),
            "lost-async-input": (
                "                    _progressAuthorized = MatchesDeparture(__instance) && __instance.IsProgressingBar;",
                "                _progressAuthorized = false;",
                "original delayed Cancel completion retains exactly its genuine user authorization"),
            "cancelled-progress-leak": (
                "            if (ReferenceEquals(__instance, _departureToggle))\n"
                "                _progressAuthorized = false;",
                "            if (ReferenceEquals(__instance, _departureToggle)) { }",
                "aborted native Cancel progress loses its departure authorization"),
            "wrong-controller": (
                "        && ReferenceEquals(Controller(), _departureController)\n", "",
                "changed context or denied input cannot borrow a departure withdrawal"),
            "wrong-quest": (
                "        && string.Equals(Controller()?.HostSelectedQuest?.ID, _departureQuest, StringComparison.Ordinal)\n", "",
                "changed context or denied input cannot borrow a departure withdrawal"),
            "nonmap-initialize": (
                "                if (phase != ActionPhaseType.MapHQ && phase != ActionPhaseType.MapAtLinkedScenario)\n"
                "                    return;\n", "",
                "departure validation excludes VR-off/offline/player/nonquest/nonmap phases"),
        }
        for name, signature, expected in (
            ("unsafe-initialize", "private static void BeforeInitialize(", "native Initialize continues after failed departure probe"),
            ("unsafe-left-prefix", "private static void BeforePlayerLeft(", "native OnPlayerLeft continues after failed prefix probe"),
            ("unsafe-left-postfix", "private static void AfterPlayerLeft(", "native OnPlayerLeft continues after failed postfix probe"),
            ("unsafe-input-prefix", "private static void BeforeExplicitInput(", "native InputToggle continues after failed departure probe"),
            ("unsafe-input-finalizer", "private static Exception? AfterExplicitInput(", "native InputToggle returns after failed progress-state probe"),
            ("unsafe-progress-prefix", "private static void BeforeProgressEnd(", "native progress completion continues after failed departure probe"),
            ("unsafe-ready-prefix", "private static void BeforeReadyUp(", "native ReadyUp continues after failed departure probe"),
            ("unsafe-probe-logger", "private static void ProbeFailed(", "failed diagnostic logger cannot escape native input"),
        ):
            method = extract(original, signature)
            changed, count = re.subn(r"catch \(Exception(?: e)?\)\s*\{[^}]*\}", "catch (Exception) { throw; }", method, count=1)
            assert count == 1, "Production exception guard seam changed: " + name
            controls[name] = (method, changed, expected)
        for name, (needle, replacement, expected) in controls.items():
            assert original.count(needle) == 1, "Production departure mutation seam changed: " + name
            (out / "MapQuestDepartureValidation.cs").write_text(original.replace(needle, replacement))
            compiled = subprocess.run(build, env=env, text=True, capture_output=True)
            if compiled.returncode:
                raise SystemExit(compiled.stdout + compiled.stderr + "\nFAIL: departure control failed during compilation: " + name)
            result = subprocess.run(execute, env=env, text=True, capture_output=True)
            log = result.stdout + result.stderr
            (out / (name + ".log")).write_text(log)
            if result.returncode == 0 or "System.InvalidOperationException: " + expected not in log:
                raise SystemExit(log + "\nFAIL: departure control did not reach its causal assertion: " + name)
            print("Native quest departure negative control: " + name + " rejected.", flush=True)


if __name__ == "__main__":
    main()
