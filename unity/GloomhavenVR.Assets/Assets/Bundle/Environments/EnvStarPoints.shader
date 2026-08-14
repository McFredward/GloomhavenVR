// GloomhavenVR — REAL star layer of the night sky (point sprites).
//
// User finding, ModBuild 132: "Der Sternenhimmel sollte auch ein animierte
// 'echter' Sternenhimmel sein, recharchier da was du findest statt einfach nur
// ein Bild."
// User finding, ModBuild 133: "... ein Mond der dahinter ist" — the moon sat
// BEHIND the stars, because this layer blends additively over the dome that
// carries it. Fixed here, not with a depth trick: a star inside the moon's disc
// (_MoonDir / _MoonCos, the very same disc EnvStars paints) collapses to a
// point and contributes nothing. Culling per star is exact — a star IS a point
// — costs three ALU, and needs no assumption about the game camera's depth
// buffer, its clear flags, or the runtime rescaling of the sky branch, all of
// which a ZWrite'd moon quad in the Background queue would depend on.
//
// The mesh (built by BuildEnvironments.BuildStarField from bsc5_stars.csv —
// Yale Bright Star Catalogue, public domain) is one camera-independent quad per
// star, carrying:
//   POSITION  star direction * dome radius, at hour angle H0 = -RA
//   TEXCOORD0 quad corner (-1..1)
//   TEXCOORD1 (world size in metres, per-star twinkle phase hashed from its HR)
//   COLOR     rgb = B-V -> blackbody colour, a = magnitude -> brightness
//
// This shader then does, per frame, from _Time only (no scripts):
//   * CELESTIAL ROTATION — Rodrigues rotation about the true celestial pole
//     _Pole = (0, sin lat, cos lat), so the whole sky wheels about Polaris at
//     the authored rate and stars RISE IN THE EAST AND SET IN THE WEST. Below
//     the horizon their quads collapse to a point and contribute nothing.
//   * ATMOSPHERIC EXTINCTION — stars dim as they approach the horizon.
//   * SCINTILLATION — per-star twinkle whose amplitude follows the Rozenberg
//     (1966) airmass, so horizon stars shimmer hard and zenith stars barely
//     move; two incommensurate sines so it never reads as a metronome.
// The quad is built in the star's own local frame, NOT toward the camera: the
// same geometry in both eyes, no head coupling (the permanent VR constraint).
Shader "GloomhavenVR/EnvStarPoints"
{
    Properties
    {
        _Gain ("Brightness gain", Range(0,8)) = 1.6
        _Pole ("Celestial pole (object space)", Vector) = (0,0.743,0.669,0)
        _RotSpeed ("Sky rotation (rad/sec)", Range(0,0.05)) = 0.0021817
        _TwinkleAmp ("Twinkle amount", Range(0,2)) = 0.75
        _TwinkleSpeed ("Twinkle speed", Range(0,10)) = 1.9
        _Core ("Core hardness", Range(1,80)) = 34
        _Extinct ("Extinction (mag per airmass)", Range(0,1)) = 0.26
        _MoonDir ("Moon direction (object space)", Vector) = (0,1,0,0)
        _MoonCos ("cos(moon disc radius) — stars inside are culled", Range(0.9,1)) = 1.0
    }
    SubShader
    {
        // after the photo dome (Background+5), before all room geometry; the
        // trees occlude the sky correctly because they are opaque Geometry.
        Tags { "Queue"="Background+6" "RenderType"="Background" "IgnoreProjector"="True" }
        Blend One One
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
    // Shared with every other Env* shader that reads _Time: in multiplayer the elected
    // owner's epoch arrives on Net record 31 so both skies stand at the same hour; 0 offline.
    float _GhvrTimeOfs;

            float _Gain, _RotSpeed, _TwinkleAmp, _TwinkleSpeed, _Core, _Extinct, _MoonCos;
            float4 _Pole, _MoonDir;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv     : TEXCOORD0;   // quad corner
                float2 uv1    : TEXCOORD1;   // (size, phase)
                fixed4 color  : COLOR;       // (rgb colour, a brightness)
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv  : TEXCOORD0;
                fixed4 col : COLOR;
            };

            // same wrapped clock as EnvStars — see there; the wrap IS the sky's
            // revolution period so the rotation crosses it without a jump.
            #define SKY_PERIOD 2880.0

            v2f vert (appdata v)
            {
                v2f o;
                // MP-SHARED CLOCK: see EnvStars.SkyTime — one epoch for both sky layers, 0 offline.
                float t = fmod(_Time.y + _GhvrTimeOfs, SKY_PERIOD);
                float th = _RotSpeed * t;

                float3 P = normalize(_Pole.xyz);
                float R = length(v.vertex.xyz);
                float3 d = v.vertex.xyz / max(R, 1e-4);
                float c = cos(th), s = sin(th);
                float3 dr = d * c + cross(P, d) * s + P * dot(P, d) * (1.0 - c);

                // Rozenberg (1966) airmass — finite at the horizon (X -> 40),
                // unlike sec(z). Drives BOTH the extinction and the twinkle.
                float a = max(dr.y, 0.0);
                float X = 1.0 / (a + 0.025 * exp(-11.0 * a));

                // Bouguer extinction, normalized to the zenith, on the same
                // constant the dome uses (EnvStars/_Extinct) so the two layers
                // fade into the horizon together. A hard cut just below the
                // horizon makes a set star cost nothing (its quad degenerates).
                float ext = exp(-_Extinct * 0.921034 * X) / exp(-_Extinct * 0.921034)
                          * smoothstep(-0.015, 0.02, dr.y);

                // MOON OCCLUSION (see header): a star behind the disc is gone.
                // MOON PHASE (EnvElement.cginc): under Light the disc GROWS, so
                // the cull has to grow with it or a swollen moon shines with
                // stars inside it — the exact "Mond der dahinter ist" bug this
                // cull was written for, back the other way round. 1 - cos(x) is
                // x^2/2 for a disc 1.4 deg wide, so scaling the radius by s
                // scales (1 - _MoonCos) by s^2: no trig, and EXACTLY _MoonCos
                // when Light is down.
                // The ECLIPSE does not appear here on purpose: it is Earth's
                // shadow, not a body. Nothing moves in front of the sky beside
                // the moon, so nothing beside the moon may be occulted.
                GhvrElem e = GhvrElems();
                float moonCos = _MoonCos;
                if (e.live > 0.0)
                {
                    float s = GhvrMoonSize(e);
                    moonCos = 1.0 - (1.0 - _MoonCos) * s * s;
                }
                ext *= step(dot(dr, normalize(_MoonDir.xyz)), moonCos);

                // Log-compressed so the horizon shimmers instead of strobing.
                float amp = _TwinkleAmp * saturate(log(X) / log(40.0));
                float ph = v.uv1.y;
                float tw = 1.0 + amp * (0.62 * sin(t * _TwinkleSpeed + ph)
                                      + 0.38 * sin(t * _TwinkleSpeed * 1.618 + ph * 1.7));

                // ================================ ELEMENT ART ================
                // DARK EATS THE FAINT FIELD. Every star carries its own
                // brightness in COLOR.a (magnitude -> brightness, ~0.05 for the
                // faintest catalogue star at mag 6.5, well over 1 for the
                // brightest), so "keep the bright ones, take the rest" is one
                // smoothstep on a value the mesh already ships. A star that goes
                // out costs NOTHING to draw: its quad degenerates through `ext`
                // exactly the way a set star already does.
                // LIGHT does the opposite half — the survivors get brighter.
                // Under both, the sky ends up with a handful of hard bright
                // points on black, which is the split (see EnvStars for the
                // continuous layer's half of it).
                // (`e` is read above, for the moon cull.)
                float elemGain = 1.0;
                if (e.live > 0.0)
                {
                    elemGain = 1.0 + 1.40 * e.light;
                    // ONE application, on `ext`: it scales the quad as well as
                    // the brightness, so a culled star shrinks to nothing instead
                    // of merely dimming — which is both what a star at the limit
                    // of vision does and what makes it free.
                    // 0.030..0.22 rather than 0.045..0.34: the first bake culled
                    // so far up the magnitude distribution that under Light+Dark
                    // the sky held four points and none of them was a bright
                    // star, so the "fewer but BRIGHTER" half of the split had
                    // nothing to be brighter with. This takes the faint field and
                    // leaves the constellations.
                    ext *= lerp(1.0, smoothstep(0.030, 0.22, v.color.a), e.dark);
                }

                // quad basis from the star's own direction (never the camera)
                float3 ref = abs(dr.y) > 0.999 ? float3(0, 0, 1) : float3(0, 1, 0);
                float3 right = normalize(cross(ref, dr));
                float3 up = cross(dr, right);
                float size = v.uv1.x * ext;
                float3 opos = dr * R + right * (v.uv.x * size) + up * (v.uv.y * size);

                o.pos = UnityObjectToClipPos(float4(opos, 1.0));
                o.uv = v.uv;
                o.col = fixed4(v.color.rgb, v.color.a * ext * max(tw, 0.0) * _Gain * elemGain);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float r2 = dot(i.uv, i.uv);
                // soft point-spread + a tighter core: bright stars grow a small
                // halo the way a real one does through an eyepiece/eye
                float a = exp(-r2 * 5.0) * 0.45 + exp(-r2 * _Core) * 0.85;
                return fixed4(i.col.rgb * (i.col.a * a), 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
