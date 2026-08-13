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
// serialized ZTest Always (8).
//
// OCCLUSION (deliberate departure from vanilla): ZTest Always made the highlight
// draw THROUGH walls and figures. This port instead depth-tests at the TRUE
// shaded point — the fragment shader already knows the floor point P it shades,
// so it exports P's device depth via SV_Depth (clip.z/clip.w of P; on
// UNITY_REVERSED_Z platforms — D3D11, our target — that raw value is already in
// depth-buffer space, on GL-style platforms it is NDC [-1,1] and gets remapped
// to [0,1]) and tests it with ZTest [_VRZTest], default 4 = LEqual. (Since
// ModBuild 135 that comparison lives in the ColorMask-0 prepass and reaches the
// colour pass through one stencil bit — see the SLAB TEST section below for why.
// The algebra of the test itself is unchanged.) Why not a
// plain LEqual on the rasterized fragments: the mesh is a Cull-Front BOX whose
// visible fragments are its far faces BELOW/BEHIND the floor — their own raster
// depth would fail against the floor's depth buffer and z-kill the entire
// visible highlight when looking down. With the exported depth the hardware
// tests P (on the floor plane) instead, for every fragment — top, bottom and
// SIDE faces of the box all export the depth of the same floor intersection, so
// the highlight behaves as one consistent depth surface. Consequences:
//   * figures standing on the hex (opaque queues 2000-2500, depth already
//     written when this queue-4000 decal draws) occlude it automatically;
//   * walls between camera and hex hide it;
//   * ZWrite Off stays — SV_Depth participates in the depth TEST while the
//     depth WRITE remains masked (D3D11 output-merger: an exported oDepth
//     replaces the interpolated depth for the comparison; DepthWriteMask
//     independently gates the write), so the transparent decal never pollutes
//     the depth buffer.
// [_VRDepthBias] nudges the exported depth a hair toward the camera so the
// decal never z-fights the tile floor itself, which lies exactly on the y=0
// plane we shade (floor mesh depth comes from a different vertex pipeline →
// low-order-bit mismatch → 50% speckle without the bias). Cost of exporting
// depth: early-z is disabled for this draw — negligible for one small decal.
// _VRZTest stays material-driven so on-device experiments (8 = vanilla
// draw-through) need no bundle rebuild; HexHighlightFix sets and logs it.
//
// RECEIVING-SURFACE SLAB TEST (ModBuild 132 finding 5, "das Hex feld … ist auch
// UNTER dem Spielbrett auf dem Boden drauf zu sehen"; still leaking in 134,
// "Ich habe kurz wieder diesen Rand eines tiles auf dem Boden darunter sehen
// können"). The occlusion scheme above is ONE-SIDED: exporting depth(P) with
// ZTest LEqual rejects pixels whose real surface is NEARER than the tile plane
// (walls, figures) but happily keeps every pixel whose real surface is FARTHER.
// Until ModBuild 132 "farther" meant the game's black sky sphere and the leak
// was invisible; the mod now spawns a room whose FLOOR sits far below the board,
// so wherever the decal box's screen footprint sees past the board (its overhang
// at a board edge, gaps between tiles, grazing views under the slab) the hex
// pattern — most visibly its outline ring — is painted onto that floor. This is
// inherent to shading the ray∩plane point: for such a pixel P is still a
// perfectly good point inside the hex; only the surface the player actually SEES
// there is wrong, and the shader has no way to know that from geometry alone.
//
// The missing datum is the SCENE DEPTH at the pixel, and the required predicate
// is a SLAB, not a half-space: the receiving surface must lie between the decal
// plane (object y = 0) and a floor plane a tolerance below it (object
// y = -tol) — the volume the decal box itself was authored to cover.
//     keep  iff   the scene surface at this pixel is
//                 at-or-behind  ray∩{y=0}      (near bound: nothing occludes)
//                 AND at-or-nearer ray∩{y=-tol} (far bound: it IS the tile)
// Two comparisons; the output merger offers exactly one per pass. Both possible
// sources for a second one were weighed:
//   * _CameraDepthTexture (a software compare, single pass) — REJECTED. This
//     project's head camera runs FORWARD with depthTextureMode driven by
//     [Optimize] HeadDepthPrepass, whose default is FALSE
//     (Defaults.Core.cs: `HeadDepthPrepass = false`), i.e. NO depth texture is
//     generated in a default install; an unbound sampler would make the test
//     read garbage. Turning it on is not a free flag either: on the forward
//     path Unity builds that texture by re-rendering every opaque object
//     through its shadow-caster pass — a full extra scene submission PER EYE
//     PASS (four per frame under MultiPass). Paying a whole scene submission to
//     fix a decal is the wrong trade, and it would re-introduce exactly the
//     depth-texture dependency this port was written to remove.
//   * The DEPTH BUFFER itself, via a second hardware test in a second pass —
//     CHOSEN. It always exists, it already contains the tile plane (that is
//     precisely why _VRDepthBias has to exist), and it costs one extra
//     ColorMask-0 draw per decal instead of a scene submission.
// Both bounds are ray∩plane intersections, so both are EXACT at every view
// angle: the reference point for the far bound is the point where THIS pixel's
// view ray crosses y = -tol, i.e. the very point the depth buffer would hold if
// the receiver were a surface exactly one tolerance below the tile. Nothing is
// measured along the ray and nothing is converted to world metres, so there is
// no 1/sin(elevation) blow-up at grazing angles and no coupling to the board's
// zoom scale — the two failure modes the ModBuild 133 formulation (push P away
// from the camera by _VRSurfaceTolerance WORLD METRES along the view ray) had.
//
// WHICH BOUND RUNS IN WHICH PASS — the whole robustness argument (changed in
// ModBuild 135; 133 had them the other way round):
//   * pass "SurfaceOcclusion" (ColorMask 0) carries the NEAR bound. It exports
//     depth(ray∩{y=0}) biased toward the camera and runs ZTest [_VRZTest]
//     (LEqual). Depth PASS = nothing occludes → stencil op Zero (clear
//     [_VRStencilBit]); depth FAIL = a wall/figure is in front → ZFail Replace
//     (set the bit). Comp Always, so EVERY pixel the pass covers is written:
//     whatever the bit held before this draw is irrelevant by construction.
//   * pass "Unlit" (colour) carries the FAR bound as its own hardware test:
//     it exports depth(ray∩{y=-tol}) and runs a LITERAL ZTest GEqual, which
//     passes only where the scene surface is at-or-nearer than the tolerance
//     plane. Its stencil gate is Comp Equal against Ref 0 — draw where the
//     prepass said "unoccluded" — and every op is Zero, so our bit is left
//     clear and the stencil buffer is handed back as it was found.
// The point of that split: the bound that fixes the REPORTED bug (the floor
// leak) lives in the colour pass and needs NO stencil at all. Every way the
// stencil handshake can fail — a render target with no stencil attachment
// (D3D11 then treats the test as always-pass), a foreign draw that fakes the
// bit, [_VRStencilBit] reading 0 — degrades this shader to "vanilla
// draw-through that still stops at the tile", never to a floor leak and never
// to a vanished or flickering highlight. 133 had the opposite polarity, so the
// same failures reinstated the leak exactly as reported.
//
// STATE: none. Every input is a per-draw uniform or this frame's depth buffer;
// the stencil bit is written unconditionally over the prepass's own coverage
// and consumed and cleared by the very next pass of the SAME renderer (the
// built-in pipeline draws a renderer's passes back to back, so nothing can be
// interleaved between them). No value survives a frame, a camera or an object.
//
// Properties, both live-tunable so an on-device bisect needs no bundle rebuild:
//   _VRSurfaceTolerance — thickness of the accepted slab in DECAL OBJECT UNITS
//     below the pattern plane (ModBuild 135 unit change; 133 counted world
//     metres along the view ray). Default 0.5 = the decal box's own bottom face:
//     the builtin Cube spans ±0.5, so the receiving surface must lie inside the
//     volume the game itself authored around this tile. That is scale-free — it
//     rides the board's zoom, the mod's world scale and the room's placement
//     automatically — and at the shipped local scale (2, 0.3, 2) it is 0.15
//     world units of vertical slack against a board-to-room-floor drop of ~23
//     world units, a ~150x margin, while still clearing the sub-centimetre
//     mismatch between the analytic y=0 plane and the tile mesh rasterised
//     there. Values <= 0 (or NaN) fall back to that default rather than pinning
//     the far plane onto the tile and z-fighting it. To DISABLE the far bound
//     without a bundle rebuild set it huge (1e9): the tolerance plane then sits
//     past the far clip, GEqual always passes, and the shader is back to
//     ModBuild 131 behaviour.
//   _VRStencilBit — which stencil bit the prepass borrows to hand the near
//     bound to the colour pass, default 128 (the top bit; the built-in FORWARD
//     path this camera uses does not reserve it, and UGUI's nested masks grow
//     from bit 0 upward). 0 makes ReadMask/WriteMask 0, the Equal-vs-0 compare
//     degenerates to "always" and OCCLUSION switches off — the rebuild-free
//     kill switch, identical in effect to _VRZTest = 8. The Ref-0 polarity is
//     deliberate: on a target whose stencil reads back as 0 the compare still
//     passes, so a missing stencil attachment can never blank the highlight.
// What the slab test deliberately does NOT do: it is indifferent to WHERE
// inside the hex a fragment sits, so it cannot inset the outline from the tile
// edge, and two neighbouring decals resting on the same tile plane get the same
// verdict for every shared-border pixel — multi-hex ranges stay continuous.
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
        // Mod-side extras (not in the original). _VRZTest: vanilla serialized
        // ZTest Always (8) drew the highlight through walls and figures; with
        // the per-pixel SV_Depth export (see header) the default is now 4 =
        // LEqual so the highlight respects occlusion. It drives the OCCLUSION
        // (near-bound) prepass; 8 restores vanilla draw-through at runtime via
        // SetFloat — HexHighlightFix applies the configured value on every
        // swap. The colour pass's far bound is a literal GEqual and stays on.
        _VRZTest ("ZTest", Float) = 4
        // Camera-ward bias (depth-buffer space) added to the occlusion prepass's
        // exported depth so it never z-fights the tile floor the decal lies on.
        // ~2e-4 ≈ a few mm at typical viewing distance under reversed-Z;
        // runtime-tunable. Values <= 0 fall back to the default (a zero bias
        // speckles the occlusion verdict, i.e. flickers the highlight).
        _VRDepthBias ("Depth bias", Float) = 0.0002
        // Receiving-surface slab test (see header): how far BELOW the pattern
        // plane, in DECAL OBJECT UNITS, the real surface may lie and still
        // receive the pattern. 0.5 = the decal box's own bottom face, i.e. the
        // volume the game authored around this tile — scale-free, so board zoom
        // and world scale cannot erode it. <= 0 falls back to that default;
        // 1e9 disables the far bound (rebuild-free kill switch).
        _VRSurfaceTolerance ("Surface tolerance (decal units)", Float) = 0.5
        // Stencil bit the occlusion prepass borrows to hand its verdict to the
        // colour pass. 0 = occlusion off (kill switch, same as _VRZTest = 8);
        // the far bound that stops the leak does not use it.
        _VRStencilBit ("Occlusion handshake stencil bit", Float) = 128
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" }

        // Shared between the band prepass and the colour pass: identical vertex
        // program (so both rasterise pixel-identical coverage) and the identical
        // ray∩plane reconstruction (so both talk about the same point P).
        CGINCLUDE
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
        float _VRDepthBias;
        float _VRSurfaceTolerance;

        v2f vert (appdata v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex); // per-eye VP: stereo-correct
            o.objPos = v.vertex.xyz;
            return o;
        }

        // ---- stable stand-in for the original's depth reconstruction ----
        // Per-pixel view ray ∩ decal-local plane y = planeY. planeY = 0 is the
        // tile floor plane (where the hex pattern lives); the slab test's far
        // bound uses the same routine with planeY = -tolerance, so both bounds
        // are exact ray∩plane points and share every rounding decision.
        // _WorldSpaceCameraPos is per-eye in multipass; objPos is the exact
        // rasterized surface point — no screen-space inputs anywhere.
        // Returns P in object space and hands back the object-space ray direction
        // (unnormalised, camera → surface) for callers that need it.
        float3 HexPlanePoint (float3 objPos, float planeY, out float3 dirObj)
        {
            float3 camObj = mul(unity_WorldToObject, float4(_WorldSpaceCameraPos, 1.0)).xyz;
            dirObj = objPos - camObj;
            float denom = dirObj.y;
            // Guard the horizontal-ray singularity; sign-preserving epsilon.
            if (abs(denom) < 1e-5)
                denom = (denom < 0.0) ? -1e-5 : 1e-5;
            float3 P = camObj + dirObj * ((planeY - camObj.y) / denom);
            P.y = planeY; // exact plane; original reconstructed y≈0 (floor) here
            // Rays that leave the box before reaching the plane land outside the
            // hex footprint — exactly like the original when the depth buffer held
            // floor beyond the box — and are killed by the same radial falloff.
            return P;
        }

        // Slab thickness in decal object units. A material that never had the
        // property written (an un-seeded property sheet, a hand-edited 0, a NaN)
        // must not pin the far plane onto the tile and z-fight it, so anything
        // that is not strictly positive falls back to the shipped default = the
        // decal box's own bottom face. The "> 0" form also catches NaN, which
        // compares false against everything and would otherwise poison the depth.
        float HexSlabTolerance ()
        {
            return (_VRSurfaceTolerance > 0.0) ? _VRSurfaceTolerance : 0.5;
        }

        // clip.z/w in the platform's DEPTH-BUFFER convention. On UNITY_REVERSED_Z
        // (D3D11, our target) that raw value is already depth-buffer space (1 = near);
        // on GL-style platforms it is NDC [-1,1] and gets remapped to [0,1].
        // `nearBias` is always applied TOWARD the camera, whatever the convention.
        float HexDeviceDepth (float4 clipPos, float nearBias)
        {
#if defined(UNITY_REVERSED_Z)
            return saturate(clipPos.z / clipPos.w + nearBias);
#else
            return saturate((clipPos.z / clipPos.w) * 0.5 + 0.5 - nearBias);
#endif
        }

        // Depth-buffer values for "as near as possible" / "as far as possible",
        // used to force a degenerate ray (plane intersection behind the camera)
        // to FAIL whichever comparison the calling pass runs.
#if defined(UNITY_REVERSED_Z)
        #define HEX_DEPTH_NEAR 1.0
        #define HEX_DEPTH_FAR  0.0
#else
        #define HEX_DEPTH_NEAR 0.0
        #define HEX_DEPTH_FAR  1.0
#endif
        ENDCG

        // ---------------------------------------------------------------------
        // Pass 0 — OCCLUSION PREPASS (near bound; see header). Colour-less and
        // depth-write-less: its only product is [_VRStencilBit], SET where
        // something (wall, figure, prop) stands between the camera and the tile
        // plane and CLEARED where nothing does. It exports the depth of the true
        // shaded point P = ray∩{y=0}, nudged toward the camera by _VRDepthBias so
        // it never z-fights the tile it lies on, and runs ZTest [_VRZTest]
        // (LEqual) — exactly the test the ModBuild 132 colour pass ran, moved
        // here. Comp Always + Zero/Replace means every covered pixel is written,
        // so no earlier stencil content can survive into the verdict. Cull and
        // vertex program match the colour pass, so coverage is pixel-identical.
        // ---------------------------------------------------------------------
        Pass
        {
            Name "SurfaceOcclusion"
            Cull Front
            ZWrite Off
            ZTest [_VRZTest]
            ColorMask 0
            Blend Off
            Stencil
            {
                Ref       [_VRStencilBit]
                WriteMask [_VRStencilBit]
                Comp  Always
                Pass  Zero      // depth test passed -> nothing in front -> clear
                ZFail Replace   // depth test failed -> occluded        -> set
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragOcclusion
            #pragma target 3.0

            float4 fragOcclusion (v2f i, out float oDepth : SV_Depth) : SV_Target
            {
                float3 dirObj;
                float3 P = HexPlanePoint(i.objPos, 0.0, dirObj);
                float4 clipP = UnityObjectToClipPos(float4(P, 1.0));

                // A zero/negative bias would let the prepass z-fight the tile and
                // speckle the occlusion verdict — a flickering highlight, the one
                // failure this shader must never produce. Fall back to the default.
                float bias = (_VRDepthBias > 0.0) ? _VRDepthBias : 0.0002;

                // Degenerate rays (intersection behind the camera) must FAIL the
                // LEqual test rather than pass it, so pin them at the FAR plane;
                // ZFail then marks them occluded and the colour pass drops them.
                oDepth = (clipP.w <= 1e-6) ? HEX_DEPTH_FAR : HexDeviceDepth(clipP, bias);
                return 0.0; // ColorMask 0 — never reaches the render target
            }
            ENDCG
        }

        // ---------------------------------------------------------------------
        // Pass 1 — COLOUR. Two gates, in this order of importance:
        //  * ZTest GEqual (literal, always on) against the exported depth of
        //    ray∩{y = -tolerance} — the FAR bound. This is the one that stops the
        //    pattern from landing on the room floor, and it needs no stencil, so
        //    it survives every way the handshake below can fail.
        //  * Stencil Comp Equal vs Ref 0 — draw only where the prepass found
        //    nothing occluding. Every op is Zero, so our bit is left clear and
        //    the stencil buffer is handed back to the rest of the frame exactly
        //    as it was found. Ref 0 (not the bit) is deliberate: on a target
        //    whose stencil reads back as 0 — or with _VRStencilBit = 0, which
        //    masks both sides to 0 — the compare still passes, so the degenerate
        //    case is "occlusion off", never a blanked highlight.
        // ---------------------------------------------------------------------
        Pass
        {
            Name "Unlit"
            Cull Front
            ZWrite Off
            ZTest GEqual
            Blend SrcAlpha OneMinusSrcAlpha
            Stencil
            {
                Ref      0
                ReadMask [_VRStencilBit]
                WriteMask [_VRStencilBit]
                Comp  Equal
                Pass  Zero
                Fail  Zero
                ZFail Zero
            }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            fixed4 frag (v2f i, out float oDepth : SV_Depth) : SV_Target
            {
                float3 dirObj;
                float3 P = HexPlanePoint(i.objPos, 0.0, dirObj);

                // ---- per-pixel depth of the SLAB FLOOR (far bound) ----
                // The rasterized fragment sits on the box's far/side faces, but the
                // pixel visually shows the plane point P. What we hand the output
                // merger is the point where this same view ray crosses the bottom of
                // the accepted slab, y = -tolerance; ZTest GEqual then passes only
                // where the real scene surface is at-or-nearer than that, i.e. where
                // the pixel really shows this tile and not the room floor tens of
                // units below, a wall behind, or the empty void (an untouched depth
                // buffer holds the far plane, which fails GEqual — nothing is painted
                // where nothing was drawn). Because both planes are hit by the SAME
                // ray, the test is exact at every view angle: no along-ray metric, no
                // 1/sin(elevation) blow-up, no dependence on the board's zoom scale.
                // Occlusion by figures and walls is the prepass's job (stencil).
                // ZWrite Off keeps the buffer untouched (SV_Depth feeds the TEST, the
                // masked WRITE stays off — see header). Degenerate rays (intersection
                // behind the camera → clip.w<=0) are pinned to the NEAR plane so they
                // FAIL GEqual; their colour is already killed by the radial falloff.
                float3 dirFar;
                float3 PFar = HexPlanePoint(i.objPos, -HexSlabTolerance(), dirFar);
                float4 clipFar = UnityObjectToClipPos(float4(PFar, 1.0));
                oDepth = (clipFar.w <= 1e-6) ? HEX_DEPTH_NEAR : HexDeviceDepth(clipFar, 0.0);

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
