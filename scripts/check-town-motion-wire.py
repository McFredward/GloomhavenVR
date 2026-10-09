#!/usr/bin/env python3
"""Focused production motion codec, four-peer saturation, and old-queue negative control."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import xml.sax.saxutils


def main():
    root = Path(__file__).resolve().parent.parent
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--native-front", type=Path, help="captured-original-front.gvr from the motion-fast Unity fixture")
    p.add_argument("--output-dir", type=Path, default=root / ".planning/debug/town-motion-wire")
    p.add_argument("--old-queue", action="store_true", help="Measure the actual base dev queue as a negative control")
    args = p.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    names = ["src/GloomhavenVR/Net/TownServices/TownServiceMotionCodec.cs",
             "src/GloomhavenVR/Net/TownServices/TownServiceMotionCodec.OfferedFrame.cs",
             "src/GloomhavenVR/Net/TownServices/TownServiceFastNumbers.cs",
             "src/GloomhavenVR/Net/TownServices/TownServiceMotionBudget.cs",
             "tests/GloomhavenVR.WireTests/TownMotionVectors.cs",
             "tests/GloomhavenVR.WireTests/TownOfferedFrameVectors.cs",
             "tests/GloomhavenVR.WireTests/TownCardReturnCohortVectors.cs"]
    stamp = {name: hashlib.sha256((root / name).read_bytes()).hexdigest() for name in names}
    stamp["src/GloomhavenVR/Net/TownServices/TownServiceSendQueue.cs"] = hashlib.sha256((root / "src/GloomhavenVR/Net/TownServices/TownServiceSendQueue.cs").read_bytes()).hexdigest()
    stamp["src/GloomhavenVR/Net/ExtrasSendQueue.cs"] = hashlib.sha256((root / "src/GloomhavenVR/Net/ExtrasSendQueue.cs").read_bytes()).hexdigest()
    (run / "source-hashes.json").write_text(json.dumps(stamp, indent=2) + "\n")
    for variant in (["production", "base-queue"] if args.old_queue else ["production"]):
        case = run / variant; case.mkdir()
        q = xml.sax.saxutils.escape
        lines = ["<Project><PropertyGroup><StartupObject>GloomhavenVR.WireTests.TownMotionProgram</StartupObject></PropertyGroup>",
                 '<Target Name="FocusedTownMotionSources" BeforeTargets="CoreCompile"><ItemGroup>']
        for name in names:
            lines.append(f'<Compile Remove="{q(str(root / name))}"/><Compile Include="{q(str(root / name))}"/>')
        if variant == "base-queue":
            old = subprocess.check_output(["git", "show", "3edbcb28:src/GloomhavenVR/Net/TownServices/TownServiceSendQueue.cs"], cwd=root)
            source = case / "OriginalTownServiceSendQueue.cs"; source.write_bytes(old)
            lines.append(f'<Compile Remove="{q(str(root / "src/GloomhavenVR/Net/TownServices/TownServiceSendQueue.cs"))}"/><Compile Include="{q(str(source))}"/>')
            (case / "source.sha256").write_text(hashlib.sha256(old).hexdigest() + "\n")
            # The prior queue and its original global scheduler form one control.
            # A new direct urgent turn cannot be compiled against that old queue.
            name = "src/GloomhavenVR/Net/ExtrasSendQueue.cs"
            old_scheduler = subprocess.check_output(["git", "show", "3edbcb28:" + name], cwd=root)
            scheduler = case / "OriginalExtrasSendQueue.cs"; scheduler.write_bytes(old_scheduler)
            lines.append(f'<Compile Remove="{q(str(root / name))}"/><Compile Include="{q(str(scheduler))}"/>')
            (case / "scheduler.sha256").write_text(hashlib.sha256(old_scheduler).hexdigest() + "\n")
        lines.append("</ItemGroup></Target></Project>")
        overlay = case / "Focused.targets"; overlay.write_text("\n".join(lines))
        build = [dotnet, "build", str(root / "tests/GloomhavenVR.WireTests/GloomhavenVR.WireTests.csproj"), "-c", "Release", "--nologo", "--verbosity", "quiet", "-p:StartupObject=GloomhavenVR.WireTests.TownMotionProgram",
                 f"-p:CustomAfterMicrosoftCommonTargets={overlay}", f"-p:OutputPath={case / 'bin'}/",
                 f"-p:BaseIntermediateOutputPath={case / 'obj'}/"]
        result = subprocess.run(build, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (case / "build.log").write_text(result.stdout)
        if result.returncode: print(result.stdout); raise SystemExit("FAIL focused compilation: " + variant)
        command = [dotnet, str(case / "bin/GloomhavenVR.WireTests.dll")]
        if args.native_front: command.append(str(args.native_front.resolve()))
        result = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (case / "result.txt").write_text(result.stdout); print(variant + ":\n" + result.stdout, end="")
        if variant == "production" and result.returncode: raise SystemExit("FAIL production motion wire")
        if variant == "base-queue" and "cold priority fronts share the dedicated loss-safe urgent bundle stream" not in result.stdout:
            raise SystemExit("FAIL old queue negative control did not expose absent urgent native grouping")
    print("PASS focused town motion wire; evidence: " + str(run))


if __name__ == "__main__":
    main()
