#!/usr/bin/env python3
"""Run production town-service capture/codec/playback and render comparisons in Unity 2021.3.5."""
import argparse
import hashlib
import importlib.util
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
             "TownServiceFrame", "TownCatalogBank", "TownCatalogBank.Headers", "TownCatalogClock", "TownServiceMirror.CatalogBank", "TownServiceMirror.CatalogWarm", "TownServiceMirror.NativeTemplateState", "TownServiceMirror.NativePublication", "TownRackState", "TownCatalogLayout", "TownCassetteMotion", "TownServiceMirror.Racks", "TownServiceMirror.PublicVisibility", "TownServiceMirror.Offerings", "TownServiceMirror.Voice", "TownServiceMaterial", "TownServiceFlameClock", "TownServiceMirror"]
    bound = {name + ".cs": (base / "Net/TownServices" / (name + ".cs")).read_text() for name in names}
    bound['NativePurse.cs'] = (root / 'scripts/town-purse-runtime/NativePurse.cs').read_text()
    bound['TownServicePursePresentation.cs'] = (base / 'WorldUI/TownServices/TownServicePursePresentation.cs').read_text()
    bound['BundleShaders.cs'] = (base / 'Core/BundleShaders.cs').read_text()
    bound["PresentationCompression.cs"] = (base / "Net/PresentationCompression.cs").read_text()
    bound["NetPacket.cs"] = (base / "Net/NetPacket.cs").read_text()
    stock = base / "Net/TownServices/TownServiceMirror.Stock.cs"
    if stock.exists(): bound[stock.name] = stock.read_text()
    merchant_control = base / "Net/TownServices/TownServiceMirror.MerchantControl.cs"
    if merchant_control.exists(): bound[merchant_control.name] = merchant_control.read_text()
    native_publication = base / "Net/TownServices/TownServiceMirror.NativePublication.cs"
    if native_publication.exists(): bound[native_publication.name] = native_publication.read_text()
    for fast in ("TownServiceFastNumbers", "TownServiceMotionCodec", "TownServiceMotionBudget", "TownServiceMirror.Motion"):
        path = base / "Net/TownServices" / (fast + ".cs")
        if path.exists(): bound[path.name] = path.read_text()
    pad = base / "Hands/Interact/PokeOnlyTarget.cs"
    if pad.exists(): bound[pad.name] = pad.read_text()
    stock_publisher = base / "WorldUI/TownServices/TownServiceSync.Stock.cs"
    if stock_publisher.exists(): bound[stock_publisher.name] = stock_publisher.read_text()
    face = base / "WorldUI/TownServices/TownServiceCardFace.cs"
    if face.exists(): bound[face.name] = face.read_text()
    offering = base / "WorldUI/TownServices/TownServiceOfferingPose.cs"
    bound[offering.name] = offering.read_text()
    cabinet_audio = base / "WorldUI/TownServices/TownServiceCabinetAudio.cs"
    bound[cabinet_audio.name] = cabinet_audio.read_text()
    template_assets = base / "WorldUI/TownServices/TownServiceTemplateAssets.cs"
    if template_assets.exists(): bound[template_assets.name] = template_assets.read_text()
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
        "private void PruneSources()", "private void ResetCore()", "private bool Visible(SourceEntry entry)", "private bool IsPriority(Transform source)")
    declarations = ("private uint _generation", "private ulong _relocationRevision", "private bool _generationExhausted")
    wrappers = publisher[publisher.index("    private static readonly TownServiceSync Private"):publisher.index("    private sealed class Published")]
    wrappers = wrappers.replace("internal static void Prepare() => Private.PrepareCore();", "")
    network = next(line for line in publisher.splitlines() if "internal static void ResetNetwork()" in line)
    bound["PublisherTick.cs"] = "using System;\nusing System.IO;\nusing System.Collections.Generic;\nusing GloomhavenVR.Hands;\nusing GloomhavenVR.Net;\nusing GloomhavenVR.Net.TownServices;\nusing GloomhavenVR.Cards;\nusing UnityEngine;\nusing UnityEngine.UI;\nnamespace GloomhavenVR.WorldUI;\ninternal sealed partial class TownServiceSync {\n" + wrappers + "\n" + network + "\n" + "\n".join(expression(publisher, declaration) for declaration in declarations) + "\n" + "\n".join(method(publisher, signature) for signature in signatures) + "\n}\n"
    publish = method(publisher, "private void Publish(string key, Transform? source, Transform? provenance = null, Func<Transform, Transform?>? cloneOf = null, bool prewarm = false)").replace("private void Publish(", "private void PublishNative(", 1)
    bound["TownServiceSync.CatalogBank.cs"] = (base / "WorldUI/TownServices/TownServiceSync.CatalogBank.cs").read_text()
    bound["PublisherNative.cs"] = "using System;\nusing System.IO;\nusing System.Collections.Generic;\nusing UnityEngine;\nusing GloomhavenVR.Net.TownServices;\nnamespace GloomhavenVR.WorldUI;\ninternal sealed partial class TownServiceSync {\n" + publish + "\n}\n"
    catalog = (base / "WorldUI/TownServices/TownServiceCatalog.cs").read_text()
    # Count entry into the exact native ownership walk without replacing its lookup
    # or lifetime rules; the dormant-census probe isolates real PruneSources calls.
    ownership = method(catalog, "internal static Transform? PresentationOwner(Transform source)").replace(
        "\n    {\n", "\n    {\n        FixtureOwnershipChecks++;\n", 1)
    bound["CatalogOwnership.cs"] = "using UnityEngine;\nnamespace GloomhavenVR.WorldUI;\ninternal sealed partial class TownServiceCatalog {\ninternal static int FixtureOwnershipChecks;\n" + ownership + "\n}\n"
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
    bound["NativeTemplatePaths.cs"] = "using System;\nusing UnityEngine;\nnamespace GloomhavenVR.WorldUI;\ninternal static partial class NativeTemplates {\n" + method(templates, "internal static string Append(string path, Transform child)") + "\n}\n"
    bound["NativeDynamicBoundary.cs"] = "using UnityEngine;\nnamespace GloomhavenVR.WorldUI;\ninternal static partial class NativeTemplates {\n" + expression(templates, "internal static bool IsDynamic(Transform node)") + "\n}\n"
    definitions = templates[templates.index("    internal sealed class Part"):templates.index("    private static readonly Dictionary<string, Entry>")]
    template_methods = ("private static Transform? OriginalMapBacking()", "private static void EnsureNativeProp(string key)", "private static void Freeze(string key, Entry entry)",
        "private static void Prune(Transform source, Transform copy)", "private static void Partition(Transform root, string path, List<Part> parts)",
        "internal static string Append(string path, Transform child)", "internal static IReadOnlyList<Part> Parts(string key)",
        "internal static bool Resolve(byte service, ushort template, string address)")
    count = templates[templates.index("    private static int Count("):templates.index("    private static void Partition(")]
    bound["LazyNativeTemplates.cs"] = "using System;\nusing System.IO;\nusing System.Collections.Generic;\nusing TMPro;\nusing UnityEngine;\nusing Object = UnityEngine.Object;\nusing GloomhavenVR.Net.TownServices;\nnamespace GloomhavenVR.WorldUI;\ninternal static partial class LazyTemplateProbe {\n" + definitions + count + "\n".join(method(templates, signature) for signature in template_methods) + "\n}\n"
    town_neutralizer = base / "Net/TownServices/TownServiceNeutralize.cs"
    if town_neutralizer.exists():
        bound[town_neutralizer.name] = town_neutralizer.read_text()
        hashes = {name: hashlib.sha256(text.encode()).hexdigest() for name, text in bound.items()}
        claim_helper = root / "scripts/bind-town-public-claim.py"
        if claim_helper.exists():
            spec = importlib.util.spec_from_file_location("town_public_claim_binding", claim_helper)
            helper = importlib.util.module_from_spec(spec); spec.loader.exec_module(helper)
            extra, extra_hashes = helper.sources(root); bound.update(extra); hashes.update(extra_hashes)
        return bound, hashes
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
    claim_helper = root / "scripts/bind-town-public-claim.py"
    if claim_helper.exists():
        spec = importlib.util.spec_from_file_location("town_public_claim_binding", claim_helper)
        helper = importlib.util.module_from_spec(spec); spec.loader.exec_module(helper)
        extra, extra_hashes = helper.sources(root)
        bound.update(extra); hashes.update(extra_hashes)
    return bound, hashes


def main():
    repo = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=repo)
    parser.add_argument("--output-dir", type=Path, default=repo / ".planning/debug/town-service-mirror")
    parser.add_argument("--fixture-dir", type=Path, default=repo / "scripts/town-service-mirror-runtime",
                        help="Runtime fixture to bind when resuming an integrated checkout's control")
    parser.add_argument("--unity", type=Path, default=Path(os.environ.get("UNITY_PATH", "/home/claw/unity-2021.3.5/Editor/Unity")))
    parser.add_argument("--suite", choices=("basic", "full", "lifecycle", "counter-final", "relocation", "asset-identity", "rack-clock", "catalog-lifetime", "public-catalog", "voice-relay", "shared-interaction", "item-transfer", "motion-fast"), default="full")
    parser.add_argument("--bank-controls", action="store_true", help="Run only production plus prepared cabinet bank counterfactuals")
    parser.add_argument("--no-negative-controls", action="store_true")
    parser.add_argument("--only-mutation", help="Run production plus one selected negative control after a focused fixture fix")
    parser.add_argument("--skip-production", action="store_true", help="Resume only --only-mutation when production already passed on the same source tree")
    args = parser.parse_args()
    if args.skip_production and not args.only_mutation:
        parser.error("--skip-production requires --only-mutation")
    args.source_root = args.source_root.resolve()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    fixture = args.fixture_dir.resolve()
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    managed = args.source_root / "ressources/GH_Data/Managed"
    bound, hashes = sources(args.source_root)
    native_python = Path(os.environ.get('UNITYPY_PYTHON', str(Path.home() / 'unitypy-venv/bin/python')))
    native_purse = run / 'native-purse.json'
    subprocess.run([str(native_python), str(fixture.parent / 'town-purse-runtime/export-native.py'),
        str(args.source_root), str(native_purse)], check=True)
    (run / "source-hashes.json").write_text(json.dumps({"root": str(args.source_root.resolve()), "sha256": hashes}, indent=2) + "\n")
    manifest = {"result": str(run / "results.txt"), "evidence": str(run), "suite": args.suite, "cases": []}
    variants = [("production", None, None, None, "")]
    if not args.no_negative_controls:
        variants += [
            ("offering-heartbeat", "TownServiceMirror.cs", "module.NextRefresh = now + (NeedsHeartbeat(module) ? .75f", "module.NextRefresh = now + (module.Id == _heartbeatModule ? .75f", "completed unchanged offering renews within its three-second lifetime independently of module allocation"),
            ("offering-inactive", "TownServiceMirror.Offerings.cs", "if (visible && age <= OfferingFreshSeconds)", "if (age <= OfferingFreshSeconds)", "owner withdrawal closes the shared palm before any asset playback"),
            ("offering-stale", "TownServiceMirror.Offerings.cs", "age <= OfferingFreshSeconds", "true", "other fresh modules cannot preserve stale offering intent"),
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
                ("publisher-stale-entry", "PublisherTick.cs", "if (!entry.Current || !entry.Warm || entry.Sample.IsMoving) continue;", "if (entry.Sample.IsMoving) continue;", "physical counter publishes only six current item cards"),
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
                ("retain-popup-source", "PublisherTick.cs", "pair.Value.CatalogOwner == null", "false", "retired private popup leaves public catalog identities unchanged"),
                ("dormant-parent-every-frame", "PublisherTick.cs", "if (pair.Value.CatalogResident && now < pair.Value.OwnershipCheckAfter) continue;", "// omit dormant ownership census cache", "prepared dormant originals perform zero repeated native ownership walks between censuses"),
                ("dormant-parent-never-expires", "PublisherTick.cs", "if (pair.Value.CatalogResident && now < pair.Value.OwnershipCheckAfter) continue;", "if (pair.Value.CatalogResident) continue;", "expired dormant ownership census retires a live original whose real catalog owner was removed"),
                ("dormant-destroyed-retained", "PublisherTick.cs", "if (pair.Key == null)", "if (pair.Key == null && !pair.Value.CatalogResident)", "destroyed prepared original sources and registered bank modules retire immediately before census expiry"),
                ("pooled-bank-borrower-retained", "PublisherNative.cs", "if (previous.CatalogResident)", "if (false && previous.CatalogResident)", "pooled prepared native card retires its former resident module before a new borrower publishes"),
            ]
    if args.suite == "rack-clock":
        variants = [("production", None, None, None, "")]
        if not args.no_negative_controls:
            variants += [
                ("rack-dead-host", "TownServiceMirror.cs", "RetireDestroyedRemoteModules(entry.Key, standing);", "// disabled dead-host recovery", "destroyed rack and child recover from retained owner frames"),
                ("rack-phase-alias", "TownServiceMirror.Racks.cs", "float displayed=replaying?TownRackState.Progress(clock.Elapsed):1f;", "float displayed=1f;", "missing one dependency preserves the owner's current intermediate rack pose"),
                ("rack-incomplete-page", "TownServiceMirror.Racks.cs", "bool complete=RackPageReady(pair.Key,clock.DisplayPage==clock.FromPage?clock.Outgoing??state:state,clock.DisplayPage,modules,peer);", "bool complete=true;", "owner replacement boundary hides the obsolete page and every incomplete target fragment"),
                ("rack-owner-age-loss", "TownServiceMirror.Racks.cs", "float ownerAge=Mathf.Clamp(state.Elapsed+Mathf.Max(0f,now-clock.ReceivedTime)", "float ownerAge=Mathf.Clamp(Mathf.Max(0f,now-clock.ReceivedTime)", "missing one dependency preserves the owner's current intermediate rack pose"),
                ("rack-native-fade", "TownServiceMirror.Racks.cs", "shown?stamp.Alpha:0f", "shown?1f:0f", "page gate preserves independent native ancestor fades"),
                ("rack-hidden-body", "TownServiceMirror.Racks.cs", "renderer.forceRenderingOff=!shown;", "renderer.forceRenderingOff=true;", "incoming physical body appears with its face"),
                ("rack-idle-crank", "TownServiceMirror.Racks.cs", "out var crank)&&crank.Alive&&replaying)", "out var crank)&&crank.Alive&&state.Turn!=0)", "idle manual lead pull is not overwritten by the previous clock"),
                ("rack-skipped-epochs", "TownServiceMirror.Racks.cs", "clock.DisplayPage=clock.Turning&&TownRackState.Progress(clock.Elapsed)<.5f?state.From:state.To;", "clock.DisplayPage=clock.Turning&&TownRackState.Progress(clock.Elapsed)<.5f?clock.DisplayPage:state.To;", "skipped owner epochs adopt the actual current original from-page and animation"),
                ("rack-turn-queue", "TownServiceMirror.Racks.cs", "Start(next,now);", "if (!Turning) Start(next,now);", "newest completed owner clock replaces obsolete turns despite a reordered old packet"),
            ]
    if args.suite == "public-catalog":
        variants = [("production", None, None, None, "")]
        if not args.no_negative_controls:
            variants += [
                ("public-claim-artwork-lock", "PublicMerchantClaim.cs", "if (_catalog == null || TownServiceMirror.PublicRack is not TownRackState state) return;",
                 "if (_catalog == null || !TownServiceMirror.HasReadyPublicPresentation || TownServiceMirror.PublicRack is not TownRackState state) return;",
                 "peer physical category button adopts the public page despite missing artwork and a separate merchant transaction"),
                ("public-category-no-claim", "PublicMerchantClaim.cs", "TownServicePublicMerchant.TrySelectCategory(_rack, _category)",
                 "Time.unscaledTime < 0f",
                 "visitor category press sends one reliable intent without replacing the prepared bank author"),
                ("public-declined-claim", "PublicMerchantClaim.cs", "internal static void Claim() { }",
                 "internal static void Claim() { TownServiceMirror.ClaimPublicCatalog(); }",
                 "visitor inspection preserves the prepared bank author before any reliable cabinet input"),
                ("public-declined-page-claim", "PublicMerchantClaim.cs", "return TownMerchantControlSync.RequestPage(direction);",
                 "return false;",
                 "a peer page request keeps the shared category and starts the exact native page animation"),
                ("foley-mutates-rack", "TownServiceCabinetAudio.cs", "host.transform.SetParent(_anchor.parent, false);", "host.transform.SetParent(_anchor, false);", "first category sound does not change original rack topology"),
                ("catalog-layout-wire-loss", "TownServiceCodec.cs", "byte[] layout = frame.Rack?.Layout != null ? TownCatalogLayout.Write(frame.Rack.Layout) : Array.Empty<byte>();", "byte[] layout = Array.Empty<byte>();", "full owner cabinet layout survives original public module capture and additive wire records"),
                # Category input no longer consumes the legacy clock Accessible field. Lock the
                # real production callback on incomplete public artwork to reproduce the defect.
                ("pending-rack-input-lock", "PublicMerchantClaim.cs", "if (!_available() || Time.unscaledTime - _lastPressed < .3f) return;",
                 "if (!_available() || !TownServiceMirror.HasReadyPublicPresentation || Time.unscaledTime - _lastPressed < .3f) return;",
                 "visitor category press sends one reliable intent without replacing the prepared bank author"),
                ("donation-received-clock", "TownServiceMirror.cs", "? Time.unscaledTime - frame.TempleDonationCommitAge", "? Time.unscaledTime", "remote donation keeps its owner's commit age instead of starting a new blessing on receipt"),
                ("async-cabinet-epoch-spent", "TownServiceCabinetAudio.cs", "else StartPending();", "else _pending = false;", "late-loaded cabinet clip joins its pending owner epoch at the current sound phase"),
                ("decision-step", "TownServiceMotion.cs", "? Mathf.Clamp(sampleInterval * 1.1f, 1f / 90f, .25f)",
                 "? Mathf.Clamp(sampleInterval, 1f / 90f, .1f)", "palm decision rotates continuously between 5 Hz owner samples"),
                ("private-public-collision", "TownServiceMirror.cs", "peer = -peer; _observedPublicClaim", "peer = Math.Abs(peer); _observedPublicClaim", "late public author receives every cold-page physical slot instead of its local pool order"),
                ("unfrozen-inspection-backing", "LazyNativeTemplates.cs", "Freeze(key, bodyEntry); Entries.Add(key, bodyEntry);", "Entries.Add(key, bodyEntry);", "lazy inspection backing has publication partitions on its first request"),
                ("inspection-native-gate", "PublisherTick.cs", "if (!active && !inspection && returns.Count == 0)", "if (!active && returns.Count == 0)", "closed native shop publishes all 512 owned faces and original backings exactly once"),
                ("stale-author-clock", "TownServiceMirror.cs", "if (RemoteRacks.TryGetValue(peer, out var clocks))", "if (peer > 0 && RemoteRacks.TryGetValue(peer, out var clocks))", "missing observer artwork never permanently disables the local public input proxy"),
                ("public-visitor", "TownServiceMirror.cs", "if (peer > 0) VisitorSessions[peer] = Sessions[peer];", "VisitorSessions[peer] = Sessions[peer];", "remote public stock is excluded from visitor census"),
                ("inactive-author", "TownServiceMirror.cs", "int author = PublicLane.Active ? LocalPeer : int.MaxValue;", "int author = LocalPeer;", "departed public owner leaves no stale invisible authority"),
                ("roller-missing-direction", "TownServiceMirror.Racks.cs", "TownCassetteMotion.Apply(rack.Binding.Root, replaying ? clock.Elapsed / TownRackState.TurnDuration : 1f, state.ScrollDirection);", "TownCassetteMotion.Apply(rack.Binding.Root, replaying ? clock.Elapsed / TownRackState.TurnDuration : 1f, 0);", "late observer reconstructs exact owner holder translation and hinge angle in either scroll direction"),
                ("cassette-skip-motion", "TownServiceMirror.Racks.cs", "TownCassetteMotion.Apply(rack.Binding.Root, replaying ? clock.Elapsed / TownRackState.TurnDuration : 1f, state.ScrollDirection);", "TownCassetteMotion.Apply(rack.Binding.Root, 1f, 0);", "actual peer category callback preserves the same intermediate authored cassette motion remotely"),
                ("secondary-item-erasure", "TownServiceMirror.cs", "(session.Service == 1 || session.Service == 2 || session.Service == 3);", "(session.Service == 2 || session.Service == 3);", "non-elected visitor's original held item face and backing remain visible to third player"),
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
    if args.suite == "shared-interaction":
        variants = [("production", None, None, None, "")]
        if not args.no_negative_controls:
            variants += [
                ("source-wire-session", "PublisherTick.cs", "TownServiceMirror.LocalOwnsInteraction(service, Private._generation)", "TownServiceMirror.LocalOwnsInteraction(service, sourceSession)", "local interaction ownership uses the current wire generation after service switches"),
                # The transaction election has the same tie expression; mutate only the visual lease.
                ("highest-player-wins", "TownServiceMirror.cs", 'if (pair.Key <= 0 || !session.Active || session.Service != service\n                || now - session.LastSeenTime > NetProtocol.StaleTimeoutSeconds) continue;\n            float age = session.SessionAge + Mathf.Max(0f, now - session.ReceivedTime);\n            if (owner == 0 || age > oldestAge + .05f\n                || Mathf.Abs(age - oldestAge) <= .05f && pair.Key < owner)', 'if (pair.Key <= 0 || !session.Active || session.Service != service\n                || now - session.LastSeenTime > NetProtocol.StaleTimeoutSeconds) continue;\n            float age = session.SessionAge + Mathf.Max(0f, now - session.ReceivedTime);\n            if (owner == 0 || age > oldestAge + .05f\n                || Mathf.Abs(age - oldestAge) <= .05f && pair.Key > owner)', "simultaneous resident claims use the deterministic player-ID tie break"),
                ("nonowner-author", "TownServiceMirror.cs", "|| InteractionOwner(service) != player", "|| false", "only the elected visitor session can author shared interaction state"),
                ("ignore-temple-owner", "TownServiceMirror.cs", "int owner = InteractionOwner(2);", "int owner = VisitorSessions.Count > 0 ? 3 : 0;", "disconnect releases only that player's resident leases"),
                ("missing-art-blink", "TownServiceMirror.cs", "if (!sameOriginal) { module.Host.SetActive(false); module.Motion.Reset(); }", "if (true) { module.Host.SetActive(false); module.Motion.Reset(); }", "a missing next dependency retains the last validated original front instead of blinking grey"),
                ("public-control-readiness", "TownServiceMirror.PublicVisibility.cs", "if (!PublicPageReady(pair.Key, state, state.To, session, modules)) continue;", "if (Time.unscaledTime < 0f) continue;", "new remote author retains the previous complete cabinet while one real original price is missing"),
                ("public-off-page-readiness", "TownServiceMirror.PublicVisibility.cs", "if (PublicPagedModules.Contains(id)) continue;", "if (Time.unscaledTime < 0f) continue;", "current public stock commits a populated original cabinet with its category key and crank"),
            ]
    if args.suite == "motion-fast":
        variants = [("production", None, None, None, "")]
        if not args.no_negative_controls:
            variants += [
                ("closed-fan-art-deferred", "PublisherTick.cs", 'Publish("item." + chip.Item.ID.ToString(System.Globalization.CultureInfo.InvariantCulture), face, prewarm: true);', 'Publish("item." + chip.Item.ID.ToString(System.Globalization.CultureInfo.InvariantCulture), face);', "closed owner item fan publishes complete original fronts with genuine hidden visibility before reveal"),
                ("numeric-art-backlog", "TownServiceMirror.Motion.cs", "if (slot.Entry.Kind is 2 or 4) PatchMotionProperty(frame, slot.Entry);", "if (slot.Entry.Kind == 250) PatchMotionProperty(frame, slot.Entry);", "composing fast root scroll and hover preserves every simultaneous original property"),
                ("rig-attachment-cadence", "TownServiceMirror.Motion.cs", "root != null && root.Entry.Hand != 0 && composed.Merged != null", "root != null && root.Entry.Hand == 250 && composed.Merged != null", "held original purse follows the approved smoothed rig between network events"),
                ("canvas-attachment-cadence", "TownServiceMirror.Motion.cs", "else if (entry.CanvasOnHand)", "else if (!entry.CanvasOnHand)", "enclosing original canvas follows the same approved smoothed rig between events"),
                ("purse-hand-style-scale", "TownServiceMirror.Motion.cs", "root.Pose = ReadMotionHandPose(module.Binding.Root, hand);", "root.Pose = ReadPose(module.Binding.Root, hand.Rig.Root);", "original purse world size and wrist offset survive the owner hand style"),
            ]
    if args.suite == "voice-relay":
        variants = [("production", None, None, None, "")]
        if not args.no_negative_controls:
            variants.append(("missing-enchantress-inspection", "TownServiceMirror.Voice.cs",
                "|| reaction == TownVoiceReaction.EnchantressInspect", "",
                "accepted enchantment card inspections use the private synchronized voice relay"))
            variants.append(("missing-avatar-stock-source", "TownServiceMirror.Stock.cs",
                "bool found = NetAvatarDriver.TryGetTownHeldStock(RealPeer(key), wanted, out int avatarItem);",
                "bool found = false; int avatarItem = 0;",
                "atomic avatar stock provenance preserves public vacancy and pickup voice with zero heldstock modules"))
    if args.bank_controls:
        if args.suite != "public-catalog": parser.error("--bank-controls requires --suite public-catalog")
        variants = [("production", None, None, None, ""),
            ("catalog-bank-no-updates", "TownServiceMirror.CatalogBank.cs", "Members = refs, Updates = updates", "Members = refs, Updates = Array.Empty<TownServiceFrame>()", "prepared cold far category stays fully populated at owner boundary"),
            ("catalog-bank-retire-dormant", "PublisherTick.cs", "module.Seen = TownServiceMirror.IsPublicAuthor && module.CatalogResident;", "module.Seen = false;", "prepared cold far category stays fully populated at owner boundary"),
            ("catalog-bank-cache-wrong-content", "TownServiceMirror.CatalogBank.cs", "SameBankMembers(cache.Last.Members, refs)", "cache.Last.Members.Length == refs.Length", "genuinely changed original price is installed atomically"),
            ("catalog-bank-ignore-preparation", "TownServiceMirror.CatalogBank.cs", "Prepared = cache.Prepared, Members = refs", "Prepared = true, Members = refs", "partial dormant preparation never claims a complete original bank"),
            ("catalog-bank-parent-binding", "TownServiceMirror.CatalogBank.cs", "Array.IndexOf(CatalogOriginalBinding(parent).Bindings, update.ParentBinding) < 0", "false", "missing original cabinet parent binding is rejected before any atomic pending or baseline mutation"),
            ("catalog-bank-root-topology", "TownServiceMirror.CatalogBank.cs", "CatalogOriginalBinding(root).Validate(root, Assets);", "_ = CatalogOriginalBinding(root);", "missing original cabinet root topology is rejected before any atomic pending or baseline mutation"),
            ("catalog-bank-peer-recycle", "TownServiceMirror.cs", "ClearCatalogPeer(peer); ReceivedCatalogBanks.Remove(peer);", "ReceivedCatalogBanks.Remove(peer);", "departed public peer clears original content keys before peer identity can be recycled"),
            ("catalog-bank-delayed-clock", "TownServiceMirror.Racks.cs", "clock.ReceivedTime=Mathf.Min(clock.ReceivedTime,now-Mathf.Max(0f,", "clock.ReceivedTime=Mathf.Min(clock.ReceivedTime,now-0f*Mathf.Max(0f,", "delayed atomic bank seeks newer same-owner manifest age without replaying an obsolete rack turn"),
        ]
    print(f"Production binding: {args.source_root.resolve()}; evidence: {run}", flush=True)
    if args.only_mutation:
        selected = [case for case in variants if case[0] == args.only_mutation]
        if len(selected) != 1 or args.only_mutation == "production":
            parser.error("--only-mutation must name an enabled negative control in this suite")
        variants = selected if args.skip_production else variants[:1] + selected
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
    many = 'Shader "GVR/TownManyProps" { Properties {\n' + ''.join(
        f'_P{i:02d} ("P{i:02d}", Float) = 0\n' for i in range(65))
    many += '} SubShader { Pass { Color (1,1,1,1) } } }\n'
    (project / "Assets/TownManyProps.shader").write_text(many)
    (run / "town-shader.sha256").write_text(hashlib.sha256(shader.read_bytes()).hexdigest() + "\n")
    (project / "Packages/manifest.json").write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6"}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    manifest_path = run / "manifest.json"; manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
    command = ["xvfb-run", "-a", str(args.unity), "-batchmode", "-force-glcore", "-projectPath", str(project),
               "-executeMethod", "MirrorRunner.Start", "-mirrorManifest", str(manifest_path), "-logFile", str(run / "unity.log")]
    command += ['-nativePurseData', str(native_purse)]
    result = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=300)
    evidence = Path(manifest["result"])
    if evidence.exists(): print(evidence.read_text(), end="")
    if result.returncode or not evidence.exists(): raise SystemExit(f"FAIL Unity exit {result.returncode}; see {run / 'unity.log'}")
    print(f"PASS mirror {args.suite} suite; evidence: {run}")


if __name__ == "__main__": main()
