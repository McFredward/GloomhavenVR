#!/usr/bin/env python3
"""Exercise actual owned movie routing without replacing native playback behavior.

Unity scene/video objects are explicit seams. Actual movie/content code and managed
files run unchanged. This is not Android codec, rendering or stereo hardware proof.
"""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    root = Path(__file__).resolve().parents[1]
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--runtime-source", type=Path, default=root / "unity/GloomhavenVR.Quest/Assets/Quest/Runtime")
    args = parser.parse_args()
    sources = {name: (args.runtime_source / name).read_text() for name in ("QuestGameVideos.cs", "QuestGameContent.cs", "QuestContentHash.cs")}
    mutations = (
        ("wrong-owned-url", "QuestGameVideos.cs", "selected.url = paths[clip.guid];", 'selected.url = "/unowned/wrong.mp4";', "exact-owned-url"),
        ("file-uri-route-restored", "QuestGameVideos.cs", "selected.url = paths[clip.guid];", "selected.url = new Uri(paths[clip.guid]).AbsoluteUri;", "exact-owned-url"),
        ("skip-inactive-player", "QuestGameVideos.cs", "GetComponentsInChildren<VideoPlayer>(true)", "GetComponentsInChildren<VideoPlayer>(false)", "Required original movie player is missing"),
        ("ignore-hierarchy", "QuestGameVideos.cs", " || HierarchyPath(player.transform) != binding.playerPath", "", "exact-hierarchy"),
        ("ignore-source-hash-membership", "QuestGameVideos.cs", " || file.sha256 != clip.sha256", "", "invalid-manifest"),
        ("conflicting-mapping-allowed", "QuestGameVideos.cs", "if (!bindings.Add(binding.scene + \"/\" + binding.playerPath))", "if (!bindings.Add(binding.scene + \"/\" + binding.playerPath) && binding.scene.Length == 0)", "duplicate-player-mapping"),
        ("native-play-replaced", "QuestGameVideos.cs", "selected.url = paths[clip.guid];", "selected.url = paths[clip.guid];\n                selected.Play();", "native-lifecycle-no-play"),
        ("native-flag-changed", "QuestGameVideos.cs", "selected.clip = null;", "selected.clip = null;\n                selected.playOnAwake = true;", "native-video-flags"),
        ("unbounded-prepared", "QuestGameVideos.cs", "bound == null || bound.Prepared", "bound == null", "bounded-prepared"),
        ("unbounded-errors", "QuestGameVideos.cs", "bound.Errors++ >= 2", "bound.Errors++ >= 200", "bounded-errors"),
        ("callback-leak", "QuestGameVideos.cs", "bound.Player.prepareCompleted -= Prepared;", "/* no unhook */", "dispose-unhooks-only-router"),
        ("duplicate-main-hash", "QuestGameVideos.cs", "if (!File.Exists(absolute) || new FileInfo(absolute).Length != file.size)", "if (!File.Exists(absolute) || new FileInfo(absolute).Length != file.size || QuestGameContent.Hash(absolute) != file.sha256)", "duplicate-main-thread-hash"),
        ("worker-unity-api", "QuestGameContent.cs", "public static bool IsReady(QuestGameContentManifest manifest, string root, Action<QuestGameContentProgress> progress = null)\n        {", "public static bool IsReady(QuestGameContentManifest manifest, string root, Action<QuestGameContentProgress> progress = null)\n        {\n            UnityEngine.Debug.Log(\"forbidden worker Unity call\");", "worker-api"),
        ("unchecked-delivery-provenance", "QuestGameVideos.cs", "ValidateProvenance(clip);", "/* unvalidated provenance */", "invalid-derived-provenance"),
        ("derived-claims-original-hash", "QuestGameVideos.cs", "clip.originalSha256 != clip.sha256", "clip.originalSha256.Length == 64", "invalid-derived-provenance"),
        ("stale-bound-source-attribution", "QuestGameVideos.cs", "string url = bound.Player.url ?? string.Empty;", "string url = bound.Path;", "actual-opened-media-"),
        ("unbounded-timed-state", "QuestGameVideos.cs", "bound.StateSamples < 3", "bound.StateSamples < 300", "bounded-timed-state"),
        ("unbounded-native-source-changes", "QuestGameVideos.cs", "bound.SourceChanges < 4", "bound.SourceChanges < 400", "bounded-native-source-changes"),
        ("normal-level-native-state-stream", "QuestGameVideos.cs", "if (GloomhavenVR.Core.QuestStandalonePlatform.DebugLogging)\n                {", "if (true)\n                {", "normal-level-no-native-state-stream"),
        ("dynamic-native-url-overwritten", "QuestGameVideos.cs", "Context = scene.name + \"/\" + binding.playerPath + \" source=authored-dynamic\" };", "Context = scene.name + \"/\" + binding.playerPath + \" source=authored-dynamic\" };\n                selected.url = \"/wrong/narrative.mov\";", "dynamic-native-source-untouched"),
        ("dynamic-player-coverage-omitted", "QuestGameVideos.cs", "foreach (var binding in manifest.dynamicPlayers ?? new QuestGameMovieBinding[0])", "foreach (var binding in new QuestGameMovieBinding[0])", "dynamic-output-adapter-attached"),
        ("destroyed-player-output-leaked", "QuestGameVideos.cs", "if (players[index].Output != null) players[index].Output.Dispose();", "/* missing global output-hook cleanup */", "destroyed-dynamic-output-hooks-released"),
    )
    cases = [("production", sources, ""), ("commented-defects-are-inert", dict(sources, **{
        "QuestGameVideos.cs": "/* selected.Play(); selected.playOnAwake = true; */\n" + sources["QuestGameVideos.cs"]}), "")]
    for name, filename, before, after, expected in mutations:
        if sources[filename].count(before) != 1:
            raise RuntimeError("Mutation binding drift: " + name)
        modified = dict(sources)
        modified[filename] = sources[filename].replace(before, after)
        cases.append((name, modified, expected))
    output = root / ".planning/debug/quest-startup-videos"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    evidence = {"runtimeSource": str(args.runtime_source.resolve()),
                "sources": {name: hashlib.sha256(source.encode()).hexdigest() for name, source in sources.items()},
                "boundary": "actual movie router/content; scene hierarchy and native video property/event seams; no Android codec or stereo proof", "cases": []}
    print("Quest startup movie evidence: " + str(run), flush=True)
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    for name, modified, expected in cases:
        case = run / name
        case.mkdir()
        harness = case / "fixture"
        shutil.copytree(root / "tests/QuestStartupVideos.Tests", harness, ignore=shutil.ignore_patterns("bin", "obj"))
        for filename, source in modified.items():
            (case / filename).write_text(source)
        result = subprocess.run([dotnet, "run", "--project", str(harness / "QuestStartupVideos.Tests.csproj"),
                                 "--configuration", "Release", "--property:RuntimeSource=" + str(case), "--", str(case / "files")],
                                text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=90)
        (case / "console.log").write_text(result.stdout)
        passed = (result.returncode == 0 and "PASS Quest startup videos:" in result.stdout) if not expected else (result.returncode != 0 and expected in result.stdout)
        evidence["cases"].append({"name": name, "exitCode": result.returncode, "expected": expected, "passed": passed})
        (run / "results.json").write_text(json.dumps(evidence, indent=2) + "\n")
        if not passed:
            raise SystemExit("FAIL " + name + "; see " + str(case / "console.log"))
        if expected:
            print("PASS rejected " + name + " at " + expected, flush=True)
        else:
            print(next(line for line in result.stdout.splitlines() if line.startswith("PASS Quest startup videos:")), flush=True)
    print("PASS production movie routing and " + str(len(mutations)) + " defect controls")


if __name__ == "__main__":
    main()
