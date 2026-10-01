#!/usr/bin/env python3
"""Run the production scenery budget gates against counted renderer writes/events."""
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
    arrow = source.find("=>", start, brace)
    if arrow >= 0:
        return source[start:source.index(";", arrow) + 1]
    depth = 1
    end = brace + 1
    while depth:
        depth += (source[end] == "{") - (source[end] == "}")
        end += 1
    return source[start:end]


HARNESS = r'''
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
    internal class Renderer { internal string name = ""; }
    internal sealed class MeshFilter { internal object? sharedMesh = new(); }
    internal sealed class ParticleSystemRenderer : Renderer { }
    internal sealed class MeshRenderer : Renderer
    {
        private bool _off;
        internal int Writes;
        internal bool Held;
        internal Transform transform = new();
        internal MeshFilter Filter = new();
        internal T? GetComponent<T>() where T : class => Filter as T;
        internal bool forceRenderingOff { get => _off; set { _off = value; Writes++; } }
    }
    internal sealed class Transform
    {
        internal Transform? parent;
    }
    internal sealed class ProceduralScenario { }
    internal sealed class ProceduralMapTile
    {
        private readonly int _id;
        internal readonly bool IsScenario;
        internal readonly Transform transform = new();
        internal ProceduralMapTile(int id, bool scenario) { _id=id; IsScenario=scenario; }
        internal int GetInstanceID() => _id;
        internal T? GetComponentInParent<T>() where T : class, new() =>
            IsScenario && typeof(T)==typeof(ProceduralScenario) ? new T() : null;
    }
    internal static class Time { internal static float unscaledTime = 1f; }
    internal static class Mathf { internal static float Max(float a, float b) => Math.Max(a,b); }
}

namespace GloomhavenVR.Core
{
    internal static class FigureRendererGuard
    {
        internal static bool HeldByPlayer(MeshRenderer renderer) => renderer.Held;
    }
    internal static class ScenarioSceneryBudget
    {
        private const string GrassPrefix = "FR_Floor_Grass_";
        private const string ScatterPrefix = "FR_Floor_Scatter_Grass_";
        private enum Verdict : byte
        {
            Eligible, Name, Generator, Ancestry, Composite, ColliderOrEffect, Shader, Geometry,
        }
        private sealed class Record
        {
            internal MeshRenderer Renderer = null!;
            internal Transform[] Chain = null!;
            internal bool Owned;
        }
        __OUTER_METHODS__

        private sealed class Driver
        {
            private readonly Queue<ProceduralMapTile> _pending = new();
            private readonly HashSet<int> _pendingIds = new();
            private bool _inScenarioScene = true;
            private bool _actualScenario;
            private int _density = 100;
            private float _summaryDue;
            __QUEUE_METHOD__

            internal static void CheckQueue()
            {
                var driver = new Driver();
                var room = new ProceduralMapTile(1, true);
                var notScenario = new ProceduralMapTile(2, false);
                driver.QueueTile(room);
                Assert(driver._pending.Count == 0 && !driver._actualScenario,
                       "density 100 must never queue content");
                driver._density = 25;
                driver.QueueTile(notScenario);
                Assert(driver._pending.Count == 0, "non-scenario content refused");
                driver.QueueTile(room);
                driver.QueueTile(room);
                Assert(driver._pending.Count == 1 && driver._actualScenario,
                       "new room queued once at lower density");
                driver.QueueTile(new ProceduralMapTile(3, true));
                Assert(driver._pending.Count == 2, "newly placed second room queued");
                driver._inScenarioScene = false;
                driver.QueueTile(new ProceduralMapTile(4, true));
                Assert(driver._pending.Count == 2, "flat/map scene refused");
            }
        }

        private static void Assert(bool yes, string why)
        {
            if (!yes) throw new Exception(why);
        }
        internal static void Check()
        {
            Assert(Judge(true,true,true,true,true,true,true)==Verdict.Eligible,
                   "standalone decorative grass admitted");
            Assert(Judge(false,true,true,true,true,true,true)==Verdict.Name, "name veto");
            Assert(Judge(true,false,true,true,true,true,true)==Verdict.Generator, "generator veto");
            Assert(Judge(true,true,false,true,true,true,true)==Verdict.Ancestry, "actor/preview veto");
            Assert(Judge(true,true,true,false,true,true,true)==Verdict.Composite, "composite veto");
            Assert(Judge(true,true,true,true,false,true,true)==Verdict.ColliderOrEffect, "collider veto");
            Assert(Judge(true,true,true,true,true,false,true)==Verdict.Shader, "shader veto");
            Assert(Judge(true,true,true,true,true,true,false)==Verdict.Geometry, "bounds veto");
            var grass = new MeshRenderer { name="FR_Floor_Grass_Half_01" };
            var leaf1 = new MeshRenderer { name="FR_Floor_Scatter_Grass_Small_01" };
            var leaf2 = new MeshRenderer { name="FR_Floor_Scatter_Grass_Small_01 (1)" };
            Assert(IsGrassOnlyUnit(new Renderer[] { grass, leaf1, leaf2 }),
                   "measured three-renderer floor grass unit accepted");
            Assert(!IsGrassOnlyUnit(new Renderer[] { grass, leaf1, new MeshRenderer { name="FR_Stones_06" } }),
                   "composite with stone refused");
            Assert(!IsGrassOnlyUnit(new Renderer[] { grass, new ParticleSystemRenderer { name="FR_Floor_Grass_Seg_P" } }),
                   "effect renderer refused");
            leaf2.Filter.sharedMesh = null;
            Assert(!IsGrassOnlyUnit(new Renderer[] { grass, leaf1, leaf2 }), "missing mesh refused");
            Assert(!ShouldHide(99u,100) && ShouldHide(99u,25) && !ShouldHide(24u,25)
                   && ShouldHide(0u,0), "density values including 100 and 0");

            var renderer = new MeshRenderer();
            var owned = new Record { Renderer=renderer };
            SetHidden(owned,true);
            Assert(renderer.forceRenderingOff && renderer.Writes==1 && owned.Owned,
                   "hide owns exactly one write");
            SetHidden(owned,true);
            Assert(renderer.Writes==1, "no repeated force setter");
            SetHidden(owned,false);
            Assert(!renderer.forceRenderingOff && renderer.Writes==2 && !owned.Owned,
                   "100 percent restores owned flag");
            renderer.forceRenderingOff = true;
            renderer.Writes = 0;
            SetHidden(owned,true);
            SetHidden(owned,false);
            Assert(renderer.forceRenderingOff && renderer.Writes==0 && !owned.Owned,
                   "pre-existing force flag never acquired or restored");

            var tile = new ProceduralMapTile(5,true);
            var unit = new Transform { parent=tile.transform };
            var leaf = new Transform { parent=unit };
            renderer.transform = leaf;
            owned.Chain = CaptureChain(leaf,tile);
            Assert(StillOnOriginalChain(owned), "stable generated-content ancestry");
            unit.parent = new Transform();
            Assert(!StillOnOriginalChain(owned), "reparented unit released");
            unit.parent = tile.transform;
            renderer.Held = true;
            Assert(!StillOnOriginalChain(owned), "held prop released immediately");
            renderer.Held = false;
            Driver.CheckQueue();
        }
    }
    internal static class Program
    {
        private static void Main()
        {
            ScenarioSceneryBudget.Check();
            Console.WriteLine("Scenario scenery eligibility, ownership and new-content gates passed");
        }
    }
}
'''


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-root", type=Path, default=ROOT)
    args = parser.parse_args()
    source = (args.source_root / "src/GloomhavenVR/Core/Perf/ScenarioSceneryBudget.cs").read_text()
    outer = "\n".join(method(source, signature) for signature in (
        "private static Verdict Judge(",
        "private static bool ShouldHide(uint hash, int densityPercent)",
        "private static void SetHidden(Record record, bool hide)",
        "private static Transform[] CaptureChain(Transform leaf, ProceduralMapTile tile)",
        "private static bool StillOnOriginalChain(Record record)",
        "private static bool IsGrassName(string name)",
        "private static bool IsGrassOnlyUnit(Renderer[] members)",
    ))
    queue = method(source, "internal void QueueTile(ProceduralMapTile? tile)")
    variants = (
        ("production", outer, queue, True),
        ("shader-only eligibility", outer.replace("if (!generator) return Verdict.Generator;", "if (!name) return Verdict.Generator;"), queue, False),
        ("collider admitted", outer.replace("if (!noColliderOrEffect) return Verdict.ColliderOrEffect;", "if (!name) return Verdict.ColliderOrEffect;"), queue, False),
        ("mixed prop unit admitted", outer.replace("!IsGrassName(m.name)", "m.name.Length < 0"), queue, False),
        ("foreign force flag restored", outer.replace("if (!record.Owned && !renderer.forceRenderingOff)", "if (!record.Owned)"), queue, False),
        ("reparented grass stays hidden", outer.replace("!ReferenceEquals(t, record.Chain[i])", "ReferenceEquals(t, record.Chain[i])"), queue, False),
        ("100 percent queues content", outer, queue.replace("_density >= 100", "_density > 100"), False),
        ("new room ignored", outer, queue.replace("if (_pendingIds.Add(id))", "if (id < 0 && _pendingIds.Add(id))"), False),
    )
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    env = dict(os.environ, DOTNET_ROOT=str(Path(dotnet).resolve().parent))
    with tempfile.TemporaryDirectory(prefix="ghvr-scenerybudget-") as tmp:
        folder = Path(tmp)
        (folder / "Test.csproj").write_text(
            '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
            '<TargetFramework>net8.0</TargetFramework><Nullable>enable</Nullable>'
            '<TreatWarningsAsErrors>true</TreatWarningsAsErrors></PropertyGroup></Project>'
        )
        for label, outer_methods, queue_method, should_pass in variants:
            if not should_pass and outer_methods == outer and queue_method == queue:
                raise SystemExit("Negative control did not mutate: " + label)
            code = HARNESS.replace("__OUTER_METHODS__", outer_methods).replace("__QUEUE_METHOD__", queue_method)
            (folder / "Program.cs").write_text(code)
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
        print("Scenario scenery: 7 compiled negative controls failed as expected")


if __name__ == "__main__":
    main()
