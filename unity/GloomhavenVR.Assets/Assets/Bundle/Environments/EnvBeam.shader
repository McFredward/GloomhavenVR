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
// WHAT THIS IS INSTEAD. One volume. The mesh is only a bounding HULL (a
// cylinder about the beam axis, clipped to the room's own wall and floor
// planes) — it is never seen: every pixel's value is a LINE INTEGRAL of a
// smooth density field along that pixel's view ray, so the hull's own edge sits
// where the density is already ~1e-6 and it cannot show a silhouette. Turn your
// head and the shaft behaves like a shaft: it brightens as you look ALONG it
// (the ray then runs metres inside the volume), it dims broadside, it has no
// faces, and there is no orientation at which a slab betrays itself.
//
// ============================ USER FINDING, ModBuild 137 (hardware) =========
// "Im Keller wenn man nah in den Mondschein am Fenster geht verschwindet er
//  plötzlich."  — walk INTO the beam and it is gone, all of it, at once.
// Two independent defects, both proven in the preview harness (a debug pass
// that painted the hull's coverage found ZERO hull pixels from every camera
// standing in the beam, while the same frame from 4 m away was fully covered):
//
//  (1) THE CULL MODE WAS INVERTED WITH RESPECT TO THE HULL'S WINDING.
//      BeamHullMesh emitted its side quads and its caps wound so that the
//      geometric normal pointed INWARD, i.e. against the outward normals it
//      stored in the vertices. With `Cull Front` that kept the NEAR faces, not
//      the far ones — and the near faces are exactly what ceases to exist the
//      moment the camera crosses the hull. The mesh is now wound outward (see
//      BuildEnvironmentRooms.BeamHullMesh, whose comment carries the proof), so
//      `Cull Front` really does draw the far side: the ray's EXIT point, which
//      exists from outside and from inside alike and is the SAME surface in
//      both cases — so walking in cannot pop.
//
//  (2) THE DENSITY MODEL HAD A SINGULARITY ALONG ITS OWN AXIS.
//      It sampled the density ONCE, at the ray's closest approach to the axis,
//      and multiplied by a 1/sin(theta) "path length". The axial coordinate of
//      that closest approach is
//          s = w_par - b * dot(R, w_perp) / sin^2(theta)
//      which diverges as the view direction approaches the axis: ~10 deg off
//      the axis it is already metres out of range, and an `s` clamped to either
//      end of the segment makes `along` — which is smoothstep-zero at BOTH ends
//      — exactly 0. Looking up the beam toward the window, the one pose in
//      which a light shaft is judged, the shaft therefore switched off. It is
//      there in the 136 previews as a black dot dead centre of the frame.
//      A point sample is also simply wrong once the camera is INSIDE the
//      volume: half of it is then behind the head and must not be counted.
//
// THE FIX FOR (2), and the model as it now stands. The value of a pixel is
//
//      I = _Tint.a / _W0 * INTEGRAL over t of rho(cam + t*R) dt ,  t in [t0,t1]
//
// with the interval derived analytically, never guessed:
//      t0 = max(0, entry into the support cylinder of radius _HullR,
//                  entry into the axial slab 0 <= s <= _Len)
//      t1 = min(exit from that cylinder, exit from that slab)
// The integrand vanishes smoothly at both ends of that interval (the cylinder
// is sized where the super-gaussian is ~1e-6 of peak, and the slab's ends are
// where the ramp and the end taper are zero), so a fixed-step midpoint rule is
// exact to well under a per cent — for a smooth bump with vanishing endpoints
// the midpoint rule converges exponentially in the step count (Euler-Maclaurin)
// — and there is no endpoint discontinuity that could band. NO stochastic
// jitter: a screen-space dither differs between the two eyes and would be
// stereo rivalry, which this project forbids. `t0 = max(0, ...)` is the whole
// of the "camera inside" case: the head simply becomes the near end of the
// integral, so the beam thins out continuously as you walk into and through it
// instead of popping.
//
//   rho(P) = radial * along * stripe, evaluated per sample:
//     s        = axial coordinate of P (metres from the aperture)
//     w(s)     = _W0 + _WK*s          — the gentle widening
//     radial   = exp(-(dperp^2/w^2)^_RadPow)   — super-gaussian cross-section
//     along    = ramp out of the reveal * exp(-s*_Decay) * taper at the floor
//     stripe   = the bar shadows (below)
//   ...and the shimmer is applied once, at the density-weighted centroid of the
//   ray's own samples, so it stays a property of the air rather than of the
//   sampling. The sum is passed through a Reinhard knee: standing inside the
//   volume and looking up it cannot blow out to white.
//
// BAR SHADOWS ARE A MODULATION, NOT GEOMETRY. The user asked for restraint
// ("leicht hereinkommend"), so the bars survive only as soft dark striping that
// is strongest at the aperture and gone within ~1.5 m. The stripe coordinate is
// exact, not projected: each sample is traced BACK along the light direction to
// the window plane (_WinZ) and its x there is compared against the real bar
// pitch, so the stripes are the bars' true shadows and follow the window if it
// moves. The penumbra widens with distance (_BarSig + _BarBlur*s), which is what
// makes them dissolve instead of ending. They are applied per SAMPLE now rather
// than once per pixel, so they are the shadows in the air the ray actually
// crosses: they wash out along the ray by themselves and cannot swim when the
// head moves.
//
// EVERYTHING IS OBJECT SPACE. The room prefab is SCALED at runtime (PlaySpace
// normalisation, ModBuild 134), so world-space constants baked at build time
// would be wrong in the game. The camera is transformed into object space and
// all constants are authored-metres in room coordinates — exactly like
// EnvRoom.shader's baked light rig. Uniform scale in, uniform scale out.
//
// VR SAFETY. Nothing here is camera-FACING: the hull is world-fixed geometry
// and never rotates. The result is a smooth, symmetric function of the camera
// POSITION and of the ray direction, identical in both eyes and continuous
// under head motion — the standing ground-fog ruling forbids sprites that
// swivel, not volumes that integrate. No screen-space anything (the only
// screen-space term is the sub-LSB dither, which is below one 8-bit step and
// therefore below the fusion threshold), no depth-texture read.
//
// COST. _Steps taps of ~4 transcendentals each, over the hull's screen
// footprint, once per eye. The hull is a 1.2 m-radius cylinder in one corner of
// one room. It is a deliberate choice against the alternative — a cheap closed
// form — because every closed form for this integral has a singularity
// somewhere, and this shader exists because of one.
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
        // The radius of the density's support — the SAME number the bounding
        // hull mesh is built at (BuildEnvironmentRooms: HULL * (W0 + WK*Len)).
        // It is the integration interval, so it may never be smaller than the
        // hull: the beam would then end before its own mesh does and the mesh's
        // rim would become visible, which is the one thing this may not do.
        _HullR ("Support radius (m) — must equal the hull's own radius", Float) = 1.2
        _Steps ("Integration samples", Range(6,48)) = 24
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
        Cull Front          // The hull is wound OUTWARD, so this draws its FAR
                            // side = the view ray's exit point. That surface
                            // exists from outside the volume and from inside it
                            // alike, and it is the same surface in both cases,
                            // which is what makes walking into the beam
                            // continuous. See the ModBuild 137 note above: with
                            // the inward-wound hull this same line kept the NEAR
                            // faces, and the beam vanished the instant it was
                            // entered. Never change one of the two without the
                            // other.
        Fog { Mode Off }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            fixed4 _Tint;
            float4 _BeamOrg, _BeamDir;
            float _Len, _W0, _WK, _Ramp, _Decay, _EndFade, _Knee, _HullR, _Steps;
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
                float3 R = normalize(i.opos - cam + 1e-6);
                float3 D = normalize(_BeamDir.xyz);

                // ---- camera and ray split into axial / perpendicular parts ----
                float3 w = cam - _BeamOrg.xyz;
                float wpar = dot(w, D);
                float3 wperp = w - D * wpar;
                float b = dot(R, D);
                float3 Rperp = R - D * b;
                float a = dot(Rperp, Rperp);          // sin^2(theta); 0 = along the axis

                // ---- the integration interval, analytically ----
                // (i) the support CYLINDER of radius _HullR about the axis line
                float t0 = 0.0, t1 = 1e6;
                float bq = 2.0 * dot(wperp, Rperp);
                float cq = dot(wperp, wperp) - _HullR * _HullR;
                if (a > 1e-7)
                {
                    float disc = bq * bq - 4.0 * a * cq;
                    if (disc <= 0.0) return fixed4(0, 0, 0, 1);   // the ray misses the beam
                    float sq = sqrt(disc);
                    t0 = max(t0, (-bq - sq) / (2.0 * a));
                    t1 = min(t1, (-bq + sq) / (2.0 * a));
                }
                else if (cq > 0.0) return fixed4(0, 0, 0, 1);     // parallel to the axis, outside
                // (ii) the axial SLAB 0 <= s <= _Len
                if (abs(b) > 1e-5)
                {
                    float ta = (0.0 - wpar) / b, tb = (_Len - wpar) / b;
                    t0 = max(t0, min(ta, tb));
                    t1 = min(t1, max(ta, tb));
                }
                else if (wpar < 0.0 || wpar > _Len) return fixed4(0, 0, 0, 1);
                t0 = max(t0, 0.0);                    // never integrate behind the head
                if (t1 <= t0) return fixed4(0, 0, 0, 1);

                // ---- the integral itself, midpoint rule ----
                // clamped, not trusted: a material that somehow arrives with
                // _Steps 0 would divide by zero and paint the whole hull NaN
                int N = (int)clamp(_Steps, 4.0, 64.0);
                float dt = (t1 - t0) / N;
                float invDz = 1.0 / (abs(D.z) > 1e-4 ? D.z : 1e-4);
                float acc = 0.0, sAcc = 0.0, tAcc = 0.0;
                for (int k = 0; k < N; k++)
                {
                    float tk = t0 + (k + 0.5) * dt;
                    float3 P = cam + R * tk;
                    float s = wpar + b * tk;
                    float3 del = P - _BeamOrg.xyz - D * s;
                    float wd = _W0 + _WK * s;
                    float q = dot(del, del) / (wd * wd);
                    float dens = exp(-pow(max(q, 1e-6), _RadPow) - s * _Decay)
                               * smoothstep(0.0, _Ramp, s)
                               * smoothstep(_Len, _Len - _EndFade, s);
                    // the bars' true shadow: trace this sample back along the
                    // light to the window plane and ask which gap it came through
                    float xw = P.x + (_WinZ - P.z) * invDz * D.x;
                    float ph = (xw - _BarX0) / _BarPitch;
                    float dbar = abs(frac(ph + 0.5) - 0.5) * _BarPitch;
                    float sig = _BarSig + _BarBlur * s;
                    dens *= 1.0 - _BarDepth * exp(-s / _BarFade - (dbar * dbar) / (sig * sig));
                    acc += dens; sAcc += dens * s; tAcc += dens * tk;
                }

                // slow drifting density — motes and mist crossing the beam,
                // taken at the density-weighted centroid of this ray's samples
                float inv = 1.0 / max(acc, 1e-8);
                float sm = sAcc * inv;
                float3 dm = (cam + R * (tAcc * inv)) - _BeamOrg.xyz - D * sm;
                float sh = 1.0 + _Shimmer * (sin(sm * 3.1 + t * _ShimmerSpeed * 5.3)
                                           * sin(sm * 1.3 - t * _ShimmerSpeed * 2.9
                                                 + dot(dm, D.yzx) * 2.7));

                float I = _Tint.a * acc * dt * sh / _W0;
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
