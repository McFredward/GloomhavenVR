// GloomhavenVR — ambient-environment solid-geometry shader.
//
// Same philosophy as Table/BoardLit.shader: lighting is BAKED into the material
// (configurable key + fill directions/colors + an ambient floor), so the environment
// looks identical inside every game scene regardless of the scenario's own lights,
// and never goes black. Unlike BoardLit it needs no normal map / tangents (the
// Quaternius sets are faceted low-poly — per-face normals carry all the shape) and
// adds an emission term for glowing props (torch heads, candle wax, mushrooms).
// One variant, opaque, zero external dependencies -> bundles safely.
Shader "GloomhavenVR/EnvLit"
{
    Properties
    {
        _Color ("Albedo tint", Color) = (1,1,1,1)
        _MainTex ("Albedo (optional)", 2D) = "white" {}
        _Emission ("Emission", Color) = (0,0,0,1)
        _AmbientCol ("Ambient color", Color) = (0.2,0.2,0.22,1)
        _KeyDir ("Key light direction (world)", Vector) = (0.3,1,0.2,0)
        _KeyCol ("Key light color", Color) = (1,0.9,0.75,1)
        _FillDir ("Fill light direction (world)", Vector) = (-0.4,0.3,-0.3,0)
        _FillCol ("Fill light color", Color) = (0.12,0.15,0.22,1)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull [_Cull]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex; float4 _MainTex_ST;
            fixed4 _Color, _Emission, _AmbientCol, _KeyCol, _FillCol;
            float4 _KeyDir, _FillDir;

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wn : TEXCOORD1; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.wn = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag (v2f i, fixed facing : VFACE) : SV_Target
            {
                float3 N = normalize(i.wn) * sign(facing);
                fixed4 alb = tex2D(_MainTex, i.uv) * _Color;
                float3 lit = _AmbientCol.rgb
                           + _KeyCol.rgb  * saturate(dot(N, normalize(_KeyDir.xyz)))
                           + _FillCol.rgb * saturate(dot(N, normalize(_FillDir.xyz)));
                return fixed4(alb.rgb * lit + _Emission.rgb, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
