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
        // ELEMENT ART (EnvElement.cginc). A halo IS a source, so this is where
        // the Light/Dark split is most visible: Light makes every halo bigger and
        // brighter, Dark does not extinguish them but pulls them in — the falloff
        // exponent rises, so the glow stops being a soft veil and becomes a hard
        // little core. A black room with hard bright points in it is the split.
        // _ElemWarm (default 1) lets Fire swell the warm halos; the forest's cold
        // wisps and the rat's eyes are built with less of it, because a wisp that
        // turns orange is not a wisp any more.
        _ElemWarm ("Element: fire susceptibility", Range(0,2)) = 1

        // REAL FIRE — the gate. The cellar's burning crates, barrels and shelf
        // (EnvFlame's _FireGate) each carry a halo, and a halo for a fire that
        // is not burning must not exist at all: with the gate on and Fire down
        // the sphere is COLLAPSED to a point in the vertex shader, exactly as
        // the gated flame cards and the gated particle emitters are. The default
        // 0 is every halo that shipped before — the candles, the window, the
        // wisps and the rat's eyes — and they are untouched.
        //
        // WHY A HALO AT ALL, and what it is standing in for: EnvRoom's baked
        // light rig has exactly THREE point slots and all three are candles, so
        // a fourth source cannot be added without that shader (a different lane
        // this round). The halo is therefore the fire's light: a real world-space
        // volume that brightens the air around the fire and washes the wall it
        // stands against. It is not a substitute for a surface light and it is
        // not pretending to be one — see BuildEnvironmentRooms' FIRE LIGHT note.
        _ElemGate ("Element gate: 1 = exists only under Fire", Range(0,1)) = 0
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
            #include "EnvElement.cginc"

            fixed4 _Tint;
            float _Falloff, _Flicker, _Rate, _Phase, _Blink, _BlinkPeriod, _Away, _AwayPeriod, _ElemWarm;
            float _ElemGate;
            float _GhvrTimeOfs;   // preview-only clock offset (see EnvRoom.shader)

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 wn : TEXCOORD0; float3 wp : TEXCOORD1; };

            v2f vert (appdata v)
            {
                v2f o;
                // REAL FIRE: collapse a gated halo whose element is down. Not an
                // alpha of zero — that would still cost the fill of a sphere that
                // covers a good part of the frame from close to.
                if (_ElemGate > 0.5 && GhvrElems().fire <= 0.0)
                {
                    o.pos = float4(0, 0, 0, 1);
                    o.wn = float3(0, 1, 0); o.wp = float3(0, 0, 0);
                    return o;
                }
                o.pos = UnityObjectToClipPos(v.vertex);
                o.wn = UnityObjectToWorldNormal(v.normal);
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float3 V = normalize(_WorldSpaceCameraPos - i.wp);

                // ---- ELEMENT ART: the halo's size, colour and edge -----------
                GhvrElem e = GhvrElems();
                float fall = _Falloff, elemAmp = 1.0;
                float3 elemCol = _Tint.rgb;
                if (e.live > 0.0)
                {
                    float warm = e.fire * _ElemWarm;
                    // Dark tightens the edge (the corners swallow light), Light
                    // opens it out; Fire swells the warm halos a little.
                    fall = max(_Falloff * (1.0 + 1.15 * e.dark - 0.30 * e.light - 0.25 * warm), 0.30);
                    elemAmp = max(GhvrSrcGain(e) + 0.85 * warm - 0.35 * e.dark * (1.0 - e.light), 0.0);
                    // a fire-fed halo goes ember; nothing else recolours it
                    elemCol = lerp(_Tint.rgb, float3(1.00, 0.46, 0.14), saturate(warm * 0.85));
                }
                float core = pow(saturate(dot(normalize(i.wn), V)), fall);

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

                // REAL FIRE: a gated halo RAMPS with its element rather than
                // snapping on. ElementMood smooths over about a second and its
                // waning plateau breathes 0.28..0.52, so a fire whose light
                // arrived at full strength would throw away the one cue the
                // smoothing exists to give — that the infusion is going out.
                // sqrt for the same reason EnvFlame's gate takes it: the waning
                // plateau is 0.40 and a fire that is dying back still lights the
                // wall it stands against. Zero at zero, so nothing pops in.
                if (_ElemGate > 0.5) amp *= sqrt(saturate(e.fire));

                return fixed4(elemCol, core * _Tint.a * max(amp, 0.0) * elemAmp);
            }
            ENDCG
        }
    }
    Fallback Off
}
