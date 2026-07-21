// Stereo-stable drop-in replacement for the game's hex-selection decal shader
// 'OmniDecal_Shd' (resources.assets pathId 556) — issue #6, "reflection swimming
// inside the white hex highlight", visible only in the RIGHT eye under multipass XR.
//
// WHY THE ORIGINAL SWIMS (ground truth: DXBC disassembly committed at
// tools/ShaderDisasm/evidence/OmniDecal_Shd.{vertex,fragment}.asm.txt):
// the original fragment shader reconstructs the shaded surface point SCREEN-SPACE:
//     uv    = screenPos.xy / screenPos.w
//     d     = tex2D(_CameraDepthTexture, uv).r
//     ndc   = float3(uv, 1-d) * 2 - 1
//     view  = unity_CameraInvProjection * ndc   (w-divide, z-flip)
//     world = unity_CameraToWorld * view
//     obj   = unity_WorldToObject * world       // hex pattern sampled HERE
// In the mod's MULTIPASS stereo the raster position is per-eye but the depth
// texture and the UnityPerCameraRare matrices (unity_CameraInvProjection /
// unity_CameraToWorld) bound during the RIGHT-eye pass are stale left-eye/mono
// state, so the reconstructed pattern lands differently per eye and per head
// pose (hardware clue: artifact only in the right eye = classic multipass
// staleness).
//
// WHAT THIS PORT DOES INSTEAD — the geometry the decal is drawn on is known:
// HexSelect_Control.HexProjector is the MeshRenderer of 'HexCenter_Proj', the
// builtin Cube mesh (unity default resources pathId 10202, vertices ±0.5) at
// local scale (2, 0.3, 2) sitting on the tile — a classic decal BOX rendered
// with Cull Front (back faces), NOT a flat quad. The hex pattern itself lives on
// the object-space y=0 plane of that box: the plane of the tile floor. The
// original's depth reconstruction, for every pixel that actually shows the flat
// tile floor (the entire visible highlight), produces exactly the intersection
// of the per-pixel view ray with that plane. So we compute THAT intersection
// analytically:
//     camObj = unity_WorldToObject * _WorldSpaceCameraPos      (per-eye correct)
//     dirObj = interpolated mesh object-space position - camObj (exact, no depth)
//     P      = camObj + dirObj * (-camObj.y / dirObj.y)         (ray ∩ plane y=0)
// EQUIVALENCE: for on-plane pixels the reconstructed world position equals the
// floor point the view ray hits, i.e. the same point our ray-plane intersection
// yields — identical input to all downstream algebra, up to depth-buffer
// quantization. (On a flat DECAL QUAD the interpolated surface position would
// itself already be that point; on this decal BOX the surface position is
// parallax-shifted off the floor plane, hence the explicit intersection.)
// Every input (_WorldSpaceCameraPos, unity_WorldToObject, interpolated
// position) is per-draw or per-eye-correct in multipass — NO depth-texture
// reads, NO screen-space UVs, NO UnityPerCameraRare matrices → inherently
// stereo-stable. Where the scene surface is NOT the tile plane (props, walls)
// the original draped the pattern over that geometry; this port keeps it on the
// tile plane — a minor, stable difference.
//
// The layer algebra below is ported INSTRUCTION-FOR-INSTRUCTION from the
// committed fragment disassembly (register→property mapping cross-checked
// against the serialized $Globals layout, the live material dump in
// .planning/debug/LogOutput.log:256, and numeric sanity: with the dumped
// _Offset=(0.59,0,0.59)/_Scale=0.86 the hex center P=0 maps to mask uv
// (0.507,0.507) ≈ texture center — confirming _Scale (not _ScaleB) is the uv
// scale and _ScaleB the radial-falloff scale):
//   * soft fill        : smoothstep(_BorderStep.z,.w, _HexMask.a) * _HexIntensity * 0.5
//   * crisp border     : edgeMask^20 * band(_BorderStep) * _BorderLineIntensity * 2 * step(0.3, mask.a)
//   * border flames    : _MainTex 8x8 flipbook (frame = round(frac(_Time.y*0.3125)*64))
//                        * edgeMask * smoothstep1 * _BorderFlameIntensity * 2
//   * target frame     : smoothstep(remap(_HexTargetFrame.a)) * _SinTime pulse
//                        * _CrossHair * _TargetFrameIntensity
//   * per-edge toggles : _HexMask.rgb half-ranges — upper half (2c-1) of R/G/B =
//                        NW/NE/E, lower half (1-2c) = SE/SW/W (opposite edges
//                        share a channel)
//   * radial falloff   : 1 - smoothstep((|P*_ScaleB| - _OmniMin)/(_OmniMax-_OmniMin))
//   * output           : pow(float4(I*_HexColour.rgb, I), 0.35), alpha blend
// Pass state from the serialized shader (scratch dump of m_State): Cull Front,
// ZWrite Off, Blend SrcAlpha OneMinusSrcAlpha, BlendOp Add, ColorMask RGBA,
// serialized ZTest Always (8). ZTest is exposed as [_VRZTest] (default 8 =
// vanilla) so the mod can flip it to LEqual without a bundle rebuild if
// through-wall bleed needs the occlusion treatment.
//
// Property NAMES match the original exactly: HexSelect_Control keeps calling
// ProjectorMaterialAdjustment() (SetColor/SetFloat/SetInt on these names) after
// the runtime shader swap, and Unity carries all matching property values
// (including textures) across Material.shader assignment.
//
// Bundle conventions: self-contained CG, UnityCG.cginc only (BoardLit/Overlay
// pattern) — a bundled shader referencing anything else is the pink-material trap.
Shader "GloomhavenVR/HexDecalStable"
{
    Properties
    {
        _HexMask ("HexMask", 2D) = "black" {}
        _HexColour ("HexColour", Color) = (0.702, 0.831, 0.808, 0.298)
        _Offset ("Offset", Vector) = (0.59, 0, 0.59, 0)
        _Scale ("Scale", Float) = 0.86
        _ScaleB ("ScaleB", Float) = 1.5
        _BorderStep ("BorderStep", Vector) = (0.4, 0.8, 0.1, 0.5)
        _MainTex ("Projector Texture", 2D) = "black" {}
        _BorderFlameIntensity ("BorderFlameIntensity", Float) = 0.6
        _BorderLineIntensity ("BorderLineIntensity", Float) = 0.6
        _HexIntensity ("HexIntensity", Float) = 0.5
        _OmniMin ("OmniMin", Float) = 0.54
        _OmniMax ("OmniMax", Float) = 1.0
        _NW_On ("NW_On", Float) = 1
        _NE_On ("NE_On", Float) = 1
        _E_On ("E_On", Float) = 1
        _SE_On ("SE_On", Float) = 1
        _SW_On ("SW_On", Float) = 1
        _W_On ("W_On", Float) = 1
        _HexTargetFrame ("TargetFrame", 2D) = "black" {}
        _TargetFrameIntensity ("TargetFrameIntensity", Float) = 0
        _CrossHair ("CrossHair", Float) = 0
        _HexRotation ("HexRotation", Float) = 0
        // Mod-side extra (not in the original): serialized vanilla ZTest is
        // Always (8); ShaderOcclusionPatcher would flip the ORIGINAL to LEqual
        // (4) in game data, which the user declined — so default to vanilla 8
        // and leave 4 reachable at runtime via SetFloat without game-data edits.
        _VRZTest ("ZTest", Float) = 8
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Pass
        {
            Name "Unlit"
            Cull Front
            ZWrite Off
            ZTest [_VRZTest]
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };
            struct v2f
            {
                float4 pos    : SV_POSITION;
                float3 objPos : TEXCOORD0; // interpolated object-space surface position
            };

            sampler2D _HexMask;
            sampler2D _MainTex;
            sampler2D _HexTargetFrame;
            fixed4 _HexColour;
            float4 _Offset;
            float4 _BorderStep;
            float _Scale, _ScaleB;
            float _BorderFlameIntensity, _BorderLineIntensity, _HexIntensity;
            float _OmniMin, _OmniMax;
            float _NW_On, _NE_On, _E_On, _SE_On, _SW_On, _W_On;
            float _TargetFrameIntensity, _CrossHair, _HexRotation;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex); // per-eye VP: stereo-correct
                o.objPos = v.vertex.xyz;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // ---- stable stand-in for the original's depth reconstruction ----
                // Per-pixel view ray ∩ decal-local plane y=0 (the tile floor plane).
                // _WorldSpaceCameraPos is per-eye in multipass; i.objPos is the exact
                // rasterized surface point — no screen-space inputs anywhere.
                float3 camObj = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1.0)).xyz;
                float3 dirObj = i.objPos - camObj;
                float denom = dirObj.y;
                // Guard the horizontal-ray singularity; sign-preserving epsilon.
                if (abs(denom) < 1e-5)
                    denom = (denom < 0.0) ? -1e-5 : 1e-5;
                float3 P = camObj + dirObj * (-camObj.y / denom);
                P.y = 0.0; // exact plane; original reconstructed y≈0 (floor) here
                // Rays that leave the box before reaching the plane land outside the
                // hex footprint — exactly like the original when the depth buffer held
                // floor beyond the box — and are killed by the same radial falloff.

                // ---- flipbook frame (asm lines 39-63): 8x8 grid, ~20 cells/s ----
                // frame = round(frac(_Time.y * 0.3125) * 64); u = col/8, v = (7-row)/8.
                float f = frac(_Time.y * 0.3125) * 64.0;
                float F = floor(f + 0.5);
                float fcol = F - 8.0 * floor(F * 0.125);
                float frow = floor(F * 0.125);
                frow -= 8.0 * floor(frow * 0.125);
                float2 flip = float2(fcol, 7.0 - frow) * 0.125;

                // ---- hex-local mask uv (asm lines 84, 89-112), ported verbatim ----
                // q = P + _Offset.xyz; a = ((float3x3)W2O * q).xz, per-axis rescaled by
                // the O2W row lengths (undoes the object scale baked into W2O, keeping
                // only the world-orientation twist so the mask stays board-aligned),
                // then rotated by (_HexRotation + 120)° around the _Offset.xz pivot and
                // scaled by _Scale straight into texture space. 0.01744444 is the
                // original's (slightly off) deg→rad constant — kept for parity.
                float3 q = P + _Offset.xyz;
                float2 a = mul((float3x3)unity_WorldToObject, q).xz;
                float2 axisScale = float2(length(unity_ObjectToWorld[0].xyz),
                                          length(unity_ObjectToWorld[2].xyz));
                a = a * axisScale - _Offset.xz;
                float ang = (_HexRotation + 120.0) * 0.01744444;
                float sn, cs;
                sincos(ang, sn, cs);
                float2 uvHex = (float2(a.x * cs + a.y * sn,
                                       -a.x * sn + a.y * cs) + _Offset.xz) * _Scale;
                float2 uvFlame = uvHex * 0.125 + flip; // one flipbook cell per hex

                // ---- texture taps (asm lines 113-115) ----
                float flame = tex2D(_MainTex, uvFlame).a;        // flame flipbook alpha
                float4 mask = tex2D(_HexMask, uvHex);            // rgb: edges, a: fill/band
                float tfA   = tex2D(_HexTargetFrame, uvHex).a;   // target frame alpha

                // ---- per-edge toggle mask (asm lines 118-139) ----
                // _HexMask.rgb encodes 6 edges in 3 channels: upper half (2c-1) of
                // R/G/B = NW/NE/E, lower half (1-2c) = SE/SW/W. Toggles gate with a
                // "!= 0" test (any nonzero enables), then smoothstep + sum of squares.
                float3 c3 = mask.rgb - 0.5;
                float3 hi = saturate(c3 * 2.0);
                float3 lo = saturate(c3 * -2.0);
                hi = hi * hi * (3.0 - 2.0 * hi);
                lo = lo * lo * (3.0 - 2.0 * lo);
                hi *= float3(_NW_On != 0.0 ? 1.0 : 0.0,
                             _NE_On != 0.0 ? 1.0 : 0.0,
                             _E_On  != 0.0 ? 1.0 : 0.0);
                lo *= float3(_SE_On != 0.0 ? 1.0 : 0.0,
                             _SW_On != 0.0 ? 1.0 : 0.0,
                             _W_On  != 0.0 ? 1.0 : 0.0);
                float edge = min(dot(hi, hi) + dot(lo, lo), 1.0);

                flame *= edge;                 // flames only on enabled edges (line 140)
                float ring = pow(edge, 20.0);  // crisp border ring (lines 141-143)

                // ---- border band from mask alpha (asm lines 144-153) ----
                float s1 = saturate((mask.a - _BorderStep.z) / (_BorderStep.w - _BorderStep.z));
                float s2 = saturate((mask.a - _BorderStep.x) / (_BorderStep.y - _BorderStep.x));
                float ss1 = s1 * s1 * (3.0 - 2.0 * s1);
                float ss2 = s2 * s2 * (3.0 - 2.0 * s2);
                float band = saturate(ss1 - ss2);
                float aGate = mask.a >= 0.3 ? 1.0 : 0.0;

                // ---- layer accumulation (asm lines 154-170) ----
                float borderLine = ring * band * _BorderLineIntensity * aGate * 2.0;
                float flameLayer = flame * ss1 * _BorderFlameIntensity * 2.0;
                float fill       = ss1 * _HexIntensity * 0.5;
                float tf = saturate((tfA - 0.1) * 1.666667);
                float tfSS = tf * tf * (3.0 - 2.0 * tf);
                float pulse = 1.0 - abs(_SinTime.w);
                float pulseTerm = (1.0 - pulse * pulse) * 0.7 + 0.3;
                float targetLayer = tfSS * pulseTerm * _CrossHair * _TargetFrameIntensity;
                float total = flameLayer + fill + borderLine + targetLayer;

                // ---- radial ("omni") falloff (asm lines 85-88, 171-178) ----
                float d = saturate((length(P * _ScaleB) - _OmniMin) / (_OmniMax - _OmniMin));
                total *= max(1.0 - d * d * (3.0 - 2.0 * d), 0.0);

                // ---- receiving-surface term (asm lines 179-192) ----
                // The original modulates by a normal fetched from _CameraNormalsTexture
                // — which NOTHING in the game ever binds (built-in RP), so the sample
                // is the black/gray fallback, the transformed normal saturates to 0 and
                // the term is the CONSTANT smoothstep(0)*0.5+0.5 = 0.5. Reproduced as a
                // constant to match the actual vanilla rendering.
                total *= 0.5;

                // ---- output (asm lines 193-197): gamma-ish lift, straight alpha ----
                float4 outc = float4(total * _HexColour.rgb, total);
                return pow(max(outc, 0.0), 0.35);
            }
            ENDCG
        }
    }
}
