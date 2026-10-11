#!/usr/bin/env python3
"""Exercise production resident population and author election with explicit engine boundaries."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

ROOT = Path(__file__).resolve().parents[1]
FILES = {
    "ActivityMotion.cs": "src/GloomhavenVR/WorldUI/TownServices/TownServiceActivityMotion.cs",
    "MotionClips.cs": "src/GloomhavenVR/WorldUI/TownServices/TownServiceMotionClips.cs",
    "MotionClips.Data.cs": "src/GloomhavenVR/WorldUI/TownServices/TownServiceMotionClips.Data.cs",
    "Handover.cs": "src/GloomhavenVR/WorldUI/TownServices/TownServiceActivityHandover.cs",
    "Population.cs": "src/GloomhavenVR/WorldUI/TownServices/TownServicePopulation.cs",
    "Remote.cs": "src/GloomhavenVR/Net/Remote/RemoteTownResidents.cs",
    "State.cs": "src/GloomhavenVR/Net/TownResidentsState.cs",
    "RemoteActivity.cs": "src/GloomhavenVR/Net/Remote/RemoteTownActivities.cs",
    "RemoteFace.cs": "src/GloomhavenVR/Net/Remote/RemoteTownFaces.cs",
    "RemotePerformance.cs": "src/GloomhavenVR/Net/Remote/RemoteTownPerformance.cs",
    "FaceTypes.cs": "src/GloomhavenVR/Net/TownFaceState.cs",
}

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-root", type=Path, default=ROOT)
    parser.add_argument("--negative-control", action="append", default=[], help="Run production and named focused controls")
    args = parser.parse_args()
    sources = {name: (args.source_root / source).read_text() for name, source in FILES.items()}
    sources["ActivityTypes.cs"] = (args.source_root / "src/GloomhavenVR/Net/TownActivityState.cs").read_text()
    protocol = (args.source_root / "src/GloomhavenVR/Net/NetProtocol.cs").read_text()
    if "public const float StaleTimeoutSeconds = 3f;" not in protocol:
        raise SystemExit("Production stale timeout changed; update the explicit fixture boundary")
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    env = dict(os.environ, DOTNET_ROOT=str(Path(dotnet).resolve().parent))
    with tempfile.TemporaryDirectory(prefix="ghvr-residents-") as scratch:
        folder = Path(scratch)
        (folder / "Test.csproj").write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>')
        for file in (ROOT / "scripts/town-residents-runtime").glob("*.cs"):
            shutil.copyfile(file, folder / file.name)
        variants = [
            ("baseline", None, None, None),
            ("merchant offer misses its resident", "Population.cs", "if (service == 1 && interactive) engaged |=", "if (service == 0 && interactive) engaged |="),
            ("remote owner offer ignored", "Population.cs", "|| TownServiceMirror.RemoteMerchantOffering", "|| false"),
            ("enchantress hand waits for native cue", "Population.cs",
             "lookingAtVisitor = engaged;",
             "lookingAtVisitor = engaged; if (service == 3) engaged &= TownServiceEnhancementHandoff.HasVisibleCue || TownServiceMirror.HasVisibleRemoteEnhancementCue();"),
            ("visitor-only regression", "Population.cs",
             "bool used = TownServiceAvailability.ShouldPublish(unlocked, enabled, visiting);",
             "bool used = TownServiceAvailability.ShouldPublish(unlocked, false, visiting);"),
            ("viewer floor overrides author", "Population.cs", "RefreshEnvironment(!follows)", "RefreshEnvironment(true)"),
            ("premature input and visibility", "Population.cs", "resident.Station.IsReady", "true"),
            ("viewer misses author grounding", "Population.cs", "resident.Station.SetGrounding(pose.ActorFloorOffset, pose.FurnitureBottom);", ""),
            ("viewer retains ahead facial clock", "Population.cs", "_faceClock = remoteFace.Clock;", "_faceClock = Mathf.Max(_faceClock, remoteFace.Clock);"),
            ("follower elects local facial target", "Population.cs", "IsFaceAuthor = !follows && enabled;", "IsFaceAuthor = enabled;"),
            ("opted out observer authors faces", "Population.cs", "SampleFace(IsFaceAuthor, hasFace", "SampleFace(!follows, hasFace"),
            ("clockless revision invalidates shared packet", "Population.cs",
                "? 0f : state.TransitionAge;", "? float.PositiveInfinity : state.TransitionAge;"),
            ("temple unchanged revision replays blessing", "Population.cs", "bool committed = known && unchecked((int)(revision - previous)) > 0;", "bool committed = known;"),
            ("story commitment leaves resident input", "Population.cs", "bool interactive = enabled && !StoryComposite.PointOfNoReturn;", "bool interactive = enabled;"),
            ("unavailable approach passes through available pose", "Population.cs", "!resident.TempleAvailabilityObserved", "false"),
            ("late unavailable hydration snaps the cover pose", "Population.cs",
                "resident.TempleDirectCover = displayedActivity.Attention <= .05f;",
                "resident.TempleDirectCover = true;"),
            ("temple cover interrupts blessing tail", "Population.cs", "bool coverUnavailable = unavailable && !blessingVisible;", "bool coverUnavailable = unavailable;"),
            ("temple cover starts before last mote", "ActivityMotion.cs", "internal const float TempleBlessingVisualSeconds = 4.20f;", "internal const float TempleBlessingVisualSeconds = 2.45f;"),
            ("settled activity rejected by old wire bound", "ActivityTypes.cs", "p.TransitionAge > TownActivityPose.TransitionSeconds", "p.TransitionAge > .65f"),
            ("follower derives local priestess availability", "Population.cs", "if (IsFaceAuthor)\n                {\n                    TownServiceMirror.TryTemplePresentationState", "if (IsFaceAuthor || hasActivity)\n                {\n                    TownServiceMirror.TryTemplePresentationState"),
            ("follower loses shared blessing", "Population.cs", "resident.TempleBlessingGeneration = remoteActivity.TempleBlessingGeneration;", "resident.TempleBlessingGeneration = 0;"),
            ("author fill never published", "Population.cs", "if (IsFaceAuthor) TownServiceLighting.SampleEnvironment(_frame!.transform, ref activities);", "if (!IsFaceAuthor) TownServiceLighting.SampleEnvironment(_frame!.transform, ref activities);"),
            ("observer never applies shared fill", "Population.cs", "else if (hasActivity) TownServiceLighting.ApplyEnvironment(_frame!.transform, in remoteActivity);", "else if (!hasActivity) TownServiceLighting.ApplyEnvironment(_frame!.transform, in remoteActivity);"),
            ("stale author never expires", "Remote.cs", "now - pair.Value.Received <= NetProtocol.StaleTimeoutSeconds", "true"),
        ]
        if args.negative_control:
            known = {v[0] for v in variants}
            unknown = set(args.negative_control) - known
            if unknown: parser.error("Unknown controls: " + ", ".join(sorted(unknown)))
            variants = [v for v in variants if v[1] is None or v[0] in args.negative_control]
        for label, file, before, after in variants:
            variant = dict(sources)
            if file:
                if before not in variant[file]: raise SystemExit("Production binding changed: " + label)
                variant[file] = variant[file].replace(before, after)
            for name, source in variant.items(): (folder / name).write_text(source)
            run = subprocess.run([dotnet, "run", "--project", str(folder / "Test.csproj"), "-c", "Release"], env=env, capture_output=True, text=True)
            if file is None:
                print(run.stdout, end="")
                if run.returncode: raise SystemExit(run.stdout + run.stderr)
            elif run.returncode == 0 or "error CS" in run.stdout:
                raise SystemExit("Negative control did not fail at runtime: " + label + "\n" + run.stdout + run.stderr)
        print(f"Town residents: {len(variants)-1} compiled negative controls passed")

if __name__ == "__main__": main()
