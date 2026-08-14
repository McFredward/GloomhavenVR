// GloomhavenVR — the cellar's puddle: a wet patch of flagstone that a drip from
// the ceiling keeps ringing, and that catches the candle and the moon.
//
// USER RULING, ModBuild 134: "zB tropft Wasser von irgendwo runter in eine
// pütze". So the puddle has to be a LIGHT event, not a texture: the reflections
// are what makes a dark room read as wet, and the expanding rings are what makes
// the drip land instead of merely falling.
//
// Two tiny passes over ~600 triangles, both script-free (_Time only):
//   1. MULTIPLY (Blend DstColor Zero) — darkens the flagstones underneath by
//      _Wet, feathered out by the mesh's own vertex alpha, so the puddle is a
//      wet patch OF the floor rather than a plate lying on it.
//   2. ADD (Blend One One) — the mirror images. For a flat mirror the image of
//      a light sits exactly where the REFLECTED view ray points at it, so
//      pow(saturate(dot(reflect(-V,N), toLight)), p) IS the reflection, blurred
//      by p. Done for the moon (a direction) and for one candle (a position,
//      flickering on the same sine family and rate as its EnvRoom light slot).
//      N is perturbed by the ripple, which is what makes the highlights break
//      into moving shards every time a drop lands.
//
// The ripple clock is the drip PERIOD. EnvironmentsBuilder emits the drip
// particles at exactly 1/_Period and delays the splash ring by the fall time, so
// all three (drop, ring, reflected shatter) belong to the same event.
//
// Everything is evaluated in OBJECT space; the puddle mesh is authored in room
// coordinates at identity, so object space IS room space and the light
// positions handed in by EnvRoomBuilder need no transform.
Shader "GloomhavenVR/EnvPuddle"
{
    Properties
    {
        _Wet ("Wet darkening (multiplied onto the floor)", Color) = (0.34,0.36,0.40,1)
        _Center ("Ripple centre (OBJECT space, xz)", Vector) = (0,0,0,0)
        _Radius ("Puddle radius (m)", Float) = 0.6
        _Period ("Seconds between drips (MUST equal EnvDrip._Period)", Float) = 2.6
        _Phase ("Phase (s) (MUST equal EnvDrip._Phase)", Float) = 0
        _Impact ("Seconds from cycle start to impact (EnvDrip._Hang + fall)", Float) = 2.3
        _RingFreq ("Rings per metre", Float) = 6.0
        _RingAmp ("Ripple slope", Float) = 0.35
        _RingCon ("Ripple contrast in the wet darkening", Range(0,1)) = 0.5
        _Calm ("Resting ripple slope", Float) = 0.06
        _SkyCol ("Broad sky/moon sheen", Color) = (0.10,0.13,0.20,1)
        _MoonDir ("Direction TOWARD the moon (OBJECT space)", Vector) = (0,1,0,0)
        _MoonCol ("Moon reflection colour", Color) = (0.42,0.52,0.78,1)
        _MoonPow ("Moon reflection tightness", Float) = 220
        _CandPos ("Candle position (OBJECT space)", Vector) = (0,1,0,0)
        _CandCol ("Candle reflection colour (a = flicker)", Color) = (1.0,0.55,0.22,0.9)
        _CandPow ("Candle reflection tightness", Float) = 90
        _CandRate ("Candle flicker rate (match its light slot)", Float) = 1
        _CandPhase ("Candle flicker phase (match its light slot)", Float) = 0
        _Fresnel ("Grazing-angle bias", Range(0,1)) = 0.85
        // ELEMENT ART — AIR. The draught's direction (EnvRoomBuilder.DraftDir,
        // OBJECT space) and the slope of the cat's paws it drags across the
        // water. The puddle already ruffles under Air (ElemRippleAmp), but a
        // rougher CONCENTRIC ripple is just a busier drip — it says the air is
        // moving and not which way. These waves travel, and they travel the way
        // the draught travels, so the water points at the window.
        _DraftDir ("Draught direction (OBJECT space)", Vector) = (0,0,0,0)
        _DraftWave ("Draught wave slope at full Air", Float) = 0
    }

    CGINCLUDE
    #include "UnityCG.cginc"
    #include "EnvElement.cginc"

    fixed4 _Wet, _MoonCol, _CandCol, _SkyCol;
    float4 _Center, _MoonDir, _CandPos, _DraftDir;
    float _Radius, _Period, _Phase, _Impact, _RingFreq, _RingAmp, _RingCon, _Calm;
    float _MoonPow, _CandPow, _CandRate, _CandPhase, _Fresnel, _DraftWave;
    float _GhvrTimeOfs;   // preview-only clock offset (see EnvRoom.shader)

    struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; };
    struct v2f
    {
        float4 pos  : SV_POSITION;
        float3 opos : TEXCOORD0;
        float3 ov   : TEXCOORD1;   // object-space view vector
        fixed4 vcol : COLOR;       // .a = puddle mask (1 deep, 0 at the rim)
    };

    v2f vert (appdata v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.opos = v.vertex.xyz;
        o.ov = ObjSpaceViewDir(v.vertex);
        o.vcol = v.color;
        return o;
    }

    /// Surface height of the ripple field at radius r, in metres of "slope
    /// units". One expanding train per drip plus a permanent, much slower
    /// breathing so the puddle is never a mirror-flat sheet of glass.
    // =============================================== ELEMENT ART ============
    // The puddle is the cellar's Ice, and it is the best surface in the room to
    // spend Ice on: it is the one thing the player has already watched MOVE
    // (the drip rings it every 2.85 s), so freezing it is a change to something
    // he knows the resting state of. Three effects, one number:
    //   the rings DIE (amp -> 0.15) — a glazed puddle does not ripple;
    //   the wet darkening goes pale and blue — ice is not water;
    //   the moon's reflection SHARPENS — a flat sheet of ice is a better mirror
    //   than a rippled puddle, and that is the give-away that it is frozen.
    // Air does the opposite to the same term (the draught ruffles the surface),
    // so Air+Ice reads as a half-frozen puddle with the wind still working the
    // open water — a mixture, not an average.
    /// Ripple amplitude multiplier. Exactly 1 when nothing is up.
    float ElemRippleAmp (GhvrElem e)
    {
        return max((1.0 - 0.85 * e.ice) * (1.0 + 1.8 * e.air), 0.0);
    }

    float RippleH (float r, float t, float amp)
    {
        // 0 exactly when the drop from EnvDrip touches the water: same clock,
        // same period, same phase, minus the hang+fall the drop spends in the air
        float P = max(_Period, 0.05);
        float w = frac((t + _Phase - _Impact) / P);
        float ringR = w * _Radius * 1.25;                  // the front runs outward
        float rel = r - ringR;
        float train = sin(rel * _RingFreq * 6.2831853) * exp(-abs(rel) * 3.0);
        float decay = (1.0 - w) * (1.0 - w);               // the ring dies as it spreads
        // the PREVIOUS drop's train is still on the water when the next lands
        float w2 = frac((t + _Phase - _Impact) / P + 0.5);
        float rel2 = r - w2 * _Radius * 1.25;
        float train2 = sin(rel2 * _RingFreq * 6.2831853) * exp(-abs(rel2) * 3.0)
                       * (1.0 - w2) * (1.0 - w2);
        float calm = sin(r * 9.0 - t * 1.1) * 0.5 + sin(r * 5.3 + t * 0.7) * 0.5;
        return ((train * decay + train2 * 0.55) * _RingAmp + calm * _Calm) * amp;
    }

    /// ELEMENT ART — AIR: one train of cat's paws, running along the draught.
    /// Crosswise it is broken up by a slow second wave, because a wind ripple on
    /// water is a set of short crests that do not line up, not a corrugation.
    /// Returned in the same "slope units" as RippleH so the two can be added.
    float WindH (float2 p, float t)
    {
        float2 d = p - _Center.xz;
        float u = dot(d, _DraftDir.xz);                          // along the draught
        float v = dot(d, float2(-_DraftDir.z, _DraftDir.x));     // across it
        return sin(u * 7.3 - t * 3.1 + sin(v * 3.7 + t * 0.6) * 0.85);
    }

    float3 RippleN (float3 opos, float t, float amp)
    {
        float2 d = opos.xz - _Center.xz;
        float r = length(d);
        float h0 = RippleH(r, t, amp);
        float h1 = RippleH(r + 0.012, t, amp);
        float slope = (h1 - h0) / 0.012;
        float2 dir = d / max(r, 1e-4);
        return normalize(float3(-dir.x * slope, 1.0, -dir.y * slope));
    }
    ENDCG

    SubShader
    {
        // after the opaque floor, before anything transparent: the puddle is
        // part of the floor, and nothing may sort against it
        Tags { "Queue"="Geometry+20" "RenderType"="Opaque" "IgnoreProjector"="True" }
        ZWrite Off
        Cull Back
        Offset -1, -1

        // ---- 1. the wet patch: darken the stone underneath ----
        Pass
        {
            Blend DstColor Zero
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag (v2f i) : SV_Target
            {
                // The rings have to be visible even when the water is mirroring
                // nothing: a reflection only exists at the one viewing angle that
                // catches the moon, and a ripple that can only be seen from there
                // is a ripple nobody sees. So the ring train also modulates how
                // dark the wet stone is — which is what a real ripple does, by
                // tilting the surface between "you see the dark bottom" and "you
                // see the sky".
                float t = _Time.y + _GhvrTimeOfs;
                GhvrElem e = GhvrElems();
                float r = length(i.opos.xz - _Center.xz);
                float h = RippleH(r, t, ElemRippleAmp(e));
                if (e.air > 0.0) h += _DraftWave * e.air * WindH(i.opos.xz, t);
                float m = saturate(i.vcol.a);
                // ELEMENT ART: ice glaze. The multiply pass darkens the stone by
                // _Wet; under Ice it stops darkening and starts PALING, which is
                // the difference between a wet flagstone and a frozen one.
                float3 wet = saturate(lerp(_Wet.rgb, float3(0.86, 0.92, 1.02), saturate(e.ice * 0.80))
                                      * (1.0 + _RingCon * h));
                return fixed4(lerp(float3(1,1,1), wet, m), 1.0);
            }
            ENDCG
        }

        // ---- 2. what the water reflects ----
        Pass
        {
            Blend One One
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag (v2f i) : SV_Target
            {
                float t = _Time.y + _GhvrTimeOfs;
                GhvrElem e = GhvrElems();
                float3 N = RippleN(i.opos, t, ElemRippleAmp(e));
                if (e.air > 0.0)
                {
                    // The cat's paws tilt the surface too, and that is where they
                    // are actually SEEN: the moon's reflection breaks into bands
                    // that crawl toward the stair door. Central-differenced over
                    // 2 cm — an analytic gradient of the nested sine is three
                    // more transcendentals for a normal that is then normalised
                    // anyway. Left entirely outside the resting path so the
                    // no-element frame keeps its exact radial normal.
                    float2 p = i.opos.xz;
                    // 0.30 of the authored slope, and the factor is arithmetic
                    // rather than taste: the wave's own spatial frequency is 7.3
                    // rad/m, so its DERIVATIVE is ~7x its amplitude. Feeding the
                    // amplitude that the wet-darkening pass wants straight into a
                    // normal would tilt the surface by more than a right angle
                    // and the reflections would go to noise.
                    float a = _DraftWave * e.air * 0.30;
                    float w0 = WindH(p, t);
                    float2 gr = float2(WindH(p + float2(0.02, 0), t) - w0,
                                       WindH(p + float2(0, 0.02), t) - w0) * (a / 0.02);
                    N = normalize(float3(N.x - gr.x, N.y, N.z - gr.y));
                }
                float3 V = normalize(i.ov);
                float3 R = reflect(-V, N);

                // water only mirrors properly at a grazing angle; seen from
                // straight above it is a dark hole in the floor
                float fres = lerp(1.0, pow(1.0 - saturate(dot(N, V)), 2.5), _Fresnel);

                // Two lobes for the moon, not one. The tight lobe IS the mirror
                // image and only exists from the one place you can stand to see
                // it; the broad one is the sky around the moon, which a rippled
                // puddle scatters over most of the hemisphere — that is the term
                // that makes the water read as water from anywhere in the room.
                // ELEMENT ART: ice sharpens the mirror (a sheet of ice is flat
                // where water is not), Light drives every reflected SOURCE.
                float md = saturate(dot(R, normalize(_MoonDir.xyz)));
                float moon = pow(md, _MoonPow * (1.0 + 2.5 * e.ice)) + 0.16 * pow(md, 3.0);

                float3 toC = _CandPos.xyz - i.opos;
                float cd2 = max(dot(toC, toC), 1e-4);
                float cand = pow(saturate(dot(R, toC * rsqrt(cd2))), _CandPow) / (1.0 + cd2 * 0.07);

                // same sine family, rate and phase as the candle's EnvRoom slot
                float ft = t * _CandRate;
                float f = 0.42 * sin(ft * 11.3 + _CandPhase)
                        + 0.33 * sin(ft *  6.1 + 1.7 + _CandPhase * 1.3)
                        + 0.25 * sin(ft * 19.7 + 4.2 + _CandPhase * 0.7);
                f = f * 0.70 + 0.30 * sin(ft * 1.9 + _CandPhase * 0.5);
                cand *= 1.0 + _CandCol.a * 0.35 * f;

                // ...plus the plain sheen of a wet surface: grazing angles see
                // the sky, and the ripple slope decides which way each band tips
                float sheen = fres * (0.65 + 0.35 * saturate(N.y * 4.0 - 3.0));
                // the candle's shard warms and grows with Fire; every reflected
                // source follows the split's source gain (Light lifts, Dark does
                // not dim — see EnvElement.cginc)
                float3 col = _MoonCol.rgb * moon
                           + _CandCol.rgb * (cand * (1.0 + 0.85 * e.fire))
                           + _SkyCol.rgb * sheen;
                col *= GhvrSrcGain(e);
                return fixed4(col * (fres * saturate(i.vcol.a)), 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
