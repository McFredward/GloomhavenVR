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

            struct appdata { float4 vertex : POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };

            v2f vert (appdata v)
            {
                v2f o;
                float4 col = v.color * _Tint;
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
