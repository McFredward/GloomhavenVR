// GloomhavenVR — ANALYTIC volumetric shaft. The cellar's moonlight.
//
// USER FINDING, ModBuild 135 (hardware): "die Mondstraheln sind wirklich
// 5 Strahlen (sehen aus wie Laser) durch das Fenster. Stattdessen soll es ein
// realistisches Licht sein was durch das Fenster leicht hereinkommt vom Mond."
//
// WHY THE OLD CONSTRUCTION HAD TO GO. ModBuild 135 built the beam out of five
// EnvShaft slats, one per gap between the window bars, so that the bar shadows
// would be free geometry. Each slat is a pair of crossed flat blades. Five
// slats side by side, each with its own hard gaussian cross-section and its own
// silhouette, is five bright edges in a black room — i.e. five lasers. No amount
// of dimming fixes that: the problem is that a BLADE HAS AN OUTLINE, and an
// outline is what the eye reads as an object rather than as light.
//
// WHAT THIS IS INSTEAD. One volume, evaluated analytically per fragment. The
// mesh is only a bounding HULL (a truncated cone around the beam axis, clipped
// to the room's own wall and floor planes) — it is never seen: every pixel's
// value is an integral estimate through a smooth density field, so the hull's
// own edge sits where the density is already ~2% and it cannot show a
// silhouette. Turn your head and the shaft behaves like a shaft: it brightens
// as you look ALONG it (longer path through the volume), it dims broadside, it
// has no faces, and there is no orientation at which a slab betrays itself.
//
// THE DENSITY MODEL, per fragment:
//   ray        : from the camera through this fragment (OBJECT space, see below)
//   s, dperp   : closest approach of that ray to the beam axis segment [0, _Len]
//   w(s)       : gaussian 1/e half-width, _W0 + _WK*s — the gentle widening
//   radial     : exp(-(dperp/w)^2)              — soft in every lateral direction
//   along      : ramp out of the aperture * exp(-s*_Decay) * taper at the floor
//   path       : w(s)/max(sin(theta), _MinSin)  — the VOLUMETRIC term: looking
//                down the beam crosses more of it than looking across it. This
//                is the one thing a billboard or a blade can never do, and the
//                reason this reads as air rather than as a surface.
//   stripe     : the bar shadows, as a SUBTLE modulation (see below)
// The result is passed through a Reinhard knee so that standing inside the
// volume cannot blow out to white.
//
// BAR SHADOWS ARE NOW A MODULATION, NOT GEOMETRY. The user asked for restraint
// ("leicht hereinkommend"), so the bars survive only as soft dark striping that
// is strongest at the aperture and gone within ~1.5 m. The stripe coordinate is
// exact, not projected: a point is traced BACK along the light direction to the
// window plane (_WinZ) and its x there is compared against the real bar pitch,
// so the stripes are the bars' true shadows and follow the window if it moves.
// The penumbra widens with distance (_BarSig + _BarBlur*s), which is what makes
// them dissolve instead of ending.
//
// EVERYTHING IS OBJECT SPACE. The room prefab is SCALED at runtime (PlaySpace
// normalisation, ModBuild 134), so world-space constants baked at build time
// would be wrong in the game. The camera is transformed into object space and
// all constants are authored-metres in room coordinates — exactly like
// EnvRoom.shader's baked light rig. Uniform scale in, uniform scale out.
//
// VR SAFETY. Nothing here is camera-FACING: the hull is world-fixed geometry
// and never rotates. The view dependence is a smooth, symmetric function of the
// camera POSITION (it is the same term EnvShaft has always used for its blades,
// generalised), identical in both eyes and continuous under head motion — the
// standing ground-fog ruling forbids sprites that swivel, not volumes that
// integrate. No screen-space anything, no depth-texture read.
//
// Additive, no depth write, back faces only (Cull Front) so the hull still
// covers the screen when the camera walks into it.
Shader "GloomhavenVR/EnvBeam"
{
    Properties
    {
        _Tint ("Beam color (a = strength)", Color) = (0.55,0.68,1.0,0.5)
        _BeamOrg ("Axis origin (OBJECT space)", Vector) = (0,0,0,0)
        _BeamDir ("Axis direction of travel (OBJECT space)", Vector) = (0,-1,0,0)
        _Len ("Axis length (m)", Float) = 4
        _W0 ("Gaussian half-width at the aperture (m)", Float) = 0.30
        _WK ("Widening per metre", Float) = 0.10
        _RadPow ("Cross-section falloff exponent (1 = gaussian)", Range(0.6,3)) = 1.35
        _Ramp ("Ramp out of the aperture (m)", Float) = 0.30
        _Decay ("Density decay per metre", Float) = 0.85
        _EndFade ("Taper before the floor (m)", Float) = 0.45
        _MinSin ("Path-length clamp (sin theta floor)", Range(0.05,1)) = 0.30
        _Knee ("Reinhard knee", Range(0,4)) = 0.9
        _Shimmer ("Shimmer amount", Range(0,1)) = 0.12
        _ShimmerSpeed ("Shimmer speed", Range(0,2)) = 0.13
        // bar shadows
        _WinZ ("Window plane z (OBJECT space)", Float) = 0
        _BarX0 ("First bar x at the window plane", Float) = 0
        _BarPitch ("Bar pitch (m)", Float) = 0.22
        _BarDepth ("Bar shadow depth", Range(0,1)) = 0.38
        _BarSig ("Bar shadow half-width at the aperture (m)", Float) = 0.030
        _BarBlur ("Penumbra growth per metre", Float) = 0.085
        _BarFade ("Bar shadow fade length (m)", Float) = 0.9
    }
    SubShader
    {
        Tags { "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Front          // draw the FAR side of the hull: works from outside
                            // the volume and from inside it alike
        Fog { Mode Off }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Tint;
            float4 _BeamOrg, _BeamDir;
            float _Len, _W0, _WK, _Ramp, _Decay, _EndFade, _MinSin, _Knee;
            float _Shimmer, _ShimmerSpeed;
            float _WinZ, _BarX0, _BarPitch, _BarDepth, _BarSig, _BarBlur, _BarFade;
            float _GhvrTimeOfs;   // preview-only clock offset (see EnvRoom.shader)

            float _RadPow;

            struct appdata { float4 vertex : POSITION; };
            struct v2f { float4 pos : SV_POSITION; float3 opos : TEXCOORD0; float4 spos : TEXCOORD1; };

            // Interleaved gradient noise (Jimenez, SIGGRAPH 2014), as used on
            // the sky gradient. A beam is a very smooth ramp across ~10 of 256
            // blue levels in an otherwise black room: without a sub-LSB dither
            // it contours into onion rings, which is exactly the "manufactured
            // object" read this whole construction exists to avoid.
            float Ign(float2 p) { return frac(52.9829189 * frac(dot(p, float2(0.06711056, 0.00583715)))); }

            #define SKY_PERIOD 2880.0

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.opos = v.vertex.xyz;
                o.spos = ComputeScreenPos(o.pos);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float t = fmod(_Time.y + _GhvrTimeOfs, SKY_PERIOD);

                // ---- the view ray, in OBJECT space ----
                float3 cam = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1.0)).xyz;
                float3 R = i.opos - cam;
                R = normalize(R + 1e-6);
                float3 D = normalize(_BeamDir.xyz);

                // ---- closest approach of the ray to the axis LINE ----
                // minimise |w + tR - sD|^2 with w = cam - origin
                float3 w = cam - _BeamOrg.xyz;
                float b = dot(R, D);
                float dd = dot(R, w);
                float ee = dot(D, w);
                float den = max(1.0 - b * b, 1e-3);       // near-parallel guard
                float s = (ee - dd * b) / den;
                float tt = s * b - dd;

                // Clamped to the SEGMENT and to the half-ray. Past either end the
                // closest point stops moving, so dperp grows and the gaussian
                // takes the beam out on its own — no clip, no edge.
                float sc = clamp(s, 0.0, _Len);
                float3 X = _BeamOrg.xyz + D * sc;
                float3 Q = cam + R * max(tt, 0.0);
                float3 del = Q - X;

                float wd = _W0 + _WK * sc;
                // Super-gaussian cross-section. A plain gaussian still has 1.8%
                // of its peak two sigma out, which is enough to show the hull's
                // own rim in a black room; exp(-q^1.35) is 0.2% there and 1e-6
                // at the 2.85 sigma the hull is actually built at, while the
                // core keeps a soft shoulder. The edge is still C-infinity —
                // there is no distance at which it becomes a line.
                float q = dot(del, del) / (wd * wd);
                float radial = exp(-pow(max(q, 1e-6), _RadPow));

                // along the beam: emerges from the reveal, then thins out
                float along = smoothstep(0.0, _Ramp, sc)
                            * exp(-sc * _Decay)
                            * smoothstep(_Len, _Len - _EndFade, sc);

                // THE volumetric term
                float sinT = sqrt(saturate(1.0 - b * b));
                float path = (wd / max(sinT, _MinSin)) / _W0;

                // ---- bar shadows, traced back to the window plane ----
                // Q is the point in the volume this pixel is mostly looking at;
                // follow the light backwards from it to z = _WinZ and ask which
                // gap between the bars it came through.
                float u = (_WinZ - Q.z) / (abs(D.z) > 1e-4 ? D.z : 1e-4);
                float xw = Q.x + u * D.x;
                float ph = (xw - _BarX0) / _BarPitch;
                float dbar = abs(frac(ph + 0.5) - 0.5) * _BarPitch;   // metres to the nearest bar
                float sig = _BarSig + _BarBlur * sc;                  // penumbra grows with distance
                float stripe = 1.0 - _BarDepth * exp(-sc / _BarFade)
                                   * exp(-(dbar * dbar) / (sig * sig));

                // slow drifting density — motes and mist crossing the beam
                float sh = 1.0 + _Shimmer * (sin(sc * 3.1 + t * _ShimmerSpeed * 5.3)
                                           * sin(sc * 1.3 - t * _ShimmerSpeed * 2.9
                                                 + dot(del, D.yzx) * 2.7));

                float I = _Tint.a * radial * along * path * stripe * sh;
                I = I / (1.0 + I * _Knee);                 // cannot blow out
                // sub-LSB dither, on the linear value: ±0.5/255 of the final
                // 8-bit step, applied AFTER the knee so it cannot be amplified
                I += (Ign(i.spos.xy / max(i.spos.w, 1e-4) * _ScreenParams.xy) - 0.5) * 0.0010;
                return fixed4(_Tint.rgb * max(I, 0.0), 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
