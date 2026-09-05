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
/// first and resolution second — which was the order the (now removed) preset table used, and is
/// still the order to trim by hand.
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
/// OFFERED instead — as the MSAA row itself. From 2026-08-23 to 2026-09-05 it was ALSO offered as
/// a four-point "Grafik-Voreinstellung" dropdown; that offering is REMOVED (user ruling: "Entferne
/// die Graphik-Profile wieder in den VR-Einstellungen, die mag ich nicht."), so the trade is made
/// on the individual rows, which is where it always applied. The tombstone above the config binds
/// carries the argument and names every deleted member.
///
/// THE PER-PIXEL LIGHT CAP IS NOT ON THE EVERYDAY PAGE AT ALL (2026-08-23, user ruling, verbatim:
/// "Die Pixellichter option ist zu gefährlich für normale Nutzer, sie sollte in Erweitert
/// verschwinden und per default auch in allen Graphik-Voreinstellungen auf 0 geschaltet sein.").
/// Both halves hold: Defaults.PixelLightCount is 0, and with the presets gone there is nothing
/// left that could hand a player a light he did not ask for.
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

    // ==========================================================================================
    //  THE GRAPHICS PRESETS ARE GONE — the whole offering, not just its row
    // ==========================================================================================
    // WHAT STOOD HERE, from 2026-08-23 to 2026-09-05: a `Preset` struct, a four-entry `Presets`
    // table (Qualität 8x/1.00x, Ausgewogen 4x/0.90x, Leistung 2x/0.80x, Schwache Hardware off/
    // 0.60x, all four with a per-pixel light cap of 0), `CustomPresetIndex`, and `PresetLocIds()`
    // — plus, further down this file, `CurrentPresetIndex`, `PresetRowIndex`, `ApplyPresetByIndex`,
    // `MirrorPresetToConfig`, `PresetLabel` and `CyclePreset`.
    //
    // THE RULING, verbatim (2026-09-05): "Entferne die Graphik-Profile wieder in den
    // VR-Einstellungen, die mag ich nicht." Deleting only the menu row would have left the same
    // dropdown on Erweitert ▸ Bild & Darstellung — the catalog is a reflection walk over every
    // bound entry, so a row that stops being curated is one navigation level away, not gone — which
    // is not what "entferne" says. So [RenderQuality] QualityPreset is RETIRED at its bind (the
    // "LEGACY — no effect" marker ConfigCatalog.IsRetired reads) and the machinery went with it.
    //
    // AND THE MACHINERY HAD TO GO, WHICH IS THE PART THAT MATTERS. `ApplyPresetByIndex` was the
    // only code path in the mod that could overwrite MsaaLevel, EyeResolutionScale or
    // PixelLightCount without the player touching those rows; its only live caller was the
    // dropdown (VROptionsTab.4.Curated.TryBuildSpecialRow), and `CyclePreset` — the older cycle
    // button — had had no caller at all since the dropdown replaced it. Leaving a callerless
    // apply path standing next to three hand-tuned dials is how a tuned value gets moved by
    // something that is not the player. The three dials have exactly one writer now, and it is him.
    //
    // NOTHING WAS EVER READ BACK OUT of the preset entry: it was a MIRROR, written once per tick
    // from an index DERIVED off the three dials, and nothing consulted it to decide anything. That
    // is why retiring it moves no shipped value — and the key stays bound at its old default (4 =
    // "Eigene") so an existing dev.gloomhavenvr.rig.cfg is not rewritten under a player.
    //
    // THE HALF OF A USER RULING THAT LIVED ON THE `Presets` TABLE is preserved here, because the
    // table it was written on is what disappeared (2026-08-23, verbatim): "Die Pixellichter option
    // ist zu gefährlich für normale Nutzer, sie sollte in Erweitert verschwinden und per default
    // auch in allen Graphik-Voreinstellungen auf 0 geschaltet sein." Its first half is UNTOUCHED
    // and is what the shipped install runs — Defaults.PixelLightCount is 0, and the row lives on
    // Erweitert ▸ Bild & Darstellung. Its second half is now satisfied by there being no preset
    // that could hand a pixel light out at all. DO NOT RE-CREATE A PRESET TABLE without re-reading
    // both rulings: a preset is exactly the mechanism that gives a player a light he did not ask
    // for, and it is exactly the mechanism this one asked to have removed.

    private static ConfigFile? _file;
    internal static ConfigEntry<int>? MsaaLevel;
    internal static ConfigEntry<bool>? ForceAnisotropic;

    /// <inheritdoc cref="ApplyTextureLimit"/>
    internal static ConfigEntry<bool>? ForceFullTextureResolution;

    /// <inheritdoc cref="ApplyTextureStreaming"/>
    internal static ConfigEntry<bool>? ForceTextureStreamingOff;

    /// <inheritdoc cref="ApplyTextureStreaming"/>
    internal static ConfigEntry<int>? TextureStreamingBudgetMB;
    internal static ConfigEntry<float>? EyeResolutionScale;
    internal static ConfigEntry<int>? PixelLightCount;

    /// <summary>
    /// RETIRED and INERT — the graphics presets were removed on 2026-09-05 by user ruling
    /// ("Entferne die Graphik-Profile wieder in den VR-Einstellungen, die mag ich nicht.").
    ///
    /// <para>It was never a master: it held a MIRROR of an index derived off MsaaLevel,
    /// EyeResolutionScale and PixelLightCount, refreshed once per tick, and nothing ever read it
    /// back to decide anything. Both halves of that traffic are gone — the derivation, the mirror
    /// write, and every apply path — so the field is now only a handle on a bound key that keeps a
    /// player's cfg file from being rewritten. See the tombstone where the preset table stood, and
    /// the marker on its own bind below; DO NOT give it a reader.</para>
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

    /// <summary>
    /// How many times the cap had to be re-asserted WITHOUT the player having moved the row — i.e.
    /// how many times the game overwrote our value behind our back.
    ///
    /// <para>WHY THIS COUNTER EXISTS. Until now every such correction was SILENT: the log line in
    /// <see cref="ApplyPixelLights"/> is gated on <c>wanted != _lastLoggedPixelLights</c>, so a
    /// correction back to the SAME wanted value printed nothing at all. The ModBuild 227 hardware
    /// log therefore could not answer "is his chosen 0 ever secretly 4?" — and the six
    /// <c>4 → 0</c> lines that made the question look urgent turn out to be the mod's OWN -1
    /// round-trips (each one is preceded by a "cap released — restored the game's own value 4"
    /// line, at 9788, 10501, 12101, 14122 and 14913; the sixth is the boot assertion). That is a
    /// clean answer to the wrong question: it says nothing about the silent path, because the
    /// silent path leaves no trace. It does now.</para>
    ///
    /// <para>THE GAME'S WRITERS, from the decompiled sources, are
    /// <c>GraphicProfile.Setup()</c> (writes <c>QualitySettings.pixelLightCount = PixelLight</c>),
    /// <c>GraphicSettings.SetPixelLight()</c> (the options dropdown) and
    /// <c>GraphicSettings.SetQualityLevel()</c> → <c>QualitySettings.SetQualityLevel(index)</c>,
    /// which resets the value to the level's authored one. All three run from ordinary
    /// <c>Update</c>-order UI callbacks or from scene init, and this tick runs in the rig's
    /// <c>Update</c> tail, i.e. BEFORE any camera renders that frame — so a correction lands in the
    /// same frame it is needed and no frame is drawn at the wrong cap. The counter is what proves
    /// that rather than asserting it: a figure of 0 means the game never touched it, a small figure
    /// means it was corrected the same frame, and a figure that climbs every window would mean a
    /// writer we have not found is fighting us per frame and the cadence is NOT adequate.</para>
    /// </summary>
    private static int _silentCorrections;
    private static int _silentCorrectionsReported;
    private static float _nextSilentReportTime;
    private static int _lastSilentFoundValue = int.MinValue;

    /// <summary>Seconds between silent-correction summaries (only printed when the count moved).</summary>
    private const float SilentReportSeconds = 30f;
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
        ForceFullTextureResolution = _file.Bind("RenderQuality", "ForceFullTextureResolution",
            Defaults.ForceFullTextureResolution,
            "Force Unity's global texture limit back to 0 — i.e. stop the game discarding the top "
            + "mip levels of every texture. The game's own Options > Graphics > Texture Quality row "
            + "writes this as a MIP-DROP COUNT (FULL/HALF/QUARTER/EIGHTH), it is persisted in the "
            + "save file, and QualitySettings.SetQualityLevel re-loads it on every quality swap — "
            + "so it is re-asserted per frame like the MSAA and per-pixel-light dials. Its "
            + "deserialisation fallback for an unrecognised saved value is EIGHTH, which would "
            + "render the whole game at 1/8 resolution with nothing reporting it. Costs VRAM only "
            + "and no frame time: a larger mip is not sampled more often, it is sampled from a "
            + "different level. Turn off only to A/B against the game's own setting.");
        ForceTextureStreamingOff = _file.Bind("RenderQuality", "ForceTextureStreamingOff",
            Defaults.ForceTextureStreamingOff,
            "Turn Unity's MIPMAP STREAMING off, so every mipped texture is resident at its full "
            + "authored level instead of being held below it until a budget catches up. THIS IS THE "
            + "ANSWER TO 'the higher graphics preset looks WORSE': the game authors streaming ON at "
            + "its 'Fantastic' quality level and OFF at 'Beautiful', which the ModBuild 228 hardware "
            + "log shows three times in a row — every SetQualityLeve(Fantastic) is followed by a "
            + "[Perf] TEX reading of streamingMipmaps=True budget=900MB maxLevelReduction=2, and "
            + "every SetQualityLeve(Beautiful) by streamingMipmaps=False. In the same log EVERY "
            + "streamed texture in view read BELOW its desired mip level (47 of 47 in one window, 17 "
            + "of 21 in another), which is what 'matschige Texturen' looks like. Costs VRAM only, for "
            + "the same reason ForceFullTextureResolution does: mip 0 ships inside the texture either "
            + "way and a resident mip is not sampled more often, only from a different level. "
            + "Re-asserted per frame, because QualitySettings.SetQualityLevel re-loads the level "
            + "asset's own value on every quality swap. Turn OFF to hand the decision back to the "
            + "game — TextureStreamingBudgetMB below then raises its budget instead.");
        TextureStreamingBudgetMB = _file.Bind("RenderQuality", "TextureStreamingBudgetMB",
            Defaults.TextureStreamingBudgetMB, new ConfigDescription(
            "Minimum mipmap-streaming budget in MB, applied ONLY while ForceTextureStreamingOff "
            + "above is false AND the game currently has streaming on. It RAISES the game's own "
            + "budget to at least this value and never lowers it, so a game that already asks for "
            + "more keeps what it asked for. This is the softer half of the same fix: streaming "
            + "keeps a texture below its desired mip level while its budget is full, and the game's "
            + "authored budget at the 'Fantastic' level is 900 MB with maxLevelReduction=2 (measured, "
            + "ModBuild 228 log). Raising the budget and turning streaming off produce the SAME "
            + "picture when the budget is the only thing starving the mip chain; they differ only in "
            + "how much VRAM is held. Inert while the row above is on.",
            new AcceptableValueRange<int>(256, 16384)));
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
            "Maximum number of PER-PIXEL lights. 0 IS WHAT SHIPS SINCE 2026-08-23 (user ruling: the "
            + "row is 'zu gefährlich für normale Nutzer' as an everyday dial), -1 hands the decision "
            + "back to the game, which runs 4. THIS IS THE STRONGEST SINGLE PERFORMANCE LEVER "
            + "THE MOD HAS IN A DUNGEON, and it is expensive for TWO reasons stacked on one number. "
            + "First, in the built-in forward renderer a renderer touched by k per-pixel lights is "
            + "DRAWN min(k, cap) TIMES — one extra full pass, draw call, vertex transform and "
            + "fragment shading per extra light — and this scene submits up to 5,660 visible "
            + "renderers of 8,570. Second, a per-pixel light is the only kind that casts a SHADOW, "
            + "so going from 0 to 1 does not add one light: it switches the whole shadow pipeline "
            + "back on, at a 150-world-unit shadow distance that reaches the entire dungeon, with "
            + "each promoted point light re-rendering every caster in range six times (one per "
            + "cubemap face). That step is why even a cap of 1 or 2 costs a lot more than the "
            + "difference between 3 and 4. Measured, ModBuild 227 hardware log, bucketed by the cap "
            + "in force: main-thread render loop ~1.6 ms per 1000 visible renderers at cap 4 against "
            + "~1.2 ms at cap 0 (~8.5 ms vs ~5.7 ms at 5,200 visible) — a correlation across "
            + "different views, not a controlled A/B, and the GPU side cannot be read at all on this "
            + "runtime ('gpu n/a' on every [Perf] FRAME line). Lights beyond the cap still light the "
            + "scene, but per VERTEX. THE TRADE IS REAL AND VISIBLE: point-light falloff on walls "
            + "and floors gets flatter, and at 0 lights can visibly STEP between quality tiers on "
            + "some props — that is what [Lights] StabiliseAtZeroCap is for. The game exposes no "
            + "control for this, which is why the mod does. Re-asserted per frame like MsaaLevel, "
            + "because the game rewrites QualitySettings on every quality-level swap; how often it "
            + "actually does is now counted and reported in the log.",
            new AcceptableValueRange<int>(-1, 8)));

        // THE PRESET ROW (2026-08-23) IS RETIRED (2026-09-05, user ruling, verbatim: "Entferne die
        // Graphik-Profile wieder in den VR-Einstellungen, die mag ich nicht."). It is bound and
        // marked, not deleted, and the three reasons are worth writing down because "retire" and
        // "unbind" are not the same act:
        //   * The MARKER is what removes it from the menu. ConfigCatalog.Describe drops an entry
        //     whose bound ENGLISH description starts "LEGACY — no effect" (IsRetired), so this key
        //     leaves BOTH the curated Bild page and the Erweitert index in the same build. Deleting
        //     only the curated row would have left the dropdown one navigation level down, which is
        //     not what "entferne" says.
        //   * The KEY STAYS so an existing dev.gloomhavenvr.rig.cfg is not rewritten under a
        //     player, and the default and range are byte-for-byte the ones that shipped
        //     (Defaults.QualityPreset = 4, range 0..4). The 4 is a LITERAL now because
        //     CustomPresetIndex went with the preset table; it is the same number that expression
        //     evaluated to, and it is written here rather than inferred.
        //   * NOTHING WRITES OR READS IT any more. MirrorPresetToConfig is gone from Tick, and no
        //     code ever consulted the value to decide anything — it was a per-tick mirror of an
        //     index derived off the three dials, never a master. So retiring it moves no pixel.
        QualityPreset = _file.Bind("RenderQuality", "QualityPreset", Defaults.QualityPreset, new ConfigDescription(
            "LEGACY — no effect (the graphics presets were REMOVED 2026-09-05 by user ruling: "
            + "\"Entferne die Graphik-Profile wieder in den VR-Einstellungen, die mag ich nicht.\"). "
            + "This was a named point on the MSAA x resolution curve — 0 = Quality (MSAA 8x, eye "
            + "1.00x), 1 = Balanced (4x, 0.90x), 2 = Performance (2x, 0.80x), 3 = Minimum (MSAA "
            + "off, 0.60x), 4 = custom — mirrored here from the three dials once per tick and never "
            + "read back. The preset table and every apply path are deleted, so this value is inert "
            + "and no longer tracks anything; the key is kept only so a tuned file is not rewritten. "
            + "THE THREE DIALS IT USED TO WRITE ARE THE AUTHORITY AND ARE UNCHANGED: "
            + "[RenderQuality] MsaaLevel, EyeResolutionScale and ForceAnisotropic are curated rows "
            + "on Bild, and [RenderQuality] PixelLightCount is on Erweitert with its shipped "
            + "default of 0 (2026-08-23 ruling). Set them yourself; nothing else will.",
            new AcceptableValueRange<int>(0, 4)));

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

        // LIGHT STABILISER ([Lights] StabiliseAtZeroCap / PinnedPixelLights / FlickerDamping) rides
        // this file for the fifth time, and for the most direct reason of the five: it is a property
        // OF the per-pixel light cap bound above. It exists only to make that row's 0 usable, it is
        // inert at every other value, and a player reading the cap's description is exactly the
        // player who needs to find it. Runtime stays in Rig/LightStabiliser.cs.
        LightStabiliser.BindConfig(_file);
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
        ApplyTextureLimit();
        // IMMEDIATELY after the mip-drop force, because the two are one subject: masterTextureLimit
        // throws the top mips away outright, streaming keeps them out of memory until a budget
        // catches up, and a reader chasing "matschige Texturen" has to be able to rule both out in
        // the same breath. Same per-frame re-assert for the same reason (quality-level swaps).
        ApplyTextureStreaming();
        ApplyPixelLights();
        // IMMEDIATELY after the cap is asserted, and nested here rather than added as a seventh
        // VRRigDriver tail step on purpose: the stabiliser is a FUNCTION of the value the line above
        // just wrote, and that tail array is locked by name in .planning/refactor/FRAME-ORDER.lock.
        // Nesting expresses the real dependency and leaves the locked order alone.
        LightStabiliser.Tick(PixelLightCount!.Value);
        // MirrorPresetToConfig() WAS CALLED HERE, every frame, to copy the derived preset index
        // into [RenderQuality] QualityPreset. It is gone with the preset offering (user ruling
        // 2026-09-05) and had to be: a key marked "LEGACY — no effect" that this file kept
        // rewriting on every tick would be a lie in the cfg file and a write nothing reads.
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
    /// main-thread render loop from 14.9 ms to 1.8 ms.</para>
    ///
    /// <para>AND THE CONCLUSION DRAWN FROM THAT IS RETRACTED (2026-08-23). This block used to close
    /// with "it measured ~1 % even back when the main thread WAS the bottleneck, so it is now a pure
    /// quality trade with no performance case behind it … not a lever anyone should be pointed at."
    /// Two things kill that. FIRST, THE USER'S OWN REPORT, verbatim: <i>"Die Performance scheint
    /// erheblich von den Pixellichtern abzuhängen. Bereits wenige verschlechtern die Performance
    /// enorm."</i> — the strongest single-lever effect he has reported on this project. SECOND, THE
    /// ~1 % ITSELF IS NOT EVIDENCE: it was measured in the rate-locked 45 Hz era, and FINDINGS.md
    /// §3 records in its own words that "changed nothing" was the EXPECTED reading there for any
    /// improvement that did not cross the budget line. Every entry in that table shares the defect;
    /// this one happens to be the entry a player has since disproved from the headset.</para>
    ///
    /// <para>WHY IT IS EXPENSIVE — two multiplications on one dial, which is the answer to "warum
    /// so hungrig". (1) In the built-in forward path a renderer touched by k per-pixel lights is
    /// drawn <c>min(k, cap)</c> times, each pass a separate draw call with its own vertex transform
    /// and fragment shading; this scene submits up to 5,660 visible renderers of 8,570, so the cap
    /// multiplies a five-digit number. (2) A per-pixel light is the ONLY kind that renders a shadow
    /// map, so the step from 0 to 1 does not add "one light" — it turns the whole shadow pipeline
    /// back on, at a 150-world-unit shadow distance that reaches the entire dungeon, with a point
    /// light costing six cube faces. That step function is why "bereits wenige" is already enough.
    /// <see cref="LogLightCensus"/> prints both, with the scene's own counts, at every change.</para>
    ///
    /// <para><b>THE SHIPPED VALUE IS 0 SINCE 2026-08-23</b> (user ruling, verbatim: <i>"Die
    /// Pixellichter option ist zu gefährlich für normale Nutzer, sie sollte in Erweitert
    /// verschwinden und per default auch in allen Graphik-Voreinstellungen auf 0 geschaltet
    /// sein."</i>). That makes this an ACTIVE cap for every player rather than the passive -1 it was:
    /// the cost is paid up front and the look is what changes. Lights above the cap still light the
    /// scene per VERTEX, so nothing goes dark — but the falloff on walls and floors flattens, and the
    /// ModBuild 227 census counts 44 enabled lights in this dungeon (32 point, 12 spot; the "16 point
    /// lights" this doc used to quote was one room). The other cost of 0 is the flicker the user
    /// reported at that setting, which is what <see cref="LightStabiliser"/> exists for and why it is
    /// on by default.</para>
    ///
    /// <para>-1 still means "hands off" and still RESTORES, which is now the escape hatch rather than
    /// the default; the row itself moved off the curated Bild page to Erweitert ▸ Bild &amp;
    /// Darstellung with the same ruling.</para>
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
        {
            ReportSilentCorrections();
            return;
        }

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
        else
        {
            // SAME wanted value, DIFFERENT live value = somebody else wrote it. This branch used to
            // be silent, which is why "does the game reset his 0 to 4?" was unanswerable from a
            // 12 MB hardware log. See _silentCorrections for the writers and for what the number
            // means.
            _silentCorrections++;
            _lastSilentFoundValue = current;
        }
        ReportSilentCorrections();
    }

    /// <summary>
    /// Print the silent-correction tally at most every <see cref="SilentReportSeconds"/> s, and only
    /// when it moved. Zero output while the game leaves the value alone — which is itself the
    /// answer, and is why the first report also states what silence means.
    /// </summary>
    private static void ReportSilentCorrections()
    {
        if (Time.unscaledTime < _nextSilentReportTime)
            return;
        _nextSilentReportTime = Time.unscaledTime + SilentReportSeconds;
        if (_silentCorrections == _silentCorrectionsReported)
            return;

        int delta = _silentCorrections - _silentCorrectionsReported;
        _silentCorrectionsReported = _silentCorrections;
        VRLog.Info("Rig", $"Per-pixel light cap: the game overwrote it {delta} time(s) in the last "
                          + $"{SilentReportSeconds:F0}s ({_silentCorrections} this session; last "
                          + $"value found was {_lastSilentFoundValue}, wanted {PixelLightCount!.Value}). "
                          + "EACH ONE WAS CORRECTED IN THE SAME FRAME IT HAPPENED: this step runs in "
                          + "the rig's Update tail, before any camera renders, and the game's three "
                          + "writers (GraphicProfile.Setup, GraphicSettings.SetPixelLight and "
                          + "SetQualityLevel → QualitySettings.SetQualityLevel) all run in Update "
                          + "order or at scene init. So no frame was DRAWN at the wrong cap and the "
                          + "cadence is adequate — but a count that climbs in EVERY window would mean "
                          + "a per-frame writer we have not found, and then it would not be.");
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
        int candidates = 0, forcedVertex = 0, baked = 0, forcedPixel = 0;
        int shadowCasters = 0, forcedPixelShadowCasters = 0;
        for (int i = 0; i < lights.Length; i++)
        {
            Light l = lights[i];
            bool isBaked = l.bakingOutput.lightmapBakeType == LightmapBakeType.Baked;
            if (isBaked)
                baked++;
            else if (l.renderMode == LightRenderMode.ForceVertex)
                forcedVertex++;
            else
            {
                candidates++;
                if (l.renderMode == LightRenderMode.ForcePixel)
                    forcedPixel++;
            }

            if (!isBaked && l.shadows != LightShadows.None)
            {
                shadowCasters++;
                if (l.renderMode == LightRenderMode.ForcePixel)
                    forcedPixelShadowCasters++;
            }
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

        // WHY THE DIAL IS SO EXPENSIVE — the user's actual question ("Warum sind die so
        // Performancehungrig?"), answered with the two multiplications this cap sits on top of
        // rather than with the single one the line above describes. It is a SEPARATE line because it
        // is an explanation, not a census, and because the first one was already the longest line in
        // the log.
        VRLog.Info("Rig", $"Per-pixel light cap — WHY IT IS EXPENSIVE, at cap {cap}. TWO "
                          + "multiplications sit on this one dial, which is why 'bereits wenige' "
                          + "already hurt. (1) DRAW MULTIPLICATION: in the built-in FORWARD path a "
                          + "renderer touched by k per-pixel lights is drawn min(k, cap) times — a "
                          + "ForwardBase pass plus one full ForwardAdd pass per extra light, each "
                          + "with its own draw call, vertex transform and fragment shading. This "
                          + "scene submits up to 5,660 visible renderers of 8,570, so the cap is a "
                          + "multiplier on a five-digit number, not an additive cost. (2) SHADOW "
                          + $"MULTIPLICATION, and this is the step that makes 1 already expensive: a "
                          + $"per-pixel light is the ONLY kind that renders a shadow map, and this "
                          + $"scene has {shadowCasters} shadow-casting light(s) at a shadow distance "
                          + $"of {QualitySettings.shadowDistance:F0} world units, which reaches the "
                          + "whole dungeon. Going 0 → 1 therefore does not add 'one light', it turns "
                          + "the entire shadow pipeline back on: every promoted caster re-submits "
                          + "every caster in range (a point light does it SIX times, once per "
                          + "cubemap face). AT CAP 0 both multiplications are exactly 1 and no "
                          + "shadow map is rendered at all"
                          + (forcedPixel > 0
                              ? $" — EXCEPT for {forcedPixel} light(s) authored ForcePixel, which "
                                + "Unity promotes per-pixel BEFORE it consults the cap, "
                                + $"{forcedPixelShadowCasters} of them still casting shadows. Cap 0 "
                                + "is not the zero it looks like in this scene."
                              : "; no light here is authored ForcePixel, so cap 0 really is zero.")
                          + " MEASURED, from the ModBuild 227 hardware log, and honestly: bucketing "
                          + "every [Perf] SPLIT ZOOM sample by the cap in force gives a main-thread "
                          + "render loop of ~1.6 ms per 1000 visible renderers at cap 4 against "
                          + "~1.2 ms at cap 0 — about 8.5 ms vs 5.7 ms at the ~5,200 visible "
                          + "renderers of a zoomed-out view. THAT COMPARISON IS NOT A CONTROLLED "
                          + "EXPERIMENT: the cap-4 windows are not the same views as the cap-0 ones, "
                          + "and the GPU-side cost (which is where most of a forward light's bill "
                          + "lands) cannot be read at all on this runtime — every [Perf] FRAME line "
                          + "says 'gpu n/a'. The 2026-07 note that 'removing all of it moved the head "
                          + "camera by nothing' is NOT evidence against any of this: that whole round "
                          + "was measured while the app was rate-locked at 45 Hz, where "
                          + ".planning/perf/FINDINGS.md records that 'changed nothing' was the "
                          + "expected reading for ANY improvement that did not cross the budget line.");
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

    /// <summary>
    /// FORCE UNITY'S GLOBAL TEXTURE LIMIT BACK TO 0 — the mip levels the game throws away.
    ///
    /// <para><b>USER REPORT (2026-08-23, verbatim):</b> "Wenn man in VR nah ran geht sind die
    /// Texturen doch manchmal matschig … wäre es eine Option mit super resolution die texturen im
    /// Spiel zu erhöhen?" TWO INDEPENDENT INVESTIGATIONS IN THE SAME ROUND ARRIVED HERE from
    /// different directions — the texture census and the LOD lane — which is why it ships before
    /// the measurement that will confirm it.</para>
    ///
    /// <para><c>QualitySettings.masterTextureLimit</c> is a MIP-DROP COUNT, not a quality score: at
    /// 1 every mipped texture in the game renders at half its authored resolution per side, at 2 a
    /// quarter, at 3 an eighth. <b>Nothing in this mod has ever read it</b> — a grep over
    /// <c>src/</c> returned zero hits before this build — and the game writes it from three
    /// directions: <c>GraphicProfile.Setup</c> (:120) assigns it from a value persisted in the SAVE
    /// FILE, <c>GraphicSettings.SetupQualityLevel</c> re-applies that profile at every boot, and
    /// <c>QualitySettings.SetQualityLevel</c> re-loads it from the level asset on every quality
    /// swap — which this session performs (boot 'Fastest', in-scenario 'Fantastic').</para>
    ///
    /// <para><b>AND ITS DESERIALISATION FALLBACK IS THE WORST VALUE.</b>
    /// <c>GraphicProfile.cs:71</c> falls back to <c>EIGHTHEN</c> — one eighth — for any persisted
    /// value it does not recognise. A profile written by an older build, or by a different
    /// platform's enum ordering, silently renders the entire game at 1/8 texture resolution with no
    /// UI anywhere reporting it.</para>
    ///
    /// <para>WHY THIS SHIPS AHEAD OF ITS OWN MEASUREMENT, against this project's usual discipline:
    /// the remedy is <b>unconditionally non-worsening</b>. Forcing 0 when the value is already 0 is
    /// a no-op; forcing it when the value is higher restores resolution the assets already carry.
    /// There is no setting of this field at which 0 looks worse. The only cost is VRAM — mip 0 of
    /// every texture, which the shipped assets contain either way — and this runs on a 24 GB card.
    /// That is a different situation from a fix whose correctness depends on a diagnosis, which is
    /// what the "falsify before you fix" rule is about. <see cref="Core.PerfTextureCensus"/> prints
    /// the value it replaced, so the next log still says whether it mattered.</para>
    ///
    /// <para>PER-FRAME LIKE ITS NEIGHBOURS, for their reason: the game rewrites
    /// <c>QualitySettings</c> on every quality-level swap, so a one-shot write at rig build would be
    /// silently undone. Two field reads in the steady state.</para>
    /// </summary>
    private static void ApplyTextureLimit()
    {
        if (ForceFullTextureResolution!.Value)
        {
            if (QualitySettings.masterTextureLimit == 0)
                return;
            if (!_textureLimitForced)
                _textureLimitOriginal = QualitySettings.masterTextureLimit; // restore point
            int was = QualitySettings.masterTextureLimit;
            QualitySettings.masterTextureLimit = 0;
            _textureLimitForced = true;
            VRLog.Info("Rig", $"TEXTURE LIMIT forced to 0 (was {was} — that is a MIP-DROP COUNT, so "
                              + $"every mipped texture was rendering at 1/{1 << was} of its authored "
                              + "resolution PER SIDE). The game sets this from the graphics profile "
                              + "in the save file and re-loads it on every quality-level swap, which "
                              + "is why this is re-asserted per frame rather than once. Costs VRAM "
                              + "only — mip 0 ships inside the texture either way — and no frame "
                              + "time: a larger mip is not sampled more, it is sampled from a "
                              + "different level. If this line reads 'was 0' the game was already "
                              + "at full resolution and the softness is elsewhere; see [Perf] TEX.");
        }
        else if (_textureLimitForced)
        {
            QualitySettings.masterTextureLimit = _textureLimitOriginal;
            _textureLimitForced = false;
            VRLog.Info("Rig", $"TEXTURE LIMIT force released — restored the game's own value "
                              + $"{_textureLimitOriginal}.");
        }
    }

    /// <summary>Whether <see cref="ApplyTextureLimit"/> currently holds the limit down, and the
    /// value it took over from. Restored on release so the flat game is left as it was found.</summary>
    private static bool _textureLimitForced;

    /// <inheritdoc cref="_textureLimitForced"/>
    private static int _textureLimitOriginal;

    /// <summary>
    /// TURN UNITY'S MIPMAP STREAMING OFF — the second way the game keeps a texture below the
    /// resolution its own file carries, and the one the higher quality preset switches ON.
    ///
    /// <para><b>THE REPORT (user, 2026-08-23, verbatim):</b> "Die matschigen Texturen verschwinden,
    /// wenn ich in den Spiel-Grafik-Einstellungen 'Schön' statt 'Fantastisch' einstelle." The HIGHER
    /// preset looked softer, which is the shape of a setting that trades resolution for memory
    /// rather than of a setting that buys quality.</para>
    ///
    /// <para><b>WHY THIS IS THE MECHANISM, read out of his ModBuild 228 Player.log rather than
    /// inferred.</b> The game's own <c>SetQualityLeve(...)</c> line and this mod's <c>[Perf] TEX</c>
    /// reading of <see cref="QualitySettings.streamingMipmapsActive"/> correlate A-B-A-B across
    /// three transitions, all in the same direction: line 1649 <c>Fantastic</c> → line 4629
    /// <c>streamingMipmaps=True budget=900MB maxLevelReduction=2</c>; line 10636 <c>Beautiful</c> →
    /// line 10873 <c>False</c>; line 11451 <c>Fantastic</c> → line 11499 <c>True budget=900MB</c>;
    /// line 12008 <c>Beautiful</c> → line 12279 <c>False</c>, and False to the end of the session.
    /// The mod writes this field nowhere (before this build only <see cref="Core.PerfTextureCensus"/>
    /// and <c>Core/PerfSceneProfile</c> READ it), and the game only ever calls
    /// <c>QualitySettings.SetQualityLevel(...)</c> (decompiled
    /// <c>GH.Runtime/Gloomhaven/GraphicSettings.cs:350</c>, <c>GH.Runtime/PlatformLayer.cs:243</c>),
    /// which applies the QUALITY ASSET's per-level fields — so streaming-on-at-Fantastic is authored
    /// into the game's own quality levels and nothing else has to be looked for.</para>
    ///
    /// <para><b>AND THE STREAMING SYSTEM WAS NOT MERELY TIGHT, IT WAS NOT DELIVERING.</b> In the same
    /// log the <c>[Perf] TEX</c> line read "47 of them are streamed, 47 currently BELOW their desired
    /// mip level" in one window and "21 … 17 below" in another. EVERY (or nearly every) streamed
    /// texture in view was behind. The other two texture dials were already forced by this file and
    /// read clean throughout — <c>masterTextureLimit</c> 0 (FULL) and anisotropy ForceEnable — so
    /// streaming is the only remaining global texture dial that differs between the two presets.</para>
    ///
    /// <para><b>WHAT IT COSTS: VRAM, and nothing else</b> — the same argument as
    /// <see cref="ApplyTextureLimit"/>. Mip 0 ships inside the texture file either way, and a
    /// resident mip is not sampled more often, only from a different level. What streaming buys is a
    /// smaller resident set, which matters on a memory-tight card and not on the 24 GB one this is
    /// reported from. NOT VERIFIED ON HARDWARE: that turning it off REMOVES the reported softness.
    /// What is verified is the correlation above and the "all streamed textures behind" reading; the
    /// [Perf] TEX line names which of the three states it is in on the next run, so a build in which
    /// this changed nothing still says so.</para>
    ///
    /// <para>PER-FRAME LIKE ITS NEIGHBOURS, for their reason: <c>SetQualityLevel</c> re-loads the
    /// level asset's own value on every quality swap, and the log above is a record of the game doing
    /// exactly that four times in one session. Two field reads in the steady state.</para>
    ///
    /// <para>THE OTHER HALF OF THE ROW: with the force OFF the decision goes back to the game, and
    /// <c>[RenderQuality] TextureStreamingBudgetMB</c> then RAISES its budget (never lowers it) while
    /// streaming is on. That is the softer remedy for the same starvation, and it is offered because
    /// the two are only equivalent when the budget is the whole reason the mip chain is behind.</para>
    /// </summary>
    private static void ApplyTextureStreaming()
    {
        if (ForceTextureStreamingOff!.Value)
        {
            // Hand the budget back BEFORE switching the system off, so a session that walked
            // budget-raise → force-off does not leave the game holding a number we invented.
            if (_streamingBudgetForced)
                ReleaseStreamingBudget("the force-off row took over");

            if (!QualitySettings.streamingMipmapsActive)
                return;

            float wasBudget = QualitySettings.streamingMipmapsMemoryBudget;
            int wasReduction = QualitySettings.streamingMipmapsMaxLevelReduction;
            if (!_streamingForced)
            {
                _streamingOriginalActive = true;                 // restore point: it WAS on
                _streamingOriginalBudget = wasBudget;
            }
            QualitySettings.streamingMipmapsActive = false;
            _streamingForced = true;

            // One line per DISTINCT finding. The game re-enables streaming on every swap back to
            // 'Fantastic', and re-printing the identical sentence each time would bury the one
            // reading that matters (the budget and reduction it was running at).
            if (Mathf.Abs(wasBudget - _lastLoggedStreamingBudget) < 0.5f
                && wasReduction == _lastLoggedStreamingReduction)
                return;
            _lastLoggedStreamingBudget = wasBudget;
            _lastLoggedStreamingReduction = wasReduction;
            VRLog.Info("Rig", $"TEXTURE STREAMING forced OFF (was ON at a {wasBudget:F0}MB budget "
                              + $"with maxLevelReduction={wasReduction}). Unity's mipmap streaming "
                              + "holds a texture BELOW its authored mip level until its budget "
                              + "catches up, and the [Perf] TEX census read every streamed texture "
                              + "in view as below its desired level — which is exactly what "
                              + "\"matschige Texturen\" looks like. The game turns this on at the "
                              + "'Fantastic' quality level and off at 'Beautiful' (three A-B-A-B "
                              + "transitions in the ModBuild 228 log, each SetQualityLeve line "
                              + "followed by the matching streamingMipmaps reading), which is why "
                              + "the HIGHER preset looked softer. Costs VRAM only: mip 0 ships "
                              + "inside the texture either way and a resident mip is not sampled "
                              + "more often, only from a different level. If this line reads 'was "
                              + "OFF' the game was already resident-full and the softness is "
                              + "elsewhere; see [Perf] TEX.");
            return;
        }

        if (_streamingForced)
        {
            QualitySettings.streamingMipmapsActive = _streamingOriginalActive;
            if (_streamingOriginalBudget > 0f)
                QualitySettings.streamingMipmapsMemoryBudget = _streamingOriginalBudget;
            _streamingForced = false;
            _lastLoggedStreamingBudget = -1f;
            _lastLoggedStreamingReduction = int.MinValue;
            VRLog.Info("Rig", $"TEXTURE STREAMING force released — restored the game's own state "
                              + $"(streaming {(_streamingOriginalActive ? "ON" : "off")} at "
                              + $"{_streamingOriginalBudget:F0}MB). Note the game rewrites both on "
                              + "every quality-level swap, so what it ends up at is whatever the "
                              + "quality level last set.");
            return;
        }

        // ---- the softer half: raise the budget the game is running on, never lower it ----------
        if (!QualitySettings.streamingMipmapsActive)
        {
            // Streaming is off and we did not turn it off — nothing to raise, and no state of ours
            // to hold. (Releasing here would fight nothing: the raise only ever runs while on.)
            return;
        }

        float wanted = Mathf.Clamp(TextureStreamingBudgetMB!.Value, 256, 16384);
        float current = QualitySettings.streamingMipmapsMemoryBudget;
        if (current >= wanted - 0.5f)
            return;

        if (!_streamingBudgetForced)
        {
            _streamingBudgetOriginal = current;                  // restore point
            _streamingBudgetForced = true;
        }
        QualitySettings.streamingMipmapsMemoryBudget = wanted;

        if (Mathf.Abs(wanted - _lastLoggedBudgetTarget) < 0.5f)
            return;
        _lastLoggedBudgetTarget = wanted;
        VRLog.Info("Rig", $"TEXTURE STREAMING budget raised {current:F0}MB → {wanted:F0}MB "
                          + $"(maxLevelReduction={QualitySettings.streamingMipmapsMaxLevelReduction}, "
                          + "left as the game authored it). This is the SOFT half of the "
                          + "mushy-texture fix and it only runs because [RenderQuality] "
                          + "ForceTextureStreamingOff is false: streaming keeps a texture below its "
                          + "desired mip while the budget is full, so a bigger budget and no "
                          + "streaming at all give the same picture WHEN the budget is the only "
                          + "thing starving the chain. Watch the [Perf] TEX line's 'N streamed, M "
                          + "below desired' figures — if M stays equal to N after this, the budget "
                          + "was not the constraint and the force-off row is the answer.");
    }

    /// <summary>Give the streaming budget back to the game (see <see cref="ApplyTextureStreaming"/>).</summary>
    private static void ReleaseStreamingBudget(string why)
    {
        if (_streamingBudgetOriginal > 0f)
            QualitySettings.streamingMipmapsMemoryBudget = _streamingBudgetOriginal;
        _streamingBudgetForced = false;
        _lastLoggedBudgetTarget = -1f;
        VRLog.Info("Rig", $"TEXTURE STREAMING budget released → {_streamingBudgetOriginal:F0}MB ({why}).");
    }

    /// <summary>
    /// Whether <see cref="ApplyTextureStreaming"/> is currently holding mipmap streaming off, plus
    /// the state it took over from. READ BY <see cref="Core.PerfTextureCensus"/>: a
    /// <c>streamingMipmaps=False</c> reading means two completely different things depending on this
    /// flag — the game's own choice, or this mod's — and a census line that cannot tell them apart
    /// reads like the game's setting and sends the next reader looking in the wrong place.
    /// </summary>
    internal static bool TextureStreamingForcedOff => _streamingForced;

    /// <summary>
    /// Whether the force-off ROW is on, regardless of whether it has had to act yet. The pair
    /// (<see cref="TextureStreamingForcedOff"/> false, this true, streaming reading ON) is the
    /// interesting third state: the game re-enabled streaming since our last assert, i.e. within the
    /// current frame's tick order, and the census is looking at a value about to be corrected.
    /// </summary>
    internal static bool TextureStreamingForceRequested =>
        ForceTextureStreamingOff != null && ForceTextureStreamingOff.Value;

    /// <inheritdoc cref="ApplyTextureStreaming"/>
    private static bool _streamingForced;

    /// <inheritdoc cref="ApplyTextureStreaming"/>
    private static bool _streamingOriginalActive;

    /// <inheritdoc cref="ApplyTextureStreaming"/>
    private static float _streamingOriginalBudget = -1f;

    /// <summary>Last (budget, reduction) pair announced by the force-off branch — see its log gate.</summary>
    private static float _lastLoggedStreamingBudget = -1f;

    /// <inheritdoc cref="_lastLoggedStreamingBudget"/>
    private static int _lastLoggedStreamingReduction = int.MinValue;

    /// <summary>Whether the budget row is currently holding a raised budget, and what it replaced.</summary>
    private static bool _streamingBudgetForced;

    /// <inheritdoc cref="_streamingBudgetForced"/>
    private static float _streamingBudgetOriginal = -1f;

    /// <inheritdoc cref="_streamingBudgetForced"/>
    private static float _lastLoggedBudgetTarget = -1f;

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
    /// presses never drift off the 0.1 grid the shipped values sit on). Applies live: Tick re-asserts
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

    // ---- the graphics-preset accessors STOOD HERE and are DELETED (user ruling 2026-09-05) ----
    //
    // Six members: CurrentPresetIndex (which preset the three dials spell), PresetRowIndex (the
    // dropdown's derived index), ApplyPresetByIndex (write all three dials), MirrorPresetToConfig
    // (copy the derived index into the cfg key, once per tick), PresetLabel and CyclePreset.
    //
    // "Entferne die Graphik-Profile wieder in den VR-Einstellungen, die mag ich nicht." The
    // argument for deleting rather than merely un-offering them is written out at the tombstone
    // that replaced the Presets table near the top of this file; the short form is that
    // ApplyPresetByIndex was the only writer in the mod that could move a hand-set MSAA or
    // eye-resolution value, its only live caller was the dropdown, and CyclePreset had had no
    // caller at all since the dropdown replaced it in 2026-08.
    //
    // The three rows they used to drive are unchanged and are now the only authority:
    // StepEyeScale/StepMsaa above are the player's own edits, Tick asserts them, and the
    // [Rig] EYE-TARGET DIAG line says which lever actually bound.
}
