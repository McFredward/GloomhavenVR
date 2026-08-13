// GloomhavenVR — candle/lantern flame shader (shader-animated, script-free).
//
// Drawn on small static CROSS-QUAD meshes (two quads at 90°), NOT camera-facing
// billboards — the permanent VR rule forbids sprites that can visibly re-orient
// with the head (BuildEnvironments.cs header). A cross-quad flame is world-
// anchored geometry; _Time drives a gentle sway + UV wobble + brightness
// flicker matched to the EnvRoom light flicker (same sine family, so the flame
// and the light it casts breathe together). Additive, no depth write.
Shader "GloomhavenVR/EnvFlame"
{
    Properties
    {
        _MainTex ("Flame sprite", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)
        _Sway ("Sway amount", Range(0,0.2)) = 0.05
        _Flicker ("Brightness flicker", Range(0,1)) = 0.35
        _Phase ("Phase offset", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent+15" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex; float4 _MainTex_ST;
            fixed4 _Tint;
            float _Sway, _Flicker, _Phase;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed fl : TEXCOORD1; };

            v2f vert (appdata v)
            {
                v2f o;
                float t = _Time.y;
                // sway grows with height (uv.y=0 at flame base) — the tip dances
                float h = v.uv.y;
                float sway = (sin(t * 5.7 + _Phase) * 0.6 + sin(t * 9.3 + 1.3 + _Phase) * 0.4)
                             * _Sway * h * h;
                float4 p = v.vertex;
                p.x += sway;
                p.z += sway * 0.6;
                o.pos = UnityObjectToClipPos(p);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                // brightness flicker — same sine family as EnvRoom.Flicker
                float f = 0.42 * sin(t * 11.3 + _Phase)
                        + 0.33 * sin(t *  6.1 + 1.7 + _Phase * 1.3)
                        + 0.25 * sin(t * 19.7 + 4.2 + _Phase * 0.7);
                o.fl = 1.0 + _Flicker * 0.35 * f;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // wick-anchored UV wobble: zero at base, grows toward the tip
                float t = _Time.y;
                float wob = (sin(t * 13.1 + i.uv.y * 9.0 + _Phase)
                           + sin(t * 7.3 + 2.1 + _Phase)) * 0.012 * i.uv.y;
                fixed4 c = tex2D(_MainTex, i.uv + float2(wob, 0));
                c *= _Tint;
                c.rgb *= c.a * i.fl; // premodulate: alpha drives additive energy
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
