// GloomhavenVR — night-sky dome shader: the BACKDROP layer of the sky.
//
// User finding, ModBuild 132: "Der Sternenhimmel sollte auch ein animierte
// 'echter' Sternenhimmel sein ... statt einfach nur ein Bild."
// The sky is now TWO layers with a clear division of labour:
//   * THIS shader draws the photographic backdrop — Milky Way band, nebula
//     washes, unresolved star glow, airglow and the horizon mist of the
//     processed 'Rogland Clear Night' panorama (CC0, see BuildEnvironments.cs
//     NIGHT SKY). Its bright STAR CORES are suppressed here (_CoreSuppress
//     against the alpha star mask the pipeline already bakes), because...
//   * ...EnvStarPoints.shader draws the real stars: 8404 Yale Bright Star
//     Catalogue entries at their true RA/Dec with catalogue magnitudes and
//     B-V colours, wheeling about the celestial pole and rising/setting.
// Photo drift and star rotation share one 2880 s period so the two layers read
// as ONE turning sky.
//
// Everything is _Time-driven (no scripts — bundle rule), world-direction keyed
// (never screen-space: a screen-keyed pattern differs per eye under MultiPass
// and causes binocular rivalry), and stereo-correct: the dome is a real
// inverted sphere mesh. Background queue backdrop, no depth write.
Shader "GloomhavenVR/EnvStars"
{
    Properties
    {
        _MainTex ("Night sky photo band -20..90deg (RGB=sky, A=star-core mask)", 2D) = "black" {}
        _SkyBoost ("Sky layer intensity", Range(0,4)) = 1.0
        _CoreSuppress ("Photo star-core suppression", Range(0,1)) = 0.85
        _StarCol ("Twinkle top-up color", Color) = (0.85,0.9,1.0,1)
        _TwinkleSpeed ("Twinkle speed", Range(0,10)) = 1.6
        _TwinkleAmp ("Twinkle amount", Range(0,1)) = 0.5
        _DriftSpeed ("Sky drift (UV/sec)", Range(0,0.01)) = 0.000347
        _TopCol ("Sky zenith color", Color) = (0.009,0.014,0.030,1)
        _HorizonCol ("Sky horizon color", Color) = (0.034,0.05,0.076,1)
        _HazeTex ("Haze veil (tiling noise)", 2D) = "black" {}
        _HazeCol ("Haze color", Color) = (0.09,0.11,0.15,1)
        _HazeAmt ("Haze amount", Range(0,2)) = 0.55
        _HorizonDim ("Low-sky dimming", Range(0,1)) = 1.0
        _MoonTex ("Moon sprite (RGBA)", 2D) = "black" {}
        _MoonDir ("Moon direction (world)", Vector) = (0.6,0.37,0.71,0)
        _MoonCol ("Moon color", Color) = (1,0.98,0.92,1)
        _MoonExtent ("Moon half-extent (tan units)", Range(0.01,0.4)) = 0.085
    }
    SubShader
    {
        Tags { "Queue"="Background+5" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off
        Fog { Mode Off }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex, _MoonTex, _HazeTex;
            fixed4 _StarCol, _TopCol, _HorizonCol, _MoonCol, _HazeCol;
            float _SkyBoost, _CoreSuppress, _TwinkleSpeed, _TwinkleAmp, _DriftSpeed;
            float _MoonExtent, _HazeAmt, _HorizonDim;
            float4 _MoonDir;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wp : TEXCOORD1; };

            // _Time.y is seconds since load in a float: after hours of play its
            // precision collapses and Adreno GPUs visibly stutter on the big
            // values. Every animated term uses this wrapped clock instead; the
            // wrap equals the sky's own revolution period, so the drift and the
            // star rotation cross it seamlessly.
            #define SKY_PERIOD 2880.0
            float SkyTime() { return fmod(_Time.y, SKY_PERIOD); }

            // Interleaved gradient noise (Jimenez) — 1 LSB of dither on the
            // near-black sky gradient. Without it a hemisphere spanning ~6 of
            // 256 blue levels contours into visible bands on the headset.
            float Ign(float2 p) { return frac(52.9829189 * frac(dot(p, float2(0.06711056, 0.00583715)))); }

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.wp = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float t = SkyTime();
                float3 d = normalize(i.wp - _WorldSpaceCameraPos);
                float h = saturate(d.y);                      // 0 at horizon, 1 at zenith
                float3 sky = lerp(_HorizonCol.rgb, _TopCol.rgb, pow(h, 0.6));
                // below the horizon: fade to near-black so the dome never shows a
                // bright band beyond the ground disc's rim
                sky = lerp(sky, float3(0.004, 0.006, 0.010), saturate(-d.y * 6.0));

                // photographic backdrop. U drifts one full turn per SKY_PERIOD,
                // matching the star layer's rotation; wrapU=Repeat handles it.
                float2 suv = float2(i.uv.x + t * _DriftSpeed, i.uv.y);
                fixed4 s = tex2D(_MainTex, suv);
                // the alpha mask marks the photo's own star cores: fade them out
                // so the catalogue star layer is the only source of point stars
                // (two unaligned starfields would read as a double exposure).
                float3 photo = s.rgb * (1.0 - _CoreSuppress * s.a);
                // The panorama's own starlit mist band is bright, and against it
                // a treeline silhouettes as a flat black wall. Dimming the lowest
                // ~10 deg of sky lets the forest close the horizon instead — and
                // deep black between the trunks is the whole point of the room.
                photo *= lerp(_HorizonDim, 1.0, smoothstep(-0.02, 0.17, d.y));
                float3 col = sky + photo * _SkyBoost;

                // residual twinkle on what is left of the photo's cores — keeps
                // the faint unresolved field alive without fighting the catalogue
                float ph = d.x * 47.0 + d.y * 61.0 + d.z * 53.0;
                float tw = 0.5 + 0.5 * sin(t * _TwinkleSpeed + ph);
                col += _StarCol.rgb * (s.a * (1.0 - _CoreSuppress) * tw * _TwinkleAmp);

                // drifting haze veil: two wrapping noise octaves crossing at
                // different rates, concentrated near the horizon like real
                // extinction. It DIMS what is behind it and adds its own faint
                // scatter — an additive-only veil reads as fog lit from inside.
                float2 huv = float2(i.uv.x, i.uv.y * 2.2);
                float n1 = tex2D(_HazeTex, huv * float2(1.7, 1.0) + float2( 0.00062, 0.00004) * t).r;
                float n2 = tex2D(_HazeTex, huv * float2(0.9, 0.7) + float2(-0.00031, 0.00011) * t).r;
                float veil = saturate(n1 * n2 * 2.6 - 0.35) * _HazeAmt * pow(1.0 - saturate(d.y), 2.2);
                col = col * (1.0 - 0.75 * veil) + _HazeCol.rgb * veil;

                // moon: baked into the dome (a separate blended quad showed sorting
                // seams against the gradient) — gnomonic-project the sprite around
                // _MoonDir and alpha-blend it over the sky.
                float3 md = normalize(_MoonDir.xyz);
                float mt = dot(d, md);
                if (mt > 0.5)
                {
                    float3 right = normalize(cross(float3(0, 1, 0), md));
                    float3 up = cross(md, right);
                    float3 p = d / mt;
                    float2 muv = float2(dot(p, right), dot(p, up)) / (2.0 * _MoonExtent) + 0.5;
                    if (all(muv > 0.0) && all(muv < 1.0))
                    {
                        fixed4 mc = tex2D(_MoonTex, muv);
                        col = lerp(col, mc.rgb * _MoonCol.rgb, mc.a);
                    }
                }

                col += (1.0 / 255.0) * Ign(i.pos.xy) - (0.5 / 255.0);
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
