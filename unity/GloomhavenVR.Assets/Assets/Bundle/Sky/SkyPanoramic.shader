// GloomhavenVR — equirectangular panorama backdrop shader (skybox alternatives feature).
//
// Renders an equirect panorama purely from the VIEW DIRECTION (fragment world position
// minus camera position), so ANY enclosing mesh works — the mod uses an inverted sphere,
// but the mapping never depends on the mesh's UVs or scale. Background queue, no depth
// write, front-face culling (viewer sits inside), fog off: a pure non-occluding backdrop,
// the same contract SkyBackdrop enforces on the game's own sky sphere.
Shader "GloomhavenVR/SkyPanoramic"
{
    Properties
    {
        _MainTex ("Panorama (equirect)", 2D) = "grey" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _RotationDeg ("Yaw rotation (deg)", Range(0,360)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Front
        ZWrite Off
        ZTest LEqual
        Fog { Mode Off }

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Tint;
            float _RotationDeg;

            struct appdata { float4 vertex : POSITION; };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                float3 world = mul(unity_ObjectToWorld, v.vertex).xyz;
                o.dir = world - _WorldSpaceCameraPos;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float yaw = _RotationDeg * UNITY_PI / 180.0;
                float cs = cos(yaw), sn = sin(yaw);
                d = float3(d.x * cs - d.z * sn, d.y, d.x * sn + d.z * cs);
                // equirect: u from atan2 around Y, v from asin of height
                float u = atan2(d.x, -d.z) / (2.0 * UNITY_PI) + 0.5;
                float v = asin(clamp(d.y, -1.0, 1.0)) / UNITY_PI + 0.5;
                return tex2D(_MainTex, float2(u, v)) * _Tint;
            }
            ENDCG
        }
    }
    Fallback Off
}
