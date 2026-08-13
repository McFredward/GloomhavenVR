// GloomhavenVR — the cellar rat. A real, lit, three-dimensional animal that
// runs a fixed route across the floor every _Period seconds and is gone again.
//
// USER RULING, ModBuild 134: "eine Ratte huscht durch den Raum". The brief also
// says it must NOT look like a sliding sprite — so nothing here billboards and
// nothing here merely translates:
//   * the body is REAL geometry (EnvRoomBuilder.RatMesh) with a normal per
//     vertex, lit by the same baked rig as the room, so it darkens as it leaves
//     a candle pool and brightens as it enters the next one;
//   * it walks a cubic BEZIER (_W0.._W3, room coordinates), and its heading is
//     the curve's own tangent, so it banks into the corner it runs around;
//   * the gait is in the vertices: a dart-and-pause speed profile, a body bob at
//     stride frequency, a lateral spine wave, four legs on two alternating
//     phases, and a tail that trails and whips (weights authored in the mesh's
//     vertex colours: r = tail, g = leg, b = leg phase);
//   * it emerges from one hole and vanishes into another (a smooth shrink at
//     both ends), and it is INVISIBLE — scale 0 — for the rest of the period.
//
// Script-free: everything above is _Time in the vertex shader. The critter node
// sits at IDENTITY under RoomGeo and its mesh is authored in rat-local metres,
// so the shader's output position is already in room space — which is the space
// the baked light rig is written in.
Shader "GloomhavenVR/EnvCritter"
{
    Properties
    {
        _Tint ("Fur colour", Color) = (0.20,0.17,0.15,1)
        _BellyTint ("Belly colour", Color) = (0.30,0.26,0.24,1)

        _W0 ("Path P0 (room space)", Vector) = (0,0,0,0)
        _W1 ("Path P1", Vector) = (0,0,1,0)
        _W2 ("Path P2", Vector) = (1,0,1,0)
        _W3 ("Path P3", Vector) = (1,0,0,0)
        _Period ("Seconds between runs", Float) = 31
        _RunTime ("Seconds the run lasts", Float) = 4.2
        _Phase ("Cycle phase (0..1)", Float) = 0
        _Dart ("Dart-and-pause amount", Range(0,0.08)) = 0.06
        _Stride ("Strides per run", Float) = 26
        _Scale ("Body scale", Float) = 1

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
        _PtHard ("Point falloff hardness", Range(0,64)) = 0

        // THE MOONBEAM. Nothing else in these rooms is lit by the shafts —
        // they are additive geometry, and the pools they make on the floor are
        // painted separately. That is fine for a wall, and completely wrong for
        // the one thing whose whole point is that it CROSSES the beam: a rat
        // that stays a black smudge while it walks through moonlight reads as a
        // bug. So the critter alone gets the beam as a real light: a cold line
        // source through _ShaftP along _ShaftD, falling off over _ShaftR.
        _ShaftP ("Beam point (OBJECT space)", Vector) = (0,0,0,0)
        _ShaftD ("Beam direction (OBJECT space, travel)", Vector) = (0,-1,0,0)
        _ShaftR ("Beam radius (m)", Float) = 0.45
        _ShaftCol ("Beam colour", Color) = (0,0,0,1)
    }

    SubShader
    {
        Tags { "Queue"="Geometry+5" "RenderType"="Opaque" }
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            fixed4 _Tint, _BellyTint, _AmbUp, _AmbDown, _DirCol, _L0Col, _L1Col, _L2Col, _ShaftCol;
            float4 _W0, _W1, _W2, _W3, _DirDir, _L0Pos, _L1Pos, _L2Pos, _ShaftP, _ShaftD;
            float _Period, _RunTime, _Phase, _Dart, _Stride, _Scale, _PtHard, _ShaftR;
            float _GhvrTimeOfs;   // preview-only clock offset (see EnvRoom.shader)

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv     : TEXCOORD0;   // x = belly..back blend
                fixed4 color  : COLOR;       // r tail weight, g leg weight, b leg phase
            };
            struct v2f
            {
                float4 pos  : SV_POSITION;
                float3 opos : TEXCOORD0;     // ROOM space (see header)
                float3 n    : TEXCOORD1;
                float2 uv   : TEXCOORD2;
            };

            float3 Bez (float u)
            {
                float k = 1.0 - u;
                return k*k*k*_W0.xyz + 3.0*k*k*u*_W1.xyz + 3.0*k*u*u*_W2.xyz + u*u*u*_W3.xyz;
            }
            float3 BezD (float u)
            {
                float k = 1.0 - u;
                return 3.0*k*k*(_W1.xyz-_W0.xyz) + 6.0*k*u*(_W2.xyz-_W1.xyz) + 3.0*u*u*(_W3.xyz-_W2.xyz);
            }

            float Flicker (float amt, float phase, float rate)
            {
                float t = (_Time.y + _GhvrTimeOfs) * rate;
                float f = 0.42 * sin(t * 11.3 + phase)
                        + 0.33 * sin(t *  6.1 + 1.7 + phase * 1.3)
                        + 0.25 * sin(t * 19.7 + 4.2 + phase * 0.7);
                f = f * 0.70 + 0.30 * sin(t * 1.9 + phase * 0.5);
                return 1.0 + amt * 0.35 * f;
            }

            float3 PointLight (float4 lpos, fixed4 lcol, float3 opos, float3 N, float phase, float rate)
            {
                float3 lv = lpos.xyz - opos;
                float d2 = max(dot(lv, lv), 1e-8);
                float d = sqrt(d2);
                float q = d2 * lpos.w * lpos.w;
                float x = saturate(1.0 - q);
                float atten = x * x / (1.0 + _PtHard * q);
                float ndl = saturate(dot(N, lv / d));
                return lcol.rgb * (atten * ndl * Flicker(lcol.a, phase, rate));
            }

            v2f vert (appdata v)
            {
                float t = _Time.y + _GhvrTimeOfs;
                float s = frac(t / max(_Period, 0.1) + _Phase) * _Period;   // seconds into the cycle
                float u = saturate(s / max(_RunTime, 0.1));

                // in the hole before the run, in the next hole after it — and a
                // hard 0 for the whole waiting stretch, because u pins at 1
                float vis = smoothstep(0.0, 0.07, u) * smoothstep(1.0, 0.90, u);

                // dart and pause. The amplitude is bounded so du/dU stays
                // positive: a rat that reversed would be a bug you cannot unsee.
                float ue = saturate(u + _Dart * sin(u * 13.0));

                float3 P = Bez(ue);
                float3 T = BezD(ue);
                T.y = 0.0;
                float3 fwd = normalize(T + float3(1e-5, 0, 0));
                float3 up = float3(0, 1, 0);
                float3 right = normalize(cross(up, fwd));

                float stride = ue * _Stride * 6.2831853;
                float tailW = v.color.r, legW = v.color.g, legPh = v.color.b;

                float3 lp = v.vertex.xyz * _Scale;
                // spine wave: the body snakes, strongest toward the hips
                lp.x += 0.016 * sin(stride * 0.5 + lp.z * 6.0) * (1.0 - saturate(lp.z * 4.0));
                // tail trails behind the turn and whips at half stride rate
                lp.x += 0.055 * sin(stride * 0.5 - 1.9) * tailW;
                lp.y += 0.020 * sin(stride * 0.5 + 0.6) * tailW * tailW;
                // legs: two alternating pairs, reaching forward and pushing back
                lp.z += 0.017 * sin(stride + legPh * 6.2831853) * legW;
                lp.y += 0.011 * max(0.0, sin(stride + legPh * 6.2831853 + 1.57)) * legW;

                float3 wp = P + right * lp.x + up * lp.y + fwd * lp.z;
                // body bob at stride frequency — a scurrying rat is never level
                wp.y += 0.009 * abs(sin(stride * 0.5));
                // vanish into the hole: shrink onto the path point, not onto the
                // world origin, so it never streaks across the room
                wp = lerp(P, wp, vis);

                float3 n = v.normal;
                float3 nw = right * n.x + up * n.y + fwd * n.z;

                v2f o;
                o.pos = UnityObjectToClipPos(float4(wp, 1.0));
                o.opos = wp;
                o.n = nw;
                o.uv = float2(v.uv.x, vis);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // vis == 0 => the whole animal has collapsed to a point; clip it
                // so no degenerate sliver can flash on the floor
                clip(i.uv.y - 0.004);

                float3 N = normalize(i.n);
                fixed4 alb = lerp(_BellyTint, _Tint, saturate(i.uv.x));
                float3 nw = normalize(mul((float3x3)unity_ObjectToWorld, N));
                float3 light = lerp(_AmbDown.rgb, _AmbUp.rgb, nw.y * 0.5 + 0.5);
                light += _DirCol.rgb * saturate(dot(N, normalize(_DirDir.xyz)));
                light += PointLight(_L0Pos, _L0Col, i.opos, N, 0.0, 1.00);
                light += PointLight(_L1Pos, _L1Col, i.opos, N, 2.1, 0.83);
                light += PointLight(_L2Pos, _L2Col, i.opos, N, 4.4, 1.19);

                // inside the moon shaft: distance to the beam's axis line
                float3 bd = normalize(_ShaftD.xyz);
                float3 rel = i.opos - _ShaftP.xyz;
                float3 perp = rel - bd * dot(rel, bd);
                float k = exp(-dot(perp, perp) / max(_ShaftR * _ShaftR, 1e-4));
                light += _ShaftCol.rgb * (k * saturate(dot(N, -bd) * 0.65 + 0.35));

                return fixed4(alb.rgb * light, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
