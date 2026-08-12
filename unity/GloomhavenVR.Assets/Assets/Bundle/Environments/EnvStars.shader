// GloomhavenVR — night-sky dome shader (Sumpfnacht). Renders a vertical sky gradient
// plus a PAINTED sky layer (Env_Stars.png: RGB = milky-way/nebula wash + stars with
// PSF halos, A = bright-star twinkle mask). Twinkle and an ultra-slow sky drift are
// driven purely by shader _Time — data-driven animation, no scripts, world-anchored,
// stereo-correct (the dome is a real inverted sphere mesh ~45 m out).
// Background-queue backdrop, no depth write, exactly the SkyPanoramic contract so it
// never occludes anything.
Shader "GloomhavenVR/EnvStars"
{
    Properties
    {
        _MainTex ("Night sky (RGB=painted layer, A=twinkle mask)", 2D) = "black" {}
        _SkyBoost ("Painted layer intensity", Range(0,4)) = 1.0
        _StarCol ("Twinkle top-up color", Color) = (0.85,0.9,1.0,1)
        _TwinkleSpeed ("Twinkle speed", Range(0,10)) = 1.6
        _TwinkleAmp ("Twinkle amount", Range(0,1)) = 0.5
        _DriftSpeed ("Sky drift (UV/sec)", Range(0,0.01)) = 0.00035
        _TopCol ("Sky zenith color", Color) = (0.009,0.014,0.030,1)
        _HorizonCol ("Sky horizon color", Color) = (0.034,0.05,0.076,1)
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

            sampler2D _MainTex, _MoonTex;
            fixed4 _StarCol, _TopCol, _HorizonCol, _MoonCol;
            float _SkyBoost, _TwinkleSpeed, _TwinkleAmp, _DriftSpeed, _MoonExtent;
            float4 _MoonDir;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 wp : TEXCOORD1; };

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
                float3 d = normalize(i.wp - _WorldSpaceCameraPos);
                float h = saturate(d.y);                      // 0 at horizon, 1 at zenith
                float3 sky = lerp(_HorizonCol.rgb, _TopCol.rgb, pow(h, 0.6));
                // below the horizon: fade to near-black so the dome never shows a
                // bright band beyond the water disc's rim
                sky = lerp(sky, float3(0.004, 0.006, 0.010), saturate(-d.y * 6.0));

                // painted layer (milky way, nebula washes, stars incl. halos and
                // horizon fades — all baked). U drifts imperceptibly slowly so the
                // vault feels alive; wrapU=Repeat handles the wrap.
                float2 suv = float2(i.uv.x + _Time.y * _DriftSpeed, i.uv.y);
                fixed4 s = tex2D(_MainTex, suv);
                float3 col = sky + s.rgb * _SkyBoost;
                // twinkle: brighten bright-star cores (alpha mask) with a spatial
                // phase field — coherent across one star, decorrelated between stars
                float ph = d.x * 47.0 + d.y * 61.0 + d.z * 53.0;
                float tw = 0.5 + 0.5 * sin(_Time.y * _TwinkleSpeed + ph);
                col += _StarCol.rgb * (s.a * tw * _TwinkleAmp);

                // moon: baked into the dome (a separate blended quad showed sorting
                // seams against the gradient) — gnomonic-project the sprite around
                // _MoonDir and alpha-blend it over the sky.
                float3 md = normalize(_MoonDir.xyz);
                float t = dot(d, md);
                if (t > 0.5)
                {
                    float3 right = normalize(cross(float3(0, 1, 0), md));
                    float3 up = cross(md, right);
                    float3 p = d / t;
                    float2 muv = float2(dot(p, right), dot(p, up)) / (2.0 * _MoonExtent) + 0.5;
                    if (all(muv > 0.0) && all(muv < 1.0))
                    {
                        fixed4 mc = tex2D(_MoonTex, muv);
                        col = lerp(col, mc.rgb * _MoonCol.rgb, mc.a);
                    }
                }
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
    Fallback Off
}
