// The game's original candle flame/glow meshes and textures use additive alpha.
// This material supplies stereo routing and station visibility without native scripts.
Shader "GloomhavenVR/TownFlame"
{
    Properties
    {
        _MainTex ("Original flame", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _TownVisibility ("Town visibility", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _Color;
            half _TownVisibility;
            struct AppData { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Interpolated { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Interpolated vert(AppData v)
            {
                Interpolated o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(Interpolated, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }
            fixed4 frag(Interpolated i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                fixed4 c = tex2D(_MainTex, i.uv) * _Color;
                c.a *= _TownVisibility;
                return c;
            }
            ENDCG
        }
    }
    FallBack Off
}
