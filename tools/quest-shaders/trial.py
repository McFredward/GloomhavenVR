"""Build an isolated wrapper for the first bound original forward-pass trial.

This is a validation fixture, not a shipping shader or a fallback.  The instruction
stream and uniforms come from the asset recovery lane.  Unity's own include files
already declare its native probe resources; duplicate declarations are removed
and the original sampler use is bound to those exact builtin declarations.
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import shutil
import subprocess


def _properties(parsed):
    result = []
    for row in parsed["m_PropInfo"]["m_Props"]:
        name = row["m_Name"]
        if not re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", name):
            raise ValueError("Original property name cannot be represented in ShaderLab.")
        description = json.dumps(row["m_Description"])
        value = [str(row[f"m_DefValue_{index}_"]) for index in range(4)]
        kind = row["m_Type"]
        attributes = "".join("[" + text + "] " for text in row["m_Attributes"])
        if kind in (0, 1):
            shape = "Color" if kind == 0 else "Vector"
            value = "(" + ",".join(value) + ")"
        elif kind == 2:
            shape, value = "Float", value[0]
        elif kind == 3:
            shape, value = "Range(" + value[1] + "," + value[2] + ")", value[0]
        elif kind == 4:
            shape = {2: "2D", 3: "3D", 4: "Cube", 5: "2DArray", 6: "CubeArray"}[row["m_DefTexture"]["m_TexDim"]]
            value = json.dumps(row["m_DefTexture"]["m_DefaultName"]) + " {}"
        else:
            raise ValueError("Original property type is not represented in this trial.")
        result.append(f"{attributes}{name} ({description}, {shape}) = {value}")
    return "\n".join(result)


def original_forward(parsed, vertex, fragment):
    pass_state = parsed["m_SubShaders"][0]["m_Passes"][0]["m_State"]
    for key, value in (("m_Culling", 2), ("m_ZTest", 4), ("m_ZWrite", 1)):
        if pass_state[key]["m_Name"] != "<noninit>" or pass_state[key]["m_Value"] != value:
            raise ValueError("First forward-pass fixture render state differs from its original.")
    # UnityShaderVariables declares exactly these resource/sampler pairs.  No
    # texture value, equation or lookup is substituted here.
    for name in ("unity_SpecCube0", "unity_SpecCube1", "unity_ProbeVolumeSH"):
        fragment = re.sub(r"(?m)^Texture(?:Cube|3D)<float4> " + name + r";\n", "", fragment)
        fragment = re.sub(r"(?m)^SamplerState sampler_" + name + r";\n", "", fragment)
        fragment = re.sub(r"\bsampler_" + name + r"\b", "sampler" + name, fragment)
    vertex = re.sub(r"\bmain\b", "QuestOriginalVertex", vertex)
    fragment = re.sub(r"\bmain\b", "QuestOriginalFragment", fragment)
    return 'Shader "Amp_Char_Shader" {\nProperties {\n' + _properties(parsed) + '\n}\nSubShader {\nTags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" }\nPass {\nName "FORWARD"\nTags { "LightMode"="ForwardBase" }\nCull Back\nZTest LEqual\nZWrite On\nHLSLPROGRAM\n#pragma target 3.5\n#pragma vertex QuestOriginalVertex\n#pragma fragment QuestOriginalFragment\n#define UNITY_LIGHT_PROBE_PROXY_VOLUME 1\n#include "UnityCG.cginc"\n#include "Lighting.cginc"\n#if defined(SHADER_STAGE_VERTEX)\n' + vertex + '\n#endif\n#if defined(SHADER_STAGE_FRAGMENT)\n' + fragment + '\n#endif\nENDHLSL\n}\n}\nFallback Off\n}\n'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--vertex", type=Path, required=True)
    parser.add_argument("--fragment", type=Path, required=True)
    parser.add_argument("--recipe", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--unity", type=Path, default=Path("/home/claw/unity-2021.3.5/Editor/Unity"))
    args = parser.parse_args()
    project = args.output / "unity"
    (project / "Assets/Editor").mkdir(parents=True, exist_ok=True)
    (project / "ProjectSettings").mkdir(exist_ok=True)
    (project / "Packages").mkdir(exist_ok=True)
    (project / "Packages/manifest.json").write_text('{"dependencies":{"com.unity.modules.assetbundle":"1.0.0"}}\n')
    (project / "ProjectSettings/ProjectVersion.txt").write_text("m_EditorVersion: 2021.3.5f1\n")
    parsed = json.loads(args.recipe.read_text())["parsedForm"]
    shader = original_forward(parsed, args.vertex.read_text(), args.fragment.read_text())
    (project / "Assets/OriginalAmpForward.shader").write_text(shader)
    shutil.copy2(Path(__file__).parent / "UnityHost/Editor/QuestShaderTrial.cs", project / "Assets/Editor/QuestShaderTrial.cs")
    process = subprocess.run([str(args.unity), "-batchmode", "-nographics", "-quit", "-buildTarget", "Android", "-projectPath", str(project),
                              "-executeMethod", "QuestShaderTrial.Compile", "-logFile", str(args.output / "unity.log")], capture_output=True, text=True, timeout=240)
    (args.output / "console.log").write_text(process.stdout + process.stderr)
    if process.returncode:
        raise SystemExit("Original bound forward-pass trial failed: " + str(args.output / "unity.log"))
    print("Actual Android shader trial: " + str(args.output / "unity/TrialOutput/results.json"))


if __name__ == "__main__":
    main()
