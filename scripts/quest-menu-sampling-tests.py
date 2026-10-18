#!/usr/bin/env python3
"""Render production Quest menu filtering and compare spinner geometry with native uGUI.

Runs a small real Unity graphics fixture, not the game or an Android build. Optional
owned assets are copied into the fixture; canonical references are read-only.
"""
import argparse
import ctypes
from itertools import chain
import hashlib
import json
import os
from pathlib import Path
import re
import signal
import shutil
import subprocess
import sys
import tempfile
import time

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / "scripts"))
from town_run_retention import cleanup_completed_run
_pending_run = None


def method(source, signature):
    assert source.count(signature) == 1, "production signature drift: " + signature
    start = source.index(signature)
    end = source.index("\n    }\n", start) + len("\n    }\n")
    return source[start:end]


def reap_fixture_group(group):
    """Stop/reap this dedicated subreaper's fixture descendants before cleanup.

    Roslyn may create another session. A process-group-only wait would miss it;
    after Unity exits Linux reparents that orphan to this runner, not PID1.
    """
    reaped, adopted = [], set()
    for signum in (signal.SIGTERM, signal.SIGKILL):
        try:
            if group is not None: os.killpg(group, signum)
        except ProcessLookupError: pass
        deadline = time.monotonic() + 3
        while time.monotonic() < deadline:
            # This process launches only the one fixture; every adopted child
            # belongs to it. Recheck PPid immediately before signalling so no
            # shared compiler or other agent's process can become a target.
            for entry in Path("/proc").iterdir():
                if not entry.name.isdigit(): continue
                try:
                    stat = (entry / "stat").read_text().rsplit(")", 1)[1].split()
                    if int(stat[1]) != os.getpid(): continue
                    adopted.add(int(entry.name))
                    os.kill(int(entry.name), signum)
                except (FileNotFoundError, ProcessLookupError, PermissionError):
                    pass
            try:
                child, status = os.waitpid(-1, os.WNOHANG)
            except ChildProcessError:
                return {"ownedProcessGroup": group, "adoptedChildren": sorted(adopted),
                        "reapedChildren": reaped, "remainingChildren": 0}
            if child:
                reaped.append({"pid": child, "status": status})
            else:
                time.sleep(.05)
    raise RuntimeError("Owned Unity fixture descendants could not be reaped before cleanup")

def main():
    global _pending_run
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity", type=Path, default=Path("/home/claw/unity-2021.3.5/Editor/Unity"))
    parser.add_argument("--owned-project", type=Path)
    parser.add_argument("--game-data", type=Path)
    parser.add_argument("--case", help="Run one named fixture while investigating an affected render path.")
    parser.add_argument("--graphics", choices=("glcore", "vulkan"), default="glcore")
    args = parser.parse_args()
    if bool(args.owned_project) != bool(args.game_data):
        parser.error("Owned sprite proof requires both --owned-project and --game-data.")
    output = ROOT / ".planning/debug/quest-menu-sampling"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output.resolve()))
    _pending_run = run
    log = run / "runner.log"
    log.write_text("Quest menu sampling completion evidence: " + str(run) + "\n")
    flat = ROOT / "src/GloomhavenVR/WorldUI/FlatScreen"
    sharp = ROOT / "src/GloomhavenVR/WorldUI/Sharpness"
    loading = (ROOT / "src/GloomhavenVR/WorldUI/LoadingIndicator.cs").read_text()
    layer = loading[loading.index("    private sealed class LayerArt\n"):loading.index("    /// <summary>\n    /// True only during an actual", loading.index("    private sealed class LayerArt\n"))]
    sources = {"Sampling.cs": (sharp / "QuestScreenSampling.cs").read_text(),
               "PrimitiveMesh.cs": (ROOT / "src/GloomhavenVR/Core/QuestPrimitiveMesh.cs").read_text(),
               "TextureCopy.cs": (ROOT / "src/GloomhavenVR/Core/QuestTextureCopy.cs").read_text(),
               "Geometry.cs": (sharp / "LoadingIconGeometry.cs").read_text(),
               "EditorValidation.cs": (ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Editor/QuestSpriteGeometryValidation.cs").read_text()}
    factory = method((flat / "FlatScreenStereo.2.Compositor.cs").read_text(), "    internal static RenderTexture CreateColorRt(")
    sources["Factory.cs"] = "using UnityEngine;\nnamespace GloomhavenVR.WorldUI;\ninternal static class FlatScreenStereo\n{\n" + factory + "}\n"
    sources["Loading.cs"] = ("using System.Collections.Generic;\nusing UnityEngine;\nusing UnityEngine.UI;\nusing GloomhavenVR.Core;\nnamespace GloomhavenVR.WorldUI;\n"
        "internal sealed class LoadingIndicator\n{\nprivate const float SizeMeters=.25f;\nprivate const int CopyMaxPixels=512;\n"
        "private readonly List<Texture> _ownedArtTextures = new();\n"
        "private GameObject? _root = new GameObject(\"fixture-loading-root\");\n" + layer
        + method(loading, "    private static void DestroyObj<")
        + method(loading, "    private static LayerArt? ReadLayer(")
        + method(loading, "    private Transform CreateQuad(")
        + method(loading, "    private Material CreateLayerMaterial(")
        + method(loading, "    private bool AdoptOwnedCopy(")
        + method(loading, "    private static bool CanUsePersistentArt(")
        + "\ninternal static bool PersistentArtAllowed() => CanUsePersistentArt();\n"
        + "internal GameObject Build(Image image, Image basis, bool boot=false) { var art=ReadLayer(image.gameObject,basis.gameObject)!; if(boot && !AdoptOwnedCopy(art)) throw new System.Exception(\"Native boot source copy failed\"); Mesh? mesh=null; return CreateQuad(\"test\",CreateLayerMaterial(art,null),art,0,ref mesh).gameObject; }\n"
        + "internal void ReleaseCopies() { foreach(var texture in _ownedArtTextures) UnityEngine.Object.DestroyImmediate(texture); _ownedArtTextures.Clear(); }\n}\n")
    cached = method(loading, "    private bool TryCachedIconArt(")
    persisted = method(loading, "    private void TryWriteIconCache(")
    assert cached.index("if (!CanUsePersistentArt()) return false;") < cached.index("File.Exists(")
    assert persisted.index("!CanUsePersistentArt()") < persisted.index("File.Exists(")
    assert 'version != "2"' in loading and 'v == "2"' in loading and 'sb.Append("v=2\\n")' in loading
    assert 'quest ? 0f : -OverlayLiftMeters' in loading and '_overlayMaterial.renderQueue = _baseMaterial.renderQueue + 1;' in loading
    stack = (flat / "FlatScreen.2.CameraStack.cs").read_text()
    assert stack.count("QuestScreenSampling.Configure(uiRt);") == 1
    variants = [("production", "", "", "", ""),
        ("quest-copy-crop-lost", "TextureCopy.cs", "new Vector4(scale.x, scale.y, offset.x, offset.y)", "new Vector4(1, 1, 0, 0)", "Quest atlas copy retains crop pixels and alpha"),
        ("quest-builtin-cache-reused", "TextureCopy.cs", "recipe == Recipe", "true", "Quest rejects previous builtin-copy cache"),
        ("quest-copy-state-leaked", "TextureCopy.cs", "GL.sRGBWrite = previousSrgbWrite;", "/* injected missing color state restore */", "Quest copy restores render state"),
        ("quest-mips-removed", "Sampling.cs", "target.useMipMap = true;", "target.useMipMap = false;", "Quest capture has a mip chain"),
        ("desktop-filter-mutated", "Sampling.cs", "if (!QuestStandalonePlatform.Enabled)", "if (QuestStandalonePlatform.Enabled)", "desktop capture defaults preserved"),
        ("animated-mips-stale", "Sampling.cs", "target.autoGenerateMips = true;", "target.autoGenerateMips = false;", "Quest animated captures regenerate their mip chain"),
        ("trilinear-filter-removed", "Sampling.cs", "target.filterMode = FilterMode.Trilinear;", "target.filterMode = FilterMode.Bilinear;", "Quest capture uses trilinear clamped anisotropic sampling"),
        ("native-aspect-discarded", "Geometry.cs", "if (image.preserveAspect &&", "if (false &&", "native Image trim and preserveAspect drawing bounds"),
        ("spinner-crop-normalized", "Loading.cs", "SizeRatio = Mathf.Max(width, height) / units,", "SizeRatio = 1f,", "converted spinner quad preserves original trim scale and pivot"),
        ("spinner-upload-omitted", "Loading.cs", 'QuestPrimitiveMesh.Rebuild(filter.sharedMesh, "loading-" + name)', "(Mesh?)null", "Quest spinner owns explicit vertex color inputs"),
        ("quest-untrusted-spinner-cache-reused", "Loading.cs", "return !QuestStandalonePlatform.Enabled;", "return true;", "Quest boot ignores untrusted persisted ReadPixels artwork"),
        ("spinner-boot-owned-source-lost", "Loading.cs", "art.Tex = rt;", "art.Tex = src;", "Quest boot renderer owns its GPU source through original scene teardown"),
        ("spinner-pivot-lost", "Loading.cs", "vertices[i] += new Vector3(x, y, 0f);", "vertices[i] += Vector3.zero;", "converted spinner quad preserves original trim scale and pivot")]
    if args.owned_project:
        variants.append(("native-trim-padding-discarded", "Geometry.cs", "Vector4 padding = DataUtility.GetPadding(sprite);", "Vector4 padding = Vector4.zero;", "native Image trim and preserveAspect drawing bounds"))
        variants.append(("native-import-size-gate-removed", "EditorValidation.cs", "Close(rect.width, entry.restoredAtlasRect.width, entry.asset);", "/* injected: imported width not validated */", "native imported geometry gate rejects altered receipt"))
    if args.case:
        variants = [row for row in variants if row[0] == args.case]
        if not variants: parser.error("Unknown fixture case.")
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    fixture = ROOT / "tests/QuestMenuSampling.Tests"
    manifest = {"result": str(run / "results.txt"), "managed": str(ROOT / "ressources/GH_Data/Managed"),
                "dependencies": str(run / "dependencies"), "cases": []}
    print("Quest native menu sampling proof: " + str(run), flush=True)
    for name, filename, before, after, expected in variants:
        case = run / name; production = case / "production"; production.mkdir(parents=True)
        for key, source in sources.items():
            if key == filename:
                assert source.count(before) == 1, "mutation binding drift: " + name
                source = source.replace(before, after, 1)
            (production / key).write_text(source)
        project = case / "Runtime.csproj"; shutil.copyfile(fixture / "Runtime.csproj", project)
        assembly = "QuestMenuSampling_" + name.replace("-", "_")
        result = subprocess.run([dotnet, "build", str(project), "-c", "Release", "--nologo", "--verbosity", "quiet",
            "-p:CaseName=" + assembly, "-p:FixtureDir=" + str(fixture), "-p:ProductionDir=" + str(production),
            "-p:UnityManaged=" + str(args.unity.parent / "Data/Managed"),
            "-p:UnityUi=" + str(ROOT / "ressources/GH_Data/Managed/UnityEngine.UI.dll")], capture_output=True, text=True)
        (case / "build.log").write_text(result.stdout + result.stderr)
        if result.returncode:
            raise SystemExit(result.stdout + result.stderr + "\nCompilation failure is not a passing negative control.")
        manifest["cases"].append({"name": name, "dll": str(case / "bin/Release/netstandard2.1" / (assembly + ".dll")), "expected": expected})
    project = run / "unity"; editor = project / "Assets/Editor"; editor.mkdir(parents=True)
    (project / "Assets/Resources").mkdir()
    shutil.copyfile(ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Resources/QuestTextureCopy.shader",
                    project / "Assets/Resources/QuestTextureCopy.shader")
    # Native JsonUtility requires Unity to register nested receipt types during
    # plugin import; loading an otherwise unregistered assembly by reflection
    # alone silently omits arrays of those custom types.
    for case in manifest["cases"]:
        imported = editor / Path(case["dll"]).name
        shutil.copyfile(case["dll"], imported)
        case["dll"] = str(imported)
    (project / "Packages").mkdir(); (project / "ProjectSettings").mkdir()
    shutil.copyfile(ROOT / "scripts/desktop-render-runtime/Editor/InteractionRunner.cs", editor / "InteractionRunner.cs")
    (project / "Packages/manifest.json").write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    owned = {}
    if args.owned_project:
        source = args.owned_project.resolve(strict=True)
        (project / "Assets/Sprite").mkdir(); (project / "Assets/Texture2D").mkdir()
        (project / "Assets/Material").mkdir(); (project / "Assets/Shader").mkdir()
        names = [p.name for p in (source / "Assets/Sprite").glob("Loading*.asset")]
        names.extend(["DLC_Promo_JawsOfTheLion.asset", "DLC_Promo_SoloScenarios_0.asset"])
        wanted = set()
        for name in names:
            path = source / "Assets/Sprite" / name
            wanted.update(re.findall(r"texture: \{fileID: 2800000, guid: ([a-f0-9]{32}),", path.read_text()))
            for original in (path, Path(str(path) + ".meta")):
                target = project / "Assets/Sprite" / original.name
                shutil.copyfile(original, target); owned[str(original)] = hashlib.sha256(original.read_bytes()).hexdigest()
        for relative in ("Material/Narrative dissolve material.mat", "Shader/UI_Dissolve mask.shader"):
            original = source / "Assets" / relative
            if original.suffix == ".mat":
                wanted.update(re.findall(r"m_Texture: \{fileID: 2800000, guid: ([a-f0-9]{32}),", original.read_text()))
            for path in (original, Path(str(original) + ".meta")):
                shutil.copyfile(path, project / "Assets" / Path(relative).parent / path.name)
                owned[str(path)] = hashlib.sha256(path.read_bytes()).hexdigest()
        # The recovered native spinner/DLC textures normally live in Texture2D.
        # Resolve this bounded closure there first; stop when complete instead of
        # reading every unrelated recovered metadata file in a full game project.
        for meta in chain((source / "Assets/Texture2D").glob("*.meta"), (source / "Assets").rglob("*.meta")):
            if not wanted: break
            guid = re.findall(r"^guid: ([a-f0-9]{32})$", meta.read_text(), re.M)
            if guid and guid[0] in wanted:
                for original in (meta, Path(str(meta)[:-5])):
                    target = project / original.relative_to(source)
                    target.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copyfile(original, target)
                    owned[str(original)] = hashlib.sha256(original.read_bytes()).hexdigest()
                wanted.remove(guid[0])
        assert not wanted, "owned sprite texture closure missing: " + str(wanted)
        geometry = source / "QuestStartupEvidence/loading-sprite-geometry.json"
        if geometry.is_file():
            # Full recovery already has native packed Sprite drawing coordinates.
            # Re-running the historical export-crop recipe without its producer
            # manifests is incorrect. Reuse the exact qualified source receipt,
            # requiring all copied original bytes and the PC bank to match.
            receipt = json.loads(geometry.read_text())
            assert receipt["sourceSha256"] == hashlib.sha256((args.game_data / "resources.assets").read_bytes()).hexdigest()
            for row in receipt["assets"]:
                asset = project / row["asset"]
                assert hashlib.sha256(asset.read_bytes()).hexdigest() == row["restoredSha256"]
                assert hashlib.sha256(Path(str(asset) + ".meta").read_bytes()).hexdigest() == row["metaSha256"]
            target = project / "QuestStartupEvidence/loading-sprite-geometry.json"
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copyfile(geometry, target)
            owned[str(geometry)] = hashlib.sha256(geometry.read_bytes()).hexdigest()
        else:
            sys.path.insert(0, str(ROOT / "tools/quest-builder"))
            import sprites
            sprites.restore_loading_sprite_geometry(project, args.game_data)
    manifest_path = run / "manifest.json"; manifest_path.write_text(json.dumps(manifest, indent=2))
    (run / "source-hashes.json").write_text(json.dumps({"sources": {k: hashlib.sha256(v.encode()).hexdigest() for k, v in sources.items()}, "ownedSources": owned,
        "graphicsApi": args.graphics,
        "limits": ["Real Unity host camera/texture pixels and native uGUI geometry; no Quest GPU or final headset readability claim."]}, indent=2))
    command = [str(args.unity), "-batchmode", "-force-" + args.graphics, "-projectPath", str(project),
               "-executeMethod", "InteractionRunner.Start", "-interactionManifest", str(manifest_path), "-logFile", str(run / "unity.log")]
    if not os.environ.get("DISPLAY"):
        command = ["xvfb-run", "-a"] + command
    print(log.read_text(), end="", flush=True)
    code = 1
    if sys.platform.startswith("linux"):
        if ctypes.CDLL(None, use_errno=True).prctl(36, 1, 0, 0, 0) != 0:
            raise OSError(ctypes.get_errno(), "Could not own Unity fixture child cleanup")
    process = subprocess.Popen(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT,
                               start_new_session=os.name == "posix")
    try:
        code = process.wait(timeout=300)
    finally:
        if process.poll() is None:
            process.kill(); process.wait()
        children = reap_fixture_group(process.pid)
        (run / "process-cleanup.json").write_text(json.dumps(children, indent=2) + "\n")
        for path, expected in owned.items():
            assert hashlib.sha256(Path(path).read_bytes()).hexdigest() == expected, "read-only owned input changed: " + path
        cleanup = cleanup_completed_run("quest-menu-sampling", code, log, ROOT, process_reaped=True)
        (run / "cleanup.json").write_text(json.dumps(cleanup, indent=2) + "\n")
    report = Path(manifest["result"])
    if report.is_file():
        print(report.read_text(), end="")
    if code or not report.is_file():
        raise SystemExit("FAIL real Unity menu sampling fixture; inspect " + str(run / "unity.log"))
    print("PASS production menu sampling + " + str(len(variants) - 1) + " defect controls; " + str(run))


if __name__ == "__main__":
    try:
        main()
    finally:
        # Early source/build failures have only reaped subprocess.run children;
        # Unity's own finally above handles any later native process lifetime.
        if _pending_run is not None and not (_pending_run / "cleanup.json").exists():
            cleanup = cleanup_completed_run("quest-menu-sampling", 1, _pending_run / "runner.log", ROOT, process_reaped=True)
            (_pending_run / "cleanup.json").write_text(json.dumps(cleanup, indent=2) + "\n")
