// Self-contained UNLIT campaign-map shader (the definitive fix for the black/flat map).
//
// WHY custom: the game's campaign-map parchment (MapChoreographer.worldMap / cityMap,
// mesh GH_Campaign_Map_Mesh) is drawn by the game's Amplify shader (Amp_Basic_N_MRAO)
// which has NO forward pass and is rendered by a DEFERRED camera. Deferred content
// renders PURE BLACK into any off-screen RenderTexture the mod owns — only FORWARD
// content captures. The mod therefore re-draws the REAL parchment mesh with a mod-owned
// FORWARD camera and a temporary MATERIAL OVERRIDE using THIS shader (scoped to that one
// render; the game's own mesh/materials are never mutated — multiplayer-safe).
//
// UV SOURCE (verified on hardware, build 20f8f79c0): the mesh DOES carry real UV channels
// — HasVertexAttribute reported TexCoord0/1/2 = dim2 (isReadable=false only blocks CPU
// DATA access, not the channel's existence). So this shader samples the mesh's OWN UV
// (default TexCoord0) directly — pixel-perfect, and it tracks the game's pan/zoom for free
// because the UVs are per-vertex. Each of the four quadrant submeshes (GH_CampaignMap_01..04)
// carries its own 4096² albedo and its own 0..1 UV, so NO per-quadrant remap is needed.
//   _UvChannel selects 0/1/2 (TexCoord0/1/2) at runtime from the mod DLL — no rebuild needed
//   to try another channel if albedo turns out to live on UV1/UV2.
//   _UvScale/_UvOffset are an optional identity-by-default fine-tune (uv*scale+offset).
//
// Being a bundled shader, it compiles INTO gloomhavenvr.bundle so it is always present at
// runtime (a bundled material referencing a built-in shader is the pink-material trap).
// MUST be compiled by the game's exact 2021.3.5f1 editor (a 2021.3.45-compiled shader
// renders PINK in-game — see the unity-bundle-build memory).
//
// Forward, unlit, Cull Off (the mesh has mixed/degenerate winding), ZWrite on, opaque.
Shader "GloomhavenVR/MapUnlit"
{
    Properties
    {
        _MainTex ("Albedo (this quadrant)", 2D) = "white" {}
        // UV channel select: 0 = TexCoord0 (default), 1 = TexCoord1, 2 = TexCoord2.
        _UvChannel ("UV channel (0/1/2)", Float) = 0
        // Optional identity-by-default fine-tune of the chosen UV (uv * scale + offset).
        _UvScale ("UV scale (xy)", Vector) = (1,1,0,0)
        _UvOffset ("UV offset (xy)", Vector) = (0,0,0,0)
        // Brightness multiplier (later tuning). Textures are DXT1/opaque, so alpha is forced 1.
        _Bright ("Brightness", Float) = 1
        // ---- retained (unused by the UV path) so old material.SetVector calls stay harmless ----
        _LocalMin ("Local bounds min (xyz) [unused]", Vector) = (0,0,0,0)
        _LocalSize ("Local bounds size (xyz) [unused]", Vector) = (1,1,1,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull Off
        ZWrite On
        ZTest LEqual
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv0 : TEXCOORD0;
                float2 uv1 : TEXCOORD1;
                float2 uv2 : TEXCOORD2;
            };

            sampler2D _MainTex;
            float _UvChannel;
            float4 _UvScale;
            float4 _UvOffset;
            float _Bright;
            float4 _LocalMin;   // unused (kept for SetVector compatibility)
            float4 _LocalSize;  // unused

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv0 = v.uv0;
                o.uv1 = v.uv1;
                o.uv2 = v.uv2;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Pick the UV channel the game's albedo is authored on (default TexCoord0).
                float2 uv = i.uv0;
                if (_UvChannel > 1.5)      uv = i.uv2;
                else if (_UvChannel > 0.5) uv = i.uv1;
                uv = uv * _UvScale.xy + _UvOffset.xy;
                fixed3 col = tex2D(_MainTex, uv).rgb * _Bright;
                // Force opaque — the quadrant textures are DXT1 (no meaningful alpha).
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
}
