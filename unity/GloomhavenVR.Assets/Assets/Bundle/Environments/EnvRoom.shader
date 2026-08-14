// GloomhavenVR — self-lit room-interior shader (custom-asset environments).
//
// The environments are script-free prefabs on the mod layer; the game scene
// contributes NO usable lights, so ALL lighting is baked into material
// parameters and evaluated here:
//   - hemisphere ambient (world-up based: _AmbUp / _AmbDown),
//   - one directional "moon" light (direction in OBJECT space, set per
//     placement by BuildEnvironments.cs),
//   - up to 3 point lights (candles/lanterns; positions in OBJECT space,
//     w = 1/range in object units; alpha of the color = flicker amount,
//     animated purely by _Time — data-driven, no scripts, stereo-correct).
// Per-pixel normal mapping works naturally against these lights (TBN stays
// in object space, no per-frame CPU cost). Vertex color (lerped in by _VCol)
// carries baked large-scale shading such as the swamp ground's radial
// fade-to-darkness. Diffuse only: no view-dependent terms except none — the
// safest possible shader for stereo.
//
// Written into the bundle because builtin shaders may be stripped from the
// game player (the pink-material trap, TOOLCHAIN.md §4.1).
Shader "GloomhavenVR/EnvRoom"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal map", 2D) = "bump" {}
        _BumpScale ("Normal strength", Range(0,2)) = 1
        _Tint ("Tint", Color) = (1,1,1,1)
        _AmbUp ("Hemisphere ambient - sky", Color) = (0.05,0.06,0.08,1)
        _AmbDown ("Hemisphere ambient - ground", Color) = (0.015,0.015,0.015,1)
        _DirDir ("Directional dir (OBJECT space, toward light)", Vector) = (0,1,0,0)
        _DirCol ("Directional color", Color) = (0,0,0,1)
        _L0Pos ("Light0 pos (OBJECT space, w=1/range)", Vector) = (0,0,0,1)
        _L0Col ("Light0 color (a=flicker)", Color) = (0,0,0,0)
        _L1Pos ("Light1 pos (OBJECT space, w=1/range)", Vector) = (0,0,0,1)
        _L1Col ("Light1 color (a=flicker)", Color) = (0,0,0,0)
        _L2Pos ("Light2 pos (OBJECT space, w=1/range)", Vector) = (0,0,0,1)
        _L2Col ("Light2 color (a=flicker)", Color) = (0,0,0,0)
        _VCol ("Vertex color amount", Range(0,1)) = 0
        // Cold moon rim (forest trunks). Defaults to BLACK so every existing
        // material — the whole cellar — is bit-identical without it.
        _RimCol ("Rim light color", Color) = (0,0,0,1)
        _RimPow ("Rim tightness", Range(0.5,8)) = 3.0
        _RimDir ("Rim gate: light dir (OBJECT space)", Vector) = (0,1,0,0)
        // Point-light NEAR-FIELD hardness (see PointLight below). 0 reproduces
        // the old pure (1-(d/r)^2)^2 window exactly => the forest is untouched.
        _PtHard ("Point falloff hardness", Range(0,64)) = 0
        [Toggle] _Cutout ("Alpha cutout", Float) = 0
        _Cutoff ("Cutout threshold", Range(0,1)) = 0.5

        // ---- ELEMENT ART (see EnvElement.cginc) ----
        // The room's own frame, written for every material by ApplyRig, so that
        // "the periphery" means the same thing for a wall authored around the
        // origin and for a barrel with its own transform. Defaults are harmless:
        // a centre of 0 and a radius of 6 m only decide WHERE an element blooms,
        // and no element blooms at all until the master is up.
        _ElemCentre ("Element: room centre (OBJECT space)", Vector) = (0,0,0,0)
        _ElemRad ("Element: room outer radius (object units)", Float) = 6
        // Per-material susceptibility. Frost and the fire rim are ON by default
        // (stone, bark, wood — everything the elements should reach); the green
        // is OFF by default and switched on for the things that can plausibly
        // grow moss, because a green barrel is a bug and a green root is Earth.
        _ElemFrost ("Element: ice frost susceptibility", Range(0,2)) = 1
        _ElemWarm ("Element: fire rim susceptibility", Range(0,2)) = 1
        _ElemMoss ("Element: earth green susceptibility", Range(0,2)) = 0
    }

    CGINCLUDE
    #include "UnityCG.cginc"
    #include "EnvElement.cginc"

    sampler2D _MainTex; float4 _MainTex_ST;
    sampler2D _BumpMap;
    float _BumpScale, _VCol, _Cutout, _Cutoff, _RimPow, _PtHard;
    fixed4 _Tint, _AmbUp, _AmbDown, _DirCol, _L0Col, _L1Col, _L2Col, _RimCol;
    float4 _DirDir, _L0Pos, _L1Pos, _L2Pos, _RimDir;
    float4 _ElemCentre;
    float _ElemRad, _ElemFrost, _ElemWarm, _ElemMoss;

    // PREVIEW-ONLY global clock offset. Never set at runtime (=> 0, the shipped
    // behaviour); EnvironmentsPreview sets it with Shader.SetGlobalFloat so a
    // still frame can be rendered at an arbitrary point of every animation.
    // Declared in every Env* shader that reads _Time — the whole room has to
    // move to the SAME offset or a time series proves nothing.
    float _GhvrTimeOfs;

    struct appdata
    {
        float4 vertex  : POSITION;
        float3 normal  : NORMAL;
        float4 tangent : TANGENT;
        float2 uv      : TEXCOORD0;
        fixed4 color   : COLOR;
    };

    struct v2f
    {
        float4 pos    : SV_POSITION;
        float2 uv     : TEXCOORD0;
        float3 opos   : TEXCOORD1; // object-space position
        float3 n      : TEXCOORD2; // object-space normal
        float3 t      : TEXCOORD3; // object-space tangent
        float3 b      : TEXCOORD4; // object-space bitangent
        float3 ov     : TEXCOORD5; // object-space view vector (rim light only)
        fixed4 vcol   : COLOR;
    };

    v2f vert (appdata v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = TRANSFORM_TEX(v.uv, _MainTex);
        o.opos = v.vertex.xyz;
        o.n = v.normal;
        o.t = v.tangent.xyz;
        o.b = cross(v.normal, v.tangent.xyz) * v.tangent.w;
        o.ov = ObjSpaceViewDir(v.vertex);
        o.vcol = v.color;
        return o;
    }

    // Candle flicker — three incommensurate sines plus one slow "breath", each
    // light on its OWN phase AND its own rate, so two candles in one room never
    // pulse as a pair. Amplitude is exactly +-amt*0.35 (the sine weights sum to
    // 1 and the breath is mixed in, not added on top).
    // amt is the alpha of the light colour, written by EnvRoomBuilder's rig.
    float Flicker (float amt, float phase, float rate)
    {
        float t = (_Time.y + _GhvrTimeOfs) * rate;
        float f = 0.42 * sin(t * 11.3 + phase)
                + 0.33 * sin(t *  6.1 + 1.7 + phase * 1.3)
                + 0.25 * sin(t * 19.7 + 4.2 + phase * 0.7);
        // the slow term is what a draft does to a flame: the whole pool swells
        // and sinks over a couple of seconds instead of only buzzing
        f = f * 0.70 + 0.30 * sin(t * 1.9 + phase * 0.5);
        return 1.0 + amt * 0.35 * f;
    }

    // Attenuation window (1-(d/r)^2)^2 divided by a near-field inverse-square
    // term. _PtHard = 0 is EXACTLY the old window (the forest's three points);
    // _PtHard > 0 collapses the lit pool toward the source, which is the only
    // way a candle can light its own table and leave the far wall black
    // (user finding, ModBuild 134: "die Kerzen beleuchten hier viel zu viel").
    // ELEMENT ART: `flickMul` and `hardMul` are the two knobs the elements turn
    // here. Both are exactly 1 when nothing is up (GhvrSrcHard's contract), so
    // the expression below is the ModBuild-134 expression unchanged, term for
    // term, in the shipped no-element state.
    //   flickMul — Air works the flames, so the POOLS shiver, not just the
    //              sprites. A draught you can see on the wall is a draught.
    //   hardMul  — Dark collapses the pool toward its own flame (see the split
    //              in EnvElement.cginc); Light opens it out again.
    float3 PointLight (float4 lpos, fixed4 lcol, float3 opos, float3 N, float phase, float rate,
                       float flickMul, float hardMul)
    {
        float3 lv = lpos.xyz - opos;
        float d2 = max(dot(lv, lv), 1e-8);
        float d = sqrt(d2);
        float q = d2 * lpos.w * lpos.w;                  // (d/range)^2
        float x = saturate(1.0 - q);
        float atten = x * x / (1.0 + _PtHard * hardMul * q);
        float ndl = saturate(dot(N, lv / d));
        return lcol.rgb * (atten * ndl * Flicker(lcol.a * flickMul, phase, rate));
    }

    fixed4 fragCore (v2f i, float face)
    {
        fixed4 alb = tex2D(_MainTex, i.uv) * _Tint;
        if (_Cutout > 0.5) clip(alb.a - _Cutoff);

        float3 n_ts = UnpackNormal(tex2D(_BumpMap, i.uv));
        n_ts.xy *= _BumpScale;
        float3 N = normalize(i.t * n_ts.x + i.b * n_ts.y + i.n * n_ts.z);
        N *= face; // two-sided foliage: light the visible side

        // hemisphere ambient against WORLD up (correct under prefab yaw/scale)
        float3 nw = normalize(mul((float3x3)unity_ObjectToWorld, N));

        // ================================================== ELEMENT ART ======
        // The whole block is behind ONE uniform compare on peak x master. With
        // no element up it does not execute, and the six modifiers below stay at
        // their identity values — which is why the zero state is not merely
        // "close to" the tuned room but the same instructions on the same data.
        // See EnvElement.cginc for the contract and for the Light/Dark split.
        GhvrElem e = GhvrElems();
        float ambGain = 1.0, srcGain = 1.0, dirGain = 1.0, hardMul = 1.0, flickMul = 1.0;
        float3 elemAdd = float3(0, 0, 0);
        float3 rimTint = float3(1, 1, 1);
        if (e.live > 0.0)
        {
            float t = _Time.y + _GhvrTimeOfs;
            // where this pixel is, as a fraction of the way out to the walls
            float2 dc = i.opos.xz - _ElemCentre.xz;
            float rr = GhvrRim(length(dc), _ElemRad, 0.18);

            ambGain = GhvrAmbGain(e);
            srcGain = GhvrSrcGain(e);
            dirGain = GhvrDirGain(e);
            hardMul = GhvrSrcHard(e);
            // AIR: the draught works the candles (see PointLight). The forest's
            // three "points" are a wisp, a far lantern and the shafts' landing
            // pool, all with a flicker alpha near zero, so this is felt in the
            // cellar and is a no-op in the wood — which is where the design puts
            // Air's cellar channel ("the authored draught strengthens").
            flickMul = 1.0 + 1.20 * e.air;

            // ---- ICE: frost blooms on the stone, from the outside in --------
            // A THRESHOLD, not a fade: frost forms in patches and spreads, so
            // coverage is thresholded against the surface's own tone (alb.g, one
            // free channel of a texture already sampled) and against how much of
            // the sky the surface can see. Upward faces frost first, walls
            // later, undersides never — which is what a cellar in a cold snap
            // and a forest floor at dawn actually look like.
            // REJECTED: a procedural noise field for the patches. It would have
            // cost a 3D hash per pixel over every opaque surface in the room for
            // a pattern the albedo already contains.
            // 0.28 + 0.95*rr, not 0.42 + 0.85: the first bake put 29% coverage on
            // the flagstones the board stands on, and the board's own floor is
            // the one surface an element may not repaint. This leaves the middle
            // of the play space at ~9% and still reaches full coverage at the
            // walls — the frost creeps in from the outside, which is also what
            // frost does.
            float cov = e.ice * _ElemFrost * (0.28 + 0.95 * rr);
            float frost = saturate(cov * 1.45 - 0.32)
                        * saturate(nw.y * 0.70 + 0.34)
                        * saturate(alb.g * 1.7 + 0.12);
            // keep the surface's own modelling: the frost takes its brightness
            // from the albedo it sits on, so a dark stone frosts dark.
            alb.rgb = lerp(alb.rgb, float3(0.70, 0.80, 0.96) * (0.50 + 0.55 * alb.g), frost);
            // ...and frost is a diffuse white, so it also answers the ambient a
            // little more strongly than wet stone does.
            ambGain += 0.30 * frost;
            // the moon rim goes cold with Ice (the forest's Ice channel)
            rimTint = lerp(float3(1, 1, 1), float3(0.70, 0.88, 1.30), saturate(e.ice));

            // ---- FIRE: a warm rim on the faces that look INTO the room ------
            // Not a fill light: a fire in the room lights the sides of things
            // that face it, and everything faces the middle. Gated on the
            // inward-facing term so the outside of a barrel stays cold, and on
            // the grazing term so it reads as a rim rather than as a repaint.
            float3 toC = _ElemCentre.xyz - i.opos;
            float inward = saturate(dot(N, toC * rsqrt(max(dot(toC, toC), 1e-4))));
            float graze = 1.0 - saturate(dot(N, normalize(i.ov)));
            elemAdd += float3(1.00, 0.36, 0.10)
                     * (e.fire * _ElemWarm * 0.26 * inward * (0.30 + 0.70 * graze * graze)
                        * GhvrEmberBreath(t, rr * 5.3));

            // ---- EARTH: moss takes, roots take, stone does not --------------
            // A tint TOWARD green rather than an added green: moss is pigment,
            // not light, and an additive green over dark bark glows like a
            // screen. _ElemMoss is 0 on everything the builder does not
            // explicitly consider growable.
            float damp = e.earth * _ElemMoss;
            alb.rgb *= lerp(float3(1, 1, 1), float3(0.74, 1.16, 0.72), saturate(damp));
            // the damp sheen: a grazing-angle wet gloss, cold and very small.
            elemAdd += float3(0.05, 0.085, 0.055) * (damp * graze * graze * (0.5 + 0.5 * rr));
        }
        // =====================================================================

        float3 light = lerp(_AmbDown.rgb, _AmbUp.rgb, nw.y * 0.5 + 0.5) * ambGain;

        light += _DirCol.rgb * saturate(dot(N, normalize(_DirDir.xyz))) * dirGain;
        // slot phases AND rates are incommensurate: the room breathes, it does
        // not pulse (user, ModBuild 134: "Eine flackernde Kerze sollte auch das
        // Licht drumrum zum flackern bekommen")
        // Each slot is scaled and accumulated SEPARATELY, and that is not a
        // style choice: summing the three first and scaling once would re-
        // associate three floating-point additions, and a re-associated sum can
        // differ in its last bit. The zero state has to be identical, not nearly
        // identical, so the accumulation order stays exactly as it was.
        light += PointLight(_L0Pos, _L0Col, i.opos, N, 0.0, 1.00, flickMul, hardMul) * srcGain;
        light += PointLight(_L1Pos, _L1Col, i.opos, N, 2.1, 0.83, flickMul, hardMul) * srcGain;
        light += PointLight(_L2Pos, _L2Col, i.opos, N, 4.4, 1.19, flickMul, hardMul) * srcGain;

        float3 col = alb.rgb * light + elemAdd * alb.rgb;

        // Cold rim: a grazing-angle wrap of the moon, gated so only the moonlit
        // SIDE of a trunk catches it. This is what makes a night forest read as
        // volumes instead of flat silhouettes. It is view-dependent and so
        // differs slightly between the eyes — which is physically what a rim IS,
        // and the gradient is smooth, so it fuses (unlike a screen-space
        // pattern, which would not). Black by default => opt-in per material.
        float rim = pow(1.0 - saturate(dot(N, normalize(i.ov))), _RimPow)
                  * saturate(dot(N, normalize(_RimDir.xyz)) * 0.5 + 0.55);
        // ELEMENT ART — the rim IS the moon, so it follows the moon's own gain
        // (Light lifts it, Dark takes it away only while Light is not up), and
        // Ice pushes it toward the blue end. That is the forest's authored Ice
        // channel: "a cold cast on the moon term". It is deliberately the same
        // dirGain the directional term uses, so a trunk's lit side and its rim
        // can never disagree about how bright the moon is.
        col += _RimCol.rgb * (rim * dirGain) * rimTint;
        col *= lerp(float3(1, 1, 1), i.vcol.rgb, _VCol);
        return fixed4(col, 1.0);
    }
    ENDCG

    // -------- opaque (default) --------
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag (v2f i) : SV_Target { return fragCore(i, 1.0); }
            ENDCG
        }
    }
    Fallback Off
}
