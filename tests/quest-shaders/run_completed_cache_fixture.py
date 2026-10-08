"""Prepare/run a tiny authored Unity fixture, without loading original game content."""
from pathlib import Path
import argparse
import json
import os
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[2]
EDITOR_SOURCES = ROOT / "unity/GloomhavenVR.Quest/Assets/Quest/Editor"
SHADER = '''Shader "QuestPrivate/Cache" {
 Properties { _Color("Color", Color)=(1,1,1,1) }
 SubShader { Pass { Lighting Off
 HLSLPROGRAM
 #pragma target 4.5
 #pragma vertex vert
 #pragma fragment frag
 #pragma multi_compile __ FIXTURE_VARIANT
 #include "UnityCG.cginc"
 #include "Assets/QuestOriginalCampaign/ShaderPrograms/CacheFixture.hlsl"
 float4 vert(float4 p : POSITION) : SV_POSITION {return UnityObjectToClipPos(p);}
 float4 frag() : SV_Target { return FixtureColor(); }
 ENDHLSL
 } }
}
'''
INCLUDE = '''float4 FixtureColor() {
#ifdef FIXTURE_VARIANT
 return float4(0.7,0.1,0.4,1.0);
#else
 return float4(0.2,0.6,0.3,1.0);
#endif
}
'''


def prepare(project: Path) -> None:
    if project.exists() and any(project.iterdir()):
        raise ValueError("Use a new empty private fixture directory; existing projects are never overwritten.")
    editor = project / "Assets/Quest/Editor"
    editor.mkdir(parents=True)
    for name in ("QuestCampaignShaderValidation.cs", "QuestCampaignShaderCache.cs", "QuestVulkanShaderValidation.cs", "QuestSmolvDecoder.cs"):
        shutil.copyfile(EDITOR_SOURCES / name, editor / name)
    shutil.copyfile(Path(__file__).parent / "UnityHost/QuestShaderCacheWitness.cs", editor / "QuestShaderCacheWitness.cs")
    assets = project / "Assets/QuestOriginalCampaign"
    assets.mkdir()
    (assets / "CacheFixture.shader").write_text(SHADER)
    (assets / "CacheFixture.shader.meta").write_text("fileFormatVersion: 2\nguid: a31ca39f74df430897c1a6b36d7a2d5e\nShaderImporter:\n  externalObjects: {}\n  defaultTextures: []\n  nonModifiableTextures: []\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n")
    programs = assets / "ShaderPrograms"
    programs.mkdir()
    (programs / "CacheFixture.hlsl").write_text(INCLUDE)
    settings = project / "ProjectSettings"
    settings.mkdir()
    (settings / "ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\nm_EditorVersionWithRevision: 2021.3.5f1 (40eb3a945986)\n")
    packages = project / "Packages"
    packages.mkdir()
    (packages / "manifest.json").write_text(json.dumps({"dependencies": {"com.unity.modules.jsonserialize": "1.0.0", "com.unity.modules.imgui": "1.0.0", "com.unity.modules.vr": "1.0.0"}}, indent=2) + "\n")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--unity", type=Path, required=True)
    parser.add_argument("--project", type=Path, required=True)
    args = parser.parse_args()
    project = args.project.resolve()
    prepare(project)
    for method, log in (("Run", "cache-first.log"), ("RepairOwnedOutputs", "cache-repair.log"), ("AddressablesAndXrClosure", "cache-addressables-xr.log")):
        command = [str(args.unity.resolve()), "-batchmode", "-force-glcore", "-quit", "-projectPath", str(project), "-buildTarget", "Android", "-executeMethod", "QuestShaderCacheWitness." + method, "-logFile", str(project.parent / log)]
        if os.name != "nt":
            command[:0] = ["xvfb-run", "-a"]
        subprocess.run(command, check=True)
    for filename in ("result.json", "repair.json", "addressables-xr.json"):
        result = json.loads((project / "CacheWitnessProof" / filename).read_text())
        if result.get("passed") is not True or result.get("headsetVerified") is not False:
            raise ValueError("Tiny authored fixture failed or falsely claims headset verification.")
    print("PASS actual tiny completed-cache and owned-output repair fixtures; original-game/headset pixels remain separate.")


if __name__ == "__main__":
    main()
