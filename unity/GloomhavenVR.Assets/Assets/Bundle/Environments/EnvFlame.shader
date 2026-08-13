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
        // MUST equal the rate of the EnvRoom light slot this flame belongs to
        // (EnvRoom: slot0 1.00, slot1 0.83, slot2 1.19) — otherwise the flame
        // you see and the light it casts drift apart.
        _Rate ("Flicker rate (match the light slot)", Float) = 1
        // The DRAFT. Deliberately UNPHASED and slow, so every flame in the room
        // leans the same way at the same moment: that is what reads as one
        // draught through the cellar rather than three independent candles.
        _Gust ("Draft amount", Range(0,0.3)) = 0
        _GustDir ("Draft direction (OBJECT space XZ)", Vector) = (1,0,0,0)
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
            float _Sway, _Flicker, _Phase, _Rate, _Gust;
            float4 _GustDir;
            float _GhvrTimeOfs;   // preview-only clock offset (see EnvRoom.shader)

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed fl : TEXCOORD1; };

            v2f vert (appdata v)
            {
                v2f o;
                float t = _Time.y + _GhvrTimeOfs;
                float ft = t * _Rate;
                // sway grows with height (uv.y=0 at flame base) — the tip dances
                float h = v.uv.y;
                float sway = (sin(ft * 5.7 + _Phase) * 0.6 + sin(ft * 9.3 + 1.3 + _Phase) * 0.4)
                             * _Sway * h * h;
                // the draft: slow, shared, unphased (see _Gust)
                float g = sin(t * 0.37) * 0.62 + sin(t * 0.83 + 1.1) * 0.38;
                float4 p = v.vertex;
                p.x += sway + _GustDir.x * _Gust * g * h * h;
                p.z += sway * 0.6 + _GustDir.z * _Gust * g * h * h;
                o.pos = UnityObjectToClipPos(p);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                // brightness flicker — same sine family AND same rate as the
                // EnvRoom light slot this candle drives
                float f = 0.42 * sin(ft * 11.3 + _Phase)
                        + 0.33 * sin(ft *  6.1 + 1.7 + _Phase * 1.3)
                        + 0.25 * sin(ft * 19.7 + 4.2 + _Phase * 0.7);
                f = f * 0.70 + 0.30 * sin(ft * 1.9 + _Phase * 0.5);
                // a flame that is bent by a draught also burns brighter
                o.fl = (1.0 + _Flicker * 0.35 * f) * (1.0 + 0.55 * _Gust * abs(g));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // wick-anchored UV wobble: zero at base, grows toward the tip
                float t = (_Time.y + _GhvrTimeOfs) * _Rate;
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
