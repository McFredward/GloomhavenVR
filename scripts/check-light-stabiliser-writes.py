#!/usr/bin/env python3
"""Exercise the real LightStabiliser scan and write gates with counted Unity setters.

The negative controls make sure this catches both a restored unconditional setter and
Unity's approximate Vector3 comparison discarding small damped movements.
"""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


ROOT = Path(__file__).resolve().parents[1]


def method(source: str, signature: str) -> str:
    start = source.index(signature)
    brace = source.index("{", start)
    depth = 1
    end = brace + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


HARNESS = r'''
using UnityEngine;
using GloomhavenVR.Core;

namespace UnityEngine
{
    internal sealed class LightFlicker { }
    internal sealed class Light
    {
        private float _intensity;
        internal int Writes;
        internal float intensity { get => _intensity; set { _intensity = value; Writes++; } }
    }
    internal struct Vector3 : System.IEquatable<Vector3>
    {
        internal float x, y, z;
        internal Vector3(float x, float y, float z) { this.x=x; this.y=y; this.z=z; }
        public bool Equals(Vector3 other) => x == other.x && y == other.y && z == other.z;
        public override bool Equals(object? other) => other is Vector3 v && Equals(v);
        public override int GetHashCode() => System.HashCode.Combine(x, y, z);
        // Deliberately approximate, as Unity's Vector3 == operator is.
        public static bool operator ==(Vector3 a, Vector3 b) =>
            System.Math.Abs(a.x-b.x) + System.Math.Abs(a.y-b.y) + System.Math.Abs(a.z-b.z) < 0.00001f;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
    }
    internal sealed class Transform
    {
        private Vector3 _localPosition;
        internal int Writes;
        internal Vector3 localPosition { get => _localPosition; set { _localPosition = value; Writes++; } }
    }
    internal static class Object
    {
        internal static int LightSearches, FlickerSearches;
        internal static T[] FindObjectsOfType<T>()
        {
            if (typeof(T) == typeof(Light)) LightSearches++;
            else if (typeof(T) == typeof(LightFlicker)) FlickerSearches++;
            else throw new System.Exception("wrong search type");
            return System.Array.Empty<T>();
        }
    }
}

namespace GloomhavenVR.Rig
{
    internal static class VRLog { internal static void Info(string area, string message) =>
        throw new System.Exception(area + message); }
    internal static partial class LightStabiliser
    {
        private static bool _censusPrinted;
        internal static int NewLights, PinChanges, CensusCalls, LogCalls;
        private static int AdoptLights(Light[] _) => NewLights;
        private static void PruneDestroyed() { }
        private static int ApplyPinning() => PinChanges;
        private static void CensusFlickers(LightFlicker[] _) => CensusCalls++;
        private static void LogCensus(int _, int __, int ___) => LogCalls++;

        // REAL PRODUCTION METHODS INSERTED HERE
        __METHODS__

        internal static void Check()
        {
            Scan();
            Assert(Object.LightSearches == 1 && Object.FlickerSearches == 1 &&
                   CensusCalls == 1 && LogCalls == 1, "initial census");
            Scan();
            Assert(Object.LightSearches == 2 && Object.FlickerSearches == 1 &&
                   CensusCalls == 1 && LogCalls == 1, "unchanged scan must avoid diagnostic search");
            NewLights = 1;
            Scan();
            Assert(Object.LightSearches == 3 && Object.FlickerSearches == 2 &&
                   CensusCalls == 2 && LogCalls == 2, "new light refreshes diagnostic census");
            NewLights = 0; PinChanges = 1;
            Scan();
            Assert(Object.LightSearches == 4 && Object.FlickerSearches == 3 &&
                   CensusCalls == 3 && LogCalls == 3, "pin change refreshes diagnostic census");

            NewLights = 0; PinChanges = 0;
            PerfConfig.LightStabiliserWorkCacheOn = false;
            Scan();
            Assert(Object.LightSearches == 5 && Object.FlickerSearches == 4 &&
                   CensusCalls == 4 && LogCalls == 3,
                   "A/B off restores original diagnostic search on unchanged scan");
            NewLights = 1;
            Scan();
            Assert(Object.LightSearches == 6 && Object.FlickerSearches == 5 &&
                   CensusCalls == 5 && LogCalls == 4,
                   "A/B off still reports a fresh census for new lights");

            var light = new Light();
            light.intensity = 1f; light.Writes = 0;
            WriteIntensity(light, 1f, 1f, true);
            Assert(light.Writes == 0 && light.intensity == 1f, "static light setter skipped");
            WriteIntensity(light, 1f, 1.000001f, true);
            Assert(light.Writes == 1 && light.intensity == 1.000001f, "small intensity change retained");
            light.Writes = 0;
            WriteIntensity(light, 1f, 1f, false);
            Assert(light.Writes == 1 && light.intensity == 1f,
                   "A/B off restores original unconditional intensity setter");

            var transform = new Transform();
            transform.localPosition = new Vector3(1f, 2f, 3f); transform.Writes = 0;
            WritePosition(transform, transform.localPosition, new Vector3(1f, 2f, 3f), true);
            Assert(transform.Writes == 0, "static position setter skipped");
            WritePosition(transform, transform.localPosition, new Vector3(1.000001f, 2f, 3f), true);
            Assert(transform.Writes == 1 && transform.localPosition.x == 1.000001f,
                   "sub-tolerance position change retained");
            transform.Writes = 0;
            WritePosition(transform, transform.localPosition, transform.localPosition, false);
            Assert(transform.Writes == 1, "A/B off restores original unconditional position setter");
        }
        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new System.Exception(message);
        }
    }
    internal static class Program
    {
        private static void Main() { LightStabiliser.Check(); System.Console.WriteLine("Light stabiliser scan/write gates passed"); }
    }
}

namespace GloomhavenVR.Core
{
    internal static class PerfConfig { internal static bool LightStabiliserWorkCacheOn = true; }
}
'''


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-root", type=Path, default=ROOT)
    args = parser.parse_args()
    source = (args.source_root / "src/GloomhavenVR/Rig/LightStabiliser.cs").read_text()
    signatures = (
        "private static void Scan()",
        "private static void WriteIntensity(Light light, float raw, float output, bool workCache)",
        "private static void WritePosition(Transform transform, Vector3 raw, Vector3 output, bool workCache)",
    )
    methods = "\n".join(method(source, signature) for signature in signatures)
    variants = (
        ("production", methods, True),
        ("unconditional intensity write", methods.replace("if (!workCache || raw != output)", "if (!workCache || output == raw || raw != output)"), False),
        ("off path skips intensity write", methods.replace("if (!workCache || raw != output)", "if (raw != output)"), False),
        ("approximate position equality", methods.replace("if (!workCache || !raw.Equals(output))", "if (!workCache || raw != output)"), False),
        ("off path skips position write", methods.replace("if (!workCache || !raw.Equals(output))", "if (!raw.Equals(output))"), False),
        ("diagnostic search on every scan", methods.replace("if (newLights == 0 && pinned == 0 && _censusPrinted)", "if (newLights == -1 && pinned == 0 && _censusPrinted)"), False),
        ("off path skips diagnostic search", methods.replace("if (!workCache)", "if (workCache)", 1), False),
    )
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    env = dict(os.environ, DOTNET_ROOT=str(Path(dotnet).resolve().parent))
    with tempfile.TemporaryDirectory(prefix="ghvr-lightwrites-") as tmp:
        folder = Path(tmp)
        (folder / "Test.csproj").write_text(
            '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
            '<TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable>'
            '<TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>'
        )
        for label, implementation, should_pass in variants:
            if not should_pass and implementation == methods:
                raise SystemExit("Negative control did not mutate: " + label)
            (folder / "Program.cs").write_text(HARNESS.replace("__METHODS__", implementation))
            run = subprocess.run(
                [dotnet, "run", "--project", str(folder / "Test.csproj"), "-c", "Release"],
                env=env, capture_output=True, text=True,
            )
            if should_pass:
                if run.returncode:
                    raise SystemExit(run.stdout + run.stderr)
                print(run.stdout, end="")
            elif run.returncode == 0 or "error CS" in run.stdout:
                raise SystemExit("Negative control did not fail at runtime: " + label + "\n" + run.stdout + run.stderr)
        print("Light stabiliser: 6 compiled negative controls failed as expected")


if __name__ == "__main__":
    main()
