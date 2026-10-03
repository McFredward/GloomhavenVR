#!/usr/bin/env python3
"""Exercise the production NPC grant lifecycle with explicit reliable-net boundaries."""
import argparse
import hashlib
import json
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    repo = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=repo)
    parser.add_argument("--output-dir", type=Path, default=repo / ".planning/debug/town-grant-lifecycle")
    parser.add_argument("--no-negative-controls", action="store_true")
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    names = ["TownServiceGrantSync", "TownServiceGrantCodec", "TownServiceGrantLedger"]
    hashes, sources = {}, {}
    for name in names:
        text = (args.source_root / "src/GloomhavenVR/Net/TownServices" / (name + ".cs")).read_text()
        sources[name] = text
        hashes[name] = hashlib.sha256(text.encode()).hexdigest()
    (run / "source-hashes.json").write_text(json.dumps(hashes, indent=2) + "\n")
    fixture = repo / "scripts/town-grant-lifecycle-runtime"
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    variants = [("production", None, None, "")]
    if not args.no_negative_controls:
        variants += [
            ("stale-own-visible", "return own.Active && own.Session == grant.Session && own.Nonce == grant.Nonce;", "return true;",
             "late old grant cannot resurrect removed local occupant"),
            ("temple-occupation", "!Online || service != 1 && service != 3", "!Online || service < 1 || service > 3",
             "temple callback mutex never represents exclusive NPC occupation"),
        ]
    for case, before, after, expected in variants:
        case_dir = run / case; production = case_dir / "production"; production.mkdir(parents=True)
        for name, original in sources.items():
            text = original
            if name == "TownServiceGrantSync" and before is not None:
                if before not in text: raise SystemExit("Negative control no longer binds: " + case)
                text = text.replace(before, after)
            (production / (name + ".cs")).write_text(text)
        project = case_dir / "Grant.csproj"
        project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Nullable>enable</Nullable><NoWarn>CS0649</NoWarn></PropertyGroup><ItemGroup><Compile Include="production/*.cs"/><Compile Include="' + str(fixture) + '/*.cs"/></ItemGroup></Project>\n')
        done = subprocess.run([dotnet, "run", "--project", str(project), "-c", "Release", "--nologo"], text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (run / (case + ".log")).write_text(done.stdout)
        if expected:
            if done.returncode == 0 or expected not in done.stdout: raise SystemExit("Negative control failed to detect " + case + ":\n" + done.stdout)
            print("Detected negative control: " + case)
        elif done.returncode:
            raise SystemExit(done.stdout)
        else: print(done.stdout)
    print("Evidence: " + str(run))


if __name__ == "__main__": main()
