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
// ELEMENT ART (ModBuild 144), two terms and both of them earn their place:
//  * MOONLIGHT. The beam IS the moon, so it scales by the contract's own
//    GhvrDirGain(e) * GhvrMoonLight() — 3.22x under Light since ModBuild 146
//    (the cellar's Light gain now goes into the moon instead of into the room's
//    ambient floor; see the MOON-LIGHT HOOK block in frag), 0.0275x under the
//    held blood moon. Under Dark it stops being a light source and the room is
//    left to its candles, which is exactly what the user asked for.
//  * WIND. Under Air the density streams: fine filaments lying ALONG the
//    draught and travelling with it, evaluated per sample inside the integral.
//    See THE WIND, CARRIED BY THE ONLY LIT AIR in frag for why it has to be a
//    ridge and cannot be a speck.
//
// COST. _Steps taps of ~4 transcendentals each (+3 sines under Air, on a
// uniform branch), over the hull's screen footprint, once per eye. The hull is
// a 1.2 m-radius cylinder in one corner of one room. It is a deliberate choice
// against the alternative — a cheap closed form — because every closed form for
// this integral has a singularity somewhere, and this shader exists because of
// one.
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
        // ELEMENT ART — AIR. The direction the cellar's draught leaves the
        // window along (EnvRoomBuilder.DraftDir, OBJECT space). Under Air the
        // air in the shaft is not merely stirred harder: the stirring TRAVELS
        // along this vector, so the one lit volume in the room shows the wind
        // going the way the wind goes. Zero-length is a hard off.
        _DraftDir ("Draught direction (OBJECT space)", Vector) = (0,0,0,0)
        // bar shadows
        _WinZ ("Window plane z (OBJECT space)", Float) = 0
        _BarX0 ("First bar x at the window plane", Float) = 0
        _BarPitch ("Bar pitch (m)", Float) = 0.22
        _BarDepth ("Bar shadow depth", Range(0,1)) = 0.38
        _BarSig ("Bar shadow half-width at the aperture (m)", Float) = 0.030
        _BarBlur ("Penumbra growth per metre", Float) = 0.085
        _BarFade ("Bar shadow fade length (m)", Float) = 0.9

        // HAUNT — the thing at the window, seen from inside the room.
        //
        // One of the cellar's six easter eggs is a head and two shoulders leaning
        // into the barred opening (EnvHaunt card "Window"). What makes it
        // believable rather than a sticker on the glass is that the LIGHT ANSWERS:
        // while it is there the beam this shader draws loses _HauntDepth of its
        // strength, because something is standing between the room and the moon.
        // The player is already using that light to see by, so the room changes
        // under them and not just in front of them.
        //
        // The two shaders never talk. They both evaluate the SAME schedule from
        // the SAME shared clock (EnvHaunt.cginc), so the dim and the silhouette
        // are the same event by construction rather than by synchronisation —
        // there is nothing to drift. _HauntDepth = 0 (the default) is a hard off
        // and costs one uniform compare in the vertex shader.
        _HauntDepth ("Haunt beam occlusion (0 = off)", Range(0,1)) = 0
        _HauntPeriod ("Haunt slot beat (s)", Float) = 83
        _HauntCards ("Haunt event count in this room", Float) = 6
        _HauntWatch ("Which haunt event occludes the beam (-1 = none)", Float) = -1
        _HauntEnv ("Watched event envelope: reveal, hold, fade, (unused)", Vector) = (0,1,1,0)
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
            // EnvHaunt.cginc, which now INCLUDES EnvElement.cginc — so this one
            // line brings both the haunt schedule and the element channel, and
            // GhvrMoonLight() is callable here. (It was not, for one round: the
            // two headers each declared _GhvrElemA/B and each used the name
            // `GhvrElems`, once for a struct and once for a function, and any
            // shader wanting both failed to compile. EnvHaunt's own note records
            // the accident and the fix. This shader was the one that wanted both,
            // which is why the moon hook below sat stubbed at 1.0.)
            #include "EnvHaunt.cginc"

            fixed4 _Tint;
            float _HauntDepth, _HauntPeriod, _HauntCards, _HauntWatch;
            float4 _HauntEnv;
            float4 _BeamOrg, _BeamDir, _DraftDir;
            float _Len, _W0, _WK, _Ramp, _Decay, _EndFade, _Knee, _HullR, _Steps;
            float _Shimmer, _ShimmerSpeed;
            float _WinZ, _BarX0, _BarPitch, _BarDepth, _BarSig, _BarBlur, _BarFade;
            float _GhvrTimeOfs;   // preview-only clock offset (see EnvRoom.shader)

            float _RadPow;

            struct appdata { float4 vertex : POSITION; };
            // .haunt is the beam's dimming factor. It is CONSTANT over the whole
            // hull, so computing it per vertex and letting the interpolator carry
            // it is exact, not an approximation — and it keeps the schedule out of
            // a fragment program that already runs a 24-tap integral per pixel.
            struct v2f { float4 pos : SV_POSITION; float3 opos : TEXCOORD0; float4 spos : TEXCOORD1;
                         float haunt : TEXCOORD2; };

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
                // HAUNT: the RAW clock, deliberately — the fragment's `t` below is
                // fmod'd to SKY_PERIOD for the shimmer, and feeding a wrapped clock
                // to a slot schedule would restart the whole haunt calendar every
                // 48 minutes on a different slot boundary per client.
                float traw = _Time.y + _GhvrTimeOfs;
                o.haunt = (_HauntDepth > 1e-4)
                    ? 1.0 - _HauntDepth * GhvrHauntPresence(traw, _HauntPeriod, _HauntCards,
                                                            _HauntWatch, _HauntEnv.x, _HauntEnv.y,
                                                            _HauntEnv.z)
                    : 1.0;
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

                // ============ THE WIND, CARRIED BY THE ONLY LIT AIR ==========
                // USER VERDICT, ModBuild 143 (verbatim): "Bei der Luft finde ich
                // die Idee gut, dass es aus dem Fenster kommt, sollte aber auch
                // wirklich mehr wie Wind wirken, aktuell diese Pünktchen erinnern
                // eher an weiße Funken, das ist nicht immersiv oder realistisch."
                //
                // He is describing the PARTICLES, and those are being rebuilt in
                // the builder — but the deeper answer is that air is invisible
                // and is only ever seen through what it carries AND through
                // where light falls on it. This beam is the one lit volume in the
                // cellar, so this is where dust in a sunbeam can actually be
                // drawn: not as sprites with outlines, but as the density of the
                // air itself, streaming.
                //
                // WHY A PLANE-WAVE STRIATION AND NOT A MOTE FIELD. Every pixel
                // here is a LINE INTEGRAL, and an isotropic high-frequency field
                // averages to its mean along the ray: individual specks would
                // integrate away to a uniform grey and cost 24 taps to do it. A
                // wave whose crests are LONG RIDGES running along the draught
                // survives, because a ray crossing the beam broadside lies in one
                // ridge for its whole length. So what the beam carries is a set
                // of fine filaments lying along the wind and travelling with it
                // — which is what dust in a shaft of light actually looks like,
                // and is the exact opposite of a dot.
                //
                // COST: three sines per sample, and ONLY under Air. `air` comes
                // from a global uniform, so this is a uniform branch — every lane
                // of every wave takes the same side of it and the cost with the
                // feature inert is one scalar compare per iteration (which the
                // compiler is free to hoist, and does). Nothing here runs, and
                // nothing here can change a bit, when the room is quiet.
                GhvrElem e = GhvrHauntElems();
                float air = e.air;
                // the wind's own frame: along the draught, and the two axes
                // across it. _DraftDir is authored horizontal, so `wUp` is up.
                float3 wDir = normalize(_DraftDir.xyz + float3(0, 1e-5, 0));
                float3 wSide = normalize(cross(float3(0, 1, 0), wDir) + 1e-6);
                float3 wUp = cross(wDir, wSide);
                // 1.35 m/s, the draught's own speed. It is kept at the number the
                // two deleted streak emitters were reconciled against (their
                // motes crossed the room at about 1.4 m/s) even though those
                // emitters are gone, because it is the speed of the AIR and not
                // of any one thing in it: it is what the flames' lean, the webs'
                // billow and the puddle's travelling cat's paws are all timed
                // against, and if the air inside the beam moved at a different
                // speed from the room's the two would read as two winds. What
                // drifts through the shaft now (ElemBeamMote) moves at 0.10-0.30
                // m/s, deliberately slower — that is Brownian jitter on top of
                // the flow, not the flow, which is what dust in a sunbeam
                // actually does.
                float wPhase = t * 1.35;

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
                    if (air > 0.0)
                    {
                        // p is the sample in the wind's frame, in metres, with
                        // the DOWNWIND coordinate already carried backwards by
                        // the clock: the whole pattern translates along the
                        // draught at wPhase m/s and does not merely wobble.
                        float pa = dot(P, wDir) - wPhase;      // along the wind
                        float pv = dot(P, wUp);                // vertical
                        float pc = dot(P, wSide);              // across
                        // Fine across (17 rad/m ~ 37 cm crest spacing, i.e. a
                        // filament you can see rather than a speckle that
                        // integrates away), slow along (2.1 rad/m), and the
                        // along-term PHASE-MODULATES the across-term so the
                        // ridges snake instead of running dead straight. Straight
                        // ridges are corrugated iron; snaking ones are dust.
                        float f = sin(pv * 17.0 + sin(pa * 2.1) * 2.3)
                                * sin(pc * 11.0 - sin(pa * 1.3) * 1.7);
                        // 1.30 at full Air, up from 0.95, and the reason is that
                        // it is now the draught's ONLY loud statement rather than
                        // its main one. ModBuild 147 DELETED both of the cellar's
                        // free-air streak emitters — up to 126 pale 17-78 cm
                        // filaments crossing the room at head height, drawn just
                        // as brightly in unlit air as in here — on the user's
                        // second rejection of that family ("diese weißen Linien
                        // gefallen mir so nicht", after 143's "weiße Funken").
                        // What is left outside this volume is a couple of dozen
                        // slow motes that live INSIDE it and are masked by this
                        // shader's own envelope (EnvParticleAlpha/_BeamMask).
                        //
                        // So the shaft has to carry the wind by itself, and it can
                        // afford to: at 0.95 the density swings x0.05..x1.95 about
                        // its mean, at 1.30 it swings x0.00..x2.30 and the beam
                        // genuinely BREAKS INTO SEPARATE FILAMENTS with dark air
                        // between them instead of merely mottling. The clamp is
                        // what makes that legal — the crests brighten and the
                        // troughs bottom out AT zero rather than going negative,
                        // which would be a hole in the air.
                        //
                        // IT COSTS NOTHING. This is a constant multiply inside a
                        // loop that already runs, on a uniform branch that is
                        // already taken; not one instruction is added, and with
                        // Air down `air` is exactly 0 and the whole line is the
                        // identity.
                        //
                        // MEASURED IN THE PREVIEW (BeamSide, full Air against
                        // rest): the contrast INSIDE the shaft — std/mean over
                        // the beam's own brightest pixels — goes 0.548 -> 0.939,
                        // and the peak goes x1.93 while the mean falls to x0.78,
                        // which is precisely what "it breaks up" means rather
                        // than "it gets brighter".
                        //
                        // AND THE ONE HONEST RESERVATION, stated here so the next
                        // round does not have to rediscover it: at 1.30 the
                        // BeamSide frame shows the shaft as THREE SEPARATED
                        // PACKETS of light rather than as fine streaming
                        // filaments, because any amplitude at or above 1.0 clamps
                        // the troughs to exact zero and the gaps then open. That
                        // is a legitimate read — gusts of dust crossing a shaft
                        // look like that — but the verdict this round answers used
                        // the word "grob", so if hardware says the beam is now the
                        // coarse thing, THIS NUMBER is the knob and 0.95 is where
                        // it came from. Nothing else in the lane depends on it.
                        dens *= max(1.0 + 1.30 * air * f, 0.0);
                    }
                    acc += dens; sAcc += dens * s; tAcc += dens * tk;
                }

                // ================================================ ELEMENT ART
                // AIR, second half: the shimmer is stirred harder and faster. The
                // FIRST half — the streaming filaments the beam actually carries —
                // is up in the integral, because it has to be per sample.
                //
                // No "is anything up" branch on these two, deliberately: `air` is
                // already folded with the master, so with the feature off it is
                // exactly 0, `1.0 + 2.4*0` is exactly 1.0, and both lines are the
                // identity. That is the same argument EnvHaunt.cginc's own element
                // block makes, and it keeps the zero state bit-identical without
                // a compare.
                float shimAmt = _Shimmer * (1.0 + 2.4 * air);
                float shimSpd = _ShimmerSpeed * (1.0 + 3.2 * air);

                // MOON-LIGHT HOOK, now connected. GhvrMoonLight() is 1.0 at rest,
                // 0.05 under full Dark (the held blood moon) and 1.34 under full
                // Light; multiplied by GhvrDirGain it is the contract's own
                // call-site form and the SAME expression EnvShaft and the room
                // surfaces use, so the beam, the pool it lands in and the disc in
                // the sky are one event.
                //
                // USER VERDICT, ModBuild 143, both halves: "Bei Licht sollte auch
                // der Mondschein aus dem Fenster viel intensiver sein" and "bei
                // Dunkelheit ... den Mondschein extrem zu reduzieren, so dass der
                // Raum insgesamt deutlich dunkler wird". Full Dark is 0.0275x,
                // i.e. the beam stops being a light source and the three candles
                // are the only ones left. The room is ALLOWED to be that dark
                // now — it is what was asked for.
                //
                // ...AND FULL LIGHT IS NOW 3.22x AND NOT 2.55x. USER VERDICT,
                // ModBuild 146 (verbatim): "Der 'Hell'-Effekt im Keller gefällt
                // mir noch nicht, es soll wirklich den Mondschein heller machen
                // statt den ganzen Raum." THIS SHADER IS WHERE THAT VERDICT IS
                // MEANT TO BE FELT. Not one number in this file moved: the room
                // stopped spending Light on its own ambient floor
                // (GHVR_AMB_LIFT_IN = 0.00) and GhvrDirGain spends it here
                // instead (GHVR_DIR_LIFT_IN = 1.40).
                //
                // MEASURED, in the preview harness, by rendering each cellar view
                // twice — once with this hull drawn and once with it hidden — so
                // that the beam's own contribution is isolated exactly rather than
                // guessed at from a rectangle. At full Light, against the build
                // the user rejected:
                //     this volume        +24% to +26% (four views)
                //     the room around it -22% to -31% in the same frames
                //     the candle-lit wall (the "Web" view) back to its RESTING
                //       value to five decimals — Light no longer touches it at all
                // so the beam's share of the light in its own frame goes 0.32 ->
                // 0.52 (BeamSide), 0.18 -> 0.29 (Window), 0.09 -> 0.15 (Puddle).
                // The absolute number matters less than that ratio: this is a
                // blade of lit air in a black room, and the eye reads it against
                // what is behind it. Roughly two thirds of the change the user
                // will see is the room getting out of the way.
                //
                // The Reinhard knee (_Knee 1.10) eats a little of the extra —
                // 1.26x of gain arrives as 1.24x of pixels — which is the point of
                // the knee: standing IN the beam and looking up it under full
                // Light must not clip to a white screen.
                float moonGain = 1.0;
                if (e.live > 0.0) moonGain = GhvrDirGain(e) * GhvrMoonLight();

                // slow drifting density — motes and mist crossing the beam,
                // taken at the density-weighted centroid of this ray's samples
                float inv = 1.0 / max(acc, 1e-8);
                float sm = sAcc * inv;
                float3 dm = (cam + R * (tAcc * inv)) - _BeamOrg.xyz - D * sm;
                float sh = 1.0 + shimAmt * (sin(sm * 3.1 + t * shimSpd * 5.3)
                                           * sin(sm * 1.3 - t * shimSpd * 2.9
                                                 + dot(dm, D.yzx) * 2.7));
                if (air > 0.0)
                {
                    // ...and a second, coarser modulation that TRAVELS along the
                    // draught. Faster shimmer alone is only agitation; a pattern
                    // with a direction is a draught, and this one runs the way
                    // the room's own draught runs, out of the window and across
                    // to the stair door. The same vector the flames lean along
                    // and the motes drift along, so all three agree.
                    float pd = dot(dm + D * sm, _DraftDir.xyz);
                    sh = max(sh * (1.0 + 0.60 * air * sin(pd * 2.3 - t * 2.1)), 0.0);
                }

                // ...and the haunt's occlusion multiplies the whole integral,
                // which is the physically right place for it: something is
                // blocking the APERTURE, so every metre of the shaft behind it
                // loses the same fraction at once, rather than a shadow crawling
                // down the beam.
                float I = _Tint.a * acc * dt * sh * i.haunt * moonGain / _W0;
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
