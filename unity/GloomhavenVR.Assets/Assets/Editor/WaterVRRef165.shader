// GloomhavenVR — THE MODBUILD 165 WATER, FROZEN, AS A MEASUREMENT REFERENCE ONLY.
//
// ============================================================================
//  THIS SHADER IS NOT SHIPPED AND MUST NEVER BE
// ============================================================================
//  It lives under Assets/Editor/, which BuildBundles does not collect — the bundle is everything
//  under Assets/Bundle/ and nothing else — so it cannot reach a headset. Nothing in src/ names it.
//  It exists for exactly one job: the contact sheet's REFERENCE COLUMN.
//
//  WHY A COPY AND NOT THE SHIPPED SHADER WITH DIFFERENT DIALS. Two rounds of this work have been
//  judged against a sheet that put the rejected build next to the proposed one, and it has worked
//  both times. ModBuild 166 could not do that from one shader, because what it changes is not a
//  number: the ruling was "Ich will außerdem so gut wie KEIN fließen, es ist kein Fluss sondern
//  eine Pfütze", and the answer was to DELETE the translation terms rather than set them to zero,
//  so that no dial and no later edit can bring them back. A reference column therefore needs a
//  shader that still HAS them, and the only honest place for that is a frozen copy that is plainly
//  labelled and provably unshippable.
//
//  IT IS FROZEN. Nothing here is to be tuned, improved or kept in step with the shipped shader.
//  If the shipped surface changes, this file stays exactly as ModBuild 165 shipped it — that is
//  what makes the column a reference rather than a second candidate. The contents below are the
//  ModBuild 165 WaterVR.cginc and WaterVR.shader, textually, with the include flattened in.
//
//  WHAT THE COLUMN IS FOR. The harness measures two numbers per column: the NET TRANSLATION of the
//  pattern between two instants (which this one has and the shipped one does not) and the FRACTION
//  OF PIXELS THAT CHANGE PER SECOND (which this one was judged "viel zu hektisch" for). A number
//  with nothing to compare it against is not a measurement.
Shader "GloomhavenVR/WaterVRRef165"
{
    Properties
    {
        _MainTex ("Body texture", 2D) = "white" {}
        _Color ("Body tint", Color) = (0.195,0.311,0.131,0.737)
        _Normal_Map ("Ripple normal map", 2D) = "bump" {}
        _NormalTilings ("Resolved tilings", Vector) = (0.52,1.61,-0.14,-0.17)
        _LayerWeights ("Layer amplitude weights", Vector) = (0.145,0.855,0,0)
        _WaterUVAnimSpeedA ("Layer A rate (peak WORLD UNITS/s)", Vector) = (0.072,0.072,0,0)
        _WaterUVAnimSpeedB ("Layer B rate (peak WORLD UNITS/s)", Vector) = (0.060,0.120,0,0)
        _RippleSway ("A period s, B period s, drift share", Vector) = (13,17,0.25,0)
        _NormalStrength ("Ripple strength", Range(0,8)) = 0.5
        _ProcNormal ("Analytic ripple instead of the texture", Range(0,1)) = 0
        _SwellAmp ("Swell amplitude (world units, peak)", Range(0,0.06)) = 0.036
        _SwellWave ("Swell wavelength (world units)", Float) = 2.4
        _SwellSpeed ("Swell trace drift (world units/s)", Float) = 0.02
        _SwellPeriod ("Swell period (seconds)", Float) = 9
        _SwellCalm ("Large-scale calm depth", Range(0,1)) = 0.75
        _TessFactor ("Tessellation factor", Range(1,8)) = 4
        _LightDir ("Direction TOWARD the light (surface-local)", Vector) = (0.34,0.22,0.91,0)
        _Shimmer ("Glint strength", Range(0,2)) = 0.05
        _WaveShade ("Wave body shading depth", Range(0,1)) = 0.35
        _Smoothness ("Glint tightness", Range(0,1)) = 0.754
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 0
        _ZWrite ("ZWrite", Float) = 0
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 10
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex;
    sampler2D _Normal_Map;
    fixed4 _Color;
    float4 _NormalTilings, _LayerWeights, _WaterUVAnimSpeedA, _WaterUVAnimSpeedB, _LightDir,
           _RippleSway;
    float _NormalStrength, _ProcNormal, _Shimmer, _WaveShade, _Smoothness;
    float _SwellAmp, _SwellWave, _SwellSpeed, _SwellPeriod, _SwellCalm;
    float _TessFactor;
    float _GhvrTimeOfs;

    #define GHVR_TAU 6.2831853

    struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
    struct v2f { float4 pos : SV_POSITION; float4 uv : TEXCOORD0; fixed4 color : COLOR; };

    // ModBuild 165's standing component — WITH the `- drift * t` in the spatial phase that this
    // round deletes.
    float3 GhvrStanding(float2 p, float t, float2 dir, float wavelength, float period,
                        float amp, float spatialPhase, float temporalPhase, float drift)
    {
        float k = GHVR_TAU / max(wavelength, 0.05);
        float w = GHVR_TAU / max(period, 0.25);
        float sp = k * (dot(dir, p) - drift * t) + spatialPhase;
        float bob = cos(w * t + temporalPhase);
        float c = cos(sp);
        return float3(amp * sin(sp) * bob, amp * k * dir.x * c * bob, amp * k * dir.y * c * bob);
    }

    #define GHVR_SWELL_ASUM 2.01

    // ModBuild 165's calm modulation — two TRAVELLING waves, drifting with the components and
    // carrying their own 480 s and 660 s cycles as a phase that grows with the clock.
    float GhvrSwellCalm (float2 p, float t, float L, float drift, out float2 grad)
    {
        const float2 dA = float2( 0.798636, 0.601815);
        const float2 dB = float2(-0.587785, 0.809017);
        float kA = GHVR_TAU / max(L * 4.1, 0.2);
        float kB = GHVR_TAU / max(L * 6.7, 0.2);
        float qA = kA * (dot(dA, p) - drift * t) + (GHVR_TAU / 480.0) * t;
        float qB = kB * (dot(dB, p) - drift * t) + (GHVR_TAU / 660.0) * t + 2.1;
        float m = 0.5 * (sin(qA) + sin(qB));
        float2 gm = 0.5 * (kA * dA * cos(qA) + kB * dB * cos(qB));
        float calm = saturate(_SwellCalm);
        grad = 0.5 * calm * gm;
        return 1.0 - calm * (0.5 - 0.5 * m);
    }

    float3 GhvrSwell (float2 p, float t)
    {
        float L = max(_SwellWave, 0.15);
        float T = max(_SwellPeriod, 0.5);
        float v = _SwellSpeed;

        float3 s;
        s  = GhvrStanding(p, t, float2( 0.956305, 0.292372), L * 1.000000,
                          T * 1.000000, 1.00 / GHVR_SWELL_ASUM, 0.000, 0.000, v);
        s += GhvrStanding(p, t, float2(-0.224951, 0.974370), L * 0.618034,
                          T * 0.786151, 0.55 / GHVR_SWELL_ASUM, 2.399, 1.777, v);
        s += GhvrStanding(p, t, float2( 0.484810, 0.874620), L * 0.414214,
                          T * 0.643595, 0.30 / GHVR_SWELL_ASUM, 4.113, 3.412, v);
        s += GhvrStanding(p, t, float2(-0.848048, 0.529919), L * 0.267949,
                          T * 0.517638, 0.16 / GHVR_SWELL_ASUM, 1.071, 5.108, v);

        float2 calmGrad;
        float calm = GhvrSwellCalm(p, t, L, v, calmGrad);
        float h = _SwellAmp * calm * s.x;
        float2 g = _SwellAmp * (calm * s.yz + s.x * calmGrad);
        return float3(h, g);
    }

    v2f GhvrWaterVert (appdata v)
    {
        v2f o;
        float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz;
        float t = _Time.y + _GhvrTimeOfs;
        wp.y += GhvrSwell(wp.xz, t).x;
        o.pos = UnityWorldToClipPos(wp);
        o.uv = float4(v.uv, wp.xz);
        o.color = v.color;
        return o;
    }

    float2 GhvrBumpXY (float4 packed)
    {
        return float2(packed.r * packed.a, packed.g) * 2.0 - 1.0;
    }

    float2 GhvrProcBumpXY (float2 p)
    {
        const float k = GHVR_TAU;
        float a = k * p.x + 0.8 * sin(k * p.y);
        float b = k * (2.0 * p.y - p.x) - 1.1;
        float2 g;
        g.x = k * cos(a) - 0.6 * k * cos(b);
        g.y = 0.8 * k * cos(k * p.y) * cos(a) + 1.2 * k * cos(b);
        return g * 0.05;
    }

    // ModBuild 165's ripple offset: a one-way drift PLUS a sway that reverses. This is the term the
    // user described as "es fließt jetzt einmal in die eine Richtung, stoppt kurz und fließt dann
    // wieder in die andere".
    float2 GhvrRippleOffset (float2 rate, float period, float2 tiling, float t)
    {
        float share = saturate(_RippleSway.z);
        float P = max(period, 0.5);
        float2 drift = frac(rate * share * t * tiling);
        float2 sway = rate * (1.0 - share) * (P / GHVR_TAU) * sin(GHVR_TAU * t / P) * tiling;
        return drift + sway;
    }

    fixed4 GhvrWaterFrag (v2f i) : SV_Target
    {
        float t = _Time.y + _GhvrTimeOfs;
        float2 tilA = _NormalTilings.xy, tilB = _NormalTilings.zw;
        float2 uvA = i.uv.zw * tilA + GhvrRippleOffset(_WaterUVAnimSpeedA.xy, _RippleSway.x, tilA, t);
        float2 uvB = i.uv.zw * tilB + GhvrRippleOffset(_WaterUVAnimSpeedB.xy, _RippleSway.y, tilB, t);

        float2 wgt = _LayerWeights.xy;
        float2 bump;
        if (_ProcNormal > 0.5)
            bump = wgt.x * GhvrProcBumpXY(uvA) + wgt.y * GhvrProcBumpXY(uvB);
        else
            bump = wgt.x * GhvrBumpXY(tex2D(_Normal_Map, uvA))
                 + wgt.y * GhvrBumpXY(tex2D(_Normal_Map, uvB));

        float3 sw = GhvrSwell(i.uv.zw, t);
        float3 n = normalize(float3(-sw.yz + bump * _NormalStrength, 1.0));
        float3 L = normalize(_LightDir.xyz + float3(0, 0, 1e-4));
        float ndl = saturate(dot(n, L));
        float ndl0 = saturate(L.z);

        float rel = (ndl - ndl0) / max(1.0 - ndl0, 0.05);
        rel = rel / (1.0 + abs(rel));
        float3 body = tex2D(_MainTex, i.uv.xy).rgb * _Color.rgb * i.color.rgb
                    * (1.0 + _WaveShade * rel);

        float e = lerp(1.0, 8.0, saturate(_Smoothness));
        float glint = _Shimmer * max(pow(ndl, e) - pow(ndl0, e), 0.0);
        float alpha = saturate(_Color.a * i.color.a);
        return fixed4(body + glint.xxx, alpha);
    }
    ENDCG

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-100" "IgnoreProjector"="True" }
        LOD 300

        Pass
        {
            Cull [_Cull]
            ZTest [_ZTest]
            ZWrite [_ZWrite]
            Blend [_SrcBlend] [_DstBlend]

            CGPROGRAM
            #pragma target 4.6
            #pragma vertex GhvrTessVert
            #pragma hull GhvrHull
            #pragma domain GhvrDomain
            #pragma fragment GhvrWaterFrag

            struct GhvrPatch
            {
                float4 vertex : INTERNALTESSPOS;
                float2 uv     : TEXCOORD0;
                float4 color  : COLOR;
            };

            struct GhvrPatchConst
            {
                float edge[3] : SV_TessFactor;
                float inside  : SV_InsideTessFactor;
            };

            GhvrPatch GhvrTessVert (appdata v)
            {
                GhvrPatch o;
                o.vertex = v.vertex;
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            GhvrPatchConst GhvrPatchConstant (InputPatch<GhvrPatch, 3> patch)
            {
                GhvrPatchConst o;
                float f = clamp(_TessFactor, 1.0, 8.0);
                o.edge[0] = f;
                o.edge[1] = f;
                o.edge[2] = f;
                o.inside = f;
                return o;
            }

            [UNITY_domain("tri")]
            [UNITY_partitioning("integer")]
            [UNITY_outputtopology("triangle_cw")]
            [UNITY_patchconstantfunc("GhvrPatchConstant")]
            [UNITY_outputcontrolpoints(3)]
            GhvrPatch GhvrHull (InputPatch<GhvrPatch, 3> patch, uint id : SV_OutputControlPointID)
            {
                return patch[id];
            }

            [UNITY_domain("tri")]
            v2f GhvrDomain (GhvrPatchConst tess, const OutputPatch<GhvrPatch, 3> patch,
                            float3 bary : SV_DomainLocation)
            {
                appdata v;
                v.vertex = patch[0].vertex * bary.x + patch[1].vertex * bary.y
                         + patch[2].vertex * bary.z;
                v.uv = patch[0].uv * bary.x + patch[1].uv * bary.y + patch[2].uv * bary.z;
                v.color = patch[0].color * bary.x + patch[1].color * bary.y
                        + patch[2].color * bary.z;
                return GhvrWaterVert(v);
            }
            ENDCG
        }
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent-100" "IgnoreProjector"="True" }
        LOD 100

        Pass
        {
            Cull [_Cull]
            ZTest [_ZTest]
            ZWrite [_ZWrite]
            Blend [_SrcBlend] [_DstBlend]

            CGPROGRAM
            #pragma target 3.0
            #pragma vertex GhvrWaterVert
            #pragma fragment GhvrWaterFrag
            ENDCG
        }
    }
    Fallback Off
}
