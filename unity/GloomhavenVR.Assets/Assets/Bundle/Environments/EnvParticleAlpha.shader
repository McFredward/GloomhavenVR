// GloomhavenVR — alpha-blended unlit particle shader (ground fog, dust). Bundled
// (builtin particle shaders may be stripped from the game player). Fog puffs are big
// WORLD-SPACE billboards: they stand in the world and do not follow the head — the
// user's hard VR rule. Texture alpha × per-particle vertex color.
//
// ELEMENT ART (EnvElement.cginc / EnvParticleElem.cginc — the mechanism is
// shared with EnvParticleAdd and documented there). What this shader carries
// that the additive one cannot:
//   THE GROUND FOG UNDER DARK. Its weights are signed — Dark thickens it and
//   Light thins it, in ONE dot product — and its brightness response is
//   NEGATIVE. That combination is the whole point: an alpha-blended lit puff
//   over black IS a raised floor (the ModBuild 134 ruling that darkened both
//   mist layers), so Dark's fog must get DENSER AND DARKER. It swallows the far
//   trunks instead of veiling them in grey. Under Light it thins away to
//   nothing and the sources stand alone — the split, in the air.
Shader "GloomhavenVR/EnvParticleAlpha"
{
    Properties
    {
        _MainTex ("Sprite", 2D) = "white" {}
        _Tint ("Tint", Color) = (1,1,1,1)

        // ---- ELEMENT ART (see EnvParticleAdd.shader for what each one is) ----
        _ElemOwn ("Element gate (fire,ice,air,earth)", Vector) = (0,0,0,0)
        _ElemOwn2 ("Element gate (light,dark,-,-)", Vector) = (0,0,0,0)
        _ElemMod ("Element modulation (fire,ice,air,earth)", Vector) = (0,0,0,0)
        _ElemMod2 ("Element modulation (light,dark,-,-)", Vector) = (0,0,0,0)
        _ElemGain ("Element: brightness response", Float) = 0
        _ElemAlpha ("Element: alpha response", Float) = 0
        _ElemCol ("Element: colour it moves toward", Color) = (1,1,1,1)
        _ElemTintAmt ("Element: how far it moves", Range(0,2)) = 0
        _ElemSpark ("Element: fast twinkle amount", Range(0,2)) = 0
        // 1 = this emitter IS moonlight and dies with the moon under the eclipse.
        // Default 0 keeps every unwritten material bit-identical. See EnvParticleElem.cginc.
        _ElemMoon ("Element: emitter is moonlight", Range(0,1)) = 0

        // ==================== THE BEAM MASK — dust that only exists in the light
        //
        // USER VERDICT, ModBuild 147 (verbatim): "Die Luftströme im Keller sind
        // zu cartoonig zu grob - diese weißen Linien gefallen mir so nicht."
        //
        // The cellar's two free-air draught emitters were deleted for that (see
        // BuildEnvironmentRooms, the AIR block), and what replaced them is a
        // SMALL population of round motes living inside the moonbeam. The
        // invariant behind BOTH of that effect's rejections — "Pünktchen ...
        // weiße Funken" in 143 and "weiße Linien" in 147 — is not the sprite. It
        // is BRIGHT MATTER IN UNLIT AIR. Air is invisible; what a shaft of light
        // shows is the Tyndall effect, dust SCATTERING that light, and the
        // particles "appear as points of light against a dark background" only
        // where the light actually is. No light on the dust, no dust.
        //
        // WHY THIS IS A SHADER TERM AND NOT JUST A TIGHT EMITTER SHAPE. Emitting
        // inside the shaft is necessary and not sufficient: a mote drifts, and a
        // mote that drifts out of a shaped emitter's volume goes on being drawn
        // at full brightness in black air until its lifetime runs out — i.e. the
        // effect leaks exactly the thing that was rejected, at the edges, where
        // the eye is best at seeing it. Fading it at an emitter boundary instead
        // would pop, because the boundary is not where the light stops.
        //
        // So the confinement is made PHYSICALLY TRUE: this mask is EnvBeam's own
        // density envelope, evaluated at the particle's vertex, with EnvBeam's
        // OWN uniforms and the same names —
        //     s      = axial distance from the aperture along _BeamDir
        //     w(s)   = _W0 + _WK*s              the gentle widening
        //     mask   = exp(-(dperp/w)^2 - s*_Decay)
        //              * smoothstep(0, _Ramp, s) * smoothstep(_Len, _Len-_EndFade, s)
        // A mote therefore fades out EXACTLY where the light it is supposed to be
        // scattering fades out, and the two lit things in the room cannot
        // disagree, because they are reading the same seven numbers.
        //
        // COST: about a dozen ALU on four vertices per particle — free — and it
        // is in the VERTEX shader, so it never touches the fill rate that made
        // the old streaks expensive.
        //
        // DEFAULT OFF, and that matters more than it looks: this shader also
        // draws the ground fog, the dust motes, the grit and (historically) the
        // snow. `_BeamMask` 0 takes a uniform branch that no lane of any wave in
        // those emitters enters, so every other EnvParticleAlpha material is
        // bit-identical to what shipped — including the degenerate case of an
        // unset _W0, which would otherwise divide by zero.
        _BeamMask ("Beam mask amount (0 = off)", Range(0,1)) = 0
        _BeamOrg ("Beam axis origin (OBJECT space)", Vector) = (0,0,0,0)
        _BeamDir ("Beam axis direction of travel (OBJECT space)", Vector) = (0,-1,0,0)
        _Len ("Beam axis length (m)", Float) = 4
        _W0 ("Beam half-width at the aperture (m)", Float) = 0.30
        _WK ("Beam widening per metre", Float) = 0.10
        _Decay ("Beam density decay per metre", Float) = 0.85
        _Ramp ("Beam ramp out of the aperture (m)", Float) = 0.30
        _EndFade ("Beam taper before the floor (m)", Float) = 0.45
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend SrcAlpha OneMinusSrcAlpha
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
            #include "EnvParticleElem.cginc"

            sampler2D _MainTex; float4 _MainTex_ST;
            fixed4 _Tint;

            // THE BEAM MASK. Same names, same semantics and same values as
            // EnvBeam.shader's — see the property block above for why they are
            // shared rather than restated.
            float _BeamMask;
            float4 _BeamOrg, _BeamDir;
            float _Len, _W0, _WK, _Decay, _Ramp, _EndFade;

            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };

            /// EnvBeam's density envelope, at one point, in the same OBJECT space
            /// the beam's own constants are authored in. Never called unless
            /// _BeamMask > 0, so an unset _W0 cannot divide by zero.
            float GhvrBeamMask (float3 P)
            {
                float3 D = normalize(_BeamDir.xyz + float3(0, -1e-6, 0));
                float3 rel = P - _BeamOrg.xyz;
                float s = dot(rel, D);
                float3 perp = rel - D * s;
                // the widening is only defined forward of the aperture; behind it
                // the ramp below is zero anyway, and clamping keeps w positive so
                // the gaussian can never blow up
                float sc = max(s, 0.0);
                float w = max(_W0 + _WK * sc, 1e-4);
                float q = dot(perp, perp) / (w * w);
                // radial x decay x the two end tapers = EnvBeam's `radial * along`
                // with _RadPow pinned at 1. A plain gaussian is very slightly
                // WIDER than the shipped super-gaussian at 1.35, which is the safe
                // direction to be wrong in: a mote fades a hair later than the
                // light around it rather than a hair earlier.
                return exp(-q - sc * _Decay)
                     * smoothstep(0.0, max(_Ramp, 1e-4), s)
                     * smoothstep(_Len, _Len - max(_EndFade, 1e-4), s);
            }

            v2f vert (appdata v)
            {
                v2f o;
                float4 col = v.color * _Tint;
                // BEFORE the element gate, so a masked-out mote is masked whatever
                // the elements are doing. Uniform branch: every emitter that is not
                // in a beam takes the other side and is bit-identical.
                if (_BeamMask > 0.0)
                    col.a *= lerp(1.0, GhvrBeamMask(v.vertex.xyz), _BeamMask);
                bool alive = GhvrParticleElem(v.vertex, col);   // gate + modulation
                // collapsed to a point when this emitter's element is down — see
                // EnvParticleAdd for why a zero alpha would not be good enough
                o.pos = alive ? UnityObjectToClipPos(v.vertex) : float4(0, 0, 0, 1);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = col;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 t = tex2D(_MainTex, i.uv);
                return fixed4(t.rgb * i.color.rgb, t.a * i.color.a);
            }
            ENDCG
        }
    }
    Fallback Off
}
