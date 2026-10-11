#!/usr/bin/env python3
"""Prove shared resident presentation converges with production elections and grants."""
import argparse
import hashlib
import json
from pathlib import Path
import re
import shutil
import subprocess
import tempfile
from xml.sax.saxutils import escape


def method(source, signature):
    """Retain the complete production body, using the same bounded extraction as other proofs."""
    start = source.index("    " + signature)
    opening = source.index("{", start)
    depth = 1
    cursor = opening + 1
    while depth:
        if source[cursor] == "{":
            depth += 1
        elif source[cursor] == "}":
            depth -= 1
        cursor += 1
    return source[start:cursor]


# Original pre-review ranking, isolated from its separate sticky-cache defect.
ORIGINAL_AGE_CANDIDATE = """    private static int LivePresentationCandidate(byte service, float now, bool requireTransaction,
        out uint sessionId)
    {
        int owner = 0; sessionId = 0; float oldestAge = float.NegativeInfinity;
        if (LiveInteraction(LocalPeer, service, PrivateLane.Session, now, requireTransaction))
        { owner = LocalPeer; sessionId = PrivateLane.Session; oldestAge = Mathf.Max(0f, now - PrivateLane.Started); }
        foreach (var pair in VisitorSessions)
        {
            if (!LiveInteraction(pair.Key, service, pair.Value.Session, now, requireTransaction)) continue;
            float age = pair.Value.SessionAge + Mathf.Max(0f, now - pair.Value.ReceivedTime);
            if (owner == 0 || age > oldestAge + .05f
                || Mathf.Abs(age - oldestAge) <= .05f && pair.Key < owner)
            { owner = pair.Key; sessionId = pair.Value.Session; oldestAge = age; }
        }
        return owner;
    }"""


def main():
    repo = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=repo)
    parser.add_argument("--output-dir", type=Path, default=repo / ".planning/debug/town-presentation-election")
    parser.add_argument("--no-negative-controls", action="store_true")
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    base = args.source_root / "src/GloomhavenVR/Net"
    source = (base / "TownServices/TownServiceMirror.cs").read_text()
    signatures = [
        "internal static int InteractionOwner(", "internal static int TransactionOwner(",
        "private static int LivePresentationCandidate(", "private static bool LiveInteraction(",
        "internal static bool LocalOwnsInteraction(", "internal static bool CanLocalBeginTransaction(",
        "internal static bool IsInteractionOwner(", "private static void ResetInteractionLeases(",
    ]
    declarations_start = source.index("    private sealed class InteractionLease")
    declarations_end = source.index(";", source.index("    private const float InteractionClaimSettleSeconds")) + 1
    extracted = source[declarations_start:declarations_end] + "\n\n" + "\n\n".join(method(source, s) for s in signatures)
    mirror = "using System;\nusing UnityEngine;\nnamespace GloomhavenVR.Net.TownServices;\ninternal static partial class TownServiceMirror\n{\n" + extracted + "\n}\n"
    sources = {"TownServiceMirror.Election.cs": mirror}
    hashes = {"TownServiceMirror.cs": hashlib.sha256(source.encode()).hexdigest()}
    for name in ("TownServiceGrantSync.cs", "TownServiceGrantCodec.cs", "TownServiceGrantLedger.cs"):
        content = (base / "TownServices" / name).read_text()
        sources[name] = content
        hashes[name] = hashlib.sha256(content.encode()).hexdigest()
    protocol = (base / "NetProtocol.cs").read_text()
    fixture = repo / "scripts/town-presentation-election-runtime"
    boundaries = (fixture / "Boundaries.cs").read_text()
    for name in ("ModBuild", "MsgTownGrant", "StaleTimeoutSeconds"):
        value = re.search(r"public const (?:int|ushort|byte|float) " + name + r" = ([^;]+);", protocol)
        if value is None:
            raise SystemExit("Production protocol constant not found: " + name)
        boundaries = re.sub(r"(internal const (?:int|byte|float) " + name + r" = )[^;]+;",
                            lambda match: match[1] + value[1] + ";", boundaries)
    (run / "source-proof.json").write_text(json.dumps({
        "sources": hashes, "extracted_methods": signatures,
        "protocol_sha256": hashlib.sha256(protocol.encode()).hexdigest(),
        "boundary": "Clock, identity, connectivity, visitor membership and reliable byte delivery; complete production grant algorithms",
    }, indent=2) + "\n")

    variants = [("production", "", "", "")]
    if not args.no_negative_controls:
        variants += [
            ("original-browsing-cache", "InteractionLease lease = InteractionLeases[service];",
             "InteractionLease lease = InteractionLeases[service];\n        if (LiveInteraction(lease.Player, service, lease.Session, now)) return lease.Player;",
             "late visitor updates the settled browsing owner"),
            ("original-age-ranking", method(source, "private static int LivePresentationCandidate("),
             ORIGINAL_AGE_CANDIDATE, "reversed observation orders select the same lowest live player ID"),
            ("stale-transaction-cache", "InteractionLease lease = TransactionLeases[service];",
             "InteractionLease lease = TransactionLeases[service];\n        if (LiveInteraction(lease.Player, service, lease.Session, now, requireTransaction: true)) return lease.Player;",
             "late transaction claimant reevaluates the settled cosmetic fallback"),
            ("ignore-host-grant", "if (granted != 0) return granted;", "/* ignore actual host grant */",
             "actual host grant overrides the lowest-ID presentation claimant immediately"),
            ("occupy-temple", "if (service == 2) return 0;", "/* allow cosmetic temple occupation */",
             "temple transaction never occupies the shared resident"),
        ]
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    for case, before, after, expected in variants:
        case_dir = run / case
        production = case_dir / "production"
        production.mkdir(parents=True)
        for name, original in sources.items():
            content = original
            if name == "TownServiceMirror.Election.cs" and before:
                if content.count(before) != 1:
                    raise SystemExit("Negative control no longer binds exactly: " + case)
                content = content.replace(before, after)
            (production / name).write_text(content)
        (case_dir / "Boundaries.cs").write_text(boundaries)
        project = case_dir / "Election.csproj"
        project.write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
                           '<TargetFramework>net8.0</TargetFramework><EnableDefaultCompileItems>false</EnableDefaultCompileItems>'
                           '<Nullable>enable</Nullable><NoWarn>CS0649</NoWarn></PropertyGroup><ItemGroup>'
                           '<Compile Include="production/*.cs"/><Compile Include="Boundaries.cs"/>'
                           '<Compile Include="' + escape(str(fixture / "Program.cs")) + '"/>'
                           '</ItemGroup></Project>\n')
        done = subprocess.run([dotnet, "run", "--project", str(project), "-c", "Release", "--nologo"],
                              text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, timeout=120)
        (run / (case + ".log")).write_text(done.stdout)
        if expected:
            if done.returncode == 0 or expected not in done.stdout:
                raise SystemExit("Negative control failed to detect " + case + ":\n" + done.stdout)
            print("Detected negative control: " + case)
        elif done.returncode:
            raise SystemExit(done.stdout)
        else:
            print(done.stdout)
    print("Evidence: " + str(run))


if __name__ == "__main__":
    main()
