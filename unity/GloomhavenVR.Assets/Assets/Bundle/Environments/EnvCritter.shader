// GloomhavenVR — the cellar rat. A real, lit, three-dimensional animal that
// comes out of a hole in the wall, crosses the floor, and is gone again.
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
//     both ends), and it is INVISIBLE — collapsed to a point — in between.
//
// Script-free: everything above is _Time in the vertex shader. The critter node
// sits at IDENTITY under RoomGeo and its mesh is authored in rat-local metres,
// so the shader's output position is already in room space — which is the space
// the baked light rig is written in.
//
// ============================================================================
// MODBUILD 140, USER FINDING: "mach das laufen ein wenig mehr random statt immer
// den selben weg". Until now this was ONE curve, ONE duration, ONE interval: the
// rat did the identical thing every 31 s, and the second time you saw it you had
// seen all of it. It now runs a SCHEDULE instead of a loop.
//
// THE SHAPE OF THE FIX. Time is cut into fixed SLOTS of _Period seconds —
// slot = floor(t / _Period), an exact integer — and everything about the
// crossing that happens in a slot is a hash of that ONE integer: whether the
// slot is quiet at all, when in the slot the rat appears, how long it takes,
// which hole it starts from, whether it turns round half way and goes back, how
// far the two middle control points of its Bezier have wandered, and whether it
// stops to sniff. The result is a continuum of routes and intervals with no
// cycle a player could ever learn, out of nine floats of state.
//
// WHY A SLOT AND NOT AN ACCUMULATED INTERVAL. A "wait a random time, then run"
// schedule needs the SUM of every interval since the epoch, which a shader
// cannot have — it is O(n) in elapsed time and it drifts. Slot + hash is O(1),
// exact at any t, and identical whether you joined the session an hour ago or a
// second ago. The variation in interval comes from the start offset INSIDE the
// slot (a run may land early in one slot and late in the next) plus the quiet
// slots: 12 s to ~2 min between crossings out of a fixed 26 s beat.
//
// WHY THIS IS MULTIPLAYER-SAFE, which is a hard requirement and not a nicety.
// The only input is t = _Time.y + _GhvrTimeOfs, the SHARED environment clock
// (SkyAlternative.EnvClockSeconds) — by construction the same number on every
// client showing this cellar. There is no Random, no per-client state, no
// per-instance seed and no frame history. And the hash H() below is built out
// of nothing but multiply, add and frac on values under 200: every one of those
// is a correctly-rounded IEEE-754 single-precision operation on every GPU this
// mod runs on, so two clients do not merely compute a SIMILAR schedule, they
// compute the same bits. sin() is deliberately NOT in the hash — it is the one
// function whose last bits differ between vendors, and it is exactly the
// function the usual frac(sin(x)*43758.5) idiom is built on. It is still used
// for the gait, where a last-bit difference is a last-bit difference.
// The C# builder mirrors H() line for line, so the bake log's route/interval/
// speed ranges are measurements of this code and not a description of it.
// ============================================================================
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
        _Period ("Slot beat (s) — one crossing at most per slot", Float) = 26
        _RunTime ("Seconds a full-length crossing takes", Float) = 4.6
        _Phase ("Schedule phase (slots)", Float) = 0
        _Dart ("Dart-and-pause amount (mean)", Range(0,0.07)) = 0.055
        _Stride ("Strides per full crossing", Float) = 24
        _Scale ("Body scale", Float) = 1

        // ---- the schedule (see the MODBUILD 140 block in the header) --------
        // Every one of these is a RANGE the per-slot hash picks out of, and the
        // C# builder proves the whole range clears the play-space and the walls
        // before it writes them. Widening one here without re-baking is how the
        // rat ends up inside a barrel.
        _Skip ("Chance a slot stays quiet", Range(0,0.6)) = 0.15
        _Timing ("start lo, start span (of period), speed lo, speed span", Vector) = (0.05,0.55,0.72,0.62)
        _Modes ("reverse chance, turn-back chance, sniff chance, sniff amount", Vector) = (0.42,0.30,0.45,0.26)
        _Peak ("turn-back apex: lo, span (0..1 of the curve)", Vector) = (0.35,0.40,0,0)
        _Wob1 ("P1 wander: ampX, ampZ, biasX, biasZ (m)", Vector) = (0.50,0.80,-0.46,0)
        _Wob2 ("P2 wander: ampX, ampZ, biasX, biasZ (m)", Vector) = (0.12,0.32,-0.12,0.28)

        // ---- HAUNT: the stare -------------------------------------------------
        // The cheapest creepy easter egg in the whole feature, and one of the
        // nastiest: now and then the rat stops in the middle of the floor and
        // turns its head toward the room. No new geometry, no new material, no
        // new draw call — the animal, the stop and the head are all already here.
        //
        // WHY IT IS NOT ONE OF THE SIX CARDS. It cannot be: it has to happen
        // where the rat happens to be, which is the rat's schedule and not the
        // haunt's. So it rides the rat's slot instead — and to keep the standing
        // rule that two easter eggs never land on top of each other, it is
        // allowed ONLY in a haunt slot that the haunt schedule has left quiet.
        // That is a one-line gate and it makes the guarantee total rather than
        // statistical.
        //
        // 0 (the default) is off, and so is _GhvrHaunt.x = 0 — the master switch
        // turns this off with everything else, which is the requirement
        // ("Es soll deaktivierbar sein").
        _Stare ("Chance a crossing stops and looks at the room", Range(0,1)) = 0
        _HauntPeriod ("Haunt slot beat (s)", Float) = 83
        _HauntCards ("Haunt event count in this room", Float) = 6

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
        // RAT SOLID — opaque, depth-written, single-sided, and that is correct
        // ONLY because the mesh is a closed solid whose triangles are wound to
        // agree with their normals. It was not, until ModBuild 140: see the
        // winding proof over AddTube() in BuildEnvironmentRooms.cs. Do not
        // "fix" a see-through critter by putting Cull Off here — that hides an
        // inside-out mesh behind double the fill and leaves it lit backwards.
        Cull Back
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // 3.0, not the 2.5 default: the schedule below evaluates thirteen
            // hashes and a Bezier per vertex, which is comfortably past the
            // 256-instruction vertex budget of shader model 2.
            #pragma target 3.0
            #include "UnityCG.cginc"
            // HAUNT — for the stare's mutual-exclusion gate only. The rat's own
            // schedule stays in this file; the include is read for _GhvrHaunt (the
            // master switch) and GhvrHauntAt (is this haunt slot quiet?).
            #include "EnvHaunt.cginc"

            fixed4 _Tint, _BellyTint, _AmbUp, _AmbDown, _DirCol, _L0Col, _L1Col, _L2Col, _ShaftCol;
            float4 _W0, _W1, _W2, _W3, _DirDir, _L0Pos, _L1Pos, _L2Pos, _ShaftP, _ShaftD;
            float4 _Timing, _Modes, _Peak, _Wob1, _Wob2;
            float _Period, _RunTime, _Phase, _Dart, _Stride, _Scale, _PtHard, _ShaftR, _Skip;
            float _Stare, _HauntPeriod, _HauntCards;
            float _GhvrTimeOfs;   // preview-only clock offset (see EnvRoom.shader)

            #define GHVR_PI   3.14159265
            #define GHVR_2PI  6.28318531

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

            // The route of ONE crossing: the authored curve with its two middle
            // control points displaced by d1/d2. The ends are never displaced —
            // they are the two holes in the wall, which are real geometry.
            float3 Bez (float u, float3 d1, float3 d2)
            {
                float k = 1.0 - u;
                return k*k*k*_W0.xyz + 3.0*k*k*u*(_W1.xyz+d1) + 3.0*k*u*u*(_W2.xyz+d2) + u*u*u*_W3.xyz;
            }
            float3 BezD (float u, float3 d1, float3 d2)
            {
                float k = 1.0 - u;
                return 3.0*k*k*(_W1.xyz+d1-_W0.xyz) + 6.0*k*u*(_W2.xyz+d2-_W1.xyz-d1)
                     + 3.0*u*u*(_W3.xyz-_W2.xyz-d2);
            }

            // The schedule's only source of variety. n is the slot index (an
            // exact non-negative integer), k selects one of thirteen decorrelated
            // channels. Multiply/add/frac only — see the header for why sin() is
            // banned here and why that makes two clients bit-identical.
            //
            // The +1.0 is not cosmetic: without it channel 0 of slot 0 starts at
            // frac(0) = 0, and 0 is this cascade's one fixed point.
            // Measured over 4000 slots x 13 channels (float32): mean 0.5001,
            // |autocorrelation| <= 0.039, |cross-channel| <= 0.035, octile spread
            // 4.4% — i.e. no drift a player could feel as a pattern.
            float H (float n, float k)
            {
                float x = frac((n + 1.0 + k * 7.13) * 0.7548776662);
                x = frac(x * (x + 31.70));
                x = frac(x * (x + 17.31));
                return frac(x * (x + 43.19));
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
                // ---------------------------------------------- WHICH crossing
                float t = _Time.y + _GhvrTimeOfs;
                float per = max(_Period, 1.0);
                float sl = floor(t / per + _Phase);    // slot index, exact integer
                float sIn = t - (sl - _Phase) * per;   // seconds into this slot

                // one hash per decision. Channel numbers are load-bearing: the
                // C# builder reads the SAME channels to prove the ranges.
                float hSkip = H(sl, 0), hStart = H(sl, 1), hSpeed  = H(sl,  2);
                float hRev  = H(sl, 3), hTurn  = H(sl, 4), hPeak   = H(sl,  5);
                float hPaus = H(sl, 6), hPsAt  = H(sl, 11), hDart  = H(sl, 12);

                // is anybody home this slot, and where has the route wandered to
                float live = step(_Skip, hSkip);
                float3 d1 = float3(_Wob1.z + _Wob1.x * (2.0*H(sl, 7) - 1.0), 0,
                                   _Wob1.w + _Wob1.y * (2.0*H(sl, 8) - 1.0));
                float3 d2 = float3(_Wob2.z + _Wob2.x * (2.0*H(sl, 9) - 1.0), 0,
                                   _Wob2.w + _Wob2.y * (2.0*H(sl,10) - 1.0));

                // TURN-BACK: a rat that thinks better of it, runs out to `peak`
                // and goes back into the hole it came from. `trv` is the DISTANCE
                // covered (always increasing) as opposed to `shape`, the position
                // along the curve — the legs must keep walking forwards while the
                // curve parameter runs backwards, or the animal moonwalks home.
                float turn = step(hTurn, _Modes.y);
                float peak = lerp(1.0, _Peak.x + _Peak.y * hPeak, turn);
                float rev  = step(hRev, _Modes.x);      // 1 = out of the far hole

                // ------------------------------------------------- WHEN, and how fast
                // A turn-back covers 2*peak of the curve, so its duration scales
                // with that: every crossing runs at a plausible rat speed rather
                // than every crossing taking the same wall-clock time.
                float travelTot = lerp(1.0, 2.0 * peak, turn);
                float runT = max(_RunTime * travelTot * (_Timing.z + _Timing.w * hSpeed), 0.2);
                float start = per * (_Timing.x + _Timing.y * hStart);
                float q = (sIn - start) / runT;         // run progress, 0..1

                // in the hole before, in a hole after, and identically zero for
                // the whole of the wait (both smoothsteps clamp outside 0..1)
                float vis = live * smoothstep(0.0, 0.07, q) * smoothstep(1.0, 0.90, q);
                float qc = saturate(q);

                // dart and pause. sin(4*pi*q) instead of the old sin(13*q): it
                // vanishes at BOTH ends, so the warp cannot push the rat past its
                // hole and no saturate() is needed to catch it. The amplitude is
                // bounded by 1/(4*pi) so dqe/dq stays positive — a rat that
                // reversed for a frame would be a bug you cannot unsee.
                float dart = _Dart * (0.40 + 0.80 * hDart);
                float qe = qc + dart * sin(qc * 2.0 * GHVR_2PI);

                // STOP AND SNIFF, on the straight crossings only (a turn-back
                // already stops, at its apex). Time is removed from the middle of
                // the run by a smoothstep and given back by the renormalisation,
                // so the rat slows to about a fifth of its speed and picks up
                // again. PW is what makes that monotone: the steepest smoothstep
                // slope is 1.5/(2*PW) = 3.13, and _Modes.w <= 0.26 keeps
                // 1 - amt*3.13 above zero.
                float PW = 0.24;
                float pAt = 0.26 + 0.46 * hPsAt;
                float pAmt = _Modes.w * saturate((_Modes.z - hPaus) / max(_Modes.z, 1e-3)) * (1.0 - turn);

                // ---- HAUNT: THE STARE (see the _Stare property block) ----------
                // Channel 13, a channel no other decision uses. The gate has three
                // factors and every one of them is a requirement rather than a
                // taste: the haunt master (the feature must be switchable off),
                // this slot's own roll against _Stare (it must be rare), and a
                // QUIET haunt slot (two easter eggs must never land together).
                // Straight crossings only — a turn-back already stops at its apex,
                // and an animal that stopped twice would read as a stutter.
                float hStare = H(sl, 13);
                GhvrHaunt hh = GhvrHauntAt(t, _HauntPeriod, _HauntCards);
                float stareOn = step(hStare, _Stare) * (1.0 - hh.live)
                              * step(0.0001, _GhvrHaunt.x) * (1.0 - turn);
                // Force the stop to full depth: a stare during a half-hearted
                // pause would be a head turning while the legs kept walking.
                pAmt = max(pAmt, stareOn * _Modes.w);
                float g = qe - pAmt * smoothstep(pAt - PW, pAt + PW, qe);
                float p = g / max(1.0 - pAmt, 0.2);

                // ------------------------------------------------- WHERE on the curve
                float sh    = sin(GHVR_PI * p);
                float shape = lerp(p, sh, turn);
                float trv   = lerp(p, (p < 0.5 ? sh : 2.0 - sh), turn);
                float u     = rev + (1.0 - 2.0 * rev) * peak * shape;

                float3 P = Bez(u, d1, d2);
                float3 T = BezD(u, d1, d2);
                T.y = 0.0;
                // Heading as an ANGLE, not as a signed tangent: a reversed run
                // and the second half of a turn-back both travel down the curve,
                // and blending the pivot over the apex is what stops the animal
                // from snapping through 180 degrees in one frame. (rev + the
                // pivot = 2*pi for a reversed turn-back, which is the same
                // heading — and correctly so, it has turned round twice.)
                float2 t2 = normalize(T.xz + float2(1e-5, 0));
                float ang = atan2(t2.x, t2.y)
                          + GHVR_PI * (rev + turn * smoothstep(0.42, 0.58, p));
                float3 fwd = float3(sin(ang), 0, cos(ang));
                float3 up = float3(0, 1, 0);
                float3 right = normalize(cross(up, fwd));

                float stride = trv * peak * _Stride * GHVR_2PI;
                float tailW = v.color.r, legW = v.color.g, legPh = v.color.b;
                // How much of the animal is "head": 0 at the shoulders, 1 at the
                // muzzle. Read off the authored z rather than a fifth vertex
                // channel — the mesh has exactly one axis of symmetry and the
                // nose is the far end of it.
                float headW = saturate((v.vertex.z - 0.085) / 0.095);

                float3 lp = v.vertex.xyz * _Scale;
                // spine wave: the body snakes, strongest toward the hips
                lp.x += 0.016 * sin(stride * 0.5 + lp.z * 6.0) * (1.0 - saturate(lp.z * 4.0));
                // tail trails behind the turn and whips at half stride rate
                lp.x += 0.055 * sin(stride * 0.5 - 1.9) * tailW;
                lp.y += 0.020 * sin(stride * 0.5 + 0.6) * tailW * tailW;
                // legs: two alternating pairs, reaching forward and pushing back
                lp.z += 0.017 * sin(stride + legPh * GHVR_2PI) * legW;
                lp.y += 0.011 * max(0.0, sin(stride + legPh * GHVR_2PI + 1.57)) * legW;

                // THE SNIFF. Wherever the animal has stopped — the middle of a
                // hesitating crossing, or the apex of a turn-back — the nose
                // comes up and casts about. The legs have already stopped on
                // their own: `stride` is driven by distance covered, not by time.
                float sniffP = saturate(1.0 - abs(qe - pAt) / PW)
                             * saturate(pAmt / max(_Modes.w, 1e-3));
                float sniffT = turn * smoothstep(0.28, 0.50, p) * smoothstep(0.72, 0.50, p);
                float sniffS = sniffP * sniffP * (3.0 - 2.0 * sniffP);
                float sniff = max(sniffS, sniffT);
                // THE STARE rides the same stop the sniff does, so the animal's
                // legs, body and timing need no special case at all — only the
                // head behaves differently.
                float stare = stareOn * sniffS;
                lp.y += 0.034 * headW * sniff * (1.0 - 0.60 * stare);
                // ...and while it stares, the nose stops casting about. A head
                // that turned to look at you and then went on sniffing would be
                // an animal; a head that turns and holds is not.
                lp.x += 0.020 * headW * sniff * sin(t * 5.7 + hPsAt * GHVR_2PI) * (1.0 - stare);

                // The yaw itself: toward the ROOM CENTRE, which is world-fixed and
                // is where the board and the players are — NOT toward the camera.
                // A head that tracked the head would be the billboard behaviour the
                // project has permanently ruled out, it would differ between the
                // two eyes, and in multiplayer it would point at a different place
                // on every client. Clamped to +-1.25 rad because a rat's neck is a
                // rat's neck, and because an unclamped turn would snap through 180
                // degrees whenever the animal happens to be running away.
                float2 toC = normalize(-P.xz + float2(1e-5, 0));
                float dA = atan2(toC.x, toC.y) - ang;
                dA = dA - GHVR_2PI * floor(dA / GHVR_2PI + 0.5);   // wrap to [-pi, pi]
                float ya = clamp(dA, -1.25, 1.25) * stare * headW;
                float2 rel = float2(lp.x, lp.z - 0.090);           // pivot at the shoulders
                float cs = cos(ya), sn = sin(ya);
                lp.x = rel.x * cs - rel.y * sn;
                lp.z = 0.090 + rel.x * sn + rel.y * cs;

                float3 wp = P + right * lp.x + up * lp.y + fwd * lp.z;
                // body bob at stride frequency — a scurrying rat is never level
                wp.y += 0.009 * abs(sin(stride * 0.5));
                // vanish into the hole: shrink onto the path point, not onto the
                // world origin, so it never streaks across the room
                wp = lerp(P, wp, vis);

                // The head's normals turn with the head. Skipping this would leave
                // the one part of the animal the player is looking at during a
                // stare lit as if it were still facing down the corridor — the
                // same class of silent default as the forest's unset _RimDir.
                float3 n = v.normal;
                n = float3(n.x * cs - n.z * sn, n.y, n.x * sn + n.z * cs);
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
