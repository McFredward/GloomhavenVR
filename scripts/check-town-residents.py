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
    "Population.cs": "src/GloomhavenVR/WorldUI/TownServices/TownServicePopulation.cs",
    "Remote.cs": "src/GloomhavenVR/Net/Remote/RemoteTownResidents.cs",
    "State.cs": "src/GloomhavenVR/Net/TownResidentsState.cs",
}

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-root", type=Path, default=ROOT)
    args = parser.parse_args()
    sources = {name: (args.source_root / source).read_text() for name, source in FILES.items()}
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
            ("visitor-only regression", "Population.cs", "bool used = enabled || visiting;", "bool used = visiting;"),
            ("viewer floor overrides author", "Population.cs", "RefreshEnvironment(!follows)", "RefreshEnvironment(true)"),
            ("premature input and visibility", "Population.cs", "resident.Station.IsReady", "true"),
            ("stale author never expires", "Remote.cs", "now - pair.Value.Received <= NetProtocol.StaleTimeoutSeconds", "true"),
        ]
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
