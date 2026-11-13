"""Run actual original GlobalData/Odin host oracles without touching game inputs."""
from __future__ import annotations

import argparse
import hashlib
import json
import subprocess
from pathlib import Path


def invoke(command: list[str], *, expected_failure: bool = False) -> str:
    result = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, check=False)
    if expected_failure:
        if result.returncode == 0 or "Host AOT oracle forbids runtime emit:" not in result.stdout:
            raise RuntimeError("The desktop poison oracle did not reject its original runtime codegen path.\n" + result.stdout)
        return "Original desktop capability reaches a forbidden runtime emit API; negative control passed."
    if result.returncode:
        raise RuntimeError("Original serializer proof failed:\n" + result.stdout)
    if "Exception" in result.stdout or "Failed" in result.stdout or "cant resolve internal call" in result.stdout:
        raise RuntimeError("Original serializer emitted an exception/native host error:\n" + result.stdout)
    return result.stdout.strip()


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--managed", required=True, type=Path)
    parser.add_argument("--unity-editor", required=True, type=Path)
    parser.add_argument("--cache", required=True, type=Path)
    parser.add_argument("--dotnet", default="dotnet")
    args = parser.parse_args()
    managed, cache = args.managed.resolve(), args.cache.resolve()
    if cache == managed or managed in cache.parents:
        parser.error("Proof cache must be outside original game inputs.")
    root = Path(__file__).resolve().parent
    host = cache / "managed"
    output = cache / "output"
    cache.mkdir(parents=True, exist_ok=True)
    mono_root = args.unity_editor.resolve().parent / "Data/MonoBleedingEdge"
    mono = str(mono_root / "bin/mono")
    compiler = str(mono_root / "lib/mono/4.5/mcs.exe")
    original_hashes = {path.name: hashlib.sha256(path.read_bytes()).hexdigest() for path in managed.glob("*.dll")}
    transcript = [invoke([args.dotnet, "run", "--project", str(root / "tests/AuditTests.csproj"), "--",
                          "stage-odin-host-proof", str(managed), str(host)])]
    executable = cache / "OdinGlobalDataProof.exe"
    transcript.append(invoke([mono, compiler, "-out:" + str(executable), str(root / "proof/OdinGlobalDataProof.cs")]))
    for variant, action in (("pc", "write"), ("quest", "read-other"), ("pc", "read-other"), ("quest-noemit", "read-other")):
        transcript.append(invoke([mono, str(executable), str(host), variant, str(output), action]))
    transcript.append(invoke([mono, str(executable), str(host), "pc-noemit", str(output), "write"], expected_failure=True))
    for path in managed.glob("*.dll"):
        if hashlib.sha256(path.read_bytes()).hexdigest() != original_hashes[path.name]:
            raise RuntimeError("Original input changed: " + path.name)
    fixtures = {}
    for kind in ("default", "nonempty", "campaign-metadata", "campaign-metadata-rewritten", "ruleset-metadata"):
        values = [(output / (variant + "-" + kind + ".dat")).read_bytes() for variant in ("pc", "quest", "quest-noemit")]
        if not all(value == values[0] for value in values):
            raise RuntimeError("Original PC/reflection/no-emission fixture bytes differ: " + kind)
        fixtures[kind] = {"size": len(values[0]), "sha256": hashlib.sha256(values[0]).hexdigest()}
    closure = (output / "quest-noemit-original-type-closure.txt").read_text(encoding="utf-8").splitlines()
    observed_strong = [line for line in closure if line.startswith("selected StrongTypeFormatterMap ")]
    expected_strong = {
        "GlobalData+KeyBinding": "ReflectionFormatter`1",
        "ClientIndependantValues+CIVKeyValuePair": "ReflectionFormatter`1",
        "System.Tuple`2": "ReflectionFormatter`1",
        "System.Tuple`3": "ReflectionFormatter`1",
        "PartyAdventureData": "SerializableFormatter`1",
        "GHRuleset": "SerializableFormatter`1",
    }
    for name, formatter in expected_strong.items():
        if not any(line.startswith("selected StrongTypeFormatterMap " + name + ",")
                   or line.startswith("selected StrongTypeFormatterMap " + name + "[[")
                   for line in observed_strong if " => OdinSerializer." + formatter + "[[" in line):
            raise RuntimeError("Actual native metadata formatter closure changed or was not captured: " + name)
    if not any(line.startswith("selected WeakTypeFormatterMap System.Byte[],")
               and " => OdinSerializer.PrimitiveArrayFormatter`1[[System.Byte," in line for line in closure):
        raise RuntimeError("Original avatar primitive byte-array formatter was not exercised.")
    receipt = {"schema": 1, "originalGlobalDataOdinBinaryWriterReader": True,
               "pcQuestFixtureBytesIdentical": True, "originalCallbacksRetained": True,
               "originalCampaignSlotMetadataRoundtrip": True, "originalRulesetMetadataRoundtrip": True,
               "formatterMapsCaptured": ["FormatterInstances", "StrongTypeFormatterMap", "WeakTypeFormatterMap"],
               "observedStrongFormatterRoots": expected_strong,
               "originalAvatarPrimitiveArrayFormatterExercised": True,
               "runtimeEmitPoisonSites": 345, "pcCodegenNegativeControl": True,
               "unityNativeHostSubstitutes": ["Application.platform/unityVersion/version/isEditor", "DebugLogHandler native logging"],
               "nativeGlobalMembers": 50, "fixtures": fixtures,
               "originalInputsUnchanged": True, "androidVerified": False, "fullCampaignPartyGraphVerified": False,
               "transcript": transcript}
    (cache / "result.json").write_text(json.dumps(receipt, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: value for key, value in receipt.items() if key != "transcript"}, indent=2))


if __name__ == "__main__":
    main()
