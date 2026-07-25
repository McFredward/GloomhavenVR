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
// The mesh is isReadable=false and carries NO CPU-readable / usable UVs (the Amplify
// shader derives them from vertex POSITION on the GPU). So this shader computes the UV
// on the GPU from the OBJECT-space vertex position: the parchment is a flat plane in its
// local X–Z (local bounds ~12.70 x 0.18 x 15.85 — Y is the thin axis), so
//     uv = (objPos.xz - _LocalMin.xz) / _LocalSize.xz
// maps the whole mesh to 0..1. Each of the four quadrant submeshes (GH_CampaignMap_01..04
// → NW/NE/SW/SE) carries its own 4096² albedo, so the per-material _UvScale=(2,2) +
// _UvOffset remap the mesh-wide 0..1 down to that quadrant's own 0..1 (uv*scale + offset).
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
        // The parchment mesh's LOCAL bounds (meshFilter.sharedMesh.bounds): min and size.
        // Only .xz are used (the plane); .y is the thin/normal axis.
        _LocalMin ("Local bounds min (xyz)", Vector) = (0,0,0,0)
        _LocalSize ("Local bounds size (xyz)", Vector) = (1,1,1,0)
        // Per-quadrant remap of the mesh-wide 0..1 UV to this submesh's own texture 0..1.
        _UvScale ("UV scale (xy)", Vector) = (1,1,0,0)
        _UvOffset ("UV offset (xy)", Vector) = (0,0,0,0)
        // Brightness multiplier (later tuning). Textures are DXT1/opaque, so alpha is forced 1.
        _Bright ("Brightness", Float) = 1
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
            };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 obj : TEXCOORD0; // object-space position (UV is derived from this)
            };

            sampler2D _MainTex;
            float4 _LocalMin;
            float4 _LocalSize;
            float4 _UvScale;
            float4 _UvOffset;
            float _Bright;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.obj = v.vertex.xyz; // pass OBJECT-space position through
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // Map the local X–Z plane to 0..1 over the whole mesh, then remap to this
                // submesh's quadrant.
                float2 uv = (i.obj.xz - _LocalMin.xz) / _LocalSize.xz;
                uv = uv * _UvScale.xy + _UvOffset.xy;
                fixed3 col = tex2D(_MainTex, uv).rgb * _Bright;
                // Force opaque — the quadrant textures are DXT1 (no meaningful alpha).
                return fixed4(col, 1.0);
            }
            ENDCG
        }
    }
}
