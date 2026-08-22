using System.Collections.Generic;
using BepInEx.Configuration;
using GloomhavenVR.Core;
using UnityEngine;
using UnityEngine.XR;

namespace GloomhavenVR.Rig;

/// <summary>
/// VR render-quality enforcement (aliasing fix, hardware items #5b/#6): hardware MSAA on
/// the XR eye textures plus forced anisotropic texture filtering.
///
/// WHY THE HMD HAD NO AA AT ALL: the game's own anti-aliasing (FXAA/SMAA) lives in the
/// PostProcessLayer that the mod kill-switches ([Compat] DisablePostProcessing — the layer
/// breaks stereo), and the game boots into its LOWEST quality level ("[PlatformLayer.cs]
/// Enabling QualitySettings named Fastest") whose <see cref="QualitySettings.antiAliasing"/>
/// is 0 — so the VR view rendered with zero AA of any kind: strong shimmer ("grisseln") on
/// card line art, control-board and initiative-track edges, worst at distance. The mod's
/// head camera runs the FORWARD rendering path ([Rig] ForwardRendering, default on), where
/// hardware MSAA works — nothing ever switched it on.
///
/// ENFORCEMENT PATTERN (same trap as the skin-weights floor in
/// <c>HandsDriver.EnforceGlobalSkinWeights</c>): the game swaps quality levels at boot and
/// when the menu applies the user's saved graphics settings, and every swap rewrites
/// <see cref="QualitySettings.antiAliasing"/> from the level's own value — a set-once fix
/// silently dies at the next swap. So <see cref="Tick"/> re-asserts per frame from the
/// VRRigDriver guarded tail (cheap int compare), logging only on actual transitions.
///
/// EYE-TEXTURE PLUMBING: on built-in XR + the OpenXR plugin the engine drives the eye-render
/// MSAA from <see cref="QualitySettings.antiAliasing"/>; changing it re-allocates the render
/// targets live (no rig rebuild). Belt-and-braces, we also push the level straight to every
/// live <see cref="XRDisplaySubsystem"/> via <see cref="XRDisplaySubsystem.SetMSAALevel"/> —
/// the same engine hook SRPs use — so the display side never lags a quality-level fight.
///
/// TEXTURE-SIDE SHIMMER: distant sparkle on card faces also comes from texture sampling;
/// <see cref="ApplyAniso"/> forces anisotropic filtering globally
/// (<see cref="AnisotropicFiltering.ForceEnable"/> + a global min-aniso floor via
/// <see cref="Texture.SetGlobalAnisotropicFilteringLimits"/>) — safe: it only raises
/// sampling quality, never touches game content.
///
/// RETRACTED 2026-07 — "eyeTextureDesc.msaaSamples==1 ⇒ MSAA is not binding" WAS A WRONG
/// INFERENCE, and this file used to print it as a VERDICT. It is not merely unproven, it is
/// unprovable from that reading, and it cost a whole performance investigation: the reader
/// trusted the log line and concluded MSAA was inert and therefore free. The facts:
///
/// - <see cref="XRSettings.eyeTextureDesc"/> and the per-display render-pass renderTargetDesc
///   describe the SUBMITTED swapchain image. A compositor swapchain image is single-sample by
///   definition — multisample surfaces are resolved before submission. msaaSamples==1 there is
///   therefore the EXPECTED reading whether MSAA ran or not; it cannot distinguish the cases.
/// - The decisive evidence is the rendered image, and it is unambiguous: cycling this row in
///   the VR settings VISIBLY changes edge quality in the headset (user report 2026-07, "MSAA
///   IST sichtbar im Spiel wenn man es anmacht … insbesondere beim Controllboard").
/// - And the change can only be the eye render, by elimination: the MSAA row's ONLY reachable
///   targets are the XR eye path and the desktop backbuffer, because EVERY RenderTexture the
///   mod allocates pins <c>antiAliasing = 1</c> explicitly (FlatScreen.2.CameraStack:265,
///   FlatScreen.3.Desktop:118, FlatScreenStereo.2.Compositor:179, FlatScreenStereo.3.Map:37
///   and :1661) and a camera rendering into a RenderTexture samples at THAT texture's count,
///   not QualitySettings'. The control board is a mod-built object seen only through the HMD.
///
/// So MSAA binds, it is a real user-visible quality feature, and it is NOT free.
/// <see cref="LogEyeTargetDiagnostics"/> now reports the descs and states what they can and
/// cannot decide; it no longer pronounces a verdict it has no evidence for.
///
/// THE COST SIDE — WHY THE TWO LEVERS BELOW ARE ONE DECISION: the runtime already hands us a
/// heavily supersampled target (hardware log: 3072x3264 per eye against a 2064x2208 Quest 3
/// panel = 1.49x linear, 2.2x the displayable pixels) and MultiPass renders it TWICE. MSAA and
/// supersampling attack the SAME artefact — geometry edges — and their sample counts multiply:
/// effective edge samples per display pixel ≈ msaa x linearOversample². At 8x MSAA on a 1.49x
/// target that is ~17.7, far past the point where more helps. Meanwhile the cost of the two is
/// NOT the same shape: resolution scales every per-pixel stage (shading, rasterization, the
/// MSAA surfaces and their resolve) with scale², whereas MSAA multiplies the surface and
/// resolve bandwidth but not the shading. So the cheap quality is bought by trimming MSAA
/// first and resolution second — which is exactly the order <see cref="Presets"/> uses.
/// NOT VERIFIED ON HARDWARE: the perceptual claim ("6.4 effective samples looks like 17.7").
/// The sample arithmetic is verifiable; how it looks is what the A/B in the settings is for.
///
/// RESOLUTION LEVER ([RenderQuality] EyeResolutionScale →
/// <see cref="XRSettings.eyeTextureResolutionScale"/>): scales the eye-texture allocation.
/// Above 1 it is brute-force AA that also fixes SHADER/TEXTURE shimmer (specular sparkle,
/// sub-pixel detail) which geometry-edge MSAA cannot touch; below 1 it is the single largest
/// GPU saving available here, because all per-pixel work scales with scale². Applies live.
///
/// VIEWPORT FALLBACK (a constant since the 2026-08-22 settings audit; it was
/// [RenderQuality] ViewportScaleFallback, default ON): some OpenXR
/// providers negotiate the swapchain once at session start and ignore
/// <see cref="XRSettings.eyeTextureResolutionScale"/> afterwards. We cannot know which from
/// static inspection, so we MEASURE: <see cref="LogEyeTargetDiagnostics"/> reads
/// <see cref="XRSettings.eyeTextureWidth"/> back against the baseline captured at scale 1.0
/// and, if the allocation did not move, engages <see cref="XRSettings.renderViewportScale"/>
/// instead — rendering into a sub-rect of the existing swapchain, which every provider
/// honours. Either way the log names WHICH lever actually bound, so the resolution row is
/// never silently dead.
///
/// THE MSAA DEFAULT STAYS 8x — RE-EXAMINED 2026-08-23 AGAINST A MEASUREMENT, AND THE MEASUREMENT
/// SAYS THE SAMPLES ARE NOT THE PROBLEM. The performance round that raised the question had a good
/// prior: 160.4 Msamples/frame, on a target the runtime already hands us supersampled, twice per
/// frame under MultiPass, asserted by this file against the game's own quality levels. Three
/// readings from the ModBuild 226 hardware log kill it as a suspect:
///
/// - THE HARDWARE. NVIDIA RTX 4090, 24 GB, D3D11. The eye render is 3072x3264 x 2 eyes =
///   20.1 Mpixel/frame. At the frame rates the complaint is actually about — the zoomed-out view
///   ran a 71–73 ms frame, i.e. ~14 fps — that is ~0.28 Gpixel/s of shading and, on the crudest
///   uncompressed model of an 8x surface (8 samples x 8 bytes of colour+depth, written and then
///   resolved), ~36 GB/s of MSAA traffic against a bus that does ~1000. MSAA cannot be spending
///   28 ms there. It could plausibly matter in a 90 Hz frame, where the same model asks for
///   ~230 GB/s — but a 90 Hz frame is one that is already inside budget.
/// - THE CONTROLLED EXPERIMENT IS ALREADY IN HIS LOG. The [Perf] SPLIT ZOOM axis buckets one
///   30 s window by viewing distance at a FIXED eye resolution and a FIXED sample count: 23
///   renderers visible → 10.97 ms/frame; 4841 visible → 71.28 ms. Same pixels, same samples, 6.5x
///   the frame time. Everything that swung is scene content.
/// - WHERE THE FRAME ACTUALLY WENT in that window: logic ~50 %, render loop (cull+submit) ~13 %,
///   blocked ~36 %. MSAA can only ever appear inside the blocked share, and the far-bucket frames
///   prove the whole pixel pipeline fits inside 11 ms when the scene is empty.
///
/// AND THE COST OF BEING WRONG IS ASYMMETRIC. Dropping the default costs the one thing this file
/// exists for: the game's own AA is gone (the mod disables the PostProcessLayer) and the boot
/// quality level sets antiAliasing 0, so the shipped default IS the anti-aliasing. A quiet default
/// change would also be undiscoverable — the player would meet a softer image with no row having
/// moved, which is precisely the shape of the report this round is answering. So the trade is
/// OFFERED instead: <see cref="Presets"/> ▸ "Ausgewogen" is 4x, "Leistung" 2x, "Schwache Hardware"
/// off, each one visible in a named dropdown the player picks and can pick back.
/// NOT VERIFIED ON HARDWARE: the bandwidth arithmetic above is a model, and no GPU-time counter
/// exists on this runtime ("gpu n/a" on every [Perf] FRAME line) to check it against. What IS
/// measured is the ZOOM axis, and it is enough to say the samples are not this frame's wall.
///
/// REBUILD TEST PATH: GONE (2026-08-22 settings audit). [RenderQuality] RebuildRigOnMsaaChange
/// tore the rig down/up on every MSAA change, as an escape hatch for providers that only
/// re-negotiate sample counts at session start. The question it was written to settle — does MSAA
/// bind at all? — is answered above (it does, visibly), and what was left was a player-facing
/// toggle whose ON state reset the view mid-scenario every time the MSAA row was touched.
/// </summary>
internal static class RenderQuality
{
    /// <summary>Valid MSAA sample counts, in the order <see cref="CycleMsaa"/> walks them.</summary>
    private static readonly int[] MsaaSteps = { 0, 2, 4, 8 };

    /// <summary>Forced minimum / global maximum aniso level while ForceAnisotropic is on.</summary>
    private const int ForcedMinAniso = 8;
    private const int GlobalMaxAniso = 16;

    /// <summary>
    /// Config bounds of the resolution lever (native = 1). The floor was 0.8 while this was
    /// an anti-aliasing knob only; it is 0.5 now that the same entry is the primary GPU
    /// lever — 0.8 caps the saving at 36% of pixel work, which is not enough to pull a
    /// machine out of runtime reprojection.
    /// </summary>
    private const float MinEyeScale = 0.5f;
    private const float MaxEyeScale = 2.0f;

    /// <summary>Tolerance for "the eye texture actually re-allocated" (fraction of expected width).</summary>
    private const float EyeWidthMatchTolerance = 0.05f;

    /// <summary>
    /// Frames between an MSAA/eye-scale change and the eye-target diagnostic readback —
    /// long enough for the live swapchain re-allocation to land before we read the desc.
    /// </summary>
    private const int DiagDelayFrames = 30;

    /// <summary>
    /// One named point on the MSAA x resolution curve. The two levers below trade against the
    /// SAME artefact and their sample counts multiply (class doc), so offering them as two
    /// independent numbers asks the player to solve a two-variable problem blind. A preset is
    /// one choice; the individual rows stay visible and adjustable afterwards, and any manual
    /// edit simply reads back as "custom" — nothing is hidden or locked.
    /// Ordered best-looking → cheapest; <see cref="CyclePreset"/> walks them in this order.
    /// Values sit on the 0.1 stepper grid so the preset and the stepper never disagree.
    /// </summary>
    private readonly struct Preset
    {
        internal readonly string LocId;
        internal readonly int Msaa;
        internal readonly float Scale;

        /// <summary>
        /// The per-pixel light cap this preset asserts (-1 = leave the game's own value alone,
        /// which is what the two quality presets do). ADDED 2026-08-23 with the preset ROW: a
        /// preset is one decision standing in for several dials, and leaving the third dial out
        /// of it would have made "Leistung" a name for two thirds of a decision. It is also the
        /// only member of the trio whose cost is a LOOK and not a sharpness, so it is the last
        /// thing spent and the first thing named in the row's description.
        /// </summary>
        internal readonly int PixelLights;

        internal Preset(string locId, int msaa, float scale, int pixelLights)
        {
            LocId = locId;
            Msaa = msaa;
            Scale = scale;
            PixelLights = pixelLights;
        }
    }

    /// <summary>
    /// MSAA is trimmed BEFORE resolution, deliberately: resolution below 1 also softens
    /// TEXTURE detail (and the game's card atlases are mipless — see the Cards FACE TEXTURE
    /// DIAG line), whereas MSAA only ever touched geometry edges, which the remaining
    /// supersample still covers. Relative per-pixel cost is noted per entry as a MODEL, not a
    /// measurement: shading ∝ scale², MSAA surface+resolve bandwidth ∝ msaa x scale².
    /// </summary>
    private static readonly Preset[] Presets =
    {
        new("preset_quality",     8, 1.0f, -1), // shading 100%, msaa bandwidth 100%, lights untouched
        new("preset_balanced",    4, 0.9f, -1), // shading  81%, msaa bandwidth  41%, lights untouched
        new("preset_performance", 2, 0.8f,  2), // shading  64%, msaa bandwidth  16%, 2 per-pixel lights
        new("preset_minimum",     0, 0.6f,  1), // shading  36%, msaa bandwidth   5%, 1 per-pixel light
    };

    /// <summary>
    /// Dropdown index of "custom" — the hand-tuned combination that matches no preset. It sits
    /// AFTER the four presets rather than before them so the preset order in the dropdown is the
    /// quality order of <see cref="Presets"/>, and so adding a fifth preset does not renumber
    /// anything a player has already picked.
    /// </summary>
    internal static int CustomPresetIndex => Presets.Length;

    /// <summary>
    /// The preset row's option list as <see cref="Core.Loc"/> keys, in dropdown order, with
    /// "custom" last. Built from <see cref="Presets"/> rather than typed out at the row, so adding
    /// a preset is one line in that table and the menu grows by itself — the same reason the head
    /// mask's option list is built from HeadMaskLibrary instead of listed in the panel.
    /// </summary>
    internal static string[] PresetLocIds()
    {
        var ids = new string[Presets.Length + 1];
        for (int i = 0; i < Presets.Length; i++)
            ids[i] = Presets[i].LocId;
        ids[Presets.Length] = "preset_custom";
        return ids;
    }

    private static ConfigFile? _file;
    internal static ConfigEntry<int>? MsaaLevel;
    internal static ConfigEntry<bool>? ForceAnisotropic;
    internal static ConfigEntry<float>? EyeResolutionScale;
    internal static ConfigEntry<int>? PixelLightCount;

    /// <summary>
    /// The preset ROW's config entry, and it is a MIRROR of a derived value rather than a fourth
    /// piece of state. <see cref="CurrentPresetIndex"/> answers "which preset do the three dials
    /// currently spell" by reading the dials themselves; <see cref="MirrorPresetToConfig"/> copies
    /// that answer in here once whenever the two disagree. Nothing else ever writes it, so this is
    /// NOT the write war the standing rule forbids — there is exactly one writer and it copies FROM
    /// the truth, never back onto it. Moving the MSAA row by hand therefore drops this to
    /// <see cref="CustomPresetIndex"/> on the next tick instead of fighting the change.
    /// </summary>
    internal static ConfigEntry<int>? QualityPreset;

    /// <summary>
    /// Fall back to <c>XRSettings.renderViewportScale</c> when <c>eyeTextureResolutionScale</c>
    /// does not move the allocation. ALWAYS ON, and no longer a dial.
    ///
    /// <para>2026-08-22 settings audit (user, verbatim): <i>"a) Lösche alle Einstellungen die das
    /// Spiel breaken könnten wenn die verändert werden. Etwas was das spiel kaputt macht wenn man
    /// es umstellt ist nicht optional und sollte daher nicht einstellbar sein."</i> The harm, and
    /// the value that causes it: <c>false</c> removes the fallback that makes
    /// <c>EyeResolutionScale</c> — a CURATED row on the Grafik page — work at all on providers
    /// that negotiate the swapchain once at session start, and the row then does nothing while
    /// still moving. That is the "der X-Offset hat keinen Einfluss" failure, aimed at the one dial
    /// a player reaches for when the picture is too soft. There is also nothing to decide: the
    /// choice between the two levers is made BY READING THE ALLOCATION BACK, not by guessing, and
    /// the [Rig] EYE-TARGET DIAG line names which one bound.</para>
    /// </summary>
    internal const bool ViewportScaleFallback = true;

    private static readonly List<XRDisplaySubsystem> Displays = new(2);
    private static int _lastPushedDisplayMsaa = -1;
    private static float _lastLoggedEyeScale = -1f;
    private static int _lastLoggedPixelLights = int.MinValue;
    /// <summary>The game's own pixelLightCount before we first capped it (-1 = we have not).</summary>
    private static int _pixelLightsOriginal = -1;
    private static int _diagCountdown;
    private static string _diagReason = "";
    private static bool _anisoForced;
    private static AnisotropicFiltering _anisoOriginal;

    /// <summary>
    /// Eye-texture width observed while the resolution scale was 1.0 — the yardstick the
    /// readback measures a scaled allocation against. 0 = not sampled yet.
    /// </summary>
    private static int _baseEyeWidth;

    /// <summary>
    /// Which lever the resolution row is actually riding on, decided by readback:
    /// null = undecided (no scaled value asserted yet), true = the eye texture re-allocated
    /// (eyeTextureResolutionScale bound), false = it did not and renderViewportScale took over.
    /// </summary>
    private static bool? _eyeScaleBinds;

    /// <summary>Last value written to <see cref="XRSettings.renderViewportScale"/> by us (1 = untouched).</summary>
    private static float _viewportScaleApplied = 1f;

    /// <summary>
    /// Has the resolution row announced itself in the log yet this session?
    ///
    /// <para>USER REPORT, ModBuild 226, verbatim: <i>"Ich habe auch versucht die Auflösung
    /// umzustellen, ich bin mir nicht sicher ob das überhaupt irgendwas gebracht hat."</i> The log
    /// of that session answers it and the answer is that the mod's own dial was never touched — the
    /// row read 1.00 throughout, and thirty EYE-TARGET DIAG blocks said "resolution scale is 1.0 —
    /// nothing to verify". THAT WAS THE DEFECT: at the default value <see cref="ApplyEyeScale"/>
    /// returned before its own log line, so the ONLY evidence that a resolution lever exists at all
    /// was a sentence saying there was nothing to look at. A player who changed a resolution
    /// somewhere else — the game's flat options page, the Virtual Desktop or SteamVR slider — had no
    /// way to learn from the log that those are UPSTREAM of this row and move a different number.
    /// So the row announces itself once per session whatever it reads, and names where it lives.</para>
    /// </summary>
    private static bool _eyeScaleAnnounced;

    /// <summary>
    /// Pixel-sample budget measured while both quality levers were at their shipped values —
    /// the yardstick every later budget line is quoted against, so a change reports what it BOUGHT
    /// ("160.4 → 102.7 Msamples/frame, -36%") instead of an absolute nobody can place. 0 = not
    /// sampled yet.
    /// </summary>
    private static double _baseMegaSamples;

    /// <summary>Last preset index mirrored into <see cref="QualityPreset"/> (-1 = none yet).</summary>
    private static int _lastMirroredPreset = -1;

    /// <summary>
    /// Bind-once against the rig's own module config (dev.gloomhavenvr.rig.cfg —
    /// canonical <see cref="ModuleConfig"/> pattern; Plugin.cs's main config is owned
    /// by another seam). Lazy: called from the tick and from the panel accessors.
    /// </summary>
    internal static void Bind()
    {
        if (_file != null)
            return;
        _file = ModuleConfig.Create("rig");
        MsaaLevel = _file.Bind("RenderQuality", "MsaaLevel", Defaults.MsaaLevel, new ConfigDescription(
            "Hardware MSAA sample count for the VR eye render (0 = off, 2/4/8). The game's own "
            + "AA lives in the PostProcessLayer the mod disables and its boot quality level sets "
            + "antiAliasing 0, so without this the HMD has NO anti-aliasing (shimmering card line "
            + "art / board edges). Re-asserted every frame against the game's quality-level swaps; "
            + "applies live. Requires the forward rendering path ([Rig] ForwardRendering, default "
            + "on) — the deferred path ignores MSAA. COSTS GPU TIME: it multiplies the render-target "
            + "and resolve bandwidth of a target the runtime already hands us supersampled, twice "
            + "per frame under MultiPass. Trade it against EyeResolutionScale below — both fight "
            + "geometry aliasing and their sample counts multiply, so 8x on a heavily supersampled "
            + "target is the expensive half of a job already mostly done.",
            new AcceptableValueList<int>(0, 2, 4, 8)));
        ForceAnisotropic = _file.Bind("RenderQuality", "ForceAnisotropic", Defaults.ForceAnisotropic,
            "Force anisotropic texture filtering for ALL textures (plus a global min-aniso floor). "
            + "Cuts distant shimmer on flat-on-view textures — card faces, initiative portraits, "
            + "board art. Purely a sampling-quality raise; disable to restore the game's setting.");
        EyeResolutionScale = _file.Bind("RenderQuality", "EyeResolutionScale", Defaults.EyeResolutionScale, new ConfigDescription(
            "Render resolution per eye, relative to what the OpenXR runtime asks for (1 = as asked). "
            + "THE primary GPU lever: essentially all per-pixel work — shading, rasterization, the "
            + "MSAA surfaces and their resolve — scales with the SQUARE of this value. 0.7 = about "
            + "half the pixel work, 1.4 = about double. Note the runtime's request is itself usually "
            + "supersampled (Virtual Desktop / SteamVR resolution sliders sit on top of this), so "
            + "below 1 is often still above panel resolution — check the [Rig] EYE-TARGET DIAG line "
            + "for the actual pixel count. Above 1 is the only lever against SHADER/TEXTURE shimmer "
            + "(specular sparkle, sub-pixel detail) that geometry-edge MSAA cannot touch; below 1 "
            + "softens texture detail before it softens edges. Applies live. "
            + "WHAT IT CANNOT BUY, measured rather than assumed (ModBuild 226 hardware log, the "
            + "[Perf] SPLIT ZOOM axis): with this row and the MSAA row held CONSTANT, one window of "
            + "that session ran 10.97 ms per frame with 23 renderers visible and 71.28 ms with 4841 "
            + "— a 6.5x swing at an IDENTICAL pixel count. Frame time in the heavy view is owned by "
            + "scene content (logic ~50 %, the render loop ~13 %) and no pixel lever touches either. "
            + "Lowering this row can only help where the frame is fill- or bandwidth-bound, which is "
            + "what the [Perf] SPLIT 'blocked' share is the place to look for. That is not nothing — "
            + "it is just not the lever for a heavy scene.",
            new AcceptableValueRange<float>(MinEyeScale, MaxEyeScale)));
        // [RenderQuality] ViewportScaleFallback and RebuildRigOnMsaaChange stood here. Both were
        // removed by the 2026-08-22 settings audit — the fallback is a constant (off made the
        // curated resolution row silently do nothing on some providers), the rebuild is gone
        // outright. The argument for each is written where it now lives; see the constant above
        // and the tombstone in PushDisplayMsaa.

        PixelLightCount = _file.Bind("RenderQuality", "PixelLightCount", Defaults.PixelLightCount, new ConfigDescription(
            "Maximum number of PER-PIXEL lights (-1 = leave the game's own value alone, which is "
            + "what ships). In the built-in forward renderer every per-pixel light beyond the first "
            + "costs an ADDITIONAL FULL DRAW CALL for every renderer it touches — a scenario "
            + "measured 4 per-pixel lights against ~1500 visible renderers, so this multiplies the "
            + "submission volume that the [Perf] SPLIT line shows as the frame's wall. Lights beyond "
            + "this count still light the scene, but per VERTEX, which costs no extra pass. THE "
            + "TRADE IS REAL AND VISIBLE: point-light falloff on walls and floors gets flatter, and "
            + "this dungeon is lit by 16 point lights. The game exposes no control for this, which "
            + "is why the mod does. Re-asserted per frame like MsaaLevel, because the game rewrites "
            + "QualitySettings on every quality-level swap.",
            new AcceptableValueRange<int>(-1, 8)));

        // THE PRESET ROW (2026-08-23). Three rows above this one are three numbers a player is
        // asked to solve as one problem — they trade against the same artefact, their sample counts
        // multiply (class doc), and the third one buys frames with a LOOK rather than with
        // sharpness. Picking a point on that curve is one decision, and it now reads as one.
        //
        // WHY THE VALUE IS A MIRROR AND NOT A MASTER, which is the whole reason this is safe to
        // ship next to the individual dials: the dropdown SHOWS CurrentPresetIndex(), derived from
        // the three dials every time it is drawn, and MirrorPresetToConfig copies that derived
        // answer into this entry when the two disagree. Editing MSAA by hand therefore moves this
        // row to "custom" — it does not get overwritten back to a preset, because nothing reads
        // this entry to decide anything. That is the "no hidden write war against the individual
        // dials" requirement, satisfied by there being exactly one writer copying FROM the truth.
        QualityPreset = _file.Bind("RenderQuality", "QualityPreset", Defaults.QualityPreset, new ConfigDescription(
            "Named point on the MSAA x resolution x per-pixel-light curve: 0 = Quality (MSAA 8x, "
            + "eye 1.00x, lights untouched — what ships), 1 = Balanced (4x, 0.90x, lights "
            + "untouched), 2 = Performance (2x, 0.80x, 2 per-pixel lights), 3 = Minimum (MSAA off, "
            + "0.60x, 1 per-pixel light), 4 = custom, i.e. the three rows spell no preset. "
            + "READ-MOSTLY: this value is DERIVED from MsaaLevel, EyeResolutionScale and "
            + "PixelLightCount and mirrored here once whenever they change, so hand-editing the "
            + "three rows moves this one rather than being overwritten by it. Setting it applies "
            + "all three at once; nothing is locked afterwards. HONEST ABOUT ITS REACH: every dial "
            + "it moves is a PIXEL or a SUBMISSION dial, and a frame whose wall is game logic "
            + "(the [Perf] SPLIT line's 'logic' share) will barely notice any of them.",
            new AcceptableValueRange<int>(0, CustomPresetIndex)));

        // The SKY dial ([Sky] Style) RIDES this module's file — the FlatScreenStereo-on-worldui
        // pattern: the catalog's force-bind of RenderQuality surfaces it, module "rig" files it
        // under the Visual topic, and the runtime stays where it belongs (Core/SkyAlternative.cs).
        SkyAlternative.BindConfig(_file);

        // ELEMENT MOOD ([Elements] EnvironmentResponse / ResponseStrength) rides the same file, for
        // the same reason and right behind the sky dial: it is the environment's reaction to the
        // element infusions, it is read in the same breath as [Sky] Style, and a player looking for
        // it on disk will look where the environment choice is. Runtime stays in Core/ElementMood.cs.
        ElementMood.BindConfig(_file);

        // HAUNT ([Haunt] EasterEggs / Frequency) rides the same file for the third time and the
        // same reason: the creepy easter eggs exist only inside the two bundled environments, so
        // the switch belongs beside the switch that chooses one. Runtime stays in Core/Haunt.cs.
        Haunt.BindConfig(_file);

        // ENV SOUND ([EnvSound] Enabled / Gain) rides the same file for the fourth time and the
        // same reason: it is the environment HEARD, it is meaningless without the environment the
        // [Sky] Style row above chooses, and it also governs the easter-egg cues bound just before
        // it (user correction 2026-08-14 — the apparitions are no longer silent, and they are on
        // THIS toggle rather than getting a second one). Runtime stays in Core/EnvSound.cs.
        EnvSound.BindConfig(_file);
    }

    /// <summary>Per-frame enforcement (VRRigDriver guarded tail step "Rig.RenderQuality").</summary>
    internal static void Tick()
    {
        if (!VRSession.IsRunning)
            return;
        Bind();
        ApplyMsaa();
        ApplyEyeScale();
        ApplyAniso();
        ApplyPixelLights();
        MirrorPresetToConfig();
        if (_diagCountdown > 0 && --_diagCountdown == 0)
            LogEyeTargetDiagnostics(_diagReason);
    }

    /// <summary>
    /// Schedule the eye-target diagnostic readback <see cref="DiagDelayFrames"/> frames out
    /// (rig build, MSAA push, eye-scale change) — delayed so the swapchain re-allocation the
    /// change triggers has landed by the time we read <see cref="XRSettings.eyeTextureDesc"/>.
    /// </summary>
    internal static void RequestEyeTargetDiagnostics(string reason)
    {
        _diagReason = reason;
        _diagCountdown = DiagDelayFrames;
    }

    /// <summary>Snap an arbitrary persisted value to the nearest valid sample count.</summary>
    private static int Sanitize(int level) =>
        level >= 8 ? 8 : level >= 4 ? 4 : level >= 2 ? 2 : 0;

    /// <summary>
    /// Cap <see cref="QualitySettings.pixelLightCount"/> ([RenderQuality] PixelLightCount).
    ///
    /// <para>WHY THIS EXISTS. Six hardware sessions established that the frame's wall WAS
    /// main-thread DRAW-CALL SUBMISSION, not pixels: culling measured 0.08 ms against 21.5 ms of
    /// submission, and resolution, MSAA, shadows, the depth prepass and the culling mask each
    /// moved it by under 10 %. The one multiplier nothing had touched is this one — in the
    /// built-in FORWARD renderer each per-pixel light past the first re-submits every renderer it
    /// affects. The scenario runs 4 of them over ~1500 visible renderers.</para>
    ///
    /// <para>THAT WALL IS GONE (2026-07-28, <c>.planning/perf/FINDINGS.md</c>): the submission was
    /// serialised on one thread, and threaded submission (<c>[Core] EnableGraphicsJobs</c>) took the
    /// main-thread render loop from 14.9 ms to 1.8 ms. This entry measured ~1 % even back when the
    /// main thread WAS the bottleneck, so it is now a pure quality trade with no performance case
    /// behind it. Kept because the game exposes no control for it and someone on weak hardware may
    /// still want it — but it is not a lever anyone should be pointed at.</para>
    ///
    /// <para>-1 (the default) does NOTHING, deliberately: this is a visible trade, not a cleanup.
    /// Lights above the cap still light the scene per VERTEX, so nothing goes dark — but the
    /// falloff on walls and floors flattens, and this dungeon is lit by 16 point lights.</para>
    /// </summary>
    private static void ApplyPixelLights()
    {
        int wanted = PixelLightCount!.Value;

        if (wanted < 0)
        {
            // RESTORE, not "do nothing" — the first version of this returned here, so switching
            // back to -1 left the cap in place and the lights never came back. Every mutation of
            // game state in this mod has to be reversible, and a sentinel that means "hands off"
            // has to HAND BACK anything already taken.
            if (_pixelLightsOriginal >= 0)
            {
                QualitySettings.pixelLightCount = _pixelLightsOriginal;
                VRLog.Info("Rig", $"Per-pixel light cap released — restored the game's own value "
                                  + $"{_pixelLightsOriginal}. (Note the game rewrites this on every "
                                  + "quality-level swap, so the restored value is whatever it last set.)");
                _pixelLightsOriginal = -1;
                _lastLoggedPixelLights = int.MinValue;
            }
            return;
        }

        int current = QualitySettings.pixelLightCount;
        if (current == wanted)
            return;

        // Remember what the game had BEFORE we ever touched it, so -1 can give it back.
        if (_pixelLightsOriginal < 0)
            _pixelLightsOriginal = current;

        QualitySettings.pixelLightCount = wanted;
        if (wanted != _lastLoggedPixelLights)
        {
            _lastLoggedPixelLights = wanted;
            VRLog.Info("Rig", $"Per-pixel light cap asserted {current} → {wanted}. In forward "
                              + "rendering each light past the first costs one extra draw call per "
                              + "renderer it touches; lights above the cap fall back to per-vertex "
                              + "shading (no extra pass, flatter falloff). Watch the [Perf] SPLIT "
                              + "line's HeadCamera submit figure — that is the number this moves.");
            LogLightCensus(wanted);
        }
    }

    /// <summary>
    /// Count the lights the cap is actually acting on, at the moment it changes.
    ///
    /// <para>WHY THIS EXISTS. The row's own description has claimed "this dungeon is lit by 16 point
    /// lights" and "4 per-pixel lights against ~1500 visible renderers" since it was written, and
    /// both are quotes from ONE measurement of ONE scenario. A cap set to 2 in a room holding three
    /// lights changes nothing and looks identical to a cap that is not being applied at all — the
    /// "gated remedy never ran" shape. So the assertion says how many candidates there are, which
    /// makes "no improvement" mean something.</para>
    ///
    /// <para>COST, AND WHY IT IS ALLOWED HERE. This is a full scene sweep, the exact thing that once
    /// owned an entire frame in this project. It runs ONLY behind the same transition gate as the
    /// log line above — i.e. when the player moves the row — never per frame, and never on the
    /// game's own quality-level swaps, which re-assert the value without changing what we want.
    /// Lights are counted whether enabled or not is NOT the question: only ENABLED lights on ACTIVE
    /// objects can be submitted, so those are what is counted, and the disabled remainder is
    /// reported separately rather than folded in.</para>
    /// </summary>
    private static void LogLightCensus(int cap)
    {
        Light[] lights;
        try
        {
            lights = Object.FindObjectsOfType<Light>(); // active-and-enabled only, by contract
        }
        catch (System.Exception e)
        {
            VRLog.Info("Rig", $"Per-pixel light cap: the light census threw '{e.Message}' — the cap "
                              + "itself is applied, only this count is missing.");
            return;
        }

        int point = 0, spot = 0, directional = 0, other = 0;
        for (int i = 0; i < lights.Length; i++)
        {
            switch (lights[i].type)
            {
                case LightType.Point: point++; break;
                case LightType.Spot: spot++; break;
                case LightType.Directional: directional++; break;
                default: other++; break;
            }
        }

        // Only a realtime/mixed light that is ALLOWED a per-pixel pass can cost one, so neither a
        // baked light (already in the lightmap) nor a ForceVertex light (which opted out of the
        // pixel path itself) is a candidate. Naming the split stops a reader concluding "16 lights,
        // cap 2, so 14 passes saved" from a number that includes both. Note bakingOutput, not
        // Light.lightmapBakeType — the latter is editor-only in 2021.3 and does not compile here.
        int candidates = 0, forcedVertex = 0, baked = 0;
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i].bakingOutput.lightmapBakeType == LightmapBakeType.Baked)
                baked++;
            else if (lights[i].renderMode == LightRenderMode.ForceVertex)
                forcedVertex++;
            else
                candidates++;
        }

        int overCap = Mathf.Max(0, candidates - Mathf.Max(cap, 0));
        VRLog.Info("Rig", $"Per-pixel light cap census at cap {cap}: {lights.Length} enabled Light(s) "
                          + $"in the scene ({point} point, {spot} spot, {directional} directional, "
                          + $"{other} other); {baked} baked and {forcedVertex} ForceVertex are not "
                          + $"candidates, leaving {candidates} that can cost a forward pass at "
                          + $"all. AT MOST {overCap} of those are "
                          + "pushed to per-vertex shading by this cap — 'at most' because Unity picks "
                          + "the per-pixel set PER RENDERER by intensity and distance, so a light far "
                          + "from a given wall was never that wall's per-pixel light to begin with. "
                          + "IF THIS NUMBER IS 0 the cap is inert in this scene and any frame-rate "
                          + "change you see came from something else.");
    }

    private static void ApplyMsaa()
    {
        int wanted = Sanitize(MsaaLevel!.Value);

        int current = QualitySettings.antiAliasing;
        if (current != wanted)
        {
            QualitySettings.antiAliasing = wanted;
            string quality = QualitySettings.names[QualitySettings.GetQualityLevel()];
            VRLog.Info("Rig", $"MSAA (re)asserted {current}x → {wanted}x (quality level '{quality}'; " +
                              "the game rewrites antiAliasing on every quality-level swap, so this " +
                              "re-arms per frame like the skin-weights floor).");
        }

        // Push to the live XR display once per value change — the built-in pipeline mirrors
        // QualitySettings into the eye-texture desc itself, but an explicit SetMSAALevel makes
        // the swapchain re-allocation deterministic (and covers any pipeline that doesn't).
        if (wanted != _lastPushedDisplayMsaa)
        {
            SubsystemManager.GetInstances(Displays);
            if (Displays.Count == 0)
                return; // display not up yet — retry next tick
            for (int i = 0; i < Displays.Count; i++)
                Displays[i].SetMSAALevel(Mathf.Max(wanted, 1)); // XR API: 1 = no MSAA
            _lastPushedDisplayMsaa = wanted;
            VRLog.Info("Rig", $"XR display MSAA level pushed to {Mathf.Max(wanted, 1)} on " +
                              $"{Displays.Count} display subsystem(s) — eye textures re-allocate live.");
            // Read the ACTUAL eye-target sample count back once the re-allocation had time
            // to land — this is the line that proves (or disproves) the MSAA took effect.
            RequestEyeTargetDiagnostics($"MSAA push {Mathf.Max(wanted, 1)}x");
            // [RenderQuality] RebuildRigOnMsaaChange drove a rig rebuild from here — an opt-in
            // hardware experiment asking whether a rebuild re-binds MSAA. DELETED by the
            // 2026-08-22 settings audit: its own bound text already closed with "the question this
            // was originally written to settle … is answered … so this is no longer a diagnostic",
            // and it shipped as an ordinary player-facing toggle whose ON state tore the whole VR
            // rig down and rebuilt it — a full view reset, mid-scenario — every time the MSAA row
            // two lines above was touched. The answer it recorded stands in the class doc.
        }
    }

    /// <summary>
    /// Resolution lever: assert <c>[RenderQuality] EyeResolutionScale</c> onto
    /// <see cref="XRSettings.eyeTextureResolutionScale"/>. Re-asserted per frame (one float
    /// compare — the setter re-allocates the eye textures, so it must never be spammed while
    /// equal); logs + schedules the diagnostic readback once per distinct target value.
    /// Fully reversible: 1.0 restores the native allocation AND releases the viewport fallback.
    /// The baseline width is sampled while the scale is still 1.0 — that is the yardstick
    /// <see cref="VerifyEyeScaleBound"/> later measures the allocation against.
    /// </summary>
    private static void ApplyEyeScale()
    {
        float wanted = Mathf.Clamp(EyeResolutionScale!.Value, MinEyeScale, MaxEyeScale);

        if (_baseEyeWidth == 0
            && Mathf.Abs(XRSettings.eyeTextureResolutionScale - 1f) < 0.0005f
            && XRSettings.eyeTextureWidth > 0)
        {
            _baseEyeWidth = XRSettings.eyeTextureWidth;
        }

        // ANNOUNCE THE ROW ONCE PER SESSION, WHATEVER IT READS (see _eyeScaleAnnounced for the
        // report this answers). Waits for a live eye texture so the announcement can quote the
        // pixel count the runtime actually asked for — the number a player who moved a DIFFERENT
        // resolution slider will recognise, and the number that tells them their change DID land,
        // one layer up, without ever touching this row.
        if (!_eyeScaleAnnounced && XRSettings.eyeTextureWidth > 0)
        {
            _eyeScaleAnnounced = true;
            bool atNative = Mathf.Abs(wanted - 1f) < 0.0005f;
            VRLog.Info("Rig", $"Eye resolution row [RenderQuality] EyeResolutionScale reads "
                              + $"{wanted:F2}x — {(atNative ? "the shipped default, i.e. UNCHANGED" : "a changed value")}. "
                              + $"The runtime is asking for {XRSettings.eyeTextureWidth}x"
                              + $"{XRSettings.eyeTextureHeight} per eye and this row scales THAT. "
                              + "IT IS THE ONLY RESOLUTION THE MOD CAN MOVE: the game's own options "
                              + "page and the Virtual Desktop / SteamVR resolution sliders sit "
                              + "UPSTREAM of it — changing one of those changes the request quoted "
                              + "above and never this number, which is why such a change leaves no "
                              + "trace on this line. In the headset the row is VR-Einstellungen ▸ "
                              + "Bild ▸ Darstellung ▸ 'Auflösung pro Auge'; on disk it is "
                              + "dev.gloomhavenvr.rig.cfg. Whether a non-default value BINDS is "
                              + "read back and named on the EYE-TARGET DIAG line below.");
        }

        // Back at native: undo the viewport fallback too, so "1.0" always means "exactly what
        // the runtime asked for" no matter which lever we were riding.
        if (Mathf.Abs(wanted - 1f) < 0.0005f && _viewportScaleApplied != 1f)
            ReleaseViewportScale();

        if (Mathf.Abs(XRSettings.eyeTextureResolutionScale - wanted) < 0.0005f)
        {
            // The allocation lever is already where we want it. If a previous readback proved
            // it does not bind here, keep the viewport fallback tracking the wanted value.
            if (_eyeScaleBinds == false && ViewportScaleFallback
                && Mathf.Abs(_viewportScaleApplied - wanted) > 0.0005f && Mathf.Abs(wanted - 1f) >= 0.0005f)
            {
                ApplyViewportScale(wanted, "resolution row changed while the viewport fallback is engaged");
            }
            return;
        }

        // Write ONCE per distinct target, tracked on our own side rather than on the property's
        // readback. A provider that ignores this setter also never reflects it in the getter, so
        // the compare above would be true forever and we would re-issue a swapchain-reallocating
        // write every single frame — on exactly the runtimes where it buys nothing.
        if (Mathf.Abs(_lastLoggedEyeScale - wanted) < 0.0005f)
            return;
        XRSettings.eyeTextureResolutionScale = wanted;
        _lastLoggedEyeScale = wanted;
        VRLog.Info("Rig", $"Eye render resolution scale asserted → {wanted:F2} " +
                          $"(per-pixel GPU work ∝ scale² ≈ {wanted * wanted:F2}x; applies live). " +
                          "Whether the ALLOCATION follows is read back below — this runtime may " +
                          "instead need the renderViewportScale fallback.");
        RequestEyeTargetDiagnostics($"eyeTextureResolutionScale → {wanted:F2}");
    }

    /// <summary>
    /// Readback half of the resolution lever, run from <see cref="LogEyeTargetDiagnostics"/>
    /// once the re-allocation has had <see cref="DiagDelayFrames"/> to land: did the eye
    /// texture actually change size? If it did, we are done. If it did not, the provider
    /// ignored the allocation request and <see cref="XRSettings.renderViewportScale"/> takes
    /// over — the same pixel saving, achieved by rendering into a sub-rect of the swapchain
    /// the provider already gave us. Returns the sentence appended to the DIAG line.
    /// </summary>
    private static string VerifyEyeScaleBound()
    {
        float wanted = Mathf.Clamp(EyeResolutionScale!.Value, MinEyeScale, MaxEyeScale);
        int actual = XRSettings.eyeTextureWidth;

        // "NOTHING TO VERIFY" WAS THE WHOLE PROBLEM. Thirty of these lines in the ModBuild 226 log
        // said exactly that and nothing else, so a player asking "did changing the resolution do
        // anything?" found a sentence that neither confirmed nor denied it. The default case now
        // states which row is at its default, what the runtime is asking for, and — the part that
        // actually answers the question — that a resolution changed ANYWHERE ELSE would have moved
        // the request rather than this row, and is therefore already included in the size below.
        if (Mathf.Abs(wanted - 1f) < 0.0005f)
            return $"resolution scale is 1.00 = the shipped default, so the mod is NOT scaling the "
                   + $"eye render at all — the {XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight} "
                   + "per eye above is exactly what the OpenXR runtime asked for. IF YOU CHANGED A "
                   + "RESOLUTION AND ARE LOOKING FOR ITS EFFECT: a change made in the game's options "
                   + "page or in Virtual Desktop / SteamVR lands in THAT number, not in this one, so "
                   + "compare the per-eye size against a previous log rather than expecting this row "
                   + "to move. The mod's own lever is [RenderQuality] EyeResolutionScale "
                   + "(VR-Einstellungen ▸ Bild ▸ Darstellung ▸ 'Auflösung pro Auge'), and at 1.00 it "
                   + "has done nothing, correctly.";
        if (_baseEyeWidth <= 0 || actual <= 0)
            return "resolution scale cannot be verified yet — no baseline eye width sampled at scale 1.0 " +
                   "(the scale was already off-native when the rig came up); the row still applies, but " +
                   "which lever carries it is unknown this session.";

        float expected = _baseEyeWidth * wanted;
        bool allocationMoved = Mathf.Abs(actual - expected) <= expected * EyeWidthMatchTolerance;

        if (allocationMoved)
        {
            if (_eyeScaleBinds != true)
            {
                _eyeScaleBinds = true;
                if (_viewportScaleApplied != 1f)
                    ReleaseViewportScale();
            }
            return $"resolution scale BOUND via eyeTextureResolutionScale — eye texture went " +
                   $"{_baseEyeWidth}px → {actual}px wide (expected ~{expected:F0}px at {wanted:F2}x).";
        }

        _eyeScaleBinds = false;
        // The "fallback is OFF, so the resolution row is doing NOTHING" branch stood here, and it
        // could never be reached again: ViewportScaleFallback is a constant since the 2026-08-22
        // settings audit, precisely so that no cfg can put the curated resolution row into that
        // silent state.
        bool fallbackStuck = ApplyViewportScale(wanted, $"eyeTextureResolutionScale did not move the allocation " +
                                                        $"({actual}px, expected ~{expected:F0}px)");

        // THE THIRD OUTCOME, WHICH HAD NO SENTENCE UNTIL NOW: both levers refused. The class doc
        // says "every provider honours renderViewportScale", and that is a claim about providers,
        // not a proof — a claim this readback can now falsify per session instead of per project.
        // If it ever prints, the resolution row genuinely did nothing on this runtime and the log
        // says so in the words the report asked for, rather than leaving the player to guess.
        if (!fallbackStuck)
        {
            return $"resolution scale {wanted:F2} DID NOTHING AT ALL on this runtime — the eye "
                   + $"texture stayed {actual}px wide (expected ~{expected:F0}px) AND the "
                   + $"renderViewportScale fallback did not stick either (reads "
                   + $"{XRSettings.renderViewportScale:F2} straight after the write). Both levers "
                   + "this mod has were refused by the provider, so the row is inert HERE and no "
                   + "value you set in it will change the picture or the frame rate. Nothing is "
                   + "broken and nothing needs undoing; use the resolution slider of Virtual Desktop "
                   + "/ SteamVR instead, which sits upstream of both.";
        }

        return $"resolution scale did not bind via eyeTextureResolutionScale (eye texture still " +
               $"{actual}px wide) — renderViewportScale {wanted:F2} engaged instead AND READ BACK " +
               "as applied; the pixel saving is real, it just comes from rendering a sub-rect of " +
               "the swapchain rather than a smaller swapchain.";
    }

    /// <summary>
    /// Write the viewport fallback and READ IT STRAIGHT BACK. Returns whether it stuck.
    ///
    /// <para>The readback is the point: this is the last lever the mod has, so "it did not take
    /// either" is the only honest way to say "your resolution change did nothing", and the caller
    /// prints exactly that. <c>renderViewportScale</c> is a plain engine-side property with no
    /// swapchain re-allocation behind it, so unlike <c>eyeTextureResolutionScale</c> it reflects
    /// immediately and needs no settling frames — which is why this check is inline rather than
    /// another delayed diagnostic.</para>
    /// </summary>
    private static bool ApplyViewportScale(float wanted, string reason)
    {
        XRSettings.renderViewportScale = wanted;
        _viewportScaleApplied = wanted;
        bool stuck = Mathf.Abs(XRSettings.renderViewportScale - wanted) < 0.005f;
        VRLog.Info("Rig", $"renderViewportScale → {wanted:F2} ({reason}); readback "
                          + $"{XRSettings.renderViewportScale:F2} = {(stuck ? "APPLIED" : "REFUSED — this provider ignores the viewport lever too")}. "
                          + $"Per-pixel GPU work ∝ scale² ≈ {wanted * wanted:F2}x; the compositor "
                          + "samples only the rendered sub-rect, so the view is unchanged apart "
                          + "from sharpness.");
        return stuck;
    }

    private static void ReleaseViewportScale()
    {
        XRSettings.renderViewportScale = 1f;
        _viewportScaleApplied = 1f;
        VRLog.Info("Rig", "renderViewportScale released → 1.00 (resolution row back at native).");
    }

    /// <summary>
    /// The delayed readback: eyeTextureDesc + per-display render-pass descs + head-camera
    /// render-path state, then the per-frame pixel-sample budget and the resolution-lever
    /// verification.
    ///
    /// WHAT THIS CAN PROVE: nothing about MSAA — see the class doc's RETRACTED section for the
    /// full argument and the trap. It prints the numbers, states their limits, and leaves the
    /// MSAA question to the headset.
    /// </summary>
    private static void LogEyeTargetDiagnostics(string reason)
    {
        int wanted = Sanitize(MsaaLevel!.Value);
        RenderTextureDescriptor desc = XRSettings.eyeTextureDesc;

        Camera? head = VRRigDriver.HeadCamera;
        string headState = head == null
            ? "no head camera"
            : $"head cam allowMSAA={head.allowMSAA}, allowHDR={head.allowHDR}, " +
              $"path={head.actualRenderingPath}, depthTex={head.depthTextureMode}, " +
              $"target={(head.targetTexture != null ? head.targetTexture.name : "XR eye target (direct)")}";

        VRLog.Info("Rig", $"EYE-TARGET DIAG ({reason}): QualitySettings.antiAliasing={QualitySettings.antiAliasing}, " +
                          $"eyeTextureDesc {desc.width}x{desc.height} msaaSamples={desc.msaaSamples} " +
                          $"fmt={desc.colorFormat} dim={desc.dimension} sRGB={desc.sRGB}; " +
                          $"eyeTexture {XRSettings.eyeTextureWidth}x{XRSettings.eyeTextureHeight}, " +
                          $"resolutionScale={XRSettings.eyeTextureResolutionScale:F2}, " +
                          $"viewportScale={XRSettings.renderViewportScale:F2}, " +
                          $"stereo={XRSettings.stereoRenderingMode}, device='{XRSettings.loadedDeviceName}', " +
                          $"gfx={SystemInfo.graphicsDeviceType}; {headState}.");

        // Display-side render passes: on the BUILT-IN pipeline the display may expose 0
        // passes outside SRP render callbacks — logged either way so absence is evidence too.
        SubsystemManager.GetInstances(Displays);
        for (int i = 0; i < Displays.Count; i++)
        {
            try
            {
                int passes = Displays[i].GetRenderPassCount();
                if (passes == 0)
                {
                    VRLog.Info("Rig", $"EYE-TARGET DIAG: display {i} exposes 0 render passes from " +
                                      "script (normal on built-in pipeline — eyeTextureDesc above is " +
                                      "the authoritative engine-side value).");
                    continue;
                }
                for (int p = 0; p < passes; p++)
                {
                    Displays[i].GetRenderPass(p, out XRDisplaySubsystem.XRRenderPass pass);
                    RenderTextureDescriptor rt = pass.renderTargetDesc;
                    VRLog.Info("Rig", $"EYE-TARGET DIAG: display {i} renderPass {p}: " +
                                      $"{rt.width}x{rt.height} msaaSamples={rt.msaaSamples} fmt={rt.colorFormat} " +
                                      $"dim={rt.dimension}.");
                }
            }
            catch (System.Exception e)
            {
                VRLog.Info("Rig", $"EYE-TARGET DIAG: display {i} render-pass query threw " +
                                  $"'{e.Message}' — eyeTextureDesc above remains the evidence.");
            }
        }

        // What the descs above CAN and CANNOT decide — the string says it, because the log is
        // where the wrong verdict was read (class doc, RETRACTED).
        VRLog.Info("Rig", "EYE-TARGET DIAG: the sample counts above describe the SUBMITTED SWAPCHAIN " +
                          "IMAGE, which is single-sample by definition (multisample surfaces are " +
                          "resolved before submission). msaaSamples=1 there is therefore the EXPECTED " +
                          "reading whether MSAA ran or not, and NOTHING here can tell the two apart. " +
                          $"What is known: we requested {wanted}x and QualitySettings.antiAliasing " +
                          $"reads {QualitySettings.antiAliasing}. Whether that lands is decided by the " +
                          "one instrument that can decide it — looking through the headset with the " +
                          "MSAA row toggled (hardware 2026-07: it is clearly visible, so it does land, " +
                          "and it is therefore also costing GPU time).");

        // Per-frame pixel-sample budget: the single number to watch when trading the two
        // quality levers. It is the product of everything the levers touch, so it moves
        // exactly as much as the settings change — unlike the descs, it is directly comparable
        // across settings and across sessions.
        int eyeW = XRSettings.eyeTextureWidth;
        int eyeH = XRSettings.eyeTextureHeight;
        float viewport = Mathf.Clamp(XRSettings.renderViewportScale, 0.01f, 1f);
        int eyePasses = XRSettings.stereoRenderingMode == XRSettings.StereoRenderingMode.MultiPass ? 2 : 1;
        double megaSamples = eyeW * (double)eyeH * viewport * viewport
                             * Mathf.Max(wanted, 1) * eyePasses / 1_000_000.0;
        // BASELINE ONCE, DELTA EVERY TIME AFTERWARDS. An absolute in Msamples is a number nobody can
        // place; the whole value of this line is answering "did the setting I just changed buy
        // anything", so it is quoted against the budget measured while both levers were at their
        // shipped values. The baseline is only captured while the row is at 1.00 and MSAA at its
        // default, so a session that starts on a tuned config honestly reports no baseline rather
        // than inventing one out of the tuned state.
        if (_baseMegaSamples <= 0
            && Mathf.Abs(Mathf.Clamp(EyeResolutionScale!.Value, MinEyeScale, MaxEyeScale) - 1f) < 0.0005f
            && viewport > 0.995f && wanted == Defaults.MsaaLevel && megaSamples > 0)
        {
            _baseMegaSamples = megaSamples;
        }

        string against = _baseMegaSamples <= 0
            ? "no shipped-default baseline was captured this session (the levers were already tuned "
              + "when the rig came up), so this figure stands alone"
            : Mathf.Abs((float)(megaSamples - _baseMegaSamples)) < 0.05f
                ? $"unchanged from the shipped-default baseline ({_baseMegaSamples:F1})"
                : $"vs {_baseMegaSamples:F1} at the shipped defaults = "
                  + $"{(megaSamples - _baseMegaSamples) / _baseMegaSamples * 100.0:+0.0;-0.0}%";

        VRLog.Info("Rig", $"EYE-TARGET DIAG: pixel-sample budget = {megaSamples:F1} Msamples/frame " +
                          $"({eyeW}x{eyeH} per eye x viewport {viewport:F2}² x {Mathf.Max(wanted, 1)} MSAA " +
                          $"samples x {eyePasses} pass(es), {XRSettings.stereoRenderingMode}) — {against}. " +
                          "This is the number the quality preset moves; halve it and the GPU frame time " +
                          "should fall roughly in step WHEREVER THE FRAME IS FILL/BANDWIDTH BOUND — and " +
                          "the [Perf] SPLIT line is what says whether it is. A frame whose 'logic' share " +
                          "is the wall will not move at all, however far this figure falls.");

        VRLog.Info("Rig", $"EYE-TARGET DIAG: {VerifyEyeScaleBound()}");
    }

    private static void ApplyAniso()
    {
        if (ForceAnisotropic!.Value)
        {
            if (QualitySettings.anisotropicFiltering == AnisotropicFiltering.ForceEnable)
                return;
            if (!_anisoForced)
                _anisoOriginal = QualitySettings.anisotropicFiltering; // restore point
            QualitySettings.anisotropicFiltering = AnisotropicFiltering.ForceEnable;
            Texture.SetGlobalAnisotropicFilteringLimits(ForcedMinAniso, GlobalMaxAniso);
            VRLog.Info("Rig", $"Anisotropic filtering forced globally (was {_anisoOriginal}; " +
                              $"min {ForcedMinAniso}, max {GlobalMaxAniso}) — texture-side " +
                              "distant-shimmer mitigation for card/board art.");
            _anisoForced = true;
        }
        else if (_anisoForced)
        {
            QualitySettings.anisotropicFiltering = _anisoOriginal;
            Texture.SetGlobalAnisotropicFilteringLimits(-1, -1); // engine: -1/-1 resets limits
            _anisoForced = false;
            VRLog.Info("Rig", $"Anisotropic filtering force released — restored {_anisoOriginal}.");
        }
    }

    // ---- panel accessors: "MSAA" cycle row — NO CALLER TODAY --------------------------------
    // Label/cycle pairs for three in-VR options rows (MSAA here, supersampling and graphics
    // preset below). THEY ARE CURRENTLY UNCALLED: the WorldUI.SettingsPanel that bound them no
    // longer exists, and WorldUI.VROptionsTab curates no RenderQuality row at all — the
    // [RenderQuality] entries are reachable only through the generic config browser
    // (Debug ▸ Alle Einstellungen). Evidence: .planning/menu-audit/03-core-rig-perf.md.
    // They are kept because whether to curate these rows again is an open product decision;
    // deleting them is that decision, not a cleanup.

    internal static string MsaaLabel()
    {
        Bind();
        int v = Sanitize(MsaaLevel!.Value);
        return v == 0 ? "Off" : $"{v}x";
    }

    internal static void CycleMsaa()
    {
        Bind();
        int idx = System.Array.IndexOf(MsaaSteps, Sanitize(MsaaLevel!.Value));
        int next = MsaaSteps[(idx + 1) % MsaaSteps.Length];
        // Close the measurement window on the OLD value before writing the new one, so the log
        // carries one [Perf] FRAME/SPLIT summary per MSAA level instead of one summary averaging
        // however many levels the tester happened to cycle inside a 30 s interval.
        Core.PerfMonitor.MarkChange($"MSAA {Sanitize(MsaaLevel!.Value)}x → {next}x");
        MsaaLevel!.Value = next;
        // No further plumbing needed: Tick's compare re-asserts QualitySettings and pushes
        // the new level to the XR display next frame; BepInEx persists on set.
    }

    // ---- panel accessors: "Supersampling" stepper row — no caller today (see the MSAA block) --

    /// <summary>Stepper readout for the resolution row ("0.5x".."2.0x").</summary>
    internal static string EyeScaleLabel()
    {
        Bind();
        return $"{Mathf.Clamp(EyeResolutionScale!.Value, MinEyeScale, MaxEyeScale):0.0}x";
    }

    /// <summary>
    /// Step <c>[RenderQuality] EyeResolutionScale</c> by ±0.1, clamped to
    /// <see cref="MinEyeScale"/>–<see cref="MaxEyeScale"/> (rounded to one decimal so repeated
    /// presses never drift off the 0.1 grid — the same grid <see cref="Presets"/> sits on, so
    /// stepping onto a preset's value reads back as that preset). Applies live: Tick re-asserts
    /// the scale, and the readback then decides whether the allocation or the viewport lever
    /// carries it. BepInEx persists on set.
    /// </summary>
    internal static void StepEyeScale(int delta)
    {
        Bind();
        float next = EyeResolutionScale!.Value + delta * 0.1f;
        float clamped = Mathf.Clamp(Mathf.Round(next * 10f) / 10f, MinEyeScale, MaxEyeScale);
        if (Mathf.Abs(clamped - EyeResolutionScale.Value) > 0.001f)
            Core.PerfMonitor.MarkChange($"eye resolution {EyeResolutionScale.Value:F1}x → {clamped:F1}x");
        EyeResolutionScale.Value = clamped;
    }

    // ---- panel accessors: graphics-preset cycle row — no caller today (see the MSAA block) ----

    /// <summary>
    /// Index of the preset the CURRENT values match, or -1 for a hand-tuned combination. The
    /// preset is DERIVED, never stored: there is no fourth piece of state to fall out of sync
    /// with the two rows below it, and editing either row simply reads back as "custom".
    /// </summary>
    private static int CurrentPresetIndex()
    {
        int msaa = Sanitize(MsaaLevel!.Value);
        float scale = Mathf.Clamp(EyeResolutionScale!.Value, MinEyeScale, MaxEyeScale);
        int lights = PixelLightCount!.Value;
        for (int i = 0; i < Presets.Length; i++)
        {
            if (Presets[i].Msaa == msaa && Mathf.Abs(Presets[i].Scale - scale) < 0.005f
                && Presets[i].PixelLights == lights)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// Dropdown index for the preset ROW: the matching preset, or <see cref="CustomPresetIndex"/>.
    /// Derived on every read — see <see cref="QualityPreset"/> for why nothing is stored.
    /// </summary>
    internal static int PresetRowIndex()
    {
        Bind();
        int idx = CurrentPresetIndex();
        return idx < 0 ? CustomPresetIndex : idx;
    }

    /// <summary>
    /// Apply the preset the row picked. Picking "custom" is a NO-OP on purpose: there is no such
    /// combination to restore to, the label only ever describes a state the three rows are already
    /// in, and writing anything for it would turn a readout into a fourth setting.
    /// </summary>
    internal static void ApplyPresetByIndex(int index)
    {
        Bind();
        if (index < 0 || index >= Presets.Length)
            return;
        Preset p = Presets[index];
        Core.PerfMonitor.MarkChange($"graphics preset → '{p.LocId}' (MSAA {p.Msaa}x, eye {p.Scale:F2}x, "
                                    + $"pixel lights {p.PixelLights})");
        MsaaLevel!.Value = p.Msaa;                 // BepInEx persists on set; Tick applies next frame
        EyeResolutionScale!.Value = p.Scale;
        PixelLightCount!.Value = p.PixelLights;
        VRLog.Info("Rig", $"Graphics preset '{p.LocId}' applied — MSAA {p.Msaa}x, eye resolution "
                          + $"{p.Scale:F2}x, per-pixel light cap {p.PixelLights}. Modelled per-pixel "
                          + $"shading ∝ {p.Scale * p.Scale:F2}x and MSAA surface/resolve bandwidth ∝ "
                          + $"{Mathf.Max(p.Msaa, 1) * p.Scale * p.Scale:F2}x relative to 1.0x/8x. "
                          + "MODELLED, not measured: the honest readout is the [Perf] FRAME line "
                          + "before and after, and if that line's 'logic' share is the wall none of "
                          + "these three will move it. The three rows stay editable — changing any "
                          + "one of them simply reads back here as 'custom'.");
    }

    /// <summary>
    /// Copy the DERIVED preset index into <see cref="QualityPreset"/> when the two disagree, so the
    /// cfg file on disk never claims a preset the dials do not spell. One writer, copying from the
    /// truth, converging in one tick — see the entry's own doc for why this is not a write war.
    /// </summary>
    private static void MirrorPresetToConfig()
    {
        int derived = PresetRowIndex();
        if (derived == _lastMirroredPreset && QualityPreset!.Value == derived)
            return;
        _lastMirroredPreset = derived;
        if (QualityPreset!.Value != derived)
            QualityPreset.Value = derived;
    }

    /// <summary>Cycle-button readout: the matching preset's name, or "custom".</summary>
    internal static string PresetLabel()
    {
        Bind();
        int idx = CurrentPresetIndex();
        return Core.Loc.Mod(idx < 0 ? "preset_custom" : Presets[idx].LocId);
    }

    /// <summary>
    /// Advance to the next preset (from "custom", start at the best-looking one) and write both
    /// levers. Nothing is locked afterwards — the MSAA and resolution rows stay live, and moving
    /// either one just drops the readout back to "custom".
    /// </summary>
    internal static void CyclePreset()
    {
        Bind();
        // Delegates since 2026-08-23: the preset is a DROPDOWN row now (Bild ▸ Darstellung), and
        // two copies of "write the three levers and say so" would be two places for the pixel-light
        // member to be forgotten. The MarkChange that makes a [Perf] sweep readable — the 2026-07
        // sweep cycled all four presets inside ONE 30 s summary window and produced four
        // measurements that could not be attributed to anything — lives in ApplyPresetByIndex now.
        ApplyPresetByIndex((CurrentPresetIndex() + 1) % Presets.Length);
    }
}
