// GloomhavenVR — the cellar's ceiling drip: one drop that swells on a ceiling
// plank, falls under real gravity, and bursts into a handful of splash droplets
// on the puddle below.
//
// USER RULING, ModBuild 134: "zB tropft Wasser von irgendwo runter in eine
// pütze". Why this is a SHADER and not a particle system, even though Shuriken
// was the obvious tool: the puddle's rings (EnvPuddle) are driven by _Time, and
// a Shuriken emitter's clock starts when the prefab is INSTANTIATED. The two
// would drift apart within a minute and the rings would ring for drops that had
// not landed yet. Sharing _Time is the only way a script-free drip and a
// script-free ripple can be the same event. EnvPuddle._Period/_Phase MUST equal
// this material's, and EnvPuddle._Impact must equal _Hang + the fall time
// (EnvRoomBuilder computes all of it from one set of constants).
//
// Geometry: one cross-quad for the drop and five for the splash droplets, all
// world-anchored (never camera-facing — the permanent VR rule); the sprite is
// radially symmetric, so no roll can ever show. Additive, no depth write.
//
// Mesh contract (EnvRoomBuilder.DripMesh):
//   POSITION  corner offset of the element's quad, in metres, around its origin
//   COLOR.r   0 = the falling drop, 1 = a splash droplet
//   COLOR.g   splash azimuth (0..1 of a full turn)
//   COLOR.b   splash speed factor (0..1)
//   COLOR.a   1 for the vertical blade of a cross-quad, 0 for the other, so the
//             drop can be stretched along its fall without shearing the pair
Shader "GloomhavenVR/EnvDrip"
{
    Properties
    {
        _MainTex ("Droplet sprite (radially symmetric)", 2D) = "white" {}
        _Tint ("Droplet colour (a = strength)", Color) = (0.62,0.74,1.0,0.9)
        _Period ("Seconds between drips", Float) = 2.7
        _Phase ("Phase (s)", Float) = 0
        _Hang ("Seconds the drop clings to the plank", Float) = 1.5
        _Y0 ("Ceiling height (object space)", Float) = 3.26
        _Y1 ("Water surface height (object space)", Float) = 0.005
        _SplashLife ("Splash droplet lifetime (s)", Float) = 0.42
        _SplashOut ("Splash outward speed (m/s)", Float) = 0.55
        _SplashUp ("Splash upward speed (m/s)", Float) = 1.15
        _Stretch ("Motion stretch per m/s", Float) = 0.055
        // ELEMENT ART — AIR. The draught's direction (EnvRoomBuilder.DraftDir,
        // OBJECT space) and how hard it pushes, in m/s^2, at full Air. A falling
        // drop is the most honest wind gauge a cellar has: it is the one thing in
        // the room whose UNDISTURBED path the player already knows is straight
        // down, so any lean at all is unmistakably the air doing it. The drip
        // hangs 0.9 m off the draught's own line (window -> stair door), which is
        // why it leans at all and why it does not blow away.
        _DraftDir ("Draught direction (OBJECT space)", Vector) = (0,0,0,0)
        _DraftPush ("Draught acceleration at full Air (m/s^2)", Float) = 0
    }
    SubShader
    {
        Tags { "Queue"="Transparent+12" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        Fog { Mode Off }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "EnvElement.cginc"

            sampler2D _MainTex; float4 _MainTex_ST;
            fixed4 _Tint;
            float _Period, _Phase, _Hang, _Y0, _Y1;
            float _SplashLife, _SplashOut, _SplashUp, _Stretch;
            float4 _DraftDir; float _DraftPush;
            float _GhvrTimeOfs;   // preview-only clock offset (see EnvRoom.shader)

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; fixed4 color : COLOR; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; fixed alive : TEXCOORD1; };

            v2f vert (appdata v)
            {
                float t = _Time.y + _GhvrTimeOfs;
                float s = frac((t + _Phase) / max(_Period, 0.1)) * max(_Period, 0.1);

                float3 off = v.vertex.xyz;
                float3 base = float3(0, _Y0, 0);
                float alive = 0;

                // ELEMENT ART: the draught, as an ACCELERATION rather than an
                // offset — wind pushes a drop for as long as it is falling, so
                // the path bends instead of shifting sideways, which is the
                // difference between "blown" and "misaligned". Exactly zero in
                // the zero state (e.air is 0 and the whole block is skipped).
                GhvrElem eAir = GhvrElems();
                float push = 0.0;
                if (eAir.live > 0.0) push = _DraftPush * eAir.air;

                if (v.color.r < 0.5)
                {
                    // ---- the drop ----
                    if (s < _Hang)
                    {
                        // clinging: it swells on the plank, then lets go
                        float g = saturate(s / max(_Hang, 0.05));
                        base.y = _Y0;
                        off *= 0.30 + 0.70 * g * g;
                        alive = smoothstep(0.0, 0.22, g);
                        // a hanging drop in a draught is dragged off plumb and
                        // trembles: the bigger it grows the more the air has to
                        // hold on to, hence the g*g weighting.
                        base.xz += _DraftDir.xz * (push * 0.020 * g * g
                                                   * (1.0 + 0.35 * sin(t * 5.3)));
                    }
                    else
                    {
                        float tf = s - _Hang;
                        float y = _Y0 - 4.905 * tf * tf;
                        base.y = y;
                        base.xz += _DraftDir.xz * (0.5 * push * tf * tf);
                        // a falling drop is a streak, not a bead: stretch the
                        // vertical blade with speed (COLOR.a marks it)
                        off.y *= 1.0 + _Stretch * (9.81 * tf) * v.color.a;
                        alive = step(_Y1, y);
                    }
                }
                else
                {
                    // ---- a splash droplet ----
                    float tf = s - _Hang - sqrt(max(2.0 * (_Y0 - _Y1) / 9.81, 1e-5));
                    float sp = 0.45 + 0.55 * v.color.b;
                    float a = v.color.g * 6.2831853;
                    // ELEMENT ART: under Ice the splash dies. The drop still
                    // falls and the puddle still takes it, but the water it lands
                    // in has glazed over (EnvPuddle's Ice term) and a drop on ice
                    // does not throw a crown. Nothing else in this shader changes:
                    // the CLOCK is shared with the puddle's rings, so slowing or
                    // stopping the fall here would break the one event those two
                    // shaders exist to tell together.
                    GhvrElem e = GhvrElems();
                    float vy = _SplashUp * sp;
                    float y = _Y1 + vy * tf - 4.905 * tf * tf;
                    base = float3(cos(a) * _SplashOut * sp * tf, y, sin(a) * _SplashOut * sp * tf);
                    off *= (0.55 + 0.45 * v.color.b) * max(1.0 - 0.75 * e.ice, 0.0);
                    alive = step(0.0, tf) * step(tf, _SplashLife) * step(_Y1 - 0.004, y);
                }

                v2f o;
                o.pos = UnityObjectToClipPos(float4(base + off * alive, 1.0));
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.alive = alive;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 c = tex2D(_MainTex, i.uv) * _Tint;
                // ELEMENT ART: the water goes ice-blue and picks up the split's
                // source gain — it is a highlight, and highlights are sources.
                GhvrElem e = GhvrElems();
                c.rgb = lerp(c.rgb, float3(0.80, 0.92, 1.10), saturate(e.ice * 0.7)) * GhvrSrcGain(e);
                c.a *= i.alive;
                c.rgb *= c.a;      // premodulate: alpha drives the additive energy
                return c;
            }
            ENDCG
        }
    }
    Fallback Off
}
