// GloomhavenVR — REAL star layer of the night sky (point sprites).
//
// User finding, ModBuild 132: "Der Sternenhimmel sollte auch ein animierte
// 'echter' Sternenhimmel sein, recharchier da was du findest statt einfach nur
// ein Bild."
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

            float _Gain, _RotSpeed, _TwinkleAmp, _TwinkleSpeed, _Core;
            float4 _Pole;

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
                float t = fmod(_Time.y, SKY_PERIOD);
                float th = _RotSpeed * t;

                float3 P = normalize(_Pole.xyz);
                float R = length(v.vertex.xyz);
                float3 d = v.vertex.xyz / max(R, 1e-4);
                float c = cos(th), s = sin(th);
                float3 dr = d * c + cross(P, d) * s + P * dot(P, d) * (1.0 - c);

                // extinction + hard horizon: stars set, and a set star costs
                // nothing (its quad degenerates to a point).
                float ext = smoothstep(-0.015, 0.10, dr.y);

                // Rozenberg (1966) airmass — finite at the horizon (X -> 40),
                // unlike sec(z). Log-compressed so the horizon shimmers instead
                // of strobing.
                float a = max(dr.y, 0.0);
                float X = 1.0 / (a + 0.025 * exp(-11.0 * a));
                float amp = _TwinkleAmp * saturate(log(X) / log(40.0));
                float ph = v.uv1.y;
                float tw = 1.0 + amp * (0.62 * sin(t * _TwinkleSpeed + ph)
                                      + 0.38 * sin(t * _TwinkleSpeed * 1.618 + ph * 1.7));

                // quad basis from the star's own direction (never the camera)
                float3 ref = abs(dr.y) > 0.999 ? float3(0, 0, 1) : float3(0, 1, 0);
                float3 right = normalize(cross(ref, dr));
                float3 up = cross(dr, right);
                float size = v.uv1.x * ext;
                float3 opos = dr * R + right * (v.uv.x * size) + up * (v.uv.y * size);

                o.pos = UnityObjectToClipPos(float4(opos, 1.0));
                o.uv = v.uv;
                o.col = fixed4(v.color.rgb, v.color.a * ext * max(tw, 0.0) * _Gain);
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
