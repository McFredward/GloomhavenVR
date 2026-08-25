// The WIND-BORNE FLAKES a floating window sheds while it materialises or dematerialises.
//
// WHAT DRAWS THIS. One quad per animating window, parented to the window's world-space host rect,
// slightly in front of it (-Z is toward the head — LoadingIndicator.cs:715). The quad is BIGGER
// than the window: its UVs run past 0..1 on the downwind side so the plume can leave the rect.
// Everything else the effect does happens in C# (WorldUI/WindowMaterialise*.cs) by writing
// CanvasRenderer alphas; this shader only paints the flakes.
//
// ---------------------------------------------------------------------------------------------
// WHY THERE IS NO _Time IN THIS FILE, AND WHY THAT IS THE WHOLE DESIGN
// ---------------------------------------------------------------------------------------------
// TRAP 1 - STEREO RIVALRY. A dissolve driven by SCREEN-SPACE noise gives the left and the right
// eye a different threshold for the same surface point, and the headset reads that as flicker
// rather than as texture. This project has already paid for that lesson (memory:
// "aliasing-is-per-eye", "flicker-is-elements-toggling"). EVERY quantity below is a function of
// `i.uv` alone - the interpolated PANEL UV - plus per-draw uniforms. There is no screen-space
// derivative, no depth-texture read, no `unity_CameraInvProjection`, no `_ScreenParams`, no
// `VPOS`. Both eyes rasterise the same triangle and interpolate the same UV to the same surface
// point, so both eyes compute a bit-identical value. That is the same argument
// HexDecalStable.shader makes at its head, and the same reason neither file uses an instancing
// macro: multipass is per-eye-correct by construction as long as nothing stale is read.
//
// TRAP 4 - FREQUENCY SCRUBBING. A strength dial that multiplies a FREQUENCY riding the shared
// clock is correct only at t=0. There is no clock here at all: the ONE time-like input is
// `_Progress`, written once per frame by C# and used only as a POSITION (the erosion front) and
// as an AMPLITUDE (how far the plume has travelled). No dial multiplies a frequency, because no
// frequency exists to multiply. `_FlakeDensity` scales a spatial rate that is constant in time.
//
// TRAP 5 - WINDING. `Cull Off`. The quad is a flat two-triangle sheet whose signed volume is zero
// and whose "correct" side is therefore undecidable from geometry; eight meshes have shipped in
// this project wound against the side they are seen from. Drawing both sides costs nothing here
// (no lighting, no depth write) and removes the failure mode entirely.
//
// PREMULTIPLIED ALPHA (`Blend One OneMinusSrcAlpha`), not additive. Additive flakes bloom to white
// over the pale parchment windows this game uses; premultiplied behaves like ordinary alpha at
// _Glow 1 and can still be pushed to a glow by raising _Glow, without ever blowing out.
//
// MUST BE COMPILED BY 2021.3.5f1 (`/home/claw/unity-2021.3.5`). A shader compiled by 2021.3.45
// renders PINK in game - see MapUnlit.shader's header.
Shader "GloomhavenVR/WindowMaterialise"
{
    Properties
    {
        _Tint ("Flake tint", Color) = (0.86, 0.80, 0.66, 1)
        _Glow ("Glow (1 = plain alpha)", Float) = 1.0

        // The animation state. C# owns all of these; nothing here reads a clock.
        _Progress ("Dissolve progress (0 present, 1 gone)", Range(0,1)) = 0
        _Wind ("Wind direction xy (unit, ISOTROPIC q-space)", Vector) = (0.92, 0.39, 0, 0)
        _Aspect ("Panel width / height", Float) = 1.0

        // Shape of the erosion front.
        _Softness ("Front softness", Float) = 0.15
        _Ragged ("Front raggedness (0 straight .. 1 pure noise)", Range(0,1)) = 0.55
        _FrontScale ("Front noise cells across the short side", Float) = 3.5
        _AgeSpan ("How long a crumbling EDGE point keeps flaking", Float) = 0.30
        _PlumeSpan ("How long a blown-away flake stays alive", Float) = 1.50
        _Spread ("How far the plume disperses sideways", Float) = 0.60
        _Streak ("How much a travelling flake stretches along the wind", Float) = 2.60
        _Thin ("How much sparser the plume gets as it blows away", Float) = 0.34
        _TailFade ("1 = the plume must die at progress 1 (a VANISH ends at nothing)", Float) = 1

        // Shape of the flakes.
        _FlakeDensity ("Flake noise cells across the short side", Float) = 34
        _FlakeCut ("Flake threshold", Range(0,0.95)) = 0.52
        _FlakeSharp ("Flake edge sharpness", Float) = 2.0
        _EdgeGain ("Crumbling-edge gain", Float) = 1.55
        _PlumeGain ("Blown-away plume gain", Float) = 0.85
        _Drift ("Plume travel at full progress (q-space units)", Float) = 0.80
        _Intensity ("Master amplitude", Range(0,2)) = 1.0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+550" "IgnoreProjector"="True" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend One OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };

            fixed4 _Tint;
            float _Glow, _Progress, _Aspect, _Softness, _Ragged, _FrontScale, _AgeSpan;
            float _PlumeSpan, _Spread, _TailFade, _Streak, _Thin;
            float _FlakeDensity, _FlakeCut, _FlakeSharp, _EdgeGain, _PlumeGain, _Drift, _Intensity;
            float4 _Wind;

            // ---- the field. Mirrored EXACTLY three times, on purpose --------------------------
            // (1) here, (2) in C# WindowMaterialiseField.cs - which decides each CanvasRenderer's
            // alpha, so the window's own elements wink out along the SAME front these flakes ride,
            // and (3) in unity/asset-preview/windowmaterialise_field.py, which is what the frame
            // strips are rendered from. If you change one, change all three; the Python file
            // carries a table of the constants for the diff.

            float hash21(float2 v)
            {
                float2 p = frac(v * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float vnoise(float2 v)
            {
                float2 i = floor(v);
                float2 f = v - i;
                float2 u = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1, 0));
                float c = hash21(i + float2(0, 1));
                float d = hash21(i + float2(1, 1));
                return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
            }

            // 0 at the upwind edge of the rect, 1 at the downwind edge, linear in between.
            float sweep(float2 uv)
            {
                float2 aw = abs(_Wind.xy);
                float n = max(aw.x + aw.y, 1e-4);
                return (dot(uv - 0.5, _Wind.xy) + 0.5 * n) / n;
            }

            // ISOTROPIC space: a panel 3x wider than tall must not get 3x-stretched flakes.
            float2 qOf(float2 uv) { return float2(uv.x * _Aspect, uv.y); }

            // (The per-point threshold is `lerp(sweep(uv), vnoise(qOf(uv) * _FrontScale),
            // _Ragged)` - it is inlined in frag() so the noise term can be reused for the plume's
            // sideways dispersal instead of being sampled twice. C# has it as a function,
            // WindowMaterialiseField.Threshold, because the element half needs it on its own.)

            // `cut` is passed in rather than read from _FlakeCut, because the plume RAISES it as
            // it ages: fewer and fewer cells clear the bar, so the debris gets sparser the further
            // it blows. That thinning is most of the difference between "blown away" and "a
            // rectangle of static".
            float speck(float n, float cut)
            {
                return pow(saturate((n - cut) / max(1e-3, 1.0 - cut)), _FlakeSharp);
            }

            // Soft 0..1 window on "is this UV inside the panel rect". The quad overhangs the rect,
            // and flakes may only be BORN inside it.
            float inRect(float2 uv, float w)
            {
                float2 e = smoothstep(0.0, w, uv) * smoothstep(0.0, w, 1.0 - uv);
                return e.x * e.y;
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // The front sweeps from just before the upwind edge to just past the downwind one,
                // so presence is EXACTLY 1 at _Progress 0 and EXACTLY 0 at _Progress 1 for every
                // UV. That exactness is what lets C# hand the window back at full alpha with no
                // epsilon and no "close enough" branch.
                float front = _Progress * (1.0 + 2.0 * _Softness) - _Softness;

                // A - THE CRUMBLING EDGE, still sitting on the window where it is being eaten. Its
                // life is short (_AgeSpan) because it is the band, not the debris.
                float2 q  = qOf(i.uv);
                float nA   = vnoise(q * _FrontScale);
                float tA   = lerp(sweep(i.uv), nA, _Ragged);
                float ageA = saturate((front - tA) / max(_AgeSpan, 1e-3));
                float envA = smoothstep(0.0, 0.12, ageA) * (1.0 - smoothstep(0.28, 0.80, ageA));
                float A = speck(vnoise(q * _FlakeDensity), _FlakeCut) * envA * _EdgeGain
                          * inRect(i.uv, 0.03);

                // B - THE PLUME. The same field sampled UPWIND of this fragment: a flake out here
                // is the one that was born back there. The offset depends only on _Progress, so it
                // is an AMPLITUDE and never a frequency; pow 1.5 gets it moving early instead of
                // sitting still for the first third.
                float travel = _Drift * pow(_Progress, 1.5);
                float invA   = 1.0 / max(_Aspect, 1e-3);
                float2 wuv = float2(_Wind.x * invA, _Wind.y);            // downwind, in UV
                float2 puv = float2(-_Wind.y * invA, _Wind.x);           // across the wind, in UV
                float2 uvB = i.uv - wuv * travel;
                float2 qB0 = qOf(uvB);
                float nB   = vnoise(qB0 * _FrontScale);
                // DISPERSE. A plume that only translates reads as a sliding texture; shearing the
                // source point sideways by a noise that grows with travel is what turns it into
                // something blowing apart. Same noise as the ragged front, so it costs nothing.
                uvB -= puv * ((nB - 0.5) * _Spread * travel);
                float2 qB  = qOf(uvB);
                float tB   = lerp(sweep(uvB), nB, _Ragged);
                float ageB = saturate((front - tB) / max(_PlumeSpan, 1e-3));
                float envB = smoothstep(0.03, 0.22, ageB) * (1.0 - smoothstep(0.55, 1.0, ageB));
                // STREAK ALONG THE WIND. A flake that has been travelling is a streak, not a dot,
                // and a field of dots at any density reads as static rather than as motion. The
                // noise coordinate is compressed ALONG the wind by a factor that grows with age, in
                // the wind-aligned frame — so the same cheap value noise draws round specks at the
                // crumbling edge and long streaks out in the plume.
                float2 wq = normalize(_Wind.xy);
                float2 pq = float2(-wq.y, wq.x);
                float stretch = 1.0 + _Streak * ageB;
                float2 qS = wq * (dot(qB, wq) / stretch) + pq * dot(qB, pq);
                // _TailFade IS THE ONE PLACE THE TWO DIRECTIONS ARE NOT MIRROR IMAGES, and they
                // must not be. A VANISH has to END at nothing, so its plume is faded out over the
                // last third of progress. An APPEAR STARTS at progress 1 - so the same fade would
                // make its first frames completely empty, i.e. a window that is live and clickable
                // while showing the player nothing at all. C# passes 1 for a vanish and 0 for an
                // appear.
                float tail = 1.0 - _TailFade * smoothstep(0.68, 1.0, _Progress);
                // The plume's rect edge is soft AND ragged: a hard `inRect` drew a straight-sided
                // rectangle of debris with a ruler-flat bottom, which no wind has ever done.
                float edgeB = inRect(uvB, 0.13) * (0.45 + 0.55 * nB);
                float B = speck(vnoise(qS * _FlakeDensity * 1.83 + 7.3), _FlakeCut + _Thin * ageB)
                          * envB * _PlumeGain * edgeB * tail;

                float a = saturate(A + B) * _Intensity * _Tint.a;
                return fixed4(_Tint.rgb * a * _Glow, a);
            }
            ENDCG
        }
    }
}
