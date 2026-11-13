#!/usr/bin/env python3
"""Run the production Quest movie lease and consuming-capture predicate.

Unity objects/native decoder and the selected finite movie source are explicit
property seams. This verifies ownership and native alpha lifetime, not GPU pixels
or Android camera-plane latch timing; the headset remains a separate gate.
"""
import hashlib
import json
import os
from pathlib import Path
import shutil
import signal
import subprocess
import sys
import tempfile

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
from town_run_retention import cleanup_completed_run


def member(source, signature):
    start = source.index(signature)
    opening = source.index("{", start)
    depth, end = 1, opening + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


def main():
    output = ROOT / ".planning/debug/quest-movie-plane-lease"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    completion = run / "completion.log"
    completion.write_text("evidence: " + str(run) + "\n")
    source = ROOT / "src/GloomhavenVR/WorldUI/FlatScreen"
    lease = (source / "NativeMovieCameraPlane.cs").read_text()
    stack = (source / "FlatScreen.2.CameraStack.cs").read_text()
    ownership = "using GloomhavenVR.Core; using UnityEngine;\nnamespace GloomhavenVR.WorldUI;\ninternal sealed partial class FlatScreen\n{\n" + member(stack, "    internal static bool OwnsCapturedMoviePlane(") + "\n}\n"
    # Source-bound producer closure: the pre-Play ambient helper and the normal
    # complete capture both reach the same owner; unknown late targets remain out.
    movies = (source / "NativeVideoMovies.cs").read_text()
    ambient = member(movies, "    internal static void SetAmbientUrl(")
    assert ambient.index("RegisterCameraPlane(player);") < ambient.index("FlatScreen.PrepareQuestMovie(player);")
    capture = member(stack, "    private void CaptureStack()\n")
    assert capture.rstrip().endswith("NativeMovieCameraPlane.RefreshQuestMovie();\n    }")
    prepare = member(stack, "    internal static void PrepareQuestMovie(")
    assert "captured.Owner != owner) return;" in prepare and "try { owner.CaptureStack(); }" in prepare
    cases = [("production", lease, ownership, "")]
    mutations = [
        ("lost-persistent-admission", "lease", "if (!QuestStandalonePlatform.Enabled) return;", "if (QuestStandalonePlatform.Enabled) return;", "surviving packed studio source is suppressed across the next render without rerunning Play or prefix"),
        ("early-post-render-release", "lease", "    internal static void AfterRender(Camera camera)\n    {", "    internal static void AfterRender(Camera camera)\n    {\n        RestoreAll();", "surviving packed studio source is suppressed across the next render without rerunning Play or prefix"),
        ("foreign-target-admitted", "owner", "camera.targetTexture == owner.TargetFor(captured)", "true", "unsafe Quest movie ownership restores exact original alpha: foreign-target"),
        ("foreign-decoder-admitted", "owner", "owner._stereo.OwnsStartupMovie(player, owner._screenMaterial)", "true", "unsafe Quest movie ownership restores exact original alpha: decoder-identity"),
        ("source-revision-not-rebound", "lease", " || player.url != _startupUrl", "", "source revision is rebound before another native render"),
        ("failed-source-reacquired", "lease", "|| ReferenceEquals(player, _failedPlayer) && player.url == _failedUrl", "|| false", "failed unchanged decoder is not silently reacquired on the next capture"),
        ("native-fade-discarded", "lease", "_startupAlpha = player.targetCameraAlpha; // Preserve a later native fade.", "/* injected: discard later original alpha */", "later authored fade updates the persistent eye alpha"),
    ]
    for name, kind, before, after, expected in mutations:
        original = lease if kind == "lease" else ownership
        assert original.count(before) == 1, name
        changed = original.replace(before, after, 1)
        cases.append((name, changed if kind == "lease" else lease, changed if kind == "owner" else ownership, expected))
    results = {"boundary": __doc__, "sources": {str(p.relative_to(ROOT)): hashlib.sha256(p.read_bytes()).hexdigest()
               for p in (source / "NativeMovieCameraPlane.cs", source / "FlatScreen.2.CameraStack.cs", source / "NativeVideoMovies.cs")}, "cases": []}
    status = 1
    print("Quest movie lease evidence: " + str(run), flush=True)
    try:
        for name, code, predicate, expected in cases:
            case = run / name
            case.mkdir()
            (case / "Lease.cs").write_text(code)
            (case / "Ownership.cs").write_text(predicate)
            shutil.copyfile(ROOT / "tests/QuestMoviePlaneLease.Tests/QuestMoviePlaneLease.Tests.csproj", case / "Fixture.csproj")
            command = [shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet"), "run", "--project", str(case / "Fixture.csproj"), "-c", "Release",
                "-p:UseSharedCompilation=false", "-p:FixtureSource=" + str(ROOT / "tests/QuestMoviePlaneLease.Tests"),
                "-p:LeaseSource=" + str(case / "Lease.cs"), "-p:OwnershipSource=" + str(case / "Ownership.cs")]
            with (case / "console.log").open("w") as stream:
                process = subprocess.Popen(command, stdout=stream, stderr=subprocess.STDOUT, start_new_session=True,
                    env=dict(os.environ, MSBUILDDISABLENODEREUSE="1", DOTNET_CLI_USE_MSBUILD_SERVER="0"))
                try:
                    returncode = process.wait(timeout=60)
                finally:
                    if process.poll() is None:
                        os.killpg(process.pid, signal.SIGKILL)
                        process.wait()
            text = (case / "console.log").read_text()
            passed = returncode == 0 and "PASS " in text if not expected else returncode != 0 and expected in text
            results["cases"].append({"name": name, "exitCode": returncode, "passed": passed, "expected": expected})
            (run / "results.json").write_text(json.dumps(results, indent=2) + "\n")
            if not passed:
                raise RuntimeError("Quest movie plane case failed: " + str(case) + "\n" + text)
            print(next(line for line in text.splitlines() if line.startswith("PASS ")) if not expected else "PASS rejected " + name, flush=True)
        status = 0
    finally:
        result = cleanup_completed_run("quest-movie-plane-lease", status, completion, ROOT, process_reaped=True)
        (run / "cache-cleanup.json").write_text(json.dumps(result, indent=2) + "\n")


if __name__ == "__main__":
    main()
