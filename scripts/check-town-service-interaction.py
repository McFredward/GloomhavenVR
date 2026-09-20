#!/usr/bin/env python3
"""Compile production-bound interaction cases and run them inside Unity 2021.3.5.

No game launch, network service, source mutation or generated tracked files. See
.planning/research/TOWN-SERVICES-INTERACTION-VALIDATION.md for fixture boundaries.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


def method(source, signature):
    start = source.index("    " + signature)
    end = source.index("\n    }", start) + len("\n    }")
    return source[start:end]


def replace_once(source, before, after):
    if source.count(before) != 1:
        raise RuntimeError(f"Production binding drift: expected one occurrence of {before!r}, got {source.count(before)}")
    return source.replace(before, after, 1)


def sources(root):
    base = root / "src/GloomhavenVR"
    paths = {
        "Token.cs": "WorldUI/TownServices/TownServiceToken.cs",
        "Presentation.cs": "WorldUI/TownServices/TownServicePresentation.cs",
        "Handoff.cs": "WorldUI/Modal/ModalFallback.TownServices.cs",
        "Composite.cs": "WorldUI/Modal/ModalFallback.CompositeTransfer.cs",
        "Grabber.cs": "Hands/Interact/ProximityGrabber.cs",
        "Interfaces.cs": "Hands/Interact/IGrabbable.cs",
    }
    raw = {name: (base / path).read_text() for name, path in paths.items()}
    hashes = {paths[name]: hashlib.sha256(text.encode()).hexdigest() for name, text in raw.items()}
    bound = {name: raw[name] for name in ("Token.cs", "Presentation.cs", "Handoff.cs")}
    bound["Composite.cs"] = "using System;\nnamespace GloomhavenVR.WorldUI;\ninternal static partial class ModalFallback {\n" + method(raw["Composite.cs"], "internal static bool ReleaseForComposite(UIWindow window)") + "\n}\n"
    bound["Grabber.cs"] = "using System;\nusing UnityEngine;\nnamespace GloomhavenVR.Hands.Interact;\ninternal partial class ProximityGrabber {\n" + "\n".join(method(raw["Grabber.cs"], sig) for sig in (
        "private void BeginGrab(", "private bool HealDeadHeld()", "internal void CancelAll()")) + "\n}\n"
    # Bind the held branch of the real Tick as well. Its surrounding election/physics and mode
    # policy are outside this fixture; no release-condition or callback logic is reimplemented.
    tick = method(raw["Grabber.cs"], "internal void Tick()")
    start = tick.index("            bool stillHeld =")
    end = tick.index("\n            return;", start)
    bound["ReleaseTick.cs"] = "using UnityEngine;\nnamespace GloomhavenVR.Hands.Interact;\ninternal partial class ProximityGrabber {\ninternal void ReleaseTick() {\nif (Held == null) return;\n" + tick[start:end] + "\n}\n}\n"
    interfaces = []
    for name in ("IGrabbable", "IGrabHighlight", "IGrabbableHandFilter", "ITriggerOnlyGrabbable", "IGrabCancellation"):
        start = raw["Interfaces.cs"].index("internal interface " + name + "\n")
        end = raw["Interfaces.cs"].index("\n}", start) + 2
        interfaces.append(raw["Interfaces.cs"][start:end])
    bound["Interfaces.cs"] = "using UnityEngine;\nnamespace GloomhavenVR.Hands.Interact;\n" + "\n".join(interfaces)
    return bound, hashes


def mutations():
    # Every mutant compiles and must reach the specified runtime assertion. A compile error,
    # unrelated exception or changed source binding cannot count as a rejected negative control.
    return [
        ("owner", "Presentation.cs", "!ReferenceEquals(owner, _selectionOwner)", "false", "owner switch cancels held selection"),
        ("mode", "Presentation.cs", "mode != _selectionMode", "false", "mode switch cancels held selection"),
        ("committed-card", "Presentation.cs", "!ReferenceEquals(card, _selectionCard)", "false", "committed card switch cancels held selection"),
        ("hover-context", "Presentation.cs", "card = shop.selectedCard != null ? shop.selectedCard.AbilityCard : null;", "card = shop.SowingCard != null ? shop.SowingCard.AbilityCard : null;", "BeginGrab hover preserves committed selection context"),
        ("pool-card", "Presentation.cs", "() => slot.AbilityCard != null ? slot.AbilityCard.AbilityCard : null", "() => slot.AbilityCard", "pooled underlying card switch cancels held selection"),
        ("release-context", "Token.cs", "&& ReferenceEquals(_pickedIdentity, _identity()) && ReferenceEquals(_pickedContext, _contextIdentity())", "&& ReferenceEquals(_pickedIdentity, _identity())", "release rechecks context before next tick"),
        ("release-identity", "Token.cs", "&& ReferenceEquals(_pickedIdentity, _identity()) && ReferenceEquals(_pickedContext, _contextIdentity())", "&& ReferenceEquals(_pickedContext, _contextIdentity())", "release rechecks pooled identity before next tick"),
        ("session-fence", "Presentation.cs", "() => Active && _session == session", "() => Active", "captured session mismatch cancels held selection"),
        ("cancel-origin", "Grabber.cs", "if (released is IGrabCancellation cancellation) cancellation.OnGrabCancelled(_hand);\n            else released.OnRelease(_hand, Vector3.zero);", "released.OnRelease(_hand, Vector3.zero);", "synthetic cancel never clicks even on trigger-up frame"),
        ("heal-origin", "Grabber.cs", "if (held is IGrabCancellation cancellation) cancellation.OnGrabCancelled(_hand);\n                else held.OnRelease(_hand, Vector3.zero);", "held.OnRelease(_hand, Vector3.zero);", "hand healing uses explicit cancellation even on trigger-up frame"),
        ("hand-filter", "Token.cs", "(!ReferenceEquals(hand.Grabber.Held, this) || _hand == hand)", "true", "cancelled token releases grabber hand ownership"),
        ("tracking-pose", "Token.cs", "hand.HasPose && hand.TriggerUp", "hand.TriggerUp", "tracking loss never clicks native selection"),
        ("single-click", "Token.cs", "        CancelHold();\n        if (!select", "        // mutant retains held gesture after dispatch\n        if (!select", "real drop dispatches native button exactly once"),
        ("original-parent", "Handoff.cs", "        if (previous.Target.parent != previous.OriginalParent) return false;", "", "wrong original parent refuses handoff"),
        ("null-parent", "Handoff.cs", "        if (previous.Target.parent != previous.OriginalParent) return false;", "        if (previous.OriginalParent != null && previous.Target.parent != previous.OriginalParent) return false;", "null original parent refuses unrelated parent handoff"),
        ("active-owner", "Handoff.cs", "            if (panel.Target == previous.Target) return false;", "            if (panel.Target == null) return false;", "active conversion owner refuses handoff"),
        ("rollback-lifo", "Presentation.cs", "for (int i = Surfaces.Count - 1; i >= 0; i--)", "for (int i = 0; i < Surfaces.Count; i++)", "section rollback restores native hierarchy in LIFO order"),
        ("parent-first", "Presentation.cs", "        if (_window != null && _context != null && _context.IsAlive)\n            ModalFallback.ReleaseForComposite(_window);", "", "context retires before restoring descendant sections"),
        ("native-hidden", "Presentation.cs", "            window.onHidden.AddListener(OnNativeHidden);", "", "same-frame close reopen retires session"),
    ]


def main():
    repo = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=repo, help="Production checkout to bind (read only)")
    parser.add_argument("--output-dir", type=Path, default=repo / ".planning/debug/town-service-interaction")
    parser.add_argument("--unity", type=Path, default=Path(os.environ.get("UNITY_PATH", "/home/claw/unity-2021.3.5/Editor/Unity")))
    parser.add_argument("--unity-ui", type=Path, help="Real UnityEngine.UI.dll (never metadata-only RefAsm)")
    parser.add_argument("--no-negative-controls", action="store_true", help="Quick positive run; not complete validation")
    args = parser.parse_args()
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    fixture = Path(__file__).resolve().parent / "town-service-interaction-runtime"
    ui_candidates = [
        args.source_root / "unity/GloomhavenVR.Assets/Library/ScriptAssemblies/UnityEngine.UI.dll",
        args.source_root / "ressources/GH_Data/Managed/UnityEngine.UI.dll",
    ]
    ui = args.unity_ui or next((path for path in ui_candidates if path.is_file()), None)
    if not args.unity.is_file() or ui is None:
        parser.error("Unity 2021.3.5 and a real UnityEngine.UI.dll are required; pass --unity / --unity-ui")
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    bound, hashes = sources(args.source_root)
    (run / "source-hashes.json").write_text(json.dumps({"root": str(args.source_root.resolve()), "sha256": hashes}, indent=2) + "\n")
    manifest = {"result": str(run / "results.txt"), "cases": []}
    variants = [("production", None, None, None, "")]
    if not args.no_negative_controls:
        variants += mutations()
    print(f"Binding production from {args.source_root.resolve()}; evidence: {run}", flush=True)
    for name, filename, before, after, expected in variants:
        build = run / name
        production = build / "production"
        production.mkdir(parents=True)
        for path, text in bound.items():
            if path == filename:
                text = replace_once(text, before, after)
            (production / path).write_text(text)
        project = build / "Interaction.csproj"
        shutil.copyfile(fixture / "Interaction.csproj", project)
        assembly = "TownInteraction_" + name.replace("-", "_")
        command = [dotnet, "build", str(project), "--configuration", "Release", "--nologo", "--verbosity", "quiet",
                   f"-p:CaseName={assembly}", f"-p:FixtureDir={fixture}", f"-p:ProductionDir={production}",
                   f"-p:UnityManaged={args.unity.parent / 'Data/Managed'}", f"-p:UnityUi={ui.resolve()}"]
        compiled = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        (build / "build.log").write_text(compiled.stdout)
        if compiled.returncode:
            print(compiled.stdout)
            raise SystemExit(f"FAIL: {name} did not compile (not a successful negative control)")
        manifest["cases"].append({"name": name, "dll": str(build / "bin/Release/netstandard2.1" / (assembly + ".dll")), "expected": expected})
        print(f"Compiled {name}", flush=True)
    manifest_path = run / "manifest.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n")
    project = run / "unity"
    (project / "Assets/Editor").mkdir(parents=True)
    (project / "Packages").mkdir()
    (project / "ProjectSettings").mkdir()
    shutil.copyfile(fixture / "Editor/InteractionRunner.cs", project / "Assets/Editor/InteractionRunner.cs")
    (project / "Packages/manifest.json").write_text('{"dependencies":{"com.unity.ugui":"1.0.0"}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    log = run / "unity.log"
    command = [str(args.unity), "-batchmode", "-nographics", "-projectPath", str(project),
               "-executeMethod", "InteractionRunner.Start", "-interactionManifest", str(manifest_path), "-logFile", str(log)]
    completed = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=240)
    result = Path(manifest["result"])
    if result.exists():
        print(result.read_text(), end="")
    if completed.returncode or not result.exists():
        print(f"FAIL: Unity exit {completed.returncode}; log: {log}")
        raise SystemExit(1)
    print(f"PASS: {len(variants)} production/negative variants; evidence: {run}")


if __name__ == "__main__":
    main()
