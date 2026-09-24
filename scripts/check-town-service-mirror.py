#!/usr/bin/env python3
"""Run production town-service capture/codec/playback and render comparisons in Unity 2021.3.5."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


def method(text, signature):
    start = text.index("    " + signature)
    end = text.index("\n    }", start) + 6
    return text[start:end]


def expression(text, signature):
    start = text.index("    " + signature)
    return text[start:text.index(";", start) + 1]


def sources(root):
    base = root / "src/GloomhavenVR"
    names = ["TownServiceAssets", "TownServiceBinding", "TownServiceCodec", "TownServiceDelta",
             "TownServiceFrame", "TownRackState", "TownCassetteMotion", "TownServiceMirror.Racks", "TownServiceMaterial", "TownServiceFlameClock", "TownServiceMirror"]
    bound = {name + ".cs": (base / "Net/TownServices" / (name + ".cs")).read_text() for name in names}
    backdrop = base / "WorldUI/TownServices/TownServiceBackdropAssets.cs"
    if backdrop.exists(): bound[backdrop.name] = backdrop.read_text()
    motion = base / "Net/TownServices/TownServiceMotion.cs"
    if motion.exists(): bound[motion.name] = motion.read_text()
    publisher = (base / "WorldUI/TownServices/TownServiceSync.cs").read_text()
    signatures = ("private void TickCore(Transform sharedFrame, Transform? stationRoot)",
        "private void PublishHeld(TownServiceToken sample, Transform original)",
        "private void PublishCopiedCards(Transform original, Func<Transform, Transform?> cloneOf)",
        "private string? DynamicKey(Transform source)",
        "private void PublishRackClock(TownServiceCatalog catalog, TownServiceMerchantDrawer rack)",
        "private void AddRackMembers(Transform? root,TownServiceCatalog.Entry entry,TownServiceMerchantDrawer rack,ushort rackId)",
        "private int CompareRackMembers(TownRackMember a,TownRackMember b)",
        "private void PublishCatalog(TownServiceCatalog catalog, Transform? furniture)",
        "private void TickCatalog(Transform frame, Transform station, TownServiceCatalog catalog, uint session, float age)",
        "private void PruneSources()", "private void ResetCore()")
    declarations = ("private uint _generation", "private ulong _relocationRevision", "private bool _generationExhausted")
    wrappers = publisher[publisher.index("    private static readonly TownServiceSync Private"):publisher.index("    private sealed class Published")]
    wrappers = wrappers.replace("internal static void Prepare() => Private.PrepareCore();", "")
    network = next(line for line in publisher.splitlines() if "internal static void ResetNetwork()" in line)
    bound["PublisherTick.cs"] = "using System;\nusing System.IO;\nusing System.Collections.Generic;\nusing GloomhavenVR.Net.TownServices;\nusing GloomhavenVR.Cards;\nusing UnityEngine;\nnamespace GloomhavenVR.WorldUI;\ninternal sealed partial class TownServiceSync {\n" + wrappers + "\n" + network + "\n" + "\n".join(expression(publisher, declaration) for declaration in declarations) + "\n" + "\n".join(method(publisher, signature) for signature in signatures) + "\n}\n"
    publish = method(publisher, "private void Publish(string key, Transform? source, Transform? provenance = null, Func<Transform, Transform?>? cloneOf = null, bool prewarm = false)").replace("private void Publish(", "private void PublishNative(", 1)
    bound["PublisherNative.cs"] = "using System;\nusing System.IO;\nusing System.Collections.Generic;\nusing UnityEngine;\nusing GloomhavenVR.Net.TownServices;\nnamespace GloomhavenVR.WorldUI;\ninternal sealed partial class TownServiceSync {\n" + publish + "\n}\n"
    catalog = (base / "WorldUI/TownServices/TownServiceCatalog.cs").read_text()
    bound["CatalogOwnership.cs"] = "using UnityEngine;\nnamespace GloomhavenVR.WorldUI;\ninternal sealed partial class TownServiceCatalog {\n" + method(catalog, "internal static Transform? PresentationOwner(Transform source)") + "\n}\n"
    drawer = (base / "WorldUI/TownServices/TownServiceMerchantDrawer.cs").read_text()
    bound["DrawerTemplates.cs"] = "using System;\nusing UnityEngine;\nusing TMPro;\nusing GloomhavenVR.Net.TownServices;\nnamespace GloomhavenVR.WorldUI;\ninternal sealed partial class TownServiceMerchantDrawer {\n" + "\n".join(method(drawer, signature) for signature in ("internal static GameObject Authored(string name)", "internal static GameObject CreateHousingTemplate()")) + "\n" + expression(drawer, "internal static GameObject CreateTemplate(TMP_Text? font)") + "\n}\n"
    transfer = (base / "Cards/Driver/CardsDriver.5.Interactions.cs").read_text()
    bound["TransferDetector.cs"] = "using System;\nusing UnityEngine;\nusing GloomhavenVR.Core;\nusing GloomhavenVR.Hands;\nusing GloomhavenVR.Hands.Interact;\nnamespace GloomhavenVR.Cards;\ninternal sealed partial class CardsDriver {\n" + "\n".join(method(transfer, signature) for signature in ("private void UpdateHeldCardTransfer()", "private static IFanSweepTarget? HeldTransferable(VRHand? hand)")) + "\n" + expression(transfer, "private const float TransferHoverExitScale") + "\n}\n"
    sweep = (base / "Cards/FanSweep.cs").read_text()
    end = sweep.index("    // ---- election")
    bound["TransferReach.cs"] = sweep[:end] + "}\n"
    hold = (base / "Cards/ItemCardHold.cs").read_text()
    bound["TransferCapability.cs"] = hold[:hold.index("\n/// <summary>", hold.index("internal interface IItemCardHold"))].replace("using GloomhavenVR.Rig;\n", "")
    templates = (base / "WorldUI/TownServices/NativeTemplates.cs").read_text()
    definitions = templates[templates.index("    internal sealed class Part"):templates.index("    private static readonly Dictionary<string, Entry>")]
    template_methods = ("private static void EnsureNativeProp(string key)", "private static void Freeze(string key, Entry entry)",
        "private static void Prune(Transform source, Transform copy)", "private static void Partition(Transform root, string path, List<Part> parts)",
        "internal static string Append(string path, Transform child)", "internal static IReadOnlyList<Part> Parts(string key)",
        "internal static bool Resolve(byte service, ushort template, string address)")
    count = templates[templates.index("    private static int Count("):templates.index("    private static void Partition(")]
    bound["LazyNativeTemplates.cs"] = "using System;\nusing System.IO;\nusing System.Collections.Generic;\nusing TMPro;\nusing UnityEngine;\nusing Object = UnityEngine.Object;\nusing GloomhavenVR.Net.TownServices;\nnamespace GloomhavenVR.WorldUI;\ninternal static partial class LazyTemplateProbe {\n" + definitions + count + "\n".join(method(templates, signature) for signature in template_methods) + "\n}\n"
    town_neutralizer = base / "Net/TownServices/TownServiceNeutralize.cs"
    if town_neutralizer.exists():
        bound[town_neutralizer.name] = town_neutralizer.read_text()
        return bound, {name: hashlib.sha256(text.encode()).hexdigest() for name, text in bound.items()}
    neutral = (base / "Net/Remote/RemoteWidgetMirror.cs").read_text()
    scaffold = "using System;\nusing System.Collections.Generic;\nusing UnityEngine;\nusing UnityEngine.UI;\nusing Object = UnityEngine.Object;\nnamespace GloomhavenVR.Net;\ninternal static class RemoteWidgetMirror {\ninternal enum LayoutOwner { Source, CloneAtBoardOwnersWidth }\n"
    scaffold += method(neutral, "internal static void Neutralize(") + "\n"
    scaffold += expression(neutral, "private static bool IsPresentation(") + "\n"
    scaffold += expression(neutral, "private static bool IsStockLayout(") + "\n"
    scaffold += method(neutral, "private static bool InsideAny(") + "\n"
    scaffold += method(neutral, "private static bool IsSelfOrDescendant(") + "\n}\n"
    bound["Neutralize.cs"] = scaffold
    hashes = {name: hashlib.sha256(text.encode()).hexdigest() for name, text in bound.items()}
    hashes["RemoteWidgetMirror.cs (full source)"] = hashlib.sha256(neutral.encode()).hexdigest()
    return bound, hashes


def main():
    repo = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=repo)
    parser.add_argument("--output-dir", type=Path, default=repo / ".planning/debug/town-service-mirror")
    parser.add_argument("--unity", type=Path, default=Path(os.environ.get("UNITY_PATH", "/home/claw/unity-2021.3.5/Editor/Unity")))
    parser.add_argument("--suite", choices=("basic", "full", "lifecycle", "counter-final", "relocation", "asset-identity", "rack-clock", "catalog-lifetime", "public-catalog", "item-transfer"), default="full")
    parser.add_argument("--no-negative-controls", action="store_true")
    args = parser.parse_args()
    args.source_root = args.source_root.resolve()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    fixture = Path(__file__).resolve().parent / "town-service-mirror-runtime"
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    managed = args.source_root / "ressources/GH_Data/Managed"
    bound, hashes = sources(args.source_root)
    (run / "source-hashes.json").write_text(json.dumps({"root": str(args.source_root.resolve()), "sha256": hashes}, indent=2) + "\n")
    manifest = {"result": str(run / "results.txt"), "evidence": str(run), "suite": args.suite, "cases": []}
    variants = [("production", None, None, None, "")]
    if not args.no_negative_controls:
        variants += [
            ("text", "TownServiceBinding.cs", "tmp.text = text[0];", 'tmp.text = "CORRUPTED";', "owner TMP text survives codec and playback"),
            ("mesh", "TownServiceBinding.cs", "mesh.enabled = n[0] != 0;", "mesh.enabled = true;", "handle mesh enabled state follows owner"),
            ("group", "TownServiceBinding.cs", "cg.alpha = n[1];", "cg.alpha = .1f;", "root CanvasGroup alpha matches owner"),
            ("raycast", "TownServiceBinding.cs", "g.raycastTarget = false;", "g.raycastTarget = true;", "clone graphic raycasts are disabled"),
            ("color", "TownServiceBinding.cs", "g.color = ColorAt(n, 1);", "g.color = Color.red;", "owner and observer rendered UI match: baseline"),
            ("rect-mask", "TownServiceBinding.cs", "clip.padding = new Vector4(n[1], n[2], n[3], n[4]);", "clip.padding = Vector4.zero;", "owner RectMask padding survives playback"),
            ("early-awake", "TownServiceMirror.cs", "Object.Instantiate(original.gameObject, _templateHost.transform, false)", "Object.Instantiate(original.gameObject)", "inactive template never executes gameplay callbacks"),
        ]
        if args.suite == "full":
            variants += [
                ("publisher-rack", "PublisherTick.cs", 'Publish("merchant.rack", rack.HousingRoot);', '// rack omitted', "crank and revolving rack publish their actual moving roots"),
                ("publisher-old-window", "PublisherTick.cs", 'if (service != 1 && catalog == null && TownServicePresentation.Ritual == null)', 'if (true)', "physical counter does not publish suppressed flat merchant window"),
                ("publisher-stale-entry", "PublisherTick.cs", "if (!entry.Current || !entry.Warm) continue;", "// publish stale entry", "physical counter publishes only six current item cards"),
                ("publisher-cardbody", "PublisherTick.cs", 'Publish("merchant.cardbody", entry.BodyRoot, prewarm: true);', '// body omitted', "every original face retains its physical body remotely"),
                ("publisher-held-duplicate", "PublisherTick.cs", 'if (sample.IsPhysical) continue;', '// physical guard omitted', "physical original is not duplicated by generic held publication"),
                ("publisher-price-provenance", "PublisherTick.cs", "entry.RowSource.transform, entry.RowCloneOf", "null, null", "counter price clone retains original row provenance map"),
                ("parent-alpha", "TownServiceMirror.cs", "alpha *= group.alpha;", "alpha *= Mathf.Abs(group.alpha - .37f) < .0001f ? 1f : group.alpha;", "counter opening transports inherited parent alpha"),
                ("canvas", "TownServiceBinding.cs", "canvas.enabled = n[0] != 0;", "canvas.enabled = true;", "false Canvas remains disabled"),
                ("sibling", "TownServiceMirror.cs", "if (reorder) OrderOriginalSiblings(standing);", "if (reorder && standing.Count == 1) OrderOriginalSiblings(standing);", "owner and observer rendered UI match: nested-row-module"),
                ("mask", "TownServiceBinding.cs", "mask.showMaskGraphic = n[1] != 0;", "mask.showMaskGraphic = false;", "owner and observer rendered UI match: dynamic-order-component-mask"),
            ]
    if args.suite == "lifecycle":
        variants = [("production", None, None, None, "")]
        if not args.no_negative_controls:
            variants.append(("cancel-target-restore", "TownServiceMotion.cs",
                "if (_hasTarget)\n            for", "if (_hasTarget && _nodes.Length == 0)\n            for",
                "reopen restores unchanged child target after interrupted tween"))
    if args.suite == "counter-final":
        variants = [("production", None, None, None, "")]
        if not args.no_negative_controls:
            variants += [
                ("preview-nested-card", "PublisherTick.cs", "for (int i = 0; i < original.childCount; i++) PublishCopiedCards(original.GetChild(i), cloneOf);", "// omit nested card traversal", "native detail preview publishes its nested pooled item card"),
                ("preview-provenance", "PublisherTick.cs", "Publish(key, clone, original, cloneOf);", "Publish(key, clone, null, null);", "nested preview retains original card provenance"),
                ("furniture-visibility", "TownServiceMaterial.cs", "value.x = material.GetFloat(name);", "value.x = name == \"_TownVisibility\" ? 1f : material.GetFloat(name);", "remote furniture uses exact owned visibility material value"),
            ]
    if args.suite == "relocation":
        variants = [("production", None, None, None, "")]
        if not args.no_negative_controls:
            variants += [
                ("no-relocation-generation", "PublisherTick.cs", " || _relocationRevision != relocation", "", "dropped invisible frames cannot interpolate across relocation"),
                ("invisible-baseline", "PublisherTick.cs", "if (active && TownServicePresentation.RelocationVisibility <= 0f) return;", "", "first visible relocation state is independently decodable"),
                ("reused-generation", "PublisherTick.cs", "_generation++;", "_generation = session;", "dropped invisible frames cannot interpolate across relocation"),
                ("generation-wrap", "PublisherTick.cs", "if (_generation == uint.MaxValue)", "if (_generation == uint.MaxValue && _generation == 0)", "Missing town-service session frame"),
            ]
    if args.suite == "asset-identity":
        variants = [("production", None, None, None, "")]
        if not args.no_negative_controls:
            variants += [
                ("no-original-identity", "TownServiceAssets.cs", "_originalKeys.Add(id, key); _keys[id] = key; _assets[key] = asset;", "return;", "explicit original sprite replaces its previously cached descriptor key"),
                ("last-window-wins", "TownServiceAssets.cs", "if (_originalKeys.ContainsKey(id)) return;", "if (_originalKeys.ContainsKey(id)) { _keys[id] = key; return; }", "shared original keeps merchant provenance"),
                ("same-backdrop-key", "TownServiceBackdropAssets.cs", '"native-town|backdrop|" + template + "|texture"', '"native-town|backdrop|same|texture"', "Conflicting original town-service provenance"),
                ("retain-cleared-provenance", "TownServiceAssets.cs", "_originalKeys.Clear();", "", "Ambiguous native town-service texture"),
            ]
    if args.suite == "catalog-lifetime":
        variants = [("production", None, None, None, "")]
        if not args.no_negative_controls:
            variants += [
                ("retire-hidden-source", "PublisherTick.cs", "pair.Value.CatalogOwner == null", "true", "valid page cycling preserves the original module namespace"),
                ("reuse-pooled-owner", "PublisherNative.cs", 'if (catalogOwner != null) identity += "/catalog/" + catalogOwner.GetInstanceID();', '// omit physical ownership identity', "pooled native replacement never inherits the previous borrower module ID"),
                ("retain-popup-source", "PublisherTick.cs", "pair.Value.CatalogOwner == null", "false", "ordinary unrelated popup sources retire when unseen"),
            ]
    if args.suite == "rack-clock":
        variants = [("production", None, None, None, "")]
        if not args.no_negative_controls:
            variants += [
                ("rack-phase-alias", "TownServiceMirror.Racks.cs", "float displayed=clock.Turning?TownRackState.Progress(clock.Elapsed):1f;", "float displayed=1f;", "late join reconstructs the actual owner mid-turn phase"),
                ("rack-incomplete-page", "TownServiceMirror.Racks.cs", "clock.Turning&&clock.Waiting&&fromReady&&toReady", "clock.Turning&&clock.Waiting", "missing one dependency keeps the complete outgoing page at rest"),
                ("rack-native-fade", "TownServiceMirror.Racks.cs", "shown?stamp.Alpha:0f", "shown?1f:0f", "page gate preserves independent native ancestor fades"),
                ("rack-hidden-body", "TownServiceMirror.Racks.cs", "renderer.forceRenderingOff=!shown;", "renderer.forceRenderingOff=true;", "incoming physical body appears with its face"),
                ("rack-idle-crank", "TownServiceMirror.Racks.cs", "out var crank)&&replaying)", "out var crank)&&state.Turn!=0)", "idle manual lead pull is not overwritten by the previous clock"),
                ("rack-skipped-epochs", "TownServiceMirror.Racks.cs", "FromPage=joining?state.From:DisplayPage;", "FromPage=state.From;", "skipped owner epochs preserve the actual outgoing front until the opaque midpoint"),
                ("rack-turn-queue", "TownServiceMirror.Racks.cs", "if(!Turning&&Queue.Count==0)", "if(Queue.Count>=0)", "newer queued turn and reordered old packet do not reset an in-flight rack"),
            ]
    if args.suite == "public-catalog":
        variants = [("production", None, None, None, "")]
        if not args.no_negative_controls:
            variants += [
                ("private-public-collision", "TownServiceMirror.cs", "if (frame!.PublicCatalog) { peer = -peer;", "if (frame!.PublicCatalog) { peer = Math.Abs(peer);", "one lowest live stock author is elected"),
                ("unfrozen-inspection-backing", "LazyNativeTemplates.cs", "Freeze(key, bodyEntry); Entries.Add(key, bodyEntry);", "Entries.Add(key, bodyEntry);", "lazy inspection backing has publication partitions on its first request"),
                ("inspection-native-gate", "PublisherTick.cs", "if (!active && !inspection && returns.Count == 0)", "if (!active && returns.Count == 0)", "closed native shop publishes all 512 owned faces and original backings exactly once"),
                ("stale-author-clock", "TownServiceMirror.cs", "if (RemoteRacks.TryGetValue(peer, out var clocks))", "if (peer > 0 && RemoteRacks.TryGetValue(peer, out var clocks))", "authority handoff retains completed observer clock instead of rewinding stale owner sample"),
                ("public-visitor", "TownServiceMirror.cs", "if (peer > 0) VisitorSessions[peer] = Sessions[peer];", "VisitorSessions[peer] = Sessions[peer];", "remote public stock is excluded from visitor census"),
                ("inactive-author", "TownServiceMirror.cs", "int author = PublicLane.Active ? LocalPeer : int.MaxValue;", "int author = LocalPeer;", "departed public owner leaves no stale invisible authority"),
                ("cassette-skip-motion", "TownServiceMirror.Racks.cs", "TownCassetteMotion.Apply(rack.Binding.Root, clock.Turning ? clock.Elapsed / TownRackState.TurnDuration : 1f);", "TownCassetteMotion.Apply(rack.Binding.Root, 1f);", "late public observer reconstructs cassette withdrawal from explicit clock"),
            ]
    if args.suite == "item-transfer":
        variants = [("production", None, None, None, "")]
        if not args.no_negative_controls:
            variants += [
                ("drop-merchant-shape", "TransferDetector.cs", "or IItemCardHold { IsItemCard: true }", "", "either fingertip or palm contact admits transfer"),
                ("require-both-contacts", "TransferDetector.cs", "tipDist > reach.Tip * exit && palmDist > reach.Palm * exit", "tipDist > reach.Tip * exit || palmDist > reach.Palm * exit", "either fingertip or palm contact admits transfer"),
                ("haptic-repeat", "TransferDetector.cs", "if (!wasHovering)", "if (true)", "continued shared hover emits no repeated haptic"),
                ("ignore-closer-ui", "TransferDetector.cs", "if (free.RayUgui.HasHit)", "if (free.RayUgui.HasHit && free.WorldScale < 0)", "nearer native UI retains the trigger"),
                ("ignore-modal", "TransferDetector.cs", " || _modalInputBlocked", "", "modal decision keeps transfer input blocked"),
            ]
    print(f"Production binding: {args.source_root.resolve()}; evidence: {run}", flush=True)
    for name, filename, before, after, expected in variants:
        build = run / name; production = build / "production"; production.mkdir(parents=True)
        for path, text in bound.items():
            if path == filename:
                if text.count(before) != 1: raise RuntimeError("Production mutation binding drift: " + name)
                text = text.replace(before, after, 1)
            (production / path).write_text(text)
        project = build / "Mirror.csproj"; shutil.copyfile(fixture / "Mirror.csproj", project)
        assembly = "TownMirror_" + name.replace("-", "_")
        command = [dotnet, "build", str(project), "-c", "Release", "--nologo", "--verbosity", "quiet",
                   f"-p:CaseName={assembly}", f"-p:FixtureDir={fixture}", f"-p:ProductionDir={production}",
                   f"-p:UnityManaged={args.unity.parent / 'Data/Managed'}",
                   f"-p:UnityUi={managed / 'UnityEngine.UI.dll'}", f"-p:UnityTmp={managed / 'Unity.TextMeshPro.dll'}"]
        result = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (build / "build.log").write_text(result.stdout)
        if result.returncode: print(result.stdout); raise SystemExit("FAIL compilation: " + name)
        manifest["cases"].append({"name": name, "dll": str(build / "bin/Release/netstandard2.1" / (assembly + ".dll")), "expected": expected})
        print("Compiled " + name, flush=True)
    project = run / "unity"; (project / "Assets/Editor").mkdir(parents=True)
    (project / "Packages").mkdir(); (project / "ProjectSettings").mkdir()
    shutil.copyfile(fixture / "Editor/MirrorRunner.cs", project / "Assets/Editor/MirrorRunner.cs")
    # Exercise the actual dissolve/material contract, with no replacement test shader.
    shader = args.source_root / "unity/GloomhavenVR.Assets/Assets/Bundle/TownServices/Shaders/TownNpc.shader"
    shutil.copyfile(shader, project / "Assets/TownNpc.shader")
    (run / "town-shader.sha256").write_text(hashlib.sha256(shader.read_bytes()).hexdigest() + "\n")
    (project / "Packages/manifest.json").write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6"}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    manifest_path = run / "manifest.json"; manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
    command = ["xvfb-run", "-a", str(args.unity), "-batchmode", "-force-glcore", "-projectPath", str(project),
               "-executeMethod", "MirrorRunner.Start", "-mirrorManifest", str(manifest_path), "-logFile", str(run / "unity.log")]
    result = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=300)
    evidence = Path(manifest["result"])
    if evidence.exists(): print(evidence.read_text(), end="")
    if result.returncode or not evidence.exists(): raise SystemExit(f"FAIL Unity exit {result.returncode}; see {run / 'unity.log'}")
    print(f"PASS mirror {args.suite} suite; evidence: {run}")


if __name__ == "__main__": main()
