using System.Collections.Generic;
using System.Text;
using GloomhavenVR.Core;
using GloomhavenVR.Rig;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace GloomhavenVR.WorldUI;

// THE DIGITS IN THESE FILENAMES ARE THE SPLIT — do not rename them (rule and reasoning:
// FlatScreen.1.Core.cs). Part 1 is the argument, the state and the lifecycle; part 2 is everything
// that allocates, renders or re-layers; part 3 is the measurement and the log line.

/// <summary>
/// <b>ONE FLOATED WINDOW, ONE BAND-LIMITED TEXTURE.</b> A converted window's canvas is rendered by a
/// private orthographic camera into its own mip-mapped, multisampled render target, and a single quad
/// shows that target where the canvas would have drawn. Only the canvas LAYER moves; pose, rect,
/// <c>Canvas.enabled</c>, the <c>GraphicRaycaster</c> and the poke registration all stay exactly as
/// they were, so input and multiplayer are untouched by construction.
///
/// <para><b>WHY IT EXISTS.</b> A floated window is MINIFIED in the eye — hundreds of point-sampled
/// graphics resampled through fewer pixels than they were authored at. The sub-texel phase is constant
/// while the window is still (it reads as sharp) and sweeps every frame while it is carried (it reads
/// as shimmer). Turning this on ended that: the user, ModBuild 193, <i>"Durchbruch beim Flackern!"</i>.
/// A window that cannot claim a private capture layer is REFUSED and keeps direct rendering, which is
/// also the dial's OFF behaviour.</para>
///
/// <para><b>THE PIECES.</b> Part 1 is the argument, the state and the lifecycle; part 2 allocates,
/// renders and re-layers; part 3 is the report; part 4 measures the capture frame and the content fit;
/// part 5 owns the per-panel layer pool that keeps two windows from drawing into each other.</para>
///
/// <para><b>TWO CONSTRAINTS THAT LOOK LIKE DETAILS AND ARE NOT.</b> A RenderTexture cannot be
/// multisampled AND mip-mapped — <c>Create()</c> returns true and silently drops the mips — so the
/// capture and the display are two targets with a resolve between them. And the per-axis rate is
/// quantised: the achievable factor is <c>MaxRtDimension / frame.width</c> rounded down to
/// <see cref="RateQuantum"/>, so a frame 32 px wider can cost a whole quantum of sharpness.</para>
///
/// <para><b>THE DEFECT THIS CLASS CHASED FOR SIXTEEN BUILDS WAS NOT IN THIS CLASS.</b> ModBuild
/// 203-216 measured mesh, atlas, sub-meshes, alpha, cull, the CanvasGroup chain, layer, frustum,
/// sorting, the mip chain, the resolve blit and the sampling rate — every one of them correct, and the
/// picture still lost whole elements. The cause was a <c>Custom/SimpleGrabPassBlur</c> under
/// <c>UI Item Confirmation Box</c> covering 99 % of the sub-view: a GrabPass shader grabs the
/// framebuffer it is being drawn into and paints a blurred copy back, which inside a render-to-texture
/// pass is the capture target MID-RENDER. It hid from every instrument because its inherited alpha is
/// 0.000 and they all classified visibility by alpha — <b>a graphic with a non-stock material is not
/// governed by the alpha uGUI writes into its vertex colour.</b> Fixed in
/// <c>NeutraliseGrabPassBlur</c>; the diagnostic apparatus built to find it has been removed.</para>
/// </summary>
internal static partial class PanelSupersample
{
    private const string Scope = "WorldUI";

    // ---- caps (constants on purpose: the user gets one switch and one factor, not five knobs) ----

    /// <summary>
    /// How many panels may be supersampled at once. EIGHT, raised from two in ModBuild 193, and the
    /// reason the old value was wrong is worth writing down because it was set from an estimate
    /// rather than from a measurement.
    ///
    /// <para>THE 192 HARDWARE LOG: <i>"PANEL SUPERSAMPLE cap reached: 2 panel(s) are supersampled
    /// and 3 further eligible window(s) are NOT"</i> — i.e. the cap, not VRAM, was what sent three
    /// of the five open windows back to the shimmering direct path. The map room's window arc holds
    /// five at once and there are permanent windows besides, so two was never going to cover a
    /// normal session.</para>
    ///
    /// <para>WHAT A PANEL ACTUALLY COSTS, measured in that same log rather than estimated. The two
    /// engaged panels reported 14.0 MB (328x1080) and 13.5 MB (390x880); the party window then
    /// grew to its fitted 1920x1080 and RE-ALLOCATED to 81.7 MB (the "PANEL SUPERSAMPLE
    /// re-allocated 'New Party display' to 1920x1080" line). The session figure of 95.3 MB is
    /// 81.7 + 13.5 and is therefore NOT counted differently from the per-panel figures at all — the
    /// apparent discrepancy in the 192 reading was simply the engage-time number (pre-fit rect)
    /// being compared with the post-fit total. Both are right; only one of them is current.</para>
    ///
    /// <para>SO THE VRAM CEILING IS THE REAL BOUND, and this number is only there to stop an
    /// unbounded set of windows each holding a camera and a target. Eight of the SMALL windows cost
    /// ~210 MB with mips; a full-size 1920x1080 window at MSAA 4x costs ~90 MB, so
    /// <see cref="MaxTotalVramBytes"/> stops that family at four and the allocator degrades the
    /// fifth's MSAA rather than refusing it outright.</para>
    ///
    /// <para><b>MODBUILD 194 CAPS THIS AGAINST THE LAYER POOL.</b> Every supersampled panel now owns
    /// a PRIVATE capture layer (<see cref="PoolSize"/>, and the whole argument in
    /// PanelSupersample.5.Isolation.cs), because ModBuild 193 gave them all the same one and each
    /// camera therefore captured every neighbour inside its frustum. Unity has 32 layers, most of
    /// them named by the game, so the real cap is <see cref="EffectiveMaxPanels"/> =
    /// min(this, pool) — expected to be SEVEN on this game (layers 20-26). Read THAT everywhere,
    /// never this constant.</para>
    /// </summary>
    private const int MaxPanels = 8;

    /// <summary>Hard per-axis RT ceiling. 4096 is the conservative floor of what every desktop GPU
    /// this mod runs on supports as a render target; the factor dial cannot exceed it.</summary>
    private const int MaxRtDimension = 4096;

    /// <summary>
    /// Hard VRAM ceiling for ONE panel: capture target + its depth/stencil + its MSAA surfaces +
    /// the separate mipped display target, all together. The allocator steps MSAA down 4 → 2 → 1
    /// until the estimate fits and logs which step it took.
    /// <para>160 MB (ModBuild 198, raised from 128). THE ARITHMETIC, from the ModBuild 197 hardware
    /// log's own numbers rather than from an estimate. The largest window this mod floats is the
    /// party display at a measured 1971x1458 capture frame. At the OLD operating point (factor 1.0,
    /// MSAA 4x) it cost 124.2 MB: capture 109.6 (colour 11.0 + depth/stencil 11.0 + 4x multisample
    /// 87.7) + mipped display 14.6. At the NEW one (<see cref="BandLimitFactor"/> 2.0, MSAA 1x) it
    /// costs 146.2 MB: capture 87.7 (colour 43.9 + depth/stencil 43.9, no multisample surfaces) +
    /// mipped display 58.5. That is +18 % for the same window, and it buys the whole of the fix
    /// described in <see cref="BandLimitFactor"/>. 128 MB would refuse it outright, and a cap a real
    /// window cannot fit under is not a cap, it is an outage.</para>
    /// </summary>
    private const long MaxPanelVramBytes = 160L * 1024 * 1024;

    /// <summary>
    /// Hard VRAM ceiling across every supersampled panel — THE real bound, and the one the cap
    /// message quotes. 448 MB (ModBuild 198, raised from 384) so the ceiling still holds exactly
    /// THREE full-size windows after the factor/MSAA trade above: 3 x 146.2 = 438.6 MB, where 384 MB
    /// held 3 x 124.2 = 372.6 MB. How many windows this path can serve is therefore unchanged; only
    /// what each of them buys changed. All eight of the arc's typical ~14–30 MB windows still fit
    /// with room to spare. A panel that cannot fit even at MSAA 1x is refused with a Warn and keeps
    /// today's rendering.
    /// </summary>
    private const long MaxTotalVramBytes = 448L * 1024 * 1024;

    /// <summary>
    /// MSAA sample count the allocator ASKS for (stepped down by the budget, and again if the driver
    /// refuses to create the surface).
    ///
    /// <para><b>ONE, from FOUR — ModBuild 198, and this is a TRADE, not a saving.</b> MSAA 4x was
    /// costing 87.7 MB of the party window's 124.2 MB — <b>71 % of the entire per-panel budget</b> —
    /// and on this content it buys almost nothing, for a reason specific to what this camera renders.
    /// The capture camera is ORTHOGRAPHIC and exactly aligned to the panel plane
    /// (<see cref="SyncProjection"/>), and uGUI draws axis-aligned quads in that plane, so at an
    /// integer factor a quad's edges land on exact texel boundaries and there is no geometry edge for
    /// multisampling to resolve. What the eye actually sees the edges of — glyphs, icons, card art —
    /// is TEXTURE content (TMP samples an SDF atlas), and MSAA has never touched texture content.
    /// Only rotated or fractionally-placed quads benefit, and they are a small minority.</para>
    ///
    /// <para><b>AND SUPERSAMPLING SUBSUMES IT.</b> Rendering at <see cref="BandLimitFactor"/> = 2 and
    /// box-filtering down one mip level is four samples per authored pixel applied to EVERYTHING —
    /// geometry edges AND texture content — which is strictly more than 4x MSAA delivered, at less
    /// memory than 4x MSAA cost. The two were documented here as "complementary, not redundant";
    /// against a 1:1 factor that was true, but the money was in the wrong one of the two.</para>
    ///
    /// <para>The step-down machinery is kept — it is what degrades gracefully if a future factor or a
    /// larger window pushes past the budget — and simply has nothing to step down from at 1.</para>
    /// </summary>
    private const int PreferredMsaa = 1;

    /// <summary>
    /// <b>THE MINIMUM RENDER-TARGET TEXELS PER AUTHORED PIXEL — the ModBuild 198 fix, and the whole
    /// of the "the flicker is back as soon as the window is IN MY HAND" report.</b>
    ///
    /// <para><b>WHAT THE ModBuild 197 LOG MEASURED.</b> All 70 of its state lines report
    /// <c>texelsPerPixel / authoredPerPixel = 1.00</c>, i.e. the session ran at
    /// <c>[WorldUI] PanelSupersampleFactor = 1.0</c> — its shipped default — so <b>the "supersample"
    /// path was allocating exactly one render-target texel per authored window pixel and
    /// supersampling nothing</b>. Read the whole distribution of the sampling field rather than its
    /// mode: authored pixels per RENDERED eye pixel runs 0.73 to 2.18 with the mode at ~1.05, so the
    /// quad is displayed at roughly <b>one RT texel per rendered eye pixel</b> for most of a session.
    /// 45 of those 70 lines have half or more of every texture sample coming from UNFILTERED mip
    /// level 0, and 19 of them have 90 % or more.</para>
    ///
    /// <para><b>WHY THAT IS EXACTLY "STILL = FIXED, IN MY HAND = BROKEN".</b> Sampling a texture at
    /// ~1 texel per pixel with a bilinear filter is a resample whose kernel is <c>[1-p, p]</c> for
    /// the sub-texel phase p. At p = 0 it reproduces the source exactly; at p = 0.5 it averages two
    /// neighbours and its response at Nyquist is ZERO. A one-pixel glyph stroke therefore swings
    /// between full contrast and NO CONTRAST as p sweeps — and p is a function of where the window
    /// sits relative to the eye's pixel grid. <b>A still window has a CONSTANT phase: the pattern
    /// freezes and reads as "sharp".</b> A window in the hand sweeps the phase every frame: the same
    /// pattern crawls, which is the flicker. <b>And on release the phase LOCKS at whatever value the
    /// hand left behind</b> — which is the user's "the state as it was during the flickering gets
    /// FROZEN, which can lead to certain elements not being displayed", and his earlier ModBuild 195
    /// words <i>"bewege ich es nochmal und lasse los, sieht es wieder anders aus"</i>: another
    /// release, another resting phase, another set of strokes washed out. No content defect can
    /// produce that signature; a resampling phase produces exactly it.</para>
    ///
    /// <para><b>WHY ModBuild 197's "894 CAPTURES / 894 FRAMES" WAS RIGHT AND STILL MISSED IT.</b> It
    /// measured the CAPTURE, and the capture is flawless — this log confirms it on every panel:
    /// captures equal resolves exactly, moving and still; 0 frames on which the quad showed the raw
    /// target; 0 resolves with mismatched sizes; and ~900 captures per 10 s report window continuing
    /// forever after a release, so the displayed texture is re-captured at 90 Hz and is never stale.
    /// The defect is one step LATER, in how the eye samples the finished texture, and no
    /// capture-side counter can see it.</para>
    ///
    /// <para><b>WHY 2.0 AND NOT A MIP BIAS.</b> A positive <c>mipMapBias</c> would also stop the eye
    /// reading level 0 and costs no memory — but it forces a level COARSER than the eye's rate, i.e.
    /// it pays for stability in blur, and the still-window sharpness is this project's accepted win.
    /// Factor 2.0 instead makes mip level 1 — the level the eye lands on at ~1 authored px per
    /// rendered px — a proper 2x2 box downsample of a 2x rasterization. That level carries the
    /// authored resolution with its near-Nyquist content already attenuated by the box filter, so the
    /// phase term has far less to modulate AND the image is 4x-supersampled rather than merely
    /// rasterized. The still window gets SHARPER, not softer.</para>
    ///
    /// <para><b>THE COST IS PAID FOR IN <see cref="PreferredMsaa"/>,</b> not in new memory: +18 % per
    /// full-size window, and the same three of them fit the session ceiling as before.</para>
    ///
    /// <para><b>THIS IS A FLOOR ON THE DEFAULT, NEVER AN OVERRIDE OF A TUNED VALUE.</b> It applies
    /// only while <c>[WorldUI] PanelSupersampleFactor</c> still reads its shipped default; a value
    /// the user has actually set is taken verbatim (this project's "tuned cfg drops are current"
    /// rule). Which of the two happened is printed once per panel at engage, with both numbers.</para>
    /// </summary>
    private const float BandLimitFactor = 2f;

    /// <summary>
    /// RT texels per RENDERED eye pixel at and above which the hardware's trilinear filter stops
    /// blending in unfiltered mip level 0 at all — i.e. the threshold the resample instrument judges
    /// every measurement against. TWO, and it is arithmetic, not a preference: the selected LOD is
    /// <c>log2(texels per pixel)</c> and level 0 keeps a weight of <c>1 - LOD</c> until LOD reaches
    /// 1, which is exactly 2 texels per pixel.
    /// <para>Anisotropic filtering makes this a LOWER BOUND rather than a guarantee: aniso selects
    /// the LOD from the MINOR axis' rate, so a window yawed away from the head reads a lower LOD —
    /// more level 0 — than this figure states. That is a deliberate trade (see
    /// <see cref="AnisoLevel"/>) and the instrument says so on the line.</para>
    /// </summary>
    private const float BandLimitedTexelsPerPixel = 2f;

    /// <summary>How much the allocator gives back per step when a panel does not fit its VRAM budget,
    /// and the floor it stops at. 1.0 is deliberately the floor and not lower: at factor 1.0 the
    /// window has exactly ModBuild 197's behaviour, which is a known, shipped, playable state, whereas
    /// below 1.0 the capture would be COARSER than the authored art and would soften text that the
    /// direct path renders sharp. See the step-down loop in <see cref="Engage"/> for why a factor
    /// concession replaced the MSAA concession.</summary>
    private const float FactorStepDown = 0.25f;

    /// <summary>Floor for <see cref="FactorStepDown"/> — see it.</summary>
    private const float MinStepDownFactor = 1f;

    /// <summary>Anisotropic level on the RT. The floated windows in the map room are yawed away from
    /// the head by up to 85°, which is precisely the case aniso exists for — at 0/1 the far edge of
    /// an angled window would blur to a smear that trilinear alone cannot avoid.</summary>
    private const int AnisoLevel = 8;

    // ---- ModBuild 243: THE EYE GRID LOCK ------------------------------------------------------

    /// <summary>
    /// <b>THE USER'S REPORT, ModBuild 242, verbatim:</b> <i>"Die 'Fenster scharfzeichnen' Option
    /// hilft sehr bei dem Aliasing das sonst auf dem Fenster sichtbar ist. Allerdings sieht das
    /// Fenster dann niedrig aufgelöst aus, von weiter weg ist der Text dann nur noch Matsch."</i>
    ///
    /// <para><b>THE TWO HALVES ARE ONE CAUSE, AND IT IS NOT THE FACTOR.</b> This path is a
    /// RESAMPLING CHAIN with three grids in it, and until this build only two of them were ever
    /// consulted. Grid 1: the window's AUTHORED uGUI pixels (host rect, e.g. 1920x1080). Grid 2: the
    /// capture render target, sized <c>frame.size * rate</c> where <c>rate</c> came from
    /// <see cref="BandLimitFactor"/> and nothing else — 2.00 texels per AUTHORED pixel, a constant.
    /// Grid 3: the RENDERED EYE PIXELS the display quad actually covers, which is a function of how
    /// far the window is from the head and is consulted by NOBODY on the sizing path. The eye
    /// samples grid 2 through grid 3 with a trilinear mip filter, and where that filter lands is
    /// decided ENTIRELY by the ratio of the two — <c>LOD = log2(RT texels per rendered eye px)</c>.
    /// The factor sets grid 2 against grid 1. It has never had an opinion about grid 3.</para>
    ///
    /// <para><b>WHAT THE ModBuild 242 HARDWARE LOG MEASURES, and it is the whole diagnosis.</b>
    /// <c>'UI Loadout Window'</c>: host rect 1920x1080, capture frame 1984x1144, RT 3968x2288 at
    /// factor 2.00, MSAA 1x, mips 12, Trilinear aniso 8, 115.4 MB — <b>drawn into 1255x649 rendered
    /// px</b> through the left eye of a 3072x3264 per-eye target. That is <b>3.33 RT texels per
    /// rendered pixel</b>, so trilinear selects <b>LOD 1.74</b>, and a fractional LOD is a BLEND OF
    /// TWO LEVELS: <b>74 % of every texture sample comes from mip level 2, which carries 0.79 texels
    /// per rendered pixel</b> — below one — and 26 % from level 1, which carries 1.58 and is
    /// therefore the undersampled, crawling one the toggle exists to remove. The picture the eye is
    /// handed is three quarters an under-resolved raster magnified back up 27 %. <b>That is the
    /// "Matsch", and it is arithmetic, not taste.</b> The same reading on
    /// <c>'Quest Log Manager'</c> at its peak: 6.70 texels per rendered px, LOD 2.74, 74 % of every
    /// sample from level 3 at 0.84 texels per rendered pixel.</para>
    ///
    /// <para><b>WHY IT GOT WORSE IN 242 RATHER THAN BEING A NEW BUG.</b> Read
    /// <see cref="BandLimitFactor"/>'s own argument: <i>"Factor 2.0 makes mip level 1 — THE LEVEL THE
    /// EYE LANDS ON AT ~1 AUTHORED PX PER RENDERED PX — a proper 2x2 box downsample of a 2x
    /// rasterization."</i> That is exactly right, and it is a statement about grid 3 that the code
    /// never checked. ModBuild 242 moved <c>WindowDistanceMeters</c> 1.20 -> 1.40; the 242 log's
    /// windows sit at 1.04 .. 3.35 authored px per rendered px, peak per window 1.04, 1.26, 1.67,
    /// 1.74, 1.84, 2.08, 2.82, 3.23, 3.35. The design's stated operating point walked out from under
    /// it and the constant stayed where it was.</para>
    ///
    /// <para><b>THE FIX, IN ONE SENTENCE: size the capture so that the number of RT texels per
    /// RENDERED EYE PIXEL is a POWER OF TWO.</b> Then <c>log2</c> of it is an INTEGER, trilinear
    /// selects ONE level with no blend, and that level carries EXACTLY 1.00 texels per rendered
    /// pixel — nothing below the screen grid to be magnified back up, nothing above it to alias. On
    /// the Loadout window that is <c>rate = 1.3125</c> instead of 2.00 (the exact target is
    /// <c>2^1.125 / 1.68 = 1.297</c>; see the margin paragraph below), i.e. an RT of <b>2604x1502
    /// instead of 3968x2288</b> — <b>43 % of the texels, 115.4 MB -> 49.7 MB</b> — and the eye then
    /// reads 88 % of its samples from ONE level carrying 1.09 texels per rendered pixel instead of
    /// 74 % from a level carrying 0.79. <b>The good version is the CHEAP version</b>, and the
    /// capture camera, the resolve blit and the mip chain all shrink by the same 2.3x. Every level
    /// this removes is a level the hardware was never going to read. That is the answer to "if your
    /// answer is bigger RTs, what pays": nothing pays, because the answer is SMALLER RTs. The four
    /// windows the 242 log holds open at once cost 326.9 MB of the 448 MB session cap; the same four
    /// come to roughly 145 MB, so the fifth and sixth window the map room can open now fit where the
    /// fifth was already within 6 MB of the ceiling.</para>
    ///
    /// <para><b>WHY NOT A MIP LOD BIAS — the dial that already exists.</b>
    /// <c>[WorldUI] PanelMipLodOffset</c> is a CONSTANT offset, so it moves the sampled level to
    /// <c>2^-b</c> texels per rendered pixel whatever the minification is; it cannot snap a
    /// fractional LOD onto an integer, because it does not know where the fraction is. Any negative
    /// value buys sharpness by putting an UNDERSAMPLED level back into the blend — which is the
    /// aliasing he says the toggle currently saves him from, and it is why ModBuild 204 took the
    /// shipped value back to 0.00. The RT's SIZE is the only lever that can put the sampled level
    /// exactly on the eye's grid. That dial stays at 0.00 and is untouched by this build.</para>
    ///
    /// <para><b>WHY NOT A SHARPEN PASS.</b> An unsharp or CAS kernel after the resample would add a
    /// full-screen-quad pass per window per eye at 3072x3264 (paid twice, MultiPass) to raise the
    /// contrast of a signal that is genuinely absent from 74 % of the samples — it would amplify the
    /// 26 % aliased term hardest, which is the crawl he is currently free of. It is worth revisiting
    /// only once the sampled level is at 1.00 and the residual softness is the box filter's own MTF
    /// rather than a missing octave. Not this build.</para>
    ///
    /// <para><b>WHAT THIS CANNOT DO.</b> It does not add rendered eye pixels. At 1.66 authored px
    /// per rendered px the window is still showing 60 % of its authored resolution and an
    /// 8-authored-px stroke still arrives as 4.8 eye px — <see cref="MaxRtDimension"/> and the
    /// window's angular size own that, and this class owns neither. What it recovers is the CONTRAST
    /// at the resolution the eye does have. And on a window yawed hard away from the head,
    /// <see cref="AnisoLevel"/> lowers the selected LOD toward the minor axis per pixel, so no fixed
    /// RT size can pin it — the lock buys less there, and the report line says so rather than
    /// implying otherwise.</para>
    ///
    /// <para><b>THE ONE CLIFF IN IT, NAMED RATHER THAN DISCOVERED LATER.</b> <c>k</c> is bounded
    /// below by <see cref="MinStepDownFactor"/>: the capture may never be COARSER than the source
    /// art, because the game's own uGUI atlases are MIPLESS and minifying them into the capture
    /// bakes in an aliasing no filter downstream can remove. So as the minification rises past the
    /// point where <c>2^k / apr</c> would fall under 1.0, <c>k</c> steps and the target jumps back
    /// up by up to 3.4x in area. The jump is bounded ABOVE by the un-locked ModBuild 242 rate — the
    /// worst case of this build is exactly the cost of the last one — it is rate-limited by
    /// <see cref="EyeGridMinReallocFrames"/>, and it is printed.</para>
    ///
    /// <para><b>AND IT IS EQUAL TO THE DEAD BAND BY CONSTRUCTION.</b> The value below is used twice:
    /// as the margin the rate aims above <c>2^k</c>, and as the slack allowed before the lock is
    /// re-taken. They MUST be the same number — a target sized for exactly <c>2^k</c> would spend
    /// half of the permitted wander below it, and below <c>2^1</c> is unfiltered mip level 0 on a
    /// minified window, i.e. the ModBuild 198 defect handed back. Writing it as one constant is what
    /// stops the two drifting apart in a later build.</para>
    /// </summary>
    private const float EyeGridLockTolerance = EyeGridDeadBandOctaves;

    /// <summary>
    /// The rate is snapped to this grid (i.e. to multiples of <c>1/32</c>) and NOT left as the exact
    /// real the eye-grid arithmetic produces. <b>This is not tidiness; it is the ModBuild 201
    /// invariant.</b> <see cref="FrameQuantumPx"/>'s header states it: the capture frame's overspill
    /// is quantised to a 32-authored-px grid so that a frame edge moving between two content
    /// measurements moves by a whole number of grid cells — and that is a whole number of TEXELS
    /// only if <c>rate * FrameQuantumPx</c> is an integer. At a free real rate every frame
    /// measurement would re-roll the sub-texel phase of every glyph in the window, which is exactly
    /// the "es ist ziemlich zufällig" defect ModBuild 201 closed. <c>1/32</c> is the coarsest grid
    /// that keeps the product integral, and it costs at most 1/64 of a mip level of snap error
    /// (measured: 0.017 levels on the Loadout window, i.e. 1.7 % of the sample weight on the
    /// neighbouring level instead of 74 %).
    /// </summary>
    private const float EyeGridRateQuantum = 1f / FrameQuantumPx;

    /// <summary>
    /// How far the measured minification must move, in OCTAVES, before the lock is re-taken. An
    /// eighth of an octave is a 9 % change in the window's angular size — at the shipped 1.40 m
    /// window distance that is a 12 cm lean, which a seated player crosses and re-crosses all
    /// session. Below it nothing is re-allocated at all.
    /// <para>It is expressed in octaves and not in a rate fraction on purpose: the quantity that
    /// decides the picture is <c>log2</c> of the sampling ratio, so a dead band that is uniform in
    /// log space is uniform in what the player sees, at 1.0x and at 3.35x alike.</para>
    /// </summary>
    private const float EyeGridDeadBandOctaves = 0.125f;

    /// <summary>Consecutive measurements outside <see cref="EyeGridDeadBandOctaves"/> required
    /// before the lock moves. Four, at <see cref="EyeGridSampleIntervalFrames"/>, is ~0.67 s: a lean
    /// in and back out costs nothing, a new posture costs one re-allocation. The measured price of
    /// one re-allocation in the 242 log is 0.85–1.02 ms (create + destroy + synchronous prime), so
    /// this number is what stands between a correct rate and a per-frame flap.</summary>
    private const int EyeGridConfirmMeasurements = 4;

    /// <summary>Frames between eye-grid measurements. Fifteen, matching
    /// <see cref="SweepIntervalFrames"/>: the measurement is four <c>WorldToViewportPoint</c> calls
    /// and two square roots per window, so the cadence is chosen for the CONFIRM WINDOW above rather
    /// than for its own cost.</summary>
    private const int EyeGridSampleIntervalFrames = 15;

    /// <summary>Minimum frames between two eye-grid re-allocations on ONE window — one second at
    /// 90 Hz. The confirm run already rejects a flap; this is the floor that holds even if the
    /// player's head oscillates exactly on the confirm period, which is the failure mode a run
    /// counter alone cannot see. Deferrals are COUNTED and printed: a limiter that silently ate the
    /// work would read exactly like a lock that never needed to move.</summary>
    private const int EyeGridMinReallocFrames = 90;

    /// <summary>
    /// Frames between captures. ONE, and the reason is CORRECTNESS, not smoothness — this is the
    /// finding of the on-demand evaluation the design asked for.
    ///
    /// <para>Unity's <see cref="GraphicRaycaster"/> skips any graphic whose <c>Graphic.depth</c> is
    /// -1, and uGUI's own comment for that test reads <i>"-1 means it hasn't been processed by the
    /// canvas, which means it isn't actually drawn"</i>. On the supersample path OUR capture camera
    /// is the only camera that draws this canvas, so a skipped capture frame is a frame in which the
    /// canvas may be processed by nobody — and every click, hover and drag on that window could die
    /// on it. A 50 %-of-frames-dead pointer is a far worse defect than the one being fixed, and it
    /// would be nearly impossible to attribute from a hardware log.</para>
    ///
    /// <para>The animation argument points the same way and is the cheaper of the two: the live
    /// character render is a game RenderTexture re-rendered every frame by the game's own preview
    /// camera, and the game's hover feedback is a LeanTween scale of ~0.15 s
    /// (<c>ExtendedButton.OnHighlight</c>) — at 90 Hz a 2-frame throttle samples that tween 6 times
    /// and reads as a step. So: capture every frame, and buy the cost back with
    /// <see cref="MaxPanels"/> instead. The measured per-capture cost is printed in the report line
    /// so a future round can revisit this with numbers rather than with this argument.</para>
    /// </summary>
    private const int CaptureIntervalFrames = 1;

    /// <summary>Layer sweep cadence for pooled/late children, mirroring
    /// <c>CanvasConversion.CanvasSweepIntervalFrames</c>. A window that repopulates brings new
    /// GameObjects on the game's UI layer; they must reach the capture layer before they are drawn
    /// straight into the eye.</summary>
    private const int SweepIntervalFrames = 15;

    /// <summary>How often the per-panel state line is printed, whether or not anything changed. A
    /// silent path and a path that never ran must never look the same (the RenderTargetProbe rule).</summary>
    private const float ReportIntervalSeconds = 10f;

    /// <summary>Re-allocate the RT when the host rect changes by more than this fraction on either
    /// axis. A content fit that trims 2 % must not cost a 21 MB reallocation.</summary>
    private const float RectChangeFraction = 0.02f;

    /// <summary>Minimum host rect (uGUI px, either axis) worth supersampling. Below this the panel
    /// is a badge, not a window, and its RT would cost more than it buys.</summary>
    private const float MinAuthoredPx = 64f;

    /// <summary>
    /// Frames between CONTENT-FRAME measurements (<see cref="MeasureFrame"/>) while nothing about
    /// the panel is changing. The measurement is an O(subtree) walk — 2700 transforms on the party
    /// window in the ModBuild 192 log — so it is not run per frame for a settled window; every
    /// event that could invalidate it (host rect, scale, pose, a stand-up) forces it immediately
    /// instead, which is what <see cref="MarkGeometryDirty"/> is for.
    /// </summary>
    private const int ContentMeasureIntervalFrames = 15;

    /// <summary>
    /// Floor on how often the content-frame measurement may be forced by a geometry change. A
    /// two-hand resize changes the host's lossy scale EVERY frame for as long as the pinch lasts,
    /// and re-walking a 2700-transform subtree at 90 Hz for the whole gesture is a cost this path
    /// does not need to pay: two frames of lag in a rectangle the user is actively dragging is
    /// invisible, while the frame-rate cost would not be. A resize therefore re-measures at most
    /// every other frame, and the moment the gesture stops the ordinary cadence takes over.
    /// </summary>
    private const int ContentMeasureMinIntervalFrames = 2;

    /// <summary>
    /// How far the capture frame may grow beyond the host rect, as a multiple of the host rect on
    /// each axis. THIS IS THE ONLY PLACE VISIBLE CONTENT CAN STILL BE LOST, so it is generous (2x =
    /// half a host rect of overspill allowed on every edge) and it is reported: crossing it sets
    /// the state line's CONTENT field to CLAMPED and prints one Warn naming what was cut.
    /// </summary>
    private const float MaxContentExpansion = 2f;

    /// <summary>
    /// <b>THE CAPTURE FRAME IS QUANTISED TO THIS GRID (host-local uGUI px), AND THAT IS THE FLICKER
    /// HALF OF ModBuild 201.</b>
    ///
    /// <para><b>WHAT THE ModBuild 200 LOG SHOWS.</b> Every window in that session holds a rock-steady
    /// capture frame — <c>Quest Log Manager</c> 390x880 on every one of its state lines,
    /// <c>UI Map Esc Menu</c> 405x1080 on every one of its. The character window does not:
    /// 1964x1453, then 1715x1080, 1109, 1082, 1110, 1200, 1099, 1107 — its height wanders by up to
    /// 120 authored px across the session, because <see cref="MeasureFrame"/> takes the union of what
    /// LIVE content draws and that window's content animates and scrolls. <b>It is the only window
    /// whose frame moves, and it is the only window the user reports as flickering.</b></para>
    ///
    /// <para><b>WHY A MOVING FRAME IS A DEFECT AND NOT JUST AN ALLOCATION CHURN.</b> The capture
    /// camera is orthographic and framed on <c>Frame</c>; the render target is
    /// <c>round(Frame.size * Factor)</c>. So the mapping from an authored pixel to a render-target
    /// TEXEL is <c>(authored - Frame.min) * Factor</c> — and <c>Frame.min</c> is an arbitrary float
    /// that the union hands over. When the frame wanders by 19 px, every authored pixel in the window
    /// lands on a DIFFERENT SUB-TEXEL PHASE, and the whole image is re-rasterised at a new phase. For
    /// content that has texels to spare that is invisible. For content sampled at about one texel per
    /// authored pixel it decides, glyph by glyph and hairline by hairline, which strokes land on a
    /// texel centre and survive and which fall between two and disappear. That is a re-roll of the
    /// picture on the measurement cadence — every 15 frames while still, every 2 frames while the
    /// geometry is changing — and it is precisely the user's <i>"es ist ziemlich zufällig, es kommt
    /// darauf an, wann man loslässt"</i> and his <i>"mit etwas Glück kann ich es wieder lesen"</i>.
    /// The capture is otherwise pose-invariant, so nothing else in this path can produce a picture
    /// that changes while the canvas underneath it does not.</para>
    ///
    /// <para><b>WHAT THE GRID DOES, AND WHAT IT IS ANCHORED ON.</b> What is quantised is the frame's
    /// OVERSPILL PAST THE HOST RECT, per edge, rounded OUTWARD (never inward — this must not be able
    /// to crop a pixel ModBuild 200 kept), and the expansion limit is rounded outward too. It is
    /// deliberately NOT the absolute edge: a host rect is 390x880 or 1143x1080 px and its half-
    /// extents are multiples of nothing, so snapping absolute edges would inflate EVERY window
    /// (390x880 -> 448x896) and destroy this path's "the capture frame IS the host rect, so the
    /// dial's ON and OFF geometry are identical" promise on windows that overspill by nothing at all.
    /// Anchored on the host edges, a non-overspilling frame stays exactly the host rect and the
    /// property the grid exists for still holds: the host rect does not move between two content
    /// measurements, so every frame edge differs from the last by a whole number of grid cells, and
    /// with the rate quantised to <see cref="RateQuantum"/> that is a whole number of TEXELS. The
    /// residual phase drift is the rounding of <c>hostWidth * rate</c> to an integer texel count,
    /// which is under 0.01 authored px across a 1143 px window — against the arbitrary fractional
    /// offsets an unquantised union hands over today.</para>
    ///
    /// <para>32 px, against windows of 390–1964 px: coarse enough that the observed 19–120 px wander
    /// costs at most one or two grid steps, fine enough that the worst-case wasted margin is 31 px on
    /// an edge — under 2 % of the smallest window here, i.e. below
    /// <see cref="RectChangeFraction"/>'s own reallocation threshold.</para>
    /// </summary>
    private const float FrameQuantumPx = 32f;

    /// <summary>
    /// The capture rate (render-target texels per authored pixel) is quantised to a multiple of this.
    /// <para>It exists only to keep <see cref="FrameQuantumPx"/>'s promise once the rate is no longer
    /// a round number: with a 32 px grid and a rate that is a multiple of 0.25, one grid cell is a
    /// whole number of texels (32 x 0.25 = 8), so <c>round(Frame.size * rate)</c> is exact and the
    /// texel grid stays locked to the authored grid. Without it, a rate of, say, 2.388 (what
    /// <see cref="MaxRtDimension"/> leaves for the character window) would put a fresh fractional
    /// offset back into the mapping and undo the quantisation.</para>
    /// </summary>
    private const float RateQuantum = 0.25f;

    /// <summary>
    /// A graphic must cover at least this fraction of the host rect before it is allowed to lower
    /// <see cref="Entry.MinContentScale"/> and therefore raise the whole window's render target.
    /// <para>A bare minimum over every graphic is the wrong statistic: one decorative pip authored at
    /// 512 px and drawn at 24 would read 0.05 and ask for a 40x render target. 1 % of a 1143x1080
    /// host is a 111x111 px element — small enough that every real sub-view and every plate inside
    /// one qualifies, large enough that no icon can hijack the allocation.</para>
    /// </summary>
    private const float MinScaledAreaFraction = 0.01f;

    /// <summary>Below this scale a graphic is counted as MATERIALLY DOWNSCALED in the report's
    /// content-scale census. Purely a reporting threshold; it gates nothing.</summary>
    private const float ScaledContentThreshold = 0.95f;

    /// <summary>Hard floor on <see cref="Entry.MinContentScale"/>. Caps the boost this can ask for at
    /// <c>BandLimitFactor / 0.25 = 8x</c> BEFORE the dimension and VRAM ceilings cut it down, so a
    /// pathological measurement can never turn into a pathological allocation request.</summary>
    private const float MinContentScaleFloor = 0.25f;

    /// <summary>
    /// While a window is being MOVED, RESIZED or RE-FACED — and for this many frames after the last
    /// change — the capture-layer sweep runs on the faster <see cref="MovingSweepIntervalFrames"/>
    /// cadence instead of on <see cref="SweepIntervalFrames"/>.
    ///
    /// <para>WHY, read from source. <c>GrabbableModal.ThrottleDiagWhileMoving</c> sets
    /// <c>ConvertedPanel.Diagnostic = false</c> for up to a second at a time WHILE the host pose is
    /// changing (a deliberate log-spam fix, documented in its own header). <c>Diagnostic</c> is also
    /// what makes <c>CanvasConversion.Tick</c> run <c>AdoptNestedCanvases</c> and
    /// <c>ReassertAdoptedSorting</c> EVERY frame; while it is off, adoption falls back to the
    /// 30-frame <c>CanvasSweepNextFrame</c> schedule. So a nested canvas or a pooled child born
    /// during a drag can sit on the GAME's UI layer for tens of frames — which on this path means
    /// it is MISSING from the capture (our camera's mask is the capture layer alone) and
    /// simultaneously drawn straight into the eye at its own sorting order. That is exactly the
    /// user's ModBuild 192 report: <i>"Wenn man es dann mit in der Bewegung loslässt, werden manche
    /// Elemente nicht richtig dargestellt oder andere fehlen im Fenster."</i> Sweeping per frame
    /// while the window moves closes the window this path opened; the measured sweep cost is
    /// printed in the state line so a future round can revisit it with numbers.</para>
    /// </summary>
    private const int SweepAfterMotionFrames = 30;

    /// <summary>
    /// Layer-sweep cadence WHILE the window is inside its motion window, in frames.
    ///
    /// <para><b>THIS NUMBER USED TO BE 1, AND THE ModBuild 195 HARDWARE LOG RETIRED THAT.</b> ModBuild
    /// 193 swept EVERY frame while a window moved, on the argument in
    /// <see cref="SweepAfterMotionFrames"/> — a pooled child born mid-drag would otherwise sit on the
    /// game's UI layer for tens of frames. The argument is sound; the CADENCE was not, and the log
    /// says so in two independent numbers.
    /// <list type="number">
    /// <item><b>THE PER-FRAME CADENCE CAUGHT ONE ARRIVAL IN A WHOLE SESSION.</b> Every sweep that
    /// actually moved a late transform prints which cadence it ran on. Of the 23 such lines in the
    /// ModBuild 195 log, <b>22 read "reached its periodic cadence" and exactly ONE read "is MOVING
    /// (per-frame cadence)"</b>. So the arrivals this class exists to catch are not, in practice,
    /// born during drags at all — they are born when a window repopulates, and the 15-frame cadence
    /// catches them.</item>
    /// <item><b>AND IT COSTS 1.5 ms OF A FRAME THAT IS ALREADY OVER BUDGET.</b> The same log reports
    /// the sweep at 1.48–1.94 ms EACH (its own averaged field) on the party window, and the session's
    /// frame telemetry reads <c>frametime mean 18.31 p50 17.33 p95 24.98 p99 29.93 max 51.75 ms</c>
    /// against an 11.11 ms budget at 90 Hz, of which <c>logic</c> is 14–16 ms. Sweeping per frame
    /// therefore spent ~10 % of the main-thread frame on the one activity that the user reports as
    /// broken, and it did so ONLY while the window moved — i.e. it made the drag frame the worst
    /// frame in the session. A window dragged at the measured 30–133 RENDERED eye pixels per frame
    /// cannot survive a dropped frame: one missed frame is 30–133 px of positional error, which is
    /// the "flackern beim Verschieben" the movement remedy was supposed to fix.</item>
    /// </list></para>
    ///
    /// <para>FIVE frames keeps three times the settled cadence's coverage for a fifth of the
    /// per-frame cost, and the case the per-frame cadence was really aimed at — the RELEASE — is now
    /// covered outright and unconditionally by <see cref="ReleaseSettleFrames"/>, which sweeps,
    /// re-measures and re-generates the moment the window comes to rest whether or not a cadence is
    /// due. The report prints the sweep count and cost split into MOTION and STILL frames so the next
    /// round can price this change instead of arguing about it.</para>
    /// </summary>
    private const int MovingSweepIntervalFrames = 5;

    /// <summary>
    /// Frames of stillness after the last pose/scale/rect change at which the RELEASE REPAIR runs:
    /// one forced content re-measure, one forced layer sweep and one forced text regeneration, all
    /// unconditional. This is the answer to <i>"beim Loslassen kann es passieren, dass die
    /// dargestellte Anzeige kaputt ist"</i> — the moment a window stops moving is exactly the moment
    /// its content fit has finished, its pooled children have arrived and its glyphs are final, and
    /// it is the only moment at which repairing all three costs nothing per frame.
    /// <para>TWO frames, not zero: the content fit and the release re-face both write in the same
    /// frame the grab ends, and repairing before they land would repair the wrong state.</para>
    /// </summary>
    private const int ReleaseSettleFrames = 2;

    /// <summary>A SECOND release repair this many frames after the first. The content fit can trim a
    /// window a few frames after the release (the log's host rect flips 328 -> 716 -> 1920 uGUI px on
    /// one window in one session), and a repair that ran before that flip repaired the previous
    /// layout. Cheap: it is two passes per release, not per frame.</para></summary>
    private const int ReleaseSecondRepairFrames = 12;

    /// <summary>The per-frame budget a 90 Hz headset gives, in milliseconds. Used ONLY as the
    /// threshold the motion/still frame-time instrument reports against, so "N of M motion frames
    /// were over budget" is a statement with a stated bar rather than an adjective.</summary>
    private const float FrameBudgetMs = 1000f / 90f;

    /// <summary>Hard ceiling on how many GLYPH lookups one content-integrity scan may make, across
    /// every text component of one panel. The party window carries ~2700 transforms; an unbounded
    /// character scan on the report cadence would be a spike of its own. When the ceiling bites the
    /// report says so, so a truncated scan can never be mistaken for a clean one.</summary>
    private const int MaxGlyphChecksPerScan = 6000;

    /// <summary>Zero-area threshold for a generated glyph quad, in the text object's own local units.
    /// A quad below this cannot put a pixel anywhere at any resolution.</summary>
    private const float DegenerateQuadArea = 1e-6f;

    /// <summary>
    /// Zero-area threshold for a glyph's ATLAS UV rectangle, in normalised atlas units squared.
    /// <para>Sized against the real numbers rather than picked: this game's labels draw from a
    /// 2048x1024 'Sarala-Regular SDF' atlas, where even a full stop occupies roughly 10x10 atlas
    /// texels = 0.0049 x 0.0098 = 4.8e-5 in UV area, and a body glyph is an order of magnitude
    /// larger. 1e-9 is therefore four orders below the smallest legitimate glyph and far above
    /// single-precision noise on values of that size, so a quad counted here is one whose four UVs
    /// have genuinely collapsed onto a single atlas point.</para>
    /// </summary>
    private const float DegenerateUvArea = 1e-9f;

    /// <summary>
    /// A frame this long counts as a DROPPED frame during a drag: the display had to show the
    /// previous image again. 1.5 budgets, i.e. 16.67 ms at 90 Hz — comfortably above ordinary
    /// v-sync jitter around 11.11 ms and below two whole frames, so it counts a miss and not a
    /// wobble. Reported with the comparison count and the worst value on the same line.
    /// </summary>
    private const float DroppedFrameMs = FrameBudgetMs * 1.5f;

    // ---- THE OVER-PAINT CENSUS (ModBuild 203) --------------------------------------------------
    // The whole argument is on Entry.OverPaintCovered and on MeasureOverPaint; these are its dials.

    /// <summary>
    /// How much of the OPEN SUB-VIEW's rect a drawing graphic must cover before it is called a
    /// FULL-FRAME PLATE. Deliberately looser than <c>CanvasConversion.FixedFitPlateHeightFraction</c>
    /// (0.95 of the WINDOW frame, per axis): that test exists to decide whether removing a backdrop
    /// would let a view scale to 1.000, and it may safely miss a plate. This one exists to decide
    /// whether a graphic can HIDE the sub-view's content, and a plate covering 60 % of a sub-view
    /// hides 60 % of it — so missing it is the expensive direction. Judged on AREA of the
    /// intersection with the sub-view rect, not per axis, because a wide short banner across the
    /// middle of a view and a tall narrow column down its side both occlude and neither passes a
    /// per-axis test.
    /// </summary>
    private const float OverPaintPlateFraction = 0.60f;

    /// <summary>Cap on how many DRAWING graphics one over-paint census records. The party window's
    /// walk visits ~2700 transforms and the census keeps one small struct per drawing graphic, so
    /// this is a hard bound on both the allocation and the plate-vs-graphic comparison count. When it
    /// bites, <see cref="Entry.OverPaintTruncated"/> says so and every count becomes a LOWER
    /// BOUND — the ModBuild 196 rule that a truncated instrument must never read clean.</summary>
    private const int MaxOverPaintGraphics = 3072;

    /// <summary>How many plates the report names in full. The count is always printed with its
    /// denominator; only the sentences are capped, so a window with fifteen plates still reports
    /// fifteen.</summary>
    private const int MaxPlatesReported = 4;

    /// <summary>How many graphics a single plate names as its largest victims. Three, because the
    /// project has been burned by a top-3 line being read as the UNION — so the line prints the
    /// COUNT and the TOTAL intersecting area next to them and says which is which.</summary>
    private const int MaxCoveredNamed = 3;

    /// <summary>How many foreign render subtrees the report names in full (the count is unbounded).
    /// The ModBuild 192 shop window pooled 44 of them; naming all of them would make the line
    /// unreadable and the first eight are enough to identify the family.</summary>
    private const int MaxForeignNamed = 8;

    /// <summary>A foreign renderer within this many HOST-LOCAL uGUI px of z = 0 is COPLANAR with the
    /// display quad, whose host-local z is 0 by construction. Half an authored pixel: at the party
    /// window's ~0.00052 world units per authored px that is ~0.26 mm, far below any authored depth
    /// offset and far above float noise on a transform chain.</summary>
    private const float ForeignCoplanarEpsPx = 0.5f;

    // ---- THE SUB-VIEW SWEEP BURST (ModBuild 203) -----------------------------------------------
    // The whole argument is on Entry.SubViewChanges. These are its dials, and their arithmetic is
    // stated because a sweep costs a MEASURED 1.71 ms on this window against an 11.11 ms budget.

    /// <summary>
    /// Sweep the capture layer on EVERY frame for this many frames after the open sub-view changed,
    /// WHETHER OR NOT THE FIRST SWEEPS FIND ANYTHING.
    ///
    /// <para><b>THE FLOOR IS SIZED AGAINST AN AMBIGUITY IN THE EVIDENCE, ON PURPOSE.</b> The ModBuild
    /// 202 log reads its big arrivals 0.25 s after the view opened (2448 transforms after ABILITY
    /// CARDS, 294 after PERKS). That 0.25 s has TWO readings and the log cannot separate them: either
    /// the transforms arrived then, or they arrived immediately and the ordinary 15-frame cadence —
    /// 0.26 s at the session's measured p50 of 17.33 ms — simply did not look until then. A floor
    /// shorter than the ambiguity would answer the wrong one of those and read "0 late joiners" while
    /// the hole was still open, which is the ModBuild 196 mistake (a remedy that stops before its own
    /// diagnostic can fire carries no information). 16 frames is 0.18 s at 90 Hz and 0.28 s at the
    /// measured 58 fps, so it spans the observation under either reading.</para>
    ///
    /// <para>COST: 16 x 1.71 ms = 27 ms of CPU spread over 16 frames = 1.71 ms on each, i.e. 15 % of
    /// one 90 Hz frame or 10 % of a measured 17.33 ms one, once per tab press and never during a
    /// drag.</para>
    /// </summary>
    private const int MinSweepBurstFrames = 16;

    /// <summary>Hard cap on a burst, including the extension. A burst only ever reaches this length
    /// while its sweeps KEEP finding late joiners, so reaching it is itself the finding: the game is
    /// still repopulating after ~0.35-0.55 s and the remaining fix is an arrival HOOK, not a poll.
    /// This is deliberately NOT the ModBuild 193 remedy: that swept every frame of every drag forever,
    /// was measured doing it (3501 sweeps, 2448 late joiners) and was falsified by the user reporting
    /// the flicker identical. This one is armed by a CONTENT CHANGE and disarms itself.</summary>
    private const int MaxSweepBurstFrames = 32;

    /// <summary>Consecutive sweeps that move NOTHING which end an extended burst. Three, so a single
    /// quiet frame in the middle of a repopulation does not close the window early.</summary>
    private const int SweepBurstMissTolerance = 3;

    /// <summary>
    /// <b>THE COST FUSE.</b> No SECOND burst may be armed within this many frames of the last one.
    /// Without it, a signature that flapped every frame would re-arm every frame and this remedy
    /// would silently become the permanent per-frame sweep that ModBuild 196 measured at 1.48-1.94 ms
    /// of a 17 ms frame and removed. With it the sweeps are bounded to at most
    /// <see cref="MaxSweepBurstFrames"/> frames in every 64, i.e. a worst case of 32/64 x 1.71 ms =
    /// 0.86 ms per frame even against a pathological window that changes its signature every frame.
    /// <para>It COALESCES rather than suppresses: a change inside the cooldown still forces the
    /// capture-frame re-measure, still extends a burst that is still running, and is COUNTED
    /// (<see cref="Entry.SubViewChangesCoalesced"/>) and printed — so a window whose signature really
    /// does flap is visible in the log instead of being silently throttled. The fuse must never count
    /// a class whose normal life looks like the abuse, and a repopulating window's own settling IS
    /// its normal life.</para>
    /// </summary>
    private const int SweepBurstCooldownFrames = 64;

    // ---- THE TMP SUB-MESH CULL LATCH (ModBuild 204) ---------------------------------------------
    // The whole argument is on Entry.SubMeshCullLatched and on RepairSubMeshCull. These are its
    // dials, and there are deliberately only two of them: the repair itself is UNCONDITIONAL and has
    // no threshold to tune.

    /// <summary>
    /// Frames after the hand lets go at which the SETTLED sub-mesh cull reading is taken (and the
    /// pair cache re-collected for it). THIRTY, which is <see cref="SweepAfterMotionFrames"/> — the
    /// same window this class already calls "moving, or settling from a move" — so the reading is by
    /// construction the first one taken outside the drag. It is the fourth of the four readings the
    /// MOVING-vs-SETTLED line prints, and it is the one that decides the user's report: a count that
    /// is non-zero HERE, after the release repair has already run, is a FROZEN state and not a
    /// transient one.
    /// </summary>
    private const int SubMeshCullSettleFrames = 30;

    /// <summary>Hard cap on the (parent, sub-mesh) pair cache the per-frame invariant repair walks.
    /// The character window carries 19 sub-meshes; 512 is two orders above anything measured and
    /// exists only so a pathological window cannot turn a per-frame compare loop into a spike. When
    /// it bites, <see cref="Entry.CullPairsTruncated"/> says so and every count below it is a LOWER
    /// BOUND — the standing rule that a truncated instrument must never read clean.</summary>

    /// <summary>How many latched sub-meshes the report names in full. The COUNT is always printed
    /// with its denominator; only the sentences are capped.</summary>
    private const int MaxCullNamed = 3;

    // ---- THE CAPTURE-FRAME HYSTERESIS (ModBuild 204) --------------------------------------------
    // The whole argument is on MeasureFrame's THE 28/28 FLAP paragraph. These are its dials and each
    // one is sized against a measured number from the ModBuild 203 hardware log.

    /// <summary>
    /// <b>A CONTENT RE-MEASURE IS NOT SCHEDULED WHILE THE HOST MOVED WITHIN THIS MANY FRAMES AND
    /// NEITHER ITS RECT NOR ITS SCALE CHANGED.</b> ONE.
    ///
    /// <para>The principle is <see cref="NoticeGeometry"/>'s own, stated in its own comment since
    /// ModBuild 193: it returns dirty for a rect or a scale change and <i>NOT for a pure translation
    /// — moving a window changes nothing about what it draws or how big its render target must be</i>.
    /// Everything <see cref="MeasureFrame"/> measures is expressed in HOST-LOCAL uGUI pixels
    /// (<see cref="TryHostLocalBounds"/> inverse-transforms every world corner through the host), so a
    /// rigid translation of the host cancels exactly and the union it produces is translation
    /// invariant. Re-running it while the hand carries the window therefore cannot produce new
    /// information, and the ModBuild 203 log measured what it produces instead: 45 of 58
    /// re-allocations inside a grab, 2.51/s while held against 0.29/s while not.</para>
    ///
    /// <para><b>WHY ONE AND NOT MORE.</b> This value must stay STRICTLY BELOW
    /// <see cref="ReleaseSettleFrames"/> (2), because the release repair's forced re-measure runs at
    /// exactly <c>ReleaseSettleFrames</c> frames after the last motion frame and must never be
    /// swallowed by this gate — a release that does not re-measure is the ModBuild 192 defect back
    /// again. At 1, a drag frame (the pose is rewritten every frame by
    /// <c>GrabbableModal.SyncHostToFrame</c>) and the frame after it are gated; the release frame is
    /// not. If <see cref="ReleaseSettleFrames"/> is ever raised, this may follow it; if it is ever
    /// lowered to 1, this must go to 0.</para>
    /// </summary>
    private const int TranslateHoldFrames = 1;

    /// <summary>
    /// <b>CONSECUTIVE MEASUREMENTS A SMALLER NEED MUST HOLD BEFORE THE CAPTURE FRAME IS ALLOWED TO
    /// SHRINK.</b> FOUR, and the number it swallows is arithmetic rather than taste.
    ///
    /// <para>A settled window re-measures on <see cref="ContentMeasureIntervalFrames"/> = 15 frames.
    /// Four consecutive measurements therefore span three intervals = 45 frames = <b>0.50 s at 90 Hz
    /// and 0.78 s at the session's measured 17.33 ms p50</b>. So this run length PROVABLY SWALLOWS any
    /// flap whose SMALLER value dwells for less than that: the ModBuild 203 flap alternates 28/28 at
    /// up to 2.51 re-allocations per second, i.e. a dwell of about 0.40 s, which is inside the window
    /// at both frame rates.</para>
    ///
    /// <para><b>WHAT IT DOES NOT SWALLOW, stated so the next log is readable.</b> A union that
    /// genuinely stays small for longer than 0.8 s and then grows again is not a flap and is not
    /// treated as one: it shrinks, and it re-grows when the content comes back, at one reallocation
    /// per cycle instead of 2.51/s. That is the correct behaviour for real content and it is why this
    /// is a run length and not a lock.</para>
    /// </summary>
    private const int FrameShrinkRunMeasurements = 4;

    /// <summary>
    /// The DEAD BAND, in <see cref="FrameQuantumPx"/> units: a need within this many quanta of the
    /// committed frame may never shrink it AT ALL, however long it holds.
    /// <para>ONE, and that single quantum is precisely the ModBuild 203 flap: the two frames the party
    /// window alternated between differ by exactly one 32 px quantum in Y (overspill 32x384 against
    /// 32x416). With this dead band that alternation cannot move the frame in either direction once
    /// the larger value has been seen, so the flap is dead by construction and not merely damped by
    /// the run length. THE PRICE, stated: a genuine one-quantum shrink never happens, i.e. up to 32 px
    /// of transparent margin on an edge is kept forever — under 2 % of the smallest window this path
    /// serves, which is below <see cref="RectChangeFraction"/>'s own reallocation threshold and
    /// therefore would not have paid for a reallocation anyway.</para>
    /// </summary>
    private const int FrameShrinkDeadBandQuanta = 1;

    /// <summary>
    /// <b>THE GROWTH BOUND.</b> A single measurement that asks for MORE than this many new quanta on
    /// one edge is adopted IN FULL — content is never cropped, not for one frame — and marked an
    /// OUTLIER.
    ///
    /// <para>FOUR (128 px). Sized against the one-off in the ModBuild 203 log: a single measurement
    /// produced <c>2276x1464 (overspill 288x384)</c> against the session's steady 32 px of X
    /// overspill, i.e. a jump of 256 px = EIGHT quanta on one edge, and it was the only reading of the
    /// session whose rate fell below the band limit (<c>GOT 1.75</c>). Four quanta sits between the
    /// ordinary per-measurement movement observed (0-1 quanta) and that outlier, so the classifier
    /// separates them.</para>
    ///
    /// <para><b>HOW AN OUTLIER IS RELEASED — the answer to "a window must not ratchet upward
    /// forever".</b> Adopting it sets <see cref="Entry.FrameOutlierPending"/>, and while that is set
    /// the shrink run length drops from <see cref="FrameShrinkRunMeasurements"/> to
    /// <see cref="FrameOutlierShrinkRunMeasurements"/>. So a one-off that stops being needed is given
    /// back after two settled measurements (~0.3-0.5 s) instead of four, while a growth the content
    /// genuinely needs simply keeps being re-measured and never shrinks. The flag is cleared the
    /// moment a shrink commits or an ordinary (non-outlier) growth is adopted, and BOTH the outlier
    /// count and the release count are printed — a large refused-shrink count next to a large frame is
    /// the signature that this bound is what to look at next.</para>
    /// </summary>
    private const int FrameOutlierGrowQuanta = 4;

    /// <summary>Shrink run length while an OUTLIER growth is pending — see
    /// <see cref="FrameOutlierGrowQuanta"/>. Two, i.e. one measurement interval of confirmation, which
    /// is the shortest run that is not a single reading.</summary>
    private const int FrameOutlierShrinkRunMeasurements = 2;

    // ---- THE GROWTH CLAMP (ModBuild 205) --------------------------------------------------------

    /// <summary>
    /// <b>THE WIDEST CAPTURE FRAME, PER AXIS, THAT STILL ACHIEVES THE BAND LIMIT — and the correction
    /// of the one rule ModBuild 204 got wrong.</b>
    /// <c>MaxRtDimension / BandLimitedTexelsPerPixel = 4096 / 2.00 = <b>2048</b></c> uGUI px.
    ///
    /// <para><b>WHAT 204 SHIPPED AND WHAT IT COST.</b> 204 made the capture frame GROW immediately and
    /// unconditionally, and it fixed what it was built for: re-allocations fell from 58 in 62 s to 0-6
    /// per session. But the hardware log then carried 204's own warning — the party window
    /// <c>GREW its capture frame to 2052x1464 uGUI px, and that growth stepped the achievable capture
    /// rate from 2.00 down to 1.75</c> — with <c>ACHIEVED 1.75</c> on 15 of 70 readings,
    /// <c>NOT BAND-LIMITED</c> on 39 of 90 verdicts, and targets allocated at 3591x2562, 3591x2618 and
    /// 3927x2618 alongside the intended 4040x2992. The arithmetic is a cliff, not a slope: at a 2020 px
    /// frame the per-axis ceiling is <c>4096 / 2020 = 2.027</c> -> <b>2.00</b>, and at 2052 px — ONE
    /// <see cref="FrameQuantumPx"/> more — it is <c>1.996</c> -> <b>1.75</b>, because
    /// <see cref="RateQuantum"/> is 0.25 and there is no 1.95. The cliff sat 28 px from the operating
    /// point and "grow immediately" walked off it; the shrink hysteresis, doing exactly what it was
    /// told, then HELD it there (the dead band is one quantum and the way back is one quantum).</para>
    ///
    /// <para><b>WHY THAT IS THE WORST TRADE THIS CLASS CAN MAKE.</b> <see cref="BandLimitFactor"/>'s
    /// header is the argument in full: below the band limit a 1-px glyph stroke exists or does not
    /// exist depending on the sub-texel phase, a moving window sweeps that phase, and on release it
    /// LOCKS at whatever the hand left behind — <i>"the state as it was during the flickering gets
    /// FROZEN, which can lead to certain elements not being displayed"</i>. That IS the defect under
    /// investigation. What the growth was buying is overspill: content that lies OUTSIDE the host rect,
    /// i.e. outside the window's own frame. <b>Resolution for the whole window outranks overspill.</b>
    /// So a growth is now clamped to this budget and the crop is logged edge by edge, with the graphic
    /// that set each edge (<see cref="ExtremeRecord"/>) — see <c>ClampFrameToBandLimit</c>.</para>
    ///
    /// <para><b>AND THE CLAMP ONLY EVER ACTS WHEN IT ACTUALLY BUYS THE BAND LIMIT.</b> If an axis of
    /// the HOST RECT alone is already past this budget, no amount of cropping can reach 2.00 — cropping
    /// there would be pure loss — so the clamp stands down completely on both axes and the frame is the
    /// full measured union again. Cropping content is justified by saving the band limit and by nothing
    /// else.</para>
    ///
    /// <para><b>THE RESIDUAL, STATED AS NUMBERS, BECAUSE CLAMPING IS A MITIGATION AND NOT A CURE.</b>
    /// The 28 px margin exists because ModBuild 202 widened this host from 1143 to <b>1988</b> authored
    /// px, which cut its rate ceiling from <c>4096/1143 = 3.58</c> to <c>4096/1988 = 2.06</c>. Every
    /// number below is arithmetic on the constants in this file, and NONE of the caps are changed here
    /// — they are reported so the integrator can decide:
    /// <list type="bullet">
    /// <item><b>HOST WIDTH FOR ONE FULL <see cref="FrameQuantumPx"/> OF GROWTH HEADROOM: 1984 px.</b>
    /// The frame must satisfy <c>host + overspill + 32 &lt;= 2048</c>; at the measured 32 px of X
    /// overspill that is a host of 1984 — <b>4 px narrower than today</b>. Today's 1988 leaves 2020 and
    /// a 28 px margin, i.e. not quite one quantum, which is the whole bug.</item>
    /// <item><b>HOST WIDTH FOR ONE FULL <see cref="RateQuantum"/> OF HEADROOM (ceiling 2.25): 1788
    /// px.</b> <c>4096 / 2.25 = 1820.4</c>, so the FRAME must be &lt;= 1820 px; at 32 px of overspill
    /// the host is 1788 — 200 px narrower than today, and still 645 px wider than ModBuild 201's
    /// 1143.</item>
    /// <item><b><see cref="MaxRtDimension"/> FOR RATE 2.00 TO SURVIVE THE CURRENT FRAME: 4104 px.</b>
    /// The asked frame is 2052x1464, so X needs <c>2052 x 2.00 = 4104</c> and Y needs 2928; the next
    /// tier a driver actually advertises is 8192. <b>The VRAM cap does NOT bind there:</b>
    /// <c>VramBytesFor</c> is 13.33 bytes per texel (4 colour + 4 depth/stencil + 4 + 4/3 mipped
    /// display), so 4104x2928 = 12,016,512 texels = <b>152.8 MB</b> against
    /// <see cref="MaxPanelVramBytes"/> = 160 MB — 4.5 % spare. The DIMENSION cap is what binds
    /// today.</item>
    /// <item><b>BUT THE VRAM CAP IS THE VERY NEXT WALL.</b> 160 MB / 13.33 B = 12,582,912 texels, i.e.
    /// at rate 2.00 a frame AREA of 3,145,728 uGUI px². The committed 2020x1496 frame is 3,021,920
    /// (96.1 % of it). At 2052 px wide the height at which the VRAM cap forces 2.00 -> 1.75 is
    /// <c>3,145,728 / 2052 = 1533</c> px — <b>69 px, barely two quanta, above today's frame</b>. So
    /// raising <see cref="MaxRtDimension"/> alone buys this window about two quanta of Y before
    /// <see cref="MaxPanelVramBytes"/> takes over, and the real budget is
    /// <c>min(MaxPanelVramBytes, MaxTotalVramBytes - others)</c>, which is SMALLER whenever a second
    /// large panel is open.</item>
    /// </list></para>
    /// </summary>
    private const float BandLimitFrameBudgetPx = MaxRtDimension / BandLimitedTexelsPerPixel;

    /// <summary>How many band-limit clamp STATE CHANGES one window prints in full before the sentences
    /// stop. EIGHT. The clamp is expected to change state once or twice in a session — it is a function
    /// of the host rect and the content union, both of which settle — but a window whose content
    /// genuinely oscillates across the budget would otherwise print a paragraph every
    /// <see cref="ContentMeasureIntervalFrames"/> frames, and this project does not ship an instrument
    /// that can flood the log. THE COUNT ITSELF IS NEVER CAPPED: the CAPTURE FRAME line carries the
    /// live crop and the total number of state changes on every report, so silence here can never be
    /// read as "it stopped happening" — exactly the standing rule that a truncated instrument must not
    /// read clean.</summary>
    private const int MaxBandClampLines = 8;

    // ---- state -------------------------------------------------------------------------------

    private static readonly List<Entry> Entries = new(MaxPanels);

    /// <summary>Cameras whose capture-layer bit this frame's <see cref="Camera.onPreCull"/> cleared,
    /// with the mask to hand back in <see cref="Camera.onPostRender"/>. Keyed by camera; empty at
    /// every LateUpdate (asserted, and swept if not).</summary>
    private static readonly Dictionary<Camera, int> MaskedCameras = new(4);

    /// <summary>Host GameObject instance ids this path has already REFUSED (over budget, or the
    /// driver would not allocate the target). Never re-asked while that conversion lives: the
    /// refusal is already on the record as one Warn, and re-asking every Update would turn a
    /// bounded diagnosis into a per-frame log flood. Cleared when the conversion goes away.</summary>
    private static readonly HashSet<int> Refused = new(4);

    private static bool _hooksInstalled;

    // ---- CONTENT-INTEGRITY HOOKS (ModBuild 196) -----------------------------------------------
    // The three states the "kaputte Anzeige" report has to be able to tell apart, and the only
    // reason these are static rather than per-entry: a font atlas repack and a canvas update are
    // PROCESS-wide events, and a per-entry copy of them would say the same thing N times.

    /// <summary>Frame number of the last <see cref="Font.textureRebuilt"/> (a DYNAMIC font atlas was
    /// re-packed, which invalidates every already-generated text mesh that samples it) or of the last
    /// TMP atlas change this class detected. -1000 = never.</summary>
    private static int _fontRebuildFrame = -1000;

    /// <summary>How many font atlas repacks this session — legacy <see cref="Font"/> events plus
    /// detected TMP atlas-texture changes. Printed with the coincidence counts so "no repack
    /// happened" and "repacks happened and no capture coincided" are different sentences.</summary>
    private static int _fontRebuilds;

    /// <summary>Name of the font whose atlas was re-packed last, for the report.</summary>
    private static string _fontRebuildName = string.Empty;

    /// <summary>Frame number of the last <see cref="Canvas.willRenderCanvases"/>, i.e. the last frame
    /// uGUI actually ran its layout + graphic rebuild queue. A capture taken on a frame where this is
    /// NOT the current frame ran BEFORE the canvas finished rebuilding — failure mode (b).</summary>
    private static int _canvasUpdateFrame = -1000;

    /// <summary>Atlas fingerprints of every <see cref="TMPro.TMP_FontAsset"/> this class has seen, so
    /// a TMP repack (which does NOT raise <see cref="Font.textureRebuilt"/>) is still detectable.
    /// Keyed by font asset instance id; the value is (atlasTextureCount, atlasTexture instance id).</summary>
    private static readonly Dictionary<int, (int Count, int TextureId)> TmpAtlasSeen = new(8);

    private static bool _noLayerLogged;
    private static bool _noHeadLogged;
    private static bool _errorLogged;
    private static bool _capLogged;
    private static float _nextReport;
    private static int _poseSyncFrame = -1;
    private static long _vramTotal;

    private static readonly List<Transform> Scratch = new(256);
    private static readonly Vector3[] Corners = new Vector3[4];
    private static readonly StringBuilder Sb = new(1024);

    /// <summary>
    /// ONE EDGE OF THE MEASURED CONTENT UNION, WITH THE GRAPHIC THAT SET IT (ModBuild 204).
    ///
    /// <para><b>WHY THIS EXISTS.</b> The capture frame flapped between two values for twelve builds
    /// and no instrument in this mod could say whether the UNION genuinely changed or whether the SAME
    /// graphic was being measured to two different heights. The only extremes field that existed —
    /// <c>CanvasConversion.3.Fit.cs</c>'s <c>CONTENT EXTREMES</c> — names the LEFT and RIGHT edge only,
    /// and the ModBuild 203 flap is entirely in Y. <see cref="MeasureFrame"/> already computes the
    /// union and already holds every graphic's host-local bounds, so recording the argmax/argmin costs
    /// four compares per drawing graphic and adds no walk.</para>
    /// </summary>
    private struct ExtremeRecord
    {
        /// <summary>The extreme host-local coordinate this record holds (uGUI px).</summary>
        internal float Value;

        /// <summary>The graphic that set it, and where it landed (host-local uGUI px).</summary>
        internal string Name;
        internal Rect Rect;
        internal bool Valid;
    }

    /// <summary>Index into an <see cref="ExtremeRecord"/> array: the four edges of the content union.
    /// Kept as constants rather than an enum so the arrays index directly and no boxing or
    /// <c>Enum.ToString</c> reaches a per-frame path.</summary>
    private const int EdgeLeft = 0;
    private const int EdgeRight = 1;
    private const int EdgeDown = 2;
    private const int EdgeUp = 3;

    private static readonly string[] EdgeNames = { "LEFT (xMin)", "RIGHT (xMax)", "BOTTOM (yMin)", "TOP (yMax)" };

    /// <summary>
    /// One (TextMeshPro parent, TMP sub-mesh child) pair the per-frame cull invariant checks. Held in
    /// a cache rather than re-walked, because the walk that finds them costs a measured ~1.7 ms and
    /// the check itself is one boolean compare — see <see cref="RepairSubMeshCull"/>.
    /// </summary>
    private readonly struct CullPair
    {
        internal readonly TMPro.TMP_Text Parent;
        internal readonly TMPro.TMP_SubMeshUI Sub;

        internal CullPair(TMPro.TMP_Text parent, TMPro.TMP_SubMeshUI sub)
        {
            Parent = parent;
            Sub = sub;
        }
    }

    /// <summary>One supersampled panel: everything allocated for it and everything to hand back.</summary>
    private sealed class Entry
    {
        internal ConvertedPanel Panel = null!;
        internal string Window = string.Empty;

        /// <summary>THIS PANEL'S PRIVATE CAPTURE LAYER, taken from the pool at engage and handed back
        /// on stand-down. -1 = none held. It is the entry's whole isolation guarantee: the camera's
        /// culling mask is <c>1 &lt;&lt; Layer</c> and nothing else in the scene is ever on it. See
        /// PanelSupersample.5.Isolation.cs for why a shared layer was the ModBuild 193 defect.</summary>
        internal int Layer = -1;

        internal GameObject CamGo = null!;
        internal Camera Cam = null!;

        /// <summary>The CAPTURE target: MSAA, D24_UNorm_S8_UInt, and NO mip chain — a
        /// multisampled render target cannot carry one (see <see cref="CreateRt"/>). The camera
        /// renders here.</summary>
        internal RenderTexture Rt = null!;

        /// <summary>The DISPLAY target: single-sample, no depth, mipmapped. <see cref="Rt"/> is
        /// resolved into it after every capture and its mip chain is generated there. This is what
        /// the <see cref="RawImage"/> shows — unless <see cref="MipFallback"/>.</summary>
        internal RenderTexture MipRt = null!;

        /// <summary>The mipped display target could not be created: the RawImage falls back to the
        /// capture target, i.e. ModBuild 192's behaviour (MSAA but no mip chain). One Warn.</summary>
        internal bool MipFallback;

        internal GameObject DisplayGo = null!;
        internal RectTransform DisplayRect = null!;
        internal Canvas DisplayCanvas = null!;
        internal RawImage DisplayImage = null!;

        /// <summary>Every transform this entry moved onto the capture layer, with the layer it had
        /// before the first write. Restored on stand-down — and only while the transform is still on
        /// OUR layer (see <see cref="RestoreLayers"/>).</summary>
        internal readonly List<LayerRecord> Relayered = new(128);

        internal Vector2 Authored;
        internal int RtW;
        internal int RtH;
        internal int Msaa;
        internal int MipCount;
        internal long VramBytes;

        /// <summary>Render-target texels per AUTHORED window pixel this entry was allocated at — the
        /// value <see cref="ResolveFactor"/> returned, i.e. AFTER the
        /// <see cref="BandLimitFactor"/> floor. What the target actually ACHIEVED can still be lower
        /// if <see cref="MaxRtDimension"/> clipped an axis; that is <see cref="AchievedFactor"/>.</summary>
        internal float Factor;

        /// <summary>The raw <c>[WorldUI] PanelSupersampleFactor</c> value, before the floor. Printed
        /// next to <see cref="Factor"/> so the log never has to be guessed at.</summary>
        internal float ConfigFactor;

        /// <summary>True when <see cref="BandLimitFactor"/> raised an untouched DEFAULT config value;
        /// false when the user's own tuned value was taken verbatim. See <see cref="ResolveFactor"/>.</summary>
        internal bool FactorFloored;

        /// <summary>
        /// THE FACTOR THE RENDER TARGET ACTUALLY GOT, per axis, as
        /// <c>min(RtW / Frame.width, RtH / Frame.height)</c>. It differs from <see cref="Factor"/>
        /// only when <see cref="MaxRtDimension"/> clipped an axis (a capture frame wider than
        /// 4096 / factor), and then the whole ModBuild 198 argument is silently only partly in force
        /// on this window. It is printed with <see cref="Factor"/> and the ceiling on one line so
        /// "asked 2.00, achieved 2.00" and "asked 2.00, achieved 1.31 (clipped by the 4096 px
        /// ceiling)" can never look alike.
        /// </summary>
        internal float AchievedFactor;

        /// <summary>
        /// THE CAPTURE FRAME, in HOST-LOCAL uGUI pixels — what the camera frames, what the display
        /// quad is sized to, and what the render target is sized from. It is the UNION of the host
        /// rect and everything the window actually draws (<see cref="MeasureFrame"/>), so it equals
        /// the host rect exactly whenever the content fits inside it — which is the normal case and
        /// keeps this path's geometry identical to the dial's OFF geometry. When the content spills
        /// (the ModBuild 192 party window drew 1920x1080 into a 328x1080 host frame) the frame
        /// grows to cover the spill instead of cropping it.
        /// </summary>
        internal Rect Frame;

        /// <summary>The host rect the <see cref="Frame"/> was measured against (uGUI px).</summary>
        internal Rect HostRectAtMeasure;

        /// <summary>How far <see cref="Frame"/> had to grow past the host rect, per axis, uGUI px.
        /// 0/0 = the promise "OFF and ON geometry are identical" holds literally.</summary>
        internal float ExpandX;
        internal float ExpandY;

        /// <summary>The expansion hit <see cref="MaxContentExpansion"/> — content IS being cropped
        /// and the state line says so every report.</summary>
        internal bool ExpandClamped;
        internal bool ExpandClampWarned;

        // ---- the content-scale census (ModBuild 201) --------------------------------------------

        /// <summary>
        /// <b>THE SMALLEST SCALE AT WHICH THIS WINDOW DRAWS A SUBSTANTIAL PIECE OF CONTENT, relative
        /// to its own host. THIS IS THE ANSWER TO "WHAT DO THOSE TWO VIEWS DO DIFFERENTLY".</b>
        ///
        /// <para>The map room's character window hosts six sub-views. The ModBuild 200 log's own
        /// FIXED FIT lines give each one's scale: the perks view and the mercenary selector are
        /// written to <c>0.496</c> and <c>0.487</c> (<i>"the open sub-view group needs 1648x1080 px at
        /// scale 1 and fits a 803 px slot ... at scale 0.487 — SCALED TO FIT the slot"</i>); the other
        /// four sit at <c>1.000</c>. The user reports exactly those two as broken and exactly the
        /// other four as fine. The correlation is complete.</para>
        ///
        /// <para><b>AND THE CAPTURE PATH TURNS THAT SCALE STRAIGHT INTO A SAMPLING RATE.</b> The
        /// render target is sized from the HOST frame: <c>Rt = Frame.size * Factor</c>. So every
        /// authored pixel of an UNSCALED sub-view gets <see cref="BandLimitFactor"/> = 2.00 texels,
        /// and every authored pixel of the 0.487 sub-view gets <c>2.00 x 0.487 = 0.97</c>. <b>Those
        /// two sub-views are the only content in this mod that is captured at ONE texel per authored
        /// pixel — which is exactly the un-supersampled state that the ModBuild 198 band-limit floor
        /// exists to abolish, and exactly the state the whole shimmer complaint came from.</b> At that
        /// rate there is no headroom above Nyquist at all: a hairline or a glyph stem either lands on
        /// a texel centre or falls between two, and nothing downstream — not the mip chain, not
        /// anisotropy, not the eye — can put back a stroke the rasteriser never wrote. Combine it with
        /// a capture frame whose phase moves (<see cref="FrameQuantumPx"/>) and the picture re-rolls
        /// on every measurement: <i>perks.jpg</i>, a view that is almost entirely blank with a few
        /// surviving glyph fragments.</para>
        ///
        /// <para><b>WHAT THIS FIELD IS USED FOR, AND THE HONEST LIMIT OF IT.</b>
        /// <see cref="EffectiveFactor"/> asks for <c>BandLimitFactor / MinContentScale</c> so that the
        /// SCALED subtree, not the host, is what gets 2.00 texels per authored pixel. On the character
        /// window that is an ask of 4.11 against a 1715x1107 capture frame — 7049 px wide, which
        /// <see cref="MaxRtDimension"/> (4096) and <see cref="MaxPanelVramBytes"/> (160 MB at
        /// 13.3 bytes per texel, i.e. ~12.6 Mtexel) both refuse. The reachable rate is about 2.4–2.6,
        /// which lifts that sub-view from 0.97 to roughly 1.2 texels per authored pixel. That is a
        /// real improvement and it is NOT the band limit, so the report prints the achieved
        /// per-subtree rate as its own number rather than letting the host's 2.00 stand in for it.
        /// <b>Within this path's budget, a 0.487-scaled subtree CANNOT be given its band limit back.
        /// The only complete cure is for the sub-view not to be scaled at all.</b></para>
        /// </summary>
        internal float MinContentScale = 1f;

        /// <summary>Host-local area (uGUI px²) of the graphic that produced
        /// <see cref="MinContentScale"/>, and its name — so a suspicious reading can be traced to an
        /// object instead of argued about.</summary>
        internal float MinContentScaleArea;
        internal string MinContentScaleName = string.Empty;

        /// <summary>How many graphics the last frame measurement was able to read a scale from. A
        /// zero here means the census did not run, which must never read as "nothing is scaled".</summary>
        internal int ContentScaleSamples;

        /// <summary>Measurements that produced no scale sample at all. Kept so an unmeasured window
        /// and an unscaled one print differently.</summary>
        internal int ContentScaleUnmeasured;

        /// <summary>How many graphics the last measurement found below
        /// <see cref="ScaledContentThreshold"/>, and the largest host-local area among them.</summary>
        internal int ScaledGraphics;
        internal float ScaledGraphicsArea;

        /// <summary>THE WHOLE DISTRIBUTION of <see cref="MinContentScale"/> since engage, because the
        /// last reading is not the operating point and a summary field quoted as one has already cost
        /// this project a round. Printed as LOWEST / MEAN / HIGHEST with the count.</summary>
        internal int ContentScaleReadings;
        internal float ContentScaleSum;
        internal float MinContentScaleLowest = 1f;
        internal float MinContentScaleHighest = 1f;

        /// <summary>
        /// THE RATE THE RENDER TARGET IS ACTUALLY SIZED FROM — <see cref="Factor"/> raised by
        /// <c>1 / MinContentScale</c> and then cut down by <see cref="MaxRtDimension"/>, by the VRAM
        /// budget and by <see cref="RateQuantum"/>. Equal to <see cref="Factor"/> on every window
        /// whose content is unscaled, which is every window but one.
        /// </summary>
        internal float EffectiveFactor;

        /// <summary>What <see cref="EffectiveFactor"/> WOULD have been if nothing had cut it down.
        /// Printed next to it so a request that was refused never reads like one that was granted.</summary>
        internal float AskedFactor;

        /// <summary>The <see cref="MinContentScale"/> the current render target was sized for. A
        /// sub-view switch changes the content scale without touching the host rect or the capture
        /// frame, so this — not the frame — is what notices that the allocation is now for the wrong
        /// content.</summary>
        internal float ScaleAtAllocation = 1f;

        // ---- motion tracking (the ModBuild 192 "flackert beim Verschieben" case) ---------------
        internal bool HasPoseSnapshot;
        internal Vector3 LastPos;
        internal Quaternion LastRot;
        internal Vector3 LastScale;
        internal Vector2 LastRectSize;

        /// <summary>Frame number of the last host pose / scale / rect change. Everything within
        /// <see cref="SweepAfterMotionFrames"/> of it counts as "moving".</summary>
        internal int LastMotionFrame = -1000;

        /// <summary>Frames in this report window in which the host pose, scale or rect changed.</summary>
        internal int MotionFrames;

        /// <summary>Rect/scale changes that forced an immediate re-measure + re-sweep.</summary>
        internal int GeometryDirtyEvents;

        // ---- MOTION INSTRUMENT SELF-CHECK (ModBuild 194) ---------------------------------------
        // A zero in MotionFrames used to be ambiguous: it reads the same whether the window was
        // genuinely still, whether the detector never ran, or whether its epsilon was so large that
        // a real drag fell under it. The three fields below make those cases DISTINGUISHABLE from
        // the log alone, which is the only thing that stops a future round re-litigating whether
        // this instrument works. (For the record, and read from the ModBuild 193 hardware log: it
        // DOES — 23 of that log's 226 state lines carry a non-zero motion count, the largest being
        // 542 frames of a 900-frame report window, and several read "currently MOVING".)

        /// <summary>How many frames <see cref="NoticeGeometry"/> actually COMPARED a pose in this
        /// report window. "0 motion frames out of 0 comparisons" is a detector that never ran;
        /// "0 out of 900" is a window that genuinely did not move.</summary>
        internal int MotionTicks;

        /// <summary>The LARGEST single-frame world-space host translation seen in this report window,
        /// in world units. Reported next to the epsilon it was tested against, so a detector blinded
        /// by a wrong epsilon shows up as a large delta with a zero count.</summary>
        internal float MaxStepWorld;

        /// <summary>The epsilon the last comparison used, in world units. Derived from the panel's
        /// own lossy scale (half an authored uGUI pixel), never in metres — the map room runs ~198
        /// world units per real metre, so a fixed metric epsilon would be blind or always tripped.</summary>
        internal float MotionEpsWorld;

        /// <summary>The host's world units per authored uGUI pixel at the last comparison, so the
        /// two numbers above can be read in authored pixels as well as in world units.</summary>
        internal float WorldPerAuthoredPx;

        /// <summary>Authored uGUI pixels per RENDERED eye pixel, as
        /// <see cref="SamplingSentence"/> last measured it. 0 = not measured this report. It exists
        /// so the largest drag step can also be quoted in RENDERED pixels, which is the unit that
        /// decides what a drag looks like: a window sliding 8 rendered px per frame is a
        /// strobing/judder problem (one frame of pose lag is 8 px of error), while one sliding 0.4
        /// rendered px per frame can only be a sub-pixel SAMPLING problem. Those two want opposite
        /// fixes and the log could not tell them apart before ModBuild 194.</summary>
        internal float AuthoredPerRenderedPx;

        // ---- THE RESAMPLE INSTRUMENT (ModBuild 198) --------------------------------------------
        // The one thing three rounds of capture-side counters could not see: what the EYE does with
        // the finished texture. Every field below is written by SamplingSentence on the report
        // cadence AND re-read on every release, and every one of them is printed next to the
        // comparison it is judged against — see BandLimitFactor for the physics and AppendResample
        // for the sentence.

        /// <summary>How many times <see cref="SamplingSentence"/> COMPLETED a measurement in this
        /// report window, and how many times it bailed out and why. Without these a session in which
        /// the head camera was never found and a session in which everything measured clean print
        /// the same absence.</summary>
        internal int SamplingMeasured;
        internal int SamplingNoHead;
        internal int SamplingNoEyeTarget;
        internal int SamplingOffScreen;

        /// <summary>RT texels per RENDERED eye pixel at the last completed measurement — the number
        /// the whole ModBuild 198 argument turns on. Compared against
        /// <see cref="BandLimitedTexelsPerPixel"/> (2.0), the rate at which the eye stops reading
        /// unfiltered mip level 0 at all.</summary>
        internal float TexelsPerRenderedPx;

        /// <summary>The LOWEST <see cref="TexelsPerRenderedPx"/> seen since engage — the most
        /// MAGNIFIED sample, i.e. the window held closest to the face.
        /// <para><b>ModBuild 199 PRINTED THIS ALONE AND CALLED IT "WORST", AND IT MISLED THE READ OF
        /// THE WHOLE BUILD.</b> Lowest is the worst case for ALIASING, which is what ModBuild 198 was
        /// hunting, and it is the BEST case for LEGIBILITY, which is what the user reports now. The
        /// 199 hardware log's 97 completed measurements run 0.95 to 3.69 with a MEDIAN of 2.50, and
        /// only 7 of the 97 sit near 1.0 — all of them windows pulled up to the face mid-drag, several
        /// projecting larger than the entire 3072x3264 eye target. Quoting 0.95 as the operating point
        /// turned a 7-sample tail into a conclusion ("factor 2 traded aliasing for blur") that the
        /// other 90 samples contradict. Both ends and the mean are now printed together, over the
        /// comparison count, so no single number can stand in for the distribution again — this
        /// project's "read the whole distribution" rule, applied to the instrument itself.</para></summary>
        internal float TexelsPerRenderedPxWorst;

        /// <summary>The HIGHEST <see cref="TexelsPerRenderedPx"/> seen since engage — the most
        /// MINIFIED sample, and therefore the LEAST LEGIBLE one. This is the end of the distribution
        /// the "man kann den Text kaum lesen" report lives at, and ModBuild 199 did not record it at
        /// all.</summary>
        internal float TexelsPerRenderedPxMax;

        /// <summary>Running sum of <see cref="TexelsPerRenderedPx"/> over
        /// <see cref="SamplingMeasured"/>, so the line can print a MEAN next to the two extremes.
        /// Neither extreme is the operating point on its own.</summary>
        internal double TexelsPerRenderedPxSum;

        /// <summary>Completed measurements on which the window was MINIFIED
        /// (<see cref="AuthoredPerRenderedPx"/> &gt; 1), i.e. the eye was given FEWER pixels than the
        /// window has authored pixels. Printed over <see cref="SamplingMeasured"/>: it is the single
        /// number that separates a LEGIBILITY complaint from an ALIASING one, and no capture-side
        /// factor can move it.</summary>
        internal int MinifiedReadings;

        /// <summary>The most authored pixels ever squeezed into one rendered eye pixel since engage —
        /// the peak minification, and the direct measure of how much of the window's authored
        /// resolution the eye never receives.</summary>
        internal float AuthoredPerRenderedPxMax;

        /// <summary>Weight the hardware's trilinear filter gives to UNFILTERED mip level 0 at
        /// <see cref="TexelsPerRenderedPx"/>, 0..1. This is the aliasing that crawls under motion and
        /// freezes on release. A LOWER BOUND, not an exact figure: anisotropic filtering selects the
        /// LOD from the MINOR axis rate and therefore raises this on any yawed window.</summary>
        internal float Level0Weight;

        /// <summary>Completed measurements in this report window on which the eye read ANY unfiltered
        /// level 0 (<see cref="Level0Weight"/> above zero) while the window was MINIFIED, i.e. frames
        /// on which the ModBuild 198 defect was live. Printed over
        /// <see cref="SamplingMeasured"/>.</summary>
        internal int Level0Readings;

        internal int NextSweepFrame;
        internal int NextContentFrame;
        internal int LastMeasureFrame = -1000;
        internal int NextCaptureFrame;
        internal int Captures;
        internal double CaptureMs;
        internal float LastCaptureStart;

        internal int Reallocations;
        internal int Sweeps;
        internal double SweepMs;
        internal int LateJoiners;
        internal int ContentMeasures;
        internal double ContentMs;

        internal int LayersMoved;
        internal int ForeignSkipped;

        /// <summary>Transforms the sweep left alone because they were already on ANOTHER live
        /// entry's private capture layer. The expected value is ZERO — converted panels' subtrees
        /// are disjoint — and a non-zero value is a real finding: it means two panels share a
        /// transform, which without this guard would be a per-frame layer write war between them.</summary>
        internal int ForeignLayerSkipped;
        internal int NestedTotal;
        internal int NestedCaptured;
        internal bool MipWarned;

        // ---- CONTENT INTEGRITY: the three states the release report must separate ---------------
        // The user, still open after ModBuild 195: "beim Loslassen kann es passieren, dass die
        // dargestellte Anzeige kaputt ist ... bewege ich es nochmal und lasse los, sieht es wieder
        // anders aus". The photograph (.planning/debug/kaputte_anzeige.jpg) shows PER-GLYPH dropout
        // with the layout intact: 'Gesundheit:' renders as "Ge n i", 'Verbesserungen:' as "erb s e
        // :", while the SAME window's smaller quest line renders every character. Measured off that
        // image rather than assumed: the surviving glyphs sit exactly where a full-width layout puts
        // them (the trailing ':' of 'Verstärkungen:' and of 'Verbesserungen:' are 12 px apart for one
        // extra character, i.e. NOTHING was substituted or removed), the gaps read background
        // luminance (max 24 against a 20 background and a 147 ink), and the gaps do NOT line up into
        // vertical stripes across the six rows. So: the character ADVANCES are correct and the glyph
        // QUADS put no pixels down. These counters exist to decide WHY, from the log alone.

        /// <summary>How many times the capture ran while this entry existed — the denominator for
        /// every coincidence count below. Zero here means the instrument never ran, which is a
        /// different statement from "it ran and found nothing".</summary>
        internal int CaptureTicks;

        /// <summary>(a) Captures taken on the same frame as a font atlas repack. A non-zero value is
        /// the ONLY thing that makes "the capture caught the atlas mid-flight" a live explanation.</summary>
        internal int CapturesDuringFontRebuild;

        /// <summary>(b) Captures taken on a frame in which <see cref="Canvas.willRenderCanvases"/> had
        /// NOT yet fired, i.e. before uGUI rebuilt the layout and the graphics for that frame.
        /// EXPECTED ZERO — the capture camera renders inside the camera loop, which Unity runs after
        /// <c>PostLateUpdate.PlayerUpdateCanvases</c>. A non-zero value overturns that reading.</summary>
        internal int CapturesBeforeCanvasUpdate;

        /// <summary>Content-integrity scans made (<see cref="MeasureContent"/>). The comparison count
        /// that makes every number below readable.</summary>
        internal int ContentScans;
        internal double ContentScanMs;

        /// <summary>Content scans that THREW. Counted so a scan that failed can never be read as a
        /// scan that came back clean — the same rule as every other instrument in this file.</summary>
        internal int ContentScanFailures;
        internal bool ContentScanFailWarned;

        /// <summary>Text components found by the last scan, and the glyph lookups it made.</summary>
        internal int TextComponents;
        internal int GlyphsChecked;

        /// <summary>(c-1) Characters the text asks for that its font asset does NOT have, fallbacks
        /// included. This is the direct test of the dynamic-font-atlas hypothesis: non-zero means the
        /// atlas genuinely cannot serve the string and no amount of re-capturing will help.</summary>
        internal int GlyphsNotInAtlas;

        /// <summary>(c-2) Non-whitespace characters the text layout marked NOT VISIBLE — the text
        /// engine itself decided not to draw them (overflow truncation, a missing-glyph replacement,
        /// a maxVisibleCharacters clamp).</summary>
        internal int GlyphsNotVisible;

        /// <summary>(c-3) Non-whitespace characters that ARE visible but whose generated quad has
        /// (near) zero area, i.e. they hold their advance and put no pixels down. THAT IS THE EXACT
        /// SHAPE OF THE PHOTOGRAPH, so this is the counter that confirms or kills it.</summary>
        internal int GlyphsBlankQuad;

        /// <summary>The worst single text component of the last scan, so the log names a suspect
        /// instead of only a total.</summary>
        internal string WorstText = string.Empty;
        internal int WorstTextBad;
        internal int WorstTextChecked;

        /// <summary>(c-4) Text components uGUI/TMP has switched OFF at the renderer: the
        /// <see cref="CanvasRenderer"/> reports <c>cull</c>, or its effective alpha is zero. Those put
        /// no pixels into the CAPTURE at all, for reasons that have nothing to do with glyphs.
        ///
        /// <para><b>THIS COUNTER EXISTS TO KEEP TWO INDISTINGUISHABLE-LOOKING FAULTS APART.</b> "An
        /// element is missing from the capture" and "an element IS in the capture and something is
        /// painted over it" look identical to a player and can look identical to a probe that only
        /// asks whether an object was captured. Everything else in this scan measures the TEXT SOURCE
        /// — whether the glyph exists, whether the layout marked it visible, whether its quad has
        /// area — which is upstream of BOTH. This one field is the only one that speaks to whether
        /// the component draws at all. Neither this scan nor any other field here can see a graphic
        /// that drew correctly and was then painted over: that question is answered by the draw order,
        /// i.e. by the ISOLATION field's sortingOrder and by CanvasConversion's own order ladder, not
        /// here. The report says so in as many words so a future round cannot read a clean scan as
        /// "nothing is covering anything".</para></summary>
        internal int TextCulled;

        // ---- THE TMP SUB-MESH FAMILY (ModBuild 200) --------------------------------------------
        // WHAT THE CONTENT SCAN HAS NEVER LOOKED AT, and the reason four builds of clean readings sit
        // next to a photograph of dozens of missing glyphs.
        //
        // A TextMeshProUGUI component does NOT draw all of its own glyphs. Whenever a string needs a
        // SECOND ATLAS PAGE, a FALLBACK FONT or an INLINE SPRITE, TMP splits the run across extra
        // materials and hands those quads to TMP_SubMeshUI components on CHILD GameObjects — each
        // with its own CanvasRenderer, its own material, its own texture, its own GameObject layer
        // and its own active state. textInfo.meshInfo[1..] holds their VERTEX DATA, which is what
        // ScanTmpMesh reads and finds clean; NoteRendererState then asks the PARENT's CanvasRenderer
        // whether it was drawn and never asks theirs. And MeasureContentCore's walk classifies a
        // TMP_SubMeshUI as "a Graphic that is neither TMP_Text nor Text" and skips it outright.
        //
        // So the instrument's answer to "were these quads drawn?" is a measurement of a DIFFERENT
        // OBJECT than the one that draws them. That is exactly the shape this project keeps paying
        // for: a flawless measurement of the wrong stage looks like proof. The 199 log reads 0
        // SUBMITTED-MESH defects out of 3,471 quads on the very window whose photograph shows the
        // stat block rendering "c l e h Ba el n z ari b." where whole words belong — and the glyphs
        // that DO survive are crisp and correctly positioned, which is what a per-material split
        // looks like and is NOT what any content defect looks like.
        //
        // The capture path adds one failure mode of its own on top: this panel's camera culls by its
        // PRIVATE LAYER, so a sub-mesh child born between two layer sweeps is on the game's UI layer
        // and is INVISIBLE TO THE CAPTURE while its parent is captured normally. That is repaired
        // here unconditionally rather than reported, because a remedy gated behind its own diagnostic
        // is the other mistake this lane has already made.

        /// <summary>TMP sub-mesh child components the last scan visited — the denominator for every
        /// count below. Zero means the window's text needs no second material at all, and that is a
        /// different statement from "they were all fine".</summary>
        internal int SubMeshesSeen;

        /// <summary>
        /// <b>POOLED SUB-MESHES — the denominator ModBuild 200 was missing, and without it that
        /// build's own numbers could not be read.</b> TMP creates one sub-mesh child per material
        /// reference a string has EVER needed and leaves the surplus in place with an EMPTIED mesh. A
        /// pooled sub-mesh is legitimately culled, legitimately transparent and legitimately without a
        /// bound texture — its normal life looks exactly like the abuse every counter below is
        /// hunting. The ModBuild 200 log read <c>19 TMP SUB-MESH(ES) ... of which 17
        /// culled/transparent</c> on the character window and <c>0 of 0</c> or <c>0 of 1</c> on every
        /// other window in the session; that gap is NOT evidence, because the character window is
        /// simply the only one with enough text to pool any. From ModBuild 201 the counters below are
        /// scoped to <see cref="SubMeshesInUse"/> and this holds the rest.
        /// </summary>
        internal int SubMeshesEmpty;

        /// <summary>Sub-meshes whose mesh actually carries vertices — glyphs the text engine handed
        /// over and that CAN therefore be missing from the picture. The real denominator for every
        /// count below.</summary>
        internal int SubMeshesInUse;

        /// <summary>Sub-meshes whose GameObject was INACTIVE, so their quads drew nothing.</summary>
        internal int SubMeshesInactive;

        /// <summary>Sub-meshes whose CanvasRenderer was culled or fully transparent.</summary>
        internal int SubMeshesCulled;

        /// <summary>Sub-meshes found on a layer OTHER than this panel's private capture layer — i.e.
        /// present in the mesh, present in the eye, MISSING FROM THE CAPTURE. Repaired on the spot;
        /// this is the count of how many needed it.</summary>
        internal int SubMeshesWrongLayer;

        /// <summary>Sub-meshes with no material or no main texture bound — their quads sample
        /// nothing.</summary>
        internal int SubMeshesNoTexture;

        /// <summary>The worst sub-mesh found, named, for the report.</summary>
        internal string SubMeshWorst = string.Empty;

        /// <summary>Text components the last scan found completely clean. Printed as "K of M", so a
        /// scan that found nothing is visibly a scan that CHECKED something.</summary>
        internal int TextClean;

        /// <summary>The scan hit <see cref="MaxGlyphChecksPerScan"/> — it is a LOWER BOUND, not a
        /// total, and the report says so rather than letting a truncated scan read as a clean one.</summary>
        internal bool ContentScanTruncated;

        /// <summary>What the font assets behind this window are, measured not assumed: population
        /// mode, atlas size, atlas count. A STATIC atlas cannot repack, which kills the whole
        /// hypothesis for that window by construction.</summary>
        internal string AtlasNote = string.Empty;

        // ---- the repairs ------------------------------------------------------------------------

        /// <summary>Release repairs run (forced re-measure + forced sweep + forced text
        /// regeneration on the frame the window comes to rest).</summary>
        internal int ReleaseRepairs;

        /// <summary>Text regenerations forced by a font atlas repack, as opposed to by a release.</summary>
        internal int RebuildRepairs;

        /// <summary>Text components whose glyphs were re-requested and whose mesh was re-generated by
        /// the last repair, and how many characters were re-requested.</summary>
        internal int RegeneratedComponents;
        internal int RegeneratedChars;

        /// <summary>The <see cref="LastMotionFrame"/> the release repair has already been run for, so
        /// one release costs exactly two repairs and never one per frame.</summary>
        internal int ReleaseRepairedMotionFrame = -1000;
        internal int ReleaseRepairStage;

        /// <summary>Text components the release repair actually re-generated, and what that cost in
        /// milliseconds. ModBuild 196 reported the regeneration as if it always ran; it did not (see
        /// <see cref="ReleaseRepair"/>), so from ModBuild 197 the count and the cost are both
        /// measured and both printed.</summary>
        internal int ReleaseRegenComponents;
        internal double ReleaseRegenMs;

        /// <summary>GrabPass-shader graphics this panel has switched off, cumulative since engage.
        /// See <c>NeutraliseGrabPassBlur</c> — this was the Character sub-view's whole defect.</summary>
        internal int GrabPassNeutralised;

        // ---- THE RELEASE EDGE ITSELF (ModBuild 197) ---------------------------------------------
        // The user, after 196: "das Flackerproblem WÄHREND DER BEWEGUNG ist noch da — inklusive der
        // möglichen kaputten Darstellung, wenn man nach der Bewegung ABRUPT loslässt". ABRUPT is a
        // new word and it is a measurement request: the broken image correlates with letting go AT
        // SPEED, not with letting go. Nothing in ModBuild 196 measured the release EVENT — only the
        // aggregate number of repairs — so "the gate opened late", "the gate never opened" and "the
        // gate opened on time and the repair found nothing" all printed the same character. These
        // fields are per DRAG, reset when a new drag begins, and are printed at the release edge.

        /// <summary>Frame the current (or last) drag began, i.e. the first motion frame after a
        /// stretch of stillness. -1000 = no drag seen yet.</summary>
        internal int DragStartFrame = -1000;

        /// <summary>Frames in the current/last drag that carried a pose, scale or rect change.</summary>
        internal int DragMotionFrames;

        /// <summary>Largest single-frame world-space host step inside the current/last drag.</summary>
        internal float DragMaxStepWorld;

        /// <summary>The LAST single-frame world step before the hand let go — the "abrupt" number.
        /// A release at speed has a large value here; a release from a standstill has ~0. Read next
        /// to <see cref="DragMaxStepWorld"/>: the two being equal means the drag was still
        /// accelerating when it ended.</summary>
        internal float DragLastStepWorld;

        /// <summary>Frames inside the current/last drag whose unscaled duration exceeded
        /// <see cref="DroppedFrameMs"/> — i.e. frames on which the headset showed the previous image
        /// again while the window was travelling. The judder counter.</summary>
        internal int DragDroppedFrames;

        /// <summary>Worst unscaled frame time inside the current/last drag, ms.</summary>
        internal float DragWorstFrameMs;

        /// <summary>Releases whose settle gate opened in this report window, and the largest number
        /// of frames any of them took from the last motion frame to the gate. A gate that never opens
        /// (the "stillness gate versus an external writer" failure) is a zero here in a window whose
        /// MOTION count is non-zero — those two used to be unrelatable.</summary>
        internal int ReleasesThisWindow;
        internal int ReleaseGateFramesMax;

        /// <summary>Longest unbroken run of motion frames in this report window. If a window's pose
        /// is rewritten every frame by somebody else, this equals the whole window and no release can
        /// ever be detected — which is the shape the memory "a settle gate never opens for state
        /// someone else rewrites each frame" describes.</summary>
        internal int LongestMotionRun;
        internal int CurrentMotionRun;

        // ---- WHAT PATH A DRAGGED FRAME ACTUALLY TAKES (ModBuild 197) -----------------------------
        // The honest question for the moving flicker is whether a dragged window is supersampled AT
        // ALL while it moves, or whether something short-circuits to the raw canvas. Before this
        // build the log carried ONE capture count and one resolve was implied by it; neither was
        // split by what the window was doing, so the question could only be answered by reading the
        // source. These four pairs answer it with counts over a drag.

        /// <summary>Captures taken while the window was inside its motion window / at rest.</summary>
        internal int MotionCaptures;
        internal int StillCaptures;

        /// <summary>Resolve+mip passes that actually ran while moving / at rest. A capture without a
        /// matching resolve means the quad showed the PREVIOUS frame's resolved image.</summary>
        internal int MotionResolves;
        internal int StillResolves;

        /// <summary>Frames on which the quad was showing the RAW multisampled capture target instead
        /// of the resolved, mipped one (the <see cref="MipFallback"/> path). Expected 0.</summary>
        internal int RawQuadFrames;

        /// <summary>Frames on which the panel was visible and the capture camera was NOT enabled, so
        /// no capture was taken at all for a frame the eye then drew the quad on. Expected 0.</summary>
        internal int CameraOffWhileVisible;

        /// <summary>Resolves whose source and destination targets disagreed in size, i.e. the target
        /// was re-allocated between the capture and the resolve. Expected 0; a non-zero value is the
        /// "the capture, not the content, is what breaks" finding.</summary>
        internal int ResolveSizeMismatch;

        // ---- THE SUBMITTED MESH, WHICH ModBuild 196 NEVER LOOKED AT ------------------------------
        // ModBuild 196's scan reads TMP_CharacterInfo — the LAYOUT record written while the text is
        // being laid out. The photograph's fault (full advances, correct quad geometry, no ink) is
        // downstream of that: it lives in the vertex/UV arrays that were actually written into the
        // mesh and uploaded. A glyph whose four UVs collapsed to a point samples ONE atlas texel and
        // puts down a flat SDF value, i.e. NOTHING, while every counter 196 owned still reads clean.
        // That is why 236 readings of a working instrument found nothing.

        /// <summary>Glyph quads whose MESH data was examined — the denominator for the four counters
        /// below, and the number that separates "the mesh instrument never ran" from "it ran".</summary>
        internal int MeshQuadsChecked;

        /// <summary>Visible characters whose layout says they were laid out but whose vertex index is
        /// past the end of the mesh that was actually generated: the quad was never written.</summary>
        internal int MeshMissingQuads;

        /// <summary>Visible characters whose MESH quad (not the layout record) has ~zero area.</summary>
        internal int MeshDegenerateQuads;

        /// <summary>Visible characters whose ATLAS UV rectangle has ~zero area (threshold
        /// <see cref="DegenerateUvArea"/>): the quad samples a single atlas point and draws a flat
        /// value, which is ink-free at any resolution. THE PHOTOGRAPH'S EXACT SHAPE.</summary>
        internal int MeshDegenerateUv;

        /// <summary>Visible characters whose atlas UVs fall outside [0,1] — a stale UV against a
        /// re-packed or replaced atlas. Also ink-free, for a different reason.</summary>
        internal int MeshUvOutOfRange;

        /// <summary>Visible characters with a non-finite vertex or UV.</summary>
        internal int MeshNonFinite;

        /// <summary>Worst single component of the last mesh scan, so the log names a suspect.</summary>
        internal string MeshWorst = string.Empty;
        internal int MeshWorstBad;

        /// <summary>Frame at (or after) which a font-atlas repack repair is due. -1 = none armed.</summary>
        internal int RebuildRepairFrame = -1;

        // ---- MOTION BUDGET: what this class costs during a drag, split from what it costs at rest -
        // ModBuild 193's movement remedy (sweep every frame while moving) is FALSIFIED as a fix and
        // the ModBuild 195 log priced it: 22 of the 23 sweeps that actually found a late transform
        // ran on the PERIODIC cadence and exactly one on the per-frame motion cadence, while the
        // sweep itself averaged 1.48-1.94 ms on a frame whose measured p50 is 17.33 ms against an
        // 11.11 ms budget. These fields make that trade visible per report window instead of once.
        internal int MotionSweeps;
        internal double MotionSweepMs;
        internal int StillSweeps;
        internal double StillSweepMs;

        /// <summary>Frames sampled while this window was inside its motion window, the total and worst
        /// unscaled frame time over them, and how many exceeded <see cref="FrameBudgetMs"/>.</summary>
        internal int MotionFrameSamples;
        internal double MotionFrameMs;
        internal float MotionFrameMsMax;
        internal int MotionFramesOverBudget;

        internal int StillFrameSamples;
        internal double StillFrameMs;
        internal float StillFrameMsMax;
        internal int StillFramesOverBudget;

        // ---- THE OVER-PAINT CENSUS (ModBuild 203) ---------------------------------------------

        /// <summary>
        /// <b>HOW MANY DRAWING GRAPHICS THIS WINDOW'S FULL-FRAME PLATES ARE PAINTED OVER — the one
        /// question twenty-two rounds of instruments have not been able to ask.</b>
        ///
        /// <para><b>THE USER'S REPORT, VERBATIM.</b> <i>"beim Loslassen kann es passieren, dass die
        /// dargestellte Anzeige kaputt ist ... bewege ich es nochmal und lasse los, sieht es wieder
        /// anders aus"</i>, and narrowed since: inside the converted world-space window <i>"New Party
        /// display"</i> exactly TWO of its six sub-views render broken — the CHARACTER SHEET
        /// (<c>Campaign Adventure Party Assembly Variant</c>) and PERKS
        /// (<c>New UIPerksWindow Variant</c>). Glyphs drop out of strings; perks can come up as a
        /// near-empty dark plate. The other four sub-views are correct.</para>
        ///
        /// <para><b>WHY THIS FIELD EXISTS: THE CONTENT-INTEGRITY INSTRUMENT NAMES ITS OWN BLIND SPOT,
        /// IN THE LOG, VERBATIM.</b> <i>"a different fault from a missing glyph, and NOT the same as
        /// being drawn and then painted over, which nothing in this scan can see"</i>. Everything the
        /// last eight rounds measured came back clean and every one of those readings is still
        /// believed: 1 defect in 3919 glyph lookups (and that one a legitimate U+200B), 0 defects in
        /// 3933 submitted glyph quads, 0 TMP sub-meshes off the private capture layer, content scale
        /// LOWEST 1.000 / MEAN 1.000 / HIGHEST 1.000 over 318 readings, sampling BAND-LIMITED with
        /// <i>asked 2.00 / ACHIEVED 2.00</i>, one capture per frame with mips generated every frame at
        /// MSAA 1x. A glyph that is generated perfectly, uploaded perfectly, captured perfectly and
        /// then PAINTED OVER by an opaque rectangle produces exactly those readings and exactly the
        /// user's picture. That is the hypothesis this field tests.</para>
        ///
        /// <para><b>THE ONE FIELD IN THE WHOLE LOG THAT SEPARATES THE BROKEN TWO FROM THE WORKING
        /// FOUR IS THE BACKDROP CENSUS</b> (<c>CanvasConversion.3.Fit.cs</c>, diagnostic only):
        /// character sheet <i>"2 full-frame plate(s) inside it, the largest 'Container' at 1620x1080
        /// px"</i>, perks <i>"2 full-frame plate(s) inside it, the largest 'Blur' at 1620x1080 px"</i>,
        /// ability cards and items <i>"no full-frame plate inside it, so its width is all
        /// content"</i>. That census counts plates and measures how wide the view would be without
        /// them. It never asks the only question that matters here: WHAT IS UNDERNEATH ONE.</para>
        ///
        /// <para><b>THE COUNT IS RESOLVED PAINTER'S ORDER, NOT HIERARCHY ORDER.</b> A graphic is
        /// painted over by a plate when it INTERSECTS the plate's rect and draws BEFORE it, where
        /// "before" is (nearest enabled overrideSorting canvas's <c>sortingOrder</c>, then depth-first
        /// hierarchy index) — the actual key uGUI batches by. That resolution is deliberately computed
        /// even though the sibling-canvas TIE hypothesis it was originally aimed at is now dead (see
        /// <see cref="OverPaintTied"/>: 19 of this window's 20 adopted canvases have
        /// <c>overrideSorting = false</c> and are not sorting roots at all). With the ties gone the
        /// ordering reduces to hierarchy order INSIDE the host batch — which is exactly what uGUI
        /// does — so the count below is a statement about the authored layering itself, and it stays
        /// correct however the adoption's sorting state changes later.</para>
        ///
        /// <para><b>0 ON BOTH BROKEN VIEWS KILLS THE HYPOTHESIS OUTRIGHT</b> — the plates are then
        /// legitimate backdrops drawn first, over-paint is not the cause, and the next round must look
        /// somewhere neither this nor the content scan covers. That outcome is stated on the line
        /// itself so it cannot be read as "the instrument found nothing yet".</para>
        /// </summary>
        internal int OverPaintCovered;

        /// <summary>Census runs that produced a reading, and what they cost. Printed, because an
        /// instrument that never ran and one that ran clean must not print alike.</summary>
        internal int OverPaintScans;
        internal double OverPaintMs;

        /// <summary>Drawing graphics recorded by the last census — THE DENOMINATOR for every count
        /// in this family. A plate covering "137" means nothing without it.</summary>
        internal int OverPaintGraphics;

        /// <summary>The census hit <see cref="MaxOverPaintGraphics"/>: every count from it is a LOWER
        /// BOUND and the line says so.</summary>
        internal bool OverPaintTruncated;

        /// <summary>Full-frame plates found by the last census, and how many of them are effectively
        /// OPAQUE (own colour alpha x CanvasRenderer alpha x inherited alpha at or above 0.99). A
        /// translucent plate tints what is under it; an opaque one deletes it.</summary>
        internal int OverPaintPlates;
        internal int OverPaintOpaquePlates;

        /// <summary>The worst single plate's covered count, and the total intersecting area over
        /// every plate (host-local uGUI px²) — an extreme and a magnitude, never one alone.</summary>
        internal int OverPaintWorstCovered;
        internal float OverPaintCoveredArea;

        /// <summary>
        /// Of the graphics a plate is painted over, how many sit at the SAME resolved
        /// <c>sortingOrder</c> as the plate but under a DIFFERENT canvas — i.e. an UNSTABLE TIE whose
        /// real GPU order no census can predict, because Unity resolves equal-order sorting roots by
        /// canvas registration and not by hierarchy.
        ///
        /// <para><b>THIS IS EXPECTED TO READ 0, AND IT IS PRINTED ANYWAY.</b> The sibling-canvas
        /// draw-order hypothesis is DEAD: a parallel lane counted it out of the ModBuild 202 log —
        /// of this window's 20 adopted nested canvases, 19 carry <c>overrideSorting = false</c> (the
        /// adoption lines read <c>overrideSorting False→false</c>), so they are not sorting roots at
        /// all and sort by hierarchy inside the host batch exactly as the flat game drew them; the
        /// single exception is <c>UI Party Inventory Item Tooltip</c>, and one canvas cannot tie with
        /// itself. So this field is not evidence FOR anything — it is the standing check that the
        /// premise stays dead, and it is what makes the ordering behind
        /// <see cref="OverPaintCovered"/> auditable rather than asserted. A non-zero reading would
        /// mean the adoption's sorting state changed under us.</para>
        /// </summary>
        internal int OverPaintTied;

        /// <summary>The per-plate sentences: name, path, rect, area fraction, colour, alphas,
        /// material/shader/renderQueue and grab-texture probes, owning canvas and its sorting state,
        /// resolved order key, and the covered count with its three largest victims.</summary>
        internal string OverPaintNote = string.Empty;

        /// <summary>THE WHOLE DISTRIBUTION of <see cref="OverPaintCovered"/> since engage, because a
        /// single reading is not an operating point (the ModBuild 201 <c>RESAMPLE VERDICT WORST</c>
        /// mistake, which cost a build).</summary>
        internal int OverPaintReadings;
        internal long OverPaintCoveredSum;
        internal int OverPaintCoveredLowest;
        internal int OverPaintCoveredHighest;

        /// <summary>
        /// WHICH SUB-VIEW THIS READING BELONGS TO — the attribution eight rounds of clean
        /// measurements did not carry, which is why they were unreadable. Two of the party window's
        /// six sub-views are broken and four are not, and every field on the state line so far
        /// averaged over whichever happened to be open. Derived LOCALLY (see
        /// <c>ResolveOpenSubView</c>) from the game's own <c>NewPartyDisplayUI</c> singleton, NOT from
        /// <c>CanvasConversion</c>'s <c>fx.OpenSignature</c> — that state is private to another
        /// lane's file and no read-only accessor for it exists.
        /// </summary>
        internal string OpenViewName = string.Empty;
        internal string OpenViewSource = string.Empty;

        /// <summary>The game's OWN answer to the same question — <c>NewPartyDisplayUI.ActiveDisplay</c>
        /// as a string. Printed next to <see cref="OpenViewName"/> rather than instead of it: the two
        /// are derived independently and a disagreement between them is itself information.</summary>
        internal string OpenViewActive = string.Empty;
        internal int OpenViewCount;

        /// <summary>The open sub-view's rect in HOST-LOCAL uGUI px — the rect the plate test is
        /// judged against. Falls back to the host rect when no sub-view can be derived, and the
        /// source string says which was used.</summary>
        internal Rect OpenViewRect;

        /// <summary>
        /// FOREIGN RENDER SUBTREES, NAMED. <see cref="ForeignSkipped"/> has counted them since
        /// ModBuild 193 (the party window reports 4) and the engage line names exactly the FIRST one
        /// (<c>FX_Smoke</c>). These are the real <see cref="Renderer"/>s and <see cref="Camera"/>s the
        /// capture deliberately excludes and lets the head camera draw at their true world pose — so
        /// the display quad, whose host-local z is 0, and a foreign renderer at host-local z ~ 0 are
        /// COPLANAR, and a depth tie can resolve differently in the two MultiPass eyes. That is a
        /// judgeable hypothesis only if the z is in the log, so it is.
        /// </summary>
        internal int ForeignRenderers;
        internal int ForeignCoplanar;
        internal string ForeignNote = string.Empty;

        /// <summary>Transforms the last census visited — the denominator for
        /// <see cref="ForeignRenderers"/>.</summary>
        internal int OverPaintVisited;

        // ---- THE SUB-VIEW SWEEP BURST (ModBuild 203) ------------------------------------------

        /// <summary>
        /// <b>HOW OFTEN THE OPEN SUB-VIEW CHANGED, AND THEREFORE HOW OFTEN THIS WINDOW REPOPULATED
        /// WITH NOTHING FORCING A CAPTURE-LAYER SWEEP.</b>
        ///
        /// <para><b>THE USER'S NEWEST REPORT, VERBATIM:</b> <i>"mittlerweile taucht es auch initial
        /// kaputt auf wenn man das Fenster öffnet"</i> — broken with NO DRAG AT ALL, which is the one
        /// case no previous remedy touched. Every remedy so far has been keyed on MOTION.</para>
        ///
        /// <para><b>THE ROOT CAUSE UNDER TEST.</b> <c>ApplyCaptureLayer</c> runs on a cadence —
        /// <see cref="SweepIntervalFrames"/> = 15 frames when still,
        /// <see cref="MovingSweepIntervalFrames"/> = 5 while moving. A SUB-VIEW SWITCH is deliberately
        /// not a geometry change (the fit advances no generation and never touches the host rect), so
        /// pressing a tab forces nothing at all. Until the next cadence tick, every transform the game
        /// creates for the new view sits on the GAME's UI layer: MISSING from the capture and drawn
        /// straight into the eye at its own sorting order.</para>
        ///
        /// <para><b>THE EVIDENCE, from the ModBuild 202 log — the complete set of five late-joiner
        /// lines for this window, whose counts sum exactly to the reported 2853 late joiners, so
        /// nothing is missing:</b> L4108 t=24.25 s, 2448 transform(s), 0.25 s after the ABILITY CARDS
        /// view opened; L4546 t=44.82 s, 27, ITEMS; L4563 t=45.68 s, 56, ITEMS; L4643 t=49.71 s, 28,
        /// ITEMS; L4685 t=51.83 s, 294, 0.25 s after PERKS opened. <b>All 13 such lines in the session
        /// read "periodic"; not one reads the motion cadence.</b> So the arrivals are keyed to CONTENT
        /// CHANGES, not to motion, and the cadence is what decides how long the hole stays open.</para>
        ///
        /// <para><b>REJECTED ALTERNATIVES, both already falsified and neither to be rebuilt.</b>
        /// (1) SWEEP EVERY FRAME WHILE MOVING — ModBuild 193 shipped it, it demonstrably ran (3501
        /// sweeps, 2448 late joiners logged) and the user reported the flicker identical. (2) SWEEP
        /// EVERY FRAME, FULL STOP — ModBuild 196 priced it at 1.48-1.94 ms of a 17 ms frame and
        /// measured that 22 of 23 real arrivals were caught by the ORDINARY cadence and exactly one by
        /// the motion cadence, which is why the motion cadence went from 1 frame to 5. What has never
        /// been tried is forcing the sweep at the moment the CONTENT changes, which is this.</para>
        /// </summary>
        internal int SubViewChanges;

        /// <summary>Changes that arrived inside <see cref="SweepBurstCooldownFrames"/> of the last
        /// armed burst and were folded into it instead of arming a second one. Counted and printed:
        /// <see cref="SubViewChanges"/> minus this is the number of bursts the fuse allowed, and a
        /// large gap between the two is a window whose signature flaps — which is a finding, not a
        /// reason to stay quiet.</summary>
        internal int SubViewChangesCoalesced;

        /// <summary>Signature of the set of ACTIVE sub-view roots (and of the conversion target's
        /// active direct children) at the last check, and whether one has ever been taken. A change
        /// of this — and nothing else — arms a burst.</summary>
        internal int SubViewSignature;
        internal bool SubViewSigValid;

        /// <summary>The frame the current (or last) burst was armed on, and its live state: frames
        /// left, frames run, consecutive empty sweeps, transforms moved, and the last frame on which
        /// a sweep of this burst still found a late joiner.</summary>
        internal int SubViewChangeFrame = -1000;
        internal int SweepBurstFramesLeft;
        internal int SweepBurstFramesRun;
        internal int SweepBurstMisses;
        internal int SweepBurstMoved;
        internal int SweepBurstLastHitFrame = -1;

        /// <summary>Bursts run since engage, and how many CONVERGED IN ONE FRAME (the first sweep
        /// found nothing and no later one did either) — i.e. how often there was no hole to close.</summary>
        internal int SweepBursts;
        internal int SweepBurstsConverged;

        /// <summary>
        /// <b>THE NUMBER NOBODY HAS EVER MEASURED: how many frames elapsed between the sub-view
        /// changing and the LAST sweep that still found a late joiner.</b> That is the length of the
        /// window in which this view's content was missing from the capture and drawn straight into
        /// the eye. -1 = no burst has completed yet; 0 = the burst never found anything, i.e. there
        /// was no hole at all. Kept as last / worst / mean over a count, never as a bare extreme.
        /// </summary>
        internal int SweepBurstHoleFramesLast = -1;
        internal int SweepBurstHoleFramesMax = -1;
        internal long SweepBurstHoleSum;
        internal int SweepBurstHoleReadings;

        /// <summary>The last completed burst's totals, and what every burst so far has cost.</summary>
        internal int SweepBurstFramesLast;
        internal int SweepBurstMovedLast;
        internal double SweepBurstMs;

        // ---- THE TMP SUB-MESH CULL LATCH (ModBuild 204) ----------------------------------------

        /// <summary>
        /// <b>SUB-MESHES WHOSE CanvasRenderer IS CULLED WHILE THEIR PARENT TextMeshProUGUI'S IS NOT —
        /// THE LATCH, AND THE WHOLE OF "MANCHE ELEMENTE SIND NICHT SICHTBAR".</b>
        ///
        /// <para>The mechanism, the two decompiled quotations that prove it and the repair are on
        /// <see cref="RepairSubMeshCull"/>. In one sentence: TMP writes a sub-mesh's cull flag ONLY
        /// from inside <c>TextMeshProUGUI.Cull</c>'s <c>if (m_canvasRenderer.cull != flag)</c> guard,
        /// and uGUI's own <c>MaskableGraphic.UpdateClipParent</c> can clear the PARENT's flag through
        /// a private non-virtual <c>UpdateCull</c> that does not run that loop — after which the
        /// guard is permanently false and the children can never be repaired by TMP or by uGUI.</para>
        ///
        /// <para><b>A NON-ZERO VALUE HERE, WITH THE PARENT READING cull=FALSE, IS THE FINDING and
        /// needs no further measurement.</b> A zero across a session in which the user still reports
        /// missing elements retires the hypothesis outright — which is why the count is always printed
        /// with <see cref="SubMeshesInUse"/> as its denominator.</para>
        /// </summary>
        internal int SubMeshCullLatched;

        /// <summary>The first latched sub-mesh of the last scan, named with its parent — the sentence
        /// that makes the count auditable instead of asserted.</summary>
        internal string SubMeshCullLatchedNote = string.Empty;
        internal int SubMeshCullLatchedNamed;

        /// <summary>THE THREE DISJOINT REASONS a TMP sub-mesh puts no pixels into the capture, which
        /// ModBuild 203 and earlier collapsed into one <c>||</c>. Tested in this order and
        /// <c>continue</c>d, so every sub-mesh lands in exactly one bucket: the CULL FLAG (the latch),
        /// its OWN alpha, its INHERITED alpha. Without the split, "16 culled/transparent" is noise.</summary>
        internal int SubMeshesCullFlag;
        internal int SubMeshesOwnAlpha;
        internal int SubMeshesInheritedAlpha;

        /// <summary>The same three-way split for whole TEXT components
        /// (<see cref="NoteRendererState"/>), which had the identical <c>||</c>. The denominator is
        /// <see cref="TextComponents"/>.</summary>
        internal int TextCulledFlag;
        internal int TextCulledOwnAlpha;
        internal int TextCulledInheritedAlpha;
        internal string TextCulledNote = string.Empty;

        internal int CullPairCollections;

        /// <summary>The last correction, named: which sub-mesh, under which parent, in which
        /// direction, on which frame.</summary>
        internal string CullRepairNote = string.Empty;

        // ---- THE FOUR READINGS (ModBuild 204) --------------------------------------------------
        // baseline (before the grab) / during the drag / at the release edge / settled +30 frames.
        // A frozen state is exactly "at release == settled and both above baseline", and no previous
        // instrument could express that sentence at all.

        internal int CullBaseline = -1;
        internal int CullDragMin = -1;
        internal int CullDragMax = -1;
        internal int CullAtRelease = -1;
        internal int CullSettled = -1;

        /// <summary>Frame at which the SETTLED reading is due (a forced pair collection plus one
        /// invariant pass). -1 = none armed.</summary>
        internal int CullSettleFrame = -1;

        // ---- THE CAPTURE-FRAME HYSTERESIS (ModBuild 204) ---------------------------------------

        /// <summary>THE COMMITTED OVERSPILL PER EDGE (host-local uGUI px, always a whole number of
        /// <see cref="FrameQuantumPx"/>). The capture frame is rebuilt from the LIVE host rect plus
        /// these four on every measurement, so a host-rect change is followed immediately and exactly
        /// while the hysteresis only ever governs the OVERSPILL — which is the only part of the frame
        /// the content union decides, and therefore the only part that can flap.</summary>
        internal float HoldLeft;
        internal float HoldRight;
        internal float HoldDown;
        internal float HoldUp;

        /// <summary>The current SHRINK RUN: how many consecutive measurements have asked for a smaller
        /// frame (past the dead band), and the LARGEST need seen on each edge across that run. The run
        /// max — never the last reading — is what a committed shrink adopts, so a shrink can never
        /// crop something that was needed during the run.</summary>
        internal int ShrinkRun;
        internal float RunLeft;
        internal float RunRight;
        internal float RunDown;
        internal float RunUp;

        /// <summary>Measurements TAKEN, and measurements SKIPPED because the window was only being
        /// translated. The second number is the whole of ModBuild 204's part 1 and it is printed over
        /// the first: 45 of the 58 ModBuild 203 re-allocations happened inside a grab.</summary>
        internal int FrameMeasuresSkipped;

        /// <summary>Capture-frame GROWS, SHRINKS, and shrinks REFUSED by the hysteresis (dead band,
        /// run not yet long enough, or the window still moving). <b>Grows + shrinks near zero over a
        /// session with real drags is the fix working.</b></summary>
        internal int FrameGrows;
        internal int FrameShrinks;
        internal int FrameShrinksRefused;

        /// <summary>Single-step growths larger than <see cref="FrameOutlierGrowQuanta"/> quanta on one
        /// edge, whether one is pending now, and how many were released again by the shortened run.
        /// See <see cref="FrameOutlierGrowQuanta"/> for the whole release rule.</summary>
        internal int FrameOutlierGrowths;
        internal bool FrameOutlierPending;
        internal int FrameOutlierReleases;

        /// <summary>THE WHOLE DISTRIBUTION OF THE MEASURED NEED, per axis, since engage — because a
        /// flap is a DISTRIBUTION and the last reading of one is indistinguishable from a steady
        /// value. The ModBuild 203 flap reads as 1464 lowest / ~1480 mean / 1496 highest over 58
        /// readings; a fixed frame reads as three identical numbers.</summary>
        internal int NeedReadings;
        internal double NeedWSum;
        internal double NeedHSum;
        internal float NeedWLowest;
        internal float NeedWHighest;
        internal float NeedHLowest;
        internal float NeedHHighest;

        /// <summary>The host's lossy scale at the last COMPLETED measurement, so the translation gate
        /// can tell a pure translation from a rescale without depending on
        /// <see cref="NoticeGeometry"/>'s verdict (which lives in another lane's file and is not
        /// passed to <see cref="MeasureFrame"/>).</summary>
        internal Vector3 ScaleAtMeasure = Vector3.one;
        internal bool ScaleAtMeasureValid;

        /// <summary>Set by <see cref="NoticeSubViewChange"/>: the next measurement runs whatever the
        /// hand is doing. A sub-view switch is the one event that changes what the window draws
        /// without touching its rect, its scale or its pose, so it is the one event the translation
        /// gate must not be allowed to swallow. Cleared by the measurement that consumes it.</summary>
        internal bool ForceMeasure;

        /// <summary>Committed capture-frame CHANGES, and the sentence describing the last one — the
        /// previous and new extremes on both axes, each with the graphic that set it.</summary>
        internal int FrameChanges;
        internal string FrameChangeNote = "none since engage";

        /// <summary>THE FOUR EDGES OF THE MEASURED UNION, each with the graphic that set it: live, and
        /// as they stood at the previous committed frame change. See <see cref="ExtremeRecord"/>.</summary>
        internal readonly ExtremeRecord[] Extremes = new ExtremeRecord[4];
        internal readonly ExtremeRecord[] ExtremesPrev = new ExtremeRecord[4];

        /// <summary>The per-axis RATE CEILING <see cref="MaxRtDimension"/> leaves for the committed
        /// frame, quantised to <see cref="RateQuantum"/> exactly as <see cref="ResolveRate"/> does it.
        /// Recorded BEFORE and AFTER every committed change so a growth that silently steps the
        /// achievable rate down below <see cref="BandLimitedTexelsPerPixel"/> is named on the line
        /// instead of only showing up as a worse picture.</summary>
        internal float FrameRateCeiling;
        internal bool RateCeilingWarned;

        // ---- THE GROWTH CLAMP (ModBuild 205) ---------------------------------------------------

        /// <summary>uGUI px of MEASURED overspill the band-limit clamp refused to frame on each edge at
        /// the last measurement — see <see cref="BandLimitFrameBudgetPx"/>. All four are 0 whenever the
        /// whole ask fits under the budget, which is every window except a host rect within one
        /// <see cref="FrameQuantumPx"/> of 2048 px on an axis. <b>Non-zero means content the player
        /// could see is cropped</b>, deliberately, to keep the whole window band-limited; it is never
        /// silent (a Warn on the first clamp, an Info on every later change of it, and the live values
        /// on the CAPTURE FRAME line).</summary>
        internal float CropLeft;
        internal float CropRight;
        internal float CropDown;
        internal float CropUp;

        /// <summary>The crop that was last LOGGED, so the trade is recorded once per clamp STATE and
        /// not once per measurement, and the release back to an unclamped frame is recorded too.
        /// Initialised to the unclamped state, so a window that never clamps never logs.</summary>
        internal float LoggedCropLeft;
        internal float LoggedCropRight;
        internal float LoggedCropDown;
        internal float LoggedCropUp;

        /// <summary>Clamp STATE CHANGES logged on this window, and how many shrinks bypassed the
        /// hysteresis because the held frame was costing the band limit or costing cropped content
        /// (see <c>ShrinkRecoversBandLimit</c>). A rising bypass count is the one thing that could
        /// re-open a flap, so it is counted rather than assumed absent.</summary>
        internal int BandClamps;
        internal int BandLimitShrinks;

        // ---- THE REGENERATION RESUME CURSOR (ModBuild 204) -------------------------------------

        /// <summary>
        /// <b>WHERE THE NEXT <c>repairAll</c> PASS STARTS.</b> The release repair's regeneration is
        /// capped at <see cref="MaxRegeneratePerScan"/> = 256 components and this window carries up to
        /// 297, so before ModBuild 204 the cap truncated a DETERMINISTIC depth-first pre-order walk —
        /// i.e. it skipped THE SAME components at every release, in every session, forever, while the
        /// release line advertised the pass as unconditional. See <see cref="ScanTmpText"/>.
        /// <para>0 = the next pass starts at the first text component, i.e. a full sweep has just
        /// completed.</para>
        /// </summary>
        internal int RegenCursor;

        /// <summary>The walk index of the last component this pass regenerated, and whether the pass
        /// stopped because the cap bit (as opposed to running out of components).</summary>
        internal int RegenLastIndex;
        internal bool RegenCapBit;

        /// <summary>Components a <c>repairAll</c> pass DEFERRED to the next pass because the cap bit,
        /// and how many it skipped because they lie BEFORE the cursor and were covered by an earlier
        /// pass of the same sweep. Printed with the cursor so a partial pass can never be reported in
        /// language implying completeness.</summary>
        internal int RegenDeferred;
        internal int RegenSkippedBeforeCursor;

        /// <summary>Complete round-robin sweeps of the subtree finished since engage, and how many
        /// passes the last one took.</summary>
        internal int RegenFullSweeps;
        internal int RegenPassesThisSweep;
        internal int RegenPassesLastSweep;
    }

    // ---- public seams -------------------------------------------------------------------------

    /// <summary>
    /// Does this class currently own <paramref name="panel"/>'s subtree layers? Read by
    /// <c>CanvasConversion</c>'s two mod-layer re-assert call sites so exactly one writer touches a
    /// given panel's layers at a time (see the class doc, "LAYER OWNERSHIP IS TAKEN WHOLE").
    /// </summary>
    internal static bool OwnsPanelLayers(ConvertedPanel? panel)
    {
        if (panel == null || Entries.Count == 0)
            return false;
        for (int i = 0; i < Entries.Count; i++)
        {
            if (ReferenceEquals(Entries[i].Panel, panel))
                return true;
        }
        return false;
    }

    /// <summary>
    /// A panel is about to be released (or was pruned dead). Stand its supersample down FIRST, while
    /// its transforms still carry our layer, so <c>CanvasConversion.Release</c>'s own layer restore
    /// then puts the game's window back on the game's layer. Idempotent and never throws.
    /// </summary>
    internal static void NoticeRelease(ConvertedPanel? panel)
    {
        if (panel == null || Entries.Count == 0)
            return;
        for (int i = Entries.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(Entries[i].Panel, panel))
                StandDown(Entries[i], i, "the window closed");
        }
    }

    /// <summary>Full teardown (module shutdown / VR off / hot reload): every panel back to direct
    /// rendering, every camera mask handed back, every allocation released.</summary>
    internal static void Shutdown()
    {
        try
        {
            for (int i = Entries.Count - 1; i >= 0; i--)
                StandDown(Entries[i], i, "module shutdown");
            RestoreMaskedCameras();
            UninstallHooks();
            Refused.Clear();
        }
        catch (System.Exception ex)
        {
            VRLog.Warn(Scope, $"PANEL SUPERSAMPLE shutdown failed ({ex.GetType().Name}: {ex.Message}) "
                              + "— a floated window may keep a mod capture layer or a camera may keep "
                              + "a narrowed culling mask until the next scene load.");
        }
    }

    // ---- per-frame service --------------------------------------------------------------------

    /// <summary>
    /// Update phase: decide which panels are supersampled, engage new ones, stand ineligible ones
    /// down. Called from <c>CanvasConversion.Tick</c>. Guarded — an unguarded exception in an Update
    /// starves VR input.
    /// </summary>
    internal static void Tick()
    {
        try
        {
            bool wanted = WorldUIConfig.PanelSupersample != null && WorldUIConfig.PanelSupersample.Value;
            if (!wanted)
            {
                if (Entries.Count > 0)
                    StandDownAll("[WorldUI] PanelSupersample was switched off");
                return;
            }
            if (PoolSize <= 0)
            {
                if (!_noLayerLogged)
                {
                    _noLayerLogged = true;
                    VRLog.Warn(Scope, "PANEL SUPERSAMPLE stands down: the capture-layer POOL is empty "
                                      + "(every layer 8-31 is named, and layer "
                                      + $"{VRLayers.ModLayer} is already the mod layer), and a panel "
                                      + "without a PRIVATE layer would have to share one with its "
                                      + "neighbours — which is the ModBuild 193 defect in which one "
                                      + "window's capture contained another window's content. THE "
                                      + "CONSEQUENCE: every floated window keeps being rasterized "
                                      + "directly into the eye at ~1.86 authored pixels per rendered "
                                      + "pixel, i.e. exactly today's behaviour including the reported "
                                      + "text and edge shimmer. Nothing else changes. The one-time "
                                      + "POOL census line above lists exactly which layers are named "
                                      + "and by whom.");
                }
                return;
            }

            PruneDeparted();
            Consider();
        }
        catch (System.Exception ex)
        {
            Fail(ex, "the eligibility pass");
        }
    }

    /// <summary>
    /// LateUpdate phase: keep every live entry's geometry, visibility and allocation in step with its
    /// panel. The CAPTURE itself is not driven from here — the capture camera is an ordinary enabled
    /// camera at <c>depth = -200</c>, so Unity renders it in its own camera loop AFTER every
    /// LateUpdate and BEFORE the head camera's two eye passes. Both eyes therefore read one finished,
    /// identical RenderTexture, which is the invariant <see cref="CameraOrderProbe"/> measured and
    /// this path must not break.
    /// </summary>
    internal static void LateTick()
    {
        if (Entries.Count == 0)
            return;
        try
        {
            // Safety net: onPostRender always runs, but if a camera was destroyed mid-render its
            // saved mask would leak. At LateUpdate no camera is rendering, so the table must be
            // empty; anything left is handed back here.
            RestoreMaskedCameras();

            bool report = Time.unscaledTime >= _nextReport;
            if (report)
                _nextReport = Time.unscaledTime + ReportIntervalSeconds;

            for (int i = Entries.Count - 1; i >= 0; i--)
            {
                Entry e = Entries[i];
                if (!Eligible(e.Panel))
                {
                    StandDown(e, i, "the window is no longer eligible (closed, hidden or re-fitted "
                                    + "below the minimum size)");
                    continue;
                }
                // BEFORE SyncGeometry, because arming a burst pulls NextContentFrame to NOW and it is
                // SyncGeometry that reads it — so a tab press re-measures the capture frame on the
                // very frame it happened, not on the next 15-frame cadence tick. See
                // Entry.SubViewChanges for the whole argument and for the ModBuild 202 evidence.
                NoticeSubViewChange(e);
                SyncGeometry(e);
                SyncVisibility(e);

                bool moving = IsMoving(e);
                SampleFrameBudget(e, moving);

                // THE SUB-MESH CULL INVARIANT — ModBuild 204's headline, and it runs HERE, every
                // frame, on every entry, gated on NOTHING. This project has now shipped four remedies
                // that never executed because they were gated on the very diagnostic that was supposed
                // to decide whether they were needed (ModBuild 196's text regeneration, ModBuild 198's
                // band-limit floor, and twice besides). This one is a compare over a cached pair list
                // — see RepairSubMeshCull for the two decompiled quotations that make it a repair of a
                // TMP invariant rather than a policy of ours.

                // THE SUB-VIEW BURST, ahead of the ordinary cadence so a burst frame is never
                // followed by a redundant periodic sweep on the same frame (it re-arms NextSweepFrame
                // itself). Costs nothing on every frame in which no sub-view changed.
                ServiceSweepBurst(e);

                // THE MOVEMENT CADENCE. While the window is moving (and for SweepAfterMotionFrames
                // after), sweep on the faster MovingSweepIntervalFrames cadence: that is the interval
                // in which the panel's own per-frame nested-canvas adoption is throttled OFF by
                // GrabbableModal.ThrottleDiagWhileMoving, so a child born mid-drag would otherwise
                // stay on the game's UI layer — missing from the capture and double-drawn into the
                // eye. It is a CADENCE and no longer every frame, because the ModBuild 195 log priced
                // per-frame sweeping at 1.5 ms of a 17 ms frame and measured that it caught exactly
                // ONE of the session's 23 real arrivals; see MovingSweepIntervalFrames.
                if (Time.frameCount >= e.NextSweepFrame)
                {
                    e.NextSweepFrame = Time.frameCount
                                       + (moving ? MovingSweepIntervalFrames : SweepIntervalFrames);
                    ApplyCaptureLayer(e, initial: false);
                }

                // THE RELEASE REPAIR and the FONT-REPACK REPAIR — both unconditional, both bounded,
                // and both aimed squarely at "die dargestellte Anzeige ist kaputt". See ReleaseRepair.
                ServiceRepairs(e);

                if (report)
                {
                    // Scan on the report cadence as well as on every release, so the CONTENT
                    // INTEGRITY field is never a stale reading from the last time the window was
                    // touched. The window in the user's photograph had been STANDING STILL when the
                    // pause menu opened next to it, so "it only moved ten minutes ago" must not mean
                    // "measured ten minutes ago". Once per 10 s per panel, ~1 ms.
                    MeasureContent(e);
                    Report(e);
                    // TWO FURTHER LINES, EMITTED FROM PanelSupersample.4.Content.cs, because that is
                    // the file that owns both measurements (ModBuild 204). They are separate lines
                    // rather than more fields on Report's line for a practical reason: each one has to
                    // carry its own HOW TO READ IT paragraph, and the state line is already at the
                    // limit of what a reader can hold.
                    ReportCaptureFrame(e);
                    // ModBuild 243's falsifier. A separate line for the same reason as the two
                    // above: it carries its own verdict and its own ASKED -> GOT table, and the
                    // state line is already at the limit of what a reader can hold.
                    ReportEyeGrid(e);
                }
            }
        }
        catch (System.Exception ex)
        {
            Fail(ex, "the per-frame sync");
        }
    }

    // ---- eligibility + engage -----------------------------------------------------------------

    /// <summary>
    /// Which panels may take this path. Deliberately narrow, and every clause is load-bearing:
    /// <list type="bullet">
    /// <item><see cref="ConvertedPanel.ModLayerEnabled"/> — the FLOATED-WINDOW family, and the only
    /// family whose subtree layers are already mod-owned. Docked HUD hosts (initiative track, actor
    /// bars, tooltips) sit on the game's UI layer, are drawn by the game's own UI Camera as well, and
    /// are not what the user reports.</item>
    /// <item>THE FLATTEN CLAUSE IS GONE (ModBuild 193), and the reason it was here was FALSE. It
    /// read: <i>"NOT ConvertedPanel.FlattenEnabled — CanvasConversion.FlattenSubtree re-asserts local
    /// z = 0 on every transform in the subtree each LateUpdate, which would drag the capture camera
    /// (a child of the host, standing off along local -z) onto the panel plane and capture
    /// nothing."</i> That cannot happen, for three independent reasons, checked against the merged
    /// flatten lane rather than assumed:
    /// <list type="number">
    /// <item>THE WALK CANNOT REACH THE CAMERA. <c>CanvasConversion.FlattenSubtree</c> is
    /// <c>RunFlattenPass</c>, which seeds its walk at <c>panel.Target</c>
    /// (<c>CanvasConversion.2.Adopt.cs</c>, <c>panel.FlattenWalk.Add(panel.Target)</c>). The capture
    /// camera is parented to <c>panel.HostRect</c> (<see cref="BuildCamera"/>), and <c>Target</c> is
    /// ALSO a child of <c>HostRect</c> (<c>CanvasConversion.1.Core.cs</c>, <c>target.SetParent(
    /// hostRect, ...)</c>). The camera is <c>Target</c>'s SIBLING, not its descendant, so no flatten
    /// sweep of either family — the old <c>flatten2D</c> one or the new window-flatness guarantee —
    /// visits it.</item>
    /// <item>AND IT WOULD SKIP IT ANYWAY. Both flatten walks leave a subtree carrying a
    /// <see cref="Renderer"/> or a <see cref="Camera"/> alone ("foreign render subtree(s) LEFT
    /// ALONE" in their own census line), for the same reason this class's layer sweep does.</item>
    /// <item>AND THIS CLASS OWNS THE NUMBER REGARDLESS. <see cref="SyncProjection"/> rewrites the
    /// camera's local position, rotation and scale from the capture camera's OWN
    /// <see cref="Camera.onPreCull"/> — after every LateUpdate in the frame, whoever ran last, and
    /// inside the callback of the only camera that reads them. That is this project's "don't win a
    /// write war: own the number" rule applied where it is guaranteed to win, so even a future
    /// writer that DID reach the camera could not move what it renders.</item>
    /// </list>
    /// The clause was also DEAD: <c>useModLayer:true</c> is passed only by <c>ModalFallback</c> (the
    /// floated-window family) and <c>flatten2D:true</c> only by the docked-card surfaces
    /// (StatPanel, PropInfo, EnemyReveal, WorldSurface), so no panel in this codebase has ever had
    /// both — the <c>ModLayerEnabled</c> clause above already excluded every panel this one could
    /// have refused. Removing it therefore changes nothing today AND unblocks routing the floated
    /// window family through the ordinary <c>flatten2D</c> flag, which is the only reason the
    /// flatten lane needed a separate <c>ConvertedPanel.FlattenWindowGuarantee</c> flag and its own
    /// per-host driver. (That flag was never refused here in any case: this clause only ever read
    /// <c>FlattenEnabled</c>.)</item>
    /// <item>visible — a panel behind the reveal gate has its canvases disabled, so a capture would
    /// bake an empty frame and the display would show a blank window at the pop-in.</item>
    /// </list>
    /// </summary>
    private static bool Eligible(ConvertedPanel? panel)
    {
        if (panel == null || !panel.IsAlive || panel.HostGo == null || panel.HostCanvas == null
            || panel.HostRect == null)
            return false;
        if (!panel.ModLayerEnabled)
            return false;
        if (panel.RevealPending || panel.RenderHidden || panel.OwnerRenderHidden)
            return false;
        Rect r = panel.HostRect.rect;
        return r.width >= MinAuthoredPx && r.height >= MinAuthoredPx;
    }

    private static void PruneDeparted()
    {
        IReadOnlyList<ConvertedPanel> live = CanvasConversion.ActivePanels;
        if (live.Count == 0 && Refused.Count > 0)
            Refused.Clear(); // no conversion alive: no refusal can still refer to one
        for (int i = Entries.Count - 1; i >= 0; i--)
        {
            Entry e = Entries[i];
            bool stillLive = false;
            for (int p = 0; p < live.Count; p++)
            {
                if (ReferenceEquals(live[p], e.Panel))
                {
                    stillLive = true;
                    break;
                }
            }
            if (!stillLive)
                StandDown(e, i, "the panel left the conversion registry");
        }
    }

    private static void Consider()
    {
        int cap = EffectiveMaxPanels;
        if (Entries.Count >= cap)
            return;
        IReadOnlyList<ConvertedPanel> live = CanvasConversion.ActivePanels;
        for (int i = 0; i < live.Count && Entries.Count < cap; i++)
        {
            ConvertedPanel panel = live[i];
            if (!Eligible(panel) || OwnsPanelLayers(panel)
                || Refused.Contains(panel.HostGo.GetInstanceID()))
                continue;
            Engage(panel);
        }
        if (Entries.Count >= cap && !_capLogged)
        {
            int candidates = 0;
            for (int i = 0; i < live.Count; i++)
            {
                if (Eligible(live[i]) && !OwnsPanelLayers(live[i]))
                    candidates++;
            }
            if (candidates > 0)
            {
                _capLogged = true;
                long average = Entries.Count > 0 ? _vramTotal / Entries.Count : 0;
                string capSource = PoolSize < MaxPanels
                    ? $"the LAYER POOL ({PoolSize} private capture layer(s) exist; MaxPanels is "
                      + $"{MaxPanels}). A ninth window cannot be given a tenth layer, and it must "
                      + "never SHARE one — sharing is the ModBuild 193 defect in which one window's "
                      + "capture contained its neighbour's content. The only honest lever here is to "
                      + "free a named layer, not to raise MaxPanels."
                    : $"MaxPanels ({MaxPanels}); the layer pool holds {PoolSize} and is not the bound.";
                VRLog.Warn(Scope, $"PANEL SUPERSAMPLE cap reached: {cap} panel(s) are "
                                  + $"supersampled and {candidates} further eligible window(s) are "
                                  + "NOT. THE CAP THAT BOUND IS " + capSource
                                  + " THE CONSEQUENCE for those windows: they keep today's direct "
                                  + "rendering — the same text and edge shimmer as before this build. "
                                  + "HOW TO SIZE THE NEXT VALUE from this line rather than from an "
                                  + $"estimate: the {Entries.Count} panel(s) actually engaged cost "
                                  + $"{Mb(_vramTotal)} MB together, i.e. {Mb(average)} MB each on "
                                  + $"average, against a session ceiling of {Mb(MaxTotalVramBytes)} MB "
                                  + $"and a per-panel ceiling of {Mb(MaxPanelVramBytes)} MB — so "
                                  + $"{(average > 0 ? (MaxTotalVramBytes / average).ToString() : "many")} "
                                  + "panel(s) of THIS size fit in the session budget. If that number "
                                  + "is comfortably above the cap above, the VRAM ceiling is NOT the "
                                  + "bound and the line named the one that is; if it is below, the "
                                  + "VRAM ceiling is the real bound and neither MaxPanels nor the "
                                  + "layer pool is what refused these windows.");
            }
        }
    }

    /// <summary>
    /// The render-target texels per AUTHORED window pixel this panel will be allocated at, and where
    /// that number came from.
    ///
    /// <para>The whole argument is in <see cref="BandLimitFactor"/>. The rule here is narrow: a
    /// <c>[WorldUI] PanelSupersampleFactor</c> the user has actually SET is taken verbatim, because a
    /// tuned value is a ruling and this project does not re-express one; a factor still sitting on
    /// its shipped default is a policy nobody chose, and the ModBuild 197 log proves that policy was
    /// allocating one texel per authored pixel and supersampling nothing. Both numbers reach the
    /// engage line so the log always says which branch ran.</para>
    /// </summary>
    private static float ResolveFactor(out float configured, out bool floored)
    {
        configured = WorldUIConfig.PanelSupersampleFactor != null
            ? WorldUIConfig.PanelSupersampleFactor.Value
            : Defaults.PanelSupersampleFactor;
        // Mathf.Approximately, not ==, because BepInEx round-trips the value through the .cfg text.
        // ModBuild 199: THE FLOOR NO LONGER ASKS WHETHER THE VALUE LOOKS LIKE THE DEFAULT.
        // ModBuild 198 wrote `Mathf.Approximately(configured, Defaults.PanelSupersampleFactor)` here,
        // so that a value the user had tuned would be taken verbatim. I verified that logic against
        // the source and told the user the remedy would run. It did not: all 153 RESAMPLE VERDICT
        // lines of the next hardware log read `config 1.00, taken verbatim — this is a value the user
        // set`, because his cfg holds a hand-written 1.0 that is not bit-equal to the constant. The
        // whole ModBuild 198 experiment silently did not execute — the second time in three builds a
        // remedy shipped behind a condition that was never true.
        // A factor below the band limit is not a preference, it is a no-op: at 1.0 the capture target
        // is the window's own resolution and nothing is band-limited. So the floor is unconditional
        // now, and it SAYS SO ON EVERY REPORT LINE (see FactorFloored) naming the value it overrode,
        // rather than being a silent correction. If a deliberate 1.0 is ever wanted, it needs its own
        // explicit escape — not the accident of matching a constant.
        floored = configured < BandLimitFactor;
        return Mathf.Clamp(floored ? BandLimitFactor : configured, 0.5f, 2f);
    }

    private static void Engage(ConvertedPanel panel)
    {
        Camera? head = VRRigDriver.HeadCamera;
        if (head == null)
        {
            if (!_noHeadLogged)
            {
                _noHeadLogged = true;
                VRLog.Warn(Scope, "PANEL SUPERSAMPLE stands down: there is no head camera "
                                  + "(VRRigDriver.HeadCamera is null), so the capture-layer bit "
                                  + "cannot be kept out of the eye and the panel would be drawn "
                                  + "TWICE. THE CONSEQUENCE: floated windows keep today's direct "
                                  + "rendering. Retried on the next tick.");
            }
            return;
        }

        string window = panel.Target != null ? panel.Target.name : panel.HostGo.name;
        Rect rect = panel.HostRect.rect;
        float factor = ResolveFactor(out float configured, out bool floored);

        var e = new Entry
        {
            Panel = panel,
            Window = window,
            Factor = factor,
            ConfigFactor = configured,
            FactorFloored = floored,
            Frame = rect,
            HostRectAtMeasure = rect,
            Authored = rect.size,
            NextSweepFrame = Time.frameCount + SweepIntervalFrames,
            NextContentFrame = Time.frameCount + ContentMeasureIntervalFrames,
        };
        // Frame the CONTENT, not the host rect (see Entry.Frame): the ModBuild 192 log proved a
        // window whose content is six times wider than the frame it was measured into, and cropping
        // visible content is a worse defect than the shimmer this path removes.
        MeasureFrame(e);
        Rect frame = e.Frame;
        // `Authored` is what the render target was sized for, so it must be the FRAME, not the host
        // rect — otherwise a window whose content already overspills at engage time would look
        // "resized" to the very next LateTick and reallocate for nothing.
        e.Authored = frame.size;

        // THE RATE, THE MSAA STEP-DOWN AND THE FACTOR STEP-DOWN, in one place shared with
        // Reallocate (ModBuild 201 — before it, engaging and re-allocating derived the render-target
        // size by two separate copies of this arithmetic, and only one of them stepped down).
        //
        // The MSAA step-down answers to the SESSION headroom as well as to the per-panel cap. Before
        // ModBuild 193 it only saw the per-panel cap, so a window that could have fitted at MSAA 2x
        // or 1x was refused outright once the session filled — the cap message then blamed MaxPanels
        // for what was really a budget decision. A degraded-MSAA panel still gets the supersampled
        // rasterization AND the mip chain, which is the whole of the fix; MSAA only resolves the RT's
        // own internal edges.
        //
        // THE FACTOR STEP-DOWN (ModBuild 198) exists because ModBuild 198 REMOVED the lever the MSAA
        // loop used to be. With PreferredMsaa at 4 a window that did not fit was degraded 4x -> 2x ->
        // 1x, a 3.4x reduction, and was refused only after that; at PreferredMsaa 1 that loop cannot
        // step at all, so without this a memory-pressured window would go straight from
        // "supersampled" to "refused, keeps today's shimmer" — a graceful degradation silently traded
        // for an outage. Stepping the RATE is the same shape of concession and a better one: a window
        // at 1.5 still has half the fix, and one at 1.0 is exactly ModBuild 197's behaviour rather
        // than nothing at all. What it actually got is on the engage line and in the report's
        // ACHIEVED field, so a degraded window never reads like a full-strength one.
        int msaa = PreferredMsaa;
        int askedMsaa = msaa;
        long budget = System.Math.Min(MaxPanelVramBytes, MaxTotalVramBytes - _vramTotal);
        ResolveRate(e, frame, ref msaa, budget, out factor, out int rtW, out int rtH, out long vram);
        float askedFactor = e.AskedFactor;
        e.EffectiveFactor = factor;
        e.ScaleAtAllocation = e.MinContentScale;
        if (vram > budget)
        {
            Refused.Add(panel.HostGo.GetInstanceID());
            VRLog.Warn(Scope, $"PANEL SUPERSAMPLE refused '{window}': its {rtW}x{rtH} capture + mip "
                              + $"targets would cost {Mb(vram)} MB even at MSAA {msaa}x and factor "
                              + $"{factor:F2} (stepped down from {askedFactor:F2}, floor "
                              + $"{MinStepDownFactor:F2}), against a remaining budget of "
                              + $"{Mb(budget)} MB (per-panel cap {Mb(MaxPanelVramBytes)} MB, session "
                              + $"{Mb(_vramTotal)} of {Mb(MaxTotalVramBytes)} MB across "
                              + $"{Entries.Count} panel(s)). THE CONSEQUENCE: this window keeps "
                              + "today's direct rendering and will still shimmer. BOTH automatic "
                              + "concessions are already exhausted here, so the remaining levers are "
                              + "closing another floated window or lowering [WorldUI] "
                              + "PanelSupersampleFactor below "
                              + $"{MinStepDownFactor:F2} by hand.");
            return;
        }

        // THE PRIVATE CAPTURE LAYER — taken BEFORE anything is allocated, because a panel that
        // cannot get one must not be engaged at all. ModBuild 193 handed every panel the same layer
        // and every capture camera therefore rendered every panel inside its frustum; the whole
        // argument, the photograph that proves it and the three designs that were rejected are in
        // PanelSupersample.5.Isolation.cs.
        int layer = AcquireLayer();
        if (layer < 0)
        {
            Refused.Add(panel.HostGo.GetInstanceID());
            VRLog.Warn(Scope, $"PANEL SUPERSAMPLE refused '{window}': the capture-layer pool is "
                              + $"exhausted ({PoolSize} private layer(s) exist and all are held by "
                              + $"the {Entries.Count} panel(s) already engaged). It is NOT given a "
                              + "shared layer, because a shared layer is exactly what made one "
                              + "window's capture contain another window's content in ModBuild 193 "
                              + "(.planning/debug/window_merge.jpg). THE CONSEQUENCE: this window "
                              + "keeps today's direct rendering — the dial's OFF behaviour, "
                              + "including the shimmer — until another window closes and hands its "
                              + "layer back, at which point this one is re-considered automatically.");
            return;
        }
        e.Layer = layer;

        RenderTexture? rt = CreateRt(rtW, rtH, ref msaa, window);
        if (rt == null)
        {
            ReleaseLayer(e.Layer);
            e.Layer = -1;
            Refused.Add(panel.HostGo.GetInstanceID());
            return;
        }

        e.Rt = rt;
        e.RtW = rtW;
        e.RtH = rtH;
        e.Msaa = msaa;
        e.VramBytes = VramBytesFor(rtW, rtH, msaa);
        RecordAchievedFactor(e);
        AttachMipTarget(e, rtW, rtH);

        if (!BuildCamera(e, panel) || !BuildDisplay(e, panel))
        {
            DestroyEntryObjects(e);
            ReleaseLayer(e.Layer);
            e.Layer = -1;
            Refused.Add(panel.HostGo.GetInstanceID());
            return;
        }

        Entries.Add(e);
        _vramTotal += e.VramBytes;
        InstallHooks();
        ApplyCaptureLayer(e, initial: true);
        SyncGeometry(e);
        SyncVisibility(e);
        // THE SUB-MESH CULL PAIR CACHE, collected at engage rather than at the first content scan
        // (ModBuild 204). The scan runs on the 10 s report cadence, and a window whose sub-meshes are
        // already latched when it opens — which is the user's "mittlerweile taucht es auch initial
        // kaputt auf wenn man das Fenster öffnet" — must not stay broken for ten seconds waiting for
        // an instrument. One walk, once, at engage.

        string frameNote = e.ExpandX > 0.5f || e.ExpandY > 0.5f
            ? $" The capture frame was GROWN past the host rect by {e.ExpandX:F0}x{e.ExpandY:F0} uGUI "
              + "px to cover content the window draws proud of its own frame — without that, exactly "
              + "that content would be cropped out of the supersampled image."
            : " The capture frame IS the host rect to the pixel (no content draws outside it), so "
              + "this window's ON and OFF geometry are identical.";
        string msaaNote = msaa != askedMsaa
            ? $" MSAA was stepped {askedMsaa}x -> {msaa}x to fit the {Mb(budget)} MB budget left for "
              + "this panel; the supersampled rasterization and the mip chain are unaffected."
            : string.Empty;
        // THE FACTOR DECISION, stated with both numbers (ModBuild 198). A build that "ships factor 2"
        // and a session running at 1.0 because the user tuned it down must never read alike, and a
        // target the 4096 px ceiling clipped must not be reported as the factor it asked for.
        string factorNote = " FACTOR: "
            + (e.FactorFloored
                ? $"{e.ConfigFactor:F2} RT texels per authored px is still the SHIPPED DEFAULT, so it "
                  + $"was raised to the {BandLimitFactor:F2} band-limit floor"
                : $"{e.ConfigFactor:F2} RT texels per authored px is a value the USER SET, so it was "
                  + "taken verbatim and the band-limit floor did not apply")
            + (e.MinContentScale < ScaledContentThreshold
                ? $", then RAISED to {askedFactor:F2} because this window's smallest substantial "
                  + $"content is drawn at {e.MinContentScale:F3} of host scale "
                  + $"('{e.MinContentScaleName}', {e.MinContentScaleArea:F0} uGUI px²), which would "
                  + $"otherwise leave that subtree at {(e.Factor * e.MinContentScale):F2} texels per "
                  + "ITS OWN authored px — see Entry.MinContentScale"
                : string.Empty)
            + (factor < askedFactor - 1e-3f
                ? $", then CUT DOWN {askedFactor:F2} -> {factor:F2} by the {MaxRtDimension} px "
                  + $"per-axis ceiling and the {Mb(budget)} MB left for this panel (floor "
                  + $"{MinStepDownFactor:F2} = ModBuild 197's behaviour); below 2.00 the eye still "
                  + "reads some unfiltered mip level 0, so the carried-window flicker is only partly "
                  + "removed on THIS window"
                : string.Empty)
            + $"; the target achieves {e.AchievedFactor:F2}"
            + (e.MinContentScale < ScaledContentThreshold
                ? $" for host-scale content and {(e.AchievedFactor * e.MinContentScale):F2} for the "
                  + $"{e.MinContentScale:F3}-scaled subtree, against a band limit of "
                  + $"{BandLimitedTexelsPerPixel:F2} — if the second number is below the band limit "
                  + "then NO capture setting within this path's budget can reach it and the only "
                  + "complete cure is for that sub-view not to be scaled."
                : ".")
            + " WHY THIS NUMBER IS THE FIX: at one texel per authored pixel the eye resamples this "
            + "window at ~1 texel per rendered pixel, and a bilinear resample at that rate has a "
            + "sub-texel PHASE whose response at Nyquist runs from full contrast to zero. A still "
            + "window holds one phase and reads sharp; a carried window sweeps it every frame and "
            + "reads as the flicker; a released window LOCKS it, which is why letting go freezes "
            + "whatever the drag left behind. Two texels per authored pixel puts the eye on a mip "
            + "level that was box-filtered down from a 2x rasterization instead.";
        VRLog.Info(Scope, $"PANEL SUPERSAMPLE engaged on '{window}': the window's canvas now renders "
                          + $"into a {rtW}x{rtH} capture target (MSAA {msaa}x, D24S8) which is "
                          + $"resolved after every frame into a {rtW}x{rtH} MIPPED display target "
                          + $"(mips {(e.MipRt != null ? e.MipRt.mipmapCount : rt.mipmapCount)}, "
                          + $"Trilinear, aniso {AnisoLevel}) — {Mb(e.VramBytes)} MB for the pair — "
                          + "through a dedicated orthographic camera, and a mod-owned quad at the "
                          + $"window's exact world pose shows that texture.{factorNote}{msaaNote}{frameNote} The "
                          + $"host canvas itself is moved to this panel's PRIVATE capture layer "
                          + $"{e.Layer} (mask 0x{1 << e.Layer:X8}; {FreeLayers.Count} of "
                          + $"{PoolSize} pool layer(s) still free) — no other panel is ever on it, "
                          + "so this camera cannot capture a neighbouring window the way ModBuild "
                          + "193's shared layer did. It stays "
                          + "ENABLED, REGISTERED and RAYCASTABLE, so every laser/poke hit, hover, "
                          + "drag and scroll takes the same path as before; only the camera that "
                          + $"draws it changed. {e.LayersMoved} transform(s) moved, "
                          + $"{e.ForeignSkipped} foreign render subtree(s) left alone.");
    }
}
