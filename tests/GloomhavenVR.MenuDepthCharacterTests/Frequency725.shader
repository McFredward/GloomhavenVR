Shader "Hidden/GloomhavenVR/MenuDepthCharacterFrequency"
{
    Properties { _Frequency ("Authored cycles", Float) = 140 }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float _Frequency;
            struct input { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct output { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
            output vert(input v) { output o; o.position = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
            fixed4 frag(output i) : SV_Target
            {
                float pattern = .5 + .5 * sin(6.28318530718 * _Frequency * i.uv.y);
                return fixed4(pattern, pattern, pattern, 1);
            }
            ENDCG
        }
    }
}
