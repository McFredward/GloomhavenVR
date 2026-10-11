#!/usr/bin/env python3
"""Exercise the production NPC grant lifecycle with explicit reliable-net boundaries."""
import argparse
import hashlib
import json
import re
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
    boundaries = (fixture / "Boundaries.cs").read_text()
    protocol = (args.source_root / "src/GloomhavenVR/Net/NetProtocol.cs").read_text()
    for name in ("ModBuild", "MsgTownGrant"):
        value = re.search(r"public const (?:int|ushort|byte) " + name + r" = ([^;]+);", protocol)
        if value is None: raise SystemExit("Production protocol constant not found: " + name)
        boundaries = re.sub(r"(internal const (?:int|byte) " + name + r" = )[^;]+;",
                            lambda match: match[1] + value[1] + ";", boundaries)
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    variants = [("production", None, None, "")]
    if not args.no_negative_controls:
        variants += [
            ("stale-own-visible", "return own.Active && own.Session == grant.Session && own.Nonce == grant.Nonce;", "return true;",
             "late old grant cannot resurrect removed local occupant"),
            ("temple-occupation", "!Online || service != 1 && service != 3", "!Online || service < 1 || service > 3",
             "temple callback mutex never represents exclusive NPC occupation"),
            ("native-host-only", "return _coordinatorPlayer > 0;", "return _coordinatorPlayer == HostPlayer && VersionGuard.PeerBuild(HostPlayer) == NetProtocol.ModBuild;",
             "flat host never disables immersive native originals"),
            ("no-coordinator-drain", "? now : now + ClientGrantSeconds;", "? now : now;",
             "guest coordinator discovery drain blocks native commit"),
            ("guest-request-only-server", "hostOnly: _coordinatorPlayer == HostPlayer", "hostOnly: true",
             "flat-host requests never disappear into native OnlyServer delivery"),
            ("late-drain-flat-timeout", "Math.Max(offer.Started, _coordinatorNotBefore)", "offer.Started",
             "offer arriving inside discovery drain never expires before its next grant request"),
            ("receive-time-grant", "offer.LastGrant = now - sentAt < ClientGrantSeconds ? sentAt : 0f;", "offer.LastGrant = now;",
             "queued old reliable reply cannot restart an expired grant from receive time"),
            ("older-reply-overwrites", "if (offer.LastGrant > sentAt) return true;", "/* accept stale sequence */",
             "queued earlier sequence never shortens the recovered current request grant"),
            ("per-frame-coordinator-report", "if (selected == _coordinatorPlayer) return;",
             'VRLog.Note("Net", "mutant repeated coordinator report"); if (selected == _coordinatorPlayer) return;',
             "stable roster keeps one coordinator lifecycle report without per-frame reelection"),
            ("retired-visible-request", "if (current.SentSequences == null || current.SentSequences[slot] != message.Sequence) return true;",
             "/* publish retired own request */",
             "reelected coordinator old request slot cannot recreate visible occupation"),
            ("unelected-grant-sender", "sender != _coordinatorPlayer || VersionGuard.PeerBuild(sender)",
             "false || VersionGuard.PeerBuild(sender)",
             "a compatible unelected sender cannot manufacture native purchase permission"),
        ]
    for case, before, after, expected in variants:
        case_dir = run / case; production = case_dir / "production"; production.mkdir(parents=True)
        for name, original in sources.items():
            text = original
            if name == "TownServiceGrantSync" and before is not None:
                if before not in text: raise SystemExit("Negative control no longer binds: " + case)
                text = text.replace(before, after)
            (production / (name + ".cs")).write_text(text)
        (case_dir / "Boundaries.cs").write_text(boundaries)
        project = case_dir / "Grant.csproj"
        project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems><Nullable>enable</Nullable><NoWarn>CS0649</NoWarn></PropertyGroup><ItemGroup><Compile Include="production/*.cs"/><Compile Include="Boundaries.cs"/><Compile Include="' + str(fixture) + '/*.cs" Exclude="' + str(fixture / "Boundaries.cs") + '"/></ItemGroup></Project>\n')
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
