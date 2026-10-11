#!/usr/bin/env python3
"""Run the original paired donation packet through real Unity presentation endpoints.

The native transaction/ownership transport and final imported NPC anatomy remain
separate evidence. This fixture binds complete original codecs/receivers, the
original station blessing dispatch, face runtime, seeded particles and actual
imported shipped WAVs. It never invokes a gameplay callback.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
TOWN = "src/GloomhavenVR/WorldUI/TownServices/"
FILES = {name + ".cs": TOWN + name + ".cs" for name in (
    "TownServiceActivityMotion", "TownServiceMotionClips", "TownServiceMotionClips.Data",
    "TownServiceActivityAudio", "TownServiceActivitySoundClock", "TownServiceTempleBowlMarker",
    "TownServiceFace", "TownServiceFaceRig", "TownServiceFaceMotion", "TownServiceFaceAttention",
    "TownServiceVoice", "TownServiceVoiceSchedule", "TownServiceVoiceCurve", "TownServiceFaceSpeech")}
FILES.update({name + ".cs": "src/GloomhavenVR/Net/" + name + ".cs"
              for name in ("TownActivityState", "TownFaceState", "NetPacket")})
FILES.update({name + ".cs": "src/GloomhavenVR/Net/Remote/" + name + ".cs"
              for name in ("RemoteTownPerformance", "RemoteTownActivities", "RemoteTownFaces")})
FILES.update({name: "unity/GloomhavenVR.Assets/Assets/Bundle/TownServices/Shaders/" + name
              for name in ("TownNpc.shader", "TownPracticalLighting.cginc", "TownSharedEnvironment.cginc")})
CONTROLS = (
    ("receiver-drops-blessing", "RemoteTownActivities.cs", "state.Clock += elapsed;",
     "state.TempleBlessingGeneration = 0; state.Clock += elapsed;", "received paired packet retains the committed blessing"),
    ("blessing-automatic-clock", "TownServiceTempleBowlMarker.cs", "_blessing.Pause(true);",
     "// negative control: let the local particle clock run", "packet replay particles stay on the shared event clock"),
    ("blessing-audio-zero-age", "TownServiceActivityAudio.cs",
     "source.time = Mathf.Clamp(age, 0f, Mathf.Max(0f, clip.length - .001f));", "source.time = 0f;",
     "received donation Foley starts at the same shared age"),
    ("station-omits-voice-bind", "StationBlessingDispatch.cs",
     "TownServiceVoice.Tick(_service, clock, audible, in shown, lookingAtVisitor);",
     "// negative control: omit original station voice binding",
     "received exact voice curve reaches the same real Unity facial blend shapes"),
    ("donor-loses-focus", "TownServiceFaceAttention.cs", "? _blessedVisitor : 0;", "? 0 : 0;",
     "committed donor wins priestess attention over a closer visitor"),
)


def braced(source, marker):
    if source.count(marker) != 1:
        raise RuntimeError("Production extraction marker drift: " + marker)
    start = source.index(marker)
    opening = source.index("{", start)
    depth = 1
    at = opening + 1
    while depth:
        depth += (source[at] == "{") - (source[at] == "}")
        at += 1
    return source[start:at]


def bound_sources(root):
    result = {name: (root / path).read_text() for name, path in FILES.items()}
    protocol = (root / "src/GloomhavenVR/Net/NetProtocol.cs").read_text()
    constants = []
    for name in ("Magic", "Version", "ExtIdTownFace", "MsgTownFace", "ExtIdTownActivity", "MsgTownActivity", "StaleTimeoutSeconds"):
        match = re.search(r"public const (?:uint|byte|float) " + name + r" = [^;]+;", protocol)
        if not match:
            raise RuntimeError("Missing production protocol constant: " + name)
        constants.append(match[0])
    result["ProtocolConstants.cs"] = "namespace GloomhavenVR.Net; internal static class NetProtocol {\n" + "\n".join(constants) + "\n}\n"
    primitive = (root / "src/GloomhavenVR/Net/Avatar/AvatarSerializer.cs").read_text()
    start = primitive.index("    internal static void WriteU32(")
    end = primitive.index("    // ---- pose primitives", start)
    result["WirePrimitives.cs"] = "namespace GloomhavenVR.Net; internal static unsafe class AvatarSerializer {\n" + primitive[start:end] + "}\n"
    population = (root / (TOWN + "TownServicePopulation.cs")).read_text()
    gate = braced(population, "    internal sealed class TempleBlessingGate")
    result["DonationGate.cs"] = "using System.Collections.Generic; namespace GloomhavenVR.WorldUI; internal static class NativeDonationGate {\n" + gate + "}\n"
    station = (root / (TOWN + "TownServiceStation.cs")).read_text()
    dispatch = braced(station, "    internal void SampleTempleBlessing(")
    dispatch += "\n" + braced(station, "    internal void SampleActivityAudio(")
    result["StationBlessingDispatch.cs"] = "using UnityEngine; using GloomhavenVR.Net; namespace GloomhavenVR.WorldUI; internal sealed class StationBlessingDispatch { readonly TownServiceTempleBowlMarker _templeBlessing; readonly TownServiceActivityAudio _audio; readonly byte _service = 2; readonly bool _activityFailed = false; internal StationBlessingDispatch(TownServiceTempleBowlMarker marker, TownServiceActivityAudio audio) {_templeBlessing=marker; _audio=audio;}\n" + dispatch + "}\n"
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=ROOT)
    parser.add_argument("--negative-control", action="append", choices=[c[0] for c in CONTROLS])
    parser.add_argument("--no-negative-controls", action="store_true")
    args = parser.parse_args()
    output = ROOT / ".planning/debug/town-donation-replay"
    output.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=output))
    bound = bound_sources(args.source_root)
    hashes = {name: hashlib.sha256(text.encode()).hexdigest() for name, text in bound.items()}
    (run / "source-hashes.json").write_text(json.dumps({"root": str(args.source_root.resolve()), "sha256": hashes}, indent=2) + "\n")
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    unity = Path(os.environ.get("UNITY_PATH", "/home/claw/unity-2021.3.5/Editor/Unity"))
    native = ROOT / "ressources/GH_Data/Managed"
    fixture = ROOT / "scripts/town-donation-replay-runtime"
    variants = [("production", None, None, None, "")]
    if not args.no_negative_controls:
        variants += [c for c in CONTROLS if not args.negative_control or c[0] in args.negative_control]
    manifest = {"result": str(run / "results.txt"), "cases": []}
    for name, filename, before, after, expected in variants:
        case = run / name
        prod = case / "production"
        prod.mkdir(parents=True)
        for path, text in bound.items():
            if path == filename:
                if text.count(before) != 1:
                    raise RuntimeError("Negative control binding drift: " + name)
                text = text.replace(before, after, 1)
            (prod / path).write_text(text.replace("Time.unscaledTime", "ReplayClock.Now").replace("Time.unscaledDeltaTime", "ReplayClock.Delta"))
        project = case / "Replay.csproj"
        shutil.copyfile(fixture / "Replay.csproj", project)
        done = subprocess.run([dotnet, "build", str(project), "-c", "Release", "-v", "quiet", "--nologo",
            "-p:CaseName=Donation_" + name.replace("-", "_"), "-p:FixtureDir=" + str(fixture), "-p:ProductionDir=" + str(prod),
            "-p:UnityManaged=" + str(unity.parent / "Data/Managed"), "-p:UnityUi=" + str(native / "UnityEngine.UI.dll"),
            "-p:UnityTmp=" + str(native / "Unity.TextMeshPro.dll")], capture_output=True, text=True)
        (case / "build.log").write_text(done.stdout + done.stderr)
        if done.returncode:
            raise SystemExit(done.stdout + done.stderr)
        manifest["cases"].append({"name": name, "dll": str(case / "bin/Release/netstandard2.1" / ("Donation_" + name.replace("-", "_") + ".dll")), "expected": expected})
        print("Compiled", name, flush=True)
    project = run / "unity"
    (project / "Assets/Editor").mkdir(parents=True)
    (project / "Assets/Speech").mkdir()
    (project / "Assets/Shaders").mkdir()
    for name, text in bound.items():
        if name.endswith((".shader", ".cginc")):
            (project / "Assets/Shaders" / name).write_text(text)
    (project / "Packages").mkdir()
    (project / "ProjectSettings").mkdir()
    shutil.copyfile(ROOT / "scripts/town-service-interaction-runtime/Editor/InteractionRunner.cs", project / "Assets/Editor/InteractionRunner.cs")
    assets = ROOT / "unity/GloomhavenVR.Assets/Assets/Bundle/TownServices/Audio"
    names = ["priestess-donate" + suffix for suffix in ("", "-2", "-3", "-4", "-5")]
    asset_hashes = {}
    for name in names:
        for extension in (".wav", ".json"):
            asset = assets / (name + extension)
            shutil.copyfile(asset, project / "Assets/Speech" / asset.name)
            asset_hashes[asset.name] = hashlib.sha256(asset.read_bytes()).hexdigest()
    shutil.copyfile(assets / "spell-soft-4.wav", project / "Assets/Speech/spell-soft-4.wav")
    asset_hashes["spell-soft-4.wav"] = hashlib.sha256((assets / "spell-soft-4.wav").read_bytes()).hexdigest()
    (run / "asset-hashes.json").write_text(json.dumps(asset_hashes, indent=2) + "\n")
    (project / "Packages/manifest.json").write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    manifest_path = run / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
    result = subprocess.run([str(unity), "-batchmode", "-nographics", "-projectPath", str(project),
        "-executeMethod", "InteractionRunner.Start", "-interactionManifest", str(manifest_path),
        "-logFile", str(run / "unity.log")], timeout=240, stdout=subprocess.DEVNULL)
    print((run / "results.txt").read_text() if (run / "results.txt").exists() else "No Unity results")
    print("Evidence:", run)
    return result.returncode


if __name__ == "__main__":
    raise SystemExit(main())
