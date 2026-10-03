#!/usr/bin/env python3
"""Render production palm controls and occupancy identity beside the original book.

This is a compact geometry/identity fixture, not a replacement for headset evidence.
Native decision content and reliable leases are explicit boundaries; Unity canvas,
actual original book vertices, production layout/badge/OwnerTag and TMP are real.
"""
import argparse
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile


def load(path):
    spec = importlib.util.spec_from_file_location("palm_interaction", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def remove_block(text, start):
    first = text.index(start)
    brace = text.index("{", first)
    depth = 1
    end = brace + 1
    while depth:
        depth += (text[end] == "{") - (text[end] == "}")
        end += 1
    return text[:first] + text[end:]


def main():
    repo = Path(__file__).resolve().parent.parent
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--source-root", type=Path, default=repo)
    parser.add_argument("--identity-root", type=Path, help="Optional integration checkout supplying the new badge and OwnerTag")
    parser.add_argument("--output-dir", type=Path, default=repo / ".planning/debug/town-palm-render")
    parser.add_argument("--unity", type=Path, default=Path(os.environ.get("UNITY_PATH", "/home/claw/unity-2021.3.5/Editor/Unity")))
    parser.add_argument("--font", type=Path, default=Path("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf"), help="Local TTF for readable fixture-only captions")
    args = parser.parse_args()
    identity = args.identity_root or args.source_root
    args.output_dir.mkdir(parents=True, exist_ok=True)
    run = Path(tempfile.mkdtemp(prefix="run-", dir=args.output_dir.resolve()))
    bridge = load(repo / "scripts/check-town-service-interaction.py")
    production, hashes = bridge.sources(args.source_root)
    base = identity / "src/GloomhavenVR"
    for filename in ("Net/Avatar/OwnerTag.cs", "WorldUI/TownServices/TownServiceOccupationBadge.cs"):
        text = (base / filename).read_text()
        production[Path(filename).name] = text
        hashes[filename] = hashlib.sha256(text.encode()).hexdigest()
    board = (base / "Net/Board/BoardVisual.cs").read_text()
    tmpfit = (base / "Core/TmpFit.cs").read_text()
    remote = (base / "Net/Remote/RemoteNameTag.cs").read_text()
    cap = (base / "Cards/Caps/CapFaceLayout.cs").read_text()
    constants = "".join(line.replace("internal ", "") for line in cap.splitlines()
        if line.strip().startswith("internal const float MinFontSize =")
        or line.strip().startswith("internal const float CapPerMeterHeight ="))
    production["IdentityVisual.cs"] = ("using TMPro; using UnityEngine; namespace GloomhavenVR.Net { internal static class BoardVisual { "
        + bridge.method(board, "internal static Material Unlit(")
        + bridge.method(board, "internal static MeshRenderer Quad(")
        + "internal static void OrderWithPanels(Renderer[] r,float distance){} } internal static class RemoteNameTag { const float InkPad=.004f;"
        + bridge.method(remote, "internal static float MeasureInkWidth(") + "} } namespace GloomhavenVR.Core { internal static class TmpFit {" + constants
        + bridge.method(tmpfit, "internal static void Fit(") + "} }")
    for name, text in (("BoardVisual.cs", board), ("TmpFit.cs", tmpfit), ("RemoteNameTag.cs", remote), ("CapFaceLayout.cs", cap)):
        hashes[name] = hashlib.sha256(text.encode()).hexdigest()
    boundaries = (repo / "scripts/town-service-interaction-runtime/Boundaries.cs").read_text()
    boundaries = remove_block(boundaries, "namespace TMPro")
    boundaries = boundaries.replace("public static void Warn(string a,string b) {}", "public static void Warn(string a,string b) { throw new System.InvalidOperationException(b); }")
    boundaries = boundaries.replace("internal static class TownServiceGrantSync { internal static bool CanUseImmersive = true; }",
        "internal static class TownServiceGrantSync { internal static bool CanUseImmersive=true; internal static int Owner; internal static int GrantedOwner(byte service)=>Owner; }")
    boundaries = boundaries.replace("panel.HostRect.sizeDelta=((RectTransform)target).rect.size;",
        "panel.HostRect.sizeDelta=((RectTransform)target).rect.size; panel.HostGo.AddComponent<Canvas>().renderMode=RenderMode.WorldSpace; panel.HostGo.AddComponent<GraphicRaycaster>();")
    boundaries += "\nnamespace GloomhavenVR.Core { internal static class VRLayers { internal static void Apply(GameObject root){} } }\nnamespace GloomhavenVR.Net { internal static class NetModule { internal static bool NameTagsWanted=false; } internal static class NetPlayerActors { internal static Sprite Avatar=null!; internal static Sprite AvatarFor(int id)=>Avatar; internal static string NameFor(int id)=>\"Guest \"+id; } }\nnamespace GloomhavenVR.WorldUI { internal static class MrBacking { internal static void Label(TMPro.TMP_Text text){} } }\n"
    hashes["fixture-boundaries"] = hashlib.sha256(boundaries.encode()).hexdigest()
    (run / "source-hashes.json").write_text(json.dumps({"source_root": str(args.source_root), "identity_root": str(identity), "sha256": hashes}, indent=2) + "\n")
    source = run / "production"
    source.mkdir()
    for name, text in production.items():
        (source / name).write_text(text)
    (source / "Boundaries.cs").write_text(boundaries)
    fixture = repo / "scripts/town-palm-render-runtime"
    project = run / "Render.csproj"
    project.write_text("""<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>netstandard2.1</TargetFramework><LangVersion>latest</LangVersion><Nullable>enable</Nullable><EnableDefaultCompileItems>false</EnableDefaultCompileItems><GenerateDocumentationFile>false</GenerateDocumentationFile><AssemblyName>PalmRender</AssemblyName><NoWarn>CS0649;CS0414;CS8602;CS8604;CS8625</NoWarn></PropertyGroup><ItemGroup><Compile Include="$(ProductionDir)/*.cs"/><Compile Include="$(FixtureDir)/Program.cs"/><Reference Include="$(UnityManaged)/UnityEngine/*.dll"/><Reference Include="UnityEngine.UI"><HintPath>$(UnityUi)</HintPath><Private>false</Private></Reference><Reference Include="Unity.TextMeshPro"><HintPath>$(UnityTmp)</HintPath><Private>false</Private></Reference></ItemGroup></Project>""")
    native = run / "native-book.obj"
    native_python = Path(os.environ.get("UNITYPY_PYTHON", str(Path.home() / "unitypy-venv/bin/python")))
    subprocess.run([str(native_python), str(repo / "scripts/town-ritual-layout-runtime/export-native-book.py"), str(args.source_root), str(native)], check=True)
    (run / "native-book-reference.json").write_text(json.dumps({"mesh": "CR_ST_Shelf_Book_07",
        "obj_sha256": hashlib.sha256(native.read_bytes()).hexdigest(), "fixture_font_sha256": hashlib.sha256(args.font.read_bytes()).hexdigest()}, indent=2) + "\n")
    dotnet = shutil.which("dotnet") or str(Path.home() / ".dotnet/dotnet")
    command = [dotnet, "build", str(project), "-c", "Release", "--nologo", "--verbosity", "quiet",
        f"-p:ProductionDir={source}", f"-p:FixtureDir={fixture}", f"-p:UnityManaged={args.unity.parent / 'Data/Managed'}",
        f"-p:UnityUi={args.source_root / 'ressources/GH_Data/Managed/UnityEngine.UI.dll'}",
        f"-p:UnityTmp={args.source_root / 'ressources/GH_Data/Managed/Unity.TextMeshPro.dll'}"]
    compiled = subprocess.run(command, text=True, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    (run / "build.log").write_text(compiled.stdout)
    if compiled.returncode:
        print(compiled.stdout)
        raise SystemExit("Render fixture failed compilation")
    unity = run / "unity"
    (unity / "Assets/Editor").mkdir(parents=True)
    (unity / "Assets/Resources").mkdir()
    shutil.copyfile(args.font, unity / "Assets/Resources/FixtureFont.ttf")
    (unity / "Packages").mkdir()
    (unity / "ProjectSettings").mkdir()
    shutil.copyfile(fixture / "Runner.cs", unity / "Assets/Editor/Runner.cs")
    (unity / "Packages/manifest.json").write_text('{"dependencies":{"com.unity.ugui":"1.0.0","com.unity.textmeshpro":"3.0.6"}}\n')
    (unity / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    command = ["xvfb-run", "-a", str(args.unity), "-batchmode", "-projectPath", str(unity), "-executeMethod", "PalmRenderRunner.Start",
        "-fixtureDll", str(run / "bin/Release/netstandard2.1/PalmRender.dll"), "-renderOutput", str(run), "-nativeBookObj", str(native), "-logFile", str(run / "unity.log")]
    done = subprocess.run(command, stdout=subprocess.DEVNULL, stderr=subprocess.STDOUT, timeout=240)
    print("Evidence: " + str(run))
    result = run / "result.json"
    if done.returncode or not result.exists():
        raise SystemExit("Render fixture failed; inspect " + str(run / "unity.log"))
    print(result.read_text())


if __name__ == "__main__":
    main()
