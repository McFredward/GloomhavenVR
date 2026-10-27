Shader "Hidden/GloomhavenVR/MenuDepthCharacterEyeProbe"
{
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 vertex : SV_POSITION; };
            v2f vert(appdata input) { v2f output; output.vertex = UnityObjectToClipPos(input.vertex); return output; }
            float4 frag(v2f input) : SV_Target { return float4(_WorldSpaceCameraPos * .02 + .5, 1); }
            ENDCG
        }
    }
}
