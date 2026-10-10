#!/usr/bin/env python3
"""Run a bounded original-artwork transport proof against current production sources.

The native reflection/channel boundaries are explicit fakes; the actual SendToken,
NextBatch, batch selector, codecs, deltas and fragment assemblers are source-bound.
No Unity launch, hardware claim, full gate repetition, or tracked source mutation.
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


def method(source, signature):
    start = source.index("    " + signature)
    end = source.index("\n    }", start) + len("\n    }")
    return source[start:end]


def replace_once(source, before, after):
    if source.count(before) != 1:
        raise RuntimeError(f"Production binding drift: expected one {before!r}, found {source.count(before)}")
    return source.replace(before, after, 1)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--production-root", type=Path, default=Path(__file__).resolve().parents[1])
    parser.add_argument("--output", type=Path)
    parser.add_argument("--controls", action="store_true")
    args = parser.parse_args()
    root = args.production_root.resolve()
    fixtures = Path(__file__).resolve().parent / "town-original-delivery638-runtime"
    vectors = Path(__file__).resolve().parents[1] / "tests/GloomhavenVR.WireTests/TownOriginalDelivery638Vectors.cs"
    parent = args.output or root / ".planning/debug/town-original-delivery638"
    parent.mkdir(parents=True, exist_ok=True)
    out = Path(tempfile.mkdtemp(prefix="run-", dir=parent))
    production = out / "production"
    production.mkdir()
    names = ["Net/NetProtocol.cs", "Net/NetPacket.cs", "Net/PresentationBatch.cs",
             "Net/PresentationCompression.cs", "Net/ExtrasFragments.cs"]
    names += [f"Net/TownServices/{name}.cs" for name in (
        "TownServiceFrame", "TownServiceCodec", "TownServiceCodec.OriginalValuePool",
        "TownServiceCodec.ReturnOrigin",
        "TownRackState", "TownCatalogLayout", "TownCatalogBank", "TownCatalogBank.Headers",
        "TownCatalogClock", "TownServiceDelta", "TownServiceFragments")]
    receipt = {}
    for name in names:
        path = root / "src/GloomhavenVR" / name
        content = path.read_bytes()
        if name == "Net/NetProtocol.cs":
            # This proof uses wire constants, not Unity-based pose/math helpers.
            # Bind the exact declarations rather than require a game install on CI
            # or substitute implementations for helpers we do not exercise.
            declarations = re.findall(r"^    public const\s+\w+\s+\w+\s*=[^;]*;", content.decode(), re.M)
            if not declarations or "UnityEngine" in "\n".join(declarations):
                raise RuntimeError("Portable protocol constant binding drift")
            (production / path.name).write_text(
                "namespace GloomhavenVR.Net;\ninternal static class NetProtocol {\n"
                + "\n".join(declarations) + "\n}\n")
        else:
            (production / path.name).write_bytes(content)
        receipt[str(path.relative_to(root))] = hashlib.sha256(content).hexdigest()
    transport_path = root / "src/GloomhavenVR/Net/FfsNetTransport.cs"
    queue_path = root / "src/GloomhavenVR/Net/ExtrasSendQueue.cs"
    serializer_path = root / "src/GloomhavenVR/Net/Avatar/AvatarSerializer.cs"
    cassette_path = root / "src/GloomhavenVR/Net/TownServices/TownCassetteMotion.cs"
    for path in (transport_path, queue_path, serializer_path, cassette_path):
        receipt[str(path.relative_to(root))] = hashlib.sha256(path.read_bytes()).hexdigest()
    send = method(transport_path.read_text(), "private void SendToken(byte[] bytes)")
    batch = method(queue_path.read_text(), "internal byte[]? NextBatch(double now,")
    serializer = serializer_path.read_text()
    write = "\n".join(method(serializer, signature) for signature in (
        "internal static void WriteU32(byte[] b,", "internal static uint ReadU32(byte[] b,",
        "internal static void WriteF32(byte[] b,", "internal static float ReadF32(byte[] b,"))
    constants = re.findall(r"    internal const byte (?:RecordId|RollerRecordId) = \d+;", cassette_path.read_text())
    if len(constants) != 2:
        raise RuntimeError("Town cassette record identity binding drift")
    bindings = f"""using System;
using System.Collections.Generic;
using System.Reflection;
using GloomhavenVR.Net;
namespace GloomhavenVR.TownOriginalDelivery638 {{
internal sealed class BoundSender {{
    private readonly ConstructorInfo? _customDataCtor = typeof(FakeToken).GetConstructor(new[] {{ typeof(byte[]), typeof(bool) }});
    private readonly MethodInfo? _sendSideAction = typeof(FakeNative).GetMethod(nameof(FakeNative.Send));
    private readonly object?[] _sendArgs = new object?[] {{ 109, null, true, false, -932, 0, 0, false }};
    internal bool Unreliable => (bool)_sendArgs[2]!;
    internal void Send(byte[] bytes) => SendToken(bytes);
{send}
}}
// Only TakeNext is a queue boundary. The actual production cap/clock/batching
// method is bound verbatim; this does not re-prove weighted queue fairness.
internal sealed class BoundScheduler {{
    private double _next;
    private byte[]? _heldPage;
    private readonly Queue<byte[]> _pending = new();
    internal void Add(byte[] page) => _pending.Enqueue(page);
    private byte[]? TakeNext(double now) => _pending.Count == 0 ? null : _pending.Dequeue();
{batch}
}}
}}
namespace GloomhavenVR.Net {{ internal static unsafe class AvatarSerializer {{
{write}
}} }}
namespace GloomhavenVR.Net.TownServices {{ internal static class TownCassetteMotion {{
{chr(10).join(constants)}
}} }}
// Diagnostics are inert at the explicit normal-log boundary, not an artwork
// dependency or a substitute for the real assembler/codec.
namespace GloomhavenVR.Core {{ internal static class VRLog {{
    internal static bool WantsDebug => false;
    internal static void Info(string source, string message) {{ }}
}} }}
"""
    bound = out / "BoundProduction.cs"
    bound.write_text(bindings)
    shutil.copy2(fixtures / "Program.cs", out / "Program.cs")
    shutil.copy2(vectors, out / vectors.name)
    shutil.copy2(root / "tests/GloomhavenVR.WireTests/Hex.cs", out / "Hex.cs")
    for path in (fixtures / "Program.cs", vectors, Path(__file__)):
        receipt[str(path)] = hashlib.sha256(path.read_bytes()).hexdigest()
    (out / "source-receipt.json").write_text(json.dumps(receipt, indent=2) + "\n")
    project = out / "TransportProof.csproj"
    project.write_text("""<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework>
    <Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
    <NoWarn>CS0649</NoWarn><EnableDefaultCompileItems>true</EnableDefaultCompileItems>
    <NuGetAudit>false</NuGetAudit></PropertyGroup>
</Project>
""")
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1")

    def run(label):
        build = subprocess.run([dotnet, "build", str(project), "-c", "Release", "--nologo", "-v:q"],
                               capture_output=True, text=True, env=env)
        (out / f"{label}-build.log").write_text(build.stdout + build.stderr)
        if build.returncode:
            raise RuntimeError(f"Proof compile failed; see {out / (label + '-build.log')}")
        result = subprocess.run([dotnet, str(out / "bin/Release/net8.0/TransportProof.dll")],
                                capture_output=True, text=True, env=env)
        (out / f"{label}.log").write_text(result.stdout + result.stderr)
        print(label + ": " + result.stdout.strip(), flush=True)
        return result

    positive = run("positive")
    if positive.returncode:
        raise RuntimeError(f"Production transport proof failed; see {out / 'positive.log'}")
    controls = []
    if args.controls:
        variants = {
            "historical-unreliable-original": replace_once(bindings,
                "_sendArgs[2] = !PresentationBatch.HasTownOriginalPage(bytes, bytes.Length);", "_sendArgs[2] = true;"),
            "missing-finally-restoration": replace_once(bindings,
                "finally { _sendArgs[2] = true; }", "finally { }"),
            "cadence-regression": bindings.replace("_next = now + .05", "_next = now + .001"),
            "child-count-regression": replace_once(bindings, "pages.Count < 32", "pages.Count < 64"),
        }
        for label, content in variants.items():
            bound.write_text(content)
            result = run(label)
            caught = result.returncode != 0 and "FAIL  " in result.stderr
            controls.append({"name": label, "caught_by_assertion": caught})
            if not caught:
                raise RuntimeError(f"Causal control was not caught by an assertion: {label}")
        bound.write_text(bindings)
        selector = production / "PresentationBatch.cs"
        original_selector = selector.read_text()
        for label, signature in (
            ("compressed-original-ignored", "        int end = start + length; bool found = false;"),
            ("batched-original-ignored", "        if (length < 7 || buffer[6] < 2 || buffer[6] > 32) return false;"),
        ):
            insertion = "        if (type == NetProtocol.MsgPresentationCompression) return false;\n" if label.startswith("compressed") \
                else "        if (NetPacket.PeekType(buffer, length) == NetProtocol.MsgPresentationBatch) return false;\n"
            selector.write_text(replace_once(original_selector, signature, insertion + signature))
            result = run(label)
            caught = result.returncode != 0 and "FAIL  " in result.stderr
            controls.append({"name": label, "caught_by_assertion": caught})
            if not caught:
                raise RuntimeError(f"Causal control was not caught by an assertion: {label}")
        selector.write_text(original_selector)
        if run("restored-positive").returncode:
            raise RuntimeError("Restored production proof failed after causal controls")
    (out / "controls.json").write_text(json.dumps(controls, indent=2) + "\n")
    print("Evidence: " + str(out), flush=True)


if __name__ == "__main__":
    main()
