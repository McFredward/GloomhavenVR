#if UNITY_EDITOR
using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>Recover inline-sampler packed IDs from the exact Unity compiler.</summary>
public static class QuestShaderSamplerCalibration
{
    public static void Build()
    {
        const string asset = "Assets/QuestSamplerCalibration.shader";
        var text = new StringBuilder("Shader \"QuestValidation/NativeSamplerCalibration\" { Properties { _MainTex(\"Texture\",2D)=\"white\"{} } SubShader {\n");
        foreach (string filter in new[] { "point", "linear", "trilinear" })
        foreach (string address in new[] { "clamp", "repeat", "mirror", "mirroronce" })
        {
            string id = filter + "_" + address;
            text.Append("Pass { Name \"" + id + "\" HLSLPROGRAM\n#pragma target 4.5\n#pragma vertex vert\n#pragma fragment frag\n#include \"UnityCG.cginc\"\n");
            text.Append("Texture2D<float4> _MainTex; SamplerState sampler_" + id + ";\n");
            text.Append("struct V { float4 pos:SV_POSITION; float2 uv:TEXCOORD0; };\nV vert(appdata_base input) { V o; o.pos=UnityObjectToClipPos(input.vertex); o.uv=input.texcoord.xy; return o; }\n");
            text.Append("float4 frag(V input):SV_Target { return _MainTex.Sample(sampler_" + id + ", input.uv); }\nENDHLSL\n}\n");
        }
        text.Append("} Fallback Off }");
        File.WriteAllText(asset, text.ToString());
        AssetDatabase.ImportAsset(asset, ImportAssetOptions.ForceSynchronousImport);
        Directory.CreateDirectory("SamplerCalibration");
        var result = BuildPipeline.BuildAssetBundles("SamplerCalibration", new[] { new AssetBundleBuild { assetBundleName="native-samplers", assetNames=new[] { asset } } },
            BuildAssetBundleOptions.StrictMode | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows64);
        if (result == null) throw new InvalidOperationException("Native inline-sampler calibration bundle failed.");
    }
}
#endif
