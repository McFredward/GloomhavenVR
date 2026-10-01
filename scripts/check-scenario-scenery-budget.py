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


def check_classifier_wiring(source: str) -> None:
    """Catch a broad shader-only rule even if the pure Judge still has its veto terms."""
    classify = method(source, "private static Verdict Classify(MeshRenderer renderer, ProceduralMapTile tile,")
    for required in (
        "FigureRendererGuard.IsFigureOrActorRenderer(renderer)",
        "t.GetComponent<ProceduralProp>() != null",
        "t.GetComponent<UnityGameEditorDoorProp>() != null",
        "if (!reachedTile || unit == null || !generatedContent)",
        "IsGrassOnlyUnit(unit.GetComponentsInChildren<Renderer>(includeInactive: true))",
        "unit.GetComponentInChildren<Collider>(includeInactive: true) == null",
        "materials.Length == 1",
        "materials[0].shader.name == GrassShader",
        "return Judge(named, true, safeAncestry, grassOnly, noEffects, shader, floorBounds);",
    ):
        if required not in classify:
            raise AssertionError("Classifier bypassed a required native-hierarchy veto: " + required)


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
        internal bool Safe = true;
        internal Transform transform = new();
        internal MeshFilter Filter = new();
        internal T? GetComponent<T>() where T : class => Filter as T;
        internal bool forceRenderingOff { get => _off; set { _off = value; Writes++; } }
    }
    internal sealed class Transform
    {
        private Transform? _parent;
        internal static int ParentReads;
        internal Transform? parent { get { ParentReads++; return _parent; } set => _parent=value; }
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
    internal static class HeldProps { internal static int Count; }
    internal static class NetHeldProps { internal static bool Any = false; }
    internal static class FigureRendererGuard
    {
        internal static int Calls;
        internal static bool HeldByPlayer(MeshRenderer renderer) { Calls++; return renderer.Held; }
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
            internal bool Invalidated;
        }
        private const int AncestryChecksPerFrame = 64;
        private static Verdict Classify(MeshRenderer renderer, ProceduralMapTile tile,
                                        out Transform? unit)
        {
            unit = renderer.Safe ? renderer.transform : null;
            return renderer.Safe ? Verdict.Eligible : Verdict.Shader;
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
            private readonly List<Record> _records = new();
            private int _watchIndex;
            __QUEUE_METHOD__
            __RECHECK_METHOD__
            __KNOWN_METHOD__

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
            internal static void CheckWatchAndKnown()
            {
                var driver = new Driver();
                var tile = new ProceduralMapTile(12, true);
                for (int i=0; i<150; i++)
                {
                    var unit = new Transform { parent=tile.transform };
                    var renderer = new MeshRenderer { transform=new Transform { parent=unit } };
                    var record = new Record { Renderer=renderer, Chain=CaptureChain(renderer.transform,tile) };
                    SetHidden(record,true);
                    driver._records.Add(record);
                }
                Transform.ParentReads = 0;
                FigureRendererGuard.Calls = 0;
                driver.RecheckOwned();
                Assert(Transform.ParentReads <= 64*3, "steady-state ancestry walk must be bounded");
                Assert(FigureRendererGuard.Calls == 0, "no held-prop lookup without a hold");

                var changed = driver._records[130];
                changed.Renderer.transform.parent = new Transform();
                driver.RecheckOwned();
                Assert(changed.Owned && changed.Renderer.forceRenderingOff,
                       "unvisited later record waits for rolling validation");
                driver.RecheckOwned();
                Assert(!changed.Owned && !changed.Renderer.forceRenderingOff && changed.Invalidated,
                       "rolling watch releases reparented renderer");

                var held = driver._records[149];
                held.Renderer.Held = true;
                HeldProps.Count = 1;
                driver.RecheckOwned();
                Assert(!held.Owned && !held.Renderer.forceRenderingOff && held.Invalidated,
                       "held prop released before rolling watch reaches it");
                HeldProps.Count = 0;

                var known = driver._records[10];
                driver.RevalidateKnown(known, known.Renderer, tile);
                Assert(known.Owned && !known.Invalidated, "safe known renderer stays owned");
                known.Renderer.Safe = false;
                driver.RevalidateKnown(known, known.Renderer, tile);
                Assert(!known.Owned && !known.Renderer.forceRenderingOff && known.Invalidated,
                       "placement revalidates changed material/component on same renderer");
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
            Assert(StillOnOriginalChain(owned), "held state is handled separately from bounded ancestry");
            renderer.Held = false;
            Driver.CheckQueue();
            Driver.CheckWatchAndKnown();
        }
    }
    internal static class Program
    {
        private static void Main()
        {
            ScenarioSceneryBudget.Check();
            Console.WriteLine("Scenario scenery eligibility, ownership, bounded watch and placement gates passed");
        }
    }
}
'''


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--source-root", type=Path, default=ROOT)
    args = parser.parse_args()
    source = (args.source_root / "src/GloomhavenVR/Core/Perf/ScenarioSceneryBudget.cs").read_text()
    check_classifier_wiring(source)
    for label, old, new in (
        ("actor ancestry", "FigureRendererGuard.IsFigureOrActorRenderer(renderer)", "false"),
        ("prop ancestry", "t.GetComponent<ProceduralProp>() != null", "false"),
        ("collider descendants", "unit.GetComponentInChildren<Collider>(includeInactive: true) == null", "true"),
    ):
        mutation = source.replace(old, new, 1)
        if mutation == source:
            raise SystemExit("Classifier negative control did not mutate: " + label)
        try:
            check_classifier_wiring(mutation)
        except AssertionError:
            pass
        else:
            raise SystemExit("Classifier negative control unexpectedly passed: " + label)
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
    recheck = method(source, "private void RecheckOwned()")
    known = method(source, "private void RevalidateKnown(Record record, MeshRenderer renderer, ProceduralMapTile tile)")
    assert "RevalidateKnown(known, renderer, tile);" in method(source, "private void Examine(MeshRenderer renderer, ProceduralMapTile tile)")
    placement = method(source, "internal static void ContentPlaced(ProceduralBase entity)")
    assert "entity is ProceduralMapTile tile" in placement
    assert "entity.GetComponentInParent<ProceduralMapTile>()" in placement
    variants = (
        ("production", outer, queue, recheck, known, True),
        ("shader-only eligibility", outer.replace("if (!generator) return Verdict.Generator;", "if (!name) return Verdict.Generator;"), queue, recheck, known, False),
        ("collider admitted", outer.replace("if (!noColliderOrEffect) return Verdict.ColliderOrEffect;", "if (!name) return Verdict.ColliderOrEffect;"), queue, recheck, known, False),
        ("mixed prop unit admitted", outer.replace("!IsGrassName(m.name)", "m.name.Length < 0"), queue, recheck, known, False),
        ("foreign force flag restored", outer.replace("if (!record.Owned && !renderer.forceRenderingOff)", "if (!record.Owned)"), queue, recheck, known, False),
        ("reparented grass stays hidden", outer.replace("!ReferenceEquals(t, record.Chain[i])", "ReferenceEquals(t, record.Chain[i])"), queue, recheck, known, False),
        ("100 percent queues content", outer, queue.replace("_density >= 100", "_density > 100"), recheck, known, False),
        ("new room ignored", outer, queue.replace("if (_pendingIds.Add(id))", "if (id < 0 && _pendingIds.Add(id))"), recheck, known, False),
        ("unbounded ancestry walk", outer, queue, recheck.replace("Math.Min(_records.Count, AncestryChecksPerFrame)", "_records.Count"), known, False),
        ("held prop deferred", outer, queue, recheck.replace("if (HeldProps.Count > 0 || NetHeldProps.Any)", "if (HeldProps.Count > 99999 || NetHeldProps.Any)"), known, False),
        ("known renderer changes ignored", outer, queue, recheck, known.replace("verdict == Verdict.Eligible", "verdict != Verdict.Eligible"), False),
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
        for label, outer_methods, queue_method, recheck_method, known_method, should_pass in variants:
            if not should_pass and (outer_methods, queue_method, recheck_method, known_method) == (outer, queue, recheck, known):
                raise SystemExit("Negative control did not mutate: " + label)
            code = (HARNESS.replace("__OUTER_METHODS__", outer_methods)
                    .replace("__QUEUE_METHOD__", queue_method)
                    .replace("__RECHECK_METHOD__", recheck_method)
                    .replace("__KNOWN_METHOD__", known_method))
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
        print("Scenario scenery: 10 compiled and 3 source-wiring negative controls failed as expected")


if __name__ == "__main__":
    main()
