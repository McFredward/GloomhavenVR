// GloomhavenVR — soft light-glow shader for small emissive spheres around flames and
// window light. A real world-space sphere whose brightness falls off toward its
// silhouette (dot(N,V) falloff): reads as a volumetric halo from EVERY direction and
// in stereo, with no billboarding and nothing attached to the camera. Additive,
// no depth write.
Shader "GloomhavenVR/EnvGlow"
{
    Properties
    {
        _Tint ("Glow color (alpha = strength)", Color) = (1,0.6,0.2,0.5)
        _Falloff ("Edge falloff", Range(0.5,8)) = 2.5
        // A candle's HALO must breathe with the candle. All of the following
        // default to 0/1 => every existing glow (the forest's wisps, lantern and
        // eyes) is bit-identical without them.
        _Flicker ("Brightness flicker", Range(0,1)) = 0
        _Rate ("Flicker rate (match the light slot)", Float) = 1
        _Phase ("Phase offset", Float) = 0
        // Blink + absence, for eyes in the dark. _Blink closes them briefly every
        // _BlinkPeriod; _Away removes them for most of _AwayPeriod, so the pair
        // in the corner is not a permanent fixture you stop noticing.
        _Blink ("Blink depth", Range(0,1)) = 0
        _BlinkPeriod ("Blink period (s)", Float) = 4.7
        _Away ("Absence depth", Range(0,1)) = 0
        _AwayPeriod ("Absence period (s)", Float) = 26
    }
    SubShader
    {
        Tags { "Queue"="Transparent+5" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Back
        Fog { Mode Off }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Tint;
            float _Falloff, _Flicker, _Rate, _Phase, _Blink, _BlinkPeriod, _Away, _AwayPeriod;
            float _GhvrTimeOfs;   // preview-only clock offset (see EnvRoom.shader)

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 wn : TEXCOORD0; float3 wp : TEXCOORD1; };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 V = normalize(_WorldSpaceCameraPos - i.wp);
                float core = pow(saturate(dot(normalize(i.wn), V)), _Falloff);

                float t = _Time.y + _GhvrTimeOfs;
                float ft = t * _Rate;
                float f = 0.42 * sin(ft * 11.3 + _Phase)
                        + 0.33 * sin(ft *  6.1 + 1.7 + _Phase * 1.3)
                        + 0.25 * sin(ft * 19.7 + 4.2 + _Phase * 0.7);
                f = f * 0.70 + 0.30 * sin(ft * 1.9 + _Phase * 0.5);
                float amp = 1.0 + _Flicker * 0.35 * f;

                // lid: shut for ~2.4% of the period, centred on its half
                float bp = frac(t / max(_BlinkPeriod, 0.01) + _Phase * 0.11);
                amp *= 1.0 - _Blink * (1.0 - smoothstep(0.0, 0.012, abs(bp - 0.5)));
                // absence: present for the first ~45% of the long cycle only
                float ap = frac(t / max(_AwayPeriod, 0.01) + _Phase * 0.037);
                float present = smoothstep(0.02, 0.10, ap) * smoothstep(0.47, 0.38, ap);
                amp *= lerp(1.0, present, _Away);

                return fixed4(_Tint.rgb, core * _Tint.a * max(amp, 0.0));
            }
            ENDCG
        }
    }
    Fallback Off
}
