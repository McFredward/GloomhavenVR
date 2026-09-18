using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using UnityEngine;

namespace GloomhavenVR.Core;

/// <summary>
/// Mixed-reality / chroma-key mode (hardware test #22 item 7). When ON, the whole
/// SKY/BACKGROUND turns a flat, solid KEY COLOR (default green) so Virtual Desktop can
/// chroma-key it and composite the game over the real room — the diorama/table geometry
/// keeps rendering, only the sky/background becomes the flat key color the compositor
/// punches out.
///
/// HOW THE SKYBOX IS DISABLED WHILE THE DIORAMA STAYS VISIBLE
/// ----------------------------------------------------------
/// In VR the ONLY camera that reaches the HMD is the rig's own HeadCamera
/// (<see cref="VRCameraPolicy.AllowedHead"/>) — every game camera is forced to
/// StereoTargetEyeMask.None by <see cref="VRCameraPolicy"/>. So the passthrough result is
/// decided entirely by the HeadCamera: force its clear to SolidColor(key) and the sky is
/// gone while all the opaque diorama geometry in its culling mask still renders. We also
/// null <see cref="RenderSettings.skybox"/> (re-asserted every tick — StaticAmbience can
/// re-set it) so ambient/reflection sky contributions vanish too, and sweep every OTHER
/// live camera whose clear is still <see cref="CameraClearFlags.Skybox"/> over to
/// SolidColor(key) so the desktop mirror keys identically and nothing sky-clears anywhere.
///
/// PRECEDENCE (documented, keyed off the MR flag)
/// - HeadCamera: MR owns its clear WHILE ON. <see cref="Tick"/> is the LAST entry
///   (<c>"Rig.MixedReality"</c>) of <c>VRRigDriver</c>'s per-frame tail-step array, i.e. it runs
///   AFTER that array's own TickHeadClearColor step, so MR's key color wins the frame
///   (TickHeadClearColor only pins backgroundColor when clearFlags==SolidColor, then MR
///   overrides it). When MR turns OFF the head clear is restored and TickHeadClearColor
///   resumes its VoidColor management.
/// - Game cameras: MR only ever touches cameras whose clear is STILL Skybox. FlatScreen's
///   TickStackClears drives its RT-captured cameras to SolidColor/Depth for compositing —
///   those are already non-Skybox, so MR never writes the same camera in the same frame;
///   FlatScreen keeps authority over its captured cameras (they feed the flat window, not
///   passthrough).
/// - <see cref="VRCameraPolicy"/> is untouched (stereoTargetEye only) — it is precisely
///   what guarantees only the HeadCamera reaches the HMD, which is why keying the
///   HeadCamera is sufficient for passthrough.
///
/// SKY/BACKGROUND GEOMETRY (hardware test #23 item 2)
/// -------------------------------------------------
/// Keying the HeadCamera clear + nulling the skybox is NOT enough on its own: the scenario
/// background is drawn as OPAQUE MESH GEOMETRY, not a skybox. The scenario environment is
/// procedurally generated (Apparance), the HeadCamera renders the scenario camera's full
/// culling mask (0xFFFFFFFF — verified in the test-#23 rig-build log), and a
/// backdrop/skydome mesh in that mask draws OVER the SolidColor key. No code sets the
/// scenario cameras' clearFlags/backgroundColor and there is no per-camera Skybox
/// component (verified: <c>StaticAmbience.Apply</c> is the ONLY writer of
/// <c>RenderSettings.skybox</c>), so no clear-flag/skybox patch can remove it. We therefore
/// SWEEP the live renderers and disable the ones that draw the surrounding sky: any renderer
/// whose material/shader/name reads sky-ish, OR whose world bounds SURROUND the head with a
/// large extent on all three axes (a dome/sphere/backdrop box — never a flat floor tile or a
/// table prop). Each disabled renderer is recorded and re-enabled on restore; the diorama
/// tiles/props (which never enclose the head) stay visible. Every hidden renderer is logged
/// by name/layer/bounds/shader, and when the sweep hides nothing the biggest enclosing
/// candidates are dumped, so the next hardware run can name the real sky source.
///
/// Idempotent; originals (per camera + the skybox material + disabled sky renderers) are
/// recorded on first force and RESTORED fully on MR-off, VR-stop, or hot reload
/// (<see cref="RestoreAll"/>).
/// </summary>
internal static partial class MixedReality
{
    private static ConfigFile? _file;

    /// <summary>MR mode master (settings-panel toggle binds this).</summary>
    internal static ConfigEntry<bool> Enabled = null!;

    /// <summary>Chroma key color the sky/background clears to (default solid green).</summary>
    internal static ConfigEntry<Color> KeyColor = null!;

    /// <summary>Sweep + disable the sky/background GEOMETRY (item 2). Safety valve — default on.</summary>
    internal static ConfigEntry<bool> HideSkyMeshes = null!;

    /// <summary>Give ALL "unseen" fog-of-war geometry (the face-down preview tile stacks AND the
    /// unseen-area hexes inside revealed tiles) an opaque dark UNDERLAY while MR is on, so the
    /// passthrough/key can no longer show through it (user rulings 2026-08-04 + 2026-08-05).
    /// Safety valve like <see cref="HideSkyMeshes"/> — default on. The key keeps its original
    /// name from the preview-stack-only round; its scope has grown, its cfg identity has not.</summary>
    internal static ConfigEntry<bool> OpaquePreviewTiles = null!;

    /// <summary>XZ widening of each piece's GROOVE FILL copy (round 7 semantics — the round-4
    /// "skirt on the underlay itself" is gone: round 5 proved the animation is UV-scroll, which
    /// cannot leave the mesh silhouette, and a DISPLACED primary copy stops sitting coplanar
    /// behind the beveled groove faces, which round 7's screenshot exposed as green channels.
    /// The primary underlay is exact 1:1 again; this factor widens only the lowered fill copy so
    /// neighboring fills overlap under the groove line). Tunable live (a change rebuilds).</summary>
    internal static ConfigEntry<float> UnseenSkirtScale = null!;

    /// <summary>World-units the gap-backing WAFER sits below each piece's mesh-top plane.
    /// FRESH KEY (round 12): this was '[MixedReality] UnseenFillDrop' until ModBuild 69 — that
    /// key's persisted value (0.35, from the round-7 "deep groove fill" semantics) survived the
    /// round-11 default change and re-opened the canyon the wafer exists to close (log:
    /// "wafer = mesh-top − 0.35 wu"; MAPTILE dumps: fills at y−0.3 under tops at −0.1).
    /// Renaming is the established clean path when a key's SEMANTICS change: the new key binds
    /// fresh at 0.02, the orphaned old entry is harmless and never read. Tunable live. Too
    /// small = z-fighting with the hex tops; too large = seams reopen at shallow angles.</summary>
    internal static ConfigEntry<float> UnseenWaferDrop = null!;

    /// <summary>World-units the RIM CURTAIN (round 15) sits INSIDE each unseen piece's authored
    /// vertical side faces. The curtain is a mod-BUILT opaque dark prism — see
    /// <see cref="MrRimCurtain"/> for why it cannot be another same-mesh copy. Too small = the
    /// curtain can protrude through a concave/damaged authored side (a dark spike past the
    /// silhouette); too large = the leak band at the very outer silhouette edge widens. It cannot
    /// z-fight in either direction: the curtain writes no depth and draws at queue 2500, before
    /// the family's own depth-writing pass at 3000. Tunable live (a change rebuilds every
    /// backing).</summary>
    internal static ConfigEntry<float> UnseenRimInset = null!;

    /// <summary>World-units the RIM CURTAIN's top cap sits BELOW each piece's mesh-top plane.
    /// INVARIANT (enforced in <see cref="BuildUnseenUnderlay"/>): strictly greater than
    /// <see cref="UnseenWaferDrop"/>, so the curtain always hides behind the user-approved wafer
    /// and can never paint over an authored top face. Tunable live.</summary>
    internal static ConfigEntry<float> UnseenRimTopClearance = null!;

    /// <summary>DIAGNOSTIC (round 16, default OFF): render each class of mod-built backing in a
    /// distinct flat colour instead of the dark neutral — coplanar underlay BLUE, top wafer
    /// MAGENTA, rim curtain RED. Same geometry, same render queue, same blend/depth state, same
    /// layer: ONLY the colour changes, so the screenshot observes the real pipeline rather than a
    /// special case. It exists because fifteen rounds of dark backings produced no change at the
    /// rim while every instrument reported the geometry present — if none of the three colours
    /// appears anywhere in MR, the backings provably do not reach the screen and the whole
    /// strategy is dead; if they appear but not on the glowing cliff, the cliff is geometry the
    /// sweep never matched. Live (a change rebuilds every backing).</summary>
    internal static ConfigEntry<bool> UnseenBackingDebugColors = null!;

    /// <summary>ROUND 16 — the REGION-MEMBERSHIP route (default on, safety valve in the shape of
    /// <see cref="HideSkyMeshes"/> / <see cref="OpaquePreviewTiles"/>). After the family/tag sweep
    /// has produced its matched set, every OTHER mesh renderer standing inside one of those pieces'
    /// AABBs — below the piece's own top plane, not a figure, not mod-owned, not oversized — is
    /// backed too, regardless of shader, name, queue, RenderType tag or Preview depth. It exists
    /// because sixteen rounds of widening MATERIAL predicates never caught 'Simple Tile', the one
    /// authored piece per hex that spans the block's full height (the cliff the user photographs);
    /// asking "does this renderer stand inside the fog-of-war region" is a question the geometry can
    /// answer, where "does this material look see-through" is one a hardcoded-blend pass answers
    /// wrongly. Turn OFF if a run shows it darkening wanted geometry — the log names everything it
    /// adopted.</summary>
    internal static ConfigEntry<bool> UnseenRegionMembership = null!;


    /// <summary>
    /// Key-colour presets offered by the settings UI.
    ///
    /// <para>Green, magenta and blue are the chroma keys a compositor expects — saturated colours
    /// no game pixel is likely to share. BLACK is not a chroma key at all and is here for the other
    /// use: a headset whose passthrough composites on black, and players who simply want the void
    /// dark rather than lurid. It is last because picking it turns the chroma workflow off in
    /// everything but name.</para>
    /// </summary>
    private static readonly (string Name, Color Color)[] Presets =
    {
        ("Green", new Color(0f, 1f, 0f, 1f)),
        ("Magenta", new Color(1f, 0f, 1f, 1f)),
        ("Blue", new Color(0f, 0f, 1f, 1f)),
        ("Black", new Color(0f, 0f, 0f, 1f)),
    };

    /// <summary>Preset names, in offer order — the dropdown's option list.</summary>
    internal static string[] KeyColorNames
    {
        get
        {
            var names = new string[Presets.Length];
            for (int i = 0; i < Presets.Length; i++)
                names[i] = Presets[i].Name;
            return names;
        }
    }

    /// <summary>Index of the current key colour among the presets, or -1 for a custom one.</summary>
    internal static int KeyColorIndex
    {
        get
        {
            Color c = KeyColor.Value;
            for (int i = 0; i < Presets.Length; i++)
            {
                if (Approximately(Presets[i].Color, c))
                    return i;
            }
            return -1;
        }
    }

    /// <summary>Pick a preset by index — what a dropdown needs, where cycling needed no index.</summary>
    internal static void SetKeyColor(int index)
    {
        Bind();
        if (index < 0 || index >= Presets.Length)
            return;
        KeyColor.Value = Presets[index].Color; // BepInEx persists on set
    }

    // Recorded originals for full restore.
    private static readonly Dictionary<Camera, (CameraClearFlags Flags, Color Bg)> CamOriginals = new();
    private static readonly List<Camera> Scratch = new(8);
    private static Material? _savedSkybox;
    private static bool _skyboxSaved;

    // "Unseen" fog-of-war geometry backed by an opaque dark UNDERLAY while MR is on (user rulings
    // 2026-08-04 "die Stapel noch nicht entdeckter Räume sollen in MR nicht transparent sein" +
    // 2026-08-05 "die Kacheln, die das noch nicht entdeckte Gebiet markieren, sehen in MR aus wie
    // grünes Glas — gleicher Look, gleiche Animation, aber keine Transparenz"). Each entry is one
    // matched source renderer plus the mod-owned underlay CHILD cloned from its mesh; the child
    // dies with its source (Apparance regenerates tile content constantly — the ModBuild-57 run
    // accumulated 226 overrides in one session), so teardown is structural, not bookkept.
    // See ForceUnseenOpaque for the full derivation.
    private sealed class UnseenUnderlay
    {
        public Renderer Source = null!;
        public int SourceId;          // GetInstanceID at build time (fast dedup-set removal)
        public Renderer Plate = null!; // the underlay's own MeshRenderer, child of Source

        /// <summary>The piece's GAP BACKING WAFER (round 11 shape; born round 7 as the deep
        /// groove fill): a second same-mesh copy, mildly XZ-widened, Y-squashed flat and seated
        /// at the piece's mesh-top plane minus <see cref="UnseenWaferDrop"/>, so a ray into a
        /// seam between neighboring hexes lands on dark instead of the key. A child of the
        /// source like the plate (structural lifecycle); its union follows the hex silhouettes
        /// — the round-5/6 rectangular base quads were visible as an alien slab at the region
        /// rim (user ruling: removed).</summary>
        public Renderer? Fill;

        /// <summary>The piece's RIM CURTAIN (round 15): a mod-BUILT opaque dark prism, XZ-inset
        /// inside the authored side faces and spanning from just BELOW the top plane down past
        /// the bottom, so a ray through a translucent OUTER SIDE face terminates on dark instead
        /// of on the key. Built geometry — not a mesh copy — because the tile meshes are not
        /// CPU-readable (see <see cref="MrRimCurtain"/>). Child of the source like the others.</summary>
        public Renderer? Rim;

        /// <summary>TRUE when this source was adopted by the ROUND-16 region-membership route
        /// rather than by the family/tag sweep. Load-bearing, not just bookkeeping: the region is
        /// seeded ONLY from family/tag sources, so a region-adopted piece can never seed further
        /// adoption. Without that, each sweep would grow the region by one adopted piece's AABB and
        /// the backing would creep outward across the whole board.</summary>
        public bool ViaRegion;
    }

    private static readonly List<UnseenUnderlay> UnseenUnderlays = new(64);

    /// <summary>Instance ids of every source renderer that already carries an underlay — the
    /// sweep's dedup test (a linear list scan went quadratic against the regen churn above).</summary>
    private static readonly HashSet<int> UnseenSources = new(64);

    private static Material? _unseenDarkMat;  // opaque dark plate — key-color-safe, retinted live
    private static Material? _unseenSkipMat;  // fully invisible — fills a source's opaque slots
    private static Color _unseenDarkColor;

    // ROUND-16 DEBUG TINT (UnseenBackingDebugColors): one material per BACKING CLASS, identical to
    // the dark plate in shader, queue and blend/depth state — only .color differs. Null unless the
    // key is on; destroyed with the other underlay materials.
    private static Material? _dbgUnderlayMat; // blue   — the coplanar 1:1 plate
    private static Material? _dbgWaferMat;    // magenta— the flat top-plane gap wafer
    private static Material? _dbgRimMat;      // red    — the mod-built rim-curtain prism
    private static int _previewScanNextFrame; // throttle (same cadence as the sky sweep)
    private static int _loggedPreviewCount = -1;
    private static int _unseenVerboseLogs;    // per-renderer build log cap (see the churn note)

    // Border-census scratch/state (CensusUnseenBorder): reused lists, change-gate hash, rate floor.
    private static readonly List<Renderer> CensusScratch = new(32);
    private static readonly List<Bounds> CensusBoundsScratch = new(64);
    private static int _censusLastHash;
    private static float _censusNextAllowed;

    /// <summary>The skirt scale the live underlays were built with — a config change rebuilds
    /// them (restore + immediate resweep) so tuning needs no MR toggle, let alone a rebuild.</summary>
    private static float _appliedSkirtScale = -1f;

    /// <summary>The fill drop the live underlays were built with — tracked beside
    /// <see cref="_appliedSkirtScale"/> so a config change rebuilds live (round 7).</summary>
    private static float _appliedWaferDrop = -1f;

    /// <summary>The rim-curtain inset / top clearance the live backings were built with — tracked
    /// beside the wafer values so a config change rebuilds live (round 15).</summary>
    private static float _appliedRimInset = -1f;
    private static float _appliedRimClearance = -1f;

    /// <summary>The debug-tint state the live backings were built with — flipping
    /// <see cref="UnseenBackingDebugColors"/> rebuilds them exactly like a margin change, so the
    /// hardware round can turn the instrument on from the cfg without a rebuild or an MR toggle.</summary>
    private static bool _appliedDebugColors;

    /// <summary>Change-dedup for the RIM CURTAIN census line.</summary>
    private static int _loggedRimCount = -1;

    // ---- ROUND-16 MATCH ACCOUNTING (cumulative per MR session, reset with the underlays) --------
    // Every backed piece is attributed to the RULE that caught it, and every region candidate the
    // rails threw out is counted by REASON — so the next hardware log answers "which route fixed
    // the rim / what did the new route swallow" without another build.
    private static int _matchFamilyCount;      // backed by the family/tag/Preview sweep
    private static int _matchRegionCount;      // backed by region membership (round 16)
    private static int _regionRejectAboveTop;  // stands above its host piece's top plane (props)
    private static int _regionRejectFigure;    // figure guard (skinned / actor / animator ancestor)
    private static int _regionRejectOversize;  // footprint far larger than the host piece
    private static int _regionRejectNonMesh;   // particles/trails/lines/skinned — never touched
    private static int _regionRejectNoBacking; // matched but BuildUnseenUnderlay found nothing to back

    /// <summary>Names already reported by the region route (one line each, capped) — the answer to
    /// "which rule caught the cliff piece" in the next log.</summary>
    private static readonly HashSet<string> RegionAdoptedNames = new(8);

    /// <summary>Per-source AABBs (expanded) that define the region, plus each one's own top plane —
    /// rebuilt per sweep from the FAMILY/TAG sources only (see <see cref="UnseenUnderlay.ViaRegion"/>).</summary>
    private static readonly List<Bounds> RegionBoundsScratch = new(256);
    private static readonly List<float> RegionTopScratch = new(256);

    /// <summary>Each host piece's own BOTTOM plane — the round-17 rail needs it to tell a block that
    /// FORMS the tile (bottom at the tile's underside) from a prop STANDING ON it (bottom at the
    /// tile's top).</summary>
    private static readonly List<float> RegionBottomScratch = new(256);

    /// <summary>Union of <see cref="RegionBoundsScratch"/> — the coarse gate.</summary>
    private static Bounds _regionUnion;

    /// <summary>Slack (world units) added around each family/tag source's AABB when testing region
    /// membership. Small on purpose: a co-located piece of the SAME hex must pass, a neighbouring
    /// tile's revealed dressing must not.</summary>
    private const float RegionMembershipSlackWu = 0.05f;

    /// <summary>How far above its host piece's top plane a candidate's bounds may reach before the
    /// region route refuses it. This is the rail that keeps revealed PROPS standing on the tiles
    /// (chests, clutter, roots — all of which start at the tile top and go UP) out of the backing.</summary>
    private const float RegionTopToleranceWu = 0.02f;

    /// <summary>Round-17 SPANNING clause: how far a candidate's BOTTOM may sit above its host's
    /// bottom and still count as part of the tile block rather than as something standing on it. A
    /// prop's bottom sits ~0.3 wu higher than this (at the tile's TOP plane), so 2 cm separates the
    /// two cases with a wide margin on both sides.</summary>
    private const float RegionBottomToleranceWu = 0.02f;

    /// <summary>Round-17 SPANNING clause: how far a spanning block's TOP may exceed its host's top
    /// plane. Larger than <see cref="RegionTopToleranceWu"/> because the pieces that make up one hex
    /// are authored to slightly different heights and the cliff block is the tallest of them; still
    /// far too small for anything that stands on the tiles, and only reachable at all by a candidate
    /// that already reaches down to the tile's underside.</summary>
    private const float RegionLevelToleranceWu = 0.06f;

    /// <summary>Footprint ceiling for a region candidate, as a multiple of its host piece's XZ
    /// size. A per-hex tile block is comparable in size to the hex; a room-spanning floor, an FX
    /// volume or a whole-tile mesh is not, and adopting one would darken far more than the region.</summary>
    private const float RegionMaxFootprintFactor = 3f;

    /// <summary>Cap on the per-name "region membership backed X" lines per MR session.</summary>
    private const int RegionAdoptedNameCap = 8;

    // Round-13 unbacked-preview instrument state (RecordUnbacked / LogUnbackedPreview):
    // per-sweep scratch (cleared after every dump decision) + change gate.
    private static readonly List<Renderer> UnbackedScratch = new(16);
    private static readonly List<string> UnbackedReasons = new(16);
    private static int _unbackedLastHash;
    private static float _unbackedNextAllowed;

    /// <summary>One-shot latch for the submesh-coverage sample line (round 12): the first built
    /// backing logs mesh subMeshCount vs source/backing material counts, so the next hardware
    /// log PROVES the copies cover every submesh (an uncovered bevel submesh renders the
    /// authored translucency over raw key — wide green bands). Reset with the underlays.</summary>
    private static bool _submeshDiagLogged;

    /// <summary>Y-squash of the gap-backing wafer (round 11): the fill copy's local Y scale.
    /// Squashing the same-mesh copy to 2 % collapses all of its relief into a flat
    /// hex-silhouette WAFER — a per-piece "region slab" with the piece's own outline, no
    /// rectangles (standing user ruling) and no poke-through of a scaled bevel past the
    /// authored top surface (the round-7 deep fill's residual risk).</summary>
    private const float FillSquashY = 0.02f;

    /// <summary>How far (world units) the rim curtain's bottom cap reaches below the piece's mesh
    /// bottom, so the prism closes under the piece instead of ending flush with it. Not a config
    /// key: from below the region has read fully opaque since round 9 (mixed_reality_tiles2.png),
    /// this is only the seal that makes the prism watertight.</summary>
    private const float RimBottomDrop = 0.02f;



    /// <summary>The three debug tints (round 16). Unmistakable and mutually unambiguous, and none of
    /// them is a chroma-key preset EXACTLY (the Magenta/Blue presets exist): a debug round must run
    /// on the GREEN key, and <see cref="EnsureDebugMaterials"/> logs a warning if the live key comes
    /// close to any of them, so a "colour missing" reading can never be a keyed-away colour.</summary>
    private static readonly Color DebugUnderlayColor = new(0.10f, 0.35f, 1f, 1f);   // blue
    private static readonly Color DebugWaferColor = new(1f, 0.15f, 0.85f, 1f);      // magenta
    private static readonly Color DebugRimColor = new(1f, 0.10f, 0.10f, 1f);        // red

    /// <summary>MATERIAL names whose properties were already dumped this session (round 8: was
    /// shader names — the edge materials share the hex shader and stayed undumped;
    /// <see cref="DumpUnseenShaderProperties"/>).</summary>
    private static readonly HashSet<string> DumpedUnseenShaders = new(8);

    /// <summary>Lowest renderQueue observed on any matched family MATERIAL this session (round
    /// 6): the base quads/underlays must composite BEFORE the family draws, against a depth
    /// buffer that does not yet contain the family surfaces. All evidence says the family sits
    /// in the transparent range (&gt;2500 — the ModBuild-57 IsTranslucent match could only have
    /// passed on the queue, the materials expose no _DstBlend), so the default 2500 already
    /// precedes it; but if a family material ever shows up AT or BELOW 2500 with its pass-0
    /// hardcoded ZWrite On (ShaderOcclusionPatcher README: Amp_Basic_Unseen 0/0 zWrite On), a
    /// later-drawn backing would fail LEqual behind it from above — so the shared materials'
    /// queue adapts to observedMin−1 the moment the observation says so. int.MaxValue = none
    /// observed yet.</summary>
    private static int _familyMinQueue = int.MaxValue;

    // Sky/background geometry hidden while MR is on (item 2). The scenario backdrop/skydome is
    // opaque mesh geometry, not the skybox — disabled here, re-enabled on restore.
    private static readonly List<Renderer> HiddenSky = new(8);
    private static readonly List<Renderer> RendererScratch = new(8);
    private static int _skyScanNextFrame;   // throttle the (allocating) renderer sweep
    private static int _loggedSkyCount = -1; // change-dedup for the hidden-count log
    private static bool _skyDiagLogged;      // one-shot candidate dump when nothing matched

    /// <summary>Sweep the scene renderers this often (frames) — the backdrop can generate late.</summary>
    private const int SkyScanIntervalFrames = 60;

    /// <summary>Absolute floor for the enclosing-dome extent test (world units), all three axes.</summary>
    private const float SkyMinEnclosingSize = 10f;

    /// <summary>Enclosing extent must also clear this fraction of the head far plane (scale-aware).</summary>
    private const float SkyEnclosingFarFraction = 0.1f;

    /// <summary>Name fragment that marks the game's fog-of-war family. The game uses it
    /// consistently across the whole kit: the hex shader `Amp_Basic_Unseen`
    /// (tools/ShaderOcclusionPatcher/README.md: "X-ray floor tiles"), the dedicated ground-plane
    /// shader `UnseenGroundPlane_Shd` (Player.log addressables list), and the Apparance object
    /// names ('EN_Unseen_…'). The ModBuild-57 log proves the shader half (every preview renderer
    /// it caught ran Amp_Basic_Unseen); hardware round 2 ("die Animation drumrum ist immer noch
    /// transparent") forced the round-3 widening from shader-name-only to GO/material/shader name
    /// (<see cref="HasUnseenName"/>) so the animated pieces of the kit match too.</summary>
    private const string UnseenShaderHint = "Unseen";

    /// <summary>Cap on per-renderer "underlay built" log lines per MR session. Apparance
    /// regenerates tile content constantly (226 rebuilds of the same two names in the ModBuild-57
    /// log); after the cap the change-gated count line still tracks the total.</summary>
    private const int UnseenVerboseLogCap = 12;

    /// <summary>Border margin (world units) around the matched unseen AABBs inside which the
    /// census (<see cref="CensusUnseenBorder"/>) looks for uncovered translucent renderers — the
    /// "Animation drumrum" plays at/just beyond the unseen region's edge.</summary>
    private const float CensusBorderWu = 1f;

    /// <summary>Census candidates named in full; beyond this only the count is reported.</summary>
    private const int CensusMaxListed = 20;

    /// <summary>Rate floor between census logs even when the candidate set keeps changing —
    /// Apparance regen would otherwise re-print it every sweep.</summary>
    private const float CensusMinIntervalSeconds = 30f;

    /// <summary>The dark the unseen geometry blends against in MR — outside MR the same geometry
    /// blends against the unrendered near-black void behind doors, so a dark neutral IS the
    /// authored background. Mirrors WorldUI.MrBacking's plate neutral family (keep in sync).</summary>
    private static readonly Color UnseenDark = new(0.12f, 0.11f, 0.10f, 1f);

    /// <summary>Key-avoidance lift (same rule and values as WorldUI.MrBacking): when the live key
    /// colour comes within keying distance of the dark neutral (only the BLACK preset does), the
    /// underlay brightens so no compositor threshold can key the backing away.</summary>
    private static readonly Color UnseenLift = new(0.34f, 0.30f, 0.25f, 1f);

    /// <summary>Per-channel distance below which the underlay counts as key-colored.</summary>
    private const float UnseenKeyDistance = 0.25f;

    /// <summary>Material/shader/name fragments that mark a renderer as sky/background.</summary>
    private static readonly string[] SkyNameHints =
    {
        "skydome", "skybox", "sky", "backdrop", "dome", "horizon",
        "cloud", "vista", "firmament", "background",
    };

    private static bool _active;        // MR currently applied to the scene
    private static bool _loggedActive;  // change-dedup for the on/off log
    private static Color _loggedColor;

    // ---- menu-visibility: the sky now STAYS, the MENU renders ON TOP -----------------------
    // A floated (MR-off) menu is uGUI that ZTests LEqual against the depth buffer, so the enclosing
    // scenario backdrop ('GH_SkySphere', shader 'AMP_SkyShader' — a squashed dome that writes depth
    // and exposes no '_ZWrite' to toggle) can occlude a movable menu dragged toward the shell edge.
    //
    // The previous lever here DISABLED that backdrop renderer outright while a menu floated. The user
    // found the vanishing sky very distracting, so that lever is GONE: the sky always stays rendered.
    // The occlusion is instead fixed where it belongs — the floated MODAL's uGUI graphics are switched
    // to ZTest Always (WorldUI.CanvasConversion 'renderOnTop'), so nothing can occlude them and the sky
    // is untouched. <see cref="KeepMenusUnclipped"/> is now a no-op kept only for its call sites.
    //
    // NOTE: the MR-ON path (<see cref="HideSkyGeometry"/> / <see cref="HideSkyMeshes"/>) is unrelated
    // and fully intact — MR still hides the backdrop mesh so the chroma key shows through.

    /// <summary>
    /// True while the MR readability treatment is wanted (WorldUI.MrBacking): the SAME want
    /// condition <see cref="Tick"/> keys the whole mode off, so backings appear/vanish with the
    /// one existing MR switch (user ruling: no second toggle). Reads the config WITHOUT forcing
    /// a Bind — before the rig has ticked once there is no VR session, hence no MR, hence false;
    /// binding stays owned by the rig path.
    /// </summary>
    internal static bool BackingsWanted =>
        _file != null && Enabled.Value && VRSession.IsRunning;

    /// <summary>
    /// No-op (kept for its ModalFallback call sites). The floated-menu-vs-sky occlusion is now fixed
    /// by rendering the MODAL on top (WorldUI.CanvasConversion 'renderOnTop'); the sky is never
    /// disabled for a menu any more, so there is nothing to do here.
    ///
    /// <para>KEEP — DO NOT DELETE (refactor Batch D, verified at HEAD: both call sites exist,
    /// <c>ModalFallback.cs</c> around :800 and :1135, and pass real arguments). An empty method
    /// looks like the obvious cleanup, but the comment block above it is the ONLY record of why
    /// the sky is no longer disabled for a floated menu — and that comment states that
    /// re-implementing this body IS the regression (b84817d: the user found the vanishing sky
    /// very distracting). Removing the method deletes the question along with the answer.</para>
    /// </summary>
    internal static void KeepMenusUnclipped(bool wanted) { }

    internal static void Bind()
    {
        if (_file != null)
            return;
        _file = ModuleConfig.Create("mixedreality");
        Enabled = _file.Bind("MixedReality", "Enabled", Defaults.MixedReality_Enabled,
            "Mixed-reality (chroma-key passthrough) mode. When ON the sky/background of the " +
            "whole game turns the flat solid KeyColor and every skybox is disabled, so Virtual " +
            "Desktop (or any compositor) can chroma-key that color and show the diorama/table " +
            "floating over your real room. The 3D geometry keeps rendering — only the sky becomes " +
            "the flat key color. While ON, the mod's floating UI (menus, captions, name tags) " +
            "additionally gets opaque backing plates so text stays readable over the passthrough " +
            "room. Restored fully (plates included) when turned off.");
        KeyColor = _file.Bind("MixedReality", "KeyColor", Defaults.KeyColor,
            "The solid chroma-key color the sky/background clears to in mixed-reality mode " +
            "(default pure green RGBA 0,1,0,1). The in-VR settings panel cycles the presets " +
            "green / magenta / blue; any RGBA is accepted here.");
        OpaquePreviewTiles = _file.Bind("MixedReality", "OpaquePreviewTiles", Defaults.OpaquePreviewTiles,
            "PART OF MIXED REALITY, not a choice beside it (like HideSkyMeshes; not offered in the " +
            "VR menu). ALL of the game's translucent 'unseen' fog-of-war geometry — the face-down " +
            "tile STACKS of not-yet-discovered rooms AND the unseen-area hexes that mark the " +
            "undiscovered area behind doors — blends with whatever is behind it; over the game's " +
            "dark void that reads fine, but in MR the chroma key / passthrough room shows through " +
            "and it all looks like green glass. While MR is on, the sweep finds those renderers " +
            "(the 'Unseen' shader family, plus anything translucent under a tile's active " +
            "'Preview' subtree) and slips an OPAQUE dark backing mesh UNDER each one — the " +
            "authored translucent material keeps rendering exactly as designed, look and " +
            "animation untouched, it just blends against dark instead of against your room. The " +
            "backings are destroyed when MR turns off — normal mode is never touched. Turn OFF " +
            "only if a run shows it darkening wanted geometry — the log names what it backed.");
        UnseenSkirtScale = _file.Bind("MixedReality", "UnseenSkirtScale", Defaults.UnseenSkirtScale,
            "Widening of each unseen piece's GROOVE-FILL copy relative to its geometry (1 = " +
            "exact silhouette). Every fog-of-war piece gets TWO dark backings in MR: an exact " +
            "copy directly behind its surfaces, and a lowered fill copy that plugs the beveled " +
            "channels BETWEEN neighboring hexes — this factor widens only that fill so " +
            "neighboring fills overlap under the groove line. Applies while MR is on, live " +
            "(backings rebuild on change). Raise if grooves between hexes still glow; lower if " +
            "dark peeks out past the outermost hex edges. Clamped to 1..2.");
        UnseenWaferDrop = _file.Bind("MixedReality", "UnseenWaferDrop", Defaults.UnseenWaferDrop,
            "How far (world units) each unseen piece's flat gap-backing WAFER sits below the " +
            "piece's TOP plane in MR. The wafer is a squashed, slightly widened dark copy of " +
            "the piece that floors the gaps BETWEEN neighboring hexes just under their tops, so " +
            "looking into a seam lands on dark instead of the passthrough room while the " +
            "animated rim above it keeps playing. (Successor of the retired UnseenFillDrop key, " +
            "whose persisted deep-fill value no longer matched these semantics.) Applies while " +
            "MR is on, live (backings rebuild on change). Raise if the wafer z-fights the hex " +
            "tops; lower toward 0.01 if green seams still show at shallow angles. Clamped to 0..2.");
        UnseenRimInset = _file.Bind("MixedReality", "UnseenRimInset", Defaults.UnseenRimInset,
            "How far (world units) the dark RIM CURTAIN sits INSIDE each unseen piece's vertical " +
            "side faces in MR. The curtain is a mod-BUILT dark prism that follows the piece's hex " +
            "outline and stands just behind its side faces (its HEIGHT — the outer 'cliff' of the " +
            "fog-of-war region), so looking at the region edge lands on dark instead of on the " +
            "passthrough room. It is built rather than copied because the game's tile meshes are " +
            "not CPU-readable, so a copy would inherit whatever vertex alpha the artist put on " +
            "those side vertices — the reason twelve rounds of same-mesh backings never covered " +
            "them. Applies while MR is on, live (backings rebuild on change). Raise if any dark " +
            "pokes out through a damaged/notched piece edge; lower toward 0.01 if the outer edges " +
            "still glow. Clamped to 0.005..0.2.");
        UnseenRimTopClearance = _file.Bind("MixedReality", "UnseenRimTopClearance", Defaults.UnseenRimTopClearance,
            "How far (world units) the RIM CURTAIN's top stays BELOW each unseen piece's top " +
            "plane in MR. This is the guarantee that the curtain can never paint over an authored " +
            "hex top or its animation: it is always kept below the (wider) gap-backing wafer, so " +
            "from above it is completely hidden behind a surface that is already dark. Raise if " +
            "any dark ever shows on a hex top; lower toward the wafer drop if the very top of the " +
            "outer edge still glows. Forced to at least UnseenWaferDrop + 0.005. Applies while MR " +
            "is on, live (backings rebuild on change). Clamped to 0.005..0.5.");
        UnseenRegionMembership = _file.Bind("MixedReality", "UnseenRegionMembership",
            Defaults.UnseenRegionMembership,
            "PART OF MIXED REALITY, not a choice beside it (like HideSkyMeshes; not offered in the " +
            "VR menu). Also give a dark backing to every piece that merely STANDS INSIDE the " +
            "fog-of-war region — below the unseen tiles' own top plane — even when its material " +
            "does not look see-through to the mod. The undiscovered-area tiles are built from " +
            "several meshes per hex, and the tallest one (the block that forms the region's outer " +
            "CLIFF) advertises nothing the mod could recognise: no 'Unseen' in its name, no " +
            "transparent blend it exposes, no transparent render queue. It is the piece whose " +
            "vertical faces kept showing the room through them. Instead of guessing from materials, " +
            "this asks where the piece stands. Figures are never touched, nothing standing ON the " +
            "tiles is touched, and anything much larger than a single hex is refused. Turn OFF only " +
            "if a run shows it darkening wanted geometry — the log names everything it backed.");
        UnseenBackingDebugColors = _file.Bind("MixedReality", "UnseenBackingDebugColors",
            Defaults.UnseenBackingDebugColors,
            "DIAGNOSTIC, default off — turn this on only when asked for a screenshot. In MR every " +
            "fog-of-war piece gets three mod-built dark backings (a copy right behind its surfaces, " +
            "a flat wafer just under its top plane, and a prism behind its outer side faces). With " +
            "this ON they are painted in flat signal colours instead of dark — copy BLUE, wafer " +
            "MAGENTA, side prism RED — with everything else about them unchanged (same shape, same " +
            "position, same draw order). One photo then shows which of the mod's surfaces actually " +
            "reach your eyes and exactly where they sit, which is the one thing a dark backing can " +
            "never show. Use the GREEN key colour while it is on, so no signal colour can be keyed " +
            "away. Applies while MR is on, live (backings rebuild on change); turning it off " +
            "restores the normal dark look immediately.");
        HideSkyMeshes = _file.Bind("MixedReality", "HideSkyMeshes", Defaults.HideSkyMeshes,
            "PART OF MIXED REALITY, not a choice beside it — turning MR on does this, and the key "
            + "is kept only as an escape hatch for a run where it hides wanted geometry. It is not "
            + "offered in the VR menu, because half of MR is not a thing to switch off: keying the "
            + "camera clear without it leaves the backdrop drawn over the key colour, so MR simply "
            + "would not work. Disables the sky/background GEOMETRY while MR is on. The scenario backdrop is " +
            "an opaque mesh (not the skybox), so keying the camera clear alone leaves it drawn " +
            "over the key color; MR sweeps the renderers and disables the ones that draw the " +
            "surrounding sky (sky-ish name/material, or bounds enclosing the head on all axes — " +
            "never the diorama tiles/props), restoring them when MR turns off. Turn OFF only if " +
            "a run shows it hiding wanted geometry — the log names every renderer it disabled.");
    }

    /// <summary>Name of the current key color if it matches a preset, else "Custom".</summary>
    internal static string KeyColorName
    {
        get
        {
            Color c = KeyColor.Value;
            foreach ((string name, Color color) in Presets)
            {
                if (Approximately(color, c))
                    return name;
            }
            return "Custom";
        }
    }

    private static bool Approximately(Color a, Color b) =>
        Mathf.Abs(a.r - b.r) < 0.02f && Mathf.Abs(a.g - b.g) < 0.02f &&
        Mathf.Abs(a.b - b.b) < 0.02f && Mathf.Abs(a.a - b.a) < 0.02f;

    /// <summary>
    /// Per-frame driver (called from <c>VRRigDriver.Update</c> AFTER TickHeadClearColor and
    /// the camera-policy sweep). Applies the key-color clears while MR is wanted, restores
    /// fully otherwise.
    /// </summary>
    internal static void Tick()
    {
        Bind();

        // ELEMENT MOOD IS TICKED HERE, not from SkyAlternative.Tick, because this is the only
        // per-frame call that runs on BOTH branches below. The user's requirement is that the
        // elements reach the player under every presentation — a bundled room, the game's own sky,
        // "off/black", and passthrough — and the MR-ON branch never reaches SkyAlternative.Tick.
        // The mood does its own full gating (setting, session, scenario board) and publishes only
        // numbers; the "no geometry over passthrough" half of the MR ruling is the ART's to keep.
        // See Core/ElementMood.cs, "MIXED REALITY KEEPS SENSING".
        ElementMood.Tick();

        bool want = Enabled.Value && VRSession.IsRunning;
        if (!want)
        {
            if (_active)
                RestoreAll();
            // MR OFF: the player's SKY CHOICE applies ([Sky] Style — SkyAlternative). Default:
            // the game's sky STAYS and is made a pure NON-OCCLUDING backdrop by SkyBackdrop
            // (ZWrite-off, or — when the shader hard-codes ZWrite On — the sky is left drawing
            // its own colour at Background and a real mod-layer depth-reset renderer at queue 1001
            // overwrites depth to ~far after it, an ordinary tiled-GPU-safe draw with NO renderer
            // suppression and NO mid-pass depth clear) so floated menus, the moved board and the
            // laser in front of it are never clipped — the fix lives on the SPHERE side, not on
            // the menus (WorldUI.CanvasConversion no longer forces menus on top). A non-Default
            // choice: SkyAlternative hides the sphere and spawns its bundled 3D environment
            // (rig-anchored, mod layer — see its class doc), and SkyBackdrop stands down for it
            // exactly as it does for MR.
            bool altSkyShown = SkyAlternative.Tick();
            SkyBackdrop.Tick(skyOwnedElsewhere: altSkyShown);
            return;
        }

        // MR ON ⇒ the sky is ALWAYS off (the user's rule), whatever [Sky] Style says: the
        // alternative-sky backdrop stands down FIRST and re-enables the game sphere, so the
        // HideSkyGeometry sweep below records and disables a clean renderer for the chroma key.
        SkyAlternative.StandDown();

        // MR ON: SkyBackdrop stands down so MR's HideSkyMeshes owns the sphere for the chroma key
        // (restores the renderer/material first, so HideSkyGeometry disables a clean renderer).
        SkyBackdrop.Tick(skyOwnedElsewhere: true);

        Color key = KeyColor.Value;
        key.a = 1f; // the sky clear must be fully opaque for a clean chroma key

        // 1) Kill the global skybox (ambient/reflection contributions + any Skybox clear).
        if (!_skyboxSaved)
        {
            _savedSkybox = RenderSettings.skybox;
            _skyboxSaved = true;
        }
        if (RenderSettings.skybox != null)
            RenderSettings.skybox = null;

        // 2) HeadCamera — the only camera that reaches the HMD (VRCameraPolicy). MR owns its
        //    clear while ON; runs after TickHeadClearColor so the key color wins the frame.
        Camera? head = VRCameraPolicy.AllowedHead;
        if (head != null)
        {
            Record(head);
            ForceSolid(head, key);
        }

        // 3) Every OTHER live camera still sky-clearing → SolidColor(key) (desktop mirror +
        //    any secondary camera). FlatScreen-managed cameras are already non-Skybox, so we
        //    never collide with its RT clear policy.
        int count = VRCameraPolicy.GetAllCamerasNonAlloc(out Camera[] cams);
        for (int i = 0; i < count; i++)
        {
            Camera cam = cams[i];
            if (cam == null || cam == head || cam.clearFlags != CameraClearFlags.Skybox)
                continue;
            Record(cam);
            ForceSolid(cam, key);
        }

        // 4) Hide the sky/background GEOMETRY (item 2). The scenario backdrop is an opaque mesh
        //    the HeadCamera renders (mask 0xFFFFFFFF) — a SolidColor clear draws BEHIND it, so
        //    the key color never shows until the mesh itself is disabled.
        HideSkyGeometry();

        // 5) Back ALL translucent "unseen" fog-of-war geometry — the preview tile stacks AND the
        //    unseen-area hexes inside revealed tiles — with opaque dark underlays (user rulings
        //    2026-08-04 + 2026-08-05). Their translucent materials blend with whatever is behind
        //    them — over the key colour that mix lands inside the compositor's similarity window,
        //    so the real room shows through. Mod-owned child meshes only, the authored materials
        //    are never touched, everything destroyed on MR off; normal mode is bit-identical
        //    because none of this runs while MR is off.
        ForceUnseenOpaque();

        _active = true;
        if (!_loggedActive || _loggedColor != key)
        {
            _loggedActive = true;
            _loggedColor = key;
            VRLog.Info("Core", $"Mixed reality ON — skybox disabled, sky/background keyed to " +
                               $"{KeyColorName} (RGBA {key.r:0.##},{key.g:0.##},{key.b:0.##},{key.a:0.##}); " +
                               $"diorama geometry stays visible.");
        }
    }

    // ---- sky/background geometry (item 2) -------------------------------------------------------

    /// <summary>
    /// Throttled sweep of the live renderers: disable the ones that draw the surrounding
    /// sky/background (sky-ish name/material/shader, OR world bounds that enclose the head with
    /// a large extent on all three axes — a dome/backdrop, never a floor tile or a table prop).
    /// Recorded for restore; each hidden renderer is logged. When nothing matched, the biggest
    /// enclosing candidates are dumped once so the next hardware run can name the sky source.
    /// </summary>
    private static void HideSkyGeometry()
    {
        if (!HideSkyMeshes.Value)
        {
            // Live safety valve: restore anything we already hid when the toggle flips off.
            if (HiddenSky.Count > 0)
                RestoreSky();
            return;
        }
        if (Time.frameCount < _skyScanNextFrame)
            return;
        _skyScanNextFrame = Time.frameCount + SkyScanIntervalFrames;

        Camera? head = VRCameraPolicy.AllowedHead;
        if (head == null)
            return;
        Vector3 headPos = head.transform.position;
        float sizeFloor = Mathf.Max(SkyMinEnclosingSize, head.farClipPlane * SkyEnclosingFarFraction);

        Renderer[] all = UnityEngine.Object.FindObjectsOfType<Renderer>(); // active renderers only
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null || !r.enabled)
                continue;
            int layer = r.gameObject.layer;
            if (layer == VRLayers.ModLayer || layer == 5) // never our own mod visuals / UI hosts
                continue;
            if (IsHiddenSky(r) || !IsSkyRenderer(r, headPos, sizeFloor))
                continue;

            HiddenSky.Add(r);
            r.enabled = false;
            Bounds b = r.bounds;
            VRLog.Info("Core", $"MR: disabled sky/background renderer '{r.gameObject.name}' " +
                               $"(layer {LayerName(layer)}, bounds size {b.size} @ {b.center}, " +
                               $"shader '{ShaderName(r)}') — drew over the key color as geometry.");
        }

        if (HiddenSky.Count != _loggedSkyCount)
        {
            _loggedSkyCount = HiddenSky.Count;
            VRLog.Info("Core", $"MR: {HiddenSky.Count} sky/background renderer(s) hidden — " +
                               $"the {KeyColorName} key now shows behind the diorama.");
        }

        if (HiddenSky.Count == 0 && !_skyDiagLogged)
        {
            _skyDiagLogged = true;
            LogSkyCandidates(all, headPos);
        }
    }

    private static bool IsSkyRenderer(Renderer r, Vector3 headPos, float sizeFloor)
    {
        if (NameLooksLikeSky(r))
            return true;
        // Enclosing-dome signal: the world bounds SURROUND the head with a large extent on ALL
        // THREE axes. A flat floor/tile has one thin axis; a table prop does not contain the
        // head — only a skydome/sphere/backdrop box passes.
        Bounds b = r.bounds;
        if (!b.Contains(headPos))
            return false;
        Vector3 s = b.size;
        return s.x > sizeFloor && s.y > sizeFloor && s.z > sizeFloor;
    }

    private static bool NameLooksLikeSky(Renderer r)
    {
        if (NameHasHint(r.gameObject.name))
            return true;
        Material? m = r.sharedMaterial;
        if (m == null)
            return false;
        return NameHasHint(m.name) || (m.shader != null && NameHasHint(m.shader.name));
    }

    private static bool NameHasHint(string? s)
    {
        if (string.IsNullOrEmpty(s))
            return false;
        for (int i = 0; i < SkyNameHints.Length; i++)
        {
            if (s!.IndexOf(SkyNameHints[i], StringComparison.OrdinalIgnoreCase) >= 0)
                return true;
        }
        return false;
    }

    private static bool IsHiddenSky(Renderer r)
    {
        for (int i = 0; i < HiddenSky.Count; i++)
        {
            if (ReferenceEquals(HiddenSky[i], r))
                return true;
        }
        return false;
    }

    /// <summary>
    /// One-shot diagnostic: the sky is generated geometry with an unknown name, so when the
    /// heuristics hide nothing, dump the largest renderers that enclose the head — the tester
    /// reads the real sky source's name/layer straight off this list.
    /// </summary>
    private static void LogSkyCandidates(Renderer[] all, Vector3 headPos)
    {
        RendererScratch.Clear();
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null || !r.enabled)
                continue;
            int layer = r.gameObject.layer;
            if (layer == VRLayers.ModLayer || layer == 5)
                continue;
            if (r.bounds.Contains(headPos))
                RendererScratch.Add(r);
        }
        // Largest-first (bounds volume): a simple selection is fine for a one-shot dump.
        RendererScratch.Sort((a, b) => BoundsVolume(b.bounds).CompareTo(BoundsVolume(a.bounds)));
        int count = Mathf.Min(8, RendererScratch.Count);
        VRLog.Info("Core", $"MR: no sky renderer matched the heuristics — {RendererScratch.Count} " +
                           $"renderer(s) enclose the head; largest {count} candidate(s) (name the sky source here):");
        for (int i = 0; i < count; i++)
        {
            Renderer r = RendererScratch[i];
            Bounds b = r.bounds;
            VRLog.Info("Core", $"MR:   candidate '{r.gameObject.name}' " +
                               $"(layer {LayerName(r.gameObject.layer)}, size {b.size}, " +
                               $"shader '{ShaderName(r)}').");
        }
        RendererScratch.Clear();
    }

    private static float BoundsVolume(Bounds b) => b.size.x * b.size.y * b.size.z;

    private static string LayerName(int layer)
    {
        string name = LayerMask.LayerToName(layer);
        return string.IsNullOrEmpty(name) ? layer.ToString() : $"{name}/{layer}";
    }

    private static string ShaderName(Renderer r)
    {
        Material? m = r.sharedMaterial;
        return m != null && m.shader != null ? m.shader.name : "<none>";
    }

    // ---- "unseen" fog-of-war geometry (MR opaque underlays) -------------------------------------

    /// <summary>
    /// Throttled sweep: find ALL translucent "unseen" fog-of-war renderers — the face-down TILE
    /// STACKS of not-yet-revealed rooms and the unseen-AREA hexes inside revealed tiles — and slip
    /// an OPAQUE DARK UNDERLAY under each while MR is on.
    ///
    /// HOW THE GEOMETRY IS IDENTIFIED (read from the ModBuild-57 log + decompiled source): the
    /// game draws both classes with the <c>Amp_Basic_Unseen</c> shader family
    /// (<see cref="UnseenShaderHint"/>). The previous round matched only "renderer under an ACTIVE
    /// 'Preview' ancestor" (<c>ProceduralMapTile.ShowContent</c>, decompiled
    /// ProceduralMapTile.cs:148) — correct for the stand-in stacks, but the unseen-area hexes
    /// behind doors are part of a REVEALED tile's generated content (log: the glassy hexes sit in
    /// MAPTILE 'E', vis=All, while the Preview-vis tiles report 0 renderers — their content is not
    /// even generated), so the sweep never saw them and they stayed green glass
    /// (.planning/debug/keine_ausblendung.png, bottom). The match is now the UNION of both
    /// signals: shader-family membership OR an active 'Preview' ancestor — one mechanism for the
    /// whole see-through class, and the Preview signal keeps covering any translucent stack
    /// renderer that might not run the family shader.
    ///
    /// WHERE THE GREEN COMES FROM (user correction 2026-08-07, verbatim premise — and the fact
    /// nine earlier rounds all reasoned past): "Virtual Desktop replaces the green BACKGROUND with
    /// reality … The problem with SEMI-TRANSPARENT surfaces: they ALTER the green tone and VD can
    /// no longer key it properly … The green comes SOLELY from the background, not from the tiles
    /// themselves." The glowing pixels are the KEY BLENDED THROUGH the family's translucent
    /// surfaces, and the compositor keys FINAL pixels — which is why dark hex tops survived every
    /// round and the bright animated grout could never survive, whatever was rendered behind it.
    /// It also settles the above/below asymmetry for good: from BELOW every ray terminates on a
    /// backing; from ABOVE, grazing rays through the raised translucent rim edges exit SIDEWAYS
    /// past the diorama into the key background, where no backing can ever be behind them. Backing
    /// is therefore structurally complete below and structurally insufficient above, and the answer
    /// had to be GEOMETRY that closes the open directions, never a wider material predicate.
    ///
    /// THE KIT IS THREE AUTHORED RENDERERS PER HEX, and counting them is what ended the search
    /// after sixteen rounds of widening predicates. The WallSegmentFade MAPTILE dumps give preview
    /// subtrees of 603 / 225 / 171 renderers — every one divisible by 9 — with the backing triple
    /// hanging off 'EN_Unseen_FloorHex_Edge_Damage_03_PR'. So the kit is 'Simple Tile' +
    /// 'EN_Unseen_FloorHex_Edge_Damage_03_PR' + 'EN_CR_FloorTiles_Damaged_03', and the mod backed
    /// exactly TWO of the three. The unbacked third is 'Simple Tile' — bounds y−0.4..−0.1, the
    /// block's FULL HEIGHT, i.e. the outer CLIFF the user kept photographing. It is not family-NAMED
    /// anywhere and it fails the blend probe (hardcoded pass blend, no _DstBlend property,
    /// opaque-range queue), so no material signal was ever going to reach it.
    ///
    /// WHAT CLOSES EACH DIRECTION, in the order a ray meets them:
    /// <list type="bullet">
    /// <item>BELOW / every authored surface — the PRIMARY underlay, an exact 1:1 same-mesh dark
    ///   copy, coplanar behind every face from every direction (<see cref="BuildUnseenUnderlay"/>).
    ///   It is 1:1 and not scaled: a copy displaced about the mesh centre stops sitting coplanar
    ///   behind the beveled groove faces, and a grazing ray then slips through the parallax gap
    ///   into the V-channel and onto the key. <see cref="UnseenSkirtScale"/> survives as a widening
    ///   of the WAFER only; it was originally a fringe margin for an animation that turns out to
    ///   scroll its UVs ('Unseen_Floor_Hex_Mat' has _UV_Offset/_UVTiling and no displacement
    ///   property), so the pattern can never leave its own silhouette and no margin was ever needed.</item>
    /// <item>ABOVE / the seams between neighbouring hexes — the GAP BACKING WAFER, a flat copy
    ///   seated at each piece's own top plane minus <see cref="UnseenWaferDrop"/> (~2 cm). The
    ///   earlier deep "groove fill" sat 0.3+ wu lower than the hex tops (y−0.7..−0.4 against tops at
    ///   ~y−0.1) and the raw key floor showed straight through the canyon between pieces.</item>
    /// <item>THE OUTER RIM / the vertical cliff faces — the RIM CURTAIN
    ///   (<see cref="MrRimCurtain"/>): a mod-BUILT opaque dark prism per backed piece, hex-shaped
    ///   from the mesh-local bounds, XZ-inset <see cref="UnseenRimInset"/> inside the authored side
    ///   faces and spanning from mesh-top − <see cref="UnseenRimTopClearance"/> (forced strictly
    ///   below the wafer plane) down past the mesh bottom. BUILT geometry rather than a same-mesh
    ///   copy for a hard reason: the authored meshes carry near-zero vertex alpha on the side/rim
    ///   vertices, both candidate backing shaders multiply that into their output, and
    ///   'EN_CR_FloorTiles_Damaged_03' "is not CPU-readable — its vertex-color channel cannot be
    ///   stripped". A mesh the mod builds has no colour channel at all, the attribute defaults to
    ///   white, and the dark material renders unconditionally.</item>
    /// <item>THE CLIFF PIECE ITSELF is reached by <see cref="RegionMembershipPass"/>. Since build 530
    ///   only the documented "Simple Tile" cliff block qualifies, and cutout materials are refused.
    ///   Historically this route backed every mesh renderer that merely STANDS INSIDE a family
    ///   piece's AABB and either stays under that piece's top plane OR SPANS the host (bottom at the
    ///   tile's underside, top level with the tile's top) — see <see cref="ClassifyRegion"/>. A prop
    ///   standing ON a tile has its bottom at the tile's TOP and satisfies neither clause; the block
    ///   that FORMS the tile satisfies the second by construction. Figures, mod objects, non-meshes
    ///   and oversized footprints are refused, and every refusal is counted.</item>
    /// </list>
    /// <see cref="IsTranslucent"/> also learned the RenderType-TAG signal (Transparent/Fade/Overlay
    /// — the family's own materials are tagged 'Overlay'), which catches such pieces through the
    /// Preview-ancestor branch. Family PARTICLES/trails are recognised but never material-touched
    /// (standing instruction); they compose over the now-dark region.
    ///
    /// MECHANISMS TRIED AND REJECTED, so none of them is reached for again:
    /// <list type="bullet">
    /// <item>OPAQUE COPIES OF THE AUTHORED MATERIAL. All blend/depth state is hardcoded in the pass
    ///   (verified by the render-state dump), so a copy can only move renderQueue and keeps
    ///   blending — the old 'forced OPAQUE' lines were HasProperty-guarded no-ops plus a queue move.</item>
    /// <item>A SHADER SWAP onto the mod's bundled 'GloomhavenVR/Overlay'. User verdict: raw
    ///   '_MainTex x _Color' without the Amp shader's fog-of-war treatment rendered a BRIGHT stone
    ///   texture ("statt schwarze tiles ist da jetzt eine merkwuerdige textur"), and any swap must
    ///   also bring its own motion — no decompiled writer of '_UV_Offset' exists, the scroll is
    ///   shader-time.</item>
    /// <item>A "KEY DODGE" MPB dimming the family's exposed colour/boost properties. Its premise
    ///   ("the authored glow is green") is what the user correction above overturned, and its own
    ///   instrumentation had already convicted '_Tint' as inert.</item>
    /// <item>RECTANGULAR REGION BASE QUADS under each piece's AABB. User ruling: visible at the
    ///   region rim as an alien dark slab. Nothing rectangular from any angle, and nothing past the
    ///   outer hex edges except the long-accepted thin rim — this is a standing constraint on any
    ///   future backing.</item>
    /// <item>OPAQUE BACKINGS WITH ZWRITE PLUS A FULL-HEIGHT INSET SIDE SKIRT. The skirt is a
    ///   same-mesh copy, so it carries the piece's own TOP surface a hair inside the authored one
    ///   and simply covered it: the whole region became one featureless dark plate.</item>
    /// <item>"CORRECTING" THE WAFER'S SEATING GEOMETRY — see the DO-NOT-FIX note at the offset in
    ///   <see cref="BuildUnseenUnderlay"/>. The seam coverage the user approved IS the widened slab
    ///   riding above the hex tops; correcting it geometrically removed exactly the thing that was
    ///   working ("die Lücken sind nun wieder vollständig da wie zuvor"). Standing lesson for this
    ///   whole system: geometric correctness is not the goal, the approved look is — a "defect" the
    ///   user has blessed is a FEATURE, and any future change to the seam coverage must be additive.</item>
    /// </list>
    ///
    /// TRAPS THAT COST ROUNDS HERE, all of them instruments or engine behaviour rather than logic:
    /// <list type="bullet">
    /// <item>A PERSISTED CONFIG VALUE SURVIVES A DEFAULT CHANGE. The wafer depth was re-defaulted
    ///   and the user's cfg kept the old 0.35, so the canyon came back with the fix in place. The
    ///   key was RENAMED rather than re-defaulted — see <see cref="UnseenWaferDrop"/>.</item>
    /// <item>A CHILD TRANSFORM SCALES ITS MESH ABOUT THE OBJECT ORIGIN, not the mesh's bounds
    ///   centre, so a seating offset is wrong by −centre.y·(1−squash) for every mesh whose bounds
    ///   centre is not at y = 0.</item>
    /// <item>A SHORT MATERIAL ARRAY makes Unity skip the extra submeshes, and an unbacked bevel
    ///   submesh renders its translucency over raw key. The copies' arrays are sized to the mesh's
    ///   subMeshCount and padded with dark; a one-shot line prints subMeshCount vs material counts.</item>
    /// <item>AN ALL-CLEAR FROM A CHANGE-GATED INSTRUMENT CAN BE EMPTY-SET NOISE. "UNBACKED PREVIEW
    ///   RENDERERS — none" printed six times while the region held no renderers at all, because the
    ///   gate hashed only the (empty) list; and it records ONLY renderers passing
    ///   <see cref="UnderPreviewNode"/>, whose depth cap the Apparance nesting can exceed. The gate
    ///   now folds in the live backing count and the cap is <see cref="PreviewAncestorScanDepth"/>.
    ///   (For the record, raising that cap from 12 to 40 added nothing — the count stayed 226 — so
    ///   the depth was never the blocker it was suspected of being.)</item>
    /// </list>
    /// The census (<see cref="CensusUnseenBorder"/>) and the property/render-state dumps
    /// (<see cref="DumpUnseenShaderProperties"/>, <see cref="EnsureUnseenMaterials"/>'s adaptive
    /// queue) stay as regression instruments: with the wafers in place the census must list NO
    /// un-backed tile-geometry translucent, the remaining candidates being floating particle FX
    /// that are deliberately left authored. The REGION NAME CENSUS in MixedReality.Diag.cs
    /// classifies every renderer by the live rule with every instance in exactly one bucket, so no
    /// piece can hide in a gap between counts.
    ///
    /// (The "round N" tags scattered through this file are provenance markers on individual
    /// decisions — each states its own fact. This block is the consolidated answer they add up to;
    /// the build-by-build chronology of rounds 3–17 lives in git history and .planning/STATE.md.)
    ///
    /// WHY AN UNDERLAY AND NOT FORCED-OPAQUE MATERIAL COPIES (the previous mechanism, replaced
    /// here): forcing Blend One/Zero on a copy rewires the shader's own output — the animated
    /// alpha pattern that gives the unseen hexes their pulsing look suddenly reads as
    /// full-intensity texture, a different look from the authored one. The underlay changes
    /// NOTHING about the authored rendering: the original renderer keeps its original materials,
    /// passes and animation, and merely blends against a mod-owned opaque dark mesh (same mesh,
    /// same transform, drawn at the end of the opaque range) instead of against the chroma key.
    /// Outside MR the same geometry blends against the unrendered near-black void, so dark IS the
    /// authored background — same look, same animation, no see-through. The underlay is a CHILD
    /// of its source renderer: Apparance's constant tile regeneration (226 rebuilds in the
    /// ModBuild-57 session) destroys and re-creates sources at will, and a child dies with its
    /// parent — teardown is structural. Per-slot: translucent slots get the dark plate, opaque
    /// slots get a draws-nothing filler (their own submesh already occludes; a coplanar dark copy
    /// would z-fight it). Renderers only — Lights are never touched (standing MR constraint), no
    /// game material is ever written, no <c>_Cull</c> is changed anywhere.
    ///
    /// Throttled on the sky sweep's cadence (the cheap prune/enabled-sync in
    /// <see cref="SyncUnseenUnderlays"/> runs every tick); config safety valve:
    /// <see cref="OpaquePreviewTiles"/> (default on, name kept from the preview-only round),
    /// restoring live when flipped off.
    /// </summary>
    private static void ForceUnseenOpaque()
    {
        if (!OpaquePreviewTiles.Value)
        {
            if (UnseenUnderlays.Count > 0)
                RestoreUnseenUnderlays();
            return;
        }

        EnsureUnseenMaterials();
        SyncUnseenUnderlays();

        // Live fill tuning (rounds 4+7): a changed [MixedReality] UnseenSkirtScale or
        // UnseenWaferDrop tears every underlay down and falls through to an immediate resweep,
        // so a hardware round can dial the groove fill in without a rebuild or an MR toggle.
        // Restore resets the scan throttle.
        float skirt = Mathf.Clamp(UnseenSkirtScale.Value, 1f, 2f);
        float drop = Mathf.Clamp(UnseenWaferDrop.Value, 0f, 2f);
        // Round 15: the rim curtain's two margins join the same live-retune gate — a hardware
        // round can dial the rim in from the cfg without a rebuild or an MR toggle.
        float rimInset = Mathf.Clamp(UnseenRimInset.Value, 0.005f, 0.2f);
        float rimClear = Mathf.Clamp(UnseenRimTopClearance.Value, 0.005f, 0.5f);
        bool debugColors = UnseenBackingDebugColors.Value; // round 16: same live-retune gate
        if (_appliedSkirtScale > 0f && UnseenUnderlays.Count > 0
            && (!Mathf.Approximately(skirt, _appliedSkirtScale)
                || !Mathf.Approximately(drop, _appliedWaferDrop)
                || !Mathf.Approximately(rimInset, _appliedRimInset)
                || !Mathf.Approximately(rimClear, _appliedRimClearance)
                || debugColors != _appliedDebugColors))
        {
            VRLog.Info("Core", $"MR: unseen fill tuning changed (scale {_appliedSkirtScale:0.###} → " +
                               $"{skirt:0.###}, drop {_appliedWaferDrop:0.###} → {drop:0.###} wu, " +
                               $"rim inset {_appliedRimInset:0.###} → {rimInset:0.###} wu, rim " +
                               $"clearance {_appliedRimClearance:0.###} → {rimClear:0.###} wu, debug " +
                               $"tint {_appliedDebugColors} → {debugColors}) — rebuilding every " +
                               "underlay + wafer + rim curtain.");
            // KEEP THE MATERIALS (round-16 bug fix). This path falls straight through into the
            // sweep BELOW, in the SAME call: the plain Restore destroys the shared dark/skip
            // materials and nulls the fields, so every backing rebuilt on this tick would have been
            // assigned NULL materials and drawn NOTHING — silently, permanently (the rebuilt
            // sources are back in UnseenSources, so no later sweep revisits them). Any live retune
            // therefore blanked the whole backing system until the next MR toggle, and it would
            // have blanked THIS round's debug tint the instant the key was flipped on — turning
            // "the tint is nowhere" into an artefact of the instrument instead of a finding.
            RestoreUnseenUnderlays(keepMaterials: true);
        }
        _appliedSkirtScale = skirt;
        _appliedWaferDrop = drop;
        _appliedRimInset = rimInset;
        _appliedRimClearance = rimClear;
        _appliedDebugColors = debugColors;


        if (Time.frameCount < _previewScanNextFrame)
            return;
        _previewScanNextFrame = Time.frameCount + SkyScanIntervalFrames;

        int unseenRenderers = 0;
        Renderer[] all = UnityEngine.Object.FindObjectsOfType<Renderer>(); // active renderers only
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null || !r.enabled)
                continue;
            int layer = r.gameObject.layer;
            if (layer == VRLayers.ModLayer || layer == 5) // never our own visuals / UI hosts
                continue;
            // Never match a mod-owned object — an underlay under a 'Preview' node would otherwise
            // match the Preview signal and grow an underlay of its own.
            if (r.gameObject.name.StartsWith("GloomhavenVR.", StringComparison.Ordinal))
                continue;

            Material[] mats = r.sharedMaterials;
            if (mats == null || mats.Length == 0)
                continue;
            // ROUND 3 family signal (hardware round 2: "die Animation drumrum ist immer noch
            // transparent"): the game names its whole fog-of-war kit 'Unseen' — the object names
            // ('EN_Unseen_…'), the hex shader ('Amp_Basic_Unseen') AND a dedicated ground-plane
            // shader the Player.log addressables list ships as 'UnseenGroundPlane_Shd'. Round 2
            // read only the SHADER name, so an unseen-family mesh running a differently-named
            // animated shader — or the ground plane if its material fails the blend probe below —
            // stayed uncovered. Any of GO name / material name / shader name now counts.
            bool family = HasUnseenName(r.gameObject.name);
            bool anyTranslucent = false;
            for (int mIdx = 0; mIdx < mats.Length; mIdx++)
            {
                Material? m = mats[mIdx];
                if (m == null)
                    continue;
                if (IsUnseenFamilyMaterial(m))
                {
                    family = true;
                    if (m!.renderQueue < _familyMinQueue)
                        _familyMinQueue = m.renderQueue; // round 6: EnsureUnseenMaterials adapts
                }
                if (IsTranslucent(m))
                    anyTranslucent = true;
            }
            if (!family && !(anyTranslucent && UnderPreviewNode(r.transform)))
            {
                // Round 13 instrument: a PREVIEW-DESCENDANT the sweep rejects is fog-of-war
                // stand-in content the mod leaves un-backed — exactly how the 'Simple Tile' rim
                // sides stayed green through twelve rounds. Collect it with the reason; the
                // change-gated dump below names it in the next hardware log.
                if (!family && UnderPreviewNode(r.transform))
                    RecordUnbacked(r, "opaque by probe+tag, not family-named");
                continue;
            }
            unseenRenderers++;
            if (UnseenSources.Contains(r.GetInstanceID()))
                continue;

            // Only a mesh can carry a same-mesh underlay. A family PARTICLE/TRAIL system is left
            // authored (standing instruction: never touch particle materials) — the census below
            // names it so the region backing can be verified/extended against the next log.
            if (r is MeshRenderer mesh)
            {
                BuildUnseenUnderlay(mesh, mats);
                if (!UnseenSources.Contains(r.GetInstanceID()))
                    RecordUnbacked(r, "matched but no dark slot / no mesh filter");
                else
                    _matchFamilyCount++;
            }
            else if (!(r is ParticleSystemRenderer) && !(r is TrailRenderer))
            {
                RecordUnbacked(r, "matched but not a MeshRenderer");
            }
        }

        // ROUND 16 — THE REGION-MEMBERSHIP ROUTE. Runs after the family/tag sweep so it can seed
        // itself from this sweep's matched set (see RegionMembershipPass for the anti-creep rule).
        RegionMembershipPass(all);

        if (UnseenUnderlays.Count != _loggedPreviewCount)
        {
            _loggedPreviewCount = UnseenUnderlays.Count;
            VRLog.Info("Core", $"MR: unseen GAP BACKING — {UnseenUnderlays.Count} renderer(s) carry " +
                               $"a coplanar dark underlay + a top-plane wafer (wafer = mesh-top − " +
                               $"{(_appliedWaferDrop >= 0f ? _appliedWaferDrop : Defaults.UnseenWaferDrop):0.###} wu, " +
                               $"XZ ×{(_appliedSkirtScale > 0f ? _appliedSkirtScale : 1f):0.###}, Y squash " +
                               $"{FillSquashY:0.###}; authored materials untouched; everything " +
                               "destroyed when MR turns off).");
            VRLog.Info("Core", $"MR: unseen MATCH ACCOUNTING (this MR session) — " +
                               $"{_matchFamilyCount} backed by FAMILY/TAG/PREVIEW (shader-, " +
                               $"material- or GO-name 'Unseen', a transparent-family RenderType " +
                               $"tag, or an active 'Preview' ancestor within " +
                               $"{PreviewAncestorScanDepth} levels), {_matchRegionCount} backed by " +
                               "REGION MEMBERSHIP (round 16: stands inside a family piece's AABB, " +
                               "below its top plane — the route that does not ask the material " +
                               "anything). Region candidates refused ON THE LAST SWEEP: " +
                               $"{_regionRejectAboveTop} " +
                               $"above the host piece's top plane (props standing ON the tiles), " +
                               $"{_regionRejectFigure} figures/actors (never touched), " +
                               $"{_regionRejectOversize} oversized (> ×{RegionMaxFootprintFactor:0.#} " +
                               $"the host footprint), {_regionRejectNonMesh} non-mesh " +
                               $"(particles/trails/lines/skinned), {_regionRejectNoBacking} with no " +
                               "backable slot. Mod-owned objects and the mod layer are excluded " +
                               "before any of these counters.");
        }

        // Round-15 rim instrument: how many curtains were built, how their silhouette was DERIVED
        // (hex inscribed in the mesh-local bounds vs the box fallback) and with which margins.
        // If the rim still glows with a healthy count here, the shape/margins are wrong; if the
        // count is 0 or 'box' dominates, the bounds source is wrong — the line separates the two.
        // Gated on the LIVE backing count (not the cumulative build count, which only grows under
        // Apparance regen churn) so it fires exactly beside the GAP BACKING line above.
        if (UnseenUnderlays.Count != _loggedRimCount)
        {
            _loggedRimCount = UnseenUnderlays.Count;
            int rimTotal = MrRimCurtain.BuiltHex + MrRimCurtain.BuiltBox;
            Vector3 sample = MrRimCurtain.LastMeshSize;
            VRLog.Info("Core", $"MR: unseen RIM CURTAIN — {rimTotal} mod-built dark prism(s) built " +
                               $"this MR session ({MrRimCurtain.BuiltHex} hex, {MrRimCurtain.BuiltBox} box " +
                               $"fallback, {MrRimCurtain.Skipped} skipped as too thin) stand " +
                               $"{(_appliedRimInset > 0f ? _appliedRimInset : Defaults.UnseenRimInset):0.###} wu " +
                               "INSIDE the authored side faces, from mesh-top − " +
                               $"{Mathf.Max(_appliedRimClearance > 0f ? _appliedRimClearance : Defaults.UnseenRimTopClearance, (_appliedWaferDrop >= 0f ? _appliedWaferDrop : Defaults.UnseenWaferDrop) + 0.005f):0.###} wu " +
                               $"(strictly under the wafer — no top face is ever painted) down to " +
                               $"mesh-bottom − {RimBottomDrop:0.###} wu. Bounds source: " +
                               "MeshFilter.sharedMesh.bounds in the source's LOCAL space (works on " +
                               "a non-CPU-readable mesh, immune to board rotation/scale); last " +
                               $"sample {MrRimCurtain.LastShape}, mesh size " +
                               $"({sample.x:0.###},{sample.y:0.###},{sample.z:0.###}).");
        }

        LogUnbackedPreview();
        CensusUnseenBorder(all);
        TickUnseenDiagnostics(all); // round 16: one-shot rim-population + camera dumps (MixedReality.Diag.cs)
    }

    /// <summary>Round-13 proof instrument: every Preview-descendant (or matched-but-skipped)
    /// renderer left WITHOUT backing, with the reason — collected during the sweep
    /// (<see cref="RecordUnbacked"/>) and dumped change-gated. The goal state is ZERO
    /// unexplained entries: whatever still glows at the rim must appear on this list with its
    /// shader/queue/RenderType tag, so the next round adds the missing signal instead of
    /// guessing. Deliberately-authored leftovers (family particles/trails) are excluded at the
    /// collection site.</summary>
    private static void RecordUnbacked(Renderer r, string reason)
    {
        for (int i = 0; i < UnbackedScratch.Count; i++)
        {
            if (ReferenceEquals(UnbackedScratch[i], r))
                return;
        }
        UnbackedScratch.Add(r);
        UnbackedReasons.Add(reason);
    }

    /// <summary>Dump the unbacked-preview list (change-gated on the set, 30 s rate floor,
    /// cleared per sweep). An empty list logs once per change too — that IS the goal state.</summary>
    private static void LogUnbackedPreview()
    {
        // ROUND 16 — the change gate now folds in the LIVE BACKING COUNT. Without it the empty
        // list always hashed to 17, so the six "none. (goal state)" lines in the ModBuild-74 log
        // were ALL emitted while the region was still empty (the GAP BACKING lines beside them
        // read "0 renderer(s)"), and once 226 pieces existed the gate suppressed the instrument
        // forever: a goal-state claim about a region that did not exist yet. The count makes the
        // all-clear re-print for the populated region — the only state it is evidence about.
        int hash = 17;
        for (int i = 0; i < UnbackedScratch.Count; i++)
            hash = hash * 31 + UnbackedScratch[i].GetInstanceID();
        hash = hash * 31 + UnseenUnderlays.Count;
        float now = Time.unscaledTime;
        if (hash == _unbackedLastHash || now < _unbackedNextAllowed)
        {
            UnbackedScratch.Clear();
            UnbackedReasons.Clear();
            return;
        }
        _unbackedLastHash = hash;
        _unbackedNextAllowed = now + CensusMinIntervalSeconds;

        if (UnbackedScratch.Count == 0)
        {
            VRLog.Info("Core", $"MR: UNBACKED PREVIEW RENDERERS — none, with " +
                               $"{UnseenUnderlays.Count} backed source(s) live. Every fog-of-war " +
                               "stand-in renderer THE SWEEP CAN SEE either carries a dark backing " +
                               "or is a deliberately-authored particle/trail. NOTE (round 16): this " +
                               "line only covers renderers with a 'Preview' ancestor within 12 " +
                               "levels — a piece deeper than that is invisible to it; the RIM " +
                               "POPULATION dump is the one that can see those.");
            return;
        }

        VRLog.Info("Core", $"MR: UNBACKED PREVIEW RENDERERS — {UnbackedScratch.Count} fog-of-war " +
                           "stand-in renderer(s) carry NO dark backing; whatever still glows at " +
                           "the rim must be here:");
        int listed = Mathf.Min(UnbackedScratch.Count, 12);
        for (int i = 0; i < listed; i++)
        {
            Renderer r = UnbackedScratch[i];
            Material? m = r != null ? r.sharedMaterial : null;
            string tag = m != null ? m.GetTag("RenderType", false, "<none>") : "<none>";
            VRLog.Info("Core", $"MR:   unbacked '{(r != null ? r.gameObject.name : "<dead>")}' " +
                               $"shader '{(r != null ? ShaderName(r) : "<none>")}' queue " +
                               $"{(m != null ? m.renderQueue : -1)} renderTypeTag '{tag}' mat " +
                               $"'{(m != null ? m.name : "<none>")}' — {UnbackedReasons[i]}.");
        }
        if (UnbackedScratch.Count > listed)
            VRLog.Info("Core", $"MR:   unbacked … +{UnbackedScratch.Count - listed} more.");
        UnbackedScratch.Clear();
        UnbackedReasons.Clear();
    }

    /// <summary>
    /// THE identification instrument for the remaining "Animation drumrum" transparency (hardware
    /// round 2): every translucent/additive/particle renderer whose AABB intersects the unseen
    /// region (union of the matched sources' AABBs, ±<see cref="CensusBorderWu"/> wu) and carries
    /// NO dark backing is listed by name, kind, shader, queue, slot count and bounds. The border
    /// animation the user still sees through MUST be in this list — or the list is empty, and the
    /// transparency then comes from the matched family itself (a vertex-animated pass overhanging
    /// its static underlay silhouette), which is the other hypothesis this census exists to tell
    /// apart. Change-gated on the candidate set (plus a rate floor) so a steady scene logs once
    /// per MR session; reset with the underlays.
    /// </summary>
    private static void CensusUnseenBorder(Renderer[] all)
    {
        if (UnseenUnderlays.Count == 0)
            return;

        // Union AABB of the matched unseen sources (coarse gate), plus the per-source list for
        // the fine test — both expanded by the border margin.
        CensusBoundsScratch.Clear();
        Bounds union = default;
        bool first = true;
        for (int i = 0; i < UnseenUnderlays.Count; i++)
        {
            Renderer src = UnseenUnderlays[i].Source;
            if (src == null)
                continue;
            Bounds b = src.bounds;
            b.Expand(CensusBorderWu * 2f);
            CensusBoundsScratch.Add(b);
            if (first)
            {
                union = b;
                first = false;
            }
            else
            {
                union.Encapsulate(b);
            }
        }
        if (first)
            return;

        CensusScratch.Clear();
        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null || !r.enabled)
                continue;
            int layer = r.gameObject.layer;
            if (layer == VRLayers.ModLayer || layer == 5)
                continue;
            if (r.gameObject.name.StartsWith("GloomhavenVR.", StringComparison.Ordinal))
                continue;
            if (UnseenSources.Contains(r.GetInstanceID()))
                continue; // already carries a dark backing
            Bounds rb = r.bounds;
            if (!union.Intersects(rb))
                continue;

            // "Could show the passthrough through itself": particles/trails/lines always qualify
            // (their material state is opaque to the blend probe), meshes qualify when any slot
            // is translucent or family-named.
            bool interesting = r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer;
            if (!interesting)
            {
                Material[] mats = r.sharedMaterials;
                if (mats != null)
                {
                    for (int mIdx = 0; mIdx < mats.Length; mIdx++)
                    {
                        if (IsTranslucent(mats[mIdx]) || IsUnseenFamilyMaterial(mats[mIdx]))
                        {
                            interesting = true;
                            break;
                        }
                    }
                }
            }
            if (!interesting)
                continue;

            for (int bIdx = 0; bIdx < CensusBoundsScratch.Count; bIdx++)
            {
                if (CensusBoundsScratch[bIdx].Intersects(rb))
                {
                    CensusScratch.Add(r);
                    break;
                }
            }
        }

        int hash = 17;
        for (int i = 0; i < CensusScratch.Count; i++)
            hash = hash * 31 + CensusScratch[i].GetInstanceID();
        float now = Time.unscaledTime;
        if (hash == _censusLastHash || now < _censusNextAllowed)
        {
            CensusScratch.Clear();
            return;
        }
        _censusLastHash = hash;
        _censusNextAllowed = now + CensusMinIntervalSeconds;

        if (CensusScratch.Count == 0)
        {
            VRLog.Info("Core", "MR: UNSEEN-BORDER CENSUS — no uncovered translucent/additive " +
                               "renderer intersects the unseen region (±1 wu). If an animation " +
                               "still reads transparent there, it comes from the MATCHED family " +
                               "itself — with the skirt applied (round 4) that means the margin " +
                               "is SHORT, not a renderer missed: raise [MixedReality] " +
                               $"UnseenSkirtScale (currently {_appliedSkirtScale:0.###}).");
            return;
        }

        VRLog.Info("Core", $"MR: UNSEEN-BORDER CENSUS — {CensusScratch.Count} translucent/additive " +
                           $"renderer(s) intersect the unseen region (±{CensusBorderWu:0.#} wu, " +
                           $"union center {union.center}, size {union.size}) and carry NO dark " +
                           "backing; the still-transparent border animation must be among these:");
        int listed = Mathf.Min(CensusScratch.Count, CensusMaxListed);
        for (int i = 0; i < listed; i++)
        {
            Renderer r = CensusScratch[i];
            string kind = r switch
            {
                ParticleSystemRenderer => "particles",
                TrailRenderer => "trail",
                LineRenderer => "line",
                SkinnedMeshRenderer => "skinned",
                MeshRenderer => "mesh",
                _ => r.GetType().Name,
            };
            Material? m = r.sharedMaterial;
            Bounds b = r.bounds;
            VRLog.Info("Core", $"MR:   census '{r.gameObject.name}' [{kind}] shader " +
                               $"'{ShaderName(r)}' queue {(m != null ? m.renderQueue : -1)} " +
                               $"slot(s) {(r.sharedMaterials != null ? r.sharedMaterials.Length : 0)} " +
                               $"size {b.size} @ {b.center}.");
        }
        if (CensusScratch.Count > listed)
            VRLog.Info("Core", $"MR:   census … +{CensusScratch.Count - listed} more.");
        CensusScratch.Clear();
    }

    /// <summary>
    /// Supplemental region membership for the documented native Simple Tile fog cliff.
    /// Build 530 narrows the historical round-16 "every overlapping mesh" rule: proximity alone
    /// admitted revealed cliff dressing and filled the transparent rectangles of forest foliage.
    ///
    /// <para>WHY. Sixteen rounds widened MATERIAL predicates — shader name, material name, GO name,
    /// blend probe, render queue, RenderType tag — and none of them ever caught the piece the user
    /// photographs. The ModBuild-74 MAPTILE dumps settle what that piece is by arithmetic: a
    /// preview subtree WITH backings holds 603 / 225 / 171 renderers (all exactly 9 per hex) while
    /// one with content but no backings holds 66 = 22×3, so the kit is THREE authored renderers per
    /// hex and the mod backs TWO of them. The unbacked third is 'Simple Tile', whose bounds span
    /// the block's full height (y −0.4..−0.1) — the region's outer CLIFF. It advertises nothing:
    /// not family-named, opaque to the blend probe, opaque-range queue, no transparent tag. Asking
    /// "what does this material look like" cannot find it; asking "does this renderer stand inside
    /// the unseen region" can, and the answer comes from geometry the game cannot hide.</para>
    ///
    /// <para>ANTI-CREEP (the one rule that makes this safe to iterate). The region is seeded ONLY
    /// from FAMILY/TAG sources (<see cref="UnseenUnderlay.ViaRegion"/> == false). If adopted pieces
    /// could seed further adoption, every sweep would grow the region by one AABB and the backing
    /// would walk across the board. Seeded this way the region's extent is fixed by the game's own
    /// fog-of-war geometry and cannot expand, no matter how many sweeps run.</para>
    ///
    /// <para>RAILS, each counted by reason so the next log can audit them: never a figure (the
    /// shared clause list in <see cref="FigureRendererGuard"/> — skinned outright, a prop the
    /// player is HOLDING, plus ActorBehaviour / CInteractableActor / Animator ancestors; the
    /// held-prop clause was missing here until 2026-09-05 and a carried chest could take a
    /// permanent dark plate because of it), never mod-owned (name prefix or mod layer), never a
    /// particle/trail/line, never something reaching above its host piece's own top plane (that is
    /// what keeps chests, clutter and roots STANDING ON the tiles out of it), and never a footprint
    /// more than <see cref="RegionMaxFootprintFactor"/>× the host hex (no room-spanning floor, no FX
    /// volume). Adopted pieces get the same underlay + wafer + rim curtain as everyone else, the
    /// same enabled-mirroring in <see cref="SyncUnseenUnderlays"/>, and the same destruction on MR
    /// off; no authored material is touched here either.</para>
    /// </summary>
    private static void RegionMembershipPass(Renderer[] all)
    {
        if (UnseenUnderlays.Count == 0)
            return;

        // A revealed or disabled family source no longer authorizes supplemental backing.
        // Revalidate against this sweep's live family set, not its historical adoption pose.
        bool hasRegion = SeedRegionBounds();
        for (int i = UnseenUnderlays.Count - 1; i >= 0; i--)
        {
            UnseenUnderlay entry = UnseenUnderlays[i];
            if (!entry.ViaRegion) continue;
            if (UnseenRegionMembership.Value && hasRegion && entry.Source != null
                && ClassifyRegion(entry.Source.bounds, out _, out _, out _) == RegionVerdict.Inside)
                continue;
            if (entry.Plate != null) UnityEngine.Object.Destroy(entry.Plate.gameObject);
            if (entry.Fill != null) UnityEngine.Object.Destroy(entry.Fill.gameObject);
            if (entry.Rim != null) UnityEngine.Object.Destroy(entry.Rim.gameObject);
            UnseenSources.Remove(entry.SourceId);
            UnseenUnderlays.RemoveAt(i);
        }
        if (!UnseenRegionMembership.Value || !hasRegion)
            return;

        // The REFUSAL counters are per-sweep: the same particle system or prop is re-examined every
        // sweep, so a cumulative count would inflate by a factor of the session length and say
        // nothing. The two MATCH counters stay cumulative — they count real builds, and Apparance
        // regen genuinely rebuilds pieces.
        _regionRejectAboveTop = 0;
        _regionRejectFigure = 0;
        _regionRejectOversize = 0;
        _regionRejectNonMesh = 0;
        _regionRejectNoBacking = 0;

        for (int i = 0; i < all.Length; i++)
        {
            Renderer r = all[i];
            if (r == null || !r.enabled)
                continue;
            int layer = r.gameObject.layer;
            if (layer == VRLayers.ModLayer || layer == 5)
                continue;
            if (r.gameObject.name.StartsWith("GloomhavenVR.", StringComparison.Ordinal))
                continue;
            if (UnseenSources.Contains(r.GetInstanceID()))
                continue; // already backed by either route

            Bounds rb = r.bounds;
            if (!_regionUnion.Intersects(rb)) // coarse gate: nearly every scene renderer dies here
                continue;

            if (!(r is MeshRenderer mesh))
            {
                // Particles/trails/lines stay authored (standing instruction); a SkinnedMeshRenderer
                // is a figure by the guard's own first clause. Counted, never touched.
                _regionRejectNonMesh++;
                continue;
            }

            RegionVerdict verdict = ClassifyRegion(rb, out _, out _, out _);
            if (verdict != RegionVerdict.Inside)
            {
                if (verdict == RegionVerdict.AboveTop)
                    _regionRejectAboveTop++;
                else if (verdict == RegionVerdict.Oversize)
                    _regionRejectOversize++;
                continue;
            }

            // FIGURE GUARD LAST of the rails, deliberately: it is the only expensive test here
            // (three ancestor walks) and this is the point where the candidate would otherwise be
            // adopted — so it runs a few times per sweep instead of a few hundred, and its counter
            // means "figures we refused to swallow", not "figures that happened to be nearby".
            // Since 2026-09-05 it also refuses a prop the player is HOLDING: this rail is where a
            // carried chest used to fall through into forceDark and pick up a dark backing plate
            // that then followed it home, because teardown only fires when the source renderer
            // dies. _regionRejectFigure therefore now counts held props too.
            if (IsFigureOrActorRenderer(r))
            {
                _regionRejectFigure++;
                continue;
            }

            Material[] mats = r.sharedMaterials;
            if (mats == null)
                mats = System.Array.Empty<Material>();
            // Build 530: AABB overlap is a proximity test, not evidence that an authored
            // renderer belongs to fog. In particular hanging forest foliage sits below the
            // neighboring hex top too. Its opaque backing erased the alpha-cutout silhouette.
            // Only the documented shader-agnostic cliff block may take this fallback route.
            if (!CanBackRegion(r, mats))
                continue;
            // FORCE DARK: the whole point is that this piece's slots do NOT read see-through to the
            // probe — the per-slot rule would find nothing to back and skip it, which is exactly
            // how it stayed green for sixteen rounds. Inside the region, below the top plane, past
            // every rail above, dark IS the authored look (outside MR the same geometry blends
            // against the unrendered near-black void).
            BuildUnseenUnderlay(mesh, mats, viaRegion: true, forceDark: true);
            if (UnseenSources.Contains(r.GetInstanceID()))
            {
                _matchRegionCount++;
                if (RegionAdoptedNames.Count < RegionAdoptedNameCap
                    && RegionAdoptedNames.Add(r.gameObject.name))
                {
                    Material? m = r.sharedMaterial;
                    VRLog.Info("Core", $"MR: REGION MEMBERSHIP backed '{r.gameObject.name}' — " +
                                       $"shader '{ShaderName(r)}' mat " +
                                       $"'{(m != null ? m.name : "<none>")}' queue " +
                                       $"{(m != null ? m.renderQueue : -1)} renderTypeTag " +
                                       $"'{(m != null ? m.GetTag("RenderType", false, "<none>") : "<none>")}' " +
                                       $"bounds s{r.bounds.size} @ {r.bounds.center}. This piece " +
                                       "advertises nothing the material predicates could match; it " +
                                       "was caught because it STANDS in the unseen region.");
                }
            }
            else
            {
                _regionRejectNoBacking++;
            }
        }
    }

    private static bool CanBackRegion(Renderer source, Material[] materials)
    {
        bool cutout = false;
        for (int i = 0; i < materials.Length; i++)
            if (materials[i] != null && (materials[i].GetTag("RenderType", false, string.Empty) == "TransparentCutout"
                || materials[i].IsKeywordEnabled("_ALPHATEST_ON"))) cutout = true;
        return MrUnseenRegionEligibility.Allows(source.gameObject.name, cutout);
    }

    /// <summary>Verdict of <see cref="ClassifyRegion"/> — shared by the region route and the
    /// round-17 name census, so the census can never report a rail the route does not apply.</summary>
    private enum RegionVerdict
    {
        /// <summary>Overlaps no host piece at all.</summary>
        Outside,

        /// <summary>Stands ON/above the tiles rather than being part of the block.</summary>
        AboveTop,

        /// <summary>Footprint far larger than its host hex.</summary>
        Oversize,

        /// <summary>A member of the region — gets the backing.</summary>
        Inside,
    }

    /// <summary>Rebuild <see cref="RegionBoundsScratch"/> / <see cref="RegionTopScratch"/> /
    /// <see cref="RegionBottomScratch"/> and <see cref="_regionUnion"/> from the FAMILY/TAG sources
    /// only (anti-creep — see <see cref="RegionMembershipPass"/>). Returns false when the region is
    /// empty. Cheap and idempotent within a sweep, so the census can call it too.</summary>
    private static bool SeedRegionBounds()
    {
        RegionBoundsScratch.Clear();
        RegionTopScratch.Clear();
        RegionBottomScratch.Clear();
        bool first = true;
        for (int i = 0; i < UnseenUnderlays.Count; i++)
        {
            UnseenUnderlay e = UnseenUnderlays[i];
            if (e.ViaRegion || e.Source == null || !e.Source.enabled
                || !e.Source.gameObject.activeInHierarchy)
                continue;
            Bounds b = e.Source.bounds;
            RegionTopScratch.Add(b.max.y);
            RegionBottomScratch.Add(b.min.y);
            b.Expand(RegionMembershipSlackWu * 2f);
            RegionBoundsScratch.Add(b);
            if (first)
            {
                _regionUnion = b;
                first = false;
            }
            else
            {
                _regionUnion.Encapsulate(b);
            }
        }
        return !first;
    }

    /// <summary>
    /// THE REGION RULE, in one place (round 17). Returns the verdict for <paramref name="rb"/> plus,
    /// for the best-fitting host, how far the candidate's top overshoots that host's top plane
    /// (<paramref name="overshoot"/>) and how its bottom compares to the host's bottom
    /// (<paramref name="bottomDelta"/>) — the two numbers the census prints, so the next rail change
    /// is designed from measurements instead of from a guess.
    ///
    /// <para>THE VERTICAL RAIL (round 17, replacing a single top-plane test). The rail exists to
    /// keep things that STAND ON the tiles — chests, clutter, roots, walls — from being darkened,
    /// and the round-16 version did that with one clause: the candidate's top may not rise above
    /// its host's top plane. That also refuses the very piece the route was written for, because
    /// the CLIFF BLOCK's top is level with the hex it belongs to and a hair of it may round either
    /// way. The rule now separates the two cases by what they do at the BOTTOM, which is where they
    /// genuinely differ:</para>
    /// <list type="bullet">
    /// <item>a prop STANDING ON the tile has its bottom AT the host's TOP plane and its top well
    ///   above it → refused;</item>
    /// <item>the block that FORMS the tile spans the host: bottom at or below the host's bottom,
    ///   top level with the host's top (not above it) → accepted.</item>
    /// </list>
    /// <para>So: accept when the candidate stays under the host's top plane
    /// (<see cref="RegionTopToleranceWu"/>) OR when it spans the host — bottom within
    /// <see cref="RegionBottomToleranceWu"/> of the host's bottom AND top within
    /// <see cref="RegionLevelToleranceWu"/> of the host's top. A prop can satisfy neither: to pass
    /// the second clause it would have to reach down to the tile's underside, at which point it is
    /// part of the tile block by any reasonable reading.</para>
    /// </summary>
    private static RegionVerdict ClassifyRegion(Bounds rb, out int hostIndex, out float overshoot,
                                                out float bottomDelta)
    {
        hostIndex = -1;
        overshoot = 0f;
        bottomDelta = 0f;
        bool anyOverlap = false;
        bool aboveTop = false;
        bool oversize = false;
        float bestOvershoot = float.MaxValue;

        for (int b = 0; b < RegionBoundsScratch.Count; b++)
        {
            if (!RegionBoundsScratch[b].Intersects(rb))
                continue;
            anyOverlap = true;
            float over = rb.max.y - RegionTopScratch[b];
            float below = rb.min.y - RegionBottomScratch[b];

            bool underTop = over <= RegionTopToleranceWu;
            bool spansHost = below <= RegionBottomToleranceWu && over <= RegionLevelToleranceWu;
            if (!underTop && !spansHost)
            {
                aboveTop = true;
                if (over < bestOvershoot) // remember the least-bad host for the census
                {
                    bestOvershoot = over;
                    hostIndex = b;
                    overshoot = over;
                    bottomDelta = below;
                }
                continue;
            }

            Vector3 hostSize = RegionBoundsScratch[b].size;
            if (rb.size.x > hostSize.x * RegionMaxFootprintFactor
                || rb.size.z > hostSize.z * RegionMaxFootprintFactor)
            {
                oversize = true;
                if (hostIndex < 0)
                {
                    hostIndex = b;
                    overshoot = over;
                    bottomDelta = below;
                }
                continue;
            }

            hostIndex = b;
            overshoot = over;
            bottomDelta = below;
            return RegionVerdict.Inside;
        }

        if (!anyOverlap)
            return RegionVerdict.Outside;
        return aboveTop ? RegionVerdict.AboveTop : oversize ? RegionVerdict.Oversize : RegionVerdict.Outside;
    }

    /// <summary>
    /// FIGURES ARE NEVER TOUCHED. Over-broad on purpose (fail-open = the renderer keeps rendering
    /// normally). An actor standing in an unexplored room cannot get a dark backing, whatever its
    /// bounds overlap.
    ///
    /// <para><b>THIS WAS A HAND-MAINTAINED MIRROR AND THE MIRROR BROKE.</b> The doc that stood here
    /// said "Deliberate MIRROR of the wall system's guard … Keep the two in step", and gave the
    /// reason: the wall's copy is private to a nested type in another file, so the rule was
    /// re-stated at every sweep rather than shared across a module boundary. Then ModBuild 340 added
    /// a FIFTH clause to the wall's copy — a prop the player is HOLDING is not scenery, for the
    /// report <i>"ich sehe zwar einen Geist aber in der Hand ist es garnicht oder nur immer ganz
    /// kurz für einen Frame sichtbar"</i> — and stepped one and not the other. This copy stayed at
    /// four clauses and <c>rg -c HeldProps</c> over this file returned zero.</para>
    ///
    /// <para><b>WHAT THAT COST, and it is not hypothetical:</b> carry a chest or a gold pile over an
    /// unexplored region for ~0.7 s (RegionMembershipPass runs on a 60-frame cadence) and the held
    /// prop fails this guard, takes <c>BuildUnseenUnderlay(…, forceDark: true)</c>, and acquires a
    /// permanent dark backing plate that follows it home — teardown fires only when the source
    /// renderer dies. A held MINIATURE was immune the whole time, because it is a
    /// <see cref="SkinnedMeshRenderer"/>.</para>
    ///
    /// <para><b>SO THE CLAUSE LIST IS NO LONGER MIRRORED, IT IS SHARED</b>
    /// (<see cref="FigureRendererGuard"/>, redundancy survey R9). The wall system keeps its own
    /// entry point because it fronts the ancestor half with a pass-scoped memo this sweep has no
    /// use for; what both now read from one place is WHICH CLAUSES THERE ARE. The severity argument
    /// in the old doc was right and is the reason this is shared rather than re-stated: a rule this
    /// severe must not depend on somebody noticing a second copy.</para>
    /// </summary>
    private static bool IsFigureOrActorRenderer(Renderer r) =>
        FigureRendererGuard.IsFigureOrActorRenderer(r);

    /// <summary>
    /// Build the opaque dark underlay for one matched source renderer: a mod-owned CHILD sharing
    /// the source's mesh and full transform, dark plate material on the translucent slots,
    /// draws-nothing filler on the rest (see <see cref="ForceUnseenOpaque"/> for why). Skipped
    /// silently when the source has no MeshFilter mesh to clone.
    ///
    /// <para><paramref name="viaRegion"/> records WHICH route caught this source (round 16) — the
    /// region is seeded only from the family/tag route, see <see cref="RegionMembershipPass"/>.
    /// <paramref name="forceDark"/> gives every slot the dark plate instead of consulting the
    /// per-slot probe: a region-adopted piece is here precisely BECAUSE its material tells the mod
    /// nothing, so the probe would find no backable slot and skip it.</para>
    /// </summary>
    private static void BuildUnseenUnderlay(MeshRenderer source, Material[] mats,
                                            bool viaRegion = false, bool forceDark = false)
    {
        MeshFilter? filter = source.GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null)
            return;

        // Per-slot dark rule (round 3): a slot earns the dark plate when the blend probe reads it
        // translucent OR when it is family-named — the family IS the see-through class by the
        // game's own naming, and a family shader that hardcodes its blend in the pass (no
        // '_DstBlend' property, queue ≤ 2500) is invisible to the probe. A dark plate under a
        // slot that turns out genuinely opaque is covered by that slot's own later draw (family
        // materials render at ≥ our 2500) — harmless; a SKIPPED see-through slot is the reported
        // bug. Slots that are neither stay on the draws-nothing filler (their opaque submesh
        // already occludes; a coplanar dark copy would z-fight it).
        //
        // SUBMESH COVERAGE (round 12): the copy's material array is sized to the MESH's
        // subMeshCount, not just the source's material count. With fewer materials than
        // submeshes Unity renders only the first N submeshes — a copy inheriting a short array
        // would leave the extra submeshes (bevels/rims) UNBACKED, semi-transparent over raw
        // key: exactly the wide green bands the round-12 screenshot showed on the hex bevels.
        // Extra slots get the DARK plate (they belong to see-through family geometry); the
        // one-shot sample line below proves the counts in the next hardware log.
        // ROUND-16 DEBUG TINT: the plate and the wafer no longer share one material array — in
        // debug mode each backing CLASS gets its own signal colour (blue plate / magenta wafer /
        // red rim). Outside debug mode all three resolve to the same dark material and the arrays
        // are element-wise identical, so the rendering is bit-identical to round 15.
        Material underlayMat = _dbgUnderlayMat != null ? _dbgUnderlayMat : _unseenDarkMat!;
        Material waferMat = _dbgWaferMat != null ? _dbgWaferMat : _unseenDarkMat!;
        Material rimMat = _dbgRimMat != null ? _dbgRimMat : _unseenDarkMat!;

        int slots = Mathf.Max(filter.sharedMesh.subMeshCount, mats.Length);
        var plateMats = new Material[slots];
        var fillMats = new Material[slots];
        int backed = 0;
        for (int i = 0; i < slots; i++)
        {
            bool dark = forceDark || i >= mats.Length
                        || IsTranslucent(mats[i]) || IsUnseenFamilyMaterial(mats[i]);
            plateMats[i] = dark ? underlayMat : _unseenSkipMat!;
            fillMats[i] = dark ? waferMat : _unseenSkipMat!;
            if (dark)
                backed++;
        }
        if (backed == 0)
            return; // GO-name family with all-opaque, non-family slots: nothing to back
        if (!_submeshDiagLogged)
        {
            _submeshDiagLogged = true;
            VRLog.Info("Core", $"MR: unseen backing sample '{source.gameObject.name}' — mesh " +
                               $"submeshes {filter.sharedMesh.subMeshCount}, source mats " +
                               $"{mats.Length}, backing mats {slots} (padded " +
                               $"{slots - mats.Length} with dark) — the copies must cover every " +
                               "submesh or the uncovered ones read as key-colored bands.");
        }

        // THE PRIMARY UNDERLAY — exact 1:1 again (round 7). Round 4 scaled THIS copy as the
        // "skirt"; round 5 proved the animation is UV-scroll (cannot leave the silhouette, so
        // the margin bought nothing), and the round-7 screenshot exposed what it cost: a copy
        // displaced about the mesh center no longer sits coplanar behind the beveled groove
        // faces, so a grazing ray slips through the parallax gap between the translucent bevel
        // and its shifted backing, into the V-channel, onto the key — the glowing green grooves.
        // Coplanar means every surface pixel of the piece is backed from EVERY view direction.
        var go = new GameObject("GloomhavenVR.MrUnseenUnderlay");
        go.transform.SetParent(source.transform, worldPositionStays: false);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        go.layer = source.gameObject.layer;
        go.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
        var plate = go.AddComponent<MeshRenderer>();
        plate.sharedMaterials = plateMats;
        plate.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        plate.receiveShadows = false;
        plate.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        plate.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        // THE GAP BACKING WAFER (round 11, reshaped from the round-7 deep groove fill after the
        // ModBuild-68 MAPTILE dumps pinned the geometry: hex tops at ~y−0.1, the fills a full
        // 0.3–0.45 BELOW the top plane at y−0.7..−0.4 — so at viewing angles from above, rays
        // through the inter-hex gaps crossed the deep canyon and reached the key before any
        // fill: the bright green seams around every unseen hex in wall_und_mixed.png). The fill
        // is now a WAFER: the same mesh XZ-widened by the (tunable) skirt factor, Y-SQUASHED to
        // FillSquashY (all relief collapsed — a flat, hex-silhouette slab; no rectangle, per
        // the standing user ruling, and no squashed bevel can poke past the authored top), and
        // seated at the piece's OWN mesh-top plane minus UnseenWaferDrop (~2 cm):
        // every gap pixel from above hits dark within millimetres, at any angle, while the
        // authored translucent rim animation above it blends against dark instead of key —
        // exactly the user-approved model ("the green comes only from the background"). The
        // widened wafers of adjacent hexes overlap under the seam; the outer silhouette gains
        // at most ~10 % of a hex (≤0.15 wu) of dark ledge. ZTest LEqual, no depth write —
        // revealed geometry occludes the wafer exactly like the plate; strictly below the
        // authored top surface, never coplanar (no z-fighting).
        float skirt = _appliedSkirtScale > 0f ? _appliedSkirtScale : 1f;
        float drop = _appliedWaferDrop >= 0f ? _appliedWaferDrop : Defaults.UnseenWaferDrop;
        Bounds mb = filter.sharedMesh.bounds;
        Vector3 meshCenter = mb.center;
        var fillGo = new GameObject("GloomhavenVR.MrUnseenFill");
        fillGo.transform.SetParent(source.transform, worldPositionStays: false);
        fillGo.transform.localRotation = Quaternion.identity;
        fillGo.transform.localScale = new Vector3(skirt, FillSquashY, skirt);
        // SEATING — WHAT THIS OFFSET ACTUALLY DOES (round-16 analysis, kept because it explains the
        // geometry; its CONCLUSION was overruled by hardware — see the ROUND 17 note below, which
        // is the binding one). A child transform scales its mesh about the OBJECT ORIGIN, not about
        // the mesh's bounds centre: a vertex y maps to localPosition.y + squash·y. The offset
        // (top − centre)(1 − squash) assumes a pivot at the bounds CENTRE, so it seats the wafer
        // −centre.y·0.98 HIGHER than the mesh top, and lands ON the top plane only for meshes whose
        // bounds centre sits at y = 0. Measured in the ModBuild-74 MAPTILE dump:
        // 'EN_Unseen_FloorHex_Edge_Damage_03_PR' (world y −0.4..−0.1, mesh height 0.318 ⇒
        // centre.y ≈ −0.159) carries 'GloomhavenVR.MrUnseenFill'[y0.1..0.1] — the ×1.2-widened dark
        // slab sits ~0.16 wu ABOVE the hex tops — while the sibling class
        // 'EN_CR_FloorTiles_Damaged_03' (centre.y ≈ 0) sits at y−0.1, on its own top plane. That
        // RAISED slab is not a defect: it is what closes the seams from above, and it is the look
        // the user approved. XZ needs no such note — keeping the mesh CENTRE fixed under the
        // widening is centre·(1 − skirt) for a pivot at the origin too.
        fillGo.transform.localPosition = new Vector3(
            meshCenter.x * (1f - skirt),
            // ROUND 17 — DO NOT "FIX" THIS AGAIN. Round 16 called this offset mis-seated
            // (a child scales its mesh about the OBJECT ORIGIN, so the geometrically correct
            // seating for a pivot at the origin is top·(1 − squash)) and corrected it. The
            // hardware verdict was an immediate regression: "die Lücken sind nun wieder
            // vollständig da wie zuvor". The seam coverage the user approved in ModBuild 70
            // ("Die Lücken oben sind geschlossen und sieht gut aus top!") comes from THIS
            // offset — for the 'EN_Unseen_FloorHex_Edge_Damage_03_PR' class it lifts the
            // ×1.2-widened dark slab ABOVE the hex tops, and that is what actually closes
            // the seams from above. Geometric correctness is not the goal here; the user's
            // approved look is. Any future change to the seam coverage must be additive and
            // must leave this value alone.
            (mb.max.y - meshCenter.y) * (1f - FillSquashY),
            meshCenter.z * (1f - skirt));
        fillGo.transform.position += Vector3.down * drop; // WORLD drop, whatever the parent pose
        fillGo.layer = source.gameObject.layer;
        fillGo.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
        var fill = fillGo.AddComponent<MeshRenderer>();
        fill.sharedMaterials = fillMats;
        fill.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        fill.receiveShadows = false;
        fill.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        fill.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;

        // THE RIM CURTAIN (round 15) — the ONE defect round 13 left open: the region's outer
        // VERTICAL side faces (the tile blocks' HEIGHT, log: 'Simple Tile'[y−0.4..−0.1]) still
        // read translucent-over-key, while round 13's own instrument reported every one of those
        // pieces BACKED ("UNBACKED PREVIEW RENDERERS — none"). Backing present + no dark pixels
        // ⇒ the same-mesh copy cannot render there, and round 14 proved the suspected cause
        // (authored vertex alpha, which both candidate shaders multiply) is UNTESTABLE and
        // UNFIXABLE on these meshes: they are not CPU-readable. So the rim gets geometry the MOD
        // builds — a mesh with no color channel samples white, and the dark material renders
        // unconditionally. Shape and clearances are contracted in MrRimCurtain; the two that
        // matter here: the curtain is XZ-INSET (never past the outer silhouette — the standing
        // "no alien dark ledge" ruling) and its cap is forced BELOW the wafer plane, so the
        // approved round-13 top look is provably untouched.
        float rimInset = _appliedRimInset > 0f ? _appliedRimInset : Defaults.UnseenRimInset;
        float rimClear = _appliedRimClearance > 0f ? _appliedRimClearance : Defaults.UnseenRimTopClearance;
        rimClear = Mathf.Max(rimClear, drop + 0.005f); // INVARIANT: strictly under the wafer
        Renderer? rim = MrRimCurtain.Build(
            source, filter.sharedMesh, rimMat, rimInset, rimClear, RimBottomDrop);

        int id = source.GetInstanceID();
        var entry = new UnseenUnderlay
        {
            Source = source,
            SourceId = id,
            Plate = plate,
            Fill = fill,
            Rim = rim,
            ViaRegion = viaRegion,
        };

        UnseenUnderlays.Add(entry);
        UnseenSources.Add(id);
        for (int i = 0; i < mats.Length; i++)
        {
            if (IsUnseenFamilyMaterial(mats[i]))
                DumpUnseenShaderProperties(mats[i]!);
        }
        if (_unseenVerboseLogs < UnseenVerboseLogCap)
        {
            _unseenVerboseLogs++;
            VRLog.Info("Core", $"MR: unseen geometry '{source.gameObject.name}' backed by an " +
                               $"opaque dark underlay ({backed} of {mats.Length} slot(s), shader " +
                               $"'{ShaderName(source)}') — the fog-of-war look stays authored, " +
                               "the passthrough room can no longer show through it." +
                               (_unseenVerboseLogs == UnseenVerboseLogCap
                                   ? " (Further builds counted, not listed — tile regen churn.)"
                                   : string.Empty));
        }
    }

    /// <summary>
    /// Margin-derivation instrument (once per shader name per session): the skirt's default
    /// factor is INFERRED — the game bundles are not readable offline (ressources/ carries only
    /// Managed DLLs, tools/ShaderDisasm has no Unseen entry), so the authored wave amplitude of
    /// the family shaders is unknown. This dump prints every float/range/vector/color property
    /// of a matched family material with its LIVE value into the hardware log; if an
    /// amplitude/displacement property shows up there, the next round replaces the guessed
    /// <see cref="UnseenSkirtScale"/> default with a value read from the asset.
    /// </summary>
    private static void DumpUnseenShaderProperties(Material m)
    {
        // Round 8: dedup by MATERIAL name (was shader name) — the edge pieces' materials share
        // the hex shader and never got their color properties dumped; the key-dodge needs every
        // family material's authored colors on record.
        if (m.shader == null || DumpedUnseenShaders.Count >= 8 || !DumpedUnseenShaders.Add(m.name))
            return;

        var sb = new System.Text.StringBuilder(256);
        int count = m.shader.GetPropertyCount();
        int listed = 0;
        for (int i = 0; i < count && listed < 24; i++)
        {
            string name = m.shader.GetPropertyName(i);
            UnityEngine.Rendering.ShaderPropertyType type = m.shader.GetPropertyType(i);
            if (!m.HasProperty(name))
                continue;
            switch (type)
            {
                case UnityEngine.Rendering.ShaderPropertyType.Float:
                case UnityEngine.Rendering.ShaderPropertyType.Range:
                    sb.Append(' ').Append(name).Append('=').Append(m.GetFloat(name).ToString("0.###"));
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Vector:
                    Vector4 v = m.GetVector(name);
                    sb.Append(' ').Append(name).Append('=')
                      .Append($"({v.x:0.###},{v.y:0.###},{v.z:0.###},{v.w:0.###})");
                    break;
                case UnityEngine.Rendering.ShaderPropertyType.Color:
                    Color c = m.GetColor(name);
                    sb.Append(' ').Append(name).Append('=')
                      .Append($"rgba({c.r:0.##},{c.g:0.##},{c.b:0.##},{c.a:0.##})");
                    break;
                default:
                    continue; // textures/ints carry no wave amplitude
            }
            listed++;
        }

        // Round 6: the render-state half — the from-above/from-below asymmetry hypotheses hinge
        // on the family's queue and depth/blend state, so pin them. A state name that is not a
        // material property is hardcoded in the compiled pass (the serialized pass state for
        // Amp_Basic_Unseen — zTest LEqual, zWrite On/Off across its two FORWARD passes — is in
        // tools/ShaderOcclusionPatcher/README.md, read from the shipped bundles).
        var state = new System.Text.StringBuilder(96);
        state.Append(" queue=").Append(m.renderQueue)
             .Append(" renderTypeTag='").Append(m.GetTag("RenderType", false, "<none>")).Append('\'');
        string[] stateProps = { "_ZWrite", "_ZTest", "_SrcBlend", "_DstBlend", "_Cull" };
        for (int i = 0; i < stateProps.Length; i++)
        {
            state.Append(' ').Append(stateProps[i]).Append('=');
            if (m.HasProperty(stateProps[i]))
                state.Append(m.GetFloat(stateProps[i]).ToString("0.#"));
            else
                state.Append("hardcoded");
        }

        VRLog.Info("Core", $"MR: UNSEEN-SHADER PROPERTIES '{m.shader.name}' (material '{m.name}', " +
                           $"{count} propert(ies)):{state} |{sb} — this line pins the family's " +
                           "queue/depth/blend behaviour AND names the animation mechanism " +
                           "(round 5 read UV-scroll off it).");
    }

    /// <summary>
    /// Per-tick bookkeeping for the underlays: drop entries whose source died (the underlay child
    /// died with it — Apparance regen; the next throttled sweep re-backs the replacements) and
    /// mirror the source's <c>enabled</c> flag onto the plate, so anything that fades or disables
    /// an unseen renderer (reveal transitions, the mod's own visibility systems) never leaves a
    /// bare dark slab behind.
    /// </summary>
    private static void SyncUnseenUnderlays()
    {
        for (int i = UnseenUnderlays.Count - 1; i >= 0; i--)
        {
            UnseenUnderlay e = UnseenUnderlays[i];
            if (e.Source == null || e.Plate == null)
            {
                if (e.Plate != null) // source renderer died alone (component removal) — clean up
                    UnityEngine.Object.Destroy(e.Plate.gameObject);
                // The fill and the rim are CHILDREN of the source, exactly as the plate is, so a
                // piece that is destroyed structurally takes all three with it. This branch is the
                // one case that does not: a renderer removed as a COMPONENT leaves its GameObject
                // and therefore its children standing, so they are destroyed explicitly here.
                if (e.Fill != null) // like the plate: source died alone — clean up the children
                    UnityEngine.Object.Destroy(e.Fill.gameObject);
                if (e.Rim != null)
                    UnityEngine.Object.Destroy(e.Rim.gameObject);
                UnseenSources.Remove(e.SourceId);
                UnseenUnderlays.RemoveAt(i);
                continue;
            }
            if (e.Plate.enabled != e.Source.enabled)
                e.Plate.enabled = e.Source.enabled;
            // The fill is a child of the source like the plate, so hierarchy deactivation
            // (ProceduralMapTile.ShowContent on reveal) covers it for free — only the renderer
            // flag needs mirroring.
            if (e.Fill != null && e.Fill.enabled != e.Source.enabled)
                e.Fill.enabled = e.Source.enabled;
            if (e.Rim != null && e.Rim.enabled != e.Source.enabled)
                e.Rim.enabled = e.Source.enabled;
        }
    }

    /// <summary>
    /// The two shared underlay materials, created lazily and retinted live: the DARK plate uses a
    /// game-shipped unlit shader at the END of the opaque range (queue 2500 — after all real
    /// opaque/cutout geometry, before every translucent pass), ZTest LEqual, so nearer geometry
    /// still occludes it while it paints solid dark exactly where the unseen mesh is about to
    /// blend. The SKIP filler is the same shader fully transparent — it exists only to keep the
    /// underlay's material array aligned with the source's submesh slots. Key-colour safety as in
    /// WorldUI.MrBacking: when the live key moves within keying distance of the dark neutral, the
    /// plate lifts to a brighter warm gray so it can never be keyed away.
    /// </summary>
    private static void EnsureUnseenMaterials()
    {
        Color key = KeyColor.Value;
        bool nearKey = Mathf.Abs(key.r - UnseenDark.r) < UnseenKeyDistance
                       && Mathf.Abs(key.g - UnseenDark.g) < UnseenKeyDistance
                       && Mathf.Abs(key.b - UnseenDark.b) < UnseenKeyDistance;
        Color wanted = nearKey ? UnseenLift : UnseenDark;

        // Round 6: the backings must draw BEFORE the family, against a depth buffer that does
        // not yet contain the family surfaces (pass 0 of Amp_Basic_Unseen hardcodes ZWrite On —
        // ShaderOcclusionPatcher README). All evidence puts the family in the transparent range
        // (>2500), where the default 2500 already precedes it; the adaptive branch exists for
        // the one unconfirmed case (family AT/below 2500) so the fix cannot be outrun by a
        // material this code has not seen yet. Revealed floor stays safe in both branches: it
        // draws at the geometry queue (~2000) with depth, and a backing drawn later either
        // fails LEqual below it or — where the floor is behind — is painted over by nothing,
        // because the backing writes no depth and the floor already won the pixel.
        int wantedQueue = _familyMinQueue <= 2500 ? Mathf.Max(2000, _familyMinQueue - 1) : 2500;

        if (_unseenDarkMat == null)
        {
            Shader shader = Shader.Find("Sprites/Default")
                            ?? Shader.Find("Legacy Shaders/Diffuse")
                            ?? Shader.Find("Hidden/InternalErrorShader");
            _unseenDarkMat = new Material(shader)
            {
                name = "GloomhavenVR.MrUnseenDark",
                color = wanted,
                renderQueue = wantedQueue,
            };
            _unseenSkipMat = new Material(shader)
            {
                name = "GloomhavenVR.MrUnseenSkip",
                color = new Color(0f, 0f, 0f, 0f), // alpha 0: rasterized to nothing, writes nothing
                renderQueue = wantedQueue,
            };
            _unseenDarkColor = wanted;
            // One-shot state line (round 6): the next hardware log must show WHICH shader the
            // dark material actually got — the from-above/from-below asymmetry hypotheses hinge
            // on its cull/depth state, and Shader.Find fallbacks are invisible without this.
            VRLog.Info("Core", $"MR: unseen dark material created — shader '{shader.name}', " +
                               $"queue {wantedQueue} (per-piece coplanar underlay + groove fill; " +
                               "ModBuild-63 log confirmed Sprites/Default + family queue 3000).");
        }
        else
        {
            if (_unseenDarkMat.renderQueue != wantedQueue)
            {
                _unseenDarkMat.renderQueue = wantedQueue;
                if (_unseenSkipMat != null)
                    _unseenSkipMat.renderQueue = wantedQueue;
                VRLog.Info("Core", $"MR: unseen backings re-queued to {wantedQueue} — a family " +
                                   $"material was observed at queue {_familyMinQueue}, and the " +
                                   "backings must composite before the family's depth-writing pass.");
            }
            if (_unseenDarkColor != wanted)
            {
                _unseenDarkColor = wanted;
                _unseenDarkMat.color = wanted;
                VRLog.Info("Core", $"MR: key color moved near the unseen-underlay neutral — underlay " +
                                   $"re-tinted to RGBA {wanted.r:0.##},{wanted.g:0.##},{wanted.b:0.##},1 " +
                                   "so it can never be chroma-keyed away.");
            }
        }

        EnsureDebugMaterials();
    }

    /// <summary>
    /// ROUND-16 DEBUG TINT materials: one per backing CLASS, cloned from the dark plate's own shader
    /// and render queue so the tinted run exercises the SAME pipeline (same queue, same hardcoded
    /// blend/depth state of Sprites/Default, same layer, same geometry) and differs from the shipped
    /// look in exactly one respect — <c>.color</c>. Created only while
    /// <see cref="UnseenBackingDebugColors"/> is on and destroyed the moment it goes off, so nothing
    /// of this exists in a normal session.
    /// </summary>
    private static void EnsureDebugMaterials()
    {
        bool want = UnseenBackingDebugColors != null && UnseenBackingDebugColors.Value;
        if (!want)
        {
            if (_dbgUnderlayMat != null || _dbgWaferMat != null || _dbgRimMat != null)
            {
                DestroyMat(ref _dbgUnderlayMat);
                DestroyMat(ref _dbgWaferMat);
                DestroyMat(ref _dbgRimMat);
                VRLog.Info("Core", "MR: unseen backing DEBUG TINT off — the backings are dark again " +
                                   "(the tinted materials are destroyed; the geometry is unchanged).");
            }
            return;
        }
        if (_unseenDarkMat == null || _unseenDarkMat.shader == null)
            return;

        int queue = _unseenDarkMat.renderQueue;
        if (_dbgUnderlayMat == null || _dbgWaferMat == null || _dbgRimMat == null)
        {
            DestroyMat(ref _dbgUnderlayMat);
            DestroyMat(ref _dbgWaferMat);
            DestroyMat(ref _dbgRimMat);
            Shader shader = _unseenDarkMat.shader;
            _dbgUnderlayMat = new Material(shader)
            {
                name = "GloomhavenVR.MrUnseenDebugUnderlay",
                color = DebugUnderlayColor,
                renderQueue = queue,
            };
            _dbgWaferMat = new Material(shader)
            {
                name = "GloomhavenVR.MrUnseenDebugWafer",
                color = DebugWaferColor,
                renderQueue = queue,
            };
            _dbgRimMat = new Material(shader)
            {
                name = "GloomhavenVR.MrUnseenDebugRim",
                color = DebugRimColor,
                renderQueue = queue,
            };

            Color key = KeyColor.Value;
            bool keyClash = NearKey(key, DebugUnderlayColor) || NearKey(key, DebugWaferColor)
                            || NearKey(key, DebugRimColor);
            VRLog.Info("Core", $"MR: unseen backing DEBUG TINT ON — shader '{shader.name}', queue " +
                               $"{queue} (identical to the dark plate; only the colour differs). " +
                               "Coplanar underlay = BLUE, top-plane wafer = MAGENTA, rim curtain = " +
                               "RED. What the screenshot proves: NO tint anywhere in the fog-of-war " +
                               "region ⇒ the mod's backings never reach the screen and the backing " +
                               "strategy is dead; tints on the hex TOPS but the outer cliff still " +
                               "green ⇒ the backings render fine and the glowing cliff is geometry " +
                               "the sweep never matched (see the RIM POPULATION dump); RED visible " +
                               "on the cliff yet green over it ⇒ the curtain draws but the authored " +
                               "surface in front of it is not compositing against it." +
                               (keyClash
                                   ? " WARNING: the live key colour is close to one of the tints — " +
                                     "switch the key to GREEN before judging a missing colour."
                                   : string.Empty));
        }
        else if (_dbgUnderlayMat.renderQueue != queue)
        {
            _dbgUnderlayMat.renderQueue = queue;
            _dbgWaferMat.renderQueue = queue;
            _dbgRimMat.renderQueue = queue;
        }
    }

    /// <summary>Per-channel proximity to the live chroma key (same rule as the underlay's own
    /// key-avoidance lift) — a tint this close could be keyed away, which would read as "the
    /// backing is not rendering" and poison the whole diagnostic.</summary>
    private static bool NearKey(Color key, Color c) =>
        Mathf.Abs(key.r - c.r) < UnseenKeyDistance && Mathf.Abs(key.g - c.g) < UnseenKeyDistance
        && Mathf.Abs(key.b - c.b) < UnseenKeyDistance;

    private static void DestroyMat(ref Material? m)
    {
        if (m != null)
            UnityEngine.Object.Destroy(m);
        m = null;
    }

    /// <summary>Ancestor levels <see cref="UnderPreviewNode"/> walks before giving up.
    ///
    /// <para>ROUND 16: was 12, on the assumption that "the preview content is generated a handful
    /// of levels under the tile". Apparance nests its generated content far deeper than that, and
    /// the cap is a prime suspect for the rim: a renderer BELOW the cap is matched by nothing AND
    /// recorded by nothing — <see cref="RecordUnbacked"/> only fires for renderers this same capped
    /// test accepts — which is how "UNBACKED PREVIEW RENDERERS — none" could stay green while the
    /// cliff glowed. 40 clears any plausible Apparance nesting.</para>
    ///
    /// <para>WHY A CAP AT ALL (it is not paranoia about cycles — a Transform chain cannot loop):
    /// this walk runs per RENDERER inside a sweep over every renderer in the scene (thousands), so
    /// the cap bounds the worst case to a fixed number of parent hops per candidate. It is a cost
    /// bound, which is why raising it is safe: it costs at most 28 extra reference reads on the
    /// renderers that have no 'Preview' ancestor at all.</para></summary>
    private const int PreviewAncestorScanDepth = 40;

    /// <summary>True when an ACTIVE ancestor named 'Preview' sits above <paramref name="t"/> —
    /// the node <c>ProceduralMapTile.ShowContent</c> toggles for a hidden room's stand-in stack.
    /// One of the two match signals of <see cref="ForceUnseenOpaque"/> (the other is the 'Unseen'
    /// shader family); kept so a translucent stack renderer outside the family stays covered.
    /// Depth-capped at <see cref="PreviewAncestorScanDepth"/> (a cost bound — see there).</summary>
    private static bool UnderPreviewNode(Transform t)
    {
        Transform? p = t;
        for (int depth = 0; p != null && depth < PreviewAncestorScanDepth; depth++)
        {
            if (p.name == "Preview")
                return true;
            p = p.parent;
        }
        return false;
    }

    /// <summary>True when <paramref name="s"/> carries the game's fog-of-war naming fragment
    /// (<see cref="UnseenShaderHint"/>) — applied to GO names ('EN_Unseen_…'), material names and
    /// shader names ('Amp_Basic_Unseen', 'UnseenGroundPlane_Shd') alike since round 3.</summary>
    private static bool HasUnseenName(string? s) =>
        !string.IsNullOrEmpty(s)
        && s!.IndexOf(UnseenShaderHint, StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>Family test for one material: its own name or its shader's name reads 'Unseen'.</summary>
    private static bool IsUnseenFamilyMaterial(Material? m) =>
        m != null && (HasUnseenName(m.name) || (m.shader != null && HasUnseenName(m.shader.name)));

    /// <summary>Translucency test: a transparent-range render queue, an active alpha blend
    /// (DstBlend != Zero), or — round 13 — a transparent-family RenderType TAG. The tag closes
    /// the probe's documented blind spot (a pass that hardcodes its blend exposes no _DstBlend
    /// and can sit at an opaque-range queue): the game's own unseen materials carry
    /// RenderType='Overlay' (round-9 dump), so the tag is the authoring house style's signal,
    /// and the round-13 rim evidence ('Simple Tile' side faces translucent over key, yet
    /// invisible to both probe and census) is exactly the class only the tag can catch. Cutout
    /// (AlphaTest, DstBlend 0, tag 'TransparentCutout') still counts as opaque — its holes
    /// showing the background is authored behaviour, not key bleed-through.</summary>
    private static bool IsTranslucent(Material? m)
    {
        if (m == null)
            return false;
        if (m.renderQueue > 2500)
            return true;
        if (m.HasProperty("_DstBlend") && m.GetInt("_DstBlend") != 0)
            return true;
        string tag = m.GetTag("RenderType", false, string.Empty);
        return tag == "Transparent" || tag == "Fade" || tag == "Overlay";
    }

    /// <summary>Destroy every underlay child and (unless <paramref name="keepMaterials"/>) the
    /// shared materials — MR off / VR stop / hot reload / the safety valve flipping off. The
    /// sources' own materials were never touched, so there is nothing to reassign; underlays whose
    /// source a scene unload already destroyed died with it (Unity fake-null) and are simply
    /// dropped.
    ///
    /// <para><paramref name="keepMaterials"/> (round 16) is for the LIVE RETUNE path, which tears
    /// the backings down and rebuilds them inside the SAME call: destroying the shared materials
    /// there nulls the fields the rebuild is about to read, so every rebuilt backing gets a null
    /// material array and draws nothing — permanently, because its source is back in
    /// <see cref="UnseenSources"/>. Teardown paths that really end the session keep the default and
    /// free everything.</para></summary>
    private static void RestoreUnseenUnderlays(bool keepMaterials = false)
    {
        for (int i = 0; i < UnseenUnderlays.Count; i++)
        {
            Renderer plate = UnseenUnderlays[i].Plate;
            if (plate != null)
                UnityEngine.Object.Destroy(plate.gameObject);
            Renderer? fill = UnseenUnderlays[i].Fill;
            if (fill != null)
                UnityEngine.Object.Destroy(fill.gameObject);
            Renderer? rim = UnseenUnderlays[i].Rim;
            if (rim != null)
                UnityEngine.Object.Destroy(rim.gameObject);
        }
        // The rim curtains' MESHES are mod-owned assets — a Mesh is not collected with the
        // GameObject that referenced it, so the shared cache is released explicitly (round 15).
        MrRimCurtain.ReleaseMeshes();
        UnseenUnderlays.Clear();
        UnseenSources.Clear();
        if (!keepMaterials)
        {
            DestroyMat(ref _unseenDarkMat);
            DestroyMat(ref _unseenSkipMat);
            DestroyMat(ref _dbgUnderlayMat);
            DestroyMat(ref _dbgWaferMat);
            DestroyMat(ref _dbgRimMat);
        }
        _camDumpLogged = false;  // round-16/17 dumps re-arm for the rebuilt region
        _diagPrevBackingCount = -1;
        _diagDumps = 0;
        _diagDumpedAtCount = 0;
        _matchFamilyCount = 0;   // round-16 match accounting is per MR session
        _matchRegionCount = 0;
        _regionRejectAboveTop = 0;
        _regionRejectFigure = 0;
        _regionRejectOversize = 0;
        _regionRejectNonMesh = 0;
        _regionRejectNoBacking = 0;
        RegionAdoptedNames.Clear();
        RegionBoundsScratch.Clear();
        RegionTopScratch.Clear();
        RegionBottomScratch.Clear();
        _previewScanNextFrame = 0;
        _loggedPreviewCount = -1;
        _loggedRimCount = -1;
        _unseenVerboseLogs = 0;
        _unbackedLastHash = 0;
        _unbackedNextAllowed = 0f;
        _submeshDiagLogged = false;
        _censusLastHash = 0;
        _censusNextAllowed = 0f;
        _familyMinQueue = int.MaxValue; // re-observe per session (round 6 adaptive queue)
    }

    private static void RestoreSky()
    {
        for (int i = 0; i < HiddenSky.Count; i++)
        {
            Renderer r = HiddenSky[i];
            if (r != null) // Unity fake-null: destroyed by a scene unload
                r.enabled = true;
        }
        HiddenSky.Clear();
        _skyScanNextFrame = 0;
        _loggedSkyCount = -1;
        _skyDiagLogged = false;
    }

    private static void Record(Camera cam)
    {
        if (!CamOriginals.ContainsKey(cam))
            CamOriginals.Add(cam, (cam.clearFlags, cam.backgroundColor));
    }

    private static void ForceSolid(Camera cam, Color key)
    {
        if (cam.clearFlags != CameraClearFlags.SolidColor)
            cam.clearFlags = CameraClearFlags.SolidColor;
        if (cam.backgroundColor != key)
            cam.backgroundColor = key;
    }

    /// <summary>Restore every recorded camera + the skybox material (MR off / VR stop / hot reload).</summary>
    internal static void RestoreAll()
    {
        int restored = 0;
        foreach (KeyValuePair<Camera, (CameraClearFlags Flags, Color Bg)> pair in CamOriginals)
        {
            Camera cam = pair.Key;
            if (cam == null) // Unity fake-null: destroyed by a scene unload
                continue;
            cam.clearFlags = pair.Value.Flags;
            cam.backgroundColor = pair.Value.Bg;
            restored++;
        }
        CamOriginals.Clear();

        if (_skyboxSaved)
        {
            RenderSettings.skybox = _savedSkybox;
            _savedSkybox = null;
            _skyboxSaved = false;
        }

        int skyRestored = HiddenSky.Count;
        RestoreSky();

        int unseenRestored = UnseenUnderlays.Count;
        RestoreUnseenUnderlays();

        bool wasActive = _active;
        _active = false;
        if (wasActive && _loggedActive)
        {
            _loggedActive = false;
            VRLog.Info("Core", $"Mixed reality OFF — skybox and {restored} camera clear(s) " +
                               $"restored, {skyRestored} sky renderer(s) re-enabled and " +
                               $"{unseenRestored} unseen-geometry underlay(s) destroyed " +
                               "(authored materials were never touched) — back to vanilla.");
        }
    }

    /// <summary>Drop bookkeeping for cameras destroyed by scene unloads (defensive).
    ///
    /// <para>Same name as <c>VRCameraPolicy.PruneDead</c> and called from the same scene-load
    /// path, but a DIFFERENT map (clear-flags/background colour, not stereo eye masks) plus the
    /// hidden-sky renderer list and three scan/log latches this one alone owns. Not a duplicate;
    /// do not merge (REVIEW-Hands-Board-Core §2 item 9).</para></summary>
    internal static void PruneDead()
    {
        Scratch.Clear();
        foreach (KeyValuePair<Camera, (CameraClearFlags Flags, Color Bg)> pair in CamOriginals)
        {
            if (pair.Key == null)
                Scratch.Add(pair.Key!);
        }
        for (int i = 0; i < Scratch.Count; i++)
            CamOriginals.Remove(Scratch[i]);
        Scratch.Clear();

        // Drop sky renderers destroyed by the unload; re-scan the new scene from scratch.
        for (int i = HiddenSky.Count - 1; i >= 0; i--)
        {
            if (HiddenSky[i] == null)
                HiddenSky.RemoveAt(i);
        }
        _skyScanNextFrame = 0;
        _skyDiagLogged = false;
        _loggedSkyCount = -1;

        // Same for the unseen underlays: a source the unload destroyed took its underlay child
        // with it — drop the entry (the shared dark/skip materials are mod-owned and survive),
        // and let the next MR tick re-sweep the new scene.
        for (int i = UnseenUnderlays.Count - 1; i >= 0; i--)
        {
            if (UnseenUnderlays[i].Source == null)
            {
                UnseenSources.Remove(UnseenUnderlays[i].SourceId);
                UnseenUnderlays.RemoveAt(i);
            }
        }
        _previewScanNextFrame = 0;
        _loggedPreviewCount = -1;
        _loggedRimCount = -1;
        _unbackedLastHash = 0;
        _unbackedNextAllowed = 0f;
        // A scene load means a NEW unseen region — re-arm the round-16 one-shot dumps so they
        // describe the region that is actually on screen.
        _camDumpLogged = false;
        _diagPrevBackingCount = -1;
        _diagDumps = 0;
        _diagDumpedAtCount = 0;
    }
}
