#!/usr/bin/env python3
"""Compile production town occupation timelines, imported arm IK and shared playback cases and run them inside Unity 2021.3.5.

No game launch, network service, source mutation or generated tracked files.
Explicit fixture boundaries are documented in town-activity-runtime/Boundaries.cs.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


def replace_once(source, before, after):
    if source.count(before) != 1:
        raise RuntimeError(f"Production binding drift: expected one occurrence of {before!r}, got {source.count(before)}")
    return source.replace(before, after, 1)


def sources(root):
    base = root / "src/GloomhavenVR/WorldUI/TownServices"
    names = ["TownServiceActivityHandover.cs", "TownServiceActivityMotion.cs", "TownServiceActivityRig.cs", "TownServiceActivityProps.cs", "TownServiceSleeveLining.cs", "TownServiceGrounding.cs", "TownServiceFaceAttention.cs", "TownServiceFaceMotion.cs", "TownServiceFaceRig.cs"]
    names += ["TownServiceMotionClips.cs", "TownServiceMotionClips.Data.cs", "TownServiceLightList.cs", "TownServiceActivitySoundClock.cs", "TownServiceActivityAudio.cs"]
    bound = {name: (base / name).read_text() for name in names}
    bound["RemoteTownActivities.cs"] = (root / "src/GloomhavenVR/Net/Remote/RemoteTownActivities.cs").read_text()
    bound["TownActivityTypes.cs"] = (root / "src/GloomhavenVR/Net/TownActivityState.cs").read_text().split("/// <summary>Additive81:")[0]
    bound["RemoteTownFaces.cs"] = (root / "src/GloomhavenVR/Net/Remote/RemoteTownFaces.cs").read_text()
    bound["RemoteTownPerformance.cs"] = (root / "src/GloomhavenVR/Net/Remote/RemoteTownPerformance.cs").read_text()
    bound["FaceTypes.cs"] = (root / "src/GloomhavenVR/Net/TownFaceState.cs").read_text().split("/// <summary>Additive80:")[0]
    return bound, {name: hashlib.sha256(text.encode()).hexdigest() for name, text in bound.items()}


def mutations():
    return [
        ("audio-ignores-master", "TownServiceActivityAudio.cs", "Mathf.Clamp01(global.MasterVolume / 100f)", "1f", "native master and effects settings both apply live"),
        ("audio-not-spatial", "TownServiceActivityAudio.cs", "source.spatialBlend = 1f", "source.spatialBlend = 0f", "resident foley has continuous linear falloff and no moving-rig Doppler"),
        ("audio-fixed-world-range", "TownServiceActivityAudio.cs", "source.maxDistance = 7f * worldUnitsPerMetre;", "source.maxDistance = 7f;", "resident range follows the map's world units per perceived metre"),
        ("audio-leaks-listener", "TownServiceActivityAudio.cs", "HeadEar.Release(_claim);", "// negative: leak shared listener", "local resident effects preference immediately stops foley and releases listener"),
        ("audio-seek-replay", "TownServiceActivitySoundClock.cs", "delta >= -.001f && delta <= .25f", "true", "authority seek stall or hide cannot replay historical contact"),
        ("audio-contact-repeat", "TownServiceActivitySoundClock.cs", "before > .99f && after < .01f", "after < .01f", "each visible coin deposit sounds once independent of frame rate"),
        ("audio-author-replay", "TownServiceActivitySoundClock.cs", "author == _author && epoch == _epoch", "true", "authority seek stall or hide cannot replay historical contact"),
        ("unmirrored-mage-pronation", "TownServiceActivityMotion.cs", "LeftRoll = -65f + (variant == 1 ? -25f : 30f) * shape", "LeftRoll = 65f + (variant == 1 ? -25f : 30f) * shape", "relaxed enchantress palms face inward symmetrically"),
        ("identical-spell-variation", "TownServiceActivityMotion.cs", ".55f + .45f * Variation(block, 11u)", ".80f", "spell experiment strength varies between shared clock blocks"),
        ("constant-spell", "TownServiceActivityMotion.cs", "Cast = cast, LeftCurl", "Cast = 1f, LeftCurl", "visible spell uses an upward-facing palm"),
        ("one-spell-pose", "TownServiceActivityMotion.cs", "int variant = (int)(block % 3u);", "int variant = 0;", "shared clock selects three distinct spell effect modes"),
        ("separated-prayer", "TownServiceActivityMotion.cs", "new Vector3(.012f,height,.20f)", "new Vector3(.035f,height,.20f)", "prayer joins cupped hands at the sternum"),
        ("animated-knee-pole", "TownServiceActivityRig.cs", "Vector3.ProjectOnPlane(_root.TransformDirection(_kneePoles[upperIndex == 6 ? 0 : 1]),direction)", "Vector3.ProjectOnPlane(knee-hip,direction)", "planted knee keeps anatomical forward bend plane"),
        ("zero-weight-stance-snap", "TownServiceActivityRig.cs", "_bodyApplied=true;", "_bodyApplied=true; if(body.Weight<=0f)return;", "planted knee keeps anatomical forward bend plane"),
        ("vertical-casting-palm", "TownServiceActivityRig.cs", "palmFrame = Quaternion.AngleAxis(-roll, fingers) * palmFrame;", "palmFrame = Quaternion.AngleAxis(-roll * (_service == 3 ? .7f + .3f * attention : 1f), fingers) * palmFrame;", "actual casting palm supports the spell from below"),
        ("one-sided-merchant-hip", "TownServiceActivityRig.cs", "Vector3 flatFingers = _service == 1 ? -_root.forward", "Vector3 flatFingers = _service == 1 && side > 0f ? -_root.forward", "actual attentive resident arms and hands remain geometrically mirrored"),
        ("raised-stage-gesture", "TownServiceActivityMotion.cs", "new Vector3(.18f, 1.27f, .17f)", "new Vector3(.18f, 1.70f, .17f)", "spell shaping palm stays below its shoulder"),
        ("frozen-generated-body", "TownServiceMotionClips.cs", "body.Set(i,Rotation(data,a+15+i*4,b+15+i*4,t));", "body.Set(i,Quaternion.identity);", "generated occupation contains real torso movement"),
        ("wrapped-forearm-support", "TownServiceActivityRig.cs", "if (_service != 2) twist = Mathf.DeltaAngle(0f, twist + roll) - roll;", "twist = Mathf.Repeat(twist, 360f);", "actual forearm skin support stays continuous across pronation"),
        ("unplanted-feet", "TownServiceActivityRig.cs", "PlantFoot(6); PlantFoot(9);", "// negative control: uncorrected generated stance", "planted knee keeps anatomical forward bend plane"),
        ("phase-jump", "TownServiceActivityMotion.cs", "state.FromBlend = Blend(in state);", "state.FromBlend = state.Engaged ? 1f : 0f;", "interrupted transition keeps current pose"),
        ("work-runs-while-engaged", "TownServiceActivityMotion.cs", "dt - Integral(in state, state.TransitionAge + dt) + Integral(in state, state.TransitionAge)", "dt", "engaged occupation remains paused"),
        ("ignore-ik", "TownServiceActivityRig.cs", "if (!Ready) return;", "if (Ready) return;", "anatomical palm contacts transformed counter surface"),
        ("thumb-overcurl", "TownServiceActivityRig.cs", "arm.Anatomical ? 38f : 5f", "arm.Anatomical ? 150f : 5f", "anatomical thumb stays inside natural grasp range"),
        ("attentive-counter-bracing", "TownServiceActivityMotion.cs", "new Vector3(.20f, 1.10f, .52f)", "new Vector3(.21f, .959f, .337f)", "attentive priestess hands stay beside her robe and outside the donation bowl"),
        ("low-priestess-shoulders", "TownServiceActivityMotion.cs", "float priestessElbowHeight = 1.23f;", "float priestessElbowHeight = .40f;", "actual priestess upper arms preserve the clavicle shoulder line"),
        ("upturned-priestess-hip", "TownServiceActivityRig.cs", "-_root.up - _root.forward * .2f", "_root.up", "unavailable temple pose transitions continuously without a wrist snap"),
        ("excessive-work-bow", "TownServiceActivityRig.cs", "-6f - terrainLean", "-35f - terrainLean", "work posture does not stack an extreme torso and neck bow"),
        ("ignore-palm-offset", "TownServiceActivityRig.cs", "target -= palmOffset;", "target -= palmOffset * 0f;", "anatomical palm contacts transformed counter surface"),
        ("curl-contact-markers", "TownServiceActivityRig.cs", ' && !t.name.Contains("Tip")', "", "contact markers are not articulated finger joints"),
        ("returning-author-snap", "TownServiceActivityHandover.cs", "_age = 0f;", "_age = Duration;", "returning authority keeps displayed hands at first frame"),
        ("unpaired-sequences", "RemoteTownPerformance.cs", "if (!TownActivityCodec.Matches(in activity, in face)", "if (false", "mismatched sequence cannot partially advance pair"),
        ("stale-sequence", "RemoteTownActivities.cs", "!Newer(state.Sequence, peer.Latest.Sequence)", "false", "older occupation cannot replace current phase"),
        ("magnetic-coin", "TownServiceActivityMotion.cs", "coin == index && t >= 1.04f && t < 2.86f ? 1f : 0f", "coin == index ? grip : 0f", "coin is resting or rigidly gripped, never magnetically attracted"),
        ("paused-coin-in-air", "TownServiceActivityMotion.cs", "float transferProgress = Soft(t, 1.05f, 2.84f);", "float transferProgress = Soft(t < 1.70f ? t : t < 2f ? 1.70f : 1.70f + (t - 2f) * (2.84f - 1.70f) / .84f, 1.05f, 2.84f);", "merchant never parks a pinched coin in midair"),
        ("mid-transfer-greeting", "TownServiceActivityMotion.cs", "return t < .50f || t >= 3.05f;", "return true;", "merchant only greets after releasing the current coin"),
        ("prayer-blocks-bowl", "TownServiceActivityMotion.cs", "new Vector3(.045f, 1.105f, .18f)", "new Vector3(.012f, 1.19f, .20f)", "unavailable donation places both hands over the shared bowl"),
        ("splayed-bowl-cover", "TownServiceActivityMotion.cs", "new Vector3(.045f, 1.105f, .18f)", "new Vector3(.15f, 1.105f, .18f)", "unavailable donation places both hands over the shared bowl"),
        ("availability-cover-pop", "TownServiceActivityMotion.cs", "if (donationAvailable && blend <= 0f) return;", "if (donationAvailable) return;", "available temple returns continuously without dropping the cover pose"),
        ("upturned-bowl-cover", "TownServiceActivityRig.cs", "Vector3.ProjectOnPlane(-_root.up, coverFinger)", "Vector3.ProjectOnPlane(_root.up, coverFinger)", "unavailable donation covers the bowl with both palms facing down"),
        ("merchant-stiff-greeting", "TownServiceActivityMotion.cs", "new Vector3(.17f, .94f, .45f)", "new Vector3(.20f, 1.09f, .20f)", "attentive merchant free hand rests on his hip behind the counter"),
        ("open-sleeve-hem", "TownServiceSleeveLining.cs", "row == 0 ? -.012f", "row == 0 ? -.050f", "inner cuff overlaps the anatomical wrist ahead of the cut"),
        ("open-sleeve-interior", "TownServiceSleeveLining.cs", "int a = i, b = (i + 1) % Segments, c = 4 * Segments + 1;", "int a = i, b = (i + 1) % Segments, c = 4 * Segments;", "shallow cuff diaphragm hides the severed forearm end"),
        ("downward-offering", "TownServiceActivityMotion.cs", "Mathf.Lerp(visual.RightRoll, 180f, t)", "Mathf.Lerp(visual.RightRoll, 0f, t)", "offering palm faces upward"),
        ("coin-detached-from-grip", "TownServiceActivityProps.cs", "Vector3.Lerp(seat, pinch, grip)", "seat", "real coin follows actual pinch or resting seat"),
    ]


def main():
    repo = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--portable", action="store_true", help="Run managed phase/network cases in .NET, without Editor or asset assertions")
    parser.add_argument("--source-root", type=Path, default=repo, help="Production checkout to bind (read only)")
    parser.add_argument("--output-dir", type=Path, default=repo / ".planning/debug/town-activity")
    parser.add_argument("--unity", type=Path, default=Path(os.environ.get("UNITY_PATH", "/home/claw/unity-2021.3.5/Editor/Unity")))
    parser.add_argument("--render", type=Path, help="Optional output folder for actual rig/tool contact images")
    parser.add_argument("--anatomy-export", type=Path, help="Export actual skinned arm/torso triangles over complete cycles and visits")
    parser.add_argument("--anatomy-service", type=int, choices=[1, 2, 3], help="Export geometry for one resident; contact checks still cover all three")
    parser.add_argument("--attention-sequence", action="store_true", help="Render a24fps visitor-interruption transition instead of work cycle")
    parser.add_argument("--render-service", type=int, choices=[1,2,3], help="Render only one resident; all contact checks still run")
    parser.add_argument("--sequence", action="store_true", help="Render complete resident performances at 8 fps")
    parser.add_argument("--book-obj", type=Path, help="Read-only original-game open book OBJ (Blender Z-up export)")
    parser.add_argument("--book-texture", type=Path, help="Read-only original-game book atlas for the diagnostic render")
    parser.add_argument("--bundle", type=Path, help="Optional Linux final-asset bundle for actual prefab binding checks")
    parser.add_argument("--no-negative-controls", action="store_true", help="Quick positive run; not complete validation")
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    fixture = Path(__file__).resolve().parent / "town-activity-runtime"
    if args.portable and (args.bundle or args.render): parser.error("--portable cannot load/render Unity assets")
    if not args.portable and not args.unity.is_file():
        parser.error("Unity 2021.3.5 is required; pass --unity")
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    bound, hashes = sources(args.source_root)
    if args.portable:
        wanted = {"TownServiceMotionClips.cs", "TownServiceMotionClips.Data.cs", "TownServiceActivitySoundClock.cs", "TownServiceActivityMotion.cs", "TownServiceActivityHandover.cs", "TownActivityTypes.cs",
                  "RemoteTownActivities.cs", "RemoteTownFaces.cs", "RemoteTownPerformance.cs", "FaceTypes.cs", "TownServiceFaceMotion.cs"}
        bound = {name: code for name, code in bound.items() if name in wanted}
        face = bound["TownServiceFaceMotion.cs"]
        marker = "    internal static TownFacePose Interpolate("
        if face.count(marker) != 1: raise SystemExit("Production face interpolation binding drift")
        bound["TownServiceFaceMotion.cs"] = "using GloomhavenVR.Net; using UnityEngine; namespace GloomhavenVR.WorldUI;\ninternal static class TownServiceFaceMotion {\n" + face[face.index(marker):]
        hashes = {name: hashlib.sha256(code.encode()).hexdigest() for name, code in bound.items()}
    (run / "source-hashes.json").write_text(json.dumps({"root": str(args.source_root.resolve()), "sha256": hashes}, indent=2) + "\n")
    bundle_hash = None
    if args.bundle:
        if not args.bundle.is_file(): parser.error("--bundle must name the completed Linux validation bundle")
        bundle_hash = hashlib.sha256(args.bundle.read_bytes()).hexdigest()
        (run / "bundle-evidence.json").write_text(json.dumps({"path": str(args.bundle.resolve()),
            "bytes": args.bundle.stat().st_size, "sha256": bundle_hash}, indent=2) + "\n")
    manifest = {"result": str(run / "results.txt"), "cases": []}
    variants = [("production", None, None, None, "")]
    if not args.no_negative_controls:
        # Portable validation omits Unity components exercised by these controls.
        rig_only = ("audio-ignores-master", "audio-not-spatial", "audio-fixed-world-range", "audio-leaks-listener", "unmirrored-mage-pronation", "separated-prayer", "animated-knee-pole", "zero-weight-stance-snap", "raised-stage-gesture", "vertical-casting-palm", "wrapped-forearm-support", "unplanted-feet", "one-sided-merchant-hip", "upturned-priestess-hip", "low-priestess-shoulders", "ignore-ik", "thumb-overcurl", "attentive-counter-bracing", "excessive-work-bow", "ignore-palm-offset", "curl-contact-markers", "open-sleeve-hem", "open-sleeve-interior", "downward-offering", "coin-detached-from-grip")
        # Source-only Unity has no imported resident prefab to observe. The explicit
        # imported-asset run supplies --bundle and executes every one of these controls.
        bundle_only = ("unmirrored-mage-pronation", "attentive-counter-bracing", "unplanted-feet", "wrapped-forearm-support", "separated-prayer", "animated-knee-pole", "zero-weight-stance-snap", "raised-stage-gesture", "vertical-casting-palm", "one-sided-merchant-hip", "upturned-priestess-hip", "low-priestess-shoulders")
        variants += [v for v in mutations() if (not args.portable or v[0] not in rig_only)
            and (args.bundle or v[0] not in bundle_only)]
    # A mutation of an absent production file is not an executable negative control.
    # The full Unity suite retains every rig mutation; portable mode only claims its
    # compiled phase/network sources and must fail loudly if this partition drifts.
    for label, filename, *_ in variants:
        if filename is not None and filename not in bound:
            raise SystemExit(f"Negative control {label} targets an unbound production file: {filename}")
    print(f"Binding production from {args.source_root.resolve()}; evidence: {run}", flush=True)
    for name, filename, before, after, expected in variants:
        build = run / name
        production = build / "production"
        production.mkdir(parents=True)
        for path, text in bound.items():
            if path == filename:
                text = text.replace(before, after) if name == "stale-sequence" else replace_once(text, before, after)
            (production / path).write_text(text.replace("Time.unscaledTime", "FaceClock.Now").replace("Time.unscaledDeltaTime", "FaceClock.Delta"))
        project = build / "Interaction.csproj"
        shutil.copyfile(fixture / "Activity.csproj", project)
        managed = args.unity.parent / "Data/Managed"
        framework = "netstandard2.1"
        if args.portable:
            framework = "net8.0"
            portable = build / "portable"
            portable.mkdir()
            program = (fixture / "Program.cs").read_text().split("    private static void Actual(")[0] + "}\n"
            program = program.replace('        count += HandContacts.Run();\n', '').replace('        count += AudioSourceChecks.Run();\n', '')
            program = program.replace('        string[] args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-faceBundle");\n        if(at>=0)Actual(args[at+1]);\n', '')
            (portable / "Program.cs").write_text(program)
            shutil.copyfile(fixture / "AudioClockChecks.cs", portable / "AudioClockChecks.cs")
            shutil.copyfile(fixture / "PortableBoundaries.cs.in", portable / "Boundaries.cs")
            project.write_text(project.read_text().replace("<TargetFramework>netstandard2.1</TargetFramework>",
                "<TargetFramework>net8.0</TargetFramework><OutputType>Exe</OutputType>")
                .replace('$(FixtureDir)/*.cs', str(portable / '*.cs'))
                .replace('    <Reference Include="$(UnityManaged)/UnityEngine/*.dll" />\n', ''))
        assembly = "TownInteraction_" + name.replace("-", "_")
        command = [dotnet, "build", str(project), "--configuration", "Release", "--nologo", "--verbosity", "quiet",
                   f"-p:CaseName={assembly}", f"-p:FixtureDir={fixture}", f"-p:ProductionDir={production}",
                   f"-p:UnityManaged={managed}"]
        compiled = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (build / "build.log").write_text(compiled.stdout)
        if compiled.returncode:
            print(compiled.stdout)
            raise SystemExit(f"FAIL: {name} did not compile (not a successful negative control)")
        manifest["cases"].append({"name": name, "dll": str(build / "bin/Release" / framework / (assembly + ".dll")), "expected": expected})
        print(f"Compiled {name}", flush=True)
    if args.portable:
        results = []
        for case in manifest["cases"]:
            tested = subprocess.run([dotnet, case["dll"]], capture_output=True, text=True)
            result = tested.stdout + tested.stderr
            if case["expected"]:
                okay = tested.returncode != 0 and case["expected"] in result
            else: okay = tested.returncode == 0
            results.append(("PASS " if okay else "FAIL ") + case["name"] + ": " + result.strip())
            if not okay:
                (run / "results.txt").write_text("\n".join(results))
                raise SystemExit(results[-1])
        (run / "results.txt").write_text("\n".join(results) + "\n")
        print("\n".join(results))
        print(f"PASS: portable phase/network variants; no game assemblies or Unity scene/rig claim; evidence: {run}")
        return
    manifest_path = run / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
    project = run / "unity"
    (project / "Assets/Editor").mkdir(parents=True)
    (project / "Packages").mkdir()
    (project / "ProjectSettings").mkdir()
    shutil.copyfile(fixture / "Editor/InteractionRunner.cs", project / "Assets/Editor/InteractionRunner.cs")
    (project / "Packages/manifest.json").write_text('{"dependencies":{"com.unity.modules.assetbundle":"1.0.0","com.unity.modules.animation":"1.0.0","com.unity.modules.audio":"1.0.0","com.unity.modules.physics":"1.0.0"}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    log = run / "unity.log"
    command = ["xvfb-run", "-a", str(args.unity), "-batchmode", "-nographics", "-projectPath", str(project),
               "-executeMethod", "InteractionRunner.Start", "-interactionManifest", str(manifest_path), "-logFile", str(log)]
    if args.bundle:
        command += ["-faceBundle", str(args.bundle.resolve()), "-faceEvidence", str(run / "actual-prefabs.json")]
    if args.anatomy_export:
        args.anatomy_export.mkdir(parents=True, exist_ok=True)
        command += ["-anatomyExport", str(args.anatomy_export.resolve())]
        if args.anatomy_service: command += ["-anatomyService", str(args.anatomy_service)]
    if args.render:
        args.render.mkdir(parents=True, exist_ok=True)
        command.remove("-nographics")
        command += ["-activityRender", str(args.render.resolve())]
        if args.sequence or args.attention_sequence: command += ["-activitySequence"]
        if args.attention_sequence: command += ["-activityAttentionSequence"]
        if args.render_service: command += ["-activityService", str(args.render_service)]
        if args.book_obj: command += ["-activityBook", str(args.book_obj.resolve())]
        if args.book_texture: command += ["-activityBookTexture", str(args.book_texture.resolve())]
    completed = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=240)
    if args.bundle and hashlib.sha256(args.bundle.read_bytes()).hexdigest() != bundle_hash:
        raise SystemExit("FAIL: bundle changed during validation; rerun against the completed artifact")
    result = Path(manifest["result"])
    if result.exists():
        print(result.read_text(), end="")
    if completed.returncode or not result.exists():
        print(f"FAIL: Unity exit {completed.returncode}; log: {log}")
        raise SystemExit(1)
    print(f"PASS: {len(variants)} production/negative variants; evidence: {run}")


if __name__ == "__main__":
    main()
