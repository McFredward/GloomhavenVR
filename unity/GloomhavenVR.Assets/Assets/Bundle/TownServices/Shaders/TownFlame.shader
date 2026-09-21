// The game's original candle flame/glow meshes and textures use additive alpha.
// This material supplies stereo routing and station visibility without native scripts.
Shader "GloomhavenVR/TownFlame"
{
    Properties
    {
        _MainTex ("Original flame", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _TownVisibility ("Town visibility", Range(0,1)) = 1
        _Billboard ("Verified native XY quad", Float) = 0
        _Toggle_Flipbook ("Original flipbook", Float) = 0
        _FlipbookTileX ("Columns", Float) = 1
        _FlipbookTileY ("Rows", Float) = 1
        _FlipbookSpeed ("Cycles per second", Float) = 1
        _FlipbookStart ("Starting frame", Float) = 0
        _TownAnimationTime ("Shared station clock", Float) = 0
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
            half _TownVisibility, _Billboard;
            float _Toggle_Flipbook, _FlipbookTileX, _FlipbookTileY, _FlipbookSpeed, _FlipbookStart, _TownAnimationTime;
            struct AppData { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Interpolated { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Interpolated vert(AppData v)
            {
                Interpolated o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(Interpolated, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                if (_Billboard > .5h)
                {
                    // The original CandleFlame and Glow use Unity's XY Quad. One central
                    // facing basis keeps both eyes on the same physical surface.
                    float3 centre = mul(unity_ObjectToWorld, float4(0, 0, 0, 1)).xyz;
                    float3 eye = _WorldSpaceCameraPos.xyz;
                    #if defined(USING_STEREO_MATRICES)
                        eye = (unity_StereoWorldSpaceCameraPos[0] + unity_StereoWorldSpaceCameraPos[1]) * .5;
                    #endif
                    float3 forward = normalize(eye - centre);
                    float3 right = normalize(cross(float3(0, 1, 0), forward));
                    float3 up = cross(forward, right);
                    float sx = length(mul((float3x3)unity_ObjectToWorld, float3(1, 0, 0)));
                    float sy = length(mul((float3x3)unity_ObjectToWorld, float3(0, 1, 0)));
                    float3 world = centre + right * v.vertex.x * sx + up * v.vertex.y * sy;
                    o.vertex = UnityWorldToClipPos(world);
                }
                else o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                if (_Toggle_Flipbook > .5)
                {
                    // Original candle frames; one loop/second is authored station decor
                    // timing. The source shader's compiled speed units are unavailable.
                    float2 tiles = max(float2(1, 1), float2(_FlipbookTileX, _FlipbookTileY));
                    float frame = fmod(floor(_TownAnimationTime * _FlipbookSpeed * tiles.x * tiles.y + _FlipbookStart), tiles.x * tiles.y);
                    o.uv = (v.uv + float2(fmod(frame, tiles.x), tiles.y - 1 - floor(frame / tiles.x))) / tiles;
                }
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
