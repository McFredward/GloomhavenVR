// GloomhavenVR — self-lit room-interior shader (custom-asset environments).
//
// The environments are script-free prefabs on the mod layer; the game scene
// contributes NO usable lights, so ALL lighting is baked into material
// parameters and evaluated here:
//   - hemisphere ambient (world-up based: _AmbUp / _AmbDown),
//   - one directional "moon" light (direction in OBJECT space, set per
//     placement by BuildEnvironments.cs),
//   - up to 3 point lights (candles/lanterns; positions in OBJECT space,
//     w = 1/range in object units; alpha of the color = flicker amount,
//     animated purely by _Time — data-driven, no scripts, stereo-correct).
// Per-pixel normal mapping works naturally against these lights (TBN stays
// in object space, no per-frame CPU cost). Vertex color (lerped in by _VCol)
// carries baked large-scale shading such as the swamp ground's radial
// fade-to-darkness. Diffuse only: no view-dependent terms except none — the
// safest possible shader for stereo.
//
// Written into the bundle because builtin shaders may be stripped from the
// game player (the pink-material trap, TOOLCHAIN.md §4.1).
Shader "GloomhavenVR/EnvRoom"
{
    Properties
    {
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal map", 2D) = "bump" {}
        _BumpScale ("Normal strength", Range(0,2)) = 1
        _Tint ("Tint", Color) = (1,1,1,1)
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
        _VCol ("Vertex color amount", Range(0,1)) = 0
        // Cold moon rim (forest trunks). Defaults to BLACK so every existing
        // material — the whole cellar — is bit-identical without it.
        _RimCol ("Rim light color", Color) = (0,0,0,1)
        _RimPow ("Rim tightness", Range(0.5,8)) = 3.0
        _RimDir ("Rim gate: light dir (OBJECT space)", Vector) = (0,1,0,0)
        // Point-light NEAR-FIELD hardness (see PointLight below). 0 reproduces
        // the old pure (1-(d/r)^2)^2 window exactly => the forest is untouched.
        _PtHard ("Point falloff hardness", Range(0,64)) = 0
        [Toggle] _Cutout ("Alpha cutout", Float) = 0
        _Cutoff ("Cutout threshold", Range(0,1)) = 0.5
    }

    CGINCLUDE
    #include "UnityCG.cginc"

    sampler2D _MainTex; float4 _MainTex_ST;
    sampler2D _BumpMap;
    float _BumpScale, _VCol, _Cutout, _Cutoff, _RimPow, _PtHard;
    fixed4 _Tint, _AmbUp, _AmbDown, _DirCol, _L0Col, _L1Col, _L2Col, _RimCol;
    float4 _DirDir, _L0Pos, _L1Pos, _L2Pos, _RimDir;

    // PREVIEW-ONLY global clock offset. Never set at runtime (=> 0, the shipped
    // behaviour); EnvironmentsPreview sets it with Shader.SetGlobalFloat so a
    // still frame can be rendered at an arbitrary point of every animation.
    // Declared in every Env* shader that reads _Time — the whole room has to
    // move to the SAME offset or a time series proves nothing.
    float _GhvrTimeOfs;

    struct appdata
    {
        float4 vertex  : POSITION;
        float3 normal  : NORMAL;
        float4 tangent : TANGENT;
        float2 uv      : TEXCOORD0;
        fixed4 color   : COLOR;
    };

    struct v2f
    {
        float4 pos    : SV_POSITION;
        float2 uv     : TEXCOORD0;
        float3 opos   : TEXCOORD1; // object-space position
        float3 n      : TEXCOORD2; // object-space normal
        float3 t      : TEXCOORD3; // object-space tangent
        float3 b      : TEXCOORD4; // object-space bitangent
        float3 ov     : TEXCOORD5; // object-space view vector (rim light only)
        fixed4 vcol   : COLOR;
    };

    v2f vert (appdata v)
    {
        v2f o;
        o.pos = UnityObjectToClipPos(v.vertex);
        o.uv = TRANSFORM_TEX(v.uv, _MainTex);
        o.opos = v.vertex.xyz;
        o.n = v.normal;
        o.t = v.tangent.xyz;
        o.b = cross(v.normal, v.tangent.xyz) * v.tangent.w;
        o.ov = ObjSpaceViewDir(v.vertex);
        o.vcol = v.color;
        return o;
    }

    // Candle flicker — three incommensurate sines plus one slow "breath", each
    // light on its OWN phase AND its own rate, so two candles in one room never
    // pulse as a pair. Amplitude is exactly +-amt*0.35 (the sine weights sum to
    // 1 and the breath is mixed in, not added on top).
    // amt is the alpha of the light colour, written by EnvRoomBuilder's rig.
    float Flicker (float amt, float phase, float rate)
    {
        float t = (_Time.y + _GhvrTimeOfs) * rate;
        float f = 0.42 * sin(t * 11.3 + phase)
                + 0.33 * sin(t *  6.1 + 1.7 + phase * 1.3)
                + 0.25 * sin(t * 19.7 + 4.2 + phase * 0.7);
        // the slow term is what a draft does to a flame: the whole pool swells
        // and sinks over a couple of seconds instead of only buzzing
        f = f * 0.70 + 0.30 * sin(t * 1.9 + phase * 0.5);
        return 1.0 + amt * 0.35 * f;
    }

    // Attenuation window (1-(d/r)^2)^2 divided by a near-field inverse-square
    // term. _PtHard = 0 is EXACTLY the old window (the forest's three points);
    // _PtHard > 0 collapses the lit pool toward the source, which is the only
    // way a candle can light its own table and leave the far wall black
    // (user finding, ModBuild 134: "die Kerzen beleuchten hier viel zu viel").
    float3 PointLight (float4 lpos, fixed4 lcol, float3 opos, float3 N, float phase, float rate)
    {
        float3 lv = lpos.xyz - opos;
        float d2 = max(dot(lv, lv), 1e-8);
        float d = sqrt(d2);
        float q = d2 * lpos.w * lpos.w;                  // (d/range)^2
        float x = saturate(1.0 - q);
        float atten = x * x / (1.0 + _PtHard * q);
        float ndl = saturate(dot(N, lv / d));
        return lcol.rgb * (atten * ndl * Flicker(lcol.a, phase, rate));
    }

    fixed4 fragCore (v2f i, float face)
    {
        fixed4 alb = tex2D(_MainTex, i.uv) * _Tint;
        if (_Cutout > 0.5) clip(alb.a - _Cutoff);

        float3 n_ts = UnpackNormal(tex2D(_BumpMap, i.uv));
        n_ts.xy *= _BumpScale;
        float3 N = normalize(i.t * n_ts.x + i.b * n_ts.y + i.n * n_ts.z);
        N *= face; // two-sided foliage: light the visible side

        // hemisphere ambient against WORLD up (correct under prefab yaw/scale)
        float3 nw = normalize(mul((float3x3)unity_ObjectToWorld, N));
        float3 light = lerp(_AmbDown.rgb, _AmbUp.rgb, nw.y * 0.5 + 0.5);

        light += _DirCol.rgb * saturate(dot(N, normalize(_DirDir.xyz)));
        // slot phases AND rates are incommensurate: the room breathes, it does
        // not pulse (user, ModBuild 134: "Eine flackernde Kerze sollte auch das
        // Licht drumrum zum flackern bekommen")
        light += PointLight(_L0Pos, _L0Col, i.opos, N, 0.0, 1.00);
        light += PointLight(_L1Pos, _L1Col, i.opos, N, 2.1, 0.83);
        light += PointLight(_L2Pos, _L2Col, i.opos, N, 4.4, 1.19);

        float3 col = alb.rgb * light;

        // Cold rim: a grazing-angle wrap of the moon, gated so only the moonlit
        // SIDE of a trunk catches it. This is what makes a night forest read as
        // volumes instead of flat silhouettes. It is view-dependent and so
        // differs slightly between the eyes — which is physically what a rim IS,
        // and the gradient is smooth, so it fuses (unlike a screen-space
        // pattern, which would not). Black by default => opt-in per material.
        float rim = pow(1.0 - saturate(dot(N, normalize(i.ov))), _RimPow)
                  * saturate(dot(N, normalize(_RimDir.xyz)) * 0.5 + 0.55);
        col += _RimCol.rgb * rim;
        col *= lerp(float3(1, 1, 1), i.vcol.rgb, _VCol);
        return fixed4(col, 1.0);
    }
    ENDCG

    // -------- opaque (default) --------
    SubShader
    {
        Tags { "Queue"="Geometry" "RenderType"="Opaque" }
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            fixed4 frag (v2f i) : SV_Target { return fragCore(i, 1.0); }
            ENDCG
        }
    }
    Fallback Off
}
