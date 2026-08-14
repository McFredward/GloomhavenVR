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

        // ---- SHELF RIDERS (EnvShelfTip.cginc) -------------------------------
        // USER, ModBuild 144: "Die Kerzen und das Feuer, die auf dem Bücherregal
        // stehen, kippen nicht mit". Two of this shader's spheres stand on that
        // shelf — the shelf candle's halo and the near halo of the fire seated on
        // its top boards — and a halo that stays where the candle WAS is worse
        // than no halo at all, because it is a light with nothing in it.
        //
        // A halo is a real world-space sphere and the pose is a rigid transform,
        // so the whole sphere simply goes with the flame: the vertex path is one
        // GhvrTipRot behind one uniform compare. The candle's halo also DIES with
        // the candle, on the same GhvrTipFlameLife the flame and the baked light
        // slot take, which is the only way the three can agree about a candle
        // being out. Zero on every other glow in both rooms.
        _TipPivot ("Shelf hinge (OBJECT space, w = pose valid)", Vector) = (0,0,0,0)
        _TipAxis ("Shelf hinge axis (OBJECT space, w = max angle rad)", Vector) = (0,0,0,0)
        _TipSched ("Shelf schedule (period, cards, card)", Vector) = (0,0,0,0)
        _TipEnv ("Shelf event envelope (reveal, hold, fade)", Vector) = (0,0,0,0)
        _TipUse ("Ride self, lit slot, gutters, flame stiffness", Vector) = (0,-1,0,0)
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
            #include "EnvShelfTip.cginc"
            // ...and the five Fire pairings, for the GATED halos only: a halo on
            // a burning object is part of that fire and has to answer the same
            // combinations it does. See the PAIRINGS block in EnvFire.cginc for
            // the composition rule.
            #include "EnvFire.cginc"

            fixed4 _Tint;
            float _Falloff, _Flicker, _Rate, _Phase, _Blink, _BlinkPeriod, _Away, _AwayPeriod, _ElemWarm;
            float _ElemGate;
            float _GhvrTimeOfs;   // preview-only clock offset (see EnvRoom.shader)

            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; };
            struct v2f { float4 pos : SV_POSITION; float3 wn : TEXCOORD0; float3 wp : TEXCOORD1;
                         // SHELF RIDERS: the flame's life, so a halo goes out with
                         // the candle it belongs to. NEGATIVE means "not riding",
                         // and it is a sentinel rather than a neutral 1 for the
                         // reason spelled out at GhvrTipLight: an interpolated
                         // constant is not the constant, and every other halo in
                         // both rooms has to stay bit-identical.
                         float life : TEXCOORD2; };

            v2f vert (appdata v)
            {
                v2f o;
                // REAL FIRE: collapse a gated halo whose element is down. Not an
                // alpha of zero — that would still cost the fill of a sphere that
                // covers a good part of the frame from close to.
                if (_ElemGate > 0.5 && GhvrElems().fire <= 0.0)
                {
                    o.pos = float4(0, 0, 0, 1);
                    o.wn = float3(0, 1, 0); o.wp = float3(0, 0, 0); o.life = -1;
                    return o;
                }
                // SHELF RIDERS. A halo is a sphere of light around a flame, so
                // it takes the flame's rigid transform whole — there is nothing
                // about a sphere to bend. One uniform compare when the shelf is
                // standing, which is always outside the event.
                float3 p = v.vertex.xyz;
                float3 n = v.normal;
                o.life = -1;
                GhvrTip tip = GhvrTipNow(_Time.y + _GhvrTimeOfs);
                if (tip.live > 0.5 && _TipUse.x > 0.5)
                {
                    p = GhvrTipRot(p, tip.pivot, tip.axis, tip.ang);
                    // the sphere's normals turn with it: the falloff is measured
                    // against them, and a halo whose normals stayed put would
                    // brighten on the wrong side as it travelled
                    n = GhvrTipRot(n, float3(0, 0, 0), tip.axis, tip.ang);
                    if (_TipUse.z > 0.5)
                    {
                        float life = GhvrTipFlameLife(tip);
                        o.life = life * GhvrTipRelightFlare(tip, life);
                    }
                }
                o.pos = UnityObjectToClipPos(float4(p, 1.0));
                o.wn = UnityObjectToWorldNormal(n);
                o.wp = mul(unity_ObjectToWorld, float4(p, 1.0)).xyz;
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
                if (_ElemGate > 0.5)
                {
                    amp *= sqrt(saturate(e.fire));
                    // ---- THE FIVE FIRE PAIRINGS, on the halo. Every one of them
                    // modulates a number this shader already owns — the
                    // amplitude, the flicker rate, the edge and the colour — so
                    // a halo under Fire+Air+Dark is ONE halo that is windblown
                    // and alone. All exactly zero unless both elements are up.
                    GhvrFirePair p = GhvrFirePairs(e);
                    // FIRE+DARK: it is the only light there is, so the air around
                    // it glows harder and the edge opens out (a bright source in
                    // a black room has a bigger visible halo — that is what a
                    // halo IS). This is the counterweight to _Falloff's own Dark
                    // term above, which tightens every OTHER glow in the room:
                    // the split is "hard little points everywhere, except where
                    // something is actually burning".
                    fall = max(fall * (1.0 - 0.45 * p.dark), 0.30);
                    amp *= 1.0 + 1.35 * p.dark;
                    // FIRE+AIR: the halo breathes at the flame's rate, and the
                    // flame's rate has gone up — so this has to as well, or the
                    // one thing the whole feature is built around (a flame and
                    // the light it casts share a rate) breaks under wind. The
                    // rate itself is applied above, so what is left here is the
                    // DEPTH, which is the same +70% GhvrFireDepth gives the wash.
                    amp = 1.0 + (amp - 1.0) * (1.0 + 0.70 * p.air);
                    // FIRE+EARTH: a smoulder glows more than it flames — MORE
                    // halo, redder, and tighter to the seat.
                    elemCol = lerp(elemCol, float3(0.86, 0.20, 0.05), saturate(p.earth * 0.9));
                    amp *= 1.0 + 0.55 * p.earth;
                    fall = fall * (1.0 + 0.60 * p.earth);
                    // FIRE+ICE: steam. The air around the fire is full of it, so
                    // the halo is BIGGER and much less warm — a fire seen through
                    // its own steam has a pale corona, not an amber one.
                    elemCol = lerp(elemCol, float3(0.72, 0.80, 0.92), saturate(p.ice * 0.75));
                    fall = max(fall * (1.0 - 0.35 * p.ice), 0.30);
                    amp *= 1.0 + 0.40 * p.ice;
                    // FIRE+LIGHT: nothing. The plume is the pairing (EnvFlame),
                    // and a halo added on top of a moonlit smoke column would be
                    // the second visual layer the composition rule forbids.
                }

                // SHELF RIDERS: untouched unless this halo belongs to a candle
                // that has just been tipped over — see the vertex shader.
                if (i.life >= 0.0) amp *= i.life;

                return fixed4(elemCol, core * _Tint.a * max(amp, 0.0) * elemAmp);
            }
            ENDCG
        }
    }
    Fallback Off
}
